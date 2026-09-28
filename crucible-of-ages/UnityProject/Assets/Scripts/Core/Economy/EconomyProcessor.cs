using System;
using System.Linq;
using Crucible.Core.Empire;
using Crucible.Core.Game;

namespace Crucible.Core.Economy
{
    /// <summary>End-of-turn economy for one player: cities grow and build, then gold, science and culture pool.</summary>
    public static class EconomyProcessor
    {
        public const int SiegeStarvationDelay = 5;
        public const int SiegeStarvationInterval = 3;

        public static void ProcessTurn(GameState game, Player player)
        {
            player.Happiness = EconomyRules.Happiness(game, player);

            int gold = 0, science = 0, culture = 0, faith = 0;
            foreach (var city in game.Cities.Where(c => c.OwnerId == player.Id).OrderBy(c => c.Id).ToList())
            {
                CityGovernor.AssignCitizens(game, city);
                var y = EconomyRules.CityYields(game, city);
                gold += y.Gold;
                science += y.Science;
                culture += y.Culture;
                faith += y.Faith;

                Grow(game, city, y);
                Build(game, city, y);
                GrowBorders(game, city, y);
                CityGovernor.AssignCitizens(game, city); // population or borders may have changed
            }

            player.Gold += gold - EconomyRules.BuildingMaintenance(game, player) - EconomyRules.UnitUpkeep(game, player);
            // Bankrupt: disband one unit per turn until the books balance (Civ V).
            if (player.Gold < 0)
            {
                game.DisbandCheapestUnit(player.Id);
                player.Gold = 0;
            }

            game.ProcessGreatPeople(player, faith);
            game.ProcessTourism(player);
            player.LifetimeCulture += culture;
            player.PolicyCulture += culture;
            if (player.IsAI) AutoAdoptPolicies(game, player);

            if (player.Tech.CurrentResearch == null) AutoPickResearch(player);
            player.Tech.AddScience(science);
            if (player.Tech.CurrentResearch == null) AutoPickResearch(player);

            player.Happiness = EconomyRules.Happiness(game, player);
        }

        /// <summary>AI: open trees in a fixed preference order (Tradition, Honor, Commerce, Liberty).</summary>
        public static void AutoAdoptPolicies(GameState game, Player player)
        {
            string[] treeOrder = { "Tradition", "Honor", "Commerce", "Liberty" };
            for (int guard = 0; guard < 5; guard++)
            {
                var next = game.Content.Policies
                    .Where(p => EconomyRules.CanAdopt(game, player, p))
                    .OrderBy(p => System.Array.IndexOf(treeOrder, p.Tree))
                    .ThenBy(p => p.Requires == null ? 1 : 0) // finish a tree before opening another
                    .FirstOrDefault();
                if (next == null || !game.AdoptPolicy(player, next.Id)) return;
            }
        }

        /// <summary>
        /// Next step toward the player's research target if one is set, else the cheapest available
        /// tech (the UI can override the choice at any time).
        /// </summary>
        public static void AutoPickResearch(Player player)
        {
            var step = player.Tech.NextTowardTarget();
            if (step != null) { player.Tech.SetResearch(step); return; }
            var next = player.Tech.Available().OrderBy(t => t.ScienceCost).ThenBy(t => t.Id).FirstOrDefault();
            if (next != null) player.Tech.SetResearch(next.Id);
        }

        static void Grow(GameState game, City city, Yields y)
        {
            city.FoodStored += EconomyRules.FoodSurplus(game, city, y);

            if (city.FoodStored < 0)
            {
                city.FoodStored = 0;
                if (city.Population > 1) city.Population--;
            }
            else if (city.FoodStored >= EconomyRules.GrowthThreshold(city.Population))
            {
                city.FoodStored -= EconomyRules.GrowthThreshold(city.Population);
                city.Population++;
            }

            if (city.IsBesieged)
            {
                int elapsed = game.Turn - city.BesiegedSinceTurn;
                if (elapsed >= SiegeStarvationDelay && (elapsed - SiegeStarvationDelay) % SiegeStarvationInterval == 0 && city.Population > 1)
                    city.Population--;
            }
        }

        static void Build(GameState game, City city, Yields y)
        {
            if (city.CurrentProduction.HasValue && !EconomyRules.CanBuild(game, city, city.CurrentProduction.Value))
                city.CurrentProduction = null; // e.g. the building was finished elsewhere or pop dropped
            game.AdvanceQueue(city);
            if (!city.CurrentProduction.HasValue) city.CurrentProduction = CityGovernor.ChooseProduction(game, city);

            city.ProductionStored += Math.Max(0, y.Production);
            if (!city.CurrentProduction.HasValue) return;

            var item = city.CurrentProduction.Value;
            int cost = EconomyRules.Cost(game, item);
            if (city.ProductionStored < cost) return;

            bool done = item.Kind == ProductionKind.Building ? game.CompleteBuilding(city, item.Id)
                : item.Kind == ProductionKind.Project ? game.CompleteProject(city, item.Id)
                : game.SpawnUnit(city, item.Id) != null;
            if (!done) return; // no room to place the unit yet: keep the progress

            city.ProductionStored -= cost;
            city.CurrentProduction = null;
            game.AdvanceQueue(city); // the queue picks up straight away, so no turn is wasted
            game.RaiseProductionCompleted(city, item);
        }

        static void GrowBorders(GameState game, City city, Yields y)
        {
            city.CultureStored += Math.Max(0, y.Culture);
            int threshold = EconomyRules.BorderGrowthThreshold(city);
            if (city.CultureStored < threshold) return;
            if (game.ClaimBestTile(city)) city.CultureStored -= threshold;
        }
    }
}
