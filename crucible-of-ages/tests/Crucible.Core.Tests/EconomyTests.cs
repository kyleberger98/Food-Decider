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
    public class EconomyTests
    {
        static (GameState g, City city) OneCity(int population = 1)
        {
            var g = TestWorld.Game(TestWorld.FlatMap(20, 14));
            var city = g.FoundCity(0, TestWorld.H(6, 6), "Home", isCapital: true);
            g.FoundCity(1, TestWorld.H(16, 10), "Far", isCapital: true);
            city.Population = population;
            return (g, city);
        }

        [Theory]
        [InlineData(TerrainType.Grassland, 1, FeatureType.None, 2, 0)]
        [InlineData(TerrainType.Plains, 1, FeatureType.None, 1, 1)]
        [InlineData(TerrainType.Grassland, 2, FeatureType.None, 0, 2)] // hills
        [InlineData(TerrainType.Plains, 1, FeatureType.Forest, 1, 1)]
        [InlineData(TerrainType.Plains, 2, FeatureType.Forest, 1, 2)] // forested hills
        [InlineData(TerrainType.Desert, 0, FeatureType.Floodplain, 2, 0)]
        public void Tile_yields_follow_civ5(TerrainType terrain, int elevation, FeatureType feature, int food, int production)
        {
            var t = new Tile(new Hex.HexCoord(0, 0)) { Terrain = terrain, Elevation = elevation, Feature = feature };
            var y = EconomyRules.TileYields(t, null);
            Assert.Equal(food, y.Food);
            Assert.Equal(production, y.Production);
        }

        [Fact]
        public void Rivers_add_gold_and_mountains_yield_nothing()
        {
            var map = TestWorld.FlatMap();
            var a = TestWorld.H(3, 3);
            map.AddRiver(a, a.Neighbor(0));
            Assert.Equal(1, EconomyRules.TileYields(map.Get(a), map).Gold);
            map.Get(a).Elevation = Tile.MountainElevation;
            Assert.Equal(new Yields(), EconomyRules.TileYields(map.Get(a), map));
        }

        [Fact]
        public void Growth_threshold_matches_civ5_curve()
        {
            Assert.Equal(15, EconomyRules.GrowthThreshold(1));
            Assert.Equal(22, EconomyRules.GrowthThreshold(2));
            Assert.Equal(30, EconomyRules.GrowthThreshold(3)); // 15 + 12 + floor(2^1.8 = 3.48)
        }

        [Fact]
        public void Founding_claims_ring_one_and_assigns_citizens()
        {
            var (g, city) = OneCity(population: 3);
            CityGovernor.AssignCitizens(g, city);
            Assert.All(city.Position.Range(1), c => Assert.Equal(city.Id, g.Map.Get(c).OwnerCityId));
            Assert.Equal(3, city.WorkedTiles.Count);
            Assert.DoesNotContain(city.Position, city.WorkedTiles);
        }

        [Fact]
        public void Cities_grow_over_turns()
        {
            var (g, city) = OneCity();
            var turns = new TurnManager(g);
            turns.Start();
            for (int i = 0; i < 12; i++) turns.EndTurn();
            Assert.True(city.Population >= 2, $"pop {city.Population}, food {city.FoodStored}");
        }

        [Fact]
        public void Starving_city_shrinks()
        {
            var (g, city) = OneCity(population: 4);
            foreach (var t in g.Map.Tiles) { t.Terrain = TerrainType.Desert; t.Feature = FeatureType.None; }
            city.FoodStored = 0;
            EconomyProcessor.ProcessTurn(g, g.Player(0));
            Assert.Equal(3, city.Population);
        }

        [Fact]
        public void Besieged_city_does_not_grow_and_loses_production()
        {
            var (g, city) = OneCity(population: 2);
            CityGovernor.AssignCitizens(g, city);
            var normal = EconomyRules.CityYields(g, city);
            city.BesiegedSinceTurn = g.Turn;
            var besieged = EconomyRules.CityYields(g, city);
            Assert.Equal(normal.Production * 3 / 4, besieged.Production);
            Assert.Equal(0, EconomyRules.FoodSurplus(g, city, besieged));
        }

        [Fact]
        public void Production_completes_buildings_and_walls_raise_defence()
        {
            var (g, city) = OneCity();
            g.Player(0).Tech.Grant("masonry");
            Assert.True(g.SetProduction(city, ProductionItem.Building("walls")));
            city.ProductionStored = 1000;
            int before = g.Map.Get(city.Position).WallTier;
            EconomyProcessor.ProcessTurn(g, g.Player(0));
            Assert.True(city.Has("walls"));
            Assert.Equal(before + 1, g.Map.Get(city.Position).WallTier);
            Assert.False(g.SetProduction(city, ProductionItem.Building("walls"))); // already built
        }

        [Fact]
        public void Units_spawn_into_the_garrison_then_next_to_the_city()
        {
            var (g, city) = OneCity();
            var first = g.SpawnUnit(city, "warrior");
            Assert.NotNull(first);
            var garrison = g.ArmyAt(city.Position);
            Assert.Contains(first, garrison.Units);

            for (int i = 0; i < 3; i++) g.SpawnUnit(city, "warrior"); // fills cap 4
            var overflow = g.SpawnUnit(city, "warrior");
            Assert.NotNull(overflow);
            Assert.DoesNotContain(overflow, garrison.Units);
            Assert.Equal(1, g.Armies.Single(a => a.Units.Contains(overflow)).Position.DistanceTo(city.Position));
        }

        [Fact]
        public void Tech_and_faction_rules_gate_units()
        {
            var g = TestWorld.Game();
            var aurelPlayer = g.AddPlayer("Aurel", DefaultContent.Aurel, true);
            var khanCity = g.FoundCity(0, TestWorld.H(3, 3), "K", true);
            var aurelCity = g.FoundCity(aurelPlayer.Id, TestWorld.H(10, 8), "A", true);

            Assert.False(EconomyRules.CanBuild(g, khanCity, ProductionItem.Unit("archer"))); // needs Archery
            g.Player(0).Tech.Grant("agriculture");
            g.Player(0).Tech.Grant("archery");
            Assert.True(EconomyRules.CanBuild(g, khanCity, ProductionItem.Unit("archer")));

            aurelPlayer.Tech.Grant("iron_working");
            Assert.False(EconomyRules.CanBuild(g, aurelCity, ProductionItem.Unit("aurel_legionary"))); // no iron
            var ironHex = aurelCity.Position.Neighbor(0);
            g.Map.Get(ironHex).Resource = World.ResourceType.Iron;
            g.Map.Get(ironHex).Improvement = World.ImprovementType.Mine;
            Assert.True(EconomyRules.CanBuild(g, aurelCity, ProductionItem.Unit("aurel_legionary")));
            Assert.False(EconomyRules.CanBuild(g, aurelCity, ProductionItem.Unit("swordsman"))); // replaced
            Assert.False(EconomyRules.CanBuild(g, khanCity, ProductionItem.Unit("aurel_legionary"))); // not theirs
            Assert.False(EconomyRules.CanBuild(g, khanCity, ProductionItem.Unit(DefaultContent.MilitiaUnit)));
            Assert.False(EconomyRules.CanBuild(g, khanCity, ProductionItem.Unit(DefaultContent.SettlerUnit))); // pop 1
        }

        [Fact]
        public void Happiness_counts_cities_citizens_and_buildings()
        {
            var (g, city) = OneCity(population: 4);
            var p = g.Player(0);
            Assert.Equal(EconomyRules.BaseHappiness - 3 - 4, EconomyRules.Happiness(g, p));
            city.Buildings.Add("colosseum");
            Assert.Equal(EconomyRules.BaseHappiness - 3 - 4 + 3, EconomyRules.Happiness(g, p));

            p.Happiness = -5;
            var y = new Yields(food: 20);
            Assert.Equal((20 - 8) / 4, EconomyRules.FoodSurplus(g, city, y));
            p.Happiness = -10;
            Assert.Equal(0, EconomyRules.FoodSurplus(g, city, y));
        }

        [Fact]
        public void Bankruptcy_disbands_a_unit()
        {
            var (g, city) = OneCity();
            var army = g.CreateArmy(0, TestWorld.H(3, 3), "warrior", "warrior", "archer", "spearman");
            g.CreateArmy(0, TestWorld.H(3, 9), "warrior", "warrior");
            var p = g.Player(0);
            p.Gold = 0;
            int before = EconomyRules.MilitaryUnitCount(g, p);
            Assert.True(EconomyRules.UnitUpkeep(g, p) > 0);
            // Make income negative: no palace gold on a desert capital, plus upkeep.
            foreach (var t in g.Map.Tiles) { t.Terrain = TerrainType.Desert; t.RiverEdges = 0; }
            p.Gold = -100;
            EconomyProcessor.ProcessTurn(g, p);
            Assert.Equal(before - 1, EconomyRules.MilitaryUnitCount(g, p));
            Assert.Equal(0, p.Gold);
        }

        [Fact]
        public void Culture_grows_borders()
        {
            var (g, city) = OneCity();
            int owned = g.Map.Tiles.Count(t => t.OwnerCityId == city.Id);
            int version = g.MapVersion;
            city.CultureStored = 1000;
            EconomyProcessor.ProcessTurn(g, g.Player(0));
            Assert.Equal(owned + 1, g.Map.Tiles.Count(t => t.OwnerCityId == city.Id));
            Assert.True(g.MapVersion > version);
            Assert.Equal(1, city.TilesClaimed);
        }

        [Fact]
        public void Science_advances_research_automatically()
        {
            var (g, _) = OneCity();
            var p = g.Player(0);
            for (int i = 0; i < 6; i++) EconomyProcessor.ProcessTurn(g, p);
            Assert.Contains("agriculture", p.Tech.Researched);
            Assert.NotNull(p.Tech.CurrentResearch);
        }

        [Fact]
        public void Settlers_found_cities_at_legal_distance()
        {
            var (g, city) = OneCity(population: 3);
            var near = g.CreateArmy(0, TestWorld.H(8, 6), DefaultContent.SettlerUnit);
            Assert.Null(g.FoundCityWithSettler(near)); // 2 hexes from Home

            var far = g.CreateArmy(0, TestWorld.H(11, 3), DefaultContent.SettlerUnit, "warrior");
            var founded = g.FoundCityWithSettler(far);
            Assert.NotNull(founded);
            Assert.False(founded.IsOriginalCapital);
            Assert.Single(far.Units); // escort stays
            Assert.Equal(founded.Id, g.Map.Get(founded.Position).CityId);
        }

        [Fact]
        public void Civilians_do_not_fight_and_are_captured_when_their_side_loses()
        {
            var g = TestWorld.Game();
            var settlers = g.CreateArmy(1, TestWorld.H(5, 5), DefaultContent.SettlerUnit);
            var raiders = g.CreateArmy(0, TestWorld.H(4, 5), "horseman");
            Assert.Null(g.Attack(settlers, raiders.Position)); // civilians can't attack

            var battle = g.Attack(raiders, settlers.Position);
            Assert.NotNull(battle);
            Assert.Equal(BattleStatus.AttackerWon, battle.Status);
            Assert.Null(g.Army(settlers.Id));
        }

        [Fact]
        public void Governor_raises_a_defender_first_then_builds()
        {
            var (g, city) = OneCity();
            Assert.Equal(ProductionItem.Unit("warrior"), CityGovernor.ChooseProduction(g, city));

            g.CreateArmy(0, TestWorld.H(3, 3), "warrior");
            Assert.Equal(ProductionItem.Building("monument"), CityGovernor.ChooseProduction(g, city));
            city.Buildings.Add("monument");
            Assert.True(CityGovernor.ChooseProduction(g, city).HasValue);
        }

        [Fact]
        public void Governor_does_not_build_into_bankruptcy()
        {
            var (g, city) = OneCity();
            g.CreateArmy(0, TestWorld.H(3, 3), "warrior");
            foreach (var t in g.Map.Tiles) t.RiverEdges = 0;
            city.Buildings.Add("monument");
            city.Buildings.Add("granary"); // palace 3 gold − 2 upkeep = +1 net
            g.Player(0).Tech.Grant("pottery");
            g.Player(0).Tech.Grant("writing");
            city.Buildings.Add("library"); // net 0 now
            g.Player(0).Tech.Grant("masonry");
            Assert.NotEqual(ProductionItem.Building("walls"), CityGovernor.ChooseProduction(g, city));
        }
    }
}
