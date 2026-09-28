using System.Collections.Generic;
using System.Linq;
using Crucible.Core.Hex;
using Crucible.Core.Units;
using Crucible.Core.World;

namespace Crucible.Core.Game
{
    /// <summary>
    /// Auto-explore (Civ V style): each turn an exploring army walks to the reachable, already-seen
    /// hex that would reveal the most unexplored land for the least travel. It keeps clear of
    /// enemies it is at war with, never picks a spot another explorer is already heading to, and
    /// stops exploring once nothing it can reach would reveal anything new.
    /// </summary>
    public static class Exploration
    {
        /// <summary>How far (in hexes walked) an explorer looks for its next vantage point.</summary>
        public const int SearchRadius = 30;

        /// <summary>Scouts, military armies and fleets may explore; civilians-only armies may not.</summary>
        public static bool CanExplore(Army army) => army != null && army.Units.Any(u => u.Def.IsMilitary);

        public static void Run(GameState game, int playerId)
        {
            var claimed = new List<HexCoord>();
            foreach (var army in game.Armies.Where(a => a.OwnerId == playerId && a.AutoExplore).ToList())
                Step(game, army, claimed);
        }

        /// <summary>Moves one explorer as far as it can this turn. Returns false when it has run out of places to explore.</summary>
        public static bool Step(GameState game, Army army, List<HexCoord> claimed = null)
        {
            if (!army.AutoExplore || army.InBattle) return true;
            if (!CanExplore(army)) { army.AutoExplore = false; return false; }
            claimed ??= new List<HexCoord>();

            // Pick, walk, and pick again while moves remain (a target can be reached mid-turn).
            for (int i = 0; i < 4 && army.WorldMovesLeft > 0; i++)
            {
                var target = BestTarget(game, army, claimed);
                if (!target.HasValue)
                {
                    army.AutoExplore = false;
                    army.Destination = null;
                    game.RaiseExplorationFinished(army);
                    return false;
                }
                claimed.Add(target.Value);
                var before = army.Position;
                if (game.OrderMove(army, target.Value) == null) continue; // unreachable after all: try the next best
                if (army.Position == before) break;
            }
            return true;
        }

        static HexCoord? BestTarget(GameState game, Army army, List<HexCoord> claimed)
        {
            var vis = game.Visibility(army.OwnerId);
            var mobility = game.MobilityOf(army);
            bool recon = army.Units.Any(u => u.Def.Class == Content.UnitClass.Recon);

            // Hexes near armies and cities of players we are at war with are off limits.
            var danger = new HashSet<HexCoord>();
            foreach (var other in game.Armies.Where(a => a.OwnerId != army.OwnerId && vis.IsVisible(a.Position) &&
                                                         game.Diplomacy.IsAtWar(army.OwnerId, a.OwnerId)))
                foreach (var c in other.Position.Range(2)) danger.Add(c);
            foreach (var city in game.Cities.Where(c => c.OwnerId != army.OwnerId && vis.IsExplored(c.Position) &&
                                                        game.Diplomacy.IsAtWar(army.OwnerId, c.OwnerId)))
                foreach (var c in city.Position.Range(2)) danger.Add(c);

            // Breadth-first over seen hexes the army could stand on.
            var dist = new Dictionary<HexCoord, int> { [army.Position] = 0 };
            var queue = new Queue<HexCoord>();
            queue.Enqueue(army.Position);
            HexCoord? best = null;
            double bestScore = 0;
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                int d = dist[c];
                var tile = game.Map.Get(c);

                if (d > 0 && !danger.Contains(c) && claimed.All(k => k.DistanceTo(c) > 2))
                {
                    int sight = PlayerVisibility.BaseSight + (tile.Elevation >= 2 && !tile.IsWater ? 1 : 0) + (recon ? 1 : 0);
                    int reveal = c.Range(sight).Count(h => game.Map.InBounds(h) && !vis.IsExplored(h));
                    if (reveal > 0)
                    {
                        double score = reveal / (d + 1.0);
                        if (score > bestScore) { bestScore = score; best = c; }
                    }
                }
                if (d >= SearchRadius) continue;

                foreach (var n in c.Neighbors())
                {
                    if (dist.ContainsKey(n) || !vis.IsExplored(n)) continue;
                    var nt = game.Map.Get(n);
                    if (nt == null || !TerrainRules.CanStand(mobility, nt)) continue;
                    if (TerrainRules.StepCost(mobility, tile, nt) == TerrainRules.Impassable) continue;
                    if (!game.MayEnterTerritory(army.OwnerId, nt)) continue;
                    if (game.CityAt(n) is Empire.City city && city.OwnerId != army.OwnerId) continue;
                    if (game.ArmyAt(n) is Army blocker && blocker != army && blocker.OwnerId != army.OwnerId) continue;
                    dist[n] = d + 1;
                    queue.Enqueue(n);
                }
            }
            // A target occupied by a friendly army is fine to path through but not to stop on.
            if (best.HasValue && game.ArmyAt(best.Value) is Army there && there != army)
                return game.Map.NeighborsOf(best.Value).Select(t => t.Coord)
                    .Where(n => game.ArmyAt(n) == null && TerrainRules.CanStand(mobility, game.Map.Get(n)) && vis.IsExplored(n))
                    .OrderBy(n => n.DistanceTo(army.Position)).ThenBy(n => n.Q).ThenBy(n => n.R)
                    .Select(n => (HexCoord?)n).FirstOrDefault();
            return best;
        }
    }
}
