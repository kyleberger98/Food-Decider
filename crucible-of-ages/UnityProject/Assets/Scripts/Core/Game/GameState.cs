using System;
using System.Collections.Generic;
using System.Linq;
using Crucible.Core.AI;
using Crucible.Core.Combat;
using Crucible.Core.Content;
using Crucible.Core.Economy;
using Crucible.Core.Empire;
using Crucible.Core.Hex;
using Crucible.Core.Random;
using Crucible.Core.Units;
using Crucible.Core.World;

namespace Crucible.Core.Game
{
    /// <summary>
    /// The whole simulation. The Unity layer reads it and calls these commands; nothing else
    /// mutates state. Everything random draws from <see cref="Rng"/>, so a seed + command log
    /// replays a game exactly.
    /// </summary>
    public sealed partial class GameState
    {
        public const int DefaultTurnLimit = 500;

        readonly List<Player> _players = new List<Player>();
        readonly Dictionary<int, Army> _armies = new Dictionary<int, Army>();
        readonly Dictionary<int, City> _cities = new Dictionary<int, City>();
        readonly Dictionary<int, Battle> _battles = new Dictionary<int, Battle>();
        readonly Dictionary<int, PlayerVisibility> _visibility = new Dictionary<int, PlayerVisibility>();
        int _nextUnitId = 1, _nextArmyId = 1, _nextCityId = 1, _nextBattleId = 1;

        public ContentDatabase Content { get; }
        public WorldMap Map { get; }
        public DeterministicRng Rng { get; }
        public TacticalBattleAI BattleAI { get; } = new TacticalBattleAI();

        public int Turn { get; internal set; } = 1;
        public int TurnLimit { get; set; } = DefaultTurnLimit;
        public VictoryResult Victory { get; internal set; }

        public event Action<Battle> BattleStarted;
        public event Action<Battle> BattleEnded;

        public GameState(ContentDatabase content, WorldMap map, ulong seed)
        {
            Content = content ?? throw new ArgumentNullException(nameof(content));
            Map = map ?? throw new ArgumentNullException(nameof(map));
            Rng = new DeterministicRng(seed);
        }

        public IReadOnlyList<Player> Players => _players;
        public IEnumerable<Army> Armies => _armies.Values;
        public IEnumerable<City> Cities => _cities.Values;
        public IEnumerable<Battle> Battles => _battles.Values;

        public Player Player(int id) => _players[id];
        public PlayerVisibility Visibility(int playerId) => _visibility[playerId];
        public Army Army(int id) => _armies.TryGetValue(id, out var a) ? a : null;
        public City City(int id) => _cities.TryGetValue(id, out var c) ? c : null;
        public Battle Battle(int id) => _battles.TryGetValue(id, out var b) ? b : null;

        public Army ArmyAt(HexCoord c) => _armies.Values.FirstOrDefault(a => a.Position == c);
        public City CityAt(HexCoord c) => _cities.Values.FirstOrDefault(x => x.Position == c);

        /// <summary>A hex inside an ongoing battle can only be entered by joining that battle.</summary>
        public Battle BattleCovering(HexCoord c) => _battles.Values.FirstOrDefault(b => !b.IsFinished && b.Contains(c));

        // ------------------------------------------------------------------ setup

        public Player AddPlayer(string name, string factionId, bool isAI)
        {
            var p = new Player(_players.Count, name, Content.Faction(factionId), isAI, Content);
            _players.Add(p);
            _visibility[p.Id] = new PlayerVisibility();
            return p;
        }

        public Unit CreateUnit(string unitDefId, int ownerId) => new Unit(_nextUnitId++, Content.Unit(unitDefId), ownerId);

        public Army CreateArmy(int ownerId, HexCoord position, params string[] unitDefIds)
        {
            var tile = Map.Get(position) ?? throw new ArgumentException($"{position} is off the map.");
            if (!tile.IsPassableForLand) throw new InvalidOperationException($"{position} is not passable land.");
            if (ArmyAt(position) != null) throw new InvalidOperationException($"{position} already holds an army.");

            var army = new Army(_nextArmyId++, ownerId, position);
            int cap = Player(ownerId).ArmyCap;
            foreach (var id in unitDefIds)
                if (!army.TryAdd(CreateUnit(id, ownerId), cap))
                    throw new InvalidOperationException($"Army exceeds cap of {cap}.");
            _armies[army.Id] = army;
            army.WorldMovesLeft = WorldMovementOf(army);
            RefreshVisibility(ownerId);
            return army;
        }

        public City FoundCity(int ownerId, HexCoord position, string name, bool isCapital)
        {
            var tile = Map.Get(position) ?? throw new ArgumentException($"{position} is off the map.");
            if (tile.HasCity) throw new InvalidOperationException($"{position} already has a city.");
            var city = new City(_nextCityId++, name, ownerId, position) { IsOriginalCapital = isCapital };
            _cities[city.Id] = city;
            tile.CityId = city.Id;
            tile.OwnerPlayerId = ownerId;
            if (isCapital) tile.WallTier = Math.Max(tile.WallTier, 1); // palace defences
            ClaimInitialTerritory(city);
            RefreshVisibility(ownerId);
            return city;
        }

        public int WorldMovementOf(Army army)
        {
            var faction = Player(army.OwnerId).Faction;
            return army.MaxWorldMovement(u => u.Def.Class == UnitClass.Mounted ? faction.MountedWorldMovementBonus : 0);
        }

        public bool AtWar(int a, int b) => a != b; // TODO(M8): diplomacy — for now everyone is at war.

        // ------------------------------------------------------------------ world-map commands

        /// <summary>Moves an army one hex. Pathfinding over several hexes is issued as repeated steps.</summary>
        public bool MoveArmy(Army army, HexCoord dest)
        {
            if (army.InBattle || army.WorldMovesLeft <= 0) return false;
            if (army.Position.DistanceTo(dest) != 1) return false;
            if (ArmyAt(dest) != null || BattleCovering(dest) != null) return false;
            var city = CityAt(dest);
            if (city != null && AtWar(city.OwnerId, army.OwnerId)) return false; // cities are taken by assault

            int cost = TerrainRules.LandStepCost(Map.Get(army.Position), Map.Get(dest));
            if (cost == TerrainRules.Impassable) return false;

            // Militia never leave their city: they stay behind as its garrison.
            var bound = army.Units.Where(u => u.BoundToCityId >= 0).ToList();
            if (bound.Count == army.Count) return false;
            if (bound.Count > 0)
            {
                var stay = new Army(_nextArmyId++, army.OwnerId, army.Position);
                foreach (var u in bound)
                {
                    army.Remove(u);
                    stay.TryAdd(u, int.MaxValue);
                }
                _armies[stay.Id] = stay;
            }

            bool crossesRiver = Map.HasRiverBetween(army.Position, dest);
            army.BuildOrder = ImprovementType.None;
            army.Position = dest;
            army.WorldMovesLeft = Math.Max(0, army.WorldMovesLeft - cost);
            if (crossesRiver || InEnemyZoc(dest, army.OwnerId)) army.WorldMovesLeft = 0;
            RefreshVisibility(army.OwnerId);
            return true;
        }

        /// <summary>
        /// Gives the army a standing order to walk to <paramref name="dest"/> (possibly over several
        /// turns) and moves it as far as it can this turn. Returns the planned path, or null if unreachable.
        /// </summary>
        public ArmyPath OrderMove(Army army, HexCoord dest)
        {
            if (army.InBattle) return null;
            var path = Pathfinder.Find(this, army, dest);
            if (path == null) return null;
            army.Destination = dest;
            ContinueMoveOrder(army);
            return path;
        }

        /// <summary>Follows the army's standing order with the moves it has left. Re-plans around new obstacles.</summary>
        public void ContinueMoveOrder(Army army)
        {
            if (!army.Destination.HasValue) return;
            for (int replans = 0; replans < 2 && army.WorldMovesLeft > 0 && !army.InBattle; replans++)
            {
                var path = Pathfinder.Find(this, army, army.Destination.Value);
                if (path == null) break;
                foreach (var step in path.Steps)
                {
                    if (army.WorldMovesLeft <= 0) return;
                    if (!MoveArmy(army, step)) break; // blocked since planning: re-plan
                }
                if (army.Position == army.Destination) break;
            }
            if (army.Position == army.Destination || army.WorldMovesLeft > 0) army.Destination = null;
        }

        /// <summary>Recomputes one player's fog of war from their armies and cities.</summary>
        public void RefreshVisibility(int playerId)
        {
            var sources = _armies.Values.Where(a => a.OwnerId == playerId)
                .Select(a => (a.Position, PlayerVisibility.SightOf(a, Map)))
                .Concat(_cities.Values.Where(c => c.OwnerId == playerId)
                    .Select(c => (c.Position, PlayerVisibility.BaseSight)));
            _visibility[playerId].Recompute(Map, sources);
        }

        void RefreshAllVisibility()
        {
            foreach (var p in _players) RefreshVisibility(p.Id);
        }

        static bool HasMilitary(Army army) => army.Units.Any(u => u.Def.IsMilitary && u.IsAlive);

        public bool InEnemyZoc(HexCoord c, int playerId) =>
            c.Neighbors().Any(n => ArmyAt(n) is Army a && AtWar(a.OwnerId, playerId));

        /// <summary>Moves as many units as fit from <paramref name="source"/> into an adjacent or co-located friendly army.</summary>
        public int MergeArmies(Army source, Army target)
        {
            if (source == target || source.OwnerId != target.OwnerId || source.InBattle || target.InBattle) return 0;
            if (source.Position.DistanceTo(target.Position) > 1 || source.WorldMovesLeft <= 0) return 0;

            int cap = Player(target.OwnerId).ArmyCap, moved = 0;
            foreach (var u in source.Units.ToList())
            {
                if (!target.TryAdd(u, cap)) break;
                source.Remove(u);
                moved++;
            }
            target.WorldMovesLeft = Math.Min(target.WorldMovesLeft, source.WorldMovesLeft);
            if (source.IsEmpty) _armies.Remove(source.Id);
            RefreshVisibility(target.OwnerId);
            return moved;
        }

        /// <summary>Detaches the given units into a new army on an adjacent free hex.</summary>
        public Army SplitArmy(Army army, IEnumerable<Unit> units, HexCoord dest)
        {
            var list = units.ToList();
            if (army.InBattle || list.Count == 0 || list.Count >= army.Count) return null;
            if (list.Any(u => !army.Units.Contains(u))) return null;
            if (army.Position.DistanceTo(dest) != 1 || ArmyAt(dest) != null || BattleCovering(dest) != null) return null;
            if (TerrainRules.LandStepCost(Map.Get(army.Position), Map.Get(dest)) == TerrainRules.Impassable) return null;

            var split = new Army(_nextArmyId++, army.OwnerId, dest);
            int cap = Player(army.OwnerId).ArmyCap;
            foreach (var u in list)
            {
                army.Remove(u);
                split.TryAdd(u, cap);
            }
            split.WorldMovesLeft = 0;
            _armies[split.Id] = split;
            RefreshVisibility(army.OwnerId);
            return split;
        }

        // ------------------------------------------------------------------ battles & sieges

        /// <summary>
        /// Attacks the adjacent hex: an enemy army (field battle) or an enemy city (siege assault).
        /// Builds the battlefield, pulls in nearby armies as reinforcements, and starts round 1.
        /// </summary>
        public Battle Attack(Army attacker, HexCoord target)
        {
            if (attacker.InBattle || attacker.WorldMovesLeft <= 0) return null;
            if (attacker.Position.DistanceTo(target) != 1 || !HasMilitary(attacker)) return null;

            var city = CityAt(target);
            var defenderArmy = ArmyAt(target);
            bool isSiege = city != null && AtWar(city.OwnerId, attacker.OwnerId);
            if (!isSiege && (defenderArmy == null || !AtWar(defenderArmy.OwnerId, attacker.OwnerId))) return null;
            if (defenderArmy != null && defenderArmy.InBattle)
            {
                // Attacking an army that is already fighting: march into that battle instead.
                var ongoing = Battle(defenderArmy.BattleId);
                return ongoing != null && JoinBattle(attacker, ongoing) ? ongoing : null;
            }

            int defenderId = isSiege ? city.OwnerId : defenderArmy.OwnerId;
            if (isSiege)
            {
                if (!city.IsBesieged) BeginSiege(city, attacker);
                defenderArmy = ArmyAt(target);
            }

            int radius = isSiege ? Battlefield.SiegeRadius : Battlefield.FieldRadius;
            var tiles = Battlefield.Generate(Map, attacker.Position, target, radius);
            var battle = new Battle(_nextBattleId++, Map, tiles,
                new BattleSide(BattleSideId.Attacker, Player(attacker.OwnerId), attacker.Position),
                new BattleSide(BattleSideId.Defender, Player(defenderId), target),
                Rng, isSiege ? target : (HexCoord?)null, isSiege ? city.Id : -1);

            battle.PolicyStrength = id => Content.Policy(id).CombatStrengthBonus;
            battle.AddArmy(BattleSideId.Attacker, attacker);
            if (defenderArmy != null) battle.AddArmy(BattleSideId.Defender, defenderArmy);
            PullInReinforcements(battle);

            _battles[battle.Id] = battle;
            attacker.WorldMovesLeft = 0;
            attacker.Destination = null;
            battle.Start(deploymentPhase: true);
            BattleStarted?.Invoke(battle);
            AdvanceAIBattleTurns(battle);
            return battle;
        }

        /// <summary>
        /// Sends an army into an ongoing battle on its owner's side (GDD §4.3). It must stand inside or
        /// next to the battlefield and have moves left; its units enter from its edge of the field.
        /// </summary>
        public bool JoinBattle(Army army, Battle battle)
        {
            if (army.InBattle || !HasMilitary(army) || army.WorldMovesLeft <= 0 || battle.IsFinished) return false;
            if (!battle.Contains(army.Position) && !army.Position.Neighbors().Any(battle.Contains)) return false;

            BattleSideId side;
            if (army.OwnerId == battle.Attacker.Player.Id) side = BattleSideId.Attacker;
            else if (army.OwnerId == battle.Defender.Player.Id) side = BattleSideId.Defender;
            else return false; // TODO(M8): allies join via defensive pacts

            battle.AddArmy(side, army);
            army.WorldMovesLeft = 0;
            army.Destination = null;
            return true;
        }

        /// <summary>Lets the tactical AI finish the current round for every side, humans included.</summary>
        public void AutoResolveRound(Battle battle)
        {
            BattleAI.PlayUntilPause(battle);
            if (battle.IsFinished) ResolveBattle(battle);
        }

        /// <summary>Armies inside or adjacent to the battlefield join the side of their owner.</summary>
        void PullInReinforcements(Battle battle)
        {
            foreach (var army in _armies.Values.ToList())
            {
                if (army.InBattle || !HasMilitary(army)) continue;
                bool near = battle.Contains(army.Position) || army.Position.Neighbors().Any(battle.Contains);
                if (!near) continue;
                if (army.OwnerId == battle.Attacker.Player.Id) battle.AddArmy(BattleSideId.Attacker, army);
                else if (army.OwnerId == battle.Defender.Player.Id) battle.AddArmy(BattleSideId.Defender, army);
            }
        }

        /// <summary>
        /// Starts a siege (GDD §4.6): the city stops growing and raises militia on its centre hex.
        /// </summary>
        public void BeginSiege(City city, Army besieger)
        {
            if (city.IsBesieged) return;
            if (besieger.Position.DistanceTo(city.Position) != 1 || !AtWar(city.OwnerId, besieger.OwnerId))
                throw new InvalidOperationException("A siege needs a hostile army adjacent to the city.");

            city.BesiegedSinceTurn = Turn;
            city.SiegeProgress = 0;
            city.BesiegerId = besieger.OwnerId;
            city.SiegeEnginesBuilt = 0;

            var owner = Player(city.OwnerId);
            var garrison = ArmyAt(city.Position);
            if (garrison == null)
            {
                garrison = new Army(_nextArmyId++, city.OwnerId, city.Position);
                _armies[garrison.Id] = garrison;
            }
            for (int i = 0; i < city.MilitiaCount; i++)
            {
                var militia = CreateUnit(DefaultContent.MilitiaUnit, city.OwnerId);
                militia.BoundToCityId = city.Id;
                if (!garrison.TryAdd(militia, owner.ArmyCap)) break;
            }
        }

        /// <summary>Declares a siege with an army adjacent to a hostile city (without assaulting yet).</summary>
        public bool DeclareSiege(Army army, City city)
        {
            if (city == null || city.IsBesieged || army.InBattle) return false;
            if (army.Position.DistanceTo(city.Position) != 1 || !AtWar(city.OwnerId, army.OwnerId) || !HasMilitary(army)) return false;
            BeginSiege(city, army);
            return true;
        }

        /// <summary>Siege engines the besieging player could build right now for this city.</summary>
        public IEnumerable<UnitDef> AvailableSiegeEngines(City city) =>
            !city.IsBesieged || city.SiegeEnginesBuilt >= Empire.City.MaxSiegeEngines
                ? Enumerable.Empty<UnitDef>()
                : Content.Units.Where(u => u.SiegeProgressCost > 0 && Player(city.BesiegerId).Tech.Has(u.RequiredTech));

        /// <summary>Spends siege progress on an engine that joins the besieging army (GDD §4.6 step 4).</summary>
        public Unit BuildSiegeEngine(City city, Army army, string unitDefId)
        {
            var def = Content.Unit(unitDefId);
            if (!AvailableSiegeEngines(city).Contains(def)) return null;
            if (army.OwnerId != city.BesiegerId || army.InBattle || army.Position.DistanceTo(city.Position) != 1) return null;
            if (city.SiegeProgress < def.SiegeProgressCost) return null;

            var unit = CreateUnit(unitDefId, army.OwnerId);
            if (!army.TryAdd(unit, Player(army.OwnerId).ArmyCap)) return null;
            city.SiegeProgress -= def.SiegeProgressCost;
            city.SiegeEnginesBuilt++;
            return unit;
        }

        /// <summary>Lets AI-controlled sides act until a human side must move, the round ends, or the battle ends.</summary>
        public void AdvanceAIBattleTurns(Battle battle)
        {
            int guard = 0;
            while (battle.AwaitingAction && battle.Active.Player.IsAI && guard++ < 1000)
                BattleAI.PlayTurn(battle);
            if (battle.IsFinished) ResolveBattle(battle);
        }

        /// <summary>Applies a finished battle to the world: casualties, pushback, retreats, captures.</summary>
        public void ResolveBattle(Battle battle)
        {
            if (!battle.IsFinished || !_battles.ContainsKey(battle.Id)) return;
            _battles.Remove(battle.Id);

            var loser = battle.Winner == BattleSideId.Attacker ? battle.Defender : battle.Attacker;
            foreach (var side in new[] { battle.Attacker, battle.Defender })
                foreach (var army in side.Armies)
                {
                    army.BattleId = -1;
                    army.RemoveDead();
                    // Civilians left without an escort on the losing side are captured.
                    bool defenceless = army.Units.All(u => !u.Def.IsMilitary);
                    if (army.IsEmpty || (side == loser && defenceless)) _armies.Remove(army.Id);
                }

            switch (battle.Status)
            {
                case BattleStatus.DefenderWon:
                case BattleStatus.AttackerRetreated:
                    PushBack(battle.Attacker, battle.Defender.Origin);
                    break;
                case BattleStatus.DefenderRetreated:
                    PushBack(battle.Defender, battle.Attacker.Origin);
                    break;
                case BattleStatus.AttackerWon when battle.Objective.HasValue:
                    CaptureCity(CityAt(battle.Objective.Value), battle.Attacker.Player.Id);
                    break;
            }

            EndSiegesWithoutBesiegers();
            RefreshAllVisibility();
            BattleEnded?.Invoke(battle);
            Victory = Victory ?? VictoryChecker.Check(this);
        }

        void PushBack(BattleSide side, HexCoord awayFrom)
        {
            foreach (var army in side.Armies.Where(a => _armies.ContainsKey(a.Id)))
            {
                var dest = army.Position.Neighbors()
                    .Where(n => n.DistanceTo(awayFrom) > army.Position.DistanceTo(awayFrom))
                    .Where(n => ArmyAt(n) == null && CityAt(n) == null && Map.Get(n) != null &&
                                TerrainRules.LandStepCost(Map.Get(army.Position), Map.Get(n)) != TerrainRules.Impassable)
                    .OrderBy(n => n.Q).ThenBy(n => n.R)
                    .Select(n => (HexCoord?)n)
                    .FirstOrDefault();
                if (dest.HasValue) army.Position = dest.Value;
                army.WorldMovesLeft = 0;
            }
        }

        void CaptureCity(City city, int newOwnerId)
        {
            if (city == null) return;
            // Militia disband; any surviving garrison is destroyed with the fall of the city.
            var garrison = ArmyAt(city.Position);
            if (garrison != null && garrison.OwnerId != newOwnerId) _armies.Remove(garrison.Id);

            city.OwnerId = newOwnerId;
            city.BesiegedSinceTurn = -1;
            city.SiegeProgress = 0;
            city.BesiegerId = -1;
            city.SiegeEnginesBuilt = 0;
            city.Population = Math.Max(1, city.Population - 1);
            city.CurrentProduction = null;
            TransferTerritory(city, newOwnerId);
            CheckElimination();
        }

        void EndSiegesWithoutBesiegers()
        {
            foreach (var city in _cities.Values.Where(c => c.IsBesieged))
            {
                bool besieged = city.Position.Neighbors().Any(n => ArmyAt(n) is Army a && a.OwnerId == city.BesiegerId &&
                                                                   AtWar(a.OwnerId, city.OwnerId));
                if (besieged) continue;
                city.BesiegedSinceTurn = -1;
                city.SiegeProgress = 0;
                city.BesiegerId = -1;
                city.SiegeEnginesBuilt = 0;
                var garrison = ArmyAt(city.Position);
                if (garrison == null) continue;
                foreach (var m in garrison.Units.Where(u => u.BoundToCityId == city.Id).ToList()) garrison.Remove(m);
                if (garrison.IsEmpty) _armies.Remove(garrison.Id);
            }
        }

        internal void CheckElimination()
        {
            foreach (var p in _players.Where(p => !p.IsEliminated))
                if (!_cities.Values.Any(c => c.OwnerId == p.Id))
                    p.IsEliminated = true;
        }

        // ------------------------------------------------------------------ per-turn upkeep

        /// <summary>Start-of-turn work for one player: refresh movement, siege ticks, continue battles.</summary>
        internal void BeginPlayerTurn(Player player)
        {
            foreach (var army in _armies.Values.Where(a => a.OwnerId == player.Id))
                army.WorldMovesLeft = army.InBattle ? 0 : WorldMovementOf(army);
            ProgressImprovements(player);
            foreach (var army in _armies.Values.Where(a => a.OwnerId == player.Id && a.Destination.HasValue).ToList())
                ContinueMoveOrder(army);
            WorkerAutomation.Run(this, player.Id);
            RefreshVisibility(player.Id);

            EndSiegesWithoutBesiegers();
            foreach (var city in _cities.Values.Where(c => c.IsBesieged && c.BesiegerId == player.Id))
            {
                foreach (var n in city.Position.Neighbors())
                    if (ArmyAt(n) is Army a && a.OwnerId == player.Id)
                        city.SiegeProgress += a.Units.Sum(u => u.Def.ProductionCost) / 10;
            }

            foreach (var battle in _battles.Values.Where(b => b.Attacker.Player.Id == player.Id).ToList())
            {
                if (battle.Status == BattleStatus.AwaitingNextRound)
                {
                    PullInReinforcements(battle);
                    battle.BeginNextRound();
                }
                AdvanceAIBattleTurns(battle);
            }
        }
    }
}
