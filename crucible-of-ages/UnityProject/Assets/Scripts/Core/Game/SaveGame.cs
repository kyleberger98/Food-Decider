using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Crucible.Core.Combat;
using Crucible.Core.Content;
using Crucible.Core.Economy;
using Crucible.Core.Empire;
using Crucible.Core.Hex;
using Crucible.Core.Units;
using Crucible.Core.World;

namespace Crucible.Core.Game
{
    /// <summary>
    /// Binary save games: a full snapshot of the simulation, including battles that are mid-round.
    /// Content is not stored; it is rebuilt from <see cref="DefaultContent"/> and referenced by id, so a
    /// save stays valid as long as the ids it uses still exist. Loading a save and playing on gives exactly
    /// the same game as never having saved (tested).
    /// </summary>
    public static class SaveGame
    {
        const string Magic = "CRUCIBLE";
        public const int Version = 2; // 2: diplomacy between majors

        public static byte[] Save(GameState game, TurnManager turns)
        {
            using (var ms = new MemoryStream())
            {
                Save(game, turns?.ActivePlayerIndex ?? 0, ms);
                return ms.ToArray();
            }
        }

        public static void Save(GameState game, int activePlayerIndex, Stream stream)
        {
            var w = new SaveWriter(stream);
            w.String(Magic);
            w.Int(Version);
            w.Int(activePlayerIndex);
            game.WriteState(w);
            w.Flush();
        }

        /// <summary>Loads a save. The returned turn manager resumes on the saved player's turn (no turn start re-run).</summary>
        public static (GameState game, TurnManager turns) Load(Stream stream, IPlayerAI ai = null)
        {
            var r = new SaveReader(stream);
            string magic;
            try { magic = r.String(); }
            catch (Exception e) when (e is IOException || e is FormatException) { magic = null; }
            if (magic != Magic) throw new InvalidDataException("Not a Crucible of Ages save.");
            int version = r.Int();
            if (version != Version) throw new InvalidDataException($"Unsupported save version {version} (expected {Version}).");
            int active = r.Int();
            GameState game;
            try { game = GameState.ReadState(r, DefaultContent.Create()); }
            catch (Exception e) when (e is IOException || e is KeyNotFoundException || e is ArgumentException)
            {
                throw new InvalidDataException("The save file is damaged or uses content this version doesn't have.", e);
            }
            var turns = new TurnManager(game, ai);
            turns.Resume(active);
            return (game, turns);
        }

        public static (GameState game, TurnManager turns) Load(byte[] data, IPlayerAI ai = null)
        {
            using (var ms = new MemoryStream(data)) return Load(ms, ai);
        }
    }

    /// <summary>Little-endian binary writer with helpers for the game's value types.</summary>
    public sealed class SaveWriter
    {
        readonly BinaryWriter _w;
        public SaveWriter(Stream s) { _w = new BinaryWriter(s, Encoding.UTF8, leaveOpen: true); }
        public void Flush() => _w.Flush();
        public void Int(int v) => _w.Write(v);
        public void ULong(ulong v) => _w.Write(v);
        public void Bool(bool v) => _w.Write(v);
        public void String(string v) { _w.Write(v != null); if (v != null) _w.Write(v); }
        public void Hex(HexCoord c) { _w.Write(c.Q); _w.Write(c.R); }
        public void OptHex(HexCoord? c) { Bool(c.HasValue); if (c.HasValue) Hex(c.Value); }
        public void Ints(IEnumerable<int> xs) { var l = xs.ToList(); Int(l.Count); foreach (var x in l) Int(x); }
        public void Strings(IEnumerable<string> xs) { var l = xs.ToList(); Int(l.Count); foreach (var x in l) String(x); }
        public void Hexes(IEnumerable<HexCoord> xs) { var l = xs.ToList(); Int(l.Count); foreach (var x in l) Hex(x); }
        public void IntMap(IEnumerable<KeyValuePair<int, int>> m) { var l = m.OrderBy(kv => kv.Key).ToList(); Int(l.Count); foreach (var kv in l) { Int(kv.Key); Int(kv.Value); } }
        public void StringIntMap(IEnumerable<KeyValuePair<string, int>> m) { var l = m.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList(); Int(l.Count); foreach (var kv in l) { String(kv.Key); Int(kv.Value); } }
    }

    public sealed class SaveReader
    {
        readonly BinaryReader _r;
        public SaveReader(Stream s) { _r = new BinaryReader(s, Encoding.UTF8, leaveOpen: true); }
        public int Int() => _r.ReadInt32();
        public ulong ULong() => _r.ReadUInt64();
        public bool Bool() => _r.ReadBoolean();
        public string String() => _r.ReadBoolean() ? _r.ReadString() : null;
        public HexCoord Hex() => new HexCoord(_r.ReadInt32(), _r.ReadInt32());
        public HexCoord? OptHex() => Bool() ? Hex() : (HexCoord?)null;
        public List<int> Ints() { int n = Int(); var l = new List<int>(n); for (int i = 0; i < n; i++) l.Add(Int()); return l; }
        public List<string> Strings() { int n = Int(); var l = new List<string>(n); for (int i = 0; i < n; i++) l.Add(String()); return l; }
        public List<HexCoord> Hexes() { int n = Int(); var l = new List<HexCoord>(n); for (int i = 0; i < n; i++) l.Add(Hex()); return l; }
        public List<KeyValuePair<int, int>> IntMap() { int n = Int(); var l = new List<KeyValuePair<int, int>>(n); for (int i = 0; i < n; i++) l.Add(new KeyValuePair<int, int>(Int(), Int())); return l; }
        public List<KeyValuePair<string, int>> StringIntMap() { int n = Int(); var l = new List<KeyValuePair<string, int>>(n); for (int i = 0; i < n; i++) l.Add(new KeyValuePair<string, int>(String(), Int())); return l; }
    }

    public sealed partial class GameState
    {
        internal void WriteState(SaveWriter w)
        {
            // Core
            w.ULong(Rng.State);
            w.Int(Turn); w.Int(TurnLimit); w.Int(MapVersion);
            w.Int(_nextUnitId); w.Int(_nextArmyId); w.Int(_nextCityId); w.Int(_nextBattleId);
            w.Bool(Victory != null);
            if (Victory != null) { w.Int((int)Victory.Type); w.Int(Victory.WinnerId); }

            // Map
            w.Int(Map.Width); w.Int(Map.Height);
            foreach (var t in Map.Tiles)
            {
                w.Int((int)t.Terrain); w.Int((int)t.Feature); w.Int(t.Elevation); w.Int(t.RiverEdges); w.Int(t.WallTier);
                w.Int(t.OwnerPlayerId); w.Int(t.OwnerCityId); w.Int(t.CityId);
                w.Int((int)t.Resource); w.Int((int)t.Improvement); w.Int((int)t.ImprovementInProgress); w.Int(t.ImprovementProgress);
            }

            // Players
            w.Int(_players.Count);
            foreach (var p in _players)
            {
                w.String(p.Name); w.String(p.Faction.Id); w.Bool(p.IsAI); w.Bool(p.IsEliminated);
                w.Int(p.Gold); w.Int(p.Happiness); w.Int(p.PolicyArmyCapBonus);
                w.Bool(p.AIWantsSettlers); w.Int(p.AIMilitaryTarget); w.Int(p.AITargetCityId);
                w.Strings(p.Policies.OrderBy(x => x, StringComparer.Ordinal)); w.Int(p.PolicyCulture);
                w.Bool(p.IsCityState); w.Int((int)p.CityStateType); w.IntMap(p.Influence);
                w.IntMap(p.GreatPersonPoints.Select(kv => new KeyValuePair<int, int>((int)kv.Key, kv.Value)));
                w.IntMap(p.GreatPeopleBorn.Select(kv => new KeyValuePair<int, int>((int)kv.Key, kv.Value)));
                w.Int(p.GeneralPoints); w.Int(p.Faith); w.Int(p.ProphetsBorn); w.Int(p.FoundedReligionId);
                w.StringIntMap(p.CompletedProjects); w.Int(p.SpaceshipPartsBuilt); w.Int(p.SpaceshipArrivalTurn); w.Int(p.SpaceshipPartsLanded);
                w.Int(p.Tourism); w.Int(p.LifetimeCulture); w.Bool(p.WonWorldLeaderVote); w.IntMap(p.TourismAgainst);
                w.Strings(p.Tech.Researched.OrderBy(x => x, StringComparer.Ordinal)); w.String(p.Tech.CurrentResearch); w.Int(p.Tech.Progress);
                var vis = _visibility[p.Id];
                w.Hexes(vis.Explored.OrderBy(h => h.Q).ThenBy(h => h.R));
                w.Hexes(vis.Visible.OrderBy(h => h.Q).ThenBy(h => h.R));
                w.Int(vis.Version);
            }

            // Units live in armies or in city hangars.
            void WriteUnit(Unit u)
            {
                w.Int(u.Id); w.String(u.Def.Id); w.Int(u.OwnerId); w.Int(u.Hp); w.Int(u.Xp); w.Int(u.BoundToCityId);
                w.Int(u.BattleMovesLeft); w.Bool(u.HasAttacked); w.Bool(u.ActedThisTurn); w.Bool(u.Fortified);
            }

            w.Int(_armies.Count);
            foreach (var a in _armies.Values)
            {
                w.Int(a.Id); w.Int(a.OwnerId); w.Hex(a.Position); w.Int(a.WorldMovesLeft); w.OptHex(a.Destination);
                w.Int((int)a.BuildOrder); w.Bool(a.AutomatedWorkers); w.Int(a.BattleId);
                w.Int(a.Count);
                foreach (var u in a.Units) WriteUnit(u);
            }

            w.Int(_cities.Count);
            foreach (var c in _cities.Values)
            {
                w.Int(c.Id); w.String(c.Name); w.Int(c.OwnerId); w.Hex(c.Position); w.Int(c.FounderId);
                w.Int(c.Population); w.Bool(c.IsOriginalCapital);
                w.Int(c.FoodStored); w.Int(c.ProductionStored); w.Int(c.CultureStored); w.Int(c.TilesClaimed);
                w.Bool(c.CurrentProduction.HasValue);
                if (c.CurrentProduction.HasValue) { w.Int((int)c.CurrentProduction.Value.Kind); w.String(c.CurrentProduction.Value.Id); }
                w.Strings(c.Buildings.OrderBy(x => x, StringComparer.Ordinal));
                w.Hexes(c.WorkedTiles.OrderBy(h => h.Q).ThenBy(h => h.R));
                w.Int(c.BesiegedSinceTurn); w.Int(c.SiegeProgress); w.Int(c.BesiegerId); w.Int(c.SiegeEnginesBuilt);
                w.Int(c.AirUnits.Count);
                foreach (var u in c.AirUnits) WriteUnit(u);
                w.Int(c.ReligionId); w.IntMap(c.ReligiousPressure); w.Int(c.GreatWorks);
            }

            w.Int(_religions.Count);
            foreach (var rel in _religions) { w.String(rel.Name); w.Int(rel.FounderId); w.Int(rel.HolyCityId); }
            w.Int(WorldCongressFoundedTurn); w.Int(WorldCongressHostId); w.IntMap(LastVote);

            w.Bool(Diplomacy.MajorsStartAtWar);
            var relations = Diplomacy.All.ToList();
            w.Int(relations.Count);
            foreach (var kv in relations)
            {
                w.Int(kv.Key.Item1); w.Int(kv.Key.Item2);
                w.Bool(kv.Value.AtWar); w.Int(kv.Value.WarStartedTurn); w.Int(kv.Value.PeaceUntilTurn);
                w.Bool(kv.Value.OpenBorders); w.Bool(kv.Value.DefensivePact);
            }
            w.Int(Diplomacy.Declarations.Count);
            foreach (var d in Diplomacy.Declarations) { w.Int(d.declarer); w.Int(d.victim); w.Int(d.turn); }
            w.Int(Diplomacy.Pending.Count);
            foreach (var p in Diplomacy.Pending) { w.Int(p.FromId); w.Int(p.ToId); w.Int((int)p.Kind); w.Int(p.Turn); }

            w.Int(_battles.Count);
            foreach (var b in _battles.Values)
            {
                w.Int(b.Id);
                w.Hexes(b.Tiles.OrderBy(h => h.Q).ThenBy(h => h.R));
                w.Int(b.Attacker.Player.Id); w.Hex(b.Attacker.Origin);
                w.Int(b.Defender.Player.Id); w.Hex(b.Defender.Origin);
                w.OptHex(b.Objective); w.Int(b.ObjectiveCityId);
                b.WriteState(w);
            }
        }

        internal static GameState ReadState(SaveReader r, ContentDatabase content)
        {
            ulong rng = r.ULong();
            int turn = r.Int(), turnLimit = r.Int(), mapVersion = r.Int();
            int nextUnit = r.Int(), nextArmy = r.Int(), nextCity = r.Int(), nextBattle = r.Int();
            VictoryResult victory = r.Bool() ? new VictoryResult { Type = (VictoryType)r.Int(), WinnerId = r.Int() } : null;

            var map = new WorldMap(r.Int(), r.Int());
            foreach (var t in map.Tiles)
            {
                t.Terrain = (TerrainType)r.Int(); t.Feature = (FeatureType)r.Int(); t.Elevation = r.Int();
                t.RiverEdges = (byte)r.Int(); t.WallTier = r.Int();
                t.OwnerPlayerId = r.Int(); t.OwnerCityId = r.Int(); t.CityId = r.Int();
                t.Resource = (ResourceType)r.Int(); t.Improvement = (ImprovementType)r.Int();
                t.ImprovementInProgress = (ImprovementType)r.Int(); t.ImprovementProgress = r.Int();
            }

            var g = new GameState(content, map, 0);
            g.Rng.State = rng;
            g.Turn = turn; g.TurnLimit = turnLimit; g.MapVersion = mapVersion; g.Victory = victory;

            for (int n = r.Int(), i = 0; i < n; i++)
            {
                var p = g.AddPlayer(r.String(), r.String(), r.Bool());
                p.IsEliminated = r.Bool();
                p.Gold = r.Int(); p.Happiness = r.Int(); p.PolicyArmyCapBonus = r.Int();
                p.AIWantsSettlers = r.Bool(); p.AIMilitaryTarget = r.Int(); p.AITargetCityId = r.Int();
                foreach (var id in r.Strings()) p.Policies.Add(id);
                p.PolicyCulture = r.Int();
                p.IsCityState = r.Bool(); p.CityStateType = (CityStateType)r.Int();
                foreach (var kv in r.IntMap()) p.Influence[kv.Key] = kv.Value;
                foreach (var kv in r.IntMap()) p.GreatPersonPoints[(GreatPersonType)kv.Key] = kv.Value;
                foreach (var kv in r.IntMap()) p.GreatPeopleBorn[(GreatPersonType)kv.Key] = kv.Value;
                p.GeneralPoints = r.Int(); p.Faith = r.Int(); p.ProphetsBorn = r.Int(); p.FoundedReligionId = r.Int();
                foreach (var kv in r.StringIntMap()) p.CompletedProjects[kv.Key] = kv.Value;
                p.SpaceshipPartsBuilt = r.Int(); p.SpaceshipArrivalTurn = r.Int(); p.SpaceshipPartsLanded = r.Int();
                p.Tourism = r.Int(); p.LifetimeCulture = r.Int(); p.WonWorldLeaderVote = r.Bool();
                foreach (var kv in r.IntMap()) p.TourismAgainst[kv.Key] = kv.Value;
                p.Tech.Restore(r.Strings(), r.String(), r.Int());
                g._visibility[p.Id].Restore(r.Hexes(), r.Hexes(), r.Int());
            }

            var units = new Dictionary<int, Unit>();
            Unit ReadUnit()
            {
                var u = new Unit(r.Int(), content.Unit(r.String()), r.Int())
                {
                    Hp = r.Int(), Xp = r.Int(), BoundToCityId = r.Int(),
                    BattleMovesLeft = r.Int(), HasAttacked = r.Bool(), ActedThisTurn = r.Bool(), Fortified = r.Bool(),
                };
                units[u.Id] = u;
                return u;
            }

            for (int n = r.Int(), i = 0; i < n; i++)
            {
                var a = new Army(r.Int(), r.Int(), r.Hex())
                {
                    WorldMovesLeft = r.Int(), Destination = r.OptHex(), BuildOrder = (ImprovementType)r.Int(),
                    AutomatedWorkers = r.Bool(), BattleId = r.Int(),
                };
                for (int k = r.Int(); k > 0; k--) a.RestoreUnit(ReadUnit());
                g._armies[a.Id] = a;
            }

            for (int n = r.Int(), i = 0; i < n; i++)
            {
                var c = new City(r.Int(), r.String(), r.Int(), r.Hex(), r.Int())
                {
                    Population = r.Int(), IsOriginalCapital = r.Bool(),
                    FoodStored = r.Int(), ProductionStored = r.Int(), CultureStored = r.Int(), TilesClaimed = r.Int(),
                };
                if (r.Bool()) c.CurrentProduction = new ProductionItem((ProductionKind)r.Int(), r.String());
                foreach (var b in r.Strings()) c.Buildings.Add(b);
                foreach (var h in r.Hexes()) c.WorkedTiles.Add(h);
                c.BesiegedSinceTurn = r.Int(); c.SiegeProgress = r.Int(); c.BesiegerId = r.Int(); c.SiegeEnginesBuilt = r.Int();
                for (int k = r.Int(); k > 0; k--) c.AirUnits.Add(ReadUnit());
                c.ReligionId = r.Int();
                foreach (var kv in r.IntMap()) c.ReligiousPressure[kv.Key] = kv.Value;
                c.GreatWorks = r.Int();
                g._cities[c.Id] = c;
            }

            for (int n = r.Int(), i = 0; i < n; i++) g._religions.Add(new Religion(i, r.String(), r.Int(), r.Int()));
            g.WorldCongressFoundedTurn = r.Int(); g.WorldCongressHostId = r.Int();
            foreach (var kv in r.IntMap()) g.LastVote[kv.Key] = kv.Value;

            g.Diplomacy.MajorsStartAtWar = r.Bool();
            for (int n = r.Int(), i = 0; i < n; i++)
            {
                int a = r.Int(), b = r.Int();
                g.Diplomacy.Set(a, b, new Relation
                {
                    AtWar = r.Bool(), WarStartedTurn = r.Int(), PeaceUntilTurn = r.Int(), OpenBorders = r.Bool(), DefensivePact = r.Bool(),
                });
            }
            for (int n = r.Int(), i = 0; i < n; i++) g.Diplomacy.Declarations.Add((r.Int(), r.Int(), r.Int()));
            for (int n = r.Int(), i = 0; i < n; i++)
                g.Diplomacy.Pending.Add(new Proposal { FromId = r.Int(), ToId = r.Int(), Kind = (Treaty)r.Int(), Turn = r.Int() });

            for (int n = r.Int(), i = 0; i < n; i++)
            {
                int id = r.Int();
                var tiles = new HashSet<HexCoord>(r.Hexes());
                var attacker = new BattleSide(BattleSideId.Attacker, g.Player(r.Int()), r.Hex());
                var defender = new BattleSide(BattleSideId.Defender, g.Player(r.Int()), r.Hex());
                var objective = r.OptHex();
                int objectiveCity = r.Int();
                var b = new Battle(id, map, tiles, attacker, defender, g.Rng, objective, objectiveCity)
                {
                    PolicyStrength = pid => content.Policy(pid).CombatStrengthBonus,
                };
                b.ReadState(r, uid => units.TryGetValue(uid, out var u) ? u : null, aid => g._armies[aid]);
                g._battles[b.Id] = b;
            }

            g._nextUnitId = nextUnit; g._nextArmyId = nextArmy; g._nextCityId = nextCity; g._nextBattleId = nextBattle;
            return g;
        }
    }
}
