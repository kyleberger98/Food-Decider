using System;
using System.Collections.Generic;
using Crucible.Core.Hex;
using Crucible.Core.World;

namespace Crucible.View.Art
{
    /// <summary>Geometry for the world map, split so only the ground needs a collider.</summary>
    public sealed class TerrainMesh
    {
        public readonly MeshData Ground = new MeshData();
        public readonly MeshData Props = new MeshData();

        /// <summary>Flat hex tops only: cheap picking geometry, whatever detail the ground has.</summary>
        public readonly MeshData Collider = new MeshData();
        public readonly Dictionary<HexCoord, (int start, int count)> GroundRanges = new Dictionary<HexCoord, (int, int)>();
        public readonly Dictionary<HexCoord, (int start, int count)> PropRanges = new Dictionary<HexCoord, (int, int)>();
    }

    /// <summary>
    /// Low-poly world art (engine-free): bevelled hex prisms with per-tile colour jitter, sandy
    /// beaches and shore foam, pack ice, rivers, and decorations — conifer and broadleaf woods,
    /// jungle, marsh reeds, oasis palms, desert dunes, snow-capped mountain peaks and the natural
    /// wonders.
    /// </summary>
    public sealed class TerrainArt
    {
        public float HexSize = 1f;
        public float LevelHeight = 0.35f;
        public float WaterHeight = -0.2f;
        public float RiverWidth = 0.14f;

        const float Bevel = 0.05f;
        const float InnerRadius = 0.84f;

        WorldMap _map;

        // ------------------------------------------------------------------ palette

        static readonly Rgb Ocean = Rgb.Hex(0x1C3F6E), Coast = Rgb.Hex(0x2F72A0), LakeWater = Rgb.Hex(0x3C95B4);
        static readonly Rgb Foam = Rgb.Hex(0xBFE3EE), IceWhite = Rgb.Hex(0xE4EEF3), IceShadow = Rgb.Hex(0xA9C4D6);
        static readonly Rgb Grass = Rgb.Hex(0x6B9B3C), Plains = Rgb.Hex(0xB2A65A), Desert = Rgb.Hex(0xE2C98B);
        static readonly Rgb Tundra = Rgb.Hex(0x8C917A), Snow = Rgb.Hex(0xEEF2F5), Beach = Rgb.Hex(0xE6D59C);
        static readonly Rgb Rock = Rgb.Hex(0x7C7068), RockDark = Rgb.Hex(0x5A504A), RiverBlue = Rgb.Hex(0x3E82C8);
        static readonly Rgb Pine = Rgb.Hex(0x2C5A36), PineLight = Rgb.Hex(0x3F7A45), Leaf = Rgb.Hex(0x4E8A36), LeafLight = Rgb.Hex(0x78B04E);
        static readonly Rgb JungleLeaf = Rgb.Hex(0x2F7A34), JungleLight = Rgb.Hex(0x4FA046), Bark = Rgb.Hex(0x6A4A30);
        static readonly Rgb Palm = Rgb.Hex(0x5E9E3A), Reed = Rgb.Hex(0x8A9A4E), MarshWater = Rgb.Hex(0x456E62);

        public float TileHeight(Tile t)
        {
            if (t == null) return 0f;
            if (t.IsIce) return WaterHeight + 0.06f;
            if (t.IsWater) return WaterHeight;
            return Math.Min(t.Elevation, 2) * LevelHeight + (t.IsMountain ? 0.1f : 0); // mountains: a plinth; the peaks are props
        }

        public V3 Center(HexCoord c)
        {
            var (x, y) = c.ToPoint(HexSize);
            return new V3((float)x, TileHeight(_map?.Get(c)), (float)y);
        }

        V3 Corner(int i)
        {
            double a = Math.PI / 180 * (60 * i - 30);
            return new V3((float)Math.Cos(a) * HexSize, 0, (float)Math.Sin(a) * HexSize);
        }

        public TerrainMesh Build(WorldMap map)
        {
            _map = map;
            FindTileVariants();
            var result = new TerrainMesh();
            foreach (var tile in map.Tiles)
            {
                var c = Center(tile.Coord);
                result.Collider.Hexagon(Frame.At(c), HexSize, default);

                int g = result.Ground.VertexCount;
                AddGround(result.Ground, tile);
                result.GroundRanges[tile.Coord] = (g, result.Ground.VertexCount - g);

                int p = result.Props.VertexCount;
                AddProps(result.Props, tile);
                result.PropRanges[tile.Coord] = (p, result.Props.VertexCount - p);
            }
            return result;
        }

        // ------------------------------------------------------------------ Blender tile tops

        /// <summary>Tile-top kinds exported from Blender (tile_&lt;kind&gt;_&lt;n&gt;), see Tools/blender/build_tiles.py.</summary>
        static readonly string[] TileKinds =
        {
            "grassland", "plains", "desert", "tundra", "snow",
            "hills_grassland", "hills_plains", "hills_desert", "hills_tundra", "hills_snow",
            "mountain", "marsh", "ocean", "coast", "lake", "ice",
        };

        readonly Dictionary<string, List<string>> _tileVariants = new Dictionary<string, List<string>>();

        void FindTileVariants()
        {
            _tileVariants.Clear();
            var lib = ArtLibrary.Current;
            if (lib == null) return;
            foreach (var kind in TileKinds)
            {
                var names = new List<string>();
                for (int n = 1; lib.Has($"tile_{kind}_{n}"); n++) names.Add($"tile_{kind}_{n}");
                if (names.Count > 0) _tileVariants[kind] = names;
            }
        }

        static string TileKind(Tile t)
        {
            if (t.IsIce) return "ice";
            switch (t.Terrain)
            {
                case TerrainType.Ocean: return "ocean";
                case TerrainType.Coast: return "coast";
                case TerrainType.Lake: return "lake";
            }
            if (t.IsMountain) return "mountain";
            if (t.Feature == FeatureType.Marsh) return "marsh";
            string terrain = t.Terrain.ToString().ToLowerInvariant();
            return t.Elevation >= 2 ? "hills_" + terrain : terrain;
        }

        /// <summary>
        /// Draws a Blender tile top (one of its variants, turned by a multiple of 60°) at the tile's
        /// centre, tinted like the procedural ground (woods darken it, per-tile jitter). False if none.
        /// </summary>
        bool TileTop(MeshData m, Tile tile, V3 center, ArtRng rng, float jitter)
        {
            if (!_tileVariants.TryGetValue(TileKind(tile), out var variants)) return false;
            var model = ArtLibrary.Current.Get(variants[(int)(rng.Next() % (uint)variants.Count)], default);
            var frame = Frame.At(center).Rotate(60 * (int)(rng.Next() % 6)).Scale(HexSize);
            Rgb? tint = null;
            float amount = 0;
            switch (tile.Feature)
            {
                case FeatureType.Forest: tint = Rgb.Hex(0x3E6A30); amount = 0.45f; break;
                case FeatureType.Jungle: tint = Rgb.Hex(0x3C7A2C); amount = 0.6f; break;
                case FeatureType.Floodplain: tint = Rgb.Hex(0x93B24C); amount = 0.5f; break;
                case FeatureType.Oasis: tint = Rgb.Hex(0x9DB65A); amount = 0.35f; break;
            }
            for (int i = 0; i < model.VertexCount; i++)
            {
                var p = model.Positions[i];
                var c = model.Colors[i];
                if (tint.HasValue) c = Rgb.Lerp(c, tint.Value, amount);
                m.Positions.Add(frame.P(p.X, p.Y, p.Z));
                m.Colors.Add(c * jitter);
            }
            return true;
        }

        // ------------------------------------------------------------------ ground

        public static Rgb GroundColor(Tile t)
        {
            if (t.IsIce) return IceWhite;
            Rgb c;
            switch (t.Terrain)
            {
                case TerrainType.Ocean: c = Ocean; break;
                case TerrainType.Coast: c = Coast; break;
                case TerrainType.Lake: c = LakeWater; break;
                case TerrainType.Grassland: c = Grass; break;
                case TerrainType.Plains: c = Plains; break;
                case TerrainType.Desert: c = Desert; break;
                case TerrainType.Tundra: c = Tundra; break;
                case TerrainType.Snow: c = Snow; break;
                default: c = new Rgb(1, 0, 1); break;
            }
            switch (t.Feature)
            {
                case FeatureType.Forest: c = Rgb.Lerp(c, Rgb.Hex(0x3E6A30), 0.45f); break;
                case FeatureType.Jungle: c = Rgb.Lerp(c, Rgb.Hex(0x3C7A2C), 0.6f); break;
                case FeatureType.Marsh: c = Rgb.Lerp(c, Rgb.Hex(0x5E7A56), 0.6f); break;
                case FeatureType.Floodplain: c = Rgb.Lerp(c, Rgb.Hex(0x93B24C), 0.5f); break;
                case FeatureType.Oasis: c = Rgb.Lerp(c, Rgb.Hex(0x9DB65A), 0.35f); break;
            }
            if (t.IsMountain) c = Rgb.Lerp(c, Rock, 0.7f);
            else if (!t.IsWater && t.Elevation >= 2) c = Rgb.Lerp(c, Rgb.Hex(0xA08E62), 0.18f); // hills: drier, browner
            return c;
        }

        void AddGround(MeshData m, Tile tile)
        {
            var rng = new ArtRng(tile.Coord.Q, tile.Coord.R, 1);
            var center = Center(tile.Coord);
            float jitter = tile.IsWater ? rng.Range(0.985f, 1.015f) : rng.Range(0.95f, 1.05f);
            var baseColor = GroundColor(tile) * jitter;

            // Which corners touch land / water (for foam and beaches).
            var cornerLand = new bool[6];
            var cornerWater = new bool[6];
            for (int i = 0; i < 6; i++)
            {
                var corner = center + Corner(i);
                foreach (var n in _map.NeighborsOf(tile.Coord))
                {
                    var nc = Center(n.Coord);
                    float dx = nc.X - corner.X, dz = nc.Z - corner.Z;
                    if (dx * dx + dz * dz > HexSize * HexSize * 1.05f) continue;
                    if (n.IsWater) cornerWater[i] = true;
                    else cornerLand[i] = true;
                }
            }

            if (tile.IsWater && TileTop(m, tile, center, rng, jitter))
            {
                // Blender water or ice in the middle; a flat ring out to the corners carries the shore foam.
                for (int i = 0; i < 6; i++)
                {
                    int j = (i + 1) % 6;
                    Rgb ci = cornerLand[i] ? Rgb.Lerp(baseColor, Foam, tile.IsIce ? 0.2f : 0.55f) : baseColor * 0.97f;
                    Rgb cj = cornerLand[j] ? Rgb.Lerp(baseColor, Foam, tile.IsIce ? 0.2f : 0.55f) : baseColor * 0.97f;
                    m.Quad(center + Corner(i) * InnerRadius, center + Corner(j) * InnerRadius, center + Corner(j), center + Corner(i),
                           baseColor, baseColor, cj, ci);
                }
                if (tile.IsIce) Walls(m, tile, center, center, IceShadow, IceShadow);
                return;
            }
            if (tile.IsWater)
            {
                // Flat water; corners next to land blend into pale foam, ice floes get chunky shading.
                for (int i = 0; i < 6; i++)
                {
                    int j = (i + 1) % 6;
                    Rgb ci = cornerLand[i] ? Rgb.Lerp(baseColor, Foam, tile.IsIce ? 0.2f : 0.55f) : baseColor * 0.97f;
                    Rgb cj = cornerLand[j] ? Rgb.Lerp(baseColor, Foam, tile.IsIce ? 0.2f : 0.55f) : baseColor * 0.97f;
                    var c = tile.IsIce && (i % 2 == 0) ? baseColor * 0.93f : baseColor;
                    m.Tri(center, center + Corner(j), center + Corner(i), c, cj, ci);
                }
                if (tile.IsIce) Walls(m, tile, center, center, IceShadow, IceShadow);
                return;
            }

            // Land: a flat inner hexagon and a bevelled rim; rim corners by the sea turn to beach.
            var inner = new V3[6];
            var outer = new V3[6];
            var outerColor = new Rgb[6];
            for (int i = 0; i < 6; i++)
            {
                inner[i] = center + Corner(i) * InnerRadius;
                outer[i] = center + Corner(i) + new V3(0, -Bevel, 0);
                bool beach = cornerWater[i] && tile.Elevation <= 1 && !tile.IsMountain && tile.Terrain != TerrainType.Snow;
                outerColor[i] = beach ? Beach : baseColor * 0.9f;
            }
            bool authored = TileTop(m, tile, center, rng, jitter);
            for (int i = 0; i < 6; i++)
            {
                int j = (i + 1) % 6;
                if (!authored) m.Tri(center, inner[j], inner[i], baseColor);
                m.Quad(inner[i], inner[j], outer[j], outer[i], baseColor, baseColor, outerColor[j], outerColor[i]);
            }
            Walls(m, tile, center + new V3(0, -Bevel, 0), center, baseColor * 0.72f, Beach * 0.85f);

            // Rivers: each shared edge once (directions 0–2), as a strip on the higher side.
            for (int dir = 0; dir < 3; dir++)
            {
                if (!tile.HasRiver(dir)) continue;
                var neighbor = _map.Get(tile.Coord.Neighbor(dir));
                float y = Math.Max(center.Y, TileHeight(neighbor)) + 0.02f;
                var (a, b) = EdgeCorners(dir);
                var p0 = new V3(center.X + a.X, y, center.Z + a.Z);
                var p1 = new V3(center.X + b.X, y, center.Z + b.Z);
                var mid = (p0 + p1) * 0.5f;
                var inward = (new V3(center.X, y, center.Z) - mid).Normalized * RiverWidth;
                m.Quad(p0 + inward, p1 + inward, p1 - inward, p0 - inward, RiverBlue);
            }
        }

        /// <summary>Walls down to each lower neighbour (or to the sea floor at the map edge).</summary>
        void Walls(MeshData m, Tile tile, V3 rimCenter, V3 center, Rgb color, Rgb beach)
        {
            for (int dir = 0; dir < 6; dir++)
            {
                var neighbor = _map.Get(tile.Coord.Neighbor(dir));
                float lowY = neighbor == null ? WaterHeight - 0.3f : TileHeight(neighbor);
                if (lowY >= rimCenter.Y) continue;
                var (a, b) = EdgeCorners(dir);
                var top0 = rimCenter + a;
                var top1 = rimCenter + b;
                var bot0 = new V3(top0.X, lowY, top0.Z);
                var bot1 = new V3(top1.X, lowY, top1.Z);
                // Shores get a sandy face; inland cliffs a darker shade of the tile.
                var c = neighbor != null && neighbor.IsWater && !tile.IsWater && tile.Elevation <= 1 ? beach : color;
                m.Quad(top0, top1, bot1, bot0, c, c, c * 0.8f, c * 0.8f);
            }
        }

        (V3, V3) EdgeCorners(int dir)
        {
            var (dx, dy) = HexCoord.Direction(dir).ToPoint(1.0);
            double facing = Math.Atan2(dy, dx), half = Math.PI / 6;
            return (new V3((float)Math.Cos(facing - half), 0, (float)Math.Sin(facing - half)) * HexSize,
                    new V3((float)Math.Cos(facing + half), 0, (float)Math.Sin(facing + half)) * HexSize);
        }

        // ------------------------------------------------------------------ decorations

        void AddProps(MeshData m, Tile tile)
        {
            var rng = new ArtRng(tile.Coord.Q, tile.Coord.R, 2);
            var c = Center(tile.Coord);
            var top = Frame.At(c).Rotate(rng.Range(0, 360));

            switch (tile.Wonder)
            {
                case NaturalWonder.Worldspine: Mountain(m, top, rng, 1.7f, wonder: true); return;
                case NaturalWonder.GlassDunes: GlassDunes(m, top, rng); return;
                case NaturalWonder.EmberfallGeyser: Geyser(m, top, rng); return;
            }

            if (tile.IsMountain) { Mountain(m, top, rng, 1f, wonder: false); return; }
            if (tile.IsIce)
            {
                for (int i = 0; i < 2; i++)
                    m.Box(Scatter(top, rng, 0.5f).Rotate(rng.Range(0, 90)), rng.Range(0.12f, 0.22f), 0.05f, rng.Range(0.1f, 0.18f), IceShadow, IceWhite);
                return;
            }
            if (tile.IsWater) return;

            bool cold = tile.Terrain == TerrainType.Tundra || tile.Terrain == TerrainType.Snow || tile.Elevation >= 3;
            switch (tile.Feature)
            {
                case FeatureType.Forest:
                    int trees = 5 + (int)(rng.Value * 3);
                    for (int i = 0; i < trees; i++)
                    {
                        var at = Scatter(top, rng, 0.62f);
                        if (cold || rng.Value < 0.35f) Conifer(m, at, rng.Range(0.42f, 0.58f), cold);
                        else Broadleaf(m, at, rng.Range(1.25f, 1.6f), Leaf, LeafLight);
                    }
                    break;
                case FeatureType.Jungle:
                    for (int i = 0; i < 6; i++)
                    {
                        var at = Scatter(top, rng, 0.64f);
                        if (rng.Value < 0.3f) PalmTree(m, at, rng, 1.4f);
                        else Broadleaf(m, at, rng.Range(1.6f, 2f), JungleLeaf, JungleLight);
                    }
                    break;
                case FeatureType.Marsh:
                    for (int i = 0; i < 3; i++) m.Hexagon(Scatter(top, rng, 0.5f).Move(0, 0.012f, 0), rng.Range(0.12f, 0.2f), MarshWater, MarshWater * 0.9f);
                    for (int i = 0; i < 7; i++)
                        m.Frustum(Scatter(top, rng, 0.65f), 3, 0, 0.018f, rng.Range(0.12f, 0.2f), 0, Reed, Reed * 1.2f, caps: false);
                    break;
                case FeatureType.Oasis:
                    m.Hexagon(top.Move(0, 0.012f, 0), 0.3f, LakeWater, Rgb.Lerp(LakeWater, Foam, 0.4f));
                    PalmTree(m, top.Move(0.38f, 0, 0.1f), rng, 1.5f);
                    PalmTree(m, top.Move(-0.28f, 0, 0.32f), rng, 1.3f);
                    PalmTree(m, top.Move(0.05f, 0, -0.42f), rng, 1.15f);
                    break;
                case FeatureType.None:
                    // Blender desert tiles carry their own wind ripples; the chunky dunes are the fallback.
                    if (tile.Terrain == TerrainType.Desert && rng.Value < 0.6f && !_tileVariants.ContainsKey(TileKind(tile))) Dunes(m, top, rng);
                    else if (tile.Terrain == TerrainType.Tundra && rng.Value < 0.5f)
                        for (int i = 0; i < 2; i++) { var at = Scatter(top, rng, 0.6f); if (!Authored(m, "prop_rock", at, 0.9f)) m.Gem(at, 0.07f, 0.05f, 0.06f, RockDark, Rock); }
                    else if (tile.Elevation >= 2 && rng.Value < 0.5f)
                    { var at = Scatter(top, rng, 0.5f); if (!Authored(m, "prop_rock", at, 1.2f)) m.Gem(at, 0.1f, 0.06f, 0.08f, Rock, Rock * 1.1f); }
                    break;
            }
        }

        static Frame Scatter(Frame f, ArtRng rng, float radius)
        {
            float a = rng.Range(0, 2 * (float)Math.PI), r = radius * (float)Math.Sqrt(rng.Value);
            return f.Move((float)Math.Cos(a) * r, 0, (float)Math.Sin(a) * r).Rotate(rng.Range(0, 360));
        }

        /// <summary>Places a Blender prop if the library has it; the size is relative to the authored model.</summary>
        static bool Authored(MeshData m, string name, Frame f, float scale, float shade = 1f)
        {
            var lib = ArtLibrary.Current;
            return lib != null && lib.AppendTo(m, name, f.Scale(scale), default, shade);
        }

        static void Conifer(MeshData m, Frame f, float h, bool snowy)
        {
            // Authored conifers are ~0.5 tall; alternate the two variants by position.
            string variant = ((int)(f.O.X * 7.3f + f.O.Z * 3.1f) & 1) == 0 ? "prop_conifer" : "prop_conifer_b";
            if (Authored(m, variant, f, (h + 0.05f) / 0.5f, snowy ? 0.92f : 1f)) return;
            var green = snowy ? Rgb.Lerp(Pine, Rgb.Hex(0x9FB3A6), 0.15f) : Pine;
            var tip = snowy ? Rgb.Lerp(PineLight, Snow, 0.55f) : PineLight;
            m.Frustum(f, 4, 0, 0.022f, 0.06f, 0.018f, Bark);
            m.Frustum(f, 6, 0.05f, 0.16f, 0.05f + h * 0.62f, 0, green, Rgb.Lerp(green, tip, 0.6f), phase: 0.3f);
            m.Frustum(f, 6, 0.05f + h * 0.36f, 0.12f, 0.05f + h, 0, green, tip);
        }

        static void Broadleaf(MeshData m, Frame f, float s, Rgb leaf, Rgb light)
        {
            bool jungle = leaf.Equals(JungleLeaf);
            string variant = jungle ? "prop_jungle" : ((int)(f.O.X * 5.7f + f.O.Z * 2.3f) & 1) == 0 ? "prop_broadleaf" : "prop_broadleaf_b";
            if (Authored(m, variant, f, jungle ? s / 1.8f : s / 1.35f)) return;
            m.Frustum(f, 4, 0, 0.025f * s, 0.12f * s, 0.018f * s, Bark);
            m.Gem(f.Move(0, 0.19f * s, 0), 0.13f * s, 0.11f * s, 0.12f * s, leaf, light);
        }

        static void PalmTree(MeshData m, Frame f, ArtRng rng, float s)
        {
            if (Authored(m, "prop_palm", f, s * 0.8f)) return;
            // Curved trunk from three leaning segments, then a crown of drooping fronds.
            float lean = rng.Range(8, 18);
            var seg = f;
            for (int i = 0; i < 3; i++)
            {
                seg = seg.Rotate(0, 0, lean);
                m.Frustum(seg, 4, 0, 0.022f * s, 0.12f * s, 0.018f * s, Bark);
                seg = seg.Move(0, 0.12f * s, 0);
            }
            for (int i = 0; i < 6; i++)
            {
                var frond = seg.Rotate(i * 60 + rng.Range(-10, 10));
                var root = frond.P(0, 0, 0);
                m.Tri(frond.P(0.05f * s, 0.03f * s, -0.035f * s), root, frond.P(0.24f * s, -0.07f * s, 0), Palm * 0.85f, Palm, Palm * 1.15f);
                m.Tri(root, frond.P(0.05f * s, 0.03f * s, 0.035f * s), frond.P(0.24f * s, -0.07f * s, 0), Palm, Palm * 0.85f, Palm * 1.15f);
            }
            m.Gem(seg, 0.03f * s, 0.025f * s, 0.03f * s, Bark);
        }

        static void Dunes(MeshData m, Frame f, ArtRng rng)
        {
            // Two or three parallel wind-blown ridges: a lit windward slope and a shaded lee.
            int count = 2 + (int)(rng.Value * 2);
            for (int i = 0; i < count; i++)
            {
                var d = f.Move(rng.Range(-0.12f, 0.12f), 0, (i - (count - 1) / 2f) * 0.3f);
                float len = rng.Range(0.3f, 0.5f), h = rng.Range(0.045f, 0.07f), w = rng.Range(0.12f, 0.16f);
                var light = Desert * 1.07f;
                var shade = Desert * 0.84f;
                m.Quad(d.P(-len, 0, -w), d.P(-len * 0.7f, h, 0), d.P(len * 0.7f, h, 0), d.P(len, 0, -w), light);
                m.Quad(d.P(len, 0, w * 0.5f), d.P(len * 0.7f, h, 0), d.P(-len * 0.7f, h, 0), d.P(-len, 0, w * 0.5f), shade);
                m.Tri(d.P(-len, 0, w * 0.5f), d.P(-len * 0.7f, h, 0), d.P(-len, 0, -w), shade);
                m.Tri(d.P(len, 0, -w), d.P(len * 0.7f, h, 0), d.P(len, 0, w * 0.5f), light);
            }
        }

        static void Mountain(MeshData m, Frame f, ArtRng rng, float scale, bool wonder)
        {
            var rock = wonder ? Rgb.Lerp(Rock, Rgb.Hex(0xB08A4A), 0.35f) : Rock;
            var dark = wonder ? Rgb.Lerp(RockDark, Rgb.Hex(0x7A5A2A), 0.3f) : RockDark;
            void Peak(Frame at, float r, float h, float phase)
            {
                float snowLine = 0.62f;
                m.Frustum(at, 5, 0, r, h * snowLine, r * (1 - snowLine) * 1.05f, dark, rock, caps: false, phase: phase);
                m.Frustum(at, 5, h * snowLine, r * (1 - snowLine) * 1.05f, h, 0, Snow * 0.92f, Snow, caps: false, phase: phase);
            }
            // Blender peaks (prop_peak_1..3) when available; each already holds a main crag and two shoulders.
            string peak = $"prop_peak_{1 + (int)(rng.Next() % 3)}";
            if (Authored(m, peak, f, scale, wonder ? 0.95f : 1f)) { }
            else
            {
                Peak(f.Move(rng.Range(-0.08f, 0.08f), 0, rng.Range(-0.08f, 0.08f)), 0.8f, 1.25f * scale, rng.Range(0, 1));
                Peak(f.Move(0.42f, 0, -0.26f), 0.48f, 0.75f * scale, rng.Range(0, 1));
                Peak(f.Move(-0.36f, 0, 0.34f), 0.42f, 0.6f * scale, rng.Range(0, 1));
            }
            if (wonder)
            {
                Peak(f.Move(-0.34f, 0, -0.3f), 0.28f, 0.8f, 0.4f);
                // Gold veins catch the light: a ring of bright shards at the foot.
                for (int i = 0; i < 5; i++)
                    m.Frustum(f.Rotate(i * 72).Move(0.5f, 0, 0).Rotate(0, 0, -20), 4, 0, 0.04f, 0.22f, 0, Rgb.Hex(0xE8C25A), Rgb.Hex(0xFFE9A0), caps: false);
            }
        }

        static void GlassDunes(MeshData m, Frame f, ArtRng rng)
        {
            Dunes(m, f, rng);
            var glass = Rgb.Hex(0x6FD0DC);
            var glow = Rgb.Hex(0xE2FCFF);
            for (int i = 0; i < 9; i++)
            {
                var at = Scatter(f, rng, 0.6f).Rotate(0, rng.Range(-20, 20), rng.Range(-20, 20));
                float h = rng.Range(0.3f, 0.75f);
                m.Frustum(at, 4, 0, h * 0.2f, h * 0.8f, h * 0.12f, glass * rng.Range(0.75f, 1f), glass, caps: false);
                m.Frustum(at, 4, h * 0.8f, h * 0.12f, h, 0, glass, glow, caps: false);
            }
        }

        static void Geyser(MeshData m, Frame f, ArtRng rng)
        {
            var basalt = Rgb.Hex(0x3A3432);
            m.Frustum(f, 7, 0, 0.75f, 0.34f, 0.28f, basalt, Rgb.Hex(0x5A4A40));
            m.Hexagon(f.Move(0, 0.345f, 0), 0.24f, Rgb.Hex(0xFFB347), Rgb.Hex(0xE0501E)); // molten vent
            // The plume: stacked, widening steam puffs.
            float y = 0.45f;
            for (int i = 0; i < 5; i++)
            {
                float r = 0.14f + 0.05f * i;
                m.Gem(f.Move(rng.Range(-0.04f, 0.04f), y, rng.Range(-0.04f, 0.04f)).Rotate(i * 30), r, 0.13f, r, Rgb.Hex(0xE6ECEF), Rgb.Hex(0xFFFFFF));
                y += 0.22f;
            }
            for (int i = 0; i < 6; i++) m.Gem(Scatter(f, rng, 0.8f), 0.08f, 0.06f, 0.07f, basalt, Rgb.Hex(0xC0582A));
        }
    }
}
