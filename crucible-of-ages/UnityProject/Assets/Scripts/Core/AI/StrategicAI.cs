using System;
using System.Collections.Generic;
using System.Linq;
using Crucible.Core.Content;
using Crucible.Core.Economy;
using Crucible.Core.Empire;
using Crucible.Core.Game;
using Crucible.Core.Hex;
using Crucible.Core.Units;
using Crucible.Core.World;

namespace Crucible.Core.AI
{
    /// <summary>
    /// Strategic + operational AI v1 (GDD §6.1, §6.3). Each turn it:
    /// sets the economic plan for its governors (expand, arm), settles good sites, splits new units out
    /// of garrisons, counter-attacks raiders near its cities, and runs one offensive at a time:
    /// rally → march → besiege → build engines → assault. Battles are fought by the tactical AI.
    /// v1 knows where enemy cities are without scouting; fog-aware targeting comes later.
    /// </summary>
    public sealed class StrategicAI : IPlayerAI
    {
        public int TargetCities = 4;
        public int SettleSearchRadius = 9;
        public int MinDistanceFromEnemyCity = 6;
        public double AttackRatio = 1.3;
        public int SiegePatience = 5;

        /// <summary>No offensives before this turn: expand and build up first.</summary>
        public int EarliestOffensiveTurn = 30;

        /// <summary>…and not before having this many cities, unless the game has gone on this long.</summary>
        public int CitiesBeforeWar = 2;
        public int WarAnywayTurn = 60;

        public void TakeTurn(GameState game, Player player)
        {
            if (player.IsCityState)
            {
                // Minor powers sit tight: build up the city and its garrison, never expand or attack.
                player.AIWantsSettlers = false;
                player.AIMilitaryTarget = 2;
                return;
            }

            PlanEconomy(game, player);
            SplitGarrisons(game, player);
            SplitCivilians(game, player);
            EnsureGarrisons(game, player);
            foreach (var army in OwnArmies(game, player).Where(IsSettlerArmy).ToList()) ManageSettler(game, player, army);
            UseNukes(game, player);
            Defend(game, player);
            Offense(game, player);
            ConductDiplomacy(game, player);
            UseGreatPeople(game, player);
            CourtCityStates(game, player);
            SpendGold(game, player);
        }

        /// <summary>Points a strike must be worth before the AI uses a nuclear weapon (a city or a big army).</summary>
        public const int NukeThreshold = 8;

        /// <summary>
        /// One nuclear strike per turn at the enemy target worth most: units caught in the blast (double at
        /// ground zero) and citizens lost count for it, its own units and cities heavily against it.
        /// </summary>
        public static NuclearStrike UseNukes(GameState game, Player player)
        {
            var best = (score: NukeThreshold - 1, city: (City)null, weapon: (Unit)null, target: default(HexCoord));
            foreach (var city in game.Cities.Where(c => c.OwnerId == player.Id).OrderBy(c => c.Id))
            {
                var weapon = game.NukesIn(city).OrderByDescending(u => u.Def.BlastRadius).ThenBy(u => u.Id).FirstOrDefault();
                if (weapon == null) continue;
                var targets = game.Cities.Where(c => game.AtWar(player.Id, c.OwnerId)).Select(c => c.Position)
                    .Concat(game.Armies.Where(a => game.AtWar(player.Id, a.OwnerId)).Select(a => a.Position))
                    .Where(h => city.Position.DistanceTo(h) <= weapon.Def.Range)
                    .Distinct().OrderBy(h => h.Q).ThenBy(h => h.R);
                foreach (var target in targets)
                {
                    int score = NukeValue(game, player, target, weapon.Def.BlastRadius);
                    if (score > best.score && game.NukeBlocker(city, weapon, target) == null) best = (score, city, weapon, target);
                }
            }
            return best.weapon == null ? null : game.LaunchNuke(best.city, best.weapon, best.target);
        }

        static int NukeValue(GameState game, Player player, HexCoord target, int radius)
        {
            int score = 0;
            foreach (var army in game.Armies.Where(a => a.Position.DistanceTo(target) <= radius))
            {
                int per = army.OwnerId == player.Id ? -3 : game.AtWar(player.Id, army.OwnerId) ? (army.Position == target ? 2 : 1) : -100;
                score += per * army.Units.Count(u => u.Def.IsMilitary);
            }
            foreach (var city in game.Cities.Where(c => c.Position.DistanceTo(target) <= radius))
                score += city.OwnerId == player.Id ? -20 : city.Population / (city.Position == target ? 1 : 3);
            return score;
        }

        /// <summary>Sue for peace when losing; seek open borders and pacts with civs it likes and has no designs on.</summary>
        void ConductDiplomacy(GameState game, Player player)
        {
            foreach (var other in game.MajorPlayers.Where(p => p != player && !p.IsEliminated).ToList())
            {
                var rel = game.Diplomacy.Get(player.Id, other.Id);
                if (rel.AtWar)
                {
                    bool losing = DiplomacyAI.MilitaryStrength(game, player) < 0.6 * DiplomacyAI.MilitaryStrength(game, other);
                    if (losing && game.CanPropose(player, other, Treaty.Peace)) game.Propose(player, other, Treaty.Peace);
                    continue;
                }
                if (DiplomacyAI.PlansWarOn(game, player, other)) continue;
                int opinion = game.Opinion(player, other);
                if (opinion >= 0 && game.Turn % 10 == 0 && game.CanPropose(player, other, Treaty.OpenBorders))
                    game.Propose(player, other, Treaty.OpenBorders);
                if (opinion >= 25 && game.Turn % 10 == 5 && game.CanPropose(player, other, Treaty.DefensivePact))
                    game.Propose(player, other, Treaty.DefensivePact);
            }
        }

        /// <summary>Great people act at once; generals ride with the largest field army.</summary>
        void UseGreatPeople(GameState game, Player player)
        {
            foreach (var army in OwnArmies(game, player).Where(a => !a.InBattle).ToList())
                foreach (var person in army.Units.Where(u => u.Def.GreatPerson != GreatPersonType.None &&
                                                             u.Def.GreatPerson != GreatPersonType.General).ToList())
                    if (game.Army(army.Id) != null) game.UseGreatPerson(army, person);

            var spearhead = FieldArmies(game, player).Where(a => !a.Units.Any(IsGeneral))
                .OrderByDescending(a => a.Count).ThenBy(a => a.Id).FirstOrDefault();
            if (spearhead == null) return;
            foreach (var generalArmy in OwnArmies(game, player).Where(a => !a.InBattle && a.Units.All(u => !u.Def.IsMilitary) && a.Units.Any(IsGeneral)).ToList())
            {
                if (generalArmy.Position.DistanceTo(spearhead.Position) <= 1) game.MergeArmies(generalArmy, spearhead);
                else if (FreeNeighbor(game, spearhead.Position) is HexCoord meet) game.OrderMove(generalArmy, meet);
            }
        }

        static bool IsGeneral(Unit u) => u.Def.GreatPerson == GreatPersonType.General;

        /// <summary>
        /// Gold for influence: keep the best-liked city-state as an ally, especially once the World Congress
        /// sits (their delegates decide World Leader votes).
        /// </summary>
        void CourtCityStates(GameState game, Player player)
        {
            int budget = game.WorldCongressFounded ? 250 : 400;
            if (player.Gold - GoldReserve < budget + 100) return;
            var target = game.Players.Where(p => p.IsCityState && !p.IsEliminated)
                .Where(cs => game.AllyOf(cs) != player.Id || game.InfluenceOf(cs, player.Id) < GameState.AllyInfluence + 15)
                .OrderByDescending(cs => game.InfluenceOf(cs, player.Id)).ThenBy(cs => cs.Id)
                .FirstOrDefault();
            if (target != null) game.GiftGold(player, target, budget);
        }

        /// <summary>Gold kept in reserve for emergencies.</summary>
        public int GoldReserve = 60;

        /// <summary>
        /// Buys with surplus gold: defenders first for threatened cities, then soldiers while under the
        /// military target, then whatever each city is building (cheapest first).
        /// </summary>
        void SpendGold(GameState game, Player player)
        {
            var cities = game.Cities.Where(c => c.OwnerId == player.Id && !c.IsBesieged).ToList();
            bool Threatened(City c) => game.Armies.Any(a => game.AtWar(a.OwnerId, player.Id) && HasMilitary(a) && a.Position.DistanceTo(c.Position) <= 4);

            for (int guard = 0; guard < 6; guard++)
            {
                if (player.Gold <= GoldReserve) return;
                bool bought = false;
                foreach (var city in cities.OrderByDescending(Threatened).ThenBy(c => EconomyRules.CityYields(game, c).Production).ThenBy(c => c.Id))
                {
                    ProductionItem? want;
                    if (Threatened(city) || EconomyRules.MilitaryUnitCount(game, player) < player.AIMilitaryTarget)
                        want = CityGovernor.BestUnit(game, city);
                    else
                        want = city.CurrentProduction ?? CityGovernor.ChooseProduction(game, city);
                    if (!want.HasValue) continue;
                    if (EconomyRules.PurchaseCost(game, city, want.Value) > player.Gold - GoldReserve) continue;
                    if (game.Purchase(city, want.Value)) { bought = true; break; }
                }
                if (!bought) return;
            }
        }

        // ------------------------------------------------------------------ economy plan

        void PlanEconomy(GameState game, Player player)
        {
            int cities = game.Cities.Count(c => c.OwnerId == player.Id);
            player.AIWantsSettlers = cities < TargetCities;
            // Garrisons plus one full assault army.
            player.AIMilitaryTarget = cities + player.ArmyCap;
        }

        // ------------------------------------------------------------------ settlers

        static bool IsSettlerArmy(Army a) =>
            !a.InBattle && a.Units.Any(u => u.Def.Id == DefaultContent.SettlerUnit) && a.Units.All(u => !u.Def.IsMilitary);

        void ManageSettler(GameState game, Player player, Army army)
        {
            if (army.Destination.HasValue) return; // still marching (orders continue at turn start)

            var here = army.Position;
            var best = FindCitySite(game, player, here);
            if (best == null) return;

            if (best.Value == here || (SiteScore(game, player, here) >= SiteScore(game, player, best.Value) - 2 && game.CanFoundCityAt(here, player.Id)))
            {
                if (army.WorldMovesLeft > 0) game.FoundCityWithSettler(army);
                return;
            }
            game.OrderMove(army, best.Value);
        }

        HexCoord? FindCitySite(GameState game, Player player, HexCoord from) =>
            from.Range(SettleSearchRadius)
                .Where(c => game.Map.InBounds(c))
                .Select(c => (hex: c, score: SiteScore(game, player, c) - 1.5 * c.DistanceTo(from)))
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score).ThenBy(x => x.hex.Q).ThenBy(x => x.hex.R)
                .Select(x => (HexCoord?)x.hex)
                .FirstOrDefault();

        double SiteScore(GameState game, Player player, HexCoord c)
        {
            if (!game.CanFoundCityAt(c, player.Id)) return double.MinValue;
            if (game.Cities.Any(city => city.OwnerId != player.Id && city.Position.DistanceTo(c) < MinDistanceFromEnemyCity))
                return double.MinValue;
            var own = game.Cities.Where(city => city.OwnerId == player.Id).ToList();
            if (own.Count > 0 && own.Min(city => city.Position.DistanceTo(c)) > 8) return double.MinValue; // stay compact

            double score = 0;
            foreach (var h in c.Range(2))
            {
                var t = game.Map.Get(h);
                if (t == null || (t.OwnerPlayerId >= 0 && t.OwnerPlayerId != player.Id)) continue;
                score += CityGovernor.Score(EconomyRules.TileYields(t, game.Map));
                var kind = Improvements.KindOf(t.Resource);
                if (kind == ResourceKind.Strategic || kind == ResourceKind.Luxury) score += 6;
            }
            if (game.Map.Get(c).RiverEdges != 0) score += 4; // river city: gold and a moat
            return score / 4.0;
        }

        // ------------------------------------------------------------------ garrisons

        /// <summary>
        /// New units spawn into the city garrison. Keep the best defender home and move everything else
        /// out: military units become a field army, civilians become their own army.
        /// </summary>
        void SplitGarrisons(GameState game, Player player)
        {
            foreach (var city in game.Cities.Where(c => c.OwnerId == player.Id).ToList())
            {
                var garrison = game.ArmyAt(city.Position);
                if (garrison == null || garrison.OwnerId != player.Id || garrison.InBattle) continue;

                var civilians = garrison.Units.Where(u => !u.Def.IsMilitary).ToList();
                foreach (var civ in civilians)
                {
                    var hex = FreeNeighbor(game, city.Position);
                    if (!hex.HasValue || garrison.Count <= 1) break;
                    game.SplitArmy(garrison, new[] { civ }, hex.Value);
                }

                var soldiers = garrison.Units.Where(u => u.Def.IsMilitary && u.BoundToCityId < 0).ToList();
                if (soldiers.Count <= 1) continue;
                var keeper = soldiers.OrderBy(u => u.Def.IsRanged ? 1 : 0).ThenByDescending(u => u.Def.CombatStrength).First();
                var leaving = soldiers.Where(u => u != keeper).ToList();
                var exit = FreeNeighbor(game, city.Position);
                if (exit.HasValue && leaving.Count < garrison.Count) game.SplitArmy(garrison, leaving, exit.Value);
            }
        }

        /// <summary>Every city keeps a real garrison: an empty city pulls one unit from the nearest field army.</summary>
        void EnsureGarrisons(GameState game, Player player)
        {
            foreach (var city in game.Cities.Where(c => c.OwnerId == player.Id).ToList())
            {
                var here = game.ArmyAt(city.Position);
                if (here != null && here.OwnerId == player.Id && HasMilitary(here)) continue;
                if (here != null) continue; // occupied by someone else (e.g. an enemy we can't displace)
                if (FieldArmies(game, player).Any(a => a.Destination == city.Position)) continue; // already coming

                var source = FieldArmies(game, player)
                    .Where(a => !game.IsEmbarked(a))
                    .OrderBy(a => a.Position.DistanceTo(city.Position)).ThenBy(a => a.Id)
                    .FirstOrDefault();
                if (source == null) continue;

                if (source.Count == 1 || source.Position.DistanceTo(city.Position) <= 1)
                {
                    if (source.Count == 1) game.OrderMove(source, city.Position);
                    else
                    {
                        var guard = source.Units.OrderBy(u => u.Def.IsRanged ? 1 : 0).ThenByDescending(u => u.Def.CombatStrength).First();
                        game.SplitArmy(source, new[] { guard }, city.Position);
                    }
                    continue;
                }

                var defender = source.Units.OrderBy(u => u.Def.IsRanged ? 1 : 0).ThenByDescending(u => u.Def.CombatStrength).First();
                var hex = FreeNeighbor(game, source.Position);
                if (!hex.HasValue) continue;
                var detached = game.SplitArmy(source, new[] { defender }, hex.Value);
                if (detached != null) detached.Destination = city.Position; // marches next turn
            }
        }

        /// <summary>Settlers and workers travelling inside a field army go their own way.</summary>
        static void SplitCivilians(GameState game, Player player)
        {
            foreach (var army in OwnArmies(game, player).Where(a => !a.InBattle && game.CityAt(a.Position) == null).ToList())
            {
                var civilians = army.Units.Where(u => !u.Def.IsMilitary).ToList();
                if (civilians.Count == 0 || civilians.Count == army.Count) continue;
                var hex = FreeNeighbor(game, army.Position);
                if (hex.HasValue) game.SplitArmy(army, civilians, hex.Value);
            }
        }

        static HexCoord? FreeNeighbor(GameState game, HexCoord c) =>
            c.Neighbors()
                .Where(n => game.Map.Get(n) is Tile t && t.IsPassableForLand && game.ArmyAt(n) == null &&
                            game.CityAt(n) == null && game.BattleCovering(n) == null)
                .OrderBy(n => n.Q).ThenBy(n => n.R)
                .Select(n => (HexCoord?)n)
                .FirstOrDefault();

        // ------------------------------------------------------------------ defence

        void Defend(GameState game, Player player)
        {
            var ownCities = game.Cities.Where(c => c.OwnerId == player.Id).ToList();
            var raiders = game.Armies
                .Where(a => game.AtWar(a.OwnerId, player.Id) && !a.InBattle && HasMilitary(a) &&
                            ownCities.Any(c => c.Position.DistanceTo(a.Position) <= 3))
                .ToList();

            // Recall: field armies near a threatened city come back to fight in front of its walls.
            foreach (var raider in raiders)
            {
                var threatened = ownCities.OrderBy(c => c.Position.DistanceTo(raider.Position)).First();
                foreach (var army in FieldArmies(game, player).Where(a => a.Position.DistanceTo(threatened.Position) <= 8 &&
                                                                          a.Position.DistanceTo(raider.Position) > 1))
                {
                    var meet = raider.Position.Neighbors()
                        .Where(n => TerrainRules.CanStand(game.MobilityOf(army), game.Map.Get(n)) && game.ArmyAt(n) == null && game.CityAt(n) == null)
                        .OrderBy(n => n.DistanceTo(army.Position)).ThenBy(n => n.Q).ThenBy(n => n.R)
                        .Select(n => (HexCoord?)n).FirstOrDefault();
                    if (meet.HasValue) game.OrderMove(army, meet.Value);
                }
            }

            foreach (var raider in raiders)
            {
                var responder = OwnArmies(game, player)
                    .Where(a => !a.InBattle && a.WorldMovesLeft > 0 && HasMilitary(a) && a.Position.DistanceTo(raider.Position) == 1)
                    .OrderByDescending(Strength)
                    .FirstOrDefault();
                if (responder != null && Strength(responder) >= AttackRatio * Strength(raider))
                    game.Attack(responder, raider.Position);
            }
        }

        // ------------------------------------------------------------------ offence

        void Offense(GameState game, Player player)
        {
            int cities = game.Cities.Count(c => c.OwnerId == player.Id);
            if (game.Turn < EarliestOffensiveTurn || (cities < CitiesBeforeWar && game.Turn < WarAnywayTurn)) return;

            var target = game.City(player.AITargetCityId);
            if (target == null || target.OwnerId == player.Id || game.Player(target.OwnerId).IsCityState ||
                (!game.AtWar(target.OwnerId, player.Id) && game.CannotDeclareWar(player, game.Player(target.OwnerId)) != null))
                target = PickTarget(game, player);
            player.AITargetCityId = target?.Id ?? -1;
            if (target == null) return;

            var field = FieldArmies(game, player);
            if (field.Count == 0) return;

            // The biggest field army leads; the others join it.
            var spearhead = field.OrderByDescending(a => a.Count).ThenByDescending(Strength).ThenBy(a => a.Id).First();
            foreach (var other in field.Where(a => a != spearhead))
            {
                if (other.Position.DistanceTo(spearhead.Position) <= 1) game.MergeArmies(other, spearhead);
                else if (!other.Destination.HasValue || other.Destination.Value.DistanceTo(spearhead.Position) > 1)
                {
                    var meet = FreeNeighbor(game, spearhead.Position);
                    if (meet.HasValue) game.OrderMove(other, meet.Value);
                }
            }

            int ready = Math.Max(2, player.ArmyCap - 1); // leave a slot for a siege engine
            if (spearhead.Count < ready || spearhead.InBattle) return;

            // Ready to march: declare war first if we're at peace (treaties permitting).
            if (!game.AtWar(player.Id, target.OwnerId) && !game.DeclareWar(player, game.Player(target.OwnerId))) return;

            // Opportunistic field battle on the way.
            var blocker = spearhead.Position.Neighbors().Select(game.ArmyAt)
                .FirstOrDefault(a => a != null && game.AtWar(a.OwnerId, player.Id) && !a.InBattle && HasMilitary(a) &&
                                     game.CityAt(a.Position) == null);
            if (blocker != null && spearhead.WorldMovesLeft > 0 && Strength(spearhead) >= AttackRatio * Strength(blocker))
            {
                game.Attack(spearhead, blocker.Position);
                return;
            }

            if (spearhead.Position.DistanceTo(target.Position) > 1)
            {
                if (!spearhead.Destination.HasValue || spearhead.Destination.Value.DistanceTo(target.Position) > 1)
                {
                    var approach = target.Position.Neighbors()
                        .Where(n => game.Map.Get(n) is Tile t && t.IsPassableForLand && game.ArmyAt(n) == null)
                        .OrderBy(n => n.DistanceTo(spearhead.Position)).ThenBy(n => n.Q).ThenBy(n => n.R)
                        .Select(n => (HexCoord?)n)
                        .FirstOrDefault(n => Pathfinder.Find(game, spearhead, n.Value) != null);
                    if (approach.HasValue) game.OrderMove(spearhead, approach.Value);
                }
                return;
            }

            // At the walls.
            if (!target.IsBesieged) game.DeclareSiege(spearhead, target);
            if (target.BesiegerId == player.Id) BuildEngines(game, target, spearhead);

            int siegeTurns = target.IsBesieged ? game.Turn - target.BesiegedSinceTurn : 0;
            int wallTier = game.Map.Get(target.Position).WallTier;
            bool engines = spearhead.Units.Any(u => u.Def.CarriesOverWalls || u.Def.AttacksWallsOnly || u.Def.WallDamageMultiplier > 1);
            bool wallsHandled = wallTier == 0 || engines || siegeTurns >= SiegePatience;
            bool strongEnough = Strength(spearhead) >= AttackRatio * CityDefense(game, target) || siegeTurns >= 2 * SiegePatience;
            if (wallsHandled && strongEnough && spearhead.WorldMovesLeft > 0) game.Attack(spearhead, target.Position);
        }

        static void BuildEngines(GameState game, City city, Army army)
        {
            foreach (var id in new[] { "siege_tower", "battering_ram", "trebuchet", "catapult" })
            {
                var def = game.Content.Unit(id);
                if (!game.AvailableSiegeEngines(city).Contains(def) || army.Units.Any(u => u.Def.Id == id)) continue;
                if (city.SiegeProgress >= def.SiegeProgressCost) game.BuildSiegeEngine(city, army, id);
            }
        }

        City PickTarget(GameState game, Player player)
        {
            var home = game.Cities.Where(c => c.OwnerId == player.Id).OrderByDescending(c => c.IsOriginalCapital).FirstOrDefault();
            if (home == null) return null;
            // Enemies we're already fighting first, then majors we could declare on and don't like much.
            return game.Cities
                .Where(c => !game.Player(c.OwnerId).IsCityState && c.OwnerId != player.Id)
                .Where(c => game.AtWar(c.OwnerId, player.Id) ||
                            (game.CannotDeclareWar(player, game.Player(c.OwnerId)) == null && game.Opinion(player, game.Player(c.OwnerId)) < 20))
                .OrderBy(c => game.AtWar(c.OwnerId, player.Id) ? 0 : 1)
                .OrderBy(c => c.Position.DistanceTo(home.Position))
                .ThenBy(c => CityDefense(game, c))
                .ThenBy(c => c.Id)
                .FirstOrDefault();
        }

        // ------------------------------------------------------------------ helpers

        static IEnumerable<Army> OwnArmies(GameState game, Player player) => game.Armies.Where(a => a.OwnerId == player.Id);

        List<Army> FieldArmies(GameState game, Player player) =>
            OwnArmies(game, player)
                .Where(a => !a.InBattle && HasMilitary(a) && a.Units.All(u => u.Def.IsMilitary && u.BoundToCityId < 0) &&
                            game.CityAt(a.Position) == null)
                .ToList();

        static bool HasMilitary(Army a) => a.Units.Any(u => u.Def.IsMilitary && u.IsAlive);

        public static double Strength(Army army) =>
            army.Units.Where(u => u.Def.IsMilitary && u.Def.CanAttack)
                .Sum(u => Math.Max(u.Def.CombatStrength, u.Def.RangedStrength) * u.Hp / (double)Unit.MaxHp);

        /// <summary>Garrison + militia that would rise + walls.</summary>
        public static double CityDefense(GameState game, City city)
        {
            var garrison = game.ArmyAt(city.Position);
            double g = garrison != null && garrison.OwnerId == city.OwnerId ? Strength(garrison) : 0;
            double militia = city.IsBesieged ? 0 : city.MilitiaCount * game.Content.Unit(DefaultContent.MilitiaUnit).CombatStrength;
            return g + militia + game.Map.Get(city.Position).WallTier * 12;
        }
    }
}
