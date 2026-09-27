using System.Collections.Generic;
using Crucible.Core.Content;
using Crucible.Core.Hex;
using Crucible.Core.Units;
using Crucible.Core.World;

namespace Crucible.Core.Game
{
    public enum VisibilityState
    {
        Unexplored,

        /// <summary>Seen before: terrain and cities are remembered, armies are not shown.</summary>
        Fogged,

        Visible,
    }

    /// <summary>One player's fog of war (GDD §2 / M1).</summary>
    public sealed class PlayerVisibility
    {
        public const int BaseSight = 2;

        readonly HashSet<HexCoord> _explored = new HashSet<HexCoord>();
        HashSet<HexCoord> _visible = new HashSet<HexCoord>();

        /// <summary>Bumped whenever the visible or explored set changes, so views know when to redraw.</summary>
        public int Version { get; private set; }

        public IReadOnlyCollection<HexCoord> Visible => _visible;
        public IReadOnlyCollection<HexCoord> Explored => _explored;

        public VisibilityState Get(HexCoord c) =>
            _visible.Contains(c) ? VisibilityState.Visible :
            _explored.Contains(c) ? VisibilityState.Fogged : VisibilityState.Unexplored;

        public bool IsVisible(HexCoord c) => _visible.Contains(c);
        public bool IsExplored(HexCoord c) => _explored.Contains(c);

        /// <summary>Sight radius of an army: 2, +1 on hills or higher, +1 if it contains recon units.</summary>
        public static int SightOf(Army army, WorldMap map)
        {
            int sight = BaseSight;
            if (map.Get(army.Position)?.Elevation >= 2) sight++;
            foreach (var u in army.Units)
                if (u.Def.Class == UnitClass.Recon) { sight++; break; }
            return sight;
        }

        /// <summary>Recomputes what is visible from the given (position, radius) sources.</summary>
        internal void Recompute(WorldMap map, IEnumerable<(HexCoord from, int radius)> sources)
        {
            var visible = new HashSet<HexCoord>();
            foreach (var (from, radius) in sources)
            {
                foreach (var c in from.Range(radius))
                {
                    if (!map.InBounds(c) || visible.Contains(c)) continue;
                    if (c.DistanceTo(from) <= 1 || TerrainRules.HasLineOfSight(map, from, c)) visible.Add(c);
                }
            }

            bool changed = !visible.SetEquals(_visible);
            _visible = visible;
            foreach (var c in visible)
                if (_explored.Add(c)) changed = true;
            if (changed) Version++;
        }
    }
}
