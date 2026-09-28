using System.Collections.Generic;
using System.Linq;
using Crucible.Core.Content;
using Crucible.Core.Game;
using Crucible.Core.Hex;
using Crucible.Core.Units;
using Crucible.Core.World;

namespace Crucible.Core.Economy
{
    /// <summary>
    /// Automated workers (GDD §6.2): each idle worker army picks the most valuable unimproved hex in
    /// its owner's territory (connecting strategics and luxuries first), walks there and builds.
    /// </summary>
    public static class WorkerAutomation
    {
        public static bool HasWorker(Army army) => army.Units.Any(u => u.Def.Id == DefaultContent.WorkerUnit);

        public static void Run(GameState game, int playerId)
        {
            var player = game.Player(playerId);
            var claimed = new HashSet<HexCoord>(game.Armies
                .Where(a => a.OwnerId == playerId && a.BuildOrder != ImprovementType.None)
                .Select(a => a.Position));

            foreach (var army in game.Armies.Where(a => a.OwnerId == playerId && HasWorker(a) && !a.InBattle &&
                                                        (player.IsAI || a.AutomatedWorkers)).ToList())
            {
                if (army.BuildOrder != ImprovementType.None) continue;

                // Walk to the best reachable site; an unreachable one (across water, say) must not stall the worker.
                foreach (var target in Targets(game, army, claimed).Take(6))
                {
                    if (army.Position != target && game.OrderMove(army, target) == null) continue;
                    claimed.Add(target);
                    if (army.Position == target)
                        game.StartImprovement(army, Improvements.Best(player, game.Map.Get(target)));
                    break;
                }
            }
        }

        static IEnumerable<HexCoord> Targets(GameState game, Army army, HashSet<HexCoord> claimed)
        {
            var player = game.Player(army.OwnerId);
            return game.Map.Tiles
                .Where(t => t.OwnerPlayerId == player.Id && !claimed.Contains(t.Coord))
                .Where(t => game.ArmyAt(t.Coord) == null || t.Coord == army.Position)
                .Select(t => (tile: t, imp: Improvements.Best(player, t)))
                .Where(x => x.imp != ImprovementType.None)
                .OrderByDescending(x => Value(x.tile, x.imp) - 0.5 * x.tile.Coord.DistanceTo(army.Position))
                .ThenBy(x => x.tile.Coord.Q).ThenBy(x => x.tile.Coord.R)
                .Select(x => x.tile.Coord);
        }

        static double Value(Tile t, ImprovementType imp)
        {
            double v = CityGovernor.Score(Improvements.ImprovementYields(imp));
            var kind = Improvements.KindOf(t.Resource);
            if (Improvements.ImprovementFor(t.Resource) == imp && (kind == ResourceKind.Strategic || kind == ResourceKind.Luxury)) v += 12;
            if (t.OwnerCityId >= 0) v += 2; // inside a city's workable territory
            return v;
        }
    }
}
