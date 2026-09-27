using System;
using System.Collections.Generic;
using Crucible.Core.Hex;

namespace Crucible.Core.World
{
    /// <summary>Rectangular map stored in odd-r offset layout, addressed by axial <see cref="HexCoord"/>.</summary>
    [Serializable]
    public sealed class WorldMap
    {
        readonly Tile[] _tiles;

        public int Width { get; }
        public int Height { get; }

        public WorldMap(int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentException("Map dimensions must be positive.");
            Width = width;
            Height = height;
            _tiles = new Tile[width * height];
            for (int row = 0; row < height; row++)
                for (int col = 0; col < width; col++)
                    _tiles[row * width + col] = new Tile(HexCoord.FromOffset(col, row));
        }

        public IReadOnlyList<Tile> Tiles => _tiles;

        public bool InBounds(HexCoord c)
        {
            var (col, row) = c.ToOffset();
            return col >= 0 && col < Width && row >= 0 && row < Height;
        }

        /// <summary>The tile at <paramref name="c"/>, or null when off the map.</summary>
        public Tile Get(HexCoord c)
        {
            var (col, row) = c.ToOffset();
            if (col < 0 || col >= Width || row < 0 || row >= Height) return null;
            return _tiles[row * Width + col];
        }

        public Tile Get(int col, int row) => Get(HexCoord.FromOffset(col, row));

        public IEnumerable<Tile> NeighborsOf(HexCoord c)
        {
            foreach (var n in c.Neighbors())
            {
                var t = Get(n);
                if (t != null) yield return t;
            }
        }

        public bool HasRiverBetween(HexCoord a, HexCoord b)
        {
            int dir = a.DirectionTo(b);
            var tile = Get(a);
            return dir >= 0 && tile != null && tile.HasRiver(dir);
        }

        /// <summary>Marks a river along the shared edge of two adjacent tiles (both sides).</summary>
        public void AddRiver(HexCoord a, HexCoord b)
        {
            int dir = a.DirectionTo(b);
            if (dir < 0) throw new ArgumentException($"{a} and {b} are not adjacent.");
            Get(a)?.SetRiver(dir);
            Get(b)?.SetRiver(HexCoord.OppositeDirection(dir));
        }
    }
}
