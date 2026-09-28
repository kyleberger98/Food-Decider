using System.Collections.Generic;
using Crucible.Core.Game;
using Crucible.Core.Hex;
using Crucible.Core.World;
using Crucible.View.Art;
using UnityEngine;
using UnityEngine.Rendering;

namespace Crucible.View
{
    /// <summary>
    /// Renders the low-poly world built by <see cref="TerrainArt"/>: bevelled hex ground (with the
    /// picking collider) and a separate decoration mesh — woods, jungle, reeds, palms, dunes, peaks
    /// and natural wonders. Fog recolours vertices in place; decorations on unexplored hexes are
    /// folded away so their silhouettes never give the map away.
    /// TODO: split into 16×16 chunks so edits only rebuild one chunk.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public sealed class HexMapRenderer : MonoBehaviour
    {
        public float hexSize = 1f;
        public float levelHeight = 0.35f;
        public float waterHeight = -0.2f;
        public float riverWidth = 0.14f;

        WorldMap _map;
        TerrainArt _art;
        TerrainMesh _built;
        Mesh _groundMesh, _propMesh;
        Color[] _groundBase, _groundShown, _propBase, _propShown;
        Vector3[] _propPositions, _propShownPositions;
        readonly HashSet<HexCoord> _clearedProps = new HashSet<HexCoord>();

        public WorldMap Map => _map;

        /// <summary>Shared vertex-colour material for everything low-poly (map, props, units, cities).</summary>
        public static Material LowPolyMaterial
        {
            get
            {
                if (_material == null)
                {
                    var shader = Shader.Find("Crucible/VertexColorLit") ?? Shader.Find("Sprites/Default");
                    _material = new Material(shader) { name = "LowPoly", enableInstancing = true };
                }
                return _material;
            }
        }
        static Material _material;

        public void Build(WorldMap map)
        {
            _map = map;
            _art = new TerrainArt { HexSize = hexSize, LevelHeight = levelHeight, WaterHeight = waterHeight, RiverWidth = riverWidth };
            _built = _art.Build(map);
            if (_groundMesh != null) Destroy(_groundMesh); // rebuilt after a load
            if (_propMesh != null) Destroy(_propMesh);

            _groundMesh = ToMesh(_built.Ground, "WorldGround");
            _groundBase = _groundMesh.colors;
            _groundShown = (Color[])_groundBase.Clone();
            GetComponent<MeshFilter>().sharedMesh = _groundMesh;
            GetComponent<MeshCollider>().sharedMesh = _groundMesh;
            GetComponent<MeshRenderer>().sharedMaterial = LowPolyMaterial;

            var props = transform.Find("Decorations");
            if (props == null)
            {
                props = new GameObject("Decorations").transform;
                props.SetParent(transform, false);
                props.gameObject.AddComponent<MeshFilter>();
                props.gameObject.AddComponent<MeshRenderer>().sharedMaterial = LowPolyMaterial;
            }
            _propMesh = ToMesh(_built.Props, "WorldDecorations");
            _propMesh.MarkDynamic();
            _propBase = _propMesh.colors;
            _propShown = (Color[])_propBase.Clone();
            _propPositions = _propMesh.vertices;
            _propShownPositions = (Vector3[])_propPositions.Clone();
            props.GetComponent<MeshFilter>().sharedMesh = _propMesh;
            _clearedProps.Clear();
        }

        /// <summary>Converts engine-free art into a Unity mesh (32-bit indices; flat facets via per-triangle vertices).</summary>
        public static Mesh ToMesh(MeshData data, string name)
        {
            int n = data.VertexCount;
            var verts = new Vector3[n];
            var colors = new Color[n];
            var tris = new int[n];
            for (int i = 0; i < n; i++)
            {
                var p = data.Positions[i];
                var c = data.Colors[i];
                verts[i] = new Vector3(p.X, p.Y, p.Z);
                colors[i] = new Color(c.R, c.G, c.B, 1f);
                tris[i] = i;
            }
            var mesh = new Mesh { name = name, indexFormat = n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = verts;
            mesh.colors = colors;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Hides a hex's decorations (e.g. trees under a city); cleared hexes stay cleared until the next Build.</summary>
        public void ClearDecorations(HexCoord hex)
        {
            if (_clearedProps.Add(hex)) FoldProps(hex, true);
            _propMesh.vertices = _propShownPositions;
        }

        /// <summary>
        /// Unexplored hexes go near-black; explored-but-unseen hexes are dimmed and desaturated.
        /// Owned hexes take a tint of their owner's colour so borders read at a glance.
        /// </summary>
        public void ApplyFog(PlayerVisibility vis, System.Func<int, Color> ownerColor = null)
        {
            if (_groundMesh == null) return;
            foreach (var tile in _map.Tiles)
            {
                var state = vis.Get(tile.Coord);
                int owner = tile.OwnerPlayerId;
                var tint = owner >= 0 && ownerColor != null ? ownerColor(owner) : (Color?)null;
                Recolor(_built.GroundRanges[tile.Coord], _groundBase, _groundShown, state, tint);
                Recolor(_built.PropRanges[tile.Coord], _propBase, _propShown, state, null);
                FoldProps(tile.Coord, state == VisibilityState.Unexplored || _clearedProps.Contains(tile.Coord));
            }
            _groundMesh.colors = _groundShown;
            _propMesh.colors = _propShown;
            _propMesh.vertices = _propShownPositions;
        }

        static void Recolor((int start, int count) range, Color[] from, Color[] to, VisibilityState state, Color? tint)
        {
            for (int i = range.start; i < range.start + range.count; i++)
            {
                var c = from[i];
                if (tint.HasValue) c = Color.Lerp(c, tint.Value, 0.2f);
                if (state == VisibilityState.Unexplored) c = new Color(0.05f, 0.06f, 0.08f);
                else if (state == VisibilityState.Fogged)
                {
                    float grey = c.grayscale;
                    c = Color.Lerp(c, new Color(grey, grey, grey), 0.6f) * 0.55f;
                }
                c.a = 1f;
                to[i] = c;
            }
        }

        /// <summary>Collapses (or restores) a hex's decoration vertices into a point under the ground.</summary>
        void FoldProps(HexCoord hex, bool hidden)
        {
            var (start, count) = _built.PropRanges[hex];
            var hide = HexToWorld(hex) + Vector3.down;
            for (int i = start; i < start + count; i++) _propShownPositions[i] = hidden ? hide : _propPositions[i];
        }

        public float TileHeight(Tile t) => _art != null ? _art.TileHeight(t) : 0f;

        public Vector3 HexToWorld(HexCoord c)
        {
            var (x, y) = c.ToPoint(hexSize);
            return new Vector3((float)x, TileHeight(_map?.Get(c)), (float)y);
        }

        public HexCoord WorldToHex(Vector3 p) => HexCoord.FromPoint(p.x, p.z, hexSize);
    }
}
