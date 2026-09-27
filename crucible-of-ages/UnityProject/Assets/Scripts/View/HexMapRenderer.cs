using System.Collections.Generic;
using Crucible.Core.Hex;
using Crucible.Core.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Crucible.View
{
    /// <summary>
    /// Builds the low-poly world: one vertex-coloured prism per hex, raised by elevation, with
    /// cliff walls down to lower neighbours and thin river strips along hex edges.
    /// TODO(M1): split into 16×16 chunks so edits only rebuild one chunk.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public sealed class HexMapRenderer : MonoBehaviour
    {
        public float hexSize = 1f;
        public float levelHeight = 0.35f;
        public float waterHeight = -0.2f;
        public float riverWidth = 0.14f;

        WorldMap _map;
        readonly List<Vector3> _verts = new List<Vector3>();
        readonly List<Color> _colors = new List<Color>();
        readonly List<int> _tris = new List<int>();

        public WorldMap Map => _map;

        public void Build(WorldMap map)
        {
            _map = map;
            _verts.Clear();
            _colors.Clear();
            _tris.Clear();

            foreach (var tile in map.Tiles) AddTile(tile);

            var mesh = new Mesh { name = "WorldMap", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(_verts);
            mesh.SetColors(_colors);
            mesh.SetTriangles(_tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            GetComponent<MeshFilter>().sharedMesh = mesh;
            GetComponent<MeshCollider>().sharedMesh = mesh;
            var mr = GetComponent<MeshRenderer>();
            if (mr.sharedMaterial == null) mr.sharedMaterial = CreateMaterial();
        }

        static Material CreateMaterial()
        {
            var shader = Shader.Find("Crucible/VertexColorLit") ?? Shader.Find("Sprites/Default");
            return new Material(shader);
        }

        public float TileHeight(Tile t) => t == null ? 0f : t.IsWater ? waterHeight : t.Elevation * levelHeight;

        public Vector3 HexToWorld(HexCoord c)
        {
            var (x, y) = c.ToPoint(hexSize);
            return new Vector3((float)x, TileHeight(_map?.Get(c)), (float)y);
        }

        public HexCoord WorldToHex(Vector3 p) => HexCoord.FromPoint(p.x, p.z, hexSize);

        // ------------------------------------------------------------------ mesh building

        void AddTile(Tile tile)
        {
            var center = HexToWorld(tile.Coord);
            var color = TerrainColor(tile);

            // Top face: centre + 6 corners.
            int c0 = _verts.Count;
            AddVert(center, color);
            for (int i = 0; i < 6; i++) AddVert(center + Corner(i), color * 0.97f);
            for (int i = 0; i < 6; i++) AddTri(c0, c0 + 1 + (i + 1) % 6, c0 + 1 + i);

            // Walls down to each lower neighbour (or to sea floor at the map edge).
            for (int dir = 0; dir < 6; dir++)
            {
                var neighbor = _map.Get(tile.Coord.Neighbor(dir));
                float lowY = neighbor == null ? waterHeight - 0.3f : TileHeight(neighbor);
                if (lowY >= center.y) continue;

                var (a, b) = EdgeCorners(dir);
                var top0 = center + a;
                var top1 = center + b;
                var bot0 = new Vector3(top0.x, lowY, top0.z);
                var bot1 = new Vector3(top1.x, lowY, top1.z);
                AddQuad(top0, top1, bot1, bot0, color * 0.72f);
            }

            // Rivers: draw each shared edge once (directions 0–2), as a strip sitting on the higher side.
            for (int dir = 0; dir < 3; dir++)
            {
                if (!tile.HasRiver(dir)) continue;
                var neighbor = _map.Get(tile.Coord.Neighbor(dir));
                float y = Mathf.Max(center.y, TileHeight(neighbor)) + 0.02f;
                var (a, b) = EdgeCorners(dir);
                var p0 = new Vector3(center.x + a.x, y, center.z + a.z);
                var p1 = new Vector3(center.x + b.x, y, center.z + b.z);
                var inward = (new Vector3(center.x, y, center.z) - (p0 + p1) * 0.5f).normalized * riverWidth;
                AddQuad(p0 + inward, p1 + inward, p1 - inward, p0 - inward, new Color(0.25f, 0.5f, 0.85f));
            }
        }

        /// <summary>Pointy-top corner i at angle 60·i − 30°.</summary>
        Vector3 Corner(int i)
        {
            float a = Mathf.Deg2Rad * (60f * i - 30f);
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * hexSize;
        }

        /// <summary>The two corners bounding the edge that faces hex direction <paramref name="dir"/>.</summary>
        (Vector3, Vector3) EdgeCorners(int dir)
        {
            var (dx, dy) = HexCoord.Direction(dir).ToPoint(1.0);
            float facing = Mathf.Atan2((float)dy, (float)dx);
            float half = Mathf.Deg2Rad * 30f;
            var a = new Vector3(Mathf.Cos(facing - half), 0f, Mathf.Sin(facing - half)) * hexSize;
            var b = new Vector3(Mathf.Cos(facing + half), 0f, Mathf.Sin(facing + half)) * hexSize;
            return (a, b);
        }

        void AddVert(Vector3 p, Color c)
        {
            _verts.Add(p);
            _colors.Add(c);
        }

        void AddTri(int a, int b, int c)
        {
            _tris.Add(a);
            _tris.Add(b);
            _tris.Add(c);
        }

        /// <summary>Double-sided quad so winding never hides a wall.</summary>
        void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            int i = _verts.Count;
            AddVert(a, color);
            AddVert(b, color);
            AddVert(c, color);
            AddVert(d, color);
            AddTri(i, i + 1, i + 2);
            AddTri(i, i + 2, i + 3);
            AddTri(i, i + 2, i + 1);
            AddTri(i, i + 3, i + 2);
        }

        static Color TerrainColor(Tile t)
        {
            Color c;
            switch (t.Terrain)
            {
                case TerrainType.Ocean: c = new Color(0.10f, 0.22f, 0.45f); break;
                case TerrainType.Coast: c = new Color(0.20f, 0.42f, 0.62f); break;
                case TerrainType.Grassland: c = new Color(0.40f, 0.62f, 0.28f); break;
                case TerrainType.Plains: c = new Color(0.66f, 0.64f, 0.36f); break;
                case TerrainType.Desert: c = new Color(0.86f, 0.78f, 0.52f); break;
                case TerrainType.Tundra: c = new Color(0.55f, 0.56f, 0.48f); break;
                case TerrainType.Snow: c = new Color(0.93f, 0.95f, 0.97f); break;
                default: c = Color.magenta; break;
            }

            switch (t.Feature)
            {
                case FeatureType.Forest: c = Color.Lerp(c, new Color(0.13f, 0.36f, 0.16f), 0.65f); break;
                case FeatureType.Jungle: c = Color.Lerp(c, new Color(0.08f, 0.40f, 0.20f), 0.75f); break;
                case FeatureType.Marsh: c = Color.Lerp(c, new Color(0.30f, 0.40f, 0.35f), 0.5f); break;
                case FeatureType.Floodplain: c = Color.Lerp(c, new Color(0.50f, 0.60f, 0.30f), 0.4f); break;
            }

            if (t.IsMountain) c = Color.Lerp(c, new Color(0.45f, 0.42f, 0.40f), 0.75f);
            else if (!t.IsWater) c *= 0.9f + 0.05f * t.Elevation; // higher ground reads lighter
            c.a = 1f;
            return c;
        }
    }
}
