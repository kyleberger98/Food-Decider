using System;
using System.Linq;
using Crucible.Core.World;
using Crucible.View.Art;
using Crucible.View.Icons;
using Xunit;

namespace Crucible.Core.Tests
{
    /// <summary>Procedural unit symbols, unit/city miniatures and terrain meshes.</summary>
    public class ArtTests
    {
        [Fact]
        public void Every_unit_has_a_symbol_and_every_symbol_draws_something()
        {
            foreach (var def in TestWorld.Content.Units)
                Assert.True(Enum.IsDefined(typeof(UnitIcon), IconArt.ForUnit(def)), def.Id);

            foreach (UnitIcon icon in Enum.GetValues(typeof(UnitIcon)))
            {
                var mask = IconArt.Glyph(icon, 32);
                float ink = mask.Sum() / mask.Length;
                Assert.InRange(ink, 0.08f, 0.7f); // visible but not a solid block
                Assert.All(mask, a => Assert.InRange(a, 0f, 1f));
            }
            var shield = IconArt.Shield(32);
            Assert.True(shield[16 * 32 + 16] > 0.99f, "shield centre is filled");
            Assert.True(shield[31 * 32 + 1] < 0.01f, "shield tapers to a point");
        }

        [Fact]
        public void Class_symbols_follow_the_unit_roles()
        {
            var c = TestWorld.Content;
            Assert.Equal(UnitIcon.Sword, IconArt.ForUnit(c.Unit("warrior")));
            Assert.Equal(UnitIcon.Spear, IconArt.ForUnit(c.Unit("pikeman")));
            Assert.Equal(UnitIcon.Bow, IconArt.ForUnit(c.Unit("archer")));
            Assert.Equal(UnitIcon.Horse, IconArt.ForUnit(c.Unit("knight")));
            Assert.Equal(UnitIcon.Catapult, IconArt.ForUnit(c.Unit("trebuchet")));
            Assert.Equal(UnitIcon.Tank, IconArt.ForUnit(c.Unit("tank")));
            Assert.Equal(UnitIcon.SailShip, IconArt.ForUnit(c.Unit("trireme")));
            Assert.Equal(UnitIcon.Settler, IconArt.ForUnit(c.Unit("settler")));
            Assert.Equal(UnitIcon.General, IconArt.ForUnit(c.Unit("great_general")));
        }

        [Fact]
        public void Every_model_builds_whole_triangles_with_sane_bounds()
        {
            foreach (UnitIcon icon in Enum.GetValues(typeof(UnitIcon)))
            {
                var m = UnitModels.Build(icon, new Rgb(0.2f, 0.4f, 0.9f));
                AssertSane(m, 0.9f, icon.ToString());
            }
            AssertSane(CityModels.Build(12, 3, true, new Rgb(1, 0, 0), 5), 1f, "city");
        }

        [Theory]
        [InlineData(MapScript.Continents)]
        [InlineData(MapScript.Archipelago)]
        public void Terrain_mesh_covers_every_tile_and_decorates_features(MapScript script)
        {
            var map = MapGenerator.Generate(new MapGeneratorSettings { Seed = 3, Width = 30, Height = 20, Script = script });
            var mesh = new TerrainArt().Build(map);
            Assert.Equal(map.Tiles.Count, mesh.GroundRanges.Count);
            Assert.Equal(mesh.Ground.VertexCount, mesh.GroundRanges.Values.Sum(r => r.count));
            Assert.Equal(mesh.Props.VertexCount, mesh.PropRanges.Values.Sum(r => r.count));
            Assert.All(map.Tiles, t => Assert.True(mesh.GroundRanges[t.Coord].count >= 18));
            Assert.All(map.Tiles.Where(t => t.Feature == FeatureType.Forest || t.IsMountain || t.Wonder != NaturalWonder.None),
                t => Assert.True(mesh.PropRanges[t.Coord].count > 0, $"{t} has no decoration"));
            Assert.Equal(0, mesh.Ground.VertexCount % 3);
            Assert.Equal(0, mesh.Props.VertexCount % 3);
            Assert.All(mesh.Ground.Positions.Concat(mesh.Props.Positions), p => Assert.False(float.IsNaN(p.X + p.Y + p.Z)));
        }

        [Fact]
        public void Top_faces_wind_upwards_so_the_ground_is_lit()
        {
            var map = TestWorld.FlatMap();
            var mesh = new TerrainArt().Build(map);
            var (start, count) = mesh.GroundRanges[TestWorld.H(4, 4)];
            // The first six triangles are the flat top; their normals (Unity winding) must point up.
            for (int t = start; t < start + 18; t += 3)
            {
                var a = mesh.Ground.Positions[t]; var b = mesh.Ground.Positions[t + 1]; var c = mesh.Ground.Positions[t + 2];
                Assert.True(V3.Cross(b - a, c - a).Y > 0);
            }
        }

        static void AssertSane(MeshData m, float radius, string what)
        {
            Assert.True(m.VertexCount > 0, what);
            Assert.Equal(0, m.VertexCount % 3);
            Assert.Equal(m.Positions.Count, m.Colors.Count);
            Assert.All(m.Positions, p =>
            {
                Assert.False(float.IsNaN(p.X + p.Y + p.Z), what);
                Assert.True(Math.Abs(p.X) <= radius && Math.Abs(p.Z) <= radius && p.Y >= -0.01f && p.Y < 1.2f, $"{what} vertex out of bounds: {p.X},{p.Y},{p.Z}");
            });
        }
    }
}
