using System.Linq;
using Crucible.Core.Economy;
using Crucible.Core.Game;
using Crucible.Core.Hex;
using Crucible.Core.World;
using Xunit;

namespace Crucible.Core.Tests
{
    /// <summary>Map scripts, lakes, oases, pack ice and natural wonders.</summary>
    public class GeographyTests
    {
        static WorldMap Gen(MapScript script, ulong seed = 7) =>
            MapGenerator.Generate(new MapGeneratorSettings { Seed = seed, Width = 56, Height = 36, Script = script });

        [Theory]
        [InlineData(MapScript.Continents, 0.38)]
        [InlineData(MapScript.Pangaea, 0.42)]
        [InlineData(MapScript.Archipelago, 0.3)]
        public void Each_script_hits_its_land_share(MapScript script, double share)
        {
            var map = Gen(script);
            double land = map.Tiles.Count(t => !t.IsWater) / (double)map.Tiles.Count;
            Assert.InRange(land, share - 0.03, share + 0.03);
        }

        [Fact]
        public void Pangaea_is_one_big_landmass_and_archipelago_is_many()
        {
            var pangaea = Landmasses(Gen(MapScript.Pangaea));
            var archipelago = Landmasses(Gen(MapScript.Archipelago));
            string info = $"pangaea {string.Join(",", pangaea)} archipelago {string.Join(",", archipelago)}";
            Assert.True(pangaea.Max() > pangaea.Sum() * 0.8, info);
            Assert.True(archipelago.Count(m => m >= 10) >= 3, info);
        }

        [Theory]
        [InlineData(7UL)]
        [InlineData(2024UL)]
        public void Generated_worlds_have_varied_geography(ulong seed)
        {
            var map = Gen(MapScript.Continents, seed);
            Assert.True(map.Tiles.Count(t => t.Terrain == TerrainType.Lake) >= 3, "lakes");
            Assert.True(map.Tiles.Count(t => t.IsMountain) >= 10, "mountain ranges");
            Assert.Contains(map.Tiles, t => t.IsIce);
            Assert.Contains(map.Tiles, t => t.Feature == FeatureType.Oasis);
            Assert.Contains(map.Tiles, t => t.Terrain == TerrainType.Desert);
            Assert.Contains(map.Tiles, t => t.Feature == FeatureType.Jungle);
            Assert.Contains(map.Tiles, t => t.Terrain == TerrainType.Tundra);

            var wonders = map.Tiles.Where(t => t.Wonder != NaturalWonder.None).ToList();
            Assert.Equal(3, wonders.Select(t => t.Wonder).Distinct().Count());
            Assert.All(wonders, w => Assert.All(wonders.Where(o => o != w), o => Assert.True(o.Coord.DistanceTo(w.Coord) >= 8)));
            Assert.All(map.Tiles.Where(t => t.IsIce), t => Assert.True(t.IsWater && t.Resource == ResourceType.None));
            Assert.All(map.Tiles.Where(t => t.Terrain == TerrainType.Lake),
                t => Assert.Contains(map.NeighborsOf(t.Coord), n => !n.IsWater));
        }

        [Fact]
        public void Ice_blocks_ships_and_embarked_armies_but_lakes_take_boats()
        {
            var ice = new Tile(new HexCoord(0, 0)) { Terrain = TerrainType.Ocean, Elevation = -2, Feature = FeatureType.Ice };
            var lake = new Tile(new HexCoord(1, 0)) { Terrain = TerrainType.Lake, Elevation = -1 };
            var ship = new Mobility(naval: true, canEmbark: false, oceanGoing: true);
            var embarker = new Mobility(naval: false, canEmbark: true, oceanGoing: false);
            var landlubber = new Mobility(naval: false, canEmbark: false, oceanGoing: false);

            Assert.False(TerrainRules.CanStand(ship, ice));
            Assert.False(TerrainRules.CanStand(embarker, ice));
            Assert.True(TerrainRules.CanStand(new Mobility(true, false, false), lake)); // triremes too: lakes are shallow
            Assert.True(TerrainRules.CanStand(embarker, lake));
            Assert.False(TerrainRules.CanStand(landlubber, lake));
        }

        [Fact]
        public void Lakes_oases_and_wonders_yield_and_please()
        {
            Assert.Equal(new Yields(food: 2, gold: 1), EconomyRules.TileYields(new Tile(new HexCoord(0, 0)) { Terrain = TerrainType.Lake, Elevation = -1 }, null));
            var oasis = EconomyRules.TileYields(new Tile(new HexCoord(0, 0)) { Terrain = TerrainType.Desert, Feature = FeatureType.Oasis }, null);
            Assert.Equal(3, oasis.Food);
            Assert.Equal(1, oasis.Gold);
            Assert.Equal(new Yields(), EconomyRules.TileYields(new Tile(new HexCoord(0, 0)) { Terrain = TerrainType.Ocean, Elevation = -2, Feature = FeatureType.Ice }, null));

            var peak = new Tile(new HexCoord(0, 0)) { Terrain = TerrainType.Plains, Elevation = Tile.MountainElevation, Wonder = NaturalWonder.Worldspine };
            Assert.Equal(EconomyRules.WonderYields(NaturalWonder.Worldspine), EconomyRules.TileYields(peak, null));

            var g = TestWorld.Game(TestWorld.FlatMap(), attackerAI: false);
            var city = g.FoundCity(0, TestWorld.H(4, 4), "Rome", true);
            int before = EconomyRules.Happiness(g, g.Player(0));
            g.Map.Get(TestWorld.H(5, 4)).Wonder = NaturalWonder.GlassDunes;
            Assert.Equal(before + 1, EconomyRules.Happiness(g, g.Player(0)));
            Assert.True(EconomyRules.TileYields(g.Map.Get(TestWorld.H(5, 4)), g.Map).Gold >= 3);
        }

        [Fact]
        public void Skirmish_capitals_share_a_landmass_on_good_land()
        {
            foreach (ulong seed in new ulong[] { 3, 7, 11, 2024 })
            {
                var g = GameSetup.NewSkirmish(seed, 48, 32, allAI: true);
                var caps = g.Cities.Where(c => c.IsOriginalCapital).ToList();
                Assert.Equal(2, caps.Count);
                Assert.All(caps, c => Assert.Contains(g.Map.Get(c.Position).Terrain, new[] { TerrainType.Grassland, TerrainType.Plains }));
                Assert.NotNull(Pathfinder(g, caps[0].Position, caps[1].Position));
            }
        }

        static object Pathfinder(GameState g, HexCoord from, HexCoord to)
        {
            // Land-only flood fill: can a walking army get from one capital to the other?
            var seen = new System.Collections.Generic.HashSet<HexCoord> { from };
            var queue = new System.Collections.Generic.Queue<HexCoord>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                if (c == to) return c;
                foreach (var n in g.Map.NeighborsOf(c))
                    if (n.IsPassableForLand && seen.Add(n.Coord)) queue.Enqueue(n.Coord);
            }
            return null;
        }

        static System.Collections.Generic.List<int> Landmasses(WorldMap map)
        {
            var seen = new System.Collections.Generic.HashSet<HexCoord>();
            var sizes = new System.Collections.Generic.List<int>();
            foreach (var t in map.Tiles.Where(t => !t.IsWater))
            {
                if (!seen.Add(t.Coord)) continue;
                int size = 0;
                var queue = new System.Collections.Generic.Queue<HexCoord>();
                queue.Enqueue(t.Coord);
                while (queue.Count > 0)
                {
                    size++;
                    foreach (var n in map.NeighborsOf(queue.Dequeue()))
                        if (!n.IsWater && seen.Add(n.Coord)) queue.Enqueue(n.Coord);
                }
                sizes.Add(size);
            }
            return sizes;
        }
    }
}
