using System;
using System.Collections.Generic;
using System.Linq;
using Crucible.Core.Content;
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

        /// <summary>Before round 1: the defender, then the attacker, may rearrange units inside their zone.</summary>
        Deploying,
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

        /// <summary>Reinforcements march in from where their army stood: unit id → world hex it came from.</summary>
        readonly Dictionary<int, HexCoord> _entryPoints = new Dictionary<int, HexCoord>();

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
            DeterministicRng rng, HexCoord? objective = null, int objectiveCityId = -1)
        {
            Id = id;
            Map = map ?? throw new ArgumentNullException(nameof(map));
            _tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
            Attacker = attacker ?? throw new ArgumentNullException(nameof(attacker));
            Defender = defender ?? throw new ArgumentNullException(nameof(defender));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            Objective = objective;
            ObjectiveCityId = objectiveCityId;

            if (objective.HasValue && Map.Get(objective.Value)?.WallTier is int tier && tier > 0)
            {
                WallTier = tier;
                MaxWallHp = WallHpPerTier * tier;
                WallHp = MaxWallHp;
            }
        }

        // ------------------------------------------------------------------ save / load

        /// <summary>Writes everything that changes during a battle (the rest is rebuilt from the constructor).</summary>
        internal void WriteState(Game.SaveWriter w)
        {
            w.Int(Round); w.Int(TurnInRound); w.Int((int)ActiveSide); w.Int((int)Status); w.Bool(Started);
            w.Int(WallTier); w.Int(MaxWallHp); w.Int(WallHp);
            foreach (var side in new[] { Attacker, Defender })
            {
                w.Ints(side.Armies.Select(a => a.Id));
                w.Hexes(side.DeploymentZone);
                w.Ints(side.Reserve.Select(u => u.Id));
            }
            w.Int(_positions.Count);
            foreach (var kv in _positions.OrderBy(kv => kv.Key)) { w.Int(kv.Key); w.Hex(kv.Value); }
            w.Int(_sideOf.Count);
            foreach (var kv in _sideOf.OrderBy(kv => kv.Key)) { w.Int(kv.Key); w.Int((int)kv.Value); }
            w.Ints(_moveLocked.OrderBy(x => x));
            w.Int(_entryPoints.Count);
            foreach (var kv in _entryPoints.OrderBy(kv => kv.Key)) { w.Int(kv.Key); w.Hex(kv.Value); }
            foreach (var side in new[] { BattleSideId.Attacker, BattleSideId.Defender })
            {
                w.Ints(_air[side].Select(u => u.Id));
                w.Int(_airStrikeRound[side]);
                w.Int(_damageDealt[side]);
            }
            w.Ints(_interceptedThisRound.OrderBy(x => x));
            w.Int(_interceptRound);
            w.Strings(Log);
        }

        internal void ReadState(Game.SaveReader r, Func<int, Unit> unit, Func<int, Army> army)
        {
            Round = r.Int(); TurnInRound = r.Int(); ActiveSide = (BattleSideId)r.Int(); Status = (BattleStatus)r.Int(); Started = r.Bool();
            WallTier = r.Int(); MaxWallHp = r.Int(); WallHp = r.Int();
            foreach (var side in new[] { Attacker, Defender })
            {
                foreach (var id in r.Ints()) side.Armies.Add(army(id));
                side.DeploymentZone = r.Hexes();
                foreach (var id in r.Ints()) side.Reserve.Add(unit(id));
            }
            for (int i = r.Int(); i > 0; i--) Place(unit(r.Int()), r.Hex());
            for (int i = r.Int(); i > 0; i--) _sideOf[r.Int()] = (BattleSideId)r.Int();
            foreach (var id in r.Ints()) _moveLocked.Add(id);
            for (int i = r.Int(); i > 0; i--) _entryPoints[r.Int()] = r.Hex();
            foreach (var side in new[] { BattleSideId.Attacker, BattleSideId.Defender })
            {
                _air[side] = r.Ints().Select(unit).Where(u => u != null).ToList();
                _airStrikeRound[side] = r.Int();
                _damageDealt[side] = r.Int();
            }
            foreach (var id in r.Ints()) _interceptedThisRound.Add(id);
            _interceptRound = r.Int();
            Log.AddRange(r.Strings());
        }

        // ------------------------------------------------------------------ air support (GDD §4.7)

        readonly Dictionary<BattleSideId, List<Unit>> _air = new Dictionary<BattleSideId, List<Unit>>
        {
            [BattleSideId.Attacker] = new List<Unit>(),
            [BattleSideId.Defender] = new List<Unit>(),
        };
        readonly Dictionary<BattleSideId, int> _airStrikeRound = new Dictionary<BattleSideId, int>
        {
            [BattleSideId.Attacker] = 0,
            [BattleSideId.Defender] = 0,
        };
        readonly HashSet<int> _interceptedThisRound = new HashSet<int>();
        int _interceptRound;

        /// <summary>Aircraft supporting a side this round (set by the game from city hangars in range).</summary>
        public void SetAirSupport(BattleSideId side, IEnumerable<Unit> aircraft) =>
            _air[side] = aircraft.Where(u => u.IsAlive && u.Def.Domain == UnitDomain.Air).ToList();

        public IReadOnlyList<Unit> AirSupport(BattleSideId side) => _air[side];

        /// <summary>
        /// Each supporting aircraft strikes once per round, at its side's first turn: the enemy's fighters
        /// may intercept it first (each fighter once per round). Survivors hit the ground unit they
        /// expect to hurt most.
        /// </summary>
        void RunAirStrikes(BattleSideId side)
        {
            if (_airStrikeRound[side] == Round) return;
            _airStrikeRound[side] = Round;
            if (_interceptRound != Round)
            {
                _interceptedThisRound.Clear();
                _interceptRound = Round;
            }

            var enemy = side == BattleSideId.Attacker ? BattleSideId.Defender : BattleSideId.Attacker;
            foreach (var plane in _air[side].Where(u => u.IsAlive).ToList())
            {
                var interceptor = _air[enemy].FirstOrDefault(f => f.IsAlive && f.Def.Class == UnitClass.Fighter && !_interceptedThisRound.Contains(f.Id));
                if (interceptor != null)
                {
                    _interceptedThisRound.Add(interceptor.Id);
                    int dmg = CombatResolver.RollDamage(interceptor.Def.RangedStrength - CombatResolver.WoundPenalty(interceptor) - plane.Def.CombatStrength, _rng);
                    plane.TakeDamage(dmg);
                    Log.Add($"{interceptor} intercepts {plane}: {dmg} dmg" + (plane.IsAlive ? "." : " — shot down!"));
                    if (!plane.IsAlive) continue;
                }

                var target = DeployedUnits(enemy)
                    .OrderByDescending(t => Math.Min(t.Hp, CombatResolver.ExpectedDamage(AirStrikeDelta(plane, t))) + (CombatResolver.ExpectedDamage(AirStrikeDelta(plane, t)) >= t.Hp ? 50 : 0))
                    .ThenBy(t => t.Id)
                    .FirstOrDefault();
                if (target == null) break;
                int hit = CombatResolver.RollDamage(AirStrikeDelta(plane, target), _rng);
                target.TakeDamage(hit);
                Log.Add($"{plane} strikes {target} for {hit}.");
                if (!target.IsAlive) RemoveFromField(target);
            }
            CheckElimination();
        }

        int AirStrikeDelta(Unit plane, Unit target)
        {
            int attack = plane.Def.RangedStrength - CombatResolver.WoundPenalty(plane);
            int defence = target.Def.CombatStrength - CombatResolver.WoundPenalty(target) + (target.Fortified ? CombatResolver.FortifiedBonus : 0);
            return attack - defence;
        }

        // ------------------------------------------------------------------ walls (GDD §4.6)

        public const int WallHpPerTier = 50;

        /// <summary>City whose centre is <see cref="Objective"/>; its militia fight only here.</summary>
        public int ObjectiveCityId { get; }

        public int WallTier { get; private set; }
        public int MaxWallHp { get; private set; }
        public int WallHp { get; private set; }
        public bool HasWalls => MaxWallHp > 0;
        public bool WallsIntact => WallHp > 0;

        /// <summary>Defensive strength the walls oppose to battering: 10 + 8 per tier.</summary>
        public int WallStrength => 10 + 8 * WallTier;

        /// <summary>
        /// Intact walls stop attacking melee units from entering or striking the city centre, unless
        /// they step off a hex next to a friendly siege tower (or are the tower).
        /// </summary>
        public bool BlockedByWalls(Unit unit, HexCoord from, HexCoord to)
        {
            if (!WallsIntact || !Objective.HasValue || to != Objective.Value) return false;
            if (SideOf(unit) != BattleSideId.Attacker || unit.Def.IsRanged || unit.Def.CarriesOverWalls) return false;
            return !from.Neighbors().Any(n => _occupants.TryGetValue(n, out var o) && o != unit &&
                                              o.Def.CarriesOverWalls && _sideOf[o.Id] == BattleSideId.Attacker);
        }

        /// <summary>Whether the unit could batter or bombard the walls from <paramref name="from"/>.</summary>
        public bool CanAttackWallsFrom(Unit unit, HexCoord from)
        {
            if (!WallsIntact || !Objective.HasValue || unit.HasAttacked || !unit.Def.CanAttack) return false;
            if (SideOf(unit) != BattleSideId.Attacker) return false;
            int dist = from.DistanceTo(Objective.Value);
            if (unit.Def.IsRanged)
            {
                int range = unit.Def.Range + (Map.Get(from).Elevation > Map.Get(Objective.Value).Elevation ? 1 : 0);
                return dist >= 1 && dist <= range && (unit.Def.IndirectFire || TerrainRules.HasLineOfSight(Map, from, Objective.Value));
            }
            return dist == 1 && unit.Def.Domain == UnitDomain.Land;
        }

        public double ExpectedWallDamage(Unit unit)
        {
            int strength = (unit.Def.IsRanged ? unit.Def.RangedStrength : unit.Def.CombatStrength) - CombatResolver.WoundPenalty(unit);
            return Math.Min(MaxWallHp, CombatResolver.ExpectedDamage(strength - WallStrength) * unit.Def.WallDamageMultiplier);
        }

        /// <summary>Attacks the walls. Returns the damage dealt, or -1 if the attack is not allowed.</summary>
        public int TryAttackWalls(Unit unit)
        {
            if (!CanCommand(unit)) return -1;
            var from = PositionOf(unit).Value;
            if (!CanAttackWallsFrom(unit, from)) return -1;
            if (!unit.Def.IsRanged && unit.BattleMovesLeft <= 0) return -1;

            int strength = (unit.Def.IsRanged ? unit.Def.RangedStrength : unit.Def.CombatStrength) - CombatResolver.WoundPenalty(unit);
            int dmg = (int)Math.Round(CombatResolver.RollDamage(strength - WallStrength, _rng) * unit.Def.WallDamageMultiplier);
            WallHp = Math.Max(0, WallHp - dmg);
            unit.HasAttacked = true;
            unit.ActedThisTurn = true;
            unit.Fortified = false;
            unit.BattleMovesLeft = 0;
            Log.Add($"{unit} batters the walls for {dmg} ({WallHp}/{MaxWallHp})." + (WallsIntact ? "" : " The walls are breached!"));
            return dmg;
        }

        public bool IsFinished =>
            Status == BattleStatus.AttackerWon || Status == BattleStatus.DefenderWon ||
            Status == BattleStatus.AttackerRetreated || Status == BattleStatus.DefenderRetreated;

        /// <summary>True while a side must act (deploy or play a battle turn).</summary>
        public bool AwaitingAction => Status == BattleStatus.InProgress || Status == BattleStatus.Deploying;

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
            // Civilians (settlers, workers) never take the field; they share their army's fate.
            // Militia only fight for their own city.
            foreach (var u in army.Units.Where(u => u.IsAlive && u.Def.IsMilitary &&
                                                     (u.BoundToCityId < 0 || u.BoundToCityId == ObjectiveCityId)))
            {
                _sideOf[u.Id] = sideId;
                side.Reserve.Add(u);
                if (Started) _entryPoints[u.Id] = army.Position;
            }
            if (Started) Log.Add($"{army} joins the {sideId.ToString().ToLowerInvariant()} as reinforcements.");
        }

        /// <summary>
        /// Computes deployment zones and auto-deploys both sides (defender first: it chose the ground).
        /// With <paramref name="deploymentPhase"/>, each side then gets to rearrange its units
        /// (<see cref="Redeploy"/>, <see cref="ConfirmDeployment"/>) before round 1 begins.
        /// </summary>
        public void Start(bool deploymentPhase = false)
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
            if (Status != BattleStatus.InProgress) return;
            if (deploymentPhase)
            {
                Status = BattleStatus.Deploying;
                ActiveSide = BattleSideId.Defender;
            }
            else BeginSideTurn(Attacker);
        }

        /// <summary>During deployment: moves a unit to a hex of its zone, swapping with a friendly unit there.</summary>
        public bool Redeploy(Unit unit, HexCoord hex)
        {
            if (Status != BattleStatus.Deploying || !_sideOf.TryGetValue(unit.Id, out var sideId) || sideId != ActiveSide) return false;
            var from = PositionOf(unit);
            if (from == null || from.Value == hex || !Side(sideId).DeploymentZone.Contains(hex)) return false;

            var other = UnitAt(hex);
            if (other != null && _sideOf[other.Id] != sideId) return false;
            _occupants.Remove(from.Value);
            Place(unit, hex);
            if (other != null) Place(other, from.Value);
            return true;
        }

        /// <summary>Ends the active side's deployment. After the attacker confirms, round 1 starts.</summary>
        public void ConfirmDeployment()
        {
            if (Status != BattleStatus.Deploying) throw new InvalidOperationException("Not in deployment.");
            if (ActiveSide == BattleSideId.Defender)
            {
                ActiveSide = BattleSideId.Attacker;
                return;
            }
            Status = BattleStatus.InProgress;
            Log.Add("Deployment complete. Round 1 begins.");
            BeginSideTurn(Attacker);
        }

        /// <summary>
        /// Fills free frontline slots from the reserve. Units of the original armies use the deployment
        /// zone (front-liners nearest the enemy); reinforcements enter at the field edge nearest to where
        /// their army stood.
        /// </summary>
        void DeployReserves(BattleSide side)
        {
            int slots = side.FrontlineCap - DeployedUnits(side.Id).Count();
            foreach (var unit in side.Reserve.Where(u => u.IsAlive && _entryPoints.ContainsKey(u.Id)).ToList())
            {
                if (slots <= 0) break;
                var entry = _entryPoints[unit.Id];
                var hex = _tiles
                    .Where(t => !_occupants.ContainsKey(t) && CanStand(unit, t))
                    .OrderBy(t => IsAdjacentToEnemy(t, side.Id) ? 1 : 0)
                    .ThenBy(t => t.DistanceTo(entry)).ThenBy(t => t.Q).ThenBy(t => t.R)
                    .Select(t => (HexCoord?)t)
                    .FirstOrDefault();
                if (!hex.HasValue) break;
                Place(unit, hex.Value);
                side.Reserve.Remove(unit);
                _entryPoints.Remove(unit.Id);
                unit.Fortified = false;
                slots--;
            }

            // In an assault the garrison's best non-ranged unit holds the city centre behind the walls.
            if (side.Id == BattleSideId.Defender && Objective.HasValue && slots > 0 && !_occupants.ContainsKey(Objective.Value))
            {
                var holder = side.Reserve.Where(u => u.IsAlive && !_entryPoints.ContainsKey(u.Id) && CanStand(u, Objective.Value))
                    .OrderBy(u => u.Def.IsRanged ? 1 : 0).ThenByDescending(u => u.Def.CombatStrength).ThenBy(u => u.Id)
                    .FirstOrDefault();
                if (holder != null)
                {
                    Place(holder, Objective.Value);
                    side.Reserve.Remove(holder);
                    holder.Fortified = false;
                    slots--;
                }
            }

            // Front-liners take the zone hexes nearest the enemy, ranged units the ones behind. Each unit
            // only takes hexes of its own domain (ships on water, troops on land); if its zone has none
            // left, it takes the nearest free hex of its domain on its own half of the field.
            var enemyOrigin = Opponent(side.Id).Origin;
            var zoneOrder = side.DeploymentZone
                .OrderBy(t => t.DistanceTo(enemyOrigin)).ThenBy(t => t.Q).ThenBy(t => t.R)
                .ToList();
            var queue = side.Reserve
                .Where(u => u.IsAlive && !_entryPoints.ContainsKey(u.Id))
                .OrderBy(u => IsBackline(u) ? 1 : 0)
                .ToList();

            foreach (var unit in queue)
            {
                if (slots <= 0) break;
                HexCoord? hex = zoneOrder.Where(t => !_occupants.ContainsKey(t) && CanStand(unit, t))
                    .Select(t => (HexCoord?)t).FirstOrDefault();
                if (!hex.HasValue)
                    hex = _tiles.Where(t => !_occupants.ContainsKey(t) && CanStand(unit, t) &&
                                            t.DistanceTo(side.Origin) <= t.DistanceTo(enemyOrigin))
                        .OrderBy(t => IsAdjacentToEnemy(t, side.Id) ? 1 : 0)
                        .ThenBy(t => t.DistanceTo(side.Origin)).ThenBy(t => t.Q).ThenBy(t => t.R)
                        .Select(t => (HexCoord?)t).FirstOrDefault();
                if (!hex.HasValue) continue;
                Place(unit, hex.Value);
                side.Reserve.Remove(unit);
                unit.Fortified = false;
                slots--;
            }
            side.Reserve.RemoveAll(u => !u.IsAlive);
        }

        static bool IsBackline(Unit u) => u.Def.IsRanged;

        /// <summary>Battle movement: ships on water, troops on land. No embarking mid-battle.</summary>
        public static Mobility MobilityOf(Unit u) => u.Def.Domain == UnitDomain.Naval ? Mobility.Ship : Mobility.Land;

        bool CanStand(Unit u, HexCoord c) => TerrainRules.CanStand(MobilityOf(u), Map.Get(c));

        // ------------------------------------------------------------------ queries

        public IEnumerable<Unit> DeployedUnits(BattleSideId side) =>
            _positions.Keys.OrderBy(id => id).Select(FindUnit).Where(u => u != null && _sideOf[u.Id] == side);

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

                    int cost = TerrainRules.StepCost(MobilityOf(unit), Map.Get(current), Map.Get(next));
                    if (cost == TerrainRules.Impassable || BlockedByWalls(unit, current, next)) continue;
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
            if (unit.HasAttacked || !unit.Def.CanAttack || unit.Def.AttacksWallsOnly) return false;
            if (BlockedByWalls(unit, from, target)) return false;
            int dist = from.DistanceTo(target);
            var fromTile = Map.Get(from);
            var targetTile = Map.Get(target);

            if (unit.Def.IsRanged)
            {
                int range = unit.Def.Range + (fromTile.Elevation > targetTile.Elevation ? 1 : 0);
                if (dist < 1 || dist > range) return false;
                return unit.Def.IndirectFire || TerrainRules.HasLineOfSight(Map, from, target);
            }

            // Melee: adjacent, and the attacker could step onto the target's hex (no cliffs, same domain).
            return dist == 1 && TerrainRules.StepCost(MobilityOf(unit), fromTile, targetTile) != TerrainRules.Impassable;
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
                WallsBreached = HasWalls && !WallsIntact && Objective == target,
            };
            if (HasGeneral(side)) s.AttackerExtras.Add(new CombatModifier("Great General", GeneralBonus));
            if (HasGeneral(Opponent(side).Id)) s.DefenderExtras.Add(new CombatModifier("Great General", GeneralBonus));
            int atkPolicy = PolicyCombatBonus(Side(side).Player), defPolicy = PolicyCombatBonus(Opponent(side).Player);
            if (atkPolicy != 0) s.AttackerExtras.Add(new CombatModifier("Policies", atkPolicy));
            if (defPolicy != 0) s.DefenderExtras.Add(new CombatModifier("Policies", defPolicy));
            if (Side(side).Player.IsVeryUnhappy) s.AttackerExtras.Add(new CombatModifier("Unhappiness", UnhappinessPenalty));
            if (Opponent(side).Player.IsVeryUnhappy) s.DefenderExtras.Add(new CombatModifier("Unhappiness", UnhappinessPenalty));
            return s;
        }

        public const int GeneralBonus = 3;

        /// <summary>A Great General rides with one of the side's armies.</summary>
        public bool HasGeneral(BattleSideId side) =>
            Side(side).Armies.Any(a => a.Units.Any(u => u.IsAlive && u.Def.GreatPerson == GreatPersonType.General));

        readonly Dictionary<BattleSideId, int> _damageDealt = new Dictionary<BattleSideId, int>
        {
            [BattleSideId.Attacker] = 0,
            [BattleSideId.Defender] = 0,
        };

        /// <summary>Total damage a side has inflicted on enemy units (feeds Great General points).</summary>
        public int DamageDealt(BattleSideId side) => _damageDealt[side];

        /// <summary>Set by the game from content: policy id → CS bonus (keeps Battle free of the content DB).</summary>
        public Func<string, int> PolicyStrength { get; set; }

        int PolicyCombatBonus(Player p) => PolicyStrength == null ? 0 : p.Policies.Sum(PolicyStrength);

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
            _damageDealt[SideOf(attacker)] += result.DamageToDefender;
            _damageDealt[SideOf(defender)] += result.DamageToAttacker;
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
            if (TurnInRound == 1)
            {
                RunAirStrikes(side.Id);
                if (Status != BattleStatus.InProgress) return;
            }
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
