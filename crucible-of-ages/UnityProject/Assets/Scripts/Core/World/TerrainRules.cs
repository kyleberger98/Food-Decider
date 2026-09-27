using Crucible.Core.Hex;

namespace Crucible.Core.World
{
    /// <summary>Movement and sight rules shared by the world map and tactical battles.</summary>
    public static class TerrainRules
    {
        /// <summary>Climbing this many levels (or more) in one step is a cliff.</summary>
        public const int CliffHeight = 2;

        public const int Impassable = int.MaxValue;

        /// <summary>
        /// Movement cost for a land unit stepping between adjacent tiles, or <see cref="Impassable"/>.
        /// Base 1, +1 for rough/marsh terrain, +1 for climbing a level. Descending is free.
        /// </summary>
        public static int LandStepCost(Tile from, Tile to)
        {
            if (from == null || to == null || !to.IsPassableForLand) return Impassable;
            int climb = to.Elevation - from.Elevation;
            if (climb >= CliffHeight) return Impassable;

            int cost = 1;
            if (to.IsRoughTerrain || to.Feature == FeatureType.Marsh) cost += 1;
            if (climb > 0) cost += 1;
            return cost;
        }

        /// <summary>
        /// True when no tile strictly between the endpoints rises above the higher endpoint.
        /// Forests and jungles count as one level taller (see <see cref="Tile.SightHeight"/>).
        /// </summary>
        public static bool HasLineOfSight(WorldMap map, HexCoord from, HexCoord to)
        {
            var a = map.Get(from);
            var b = map.Get(to);
            if (a == null || b == null) return false;
            int eyeLevel = System.Math.Max(a.Elevation, b.Elevation);

            var line = from.LineTo(to);
            for (int i = 1; i < line.Count - 1; i++)
            {
                var t = map.Get(line[i]);
                if (t == null || t.SightHeight > eyeLevel) return false;
            }
            return true;
        }
    }
}
