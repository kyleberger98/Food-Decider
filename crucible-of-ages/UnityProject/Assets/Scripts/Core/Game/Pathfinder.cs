using System;
using System.Collections.Generic;
using Crucible.Core.Hex;
using Crucible.Core.Units;
using Crucible.Core.World;

namespace Crucible.Core.Game
{
    /// <summary>A route for an army: the hexes to enter in order and the turn on which each is reached.</summary>
    public sealed class ArmyPath
    {
        public readonly List<HexCoord> Steps = new List<HexCoord>();

        /// <summary>0 = reached this turn, 1 = next turn, …</summary>
        public readonly List<int> TurnOfStep = new List<int>();

        public HexCoord Destination => Steps[Steps.Count - 1];

        /// <summary>Number of turns needed to arrive (1 = arrives this turn).</summary>
        public int Turns => TurnOfStep.Count == 0 ? 0 : TurnOfStep[TurnOfStep.Count - 1] + 1;
    }

    /// <summary>
    /// Turn-aware A* for world-map movement, mirroring <see cref="GameState.MoveArmy"/> exactly:
    /// any MP left lets an army take one more step (Civ V), and rivers and enemy ZOC end the turn.
    /// Other armies, enemy cities and active battlefields are obstacles.
    /// </summary>
    public static class Pathfinder
    {
        public const int MaxSearchNodes = 20000;

        public static ArmyPath Find(GameState game, Army army, HexCoord destination)
        {
            if (army == null || army.IsEmpty || army.Position == destination) return null;
            if (!IsEnterable(game, army, destination)) return null;

            int maxMp = Math.Max(1, game.WorldMovementOf(army));
            var mobility = game.MobilityOf(army);
            if (!TerrainRules.CanStand(mobility, game.Map.Get(destination))) return null;
            var start = new Node(army.Position, 0, army.WorldMovesLeft);

            var best = new Dictionary<HexCoord, Node> { [army.Position] = start };
            var cameFrom = new Dictionary<HexCoord, HexCoord>();
            var open = new MinHeap<HexCoord>();
            open.Push(army.Position, Priority(start, maxMp, army.Position.DistanceTo(destination)));

            int expanded = 0;
            while (open.Count > 0 && expanded++ < MaxSearchNodes)
            {
                var current = open.Pop(out var priority);
                var node = best[current];
                if (priority > Priority(node, maxMp, current.DistanceTo(destination))) continue; // stale entry
                if (current == destination) return Build(cameFrom, best, army.Position, destination);

                // Out of moves: the next step happens next turn with full MP.
                int turn = node.Turn, left = node.MovesLeft;
                if (left <= 0)
                {
                    turn++;
                    left = maxMp;
                }

                var fromTile = game.Map.Get(current);
                foreach (var next in current.Neighbors())
                {
                    if (!IsEnterable(game, army, next)) continue;
                    int cost = TerrainRules.StepCost(mobility, fromTile, game.Map.Get(next));
                    if (cost == TerrainRules.Impassable) continue;

                    int nextLeft = Math.Max(0, left - cost);
                    if (game.Map.HasRiverBetween(current, next) || game.InEnemyZoc(next, army.OwnerId)) nextLeft = 0;

                    var candidate = new Node(next, turn, nextLeft);
                    if (best.TryGetValue(next, out var existing) && !candidate.IsBetterThan(existing, maxMp)) continue;

                    best[next] = candidate;
                    cameFrom[next] = current;
                    open.Push(next, Priority(candidate, maxMp, next.DistanceTo(destination)));
                }
            }
            return null;
        }

        static bool IsEnterable(GameState game, Army army, HexCoord c)
        {
            var tile = game.Map.Get(c);
            if (tile == null || !game.MayEnterTerritory(army.OwnerId, tile)) return false;
            if (game.ArmyAt(c) is Army other && other != army) return false;
            if (game.CityAt(c) is Empire.City city && city.OwnerId != army.OwnerId) return false;
            return game.BattleCovering(c) == null;
        }

        /// <summary>Elapsed movement in MP units; the heuristic (1 MP per hex) never overestimates.</summary>
        static int Priority(Node n, int maxMp, int heuristic) => n.Cost(maxMp) + heuristic;

        static ArmyPath Build(Dictionary<HexCoord, HexCoord> cameFrom, Dictionary<HexCoord, Node> best, HexCoord start, HexCoord end)
        {
            var path = new ArmyPath();
            var stack = new Stack<HexCoord>();
            for (var c = end; c != start; c = cameFrom[c]) stack.Push(c);
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                path.Steps.Add(c);
                path.TurnOfStep.Add(best[c].Turn);
            }
            return path;
        }

        readonly struct Node
        {
            public readonly HexCoord Hex;
            public readonly int Turn;
            public readonly int MovesLeft;

            public Node(HexCoord hex, int turn, int movesLeft)
            {
                Hex = hex;
                Turn = turn;
                MovesLeft = movesLeft;
            }

            /// <summary>(turn, spent MP) flattened; arriving with 0 MP equals starting the next turn fresh.</summary>
            public int Cost(int maxMp) => Turn * maxMp + (maxMp - Math.Min(maxMp, MovesLeft));

            public bool IsBetterThan(Node other, int maxMp) => Cost(maxMp) < other.Cost(maxMp);
        }
    }

    /// <summary>Minimal binary heap (Unity's .NET profile has no PriorityQueue).</summary>
    public sealed class MinHeap<T>
    {
        readonly List<(T item, int priority)> _items = new List<(T, int)>();

        public int Count => _items.Count;

        public void Push(T item, int priority)
        {
            _items.Add((item, priority));
            int i = _items.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (_items[parent].priority <= _items[i].priority) break;
                (_items[parent], _items[i]) = (_items[i], _items[parent]);
                i = parent;
            }
        }

        public T Pop(out int priority)
        {
            var top = _items[0];
            int last = _items.Count - 1;
            _items[0] = _items[last];
            _items.RemoveAt(last);

            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, smallest = i;
                if (l < _items.Count && _items[l].priority < _items[smallest].priority) smallest = l;
                if (r < _items.Count && _items[r].priority < _items[smallest].priority) smallest = r;
                if (smallest == i) break;
                (_items[smallest], _items[i]) = (_items[i], _items[smallest]);
                i = smallest;
            }
            priority = top.priority;
            return top.item;
        }
    }
}
