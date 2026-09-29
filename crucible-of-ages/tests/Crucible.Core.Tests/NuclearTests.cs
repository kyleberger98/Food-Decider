using System.Linq;
using Crucible.Core.AI;
using Crucible.Core.Content;
using Crucible.Core.Economy;
using Crucible.Core.Empire;
using Crucible.Core.Game;
using Crucible.Core.World;
using Xunit;

namespace Crucible.Core.Tests
{
    /// <summary>Uranium and nuclear weapons.</summary>
    public class NuclearTests
    {
        static void Grant(Player p, string tech)
        {
            foreach (var id in p.Tech.PathTo(tech)) p.Tech.Grant(id);
        }

        /// <summary>Player 0's base at (3,6) with a connected uranium mine, and player 1's city at (11,6).</summary>
        static (GameState g, City home, City enemy) World(int enemyPop = 8)
        {
            var g = TestWorld.Game(TestWorld.FlatMap(24, 14), attackerAI: false);
            var home = g.FoundCity(0, TestWorld.H(3, 6), "Home", true);
            var enemy = g.FoundCity(1, TestWorld.H(11, 6), "Target", true);
            enemy.Population = enemyPop;
            var mine = g.Map.Get(TestWorld.H(4, 6));
            mine.Resource = ResourceType.Uranium;
            mine.Improvement = ImprovementType.Mine;
            return (g, home, enemy);
        }

        [Fact]
        public void Uranium_is_a_strategic_resource_revealed_by_atomic_theory()
        {
            var (g, home, _) = World();
            var p = g.Player(0);
            var tile = g.Map.Get(TestWorld.H(3, 7));
            tile.Resource = ResourceType.Uranium;
            Assert.Equal(ResourceKind.Strategic, Improvements.KindOf(ResourceType.Uranium));
            Assert.Equal(ImprovementType.Mine, Improvements.ImprovementFor(ResourceType.Uranium));

            Grant(p, "mining");
            Assert.False(Improvements.Knows(p, ResourceType.Uranium));
            Assert.False(Improvements.CanBuild(p, tile, ImprovementType.Mine)); // flat land: only the uranium would allow a mine
            Grant(p, "atomic_theory");
            Assert.True(Improvements.Knows(p, ResourceType.Uranium));
            Assert.True(Improvements.CanBuild(p, tile, ImprovementType.Mine));
            Assert.Equal(ImprovementType.Mine, Improvements.Best(p, tile));

            Assert.Equal(Improvements.StrategicPerSource, Improvements.StrategicAvailable(g, 0, ResourceType.Uranium)); // the mine at (4,6)
            Assert.Empty(home.AirUnits);

            // Maps have some.
            var map = MapGenerator.Generate(new MapGeneratorSettings { Seed = 7, Width = 48, Height = 32 });
            Assert.Contains(map.Tiles, t => t.Resource == ResourceType.Uranium);
        }

        [Fact]
        public void Nuclear_weapons_need_their_tech_and_uranium()
        {
            var (g, home, _) = World();
            var p = g.Player(0);
            var bomb = ProductionItem.Unit("atomic_bomb");
            Assert.False(EconomyRules.CanBuild(g, home, bomb));
            Grant(p, "nuclear_fission");
            Assert.Contains("atomic_theory", g.Content.Tech("nuclear_fission").Prerequisites);
            Assert.True(EconomyRules.CanBuild(g, home, bomb));
            Assert.False(EconomyRules.CanBuild(g, home, ProductionItem.Unit("nuclear_missile")));

            g.Map.Get(TestWorld.H(4, 6)).Improvement = ImprovementType.None; // no uranium supply
            Assert.False(EconomyRules.CanBuild(g, home, bomb));
        }

        [Fact]
        public void An_atomic_bomb_devastates_its_target_and_leaves_fallout()
        {
            var (g, home, enemy) = World();
            var third = g.AddPlayer("Onlooker", DefaultContent.Khaganate, true);
            int opinionBefore = g.Opinion(third, g.Player(0));
            var bomb = g.SpawnUnit(home, "atomic_bomb");
            Assert.Contains(bomb, home.AirUnits);

            var garrison = g.CreateArmy(1, enemy.Position, "infantry", "infantry");
            var near = g.CreateArmy(1, TestWorld.H(12, 6), "infantry");
            var far = g.CreateArmy(1, TestWorld.H(14, 6), "infantry");
            var farm = g.Map.Get(TestWorld.H(10, 6));
            farm.Improvement = ImprovementType.Farm;
            NuclearStrike seen = null;
            g.NuclearStrikeLaunched += s => seen = s;

            var strike = g.LaunchNuke(home, bomb, enemy.Position);
            Assert.NotNull(strike);
            Assert.Same(strike, seen);
            Assert.DoesNotContain(bomb, home.AirUnits);                         // spent
            Assert.Null(g.Army(garrison.Id));                                   // ground zero
            Assert.True(near.Units.Single().Hp < 70 && near.Units.Single().IsAlive);
            Assert.Equal(100, far.Units.Single().Hp);                           // outside the blast
            Assert.Equal(4, enemy.Population);                                  // half the city
            Assert.Equal(2, strike.UnitsDestroyed);
            Assert.Equal(ImprovementType.None, farm.Improvement);
            Assert.Equal(GameState.FalloutTurns, farm.Fallout);
            Assert.Equal(0, g.Map.Get(enemy.Position).Fallout);                 // city centres stay habitable
            Assert.Equal(0, EconomyRules.TileYields(farm, g.Map).Food);
            Assert.True(g.Opinion(third, g.Player(0)) <= opinionBefore + Diplomacy.NuclearPenalty);

            // Fallout clears after ten world turns.
            var turns = new TurnManager(g, new StrategicAI());
            for (int i = 0; i < GameState.FalloutTurns && !turns.IsGameOver; i++) turns.EndTurn();
            Assert.Equal(0, farm.Fallout);
            Assert.True(EconomyRules.TileYields(farm, g.Map).Food > 0);
        }

        [Fact]
        public void A_nuclear_missile_reaches_further_and_its_blast_is_wider()
        {
            var bomb = TestWorld.Content.Unit("atomic_bomb");
            var missile = TestWorld.Content.Unit("nuclear_missile");
            Assert.True(missile.Range > bomb.Range && missile.BlastRadius > bomb.BlastRadius);
            Assert.Equal(UnitDomain.Air, missile.Domain);

            var (g, home, enemy) = World();
            var ring2 = g.CreateArmy(1, TestWorld.H(13, 6), "infantry");
            var weapon = g.SpawnUnit(home, "nuclear_missile");
            Assert.NotNull(g.LaunchNuke(home, weapon, enemy.Position));
            Assert.True(ring2.Units.Single().Hp < 100);
            Assert.Equal(GameState.FalloutTurns, g.Map.Get(TestWorld.H(13, 6)).Fallout);
        }

        [Fact]
        public void Strikes_need_range_and_a_war_with_everyone_caught_in_the_blast()
        {
            var (g, home, enemy) = World();
            var bomb = g.SpawnUnit(home, "atomic_bomb");
            Assert.NotNull(g.NukeBlocker(home, bomb, TestWorld.H(20, 6)));      // 17 hexes away, range 10
            g.Diplomacy.Get(0, 1).AtWar = false;
            Assert.Contains("peace", g.NukeBlocker(home, bomb, enemy.Position));
            Assert.Null(g.LaunchNuke(home, bomb, enemy.Position));
            Assert.Equal(8, enemy.Population);
            g.AddCityState("Kessra", CityStateType.Maritime, TestWorld.H(8, 11));
            Assert.Contains("city-state", g.NukeBlocker(home, bomb, TestWorld.H(8, 11)) ?? "");
            Assert.Null(g.NukeBlocker(home, bomb, TestWorld.H(6, 1)));         // empty land, nobody's
        }

        [Fact]
        public void Nuclear_weapons_never_fly_conventional_air_strikes()
        {
            var (g, home, _) = World();
            g.SpawnUnit(home, "atomic_bomb");
            g.SpawnUnit(home, "bomber");
            var mine = g.CreateArmy(0, TestWorld.H(6, 6), "infantry");
            var foe = g.CreateArmy(1, TestWorld.H(7, 6), "infantry");
            var battle = g.Attack(mine, foe.Position);
            Assert.Single(battle.AirSupport(Combat.BattleSideId.Attacker));
        }

        [Fact]
        public void The_AI_saves_its_nukes_for_targets_worth_it()
        {
            var (g, home, enemy) = World(enemyPop: 2);
            var bomb = g.SpawnUnit(home, "atomic_bomb");
            g.CreateArmy(1, TestWorld.H(8, 3), "warrior");
            Assert.Null(StrategicAI.UseNukes(g, g.Player(0)));                 // a small town and a lone warrior
            Assert.Contains(bomb, home.AirUnits);

            enemy.Population = 9;
            g.CreateArmy(1, enemy.Position, "infantry", "infantry", "infantry");
            var strike = StrategicAI.UseNukes(g, g.Player(0));
            Assert.NotNull(strike);
            Assert.Equal(enemy.Position, strike.Target);
        }

        [Fact]
        public void Fallout_and_the_worlds_memory_of_strikes_survive_a_save()
        {
            var (g, home, enemy) = World();
            Assert.NotNull(g.LaunchNuke(home, g.SpawnUnit(home, "atomic_bomb"), enemy.Position));
            var (loaded, _) = SaveGame.Load(SaveGame.Save(g, null));
            Assert.Equal(g.Map.Tiles.Select(t => t.Fallout), loaded.Map.Tiles.Select(t => t.Fallout));
            Assert.Equal(g.Opinion(g.Player(1), g.Player(0)), loaded.Opinion(loaded.Player(1), loaded.Player(0)));
            Assert.Contains(loaded.Map.Tiles, t => t.Fallout > 0);
        }
    }
}
