using System.Linq;
using Crucible.Core.Combat;
using Crucible.Core.Game;
using Crucible.Core.Hex;
using Crucible.Core.World;
using Xunit;

namespace Crucible.Core.Tests
{
    /// <summary>Runs AI-vs-AI battles on generated terrain (cliffs, rivers, forests) to shake out rule bugs.</summary>
    public class SimulationStressTests
    {
        [Theory]
        [InlineData(1UL)]
        [InlineData(7UL)]
        [InlineData(2024UL)]
        [InlineData(31337UL)]
        public void Battles_on_generated_maps_always_terminate(ulong seed)
        {
            var map = MapGenerator.Generate(new MapGeneratorSettings { Seed = seed, Width = 40, Height = 28 });
            var g = new GameState(TestWorld.Content, map, seed);
            g.AddPlayer("A", Content.DefaultContent.Aurel, true);
            g.AddPlayer("B", Content.DefaultContent.Khaganate, true);

            int fought = 0;
            foreach (var tile in map.Tiles.Where(t => t.IsPassableForLand))
            {
                if (fought >= 5) break;
                var a = tile.Coord;
                var d = a.Neighbors().FirstOrDefault(n => map.Get(n) is Tile t && t.IsPassableForLand &&
                    TerrainRules.LandStepCost(tile, t) != TerrainRules.Impassable &&
                    g.ArmyAt(n) == null && g.BattleCovering(n) == null);
                if (d == default || g.ArmyAt(a) != null || g.BattleCovering(a) != null) continue;
                if (a.Range(Battlefield.FieldRadius + 2).Any(h => g.ArmyAt(h) != null)) continue;

                var attacker = g.CreateArmy(0, a, "swordsman", "archer", "horseman", "catapult", "spearman");
                g.CreateArmy(1, d, "spearman", "composite_bowman", "sky_rider", "warrior");
                var battle = g.Attack(attacker, d);
                Assert.NotNull(battle);

                for (int round = 0; round < Battle.MaxRounds && !battle.IsFinished; round++)
                {
                    if (battle.Status == BattleStatus.AwaitingNextRound) battle.BeginNextRound();
                    g.AdvanceAIBattleTurns(battle);
                }
                Assert.True(battle.IsFinished, string.Join("\n", battle.Log));
                Assert.Empty(g.Battles);
                Assert.All(g.Armies, army => Assert.False(army.InBattle));
                fought++;
            }
            Assert.True(fought > 0);
        }

        [Theory]
        [InlineData(3UL)]
        [InlineData(2024UL)]
        public void Long_ai_game_keeps_the_economy_sane(ulong seed)
        {
            var map = MapGenerator.Generate(new MapGeneratorSettings { Seed = seed, Width = 40, Height = 28 });
            var g = new GameState(TestWorld.Content, map, seed);
            var a = g.AddPlayer("A", Content.DefaultContent.Aurel, true);
            var b = g.AddPlayer("B", Content.DefaultContent.Khaganate, true);
            var land = map.Tiles.Where(t => t.IsPassableForLand && t.Elevation <= 2).Select(t => t.Coord).ToList();
            g.FoundCity(a.Id, land.First(), "A", true);
            g.FoundCity(b.Id, land.OrderByDescending(c => c.DistanceTo(land.First())).First(), "B", true);

            var turns = new TurnManager(g);
            turns.Start();
            for (int i = 0; i < 150 && !turns.IsGameOver; i++) turns.EndTurn();

            foreach (var p in g.Players)
            {
                var city = g.Cities.Single(c => c.OwnerId == p.Id);
                Assert.True(city.Population >= 3, $"{p.Name} pop {city.Population}");
                Assert.True(city.Buildings.Count >= 2, $"{p.Name} buildings {city.Buildings.Count}");
                Assert.True(p.Tech.Researched.Count >= 5, $"{p.Name} techs {p.Tech.Researched.Count}");
                Assert.True(Economy.EconomyRules.EmpireIncome(g, p).Gold >= 0, "the governor should not go bankrupt");
                Assert.True(g.Map.Tiles.Count(t => t.OwnerCityId == city.Id) > 7, "borders should have grown");
                Assert.True(g.Armies.Any(ar => ar.OwnerId == p.Id), "the governor should raise a garrison");
            }
        }
    }
}
