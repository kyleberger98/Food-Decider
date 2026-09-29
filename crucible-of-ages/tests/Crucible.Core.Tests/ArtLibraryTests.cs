using System;
using System.IO;
using System.Linq;
using Crucible.Core.Content;
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
        public void Every_unit_has_a_model_and_every_age_its_own_cities()
        {
            var saved = ArtLibrary.Current;
            try
            {
                var lib = ArtLibrary.Current = Library();
                foreach (var unit in TestWorld.Content.Units)
                    Assert.True(UnitModels.AuthoredName(unit) != null, unit.Id);
                // Ages get their own look: knights are not the generic riders, landships not tanks.
                foreach (var id in new[] { "warrior", "slinger", "knight", "longswordsman", "landship", "helicopter", "giant_death_robot", "trireme", "stealth_bomber", "atomic_bomb", "nuclear_missile" })
                    Assert.Equal("unit_" + id, UnitModels.AuthoredName(TestWorld.Content.Unit(id)));
                Assert.NotEqual(UnitModels.Build(TestWorld.Content.Unit("knight"), new Rgb(0, 0, 1)).VertexCount,
                                UnitModels.Build(TestWorld.Content.Unit("horseman"), new Rgb(0, 0, 1)).VertexCount);

                var looks = new System.Collections.Generic.HashSet<int>();
                foreach (Era era in Enum.GetValues(typeof(Era)))
                {
                    var style = CityModels.StyleFor(era);
                    foreach (var part in new[] { "keep", "house", "wall", "tower" })
                        Assert.Equal(style.Length > 0 ? $"city_{style}_{part}" : "city_" + part, CityModels.PartName(lib, style, part));
                    looks.Add(CityModels.Build(6, 1, false, new Rgb(0, 0, 1), 3, era).VertexCount);
                }
                Assert.Equal(6, looks.Count); // neolithic, ancient, medieval, industrial, modern, future
            }
            finally
            {
                ArtLibrary.Current = saved;
            }
        }

        [Fact]
        public void Exported_models_are_whole_sane_and_the_right_way_up()
        {
            var lib = Library();
            int groundFaces = 0;
            foreach (var name in lib.Names.Where(n => !n.StartsWith("tile_")))
            {
                var m = lib.Get(name, new Rgb(0.2f, 0.4f, 0.9f));
                Assert.True(m.VertexCount > 0 && m.VertexCount % 3 == 0, name);
                Assert.All(m.Positions, p => Assert.False(float.IsNaN(p.X + p.Y + p.Z)));
                float minY = m.Positions.Min(p => p.Y), maxY = m.Positions.Max(p => p.Y);
                Assert.InRange(minY, -0.02f, 0.02f);   // stands on the ground
                Assert.InRange(maxY, 0.03f, 1.3f);
                float reach = name.StartsWith("prop_peak") ? 0.9f : 0.6f;
                Assert.True(m.Positions.Max(p => Math.Abs(p.X)) < reach && m.Positions.Max(p => Math.Abs(p.Z)) < reach, name);

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
        public void Tile_tops_cover_every_terrain_and_meet_their_neighbours_seamlessly()
        {
            var lib = Library();
            var kinds = new[] { "grassland", "plains", "desert", "tundra", "snow", "hills_grassland", "hills_plains", "hills_desert",
                                "hills_tundra", "hills_snow", "mountain", "marsh", "ocean", "coast", "lake", "ice" };
            foreach (var kind in kinds) Assert.True(lib.Has($"tile_{kind}_1"), kind);
            Assert.True(lib.Has("prop_peak_1"));

            const float inner = 0.84f, apothem = inner * 0.8660254f;
            foreach (var name in lib.Names.Where(n => n.StartsWith("tile_")))
            {
                var m = lib.Get(name, default);
                int onEdge = 0;
                for (int i = 0; i < m.VertexCount; i++)
                {
                    var p = m.Positions[i];
                    float hex = new[] { 0f, 60f, 120f }.Max(d => Math.Abs(p.X * (float)Math.Cos(d * Math.PI / 180) + p.Z * (float)Math.Sin(d * Math.PI / 180))) / apothem;
                    Assert.True(hex < 1.001f, $"{name}: vertex outside the hexagon");
                    if (hex > 0.999f)
                    {
                        onEdge++;
                        Assert.True(Math.Abs(p.Y) < 1e-4, $"{name}: edge vertex off the rim height ({p.Y})");
                    }
                    Assert.InRange(p.Y, -0.06f, 0.25f);
                }
                Assert.True(onEdge >= 30, $"{name}: {onEdge} edge vertices");
                // Units stand in the middle: it stays near level.
                var middle = m.Positions.Where(p => Math.Sqrt(p.X * p.X + p.Z * p.Z) < 0.1).ToList();
                Assert.All(middle, p => Assert.InRange(p.Y, -0.03f, 0.05f));
                // Surface faces point up (Unity winding).
                int up = 0, down = 0;
                for (int t = 0; t < m.VertexCount; t += 3)
                {
                    var n = V3.Cross(m.Positions[t + 1] - m.Positions[t], m.Positions[t + 2] - m.Positions[t]);
                    if (n.Y > 1e-6) up++; else if (n.Y < -1e-6) down++;
                }
                Assert.True(up > down * 3, $"{name}: {up} up vs {down} down");
            }
        }

        [Fact]
        public void The_map_uses_tile_tops_and_keeps_a_flat_collider()
        {
            var saved = ArtLibrary.Current;
            try
            {
                var map = MapGenerator.Generate(new MapGeneratorSettings { Seed = 5, Width = 20, Height = 14 });
                ArtLibrary.Current = null;
                var flat = new TerrainArt().Build(map);
                ArtLibrary.Current = Library();
                var authored = new TerrainArt().Build(map);
                Assert.True(authored.Ground.VertexCount > flat.Ground.VertexCount * 3);
                Assert.Equal(map.Tiles.Count * 18, authored.Collider.VertexCount); // six triangles a hex
                Assert.Equal(flat.Collider.VertexCount, authored.Collider.VertexCount);
                Assert.All(map.Tiles, t => Assert.True(authored.GroundRanges[t.Coord].count > 100));
            }
            finally
            {
                ArtLibrary.Current = saved;
            }
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
                Assert.True(EffectModels.MushroomCloud().VertexCount > 0);
                var map = MapGenerator.Generate(new MapGeneratorSettings { Seed = 3, Width = 24, Height = 16 });
                var proceduralProps = new TerrainArt().Build(map).Props.VertexCount;

                ArtLibrary.Current = Library();
                var authored = UnitModels.Build(UnitIcon.Sword, new Rgb(1, 0, 0));
                Assert.Equal(ArtLibrary.Current.Get("unit_sword", new Rgb(1, 0, 0)).VertexCount, authored.VertexCount);
                Assert.NotEqual(procedural.VertexCount, authored.VertexCount);
                Assert.NotEqual(proceduralProps, new TerrainArt().Build(map).Props.VertexCount);
                Assert.True(CityModels.Build(6, 2, true, new Rgb(0, 0, 1), 1).VertexCount > 1000);
                Assert.Equal(ArtLibrary.Current.Get("prop_mushroom", new Rgb(1, 1, 1)).VertexCount, EffectModels.MushroomCloud().VertexCount);

                Assert.Throws<InvalidDataException>(() => ArtLibrary.Parse(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
            }
            finally
            {
                ArtLibrary.Current = saved;
            }
        }
    }
}
