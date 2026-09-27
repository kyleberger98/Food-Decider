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

        sealed class Memory
        {
            public int TargetCityId = -1;
        }

        readonly Dictionary<int, Memory> _memory = new Dictionary<int, Memory>();

        public void TakeTurn(GameState game, Player player)
        {
            if (!_memory.TryGetValue(player.Id, out var memory)) _memory[player.Id] = memory = new Memory();

            PlanEconomy(game, player);
            SplitGarrisons(game, player);
            foreach (var army in OwnArmies(game, player).Where(IsSettlerArmy).ToList()) ManageSettler(game, player, army);
            Defend(game, player);
            Offense(game, player, memory);
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

        void Offense(GameState game, Player player, Memory memory)
        {
            var target = game.City(memory.TargetCityId);
            if (target == null || !game.AtWar(target.OwnerId, player.Id)) target = PickTarget(game, player);
            memory.TargetCityId = target?.Id ?? -1;
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
            return game.Cities
                .Where(c => game.AtWar(c.OwnerId, player.Id))
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
