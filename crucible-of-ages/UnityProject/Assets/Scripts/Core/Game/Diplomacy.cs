using System;
using System.Collections.Generic;
using System.Linq;
using Crucible.Core.Empire;

namespace Crucible.Core.Game
{
    public enum Treaty
    {
        Peace,
        OpenBorders,
        DefensivePact,
    }

    /// <summary>State between two major civs (stored once per unordered pair).</summary>
    public sealed class Relation
    {
        public bool AtWar;
        public int WarStartedTurn = -1;

        /// <summary>No war may be declared before this turn (peace treaty).</summary>
        public int PeaceUntilTurn = -1;

        public bool OpenBorders;
        public bool DefensivePact;
    }

    /// <summary>A proposal waiting for a human's answer.</summary>
    public sealed class Proposal
    {
        public int FromId;
        public int ToId;
        public Treaty Kind;
        public int Turn;

        public override string ToString() => $"{Kind} from P{FromId}";
    }

    /// <summary>
    /// Relations between major civs (GDD §3): war and peace, open borders, defensive pacts, and the
    /// memories that drive AI opinion. City-states are always neutral and are handled elsewhere.
    /// </summary>
    public sealed class Diplomacy
    {
        public const int MinWarTurnsBeforePeace = 10;
        public const int PeaceTreatyTurns = 10;
        public const int DeclaredOnPenalty = -30;
        public const int WarmongerPenalty = -10;
        public const int MemoryTurns = 50;

        /// <summary>Opinion every civ holds against one that used a nuclear weapon (per strike, remembered <see cref="MemoryTurns"/>).</summary>
        public const int NuclearPenalty = -25;

        readonly SortedDictionary<(int, int), Relation> _relations = new SortedDictionary<(int, int), Relation>();

        /// <summary>(declarer, victim, turn) for every war declaration: the AI remembers.</summary>
        internal readonly List<(int declarer, int victim, int turn)> Declarations = new List<(int, int, int)>();

        /// <summary>(player, turn) for every nuclear strike: the whole world remembers.</summary>
        internal readonly List<(int playerId, int turn)> NuclearStrikes = new List<(int, int)>();

        public readonly List<Proposal> Pending = new List<Proposal>();

        /// <summary>
        /// Pairs never touched by diplomacy start in this state. True keeps sandbox games and tests
        /// "everyone at war"; new skirmishes start at peace.
        /// </summary>
        public bool MajorsStartAtWar { get; set; } = true;

        internal IEnumerable<KeyValuePair<(int, int), Relation>> All => _relations;

        static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

        public Relation Get(int a, int b)
        {
            var key = Key(a, b);
            if (!_relations.TryGetValue(key, out var r))
            {
                r = new Relation { AtWar = MajorsStartAtWar };
                _relations[key] = r;
            }
            return r;
        }

        internal void Set(int a, int b, Relation r) => _relations[Key(a, b)] = r;

        public bool IsAtWar(int a, int b) => a != b && Get(a, b).AtWar;

        public bool DeclaredOnRecently(int declarer, int victim, int now) =>
            Declarations.Any(d => d.declarer == declarer && d.victim == victim && now - d.turn < MemoryTurns);
    }

    public sealed partial class GameState
    {
        public Diplomacy Diplomacy { get; } = new Diplomacy();

        public event Action<Player, Player> WarDeclared;
        public event Action<Player, Player> PeaceMade;

        /// <summary>Why a declaration would be refused, or null if it's allowed.</summary>
        public string CannotDeclareWar(Player declarer, Player target)
        {
            if (declarer.IsCityState || target.IsCityState || declarer == target) return "Only major civilisations wage war on each other.";
            var rel = Diplomacy.Get(declarer.Id, target.Id);
            if (rel.AtWar) return "Already at war.";
            if (Turn < rel.PeaceUntilTurn) return $"A peace treaty holds until turn {rel.PeaceUntilTurn}.";
            return null;
        }

        /// <summary>Declares war. The target's defensive-pact partners join it; open borders and pacts between the two end.</summary>
        public bool DeclareWar(Player declarer, Player target)
        {
            if (CannotDeclareWar(declarer, target) != null) return false;
            StartWar(declarer, target);
            foreach (var partner in MajorPlayers.Where(p => p != declarer && p != target && !p.IsEliminated &&
                                                          Diplomacy.Get(p.Id, target.Id).DefensivePact &&
                                                          !Diplomacy.IsAtWar(p.Id, declarer.Id)))
                StartWar(partner, declarer, triggeredByPact: true);
            return true;
        }

        void StartWar(Player declarer, Player target, bool triggeredByPact = false)
        {
            var rel = Diplomacy.Get(declarer.Id, target.Id);
            rel.AtWar = true;
            rel.WarStartedTurn = Turn;
            rel.OpenBorders = false;
            rel.DefensivePact = false;
            rel.PeaceUntilTurn = -1;
            if (!triggeredByPact) Diplomacy.Declarations.Add((declarer.Id, target.Id, Turn));
            Diplomacy.Pending.RemoveAll(p => (p.FromId == declarer.Id && p.ToId == target.Id) || (p.FromId == target.Id && p.ToId == declarer.Id));
            WarDeclared?.Invoke(declarer, target);
        }

        /// <summary>
        /// Proposes a treaty. An AI answers at once (returns whether it agreed); a human gets a pending
        /// proposal (returns false until they accept it).
        /// </summary>
        public bool Propose(Player from, Player to, Treaty kind)
        {
            if (!CanPropose(from, to, kind)) return false;
            if (!to.IsAI)
            {
                if (!Diplomacy.Pending.Any(p => p.FromId == from.Id && p.ToId == to.Id && p.Kind == kind))
                    Diplomacy.Pending.Add(new Proposal { FromId = from.Id, ToId = to.Id, Kind = kind, Turn = Turn });
                return false;
            }
            if (!AI.DiplomacyAI.Accepts(this, to, from, kind)) return false;
            Conclude(from, to, kind);
            return true;
        }

        public bool CanPropose(Player from, Player to, Treaty kind)
        {
            if (from == to || from.IsCityState || to.IsCityState || from.IsEliminated || to.IsEliminated) return false;
            var rel = Diplomacy.Get(from.Id, to.Id);
            switch (kind)
            {
                case Treaty.Peace: return rel.AtWar && Turn - rel.WarStartedTurn >= Diplomacy.MinWarTurnsBeforePeace;
                case Treaty.OpenBorders: return !rel.AtWar && !rel.OpenBorders;
                case Treaty.DefensivePact: return !rel.AtWar && !rel.DefensivePact;
                default: return false;
            }
        }

        /// <summary>A human answers a pending proposal.</summary>
        public bool Answer(Proposal proposal, bool accept)
        {
            if (!Diplomacy.Pending.Remove(proposal)) return false;
            var from = Player(proposal.FromId);
            var to = Player(proposal.ToId);
            if (!accept || !CanPropose(from, to, proposal.Kind)) return false;
            Conclude(from, to, proposal.Kind);
            return true;
        }

        void Conclude(Player a, Player b, Treaty kind)
        {
            var rel = Diplomacy.Get(a.Id, b.Id);
            switch (kind)
            {
                case Treaty.Peace:
                    rel.AtWar = false;
                    rel.PeaceUntilTurn = Turn + Diplomacy.PeaceTreatyTurns;
                    EndBattlesBetween(a, b);
                    ExpelArmies(a, b);
                    ExpelArmies(b, a);
                    PeaceMade?.Invoke(a, b);
                    break;
                case Treaty.OpenBorders:
                    rel.OpenBorders = true;
                    break;
                case Treaty.DefensivePact:
                    rel.DefensivePact = true;
                    break;
            }
        }

        /// <summary>At peace without open borders, armies may not enter another major's territory.</summary>
        public bool MayEnterTerritory(int armyOwner, World.Tile tile)
        {
            int owner = tile?.OwnerPlayerId ?? -1;
            if (owner < 0 || owner == armyOwner) return true;
            var other = Player(owner);
            if (other.IsCityState || Player(armyOwner).IsCityState) return true;
            var rel = Diplomacy.Get(armyOwner, owner);
            return rel.AtWar || rel.OpenBorders;
        }

        /// <summary>Peace: <paramref name="guest"/>'s armies inside <paramref name="host"/>'s borders are moved out.</summary>
        void ExpelArmies(Player guest, Player host)
        {
            foreach (var army in _armies.Values.Where(a => a.OwnerId == guest.Id && !a.InBattle &&
                                                           Map.Get(a.Position).OwnerPlayerId == host.Id).ToList())
            {
                var mobility = MobilityOf(army);
                var exit = Map.Tiles
                    .Where(t => t.OwnerPlayerId != host.Id && ArmyAt(t.Coord) == null && CityAt(t.Coord) == null &&
                                BattleCovering(t.Coord) == null && World.TerrainRules.CanStand(mobility, t))
                    .OrderBy(t => t.Coord.DistanceTo(army.Position)).ThenBy(t => t.Coord.Q).ThenBy(t => t.Coord.R)
                    .FirstOrDefault();
                if (exit == null) continue;
                army.Position = exit.Coord;
                army.Destination = null;
                army.WorldMovesLeft = 0;
            }
            RefreshVisibility(guest.Id);
        }

        /// <summary>Peace ends any fighting between the two: battles are called off with no winner.</summary>
        void EndBattlesBetween(Player a, Player b)
        {
            foreach (var battle in _battles.Values.Where(x =>
                         (x.Attacker.Player == a && x.Defender.Player == b) || (x.Attacker.Player == b && x.Defender.Player == a)).ToList())
            {
                _battles.Remove(battle.Id);
                foreach (var side in new[] { battle.Attacker, battle.Defender })
                    foreach (var army in side.Armies)
                    {
                        army.BattleId = -1;
                        army.RemoveDead();
                        if (army.IsEmpty) _armies.Remove(army.Id);
                    }
            }
            foreach (var c in _cities.Values.Where(c => c.IsBesieged &&
                                                        ((c.OwnerId == a.Id && c.BesiegerId == b.Id) || (c.OwnerId == b.Id && c.BesiegerId == a.Id))))
            {
                c.BesiegedSinceTurn = -1;
                c.BesiegerId = -1;
                c.SiegeProgress = 0;
                c.SiegeEnginesBuilt = 0;
            }
            EndSiegesWithoutBesiegers();
        }

        /// <summary>
        /// How <paramref name="observer"/> feels about <paramref name="subject"/> (−100…100), from memories
        /// and treaties. Drives AI answers to proposals.
        /// </summary>
        public int Opinion(Player observer, Player subject)
        {
            if (observer == subject) return 100;
            var rel = Diplomacy.Get(observer.Id, subject.Id);
            int o = 0;
            if (Diplomacy.DeclaredOnRecently(subject.Id, observer.Id, Turn)) o += Diplomacy.DeclaredOnPenalty;
            o += Diplomacy.Declarations.Count(d => d.declarer == subject.Id && d.victim != observer.Id && Turn - d.turn < Diplomacy.MemoryTurns)
                 * Diplomacy.WarmongerPenalty;
            o -= 15 * _cities.Values.Count(c => c.FounderId == observer.Id && c.OwnerId == subject.Id);
            o += Diplomacy.NuclearStrikes.Count(n => n.playerId == subject.Id && Turn - n.turn < Diplomacy.MemoryTurns) * Diplomacy.NuclearPenalty;
            if (rel.AtWar) o -= 20;
            if (rel.OpenBorders) o += 10;
            if (rel.DefensivePact) o += 20;
            bool sharedEnemy = MajorPlayers.Any(t => t != observer && t != subject && !t.IsEliminated &&
                                                     Diplomacy.IsAtWar(observer.Id, t.Id) && Diplomacy.IsAtWar(subject.Id, t.Id));
            if (sharedEnemy) o += 15;
            return Math.Max(-100, Math.Min(100, o));
        }
    }
}
