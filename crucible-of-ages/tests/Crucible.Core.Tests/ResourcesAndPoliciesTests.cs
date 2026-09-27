using System.Linq;
using Crucible.Core.Combat;
using Crucible.Core.Content;
using Crucible.Core.Economy;
using Crucible.Core.Empire;
using Crucible.Core.Game;
using Crucible.Core.World;
using Xunit;

namespace Crucible.Core.Tests
{
    /// <summary>M5: resources, workers & improvements, strategic gating, luxuries, social policies.</summary>
    public class ResourcesAndPoliciesTests
    {
        static (GameState g, City city) Setup()
        {
            // Player 0 is human so each EndTurn advances exactly one player turn.
            var g = TestWorld.Game(TestWorld.FlatMap(20, 14), attackerAI: false);
            var city = g.FoundCity(0, TestWorld.H(6, 6), "Home", true);
            g.FoundCity(1, TestWorld.H(16, 10), "Far", true);
            return (g, city);
        }

        [Fact]
        public void Map_generation_places_every_kind_of_resource()
        {
            var map = MapGenerator.Generate(new MapGeneratorSettings { Seed = 11, Width = 60, Height = 40 });
            var kinds = map.Tiles.Select(t => Improvements.KindOf(t.Resource)).Distinct().ToList();
            Assert.Contains(ResourceKind.Bonus, kinds);
            Assert.Contains(ResourceKind.Strategic, kinds);
            Assert.Contains(ResourceKind.Luxury, kinds);
            Assert.All(map.Tiles.Where(t => t.Resource == ResourceType.Fish), t => Assert.Equal(TerrainType.Coast, t.Terrain));
            Assert.All(map.Tiles.Where(t => t.Resource != ResourceType.None), t => Assert.False(t.IsMountain));
        }

        [Fact]
        public void Resources_and_improvements_add_yields()
        {
            var t = new Tile(new Hex.HexCoord(0, 0)) { Terrain = TerrainType.Plains, Elevation = 1, Resource = ResourceType.Wheat };
            Assert.Equal(2, EconomyRules.TileYields(t, null).Food);
            t.Improvement = ImprovementType.Farm;
            Assert.Equal(3, EconomyRules.TileYields(t, null).Food);
        }

        [Fact]
        public void Improvement_placement_rules()
        {
            var p = TestWorld.Player(0);
            var grass = new Tile(new Hex.HexCoord(0, 0)) { Terrain = TerrainType.Grassland, Elevation = 1, OwnerPlayerId = 0 };
            var hill = new Tile(new Hex.HexCoord(1, 0)) { Terrain = TerrainType.Plains, Elevation = 2, OwnerPlayerId = 0 };
            var horses = new Tile(new Hex.HexCoord(2, 0)) { Terrain = TerrainType.Grassland, Elevation = 1, Resource = ResourceType.Horses, OwnerPlayerId = 0 };

            Assert.False(Improvements.CanBuild(p, grass, ImprovementType.Farm)); // needs Agriculture
            p.Tech.Grant("agriculture");
            p.Tech.Grant("mining");
            p.Tech.Grant("animal_husbandry");
            Assert.True(Improvements.CanBuild(p, grass, ImprovementType.Farm));
            Assert.False(Improvements.CanBuild(p, grass, ImprovementType.Mine));
            Assert.Equal(ImprovementType.Mine, Improvements.Best(p, hill));
            Assert.Equal(ImprovementType.Pasture, Improvements.Best(p, horses));

            grass.OwnerPlayerId = 1;
            Assert.False(Improvements.CanBuild(p, grass, ImprovementType.Farm)); // not our land
        }

        [Fact]
        public void Workers_build_improvements_over_turns_and_moving_cancels()
        {
            var (g, city) = Setup();
            g.Player(0).Tech.Grant("agriculture");
            var site = city.Position.Neighbor(0);
            var worker = g.CreateArmy(0, site, DefaultContent.WorkerUnit);
            var turns = new TurnManager(g);
            turns.Start();

            Assert.True(g.StartImprovement(worker, ImprovementType.Farm));
            Assert.Equal(0, worker.WorldMovesLeft);
            for (int i = 0; i < Improvements.BuildTurns(ImprovementType.Farm) - 1; i++) turns.EndTurn(); // one full round each
            Assert.Equal(ImprovementType.None, g.Map.Get(site).Improvement);
            Assert.Equal(ImprovementType.Farm, worker.BuildOrder);

            turns.EndTurn();
            Assert.Equal(ImprovementType.Farm, g.Map.Get(site).Improvement);
            Assert.Equal(ImprovementType.None, worker.BuildOrder);
            Assert.True(worker.WorldMovesLeft > 0);

            // A new job on the next hex, then walking away cancels the order but keeps the tile's progress.
            var next = city.Position.Neighbor(1);
            Assert.True(g.MoveArmy(worker, next));
            Assert.True(g.StartImprovement(worker, ImprovementType.Farm));
            turns.EndTurn();
            Assert.Equal(1, g.Map.Get(next).ImprovementProgress);
            Assert.True(g.MoveArmy(worker, city.Position.Neighbor(2)));
            Assert.Equal(ImprovementType.None, worker.BuildOrder);
            Assert.Equal(1, g.Map.Get(next).ImprovementProgress);
        }

        [Fact]
        public void Strategic_resources_cap_units()
        {
            var (g, city) = Setup();
            var p = g.Player(0);
            p.Tech.Grant("agriculture"); p.Tech.Grant("animal_husbandry"); p.Tech.Grant("horseback_riding");
            var horse = ProductionItem.Unit("sky_rider"); // player 0 is Khaganate: Sky Rider replaces Horseman
            Assert.False(EconomyRules.CanBuild(g, city, horse));

            var pasture = g.Map.Get(city.Position.Neighbor(1));
            pasture.Resource = ResourceType.Horses;
            pasture.Improvement = ImprovementType.Pasture;
            Assert.Equal(Improvements.StrategicPerSource, Improvements.StrategicAvailable(g, 0, ResourceType.Horses));
            Assert.True(EconomyRules.CanBuild(g, city, horse));

            g.CreateArmy(0, TestWorld.H(2, 2), "sky_rider", "sky_rider", "sky_rider", "sky_rider");
            Assert.Equal(4, Improvements.StrategicUsed(g, 0, ResourceType.Horses));
            Assert.False(EconomyRules.CanBuild(g, city, horse));
        }

        [Fact]
        public void Each_distinct_luxury_adds_four_happiness()
        {
            var (g, city) = Setup();
            var p = g.Player(0);
            int before = EconomyRules.Happiness(g, p);
            var a = g.Map.Get(city.Position.Neighbor(0));
            var b = g.Map.Get(city.Position.Neighbor(1));
            a.Resource = ResourceType.Wine; a.Improvement = ImprovementType.Plantation;
            b.Resource = ResourceType.Wine; b.Improvement = ImprovementType.Plantation; // duplicate: no extra
            Assert.Equal(before + 4, EconomyRules.Happiness(g, p));
            b.Resource = ResourceType.Silk;
            Assert.Equal(before + 8, EconomyRules.Happiness(g, p));
        }

        [Fact]
        public void Policy_cost_grows_and_adoption_follows_the_tree()
        {
            var (g, city) = Setup();
            var p = g.Player(0);
            Assert.Equal(25, EconomyRules.PolicyCost(g, p));

            Assert.False(g.AdoptPolicy(p, "tradition")); // no culture
            p.PolicyCulture = 1000;
            Assert.False(g.AdoptPolicy(p, "aristocracy")); // opener first
            Assert.True(g.AdoptPolicy(p, "tradition"));
            Assert.Equal(975, p.PolicyCulture);
            Assert.True(EconomyRules.PolicyCost(g, p) > 25);
            Assert.False(g.AdoptPolicy(p, "tradition")); // already adopted

            var before = EconomyRules.CityYields(g, city);
            Assert.True(g.AdoptPolicy(p, "aristocracy"));
            Assert.Equal(before.Production + 3, EconomyRules.CityYields(g, city).Production);
        }

        [Fact]
        public void Honor_tree_boosts_combat_and_army_cap()
        {
            var (g, _) = Setup();
            var p = g.Player(0);
            p.PolicyCulture = 10_000;
            int cap = p.ArmyCap;
            Assert.True(g.AdoptPolicy(p, "honor"));
            Assert.True(g.AdoptPolicy(p, "discipline"));
            Assert.True(g.AdoptPolicy(p, "professional_army"));
            Assert.Equal(cap + 1, p.ArmyCap);

            var map = TestWorld.FlatMap();
            var b = TestWorld.Battle(map, TestWorld.H(4, 5), new[] { "warrior" }, TestWorld.H(6, 5), new[] { "warrior" }, attacker: p);
            b.PolicyStrength = id => g.Content.Policy(id).CombatStrengthBonus;
            var mine = b.DeployedUnits(BattleSideId.Attacker).Single();
            var theirs = b.DeployedUnits(BattleSideId.Defender).Single();
            var preview = b.PreviewAttack(mine, b.PositionOf(mine).Value, theirs);
            Assert.Contains(preview.Attacker.Modifiers, m => m.Label == "Policies" && m.Value == 3);
        }

        [Fact]
        public void Ai_automates_workers_and_adopts_policies_over_a_long_game()
        {
            var map = MapGenerator.Generate(new MapGeneratorSettings { Seed = 5, Width = 40, Height = 28 });
            var g = new GameState(TestWorld.Content, map, 5);
            var a = g.AddPlayer("A", DefaultContent.Aurel, true);
            var b = g.AddPlayer("B", DefaultContent.Khaganate, true);
            var land = map.Tiles.Where(t => t.IsPassableForLand && t.Elevation <= 2).Select(t => t.Coord).ToList();
            g.FoundCity(a.Id, land.First(), "A", true);
            g.FoundCity(b.Id, land.OrderByDescending(c => c.DistanceTo(land.First())).First(), "B", true);

            var turns = new TurnManager(g);
            turns.Start();
            for (int i = 0; i < 120; i++) turns.EndTurn();

            foreach (var p in g.Players)
            {
                Assert.True(map.Tiles.Count(t => t.OwnerPlayerId == p.Id && t.Improvement != ImprovementType.None) >= 3,
                    $"{p.Name} improvements");
                Assert.True(p.Policies.Count >= 2, $"{p.Name} policies {p.Policies.Count}");
            }
        }
    }
}
