using System;
using System.IO;
using System.Linq;
using Crucible.Core.World;
using Crucible.View.Art;
using Crucible.View.Icons;
using Xunit;

namespace Crucible.Core.Tests
{
    /// <summary>The Blender-exported models in Resources/Art and how the art code uses them.</summary>
    [Collection("ArtLibrary")] // tests here swap the global library
    public class ArtLibraryTests
    {
        static readonly string ArtDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../UnityProject/Assets/Resources/Art"));

        static ArtLibrary Library() => ArtLibrary.FromDirectory(ArtDir);

        [Fact]
        public void Every_unit_symbol_prop_and_city_part_has_an_exported_model()
        {
            var lib = Library();
            foreach (UnitIcon icon in Enum.GetValues(typeof(UnitIcon)))
                Assert.True(lib.Has(UnitModels.ModelName(icon)), UnitModels.ModelName(icon));
            foreach (var name in new[] { "prop_conifer", "prop_broadleaf", "prop_jungle", "prop_palm", "prop_rock", "city_house", "city_keep", "city_wall", "city_tower" })
                Assert.True(lib.Has(name), name);
        }

        [Fact]
        public void Exported_models_are_whole_sane_and_the_right_way_up()
        {
            var lib = Library();
            int groundFaces = 0;
            foreach (var name in lib.Names)
            {
                var m = lib.Get(name, new Rgb(0.2f, 0.4f, 0.9f));
                Assert.True(m.VertexCount > 0 && m.VertexCount % 3 == 0, name);
                Assert.All(m.Positions, p => Assert.False(float.IsNaN(p.X + p.Y + p.Z)));
                float minY = m.Positions.Min(p => p.Y), maxY = m.Positions.Max(p => p.Y);
                Assert.InRange(minY, -0.02f, 0.02f);   // stands on the ground
                Assert.InRange(maxY, 0.03f, 1.2f);
                Assert.True(m.Positions.Max(p => Math.Abs(p.X)) < 0.6f && m.Positions.Max(p => Math.Abs(p.Z)) < 0.6f, name);

                // Winding matches Unity: the flat faces on the ground (bases, caps, hull bottoms) face down.
                int up = 0, down = 0;
                for (int t = 0; t < m.VertexCount; t += 3)
                {
                    var a = m.Positions[t]; var b = m.Positions[t + 1]; var c = m.Positions[t + 2];
                    if (Math.Abs(a.Y - b.Y) > 1e-4 || Math.Abs(a.Y - c.Y) > 1e-4 || a.Y > minY + 0.002f) continue;
                    var n = V3.Cross(b - a, c - a);
                    if (n.Y > 0) up++; else down++;
                }
                Assert.True(up == 0, $"{name}: {up} ground faces point up");
                groundFaces += down;
            }
            Assert.True(groundFaces > lib.Count, $"{groundFaces} downward ground faces");
        }

        [Fact]
        public void Team_markers_take_the_owners_colour()
        {
            var lib = Library();
            var blue = new Rgb(0.2f, 0.45f, 0.95f);
            var raw = lib.Get("unit_sword", new Rgb(1, 0, 1));
            Assert.Contains(raw.Colors, ArtLibrary.IsTeamMarker);
            var tinted = lib.Get("unit_sword", blue);
            Assert.DoesNotContain(tinted.Colors, ArtLibrary.IsTeamMarker);
            Assert.Contains(tinted.Colors, c => Math.Abs(c.B - blue.B) < 0.01f && Math.Abs(c.R - blue.R) < 0.01f);
            Assert.Same(tinted, lib.Get("unit_sword", blue)); // cached
        }

        [Fact]
        public void The_art_code_uses_blender_models_when_present_and_falls_back_without_them()
        {
            var saved = ArtLibrary.Current;
            try
            {
                ArtLibrary.Current = null;
                var procedural = UnitModels.Build(UnitIcon.Sword, new Rgb(1, 0, 0));
                var map = MapGenerator.Generate(new MapGeneratorSettings { Seed = 3, Width = 24, Height = 16 });
                var proceduralProps = new TerrainArt().Build(map).Props.VertexCount;

                ArtLibrary.Current = Library();
                var authored = UnitModels.Build(UnitIcon.Sword, new Rgb(1, 0, 0));
                Assert.Equal(ArtLibrary.Current.Get("unit_sword", new Rgb(1, 0, 0)).VertexCount, authored.VertexCount);
                Assert.NotEqual(procedural.VertexCount, authored.VertexCount);
                Assert.NotEqual(proceduralProps, new TerrainArt().Build(map).Props.VertexCount);
                Assert.True(CityModels.Build(6, 2, true, new Rgb(0, 0, 1), 1).VertexCount > 1000);

                Assert.Throws<InvalidDataException>(() => ArtLibrary.Parse(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
            }
            finally
            {
                ArtLibrary.Current = saved;
            }
        }
    }
}
