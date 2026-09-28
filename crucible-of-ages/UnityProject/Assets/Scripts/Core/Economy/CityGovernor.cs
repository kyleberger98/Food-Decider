using System.Collections.Generic;
using System.Linq;
using Crucible.Core.Content;
using Crucible.Core.Empire;
using Crucible.Core.Game;
using Crucible.Core.Hex;

namespace Crucible.Core.Economy
{
    /// <summary>
    /// Default city management (GDD §6.2): picks the tiles citizens work and what to build when
    /// the queue is empty. The AI always uses it; for humans it fills in until they choose.
    /// </summary>
    public static class CityGovernor
    {
        static readonly string[] BuildingPriority =
            { "monument", "shrine", "granary", "market", "library", "harbor", "walls", "temple", "workshop", "amphitheater",
              "university", "bank", "colosseum", "castle", "stock_exchange", "public_school", "factory",
              "stadium", "broadcast_tower", "research_lab", "data_center" };

        /// <summary>Tiles this city's citizens could work: its own territory within 3 rings, minus the centre.</summary>
        public static IEnumerable<HexCoord> WorkableTiles(GameState game, City city) =>
            city.Position.Range(City.WorkRadius).Where(c =>
            {
                if (c == city.Position) return false;
                var t = game.Map.Get(c);
                if (t == null || t.OwnerCityId != city.Id) return false;
                var army = game.ArmyAt(c);
                return army == null || !game.AtWar(army.OwnerId, city.OwnerId);
            });

        /// <summary>Food-first scoring (growth drives everything else in Civ V), then production, then gold/science.</summary>
        public static int Score(Yields y) => y.Food * 4 + y.Production * 3 + y.Gold * 2 + y.Science * 2 + y.Culture;

        public static void AssignCitizens(GameState game, City city)
        {
            city.WorkedTiles.Clear();
            foreach (var c in WorkableTiles(game, city)
                         .OrderByDescending(c => Score(EconomyRules.TileYields(game.Map.Get(c), game.Map)))
                         .ThenBy(c => c.Q).ThenBy(c => c.R)
                         .Take(city.Population))
                city.WorkedTiles.Add(c);
        }

        /// <summary>Next item for an idle city, or null if nothing is worth building.</summary>
        public static ProductionItem? ChooseProduction(GameState game, City city)
        {
            var owner = game.Player(city.OwnerId);
            int cities = game.Cities.Count(c => c.OwnerId == owner.Id);
            int units = EconomyRules.MilitaryUnitCount(game, owner);

            // Never leave the empire, or this city, without a defender.
            var garrison = game.ArmyAt(city.Position);
            bool garrisoned = garrison != null && garrison.OwnerId == owner.Id && garrison.Units.Any(u => u.Def.IsMilitary && u.BoundToCityId < 0);
            if ((units < cities || (!garrisoned && owner.IsAI)) && BestUnit(game, city) is ProductionItem defender) return defender;

            // One worker per city while there is land to improve.
            int workers = game.Armies.Where(a => a.OwnerId == owner.Id)
                .Sum(a => a.Units.Count(u => u.Def.Id == DefaultContent.WorkerUnit));
            bool improvable = game.Map.Tiles.Any(t => t.OwnerPlayerId == owner.Id &&
                                                      Improvements.Best(owner, t) != World.ImprovementType.None);
            if (workers < cities && improvable) return ProductionItem.Unit(DefaultContent.WorkerUnit);

            // Strategic AI plan: expand, then arm up to its military target while it can pay upkeep.
            int netGold = EconomyRules.EmpireIncome(game, owner).Gold;
            if (owner.AIWantsSettlers && city.Population >= 2 && owner.Happiness >= 1 &&
                EconomyRules.CanBuild(game, city, ProductionItem.Unit(DefaultContent.SettlerUnit)) &&
                !SettlerPending(game, owner.Id))
                return ProductionItem.Unit(DefaultContent.SettlerUnit);
            if (units < owner.AIMilitaryTarget && netGold >= 1 && BestUnit(game, city) is ProductionItem soldier)
                return soldier;
            // Air cover: one fighter per city once flight is known.
            if (owner.IsAI && city.AirUnits.Count == 0 && netGold >= 2 && BestAirUnit(game, city) is ProductionItem plane)
                return plane;

            // Space race: the most productive city with a factory builds Apollo, then the parts.
            if (!owner.IsCityState)
            {
                var project = game.Content.Projects
                    .Select(p => ProductionItem.Project(p.Id))
                    .FirstOrDefault(p => EconomyRules.CanBuild(game, city, p));
                bool bestCity = game.Cities.Where(c => c.OwnerId == owner.Id)
                    .OrderByDescending(c => EconomyRules.CityYields(game, c).Production).ThenBy(c => c.Id)
                    .Take(2).Contains(city);
                if (project.Id != null && bestCity) return project;
            }

            if (owner.Happiness < 2 && EconomyRules.CanBuild(game, city, ProductionItem.Building("colosseum")))
                return ProductionItem.Building("colosseum");

            foreach (var id in BuildingPriority)
            {
                var item = ProductionItem.Building(id);
                if (!EconomyRules.CanBuild(game, city, item)) continue;
                // Don't build into bankruptcy: upkeep must be covered by current net income.
                var def = game.Content.Building(id);
                if (netGold - def.Maintenance + def.Yields.Gold < 0) continue;
                return item;
            }

            // Garrison: more units while within the free-upkeep allowance.
            int free = EconomyRules.FreeUnitsBase + EconomyRules.FreeUnitsPerCity * cities;
            return units < free ? BestUnit(game, city) : null;
        }

        /// <summary>A settler already exists or is being built somewhere in the empire.</summary>
        static bool SettlerPending(GameState game, int playerId) =>
            game.Armies.Any(a => a.OwnerId == playerId && a.Units.Any(u => u.Def.Id == DefaultContent.SettlerUnit)) ||
            game.Cities.Any(c => c.OwnerId == playerId && c.CurrentProduction.HasValue && c.CurrentProduction.Value.Equals(ProductionItem.Unit(DefaultContent.SettlerUnit)));

        /// <summary>The best fighter this city can build (interceptors protect the whole region).</summary>
        public static ProductionItem? BestAirUnit(GameState game, City city)
        {
            var best = game.Content.Units
                .Where(u => u.Class == UnitClass.Fighter && EconomyRules.CanBuild(game, city, ProductionItem.Unit(u.Id)))
                .OrderByDescending(u => u.RangedStrength).ThenBy(u => u.Id)
                .FirstOrDefault();
            return best == null ? (ProductionItem?)null : ProductionItem.Unit(best.Id);
        }

        /// <summary>The strongest land unit this city can build.</summary>
        public static ProductionItem? BestUnit(GameState game, City city)
        {
            var best = game.Content.Units
                .Where(u => u.IsMilitary && u.Class != UnitClass.Recon && u.Domain == UnitDomain.Land)
                .Where(u => EconomyRules.CanBuild(game, city, ProductionItem.Unit(u.Id)))
                .OrderByDescending(u => System.Math.Max(u.CombatStrength, u.RangedStrength))
                .ThenBy(u => u.Id)
                .FirstOrDefault();
            return best == null ? (ProductionItem?)null : ProductionItem.Unit(best.Id);
        }
    }
}
