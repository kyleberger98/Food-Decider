using System.Linq;
using Crucible.Core.Hex;
using Crucible.Core.Random;
using Crucible.Core.World;
using Xunit;

namespace Crucible.Core.Tests
{
    public class HexAndMapTests
    {
        [Fact]
        public void Distance_and_neighbors()
        {
            var o = new HexCoord(0, 0);
            Assert.Equal(6, o.Neighbors().Distinct().Count());
            Assert.All(o.Neighbors(), n => Assert.Equal(1, o.DistanceTo(n)));
            Assert.Equal(5, o.DistanceTo(new HexCoord(3, 2)));
            Assert.Equal(3, o.DistanceTo(new HexCoord(-3, 3)));
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 6)]
        [InlineData(3, 18)]
        public void Ring_has_six_times_radius_hexes(int radius, int expected)
        {
            var ring = new HexCoord(2, -1).Ring(radius).ToList();
            Assert.Equal(expected, ring.Distinct().Count());
            Assert.All(ring, h => Assert.Equal(radius, new HexCoord(2, -1).DistanceTo(h)));
        }

        [Fact]
        public void Range_counts_follow_hex_numbers()
        {
            Assert.Equal(37, new HexCoord(0, 0).Range(3).Distinct().Count()); // 1 + 3·3·4
        }

        [Fact]
        public void Line_is_contiguous_and_includes_endpoints()
        {
            var a = new HexCoord(0, 0);
            var b = new HexCoord(4, -2);
            var line = a.LineTo(b);
            Assert.Equal(a.DistanceTo(b) + 1, line.Count);
            Assert.Equal(a, line.First());
            Assert.Equal(b, line.Last());
            for (int i = 1; i < line.Count; i++) Assert.Equal(1, line[i - 1].DistanceTo(line[i]));
        }

        [Fact]
        public void Offset_and_point_conversions_round_trip()
        {
            for (int row = 0; row < 10; row++)
                for (int col = 0; col < 10; col++)
                {
                    var h = HexCoord.FromOffset(col, row);
                    Assert.Equal((col, row), h.ToOffset());
                    var (x, y) = h.ToPoint(1.0);
                    Assert.Equal(h, HexCoord.FromPoint(x + 0.2, y - 0.2, 1.0));
                }
        }

        [Fact]
        public void Direction_to_is_inverse_of_neighbor()
        {
            var o = new HexCoord(3, 3);
            for (int d = 0; d < 6; d++)
            {
                Assert.Equal(d, o.DirectionTo(o.Neighbor(d)));
                Assert.Equal(HexCoord.OppositeDirection(d), o.Neighbor(d).DirectionTo(o));
            }
        }

        [Fact]
        public void Rng_is_deterministic()
        {
            var a = new DeterministicRng(123);
            var b = new DeterministicRng(123);
            for (int i = 0; i < 100; i++) Assert.Equal(a.NextULong(), b.NextULong());
            var r = new DeterministicRng(5);
            for (int i = 0; i < 1000; i++)
            {
                double d = r.Range(0.8, 1.2);
                Assert.InRange(d, 0.8, 1.2);
            }
        }

        [Fact]
        public void Map_generation_is_deterministic_and_has_land_and_sea()
        {
            var s = new MapGeneratorSettings { Seed = 99, Width = 40, Height = 26 };
            var a = MapGenerator.Generate(s);
            var b = MapGenerator.Generate(s);
            Assert.Equal(a.Tiles.Select(t => (t.Terrain, t.Elevation, t.Feature, t.RiverEdges)),
                         b.Tiles.Select(t => (t.Terrain, t.Elevation, t.Feature, t.RiverEdges)));

            int land = a.Tiles.Count(t => !t.IsWater);
            Assert.InRange(land, a.Tiles.Count / 5, a.Tiles.Count * 4 / 5);
            Assert.All(a.Tiles.Where(t => t.Coord.ToOffset().row == 0), t => Assert.True(t.IsWater));
        }

        [Fact]
        public void Cliffs_block_climbing_but_not_descending()
        {
            var map = TestWorld.FlatMap(elevation: 0);
            var low = map.Get(3, 3);
            var high = map.Get(4, 3);
            high.Elevation = 2;
            Assert.Equal(TerrainRules.Impassable, TerrainRules.LandStepCost(low, high));
            Assert.Equal(1, TerrainRules.LandStepCost(high, low));

            high.Elevation = 1;
            Assert.Equal(2, TerrainRules.LandStepCost(low, high)); // +1 climbing
            high.Feature = FeatureType.Forest;
            Assert.Equal(3, TerrainRules.LandStepCost(low, high));
        }

        [Fact]
        public void Higher_ground_between_units_blocks_line_of_sight()
        {
            var map = TestWorld.FlatMap(elevation: 1);
            var a = TestWorld.H(2, 4);
            var b = TestWorld.H(6, 4);
            Assert.True(TerrainRules.HasLineOfSight(map, a, b));

            map.Get(4, 4).Elevation = 2;
            Assert.False(TerrainRules.HasLineOfSight(map, a, b));

            map.Get(a).Elevation = 2; // shooter now on the same level as the ridge
            Assert.True(TerrainRules.HasLineOfSight(map, a, b));

            map.Get(4, 4).Feature = FeatureType.Forest; // forest adds +1 sight height
            Assert.False(TerrainRules.HasLineOfSight(map, a, b));
        }

        [Fact]
        public void Rivers_are_recorded_on_both_sides()
        {
            var map = TestWorld.FlatMap();
            var a = TestWorld.H(3, 3);
            var b = a.Neighbor(0);
            map.AddRiver(a, b);
            Assert.True(map.HasRiverBetween(a, b));
            Assert.True(map.HasRiverBetween(b, a));
            Assert.False(map.HasRiverBetween(a, a.Neighbor(1)));
        }
    }
}
