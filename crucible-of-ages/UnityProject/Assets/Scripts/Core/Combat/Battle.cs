using System;
using System.Collections.Generic;
using System.Linq;
using Crucible.Core.Empire;
using Crucible.Core.Hex;
using Crucible.Core.Random;
using Crucible.Core.Units;
using Crucible.Core.World;

namespace Crucible.Core.Combat
{
    public enum BattleSideId
    {
        Attacker = 0,
        Defender = 1,
    }

    public enum BattleStatus
    {
        /// <summary>A round is being played; <see cref="Battle.ActiveSide"/> is acting.</summary>
        InProgress,

        /// <summary>Round finished; the next round starts on the attacker's next world turn.</summary>
        AwaitingNextRound,

        AttackerWon,
        DefenderWon,
        AttackerRetreated,
        DefenderRetreated,
    }

    public sealed class BattleSide
    {
        public BattleSideId Id { get; }
        public Player Player { get; }
        public HexCoord Origin { get; }
        public List<Army> Armies { get; } = new List<Army>();
        public List<HexCoord> DeploymentZone { get; internal set; } = new List<HexCoord>();

        /// <summary>Units waiting off-field; they deploy when frontline slots free up.</summary>
        public List<Unit> Reserve { get; } = new List<Unit>();

        /// <summary>Maximum units on the field at once (the player's army cap).</summary>
        public int FrontlineCap => Player.ArmyCap;

        public BattleSide(BattleSideId id, Player player, HexCoord origin)
        {
            Id = id;
            Player = player ?? throw new ArgumentNullException(nameof(player));
            Origin = origin;
        }
    }

    /// <summary>
    /// One Humankind-style tactical battle fought on a patch of the world map (GDD §4.3–4.4).
    /// Up to 3 rounds × 3 battle turns per side, alternating attacker → defender.
    /// All state changes go through <see cref="TryMove"/>, <see cref="TryAttack"/>,
    /// <see cref="EndTurn"/>, <see cref="Retreat"/> and <see cref="BeginNextRound"/>.
    /// </summary>
    public sealed class Battle
    {
        public const int MaxRounds = 3;
        public const int TurnsPerRound = 3;
        public const int UnhappinessPenalty = -3;

        readonly DeterministicRng _rng;
        readonly Dictionary<int, HexCoord> _positions = new Dictionary<int, HexCoord>();
        readonly Dictionary<HexCoord, Unit> _occupants = new Dictionary<HexCoord, Unit>();
        readonly Dictionary<int, BattleSideId> _sideOf = new Dictionary<int, BattleSideId>();

        /// <summary>Units that entered enemy ZOC or crossed a river this turn: may still attack, not move.</summary>
        readonly HashSet<int> _moveLocked = new HashSet<int>();

        public int Id { get; }
        public WorldMap Map { get; }
        public IReadOnlyCollection<HexCoord> Tiles => _tiles;
        readonly HashSet<HexCoord> _tiles;

        public BattleSide Attacker { get; }
        public BattleSide Defender { get; }

        /// <summary>For siege assaults: the city centre the attacker must hold to win.</summary>
        public HexCoord? Objective { get; }

        public int Round { get; private set; } = 1;
        public int TurnInRound { get; private set; } = 1;
        public BattleSideId ActiveSide { get; private set; } = BattleSideId.Attacker;
        public BattleStatus Status { get; private set; } = BattleStatus.InProgress;
        public bool Started { get; private set; }

        public List<string> Log { get; } = new List<string>();

        public Battle(int id, WorldMap map, HashSet<HexCoord> tiles, BattleSide attacker, BattleSide defender,
            DeterministicRng rng, HexCoord? objective = null)
        {
            Id = id;
            Map = map ?? throw new ArgumentNullException(nameof(map));
            _tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
            Attacker = attacker ?? throw new ArgumentNullException(nameof(attacker));
            Defender = defender ?? throw new ArgumentNullException(nameof(defender));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            Objective = objective;
        }

        public bool IsFinished => Status != BattleStatus.InProgress && Status != BattleStatus.AwaitingNextRound;

        /// <summary>Side that won, or null while undecided.</summary>
        public BattleSideId? Winner
        {
            get
            {
                switch (Status)
                {
                    case BattleStatus.AttackerWon:
                    case BattleStatus.DefenderRetreated:
                        return BattleSideId.Attacker;
                    case BattleStatus.DefenderWon:
                    case BattleStatus.AttackerRetreated:
                        return BattleSideId.Defender;
                    default:
                        return null;
                }
            }
        }

        public BattleSide Side(BattleSideId id) => id == BattleSideId.Attacker ? Attacker : Defender;
        public BattleSide Opponent(BattleSideId id) => id == BattleSideId.Attacker ? Defender : Attacker;
        public BattleSide Active => Side(ActiveSide);

        // ------------------------------------------------------------------ setup

        /// <summary>Adds an army to a side before or during the battle. Its units join the reserve.</summary>
        public void AddArmy(BattleSideId sideId, Army army)
        {
            var side = Side(sideId);
            if (army.OwnerId != side.Player.Id) throw new InvalidOperationException("Army owner does not match the side.");
            if (side.Armies.Contains(army)) return;
            side.Armies.Add(army);
            army.BattleId = Id;
            foreach (var u in army.Units.Where(u => u.IsAlive))
            {
                _sideOf[u.Id] = sideId;
                side.Reserve.Add(u);
            }
            if (Started) Log.Add($"{army} joins the {sideId.ToString().ToLowerInvariant()} as reinforcements.");
        }

        /// <summary>Computes deployment zones, deploys the defender then the attacker, and starts round 1.</summary>
        public void Start()
        {
            if (Started) throw new InvalidOperationException("Battle already started.");
            Started = true;

            foreach (var side in new[] { Defender, Attacker })
            {
                var enemy = Opponent(side.Id);
                int size = Math.Max(side.FrontlineCap + 2, _tiles.Count / 3);
                side.DeploymentZone = Battlefield.DeploymentZone(_tiles, side.Origin, enemy.Origin, size);
                DeployReserves(side);
            }

            Log.Add($"Battle {Id} begins: {Attacker.Player.Name} attacks {Defender.Player.Name} on {_tiles.Count} hexes.");
            CheckElimination();
            if (Status == BattleStatus.InProgress) BeginSideTurn(Attacker);
        }

        /// <summary>Fills free frontline slots from the reserve. Front-liners take the tiles nearest the enemy.</summary>
        void DeployReserves(BattleSide side)
        {
            var enemyOrigin = Opponent(side.Id).Origin;
            var freeTiles = side.DeploymentZone
                .Where(t => !_occupants.ContainsKey(t))
                .OrderBy(t => t.DistanceTo(enemyOrigin)).ThenBy(t => t.Q).ThenBy(t => t.R)
                .ToList();
            var queue = side.Reserve
                .Where(u => u.IsAlive)
                .OrderBy(u => IsBackline(u) ? 1 : 0)
                .ToList();

            int slots = side.FrontlineCap - DeployedUnits(side.Id).Count();
            int tileIndex = 0;
            foreach (var unit in queue)
            {
                if (slots <= 0 || tileIndex >= freeTiles.Count) break;
                Place(unit, freeTiles[tileIndex++]);
                side.Reserve.Remove(unit);
                unit.Fortified = false;
                slots--;
            }
            side.Reserve.RemoveAll(u => !u.IsAlive);
        }

        static bool IsBackline(Unit u) => u.Def.IsRanged;

        // ------------------------------------------------------------------ queries

        public IEnumerable<Unit> DeployedUnits(BattleSideId side) =>
            _positions.Keys.Select(FindUnit).Where(u => u != null && _sideOf[u.Id] == side);

        public IEnumerable<Unit> AllDeployedUnits => _occupants.Values;

        public Unit UnitAt(HexCoord c) => _occupants.TryGetValue(c, out var u) ? u : null;

        public HexCoord? PositionOf(Unit u) => _positions.TryGetValue(u.Id, out var c) ? c : (HexCoord?)null;

        public BattleSideId SideOf(Unit u) => _sideOf.TryGetValue(u.Id, out var s)
            ? s
            : throw new InvalidOperationException($"{u} is not in battle {Id}.");

        public bool Contains(HexCoord c) => _tiles.Contains(c);

        public bool IsMoveLocked(Unit u) => _moveLocked.Contains(u.Id);

        Unit FindUnit(int unitId) =>
            _positions.TryGetValue(unitId, out var c) && _occupants.TryGetValue(c, out var u) ? u : null;

        bool IsAdjacentToEnemy(HexCoord c, BattleSideId side) =>
            c.Neighbors().Any(n => _occupants.TryGetValue(n, out var o) && _sideOf[o.Id] != side);

        /// <summary>
        /// Hexes the unit can move to this turn → remaining battle MP after the move.
        /// Friendly units can be passed through but not stopped on; enemy units block.
        /// Entering an enemy-adjacent hex (ZOC) or crossing a river ends movement.
        /// </summary>
        public Dictionary<HexCoord, int> ReachableHexes(Unit unit)
        {
            var result = new Dictionary<HexCoord, int>();
            var start = PositionOf(unit);
            if (start == null || unit.HasAttacked || IsMoveLocked(unit)) return result;

            var side = SideOf(unit);
            var best = new Dictionary<HexCoord, int> { [start.Value] = unit.BattleMovesLeft };
            var frontier = new List<HexCoord> { start.Value };

            while (frontier.Count > 0)
            {
                // Expand the hex with the most MP left (Dijkstra on remaining MP). Tiny fields → linear scan is fine.
                frontier.Sort((a, b) => best[b].CompareTo(best[a]));
                var current = frontier[0];
                frontier.RemoveAt(0);
                int mpHere = best[current];
                if (current != start.Value && IsStopHex(current, side, null)) continue;

                foreach (var next in current.Neighbors())
                {
                    if (!_tiles.Contains(next)) continue;
                    if (_occupants.TryGetValue(next, out var occ) && _sideOf[occ.Id] != side) continue;

                    int cost = TerrainRules.LandStepCost(Map.Get(current), Map.Get(next));
                    if (cost == TerrainRules.Impassable) continue;
                    // A unit with any MP left may always take one step (Civ V rule).
                    if (cost > mpHere && mpHere < unit.Def.BattleMovement) continue;

                    int remaining = Math.Max(0, mpHere - cost);
                    if (Map.HasRiverBetween(current, next)) remaining = 0;
                    if (best.TryGetValue(next, out var prev) && prev >= remaining) continue;

                    best[next] = remaining;
                    // Hexes reached with 0 MP are recorded as destinations but never expanded.
                    if (remaining > 0 && !frontier.Contains(next)) frontier.Add(next);
                }
            }

            foreach (var kv in best)
                if (kv.Key != start.Value && !_occupants.ContainsKey(kv.Key))
                    result[kv.Key] = kv.Value;
            return result;
        }

        bool IsStopHex(HexCoord c, BattleSideId side, Unit ignore) =>
            c.Neighbors().Any(n => _occupants.TryGetValue(n, out var o) && o != ignore && _sideOf[o.Id] != side);

        /// <summary>Whether <paramref name="unit"/> standing on <paramref name="from"/> could attack the unit on <paramref name="target"/>.</summary>
        public bool CanAttackFrom(Unit unit, HexCoord from, HexCoord target)
        {
            var defender = UnitAt(target);
            if (defender == null || _sideOf[defender.Id] == SideOf(unit)) return false;
            if (unit.HasAttacked) return false;
            int dist = from.DistanceTo(target);
            var fromTile = Map.Get(from);
            var targetTile = Map.Get(target);

            if (unit.Def.IsRanged)
            {
                int range = unit.Def.Range + (fromTile.Elevation > targetTile.Elevation ? 1 : 0);
                if (dist < 1 || dist > range) return false;
                return unit.Def.IndirectFire || TerrainRules.HasLineOfSight(Map, from, target);
            }

            // Melee: adjacent, and the step onto the target's hex must not be a cliff.
            return dist == 1 && TerrainRules.LandStepCost(fromTile, targetTile) != TerrainRules.Impassable;
        }

        /// <summary>Builds the combat situation for an attack from <paramref name="from"/> (actual or hypothetical).</summary>
        public CombatSituation BuildSituation(Unit attacker, HexCoord from, Unit defender)
        {
            var target = PositionOf(defender) ?? throw new InvalidOperationException($"{defender} is not deployed.");
            var side = SideOf(attacker);
            bool ranged = attacker.Def.IsRanged;
            var s = new CombatSituation
            {
                Attacker = attacker,
                Defender = defender,
                AttackerTile = Map.Get(from),
                DefenderTile = Map.Get(target),
                IsRanged = ranged,
                CrossesRiver = !ranged && Map.HasRiverBetween(from, target),
                Flankers = ranged ? 0 : target.Neighbors().Count(n =>
                    n != from && _occupants.TryGetValue(n, out var o) && o != attacker && _sideOf[o.Id] == side),
            };
            if (Side(side).Player.IsVeryUnhappy) s.AttackerExtras.Add(new CombatModifier("Unhappiness", UnhappinessPenalty));
            if (Opponent(side).Player.IsVeryUnhappy) s.DefenderExtras.Add(new CombatModifier("Unhappiness", UnhappinessPenalty));
            return s;
        }

        public CombatPreview PreviewAttack(Unit attacker, HexCoord from, Unit defender) =>
            CombatResolver.Preview(BuildSituation(attacker, from, defender));

        // ------------------------------------------------------------------ commands

        public bool TryMove(Unit unit, HexCoord dest)
        {
            if (!CanCommand(unit)) return false;
            var reachable = ReachableHexes(unit);
            if (!reachable.TryGetValue(dest, out var remaining)) return false;

            var from = PositionOf(unit).Value;
            _occupants.Remove(from);
            Place(unit, dest);
            unit.BattleMovesLeft = remaining;
            unit.ActedThisTurn = true;
            unit.Fortified = false;
            if (IsStopHex(dest, SideOf(unit), unit) || remaining == 0) _moveLocked.Add(unit.Id);
            return true;
        }

        public CombatResult TryAttack(Unit attacker, HexCoord target)
        {
            if (!CanCommand(attacker)) return null;
            var from = PositionOf(attacker).Value;
            if (!CanAttackFrom(attacker, from, target)) return null;
            if (!attacker.Def.IsRanged && attacker.BattleMovesLeft <= 0) return null;

            var defender = UnitAt(target);
            var result = CombatResolver.Resolve(BuildSituation(attacker, from, defender), _rng);
            Log.Add($"{attacker} → {defender}: {result.DamageToDefender} dmg" +
                    (result.DamageToAttacker > 0 ? $", takes {result.DamageToAttacker}" : "") +
                    $" [{result.Preview.Attacker.Total} vs {result.Preview.Defender.Total}]");

            attacker.HasAttacked = true;
            attacker.ActedThisTurn = true;
            attacker.Fortified = false;
            attacker.BattleMovesLeft = 0;

            if (result.DefenderKilled) RemoveFromField(defender);
            if (result.AttackerKilled) RemoveFromField(attacker);
            else if (result.DefenderKilled && !attacker.Def.IsRanged)
            {
                // Melee attackers advance into the vacated hex.
                _occupants.Remove(from);
                Place(attacker, target);
            }

            CheckElimination();
            return result;
        }

        /// <summary>Ends the active side's battle turn and hands over to the other side / next round.</summary>
        public void EndTurn()
        {
            RequireInProgress();
            foreach (var u in DeployedUnits(ActiveSide).ToList())
                if (!u.ActedThisTurn) u.Fortified = true;

            if (ActiveSide == BattleSideId.Attacker && Objective.HasValue)
            {
                var holder = UnitAt(Objective.Value);
                if (holder != null && _sideOf[holder.Id] == BattleSideId.Attacker)
                {
                    Finish(BattleStatus.AttackerWon, "The attacker holds the city centre.");
                    return;
                }
            }

            if (ActiveSide == BattleSideId.Attacker)
            {
                ActiveSide = BattleSideId.Defender;
                BeginSideTurn(Defender);
                return;
            }

            TurnInRound++;
            if (TurnInRound > TurnsPerRound)
            {
                EndRound();
                return;
            }
            ActiveSide = BattleSideId.Attacker;
            BeginSideTurn(Attacker);
        }

        /// <summary>Starts round 2 or 3. Called on the attacker's next world turn.</summary>
        public void BeginNextRound()
        {
            if (Status != BattleStatus.AwaitingNextRound) throw new InvalidOperationException("No round is pending.");
            Round++;
            TurnInRound = 1;
            Status = BattleStatus.InProgress;
            ActiveSide = BattleSideId.Attacker;
            Log.Add($"Round {Round} begins.");
            BeginSideTurn(Attacker);
        }

        /// <summary>
        /// The active side withdraws. Each of its units next to an enemy first takes a free hit
        /// from the strongest adjacent enemy (GDD §4.4).
        /// </summary>
        public void Retreat()
        {
            RequireInProgress();
            var side = ActiveSide;
            foreach (var unit in DeployedUnits(side).ToList())
            {
                var pos = PositionOf(unit).Value;
                var chaser = pos.Neighbors()
                    .Select(UnitAt)
                    .Where(o => o != null && _sideOf[o.Id] != side)
                    .OrderByDescending(o => o.Def.CombatStrength)
                    .FirstOrDefault();
                if (chaser == null) continue;

                var preview = CombatResolver.Preview(BuildSituation(chaser, PositionOf(chaser).Value, unit));
                int dmg = CombatResolver.RollDamage(preview.Delta, _rng);
                unit.TakeDamage(dmg);
                Log.Add($"{unit} is hit for {dmg} while disengaging.");
                if (!unit.IsAlive) RemoveFromField(unit);
            }
            Finish(side == BattleSideId.Attacker ? BattleStatus.AttackerRetreated : BattleStatus.DefenderRetreated,
                $"The {side.ToString().ToLowerInvariant()} retreats.");
        }

        // ------------------------------------------------------------------ internals

        bool CanCommand(Unit u) =>
            Status == BattleStatus.InProgress && u.IsAlive && _sideOf.TryGetValue(u.Id, out var s) && s == ActiveSide &&
            _positions.ContainsKey(u.Id);

        void BeginSideTurn(BattleSide side)
        {
            RemoveDeadFromField();
            DeployReserves(side);
            _moveLocked.RemoveWhere(id => _sideOf[id] == side.Id);
            foreach (var u in DeployedUnits(side.Id))
            {
                u.BattleMovesLeft = u.Def.BattleMovement;
                u.HasAttacked = false;
                u.ActedThisTurn = false;
            }
        }

        void EndRound()
        {
            foreach (var side in new[] { Attacker, Defender })
            {
                int heal = side.Player.Faction.HealOnRoundEnd;
                if (heal <= 0) continue;
                foreach (var u in DeployedUnits(side.Id).Concat(side.Reserve)) u.Heal(heal);
            }

            if (Round >= MaxRounds)
                Finish(BattleStatus.DefenderWon, "The defender holds after the final round.");
            else
            {
                Status = BattleStatus.AwaitingNextRound;
                Log.Add($"Round {Round} ends.");
            }
        }

        void Place(Unit u, HexCoord c)
        {
            _positions[u.Id] = c;
            _occupants[c] = u;
        }

        void RemoveFromField(Unit u)
        {
            if (_positions.TryGetValue(u.Id, out var c))
            {
                _positions.Remove(u.Id);
                if (_occupants.TryGetValue(c, out var occ) && occ == u) _occupants.Remove(c);
            }
            _moveLocked.Remove(u.Id);
            Log.Add($"{u.Def.Name}#{u.Id} is destroyed.");
        }

        /// <summary>Clears units killed outside a battle attack (attrition, air strikes, scripted damage).</summary>
        void RemoveDeadFromField()
        {
            foreach (var u in _occupants.Values.Where(u => !u.IsAlive).ToList()) RemoveFromField(u);
        }

        void CheckElimination()
        {
            if (IsFinished) return;
            RemoveDeadFromField();
            bool attackerAlive = DeployedUnits(BattleSideId.Attacker).Any() || Attacker.Reserve.Any(u => u.IsAlive);
            bool defenderAlive = DeployedUnits(BattleSideId.Defender).Any() || Defender.Reserve.Any(u => u.IsAlive);
            if (!defenderAlive) Finish(BattleStatus.AttackerWon, "The defenders are destroyed.");
            else if (!attackerAlive) Finish(BattleStatus.DefenderWon, "The attackers are destroyed.");
        }

        void Finish(BattleStatus status, string reason)
        {
            Status = status;
            Log.Add($"{reason} Result: {status}.");
        }

        void RequireInProgress()
        {
            if (Status != BattleStatus.InProgress) throw new InvalidOperationException($"Battle is {Status}.");
        }
    }
}
