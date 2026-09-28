using System;
using System.Collections.Generic;
using System.Linq;
using Crucible.Core.Content;
using Crucible.Core.Economy;
using Crucible.Core.Empire;
using Crucible.Core.Hex;
using Crucible.Core.Units;

namespace Crucible.Core.Game
{
    public enum CityStateStatus
    {
        Neutral,
        Friend,
        Ally,
    }

    /// <summary>
    /// M8 systems: great people, religion, city-states, the World Congress, tourism and the spaceship —
    /// everything the science, culture and diplomatic victories need (GDD §3, §5).
    /// </summary>
    public sealed partial class GameState
    {
        public const int FriendInfluence = 30;
        public const int AllyInfluence = 60;
        public const int GoldPerInfluence = 5;
        public const int WorldLeaderVoteInterval = 10;
        public const int SpaceshipFlightTurns = 10;
        public const int SpaceshipParts = 6;
        public const int ReligionSpreadRange = 10;

        static readonly string[] ReligionNames =
        {
            "The Way of the Sun", "Faith of the Iron Crown", "The Open Sky", "Covenant of the River",
            "The Silent Stone", "Order of the Lantern",
        };

        readonly List<Religion> _religions = new List<Religion>();

        public IReadOnlyList<Religion> Religions => _religions;

        /// <summary>Turn the World Congress was founded (first Globalization), or -1.</summary>
        public int WorldCongressFoundedTurn { get; private set; } = -1;
        public int WorldCongressHostId { get; private set; } = -1;

        /// <summary>Delegates each candidate received at the last World Leader vote.</summary>
        public Dictionary<int, int> LastVote { get; } = new Dictionary<int, int>();

        public event Action<Player, Unit> GreatPersonBorn;
        public event Action<Religion> ReligionFounded;

        public IEnumerable<Player> MajorPlayers => _players.Where(p => !p.IsCityState);

        // ------------------------------------------------------------------ great people

        /// <summary>Points needed for this player's next great person of a type: 100, 200, 300 …</summary>
        public static int GreatPersonThreshold(Player p, GreatPersonType type) =>
            100 * (1 + (p.GreatPeopleBorn.TryGetValue(type, out var n) ? n : 0));

        public static int ProphetThreshold(Player p) => 200 + 150 * p.ProphetsBorn;

        /// <summary>Per-turn great person points from buildings, faith for prophets. Births appear in the capital.</summary>
        internal void ProcessGreatPeople(Player player, int faith)
        {
            foreach (var city in _cities.Values.Where(c => c.OwnerId == player.Id))
                foreach (var b in city.Buildings.Select(Content.Building).Where(b => b.GreatPersonPoints > 0))
                    player.GreatPersonPoints[b.GreatPersonType] =
                        (player.GreatPersonPoints.TryGetValue(b.GreatPersonType, out var v) ? v : 0) + b.GreatPersonPoints;

            foreach (var type in player.GreatPersonPoints.Keys.ToList())
            {
                int threshold = GreatPersonThreshold(player, type);
                if (player.GreatPersonPoints[type] < threshold) continue;
                if (BirthGreatPerson(player, type) == null) continue;
                player.GreatPersonPoints[type] -= threshold;
            }

            player.Faith += faith;
            if (player.Faith >= ProphetThreshold(player) && BirthGreatPerson(player, GreatPersonType.Prophet) != null)
            {
                player.Faith -= ProphetThreshold(player);
                player.ProphetsBorn++;
            }

            int generalThreshold = GreatPersonThreshold(player, GreatPersonType.General);
            if (player.GeneralPoints >= generalThreshold && BirthGreatPerson(player, GreatPersonType.General) != null)
                player.GeneralPoints -= generalThreshold;
        }

        Unit BirthGreatPerson(Player player, GreatPersonType type)
        {
            var home = CapitalOf(player.Id) ?? _cities.Values.FirstOrDefault(c => c.OwnerId == player.Id);
            if (home == null) return null;
            var unit = SpawnUnit(home, Content.GreatPersonUnit(type).Id);
            if (unit == null) return null;
            if (type != GreatPersonType.Prophet)
                player.GreatPeopleBorn[type] = (player.GreatPeopleBorn.TryGetValue(type, out var n) ? n : 0) + 1;
            GreatPersonBorn?.Invoke(player, unit);
            return unit;
        }

        public City CapitalOf(int playerId) =>
            _cities.Values.FirstOrDefault(c => c.OwnerId == playerId && c.IsOriginalCapital && c.FounderId == playerId);

        City NearestOwnCity(int playerId, HexCoord from) =>
            _cities.Values.Where(c => c.OwnerId == playerId).OrderBy(c => c.Position.DistanceTo(from)).ThenBy(c => c.Id).FirstOrDefault();

        /// <summary>
        /// Uses a great person's ability (consuming them, except generals, which lead armies).
        /// Returns a message describing what happened, or null if nothing could be done.
        /// </summary>
        public string UseGreatPerson(Army army, Unit person)
        {
            if (!army.Units.Contains(person) || person.Def.GreatPerson == GreatPersonType.None || army.InBattle) return null;
            var player = Player(army.OwnerId);
            var city = NearestOwnCity(player.Id, army.Position);
            string result;

            switch (person.Def.GreatPerson)
            {
                case GreatPersonType.Scientist:
                {
                    int science = Math.Max(150, 8 * EconomyRules.EmpireIncome(this, player).Science);
                    if (player.Tech.CurrentResearch == null) EconomyProcessor.AutoPickResearch(player);
                    var done = player.Tech.AddScience(science);
                    result = $"Discovery: +{science} science" + (done != null ? $" — {done.Name} learned!" : ".");
                    break;
                }
                case GreatPersonType.Engineer:
                    if (city == null) return null;
                    city.ProductionStored += 400;
                    result = $"Hurried production in {city.Name} (+400).";
                    break;
                case GreatPersonType.Merchant:
                    player.Gold += 350;
                    result = "Trade mission: +350 gold.";
                    break;
                case GreatPersonType.Artist:
                    if (city == null) return null;
                    city.GreatWorks++;
                    player.LifetimeCulture += 100;
                    player.PolicyCulture += 100;
                    result = $"A great work is unveiled in {city.Name}.";
                    break;
                case GreatPersonType.Prophet:
                    result = UseProphet(player, army);
                    if (result == null) return null;
                    break;
                default:
                    return null; // generals lead armies; they have no one-off ability
            }

            army.Remove(person);
            if (army.IsEmpty) _armies.Remove(army.Id);
            return result;
        }

        // ------------------------------------------------------------------ religion

        /// <summary>At most half the major civs (rounded up) can found a religion.</summary>
        public int MaxReligions => (MajorPlayers.Count() + 1) / 2 + 1;

        string UseProphet(Player player, Army army)
        {
            if (player.FoundedReligionId < 0)
            {
                if (_religions.Count >= MaxReligions) return null;
                var holy = NearestOwnCity(player.Id, army.Position);
                if (holy == null) return null;
                var religion = new Religion(_religions.Count, ReligionNames[_religions.Count % ReligionNames.Length], player.Id, holy.Id);
                _religions.Add(religion);
                player.FoundedReligionId = religion.Id;
                AddPressure(holy, religion.Id, 1000);
                ReligionFounded?.Invoke(religion);
                return $"{religion.Name} is founded in {holy.Name}!";
            }

            // Spread: convert the nearest city (any owner) not yet following our faith.
            var target = _cities.Values
                .Where(c => c.ReligionId != player.FoundedReligionId)
                .OrderBy(c => c.Position.DistanceTo(army.Position)).ThenBy(c => c.Id)
                .FirstOrDefault();
            if (target == null) return null;
            AddPressure(target, player.FoundedReligionId, 600);
            return $"{_religions[player.FoundedReligionId].Name} spreads to {target.Name}.";
        }

        static void AddPressure(City city, int religionId, int amount)
        {
            city.ReligiousPressure[religionId] = (city.ReligiousPressure.TryGetValue(religionId, out var p) ? p : 0) + amount;
            var top = city.ReligiousPressure.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First();
            if (top.Value >= 100) city.ReligionId = top.Key;
        }

        /// <summary>Each following city presses its religion on cities within 10 hexes; holy cities twice as hard.</summary>
        void SpreadReligions()
        {
            var sources = _cities.Values.Where(c => c.ReligionId >= 0).ToList();
            foreach (var target in _cities.Values)
                foreach (var src in sources.Where(s => s != target && s.Position.DistanceTo(target.Position) <= ReligionSpreadRange))
                {
                    bool holy = _religions[src.ReligionId].HolyCityId == src.Id;
                    AddPressure(target, src.ReligionId, holy ? 12 : 6);
                }
        }

        /// <summary>Cities anywhere following this player's founded religion.</summary>
        public int FollowerCities(Player founder) =>
            founder.FoundedReligionId < 0 ? 0 : _cities.Values.Count(c => c.ReligionId == founder.FoundedReligionId);

        // ------------------------------------------------------------------ city-states

        public Player AddCityState(string name, CityStateType type, HexCoord position)
        {
            var cs = AddPlayer(name, DefaultContent.CityStateFaction, isAI: true);
            cs.IsCityState = true;
            cs.CityStateType = type;
            FoundCity(cs.Id, position, name, isCapital: false);
            return cs;
        }

        public int InfluenceOf(Player cityState, int playerId) =>
            cityState.Influence.TryGetValue(playerId, out var v) ? v : 0;

        public CityStateStatus StatusWith(Player cityState, int playerId)
        {
            int inf = InfluenceOf(cityState, playerId);
            if (inf >= AllyInfluence && AllyOf(cityState) == playerId) return CityStateStatus.Ally;
            return inf >= FriendInfluence ? CityStateStatus.Friend : CityStateStatus.Neutral;
        }

        /// <summary>The single major with the most influence of at least 60, or -1.</summary>
        public int AllyOf(Player cityState)
        {
            var best = cityState.Influence.Where(kv => kv.Value >= AllyInfluence)
                .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).FirstOrDefault();
            return best.Value >= AllyInfluence ? best.Key : -1;
        }

        /// <summary>Gold gift: 1 influence per 5 gold.</summary>
        public bool GiftGold(Player giver, Player cityState, int gold)
        {
            if (!cityState.IsCityState || giver.IsCityState || gold <= 0 || giver.Gold < gold) return false;
            giver.Gold -= gold;
            cityState.Influence[giver.Id] = InfluenceOf(cityState, giver.Id) + gold / GoldPerInfluence;
            return true;
        }

        /// <summary>Yields a major receives from its city-state friends and allies (added to its capital).</summary>
        public Yields CityStateBonusYields(Player major)
        {
            var y = new Yields();
            foreach (var cs in _players.Where(p => p.IsCityState && !p.IsEliminated))
            {
                var status = StatusWith(cs, major.Id);
                if (status == CityStateStatus.Neutral) continue;
                int full = status == CityStateStatus.Ally ? 1 : 0;
                if (cs.CityStateType == CityStateType.Maritime) y.Food += 1 + 2 * full;
                if (cs.CityStateType == CityStateType.Cultured) y.Culture += 2 + 3 * full;
            }
            return y;
        }

        public int CityStateHappiness(Player major) =>
            _players.Where(p => p.IsCityState && !p.IsEliminated && p.CityStateType == CityStateType.Mercantile)
                .Sum(cs => StatusWith(cs, major.Id) == CityStateStatus.Ally ? 4 : StatusWith(cs, major.Id) == CityStateStatus.Friend ? 2 : 0);

        void ProcessCityStates()
        {
            foreach (var cs in _players.Where(p => p.IsCityState && !p.IsEliminated))
            {
                foreach (var id in cs.Influence.Keys.ToList())
                    cs.Influence[id] = Math.Max(0, cs.Influence[id] - 1); // influence fades

                int ally = AllyOf(cs);
                if (cs.CityStateType == CityStateType.Militaristic && ally >= 0 && Turn % 15 == 0 && CapitalOf(ally) is City capital &&
                    CityGovernor.BestUnit(this, capital) is ProductionItem gift)
                    SpawnUnit(capital, gift.Id);
            }
        }

        // ------------------------------------------------------------------ World Congress

        public bool WorldCongressFounded => WorldCongressFoundedTurn >= 0;

        public int NextWorldLeaderVoteTurn =>
            WorldCongressFounded ? WorldCongressFoundedTurn + WorldLeaderVoteInterval * (1 + (Turn - WorldCongressFoundedTurn) / WorldLeaderVoteInterval) : -1;

        /// <summary>Delegates: 1 per major (+1 for the host); each city-state's delegate goes to its ally.</summary>
        public Dictionary<int, int> Delegates()
        {
            var d = MajorPlayers.Where(p => !p.IsEliminated).ToDictionary(p => p.Id, p => 1 + (p.Id == WorldCongressHostId ? 1 : 0));
            foreach (var cs in _players.Where(p => p.IsCityState && !p.IsEliminated))
            {
                int ally = AllyOf(cs);
                if (ally >= 0 && d.ContainsKey(ally)) d[ally]++;
            }
            return d;
        }

        public int TotalDelegates => MajorPlayers.Count(p => !p.IsEliminated) + 1 + _players.Count(p => p.IsCityState && !p.IsEliminated);
        public int VotesNeeded => TotalDelegates / 2 + 1;

        void ProcessWorldCongress()
        {
            if (!WorldCongressFounded)
            {
                var founder = MajorPlayers.Where(p => !p.IsEliminated && p.Tech.Has("globalization")).OrderBy(p => p.Id).FirstOrDefault();
                if (founder == null) return;
                WorldCongressFoundedTurn = Turn;
                WorldCongressHostId = founder.Id;
                return;
            }
            if ((Turn - WorldCongressFoundedTurn) % WorldLeaderVoteInterval != 0 || Turn == WorldCongressFoundedTurn) return;

            // Every civ votes for itself (diplomacy between majors comes later), allied city-states follow their ally.
            LastVote.Clear();
            foreach (var kv in Delegates()) LastVote[kv.Key] = kv.Value;
            var winner = LastVote.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).FirstOrDefault();
            if (winner.Value >= VotesNeeded) Player(winner.Key).WonWorldLeaderVote = true;
        }

        // ------------------------------------------------------------------ tourism

        /// <summary>Tourism: 3 per great work, plus a share of culture once mass media (radio) arrives.</summary>
        public int TourismPerTurn(Player p)
        {
            int works = _cities.Values.Where(c => c.OwnerId == p.Id).Sum(c => c.GreatWorks);
            int tourism = 3 * works;
            if (p.Tech.Has("radio")) tourism += EconomyRules.EmpireIncome(this, p).Culture / 2;
            if (p.Tech.Has("the_internet")) tourism *= 2;
            return tourism;
        }

        internal void ProcessTourism(Player p)
        {
            if (p.IsCityState) return;
            p.Tourism = TourismPerTurn(p);
            foreach (var other in MajorPlayers.Where(o => o.Id != p.Id))
                p.TourismAgainst[other.Id] = (p.TourismAgainst.TryGetValue(other.Id, out var t) ? t : 0) + p.Tourism;
        }

        // ------------------------------------------------------------------ projects & spaceship

        internal bool CompleteProject(City city, string projectId)
        {
            var def = Content.Project(projectId);
            var player = Player(city.OwnerId);
            player.CompletedProjects[def.Id] = (player.CompletedProjects.TryGetValue(def.Id, out var n) ? n : 0) + 1;
            if (def.SpaceshipPart)
            {
                player.SpaceshipPartsBuilt++;
                if (player.SpaceshipPartsBuilt >= SpaceshipParts && player.SpaceshipArrivalTurn < 0)
                    player.SpaceshipArrivalTurn = Turn + SpaceshipFlightTurns;
            }
            return true;
        }

        /// <summary>Taking a capital scraps its owner's spaceship programme (parts built there are lost).</summary>
        void ScrapSpaceship(Player p)
        {
            p.SpaceshipPartsBuilt = 0;
            p.SpaceshipArrivalTurn = -1;
            foreach (var part in Content.Projects.Where(x => x.SpaceshipPart)) p.CompletedProjects.Remove(part.Id);
        }

        void ProcessSpaceships()
        {
            foreach (var p in MajorPlayers.Where(p => p.SpaceshipArrivalTurn >= 0 && Turn >= p.SpaceshipArrivalTurn))
                p.SpaceshipPartsLanded = SpaceshipParts;
        }

        // ------------------------------------------------------------------ world turn

        /// <summary>Once per round, after every player has moved: faiths spread, city-states, votes, arrivals.</summary>
        internal void ProcessWorldTurn()
        {
            SpreadReligions();
            ProcessCityStates();
            ProcessWorldCongress();
            ProcessSpaceships();
        }
    }
}
