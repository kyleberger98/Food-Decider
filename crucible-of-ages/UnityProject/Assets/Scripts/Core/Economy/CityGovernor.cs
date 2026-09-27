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
            { "monument", "granary", "market", "library", "walls", "workshop", "colosseum", "castle" };

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

            // Never leave the empire without at least one defender per city.
            if (units < cities && BestUnit(game, city) is ProductionItem defender) return defender;

            if (owner.Happiness < 2 && EconomyRules.CanBuild(game, city, ProductionItem.Building("colosseum")))
                return ProductionItem.Building("colosseum");

            int netGold = EconomyRules.EmpireIncome(game, owner).Gold;
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

        /// <summary>The strongest land unit this city can build.</summary>
        static ProductionItem? BestUnit(GameState game, City city)
        {
            var best = game.Content.Units
                .Where(u => u.IsMilitary && u.Class != UnitClass.Recon)
                .Where(u => EconomyRules.CanBuild(game, city, ProductionItem.Unit(u.Id)))
                .OrderByDescending(u => System.Math.Max(u.CombatStrength, u.RangedStrength))
                .ThenBy(u => u.Id)
                .FirstOrDefault();
            return best == null ? (ProductionItem?)null : ProductionItem.Unit(best.Id);
        }
    }
}
