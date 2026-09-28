using System.Collections.Generic;
using System.Linq;
using Crucible.Core.Hex;
using Crucible.Core.World;

namespace Crucible.Core.Combat
{
    /// <summary>Carves the local battle zone out of the world map (GDD §4.3).</summary>
    public static class Battlefield
    {
        public const int FieldRadius = 3;
        public const int SiegeRadius = 4;

        /// <summary>
        /// Land and water hexes (no mountains) within <paramref name="radius"/> of the midpoint between
        /// the two armies. Both origin tiles are always included. Each unit only uses hexes of its own
        /// domain, so coastal fights mix fleets and troops (GDD §4.7).
        /// </summary>
        public static HashSet<HexCoord> Generate(WorldMap map, HexCoord attackerOrigin, HexCoord defenderOrigin, int radius)
        {
            var center = HexCoord.Round(
                (attackerOrigin.Q + defenderOrigin.Q) / 2.0 + 1e-6,
                (attackerOrigin.R + defenderOrigin.R) / 2.0 + 1e-6);

            var tiles = new HashSet<HexCoord>(center.Range(radius).Where(c =>
            {
                var t = map.Get(c);
                return t != null && (t.IsPassableForLand || t.IsWater);
            }));
            tiles.Add(attackerOrigin);
            tiles.Add(defenderOrigin);
            return tiles;
        }

        /// <summary>
        /// The side's deployment hexes: those strictly closer to its own origin than to the enemy's,
        /// nearest first, capped at <paramref name="maxSize"/>. Zones of opposing sides never overlap.
        /// </summary>
        public static List<HexCoord> DeploymentZone(IEnumerable<HexCoord> field, HexCoord ownOrigin, HexCoord enemyOrigin, int maxSize)
        {
            return field
                .Where(c => c.DistanceTo(ownOrigin) < c.DistanceTo(enemyOrigin))
                .OrderBy(c => c.DistanceTo(ownOrigin))
                .ThenBy(c => c.Q).ThenBy(c => c.R)
                .Take(maxSize)
                .ToList();
        }
    }
}
