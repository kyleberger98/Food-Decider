using System;
using System.Linq;
using Crucible.Core.Content;
using Crucible.Core.Empire;
using Crucible.Core.Game;
using Crucible.Core.World;

namespace Crucible.Core.Economy
{
    /// <summary>Civ V-style yield, growth and happiness numbers (GDD §3). Pure functions over game state.</summary>
    public static class EconomyRules
    {
        public const int FoodPerCitizen = 2;
        public const int BaseHappiness = 9;
        public const int UnhappinessPerCity = 3;
        public const int UnhappinessPerCitizen = 1;
        public const int FreeUnitsBase = 2;
        public const int FreeUnitsPerCity = 2;
        public const int MinCityDistance = 4;

        /// <summary>What a tile yields when worked, before city-centre minimums.</summary>
        public static Yields WonderYields(NaturalWonder w)
        {
            switch (w)
            {
                case NaturalWonder.EmberfallGeyser: return new Yields(science: 2, faith: 2);
                case NaturalWonder.GlassDunes: return new Yields(gold: 3, culture: 2);
                case NaturalWonder.Worldspine: return new Yields(culture: 2, faith: 3);
                default: return new Yields();
            }
        }

        public static Yields TileYields(Tile t, WorldMap map)
        {
            if (t == null || t.IsIce) return new Yields();
            if (t.IsMountain) return WonderYields(t.Wonder); // only a wonder makes a peak worth working
            Yields y;
            switch (t.Terrain)
            {
                case TerrainType.Lake: y = new Yields(food: 2, gold: 1); break;
                case TerrainType.Ocean: y = new Yields(food: 1); break;
                case TerrainType.Coast: y = new Yields(food: 1, gold: 1); break;
                case TerrainType.Grassland: y = new Yields(food: 2); break;
                case TerrainType.Plains: y = new Yields(food: 1, production: 1); break;
                case TerrainType.Tundra: y = new Yields(food: 1); break;
                default: y = new Yields(); break; // desert, snow
            }

            // Hills and highlands (elevation 2–3) trade food for production, as Civ V hills do.
            if (!t.IsWater && t.Elevation >= 2) y = new Yields(production: 2);

            switch (t.Feature)
            {
                case FeatureType.Forest: y = new Yields(food: 1, production: 1 + (t.Elevation >= 2 ? 1 : 0)); break;
                case FeatureType.Jungle: y = new Yields(food: 1); break;
                case FeatureType.Marsh: y.Food = Math.Max(0, y.Food - 1); break;
                case FeatureType.Floodplain: y.Food += 2; break;
                case FeatureType.Oasis: y = new Yields(food: 3, gold: 1); break;
            }

            if (!t.IsWater && t.RiverEdges != 0) y.Gold += 1;
            y += Improvements.ResourceYields(t.Resource);
            y += Improvements.ImprovementYields(t.Improvement);
            y += WonderYields(t.Wonder);
            return y;
        }

        /// <summary>The city centre is always worked and yields at least 2 food and 1 production.</summary>
        public static Yields CenterYields(Tile t, WorldMap map)
        {
            var y = TileYields(t, map);
            y.Food = Math.Max(y.Food, 2);
            y.Production = Math.Max(y.Production, 1);
            return y;
        }

        /// <summary>Total yields of a city this turn: centre + worked tiles + buildings + palace + citizens.</summary>
        public static Yields CityYields(GameState game, City city)
        {
            var total = CenterYields(game.Map.Get(city.Position), game.Map);
            foreach (var c in city.WorkedTiles) total += TileYields(game.Map.Get(c), game.Map);
            foreach (var id in city.Buildings) total += game.Content.Building(id).Yields;
            if (IsCapital(game, city)) total += new Yields(production: 3, gold: 3, science: 3, culture: 1);
            foreach (var id in game.Player(city.OwnerId).Policies)
            {
                var policy = game.Content.Policy(id);
                total += policy.CityYields;
                if (IsCapital(game, city)) total += policy.CapitalYields;
            }
            total.Science += city.Population; // Civ V BNW: 1 science per citizen
            total.Culture += 2 * city.GreatWorks;
            if (IsCapital(game, city))
            {
                var owner = game.Player(city.OwnerId);
                total += game.CityStateBonusYields(owner);
                // Founder belief: +1 gold and +1 culture per 2 cities following the faith, anywhere.
                int followers = game.FollowerCities(owner);
                total += new Yields(gold: followers / 2, culture: followers / 2);
            }

            if (city.IsBesieged) total.Production = total.Production * 3 / 4;
            return total;
        }

        /// <summary>The palace sits in the founder's original capital while they hold it.</summary>
        public static bool IsCapital(GameState game, City city) =>
            city.IsOriginalCapital && city.OwnerId == city.FounderId;

        /// <summary>Civ V: 15 + 6(n−1) + (n−1)^1.8 food to grow from population n.</summary>
        public static int GrowthThreshold(int population)
        {
            int n = Math.Max(0, population - 1);
            return 15 + 6 * n + (int)Math.Floor(Math.Pow(n, 1.8));
        }

        /// <summary>Food added to the store this turn after citizens eat, sieges and unhappiness.</summary>
        public static int FoodSurplus(GameState game, City city, Yields yields)
        {
            int surplus = yields.Food - FoodPerCitizen * city.Population;
            if (surplus <= 0) return surplus;
            if (city.IsBesieged) return 0;
            var owner = game.Player(city.OwnerId);
            if (owner.Happiness <= -10) return 0;
            if (owner.Happiness < 0) return surplus / 4;
            return surplus;
        }

        /// <summary>Culture needed for the city's next border tile.</summary>
        public static int BorderGrowthThreshold(City city) => 15 + 10 * city.TilesClaimed + city.TilesClaimed * city.TilesClaimed;

        public static int Happiness(GameState game, Player player)
        {
            var cities = game.Cities.Where(c => c.OwnerId == player.Id).ToList();
            int buildings = cities.Sum(c => c.Buildings.Sum(b => game.Content.Building(b).Happiness));
            int policies = player.Policies.Sum(p => game.Content.Policy(p).Happiness);
            int luxuries = Improvements.ConnectedLuxuries(game, player.Id).Count() * Improvements.HappinessPerLuxury;
            // Follower belief: +1 happiness per own city following any religion.
            int faithful = cities.Count(c => c.ReligionId >= 0);
            int wonders = game.Map.Tiles.Count(t => t.OwnerPlayerId == player.Id && t.Wonder != NaturalWonder.None); // natural wonders inspire
            return BaseHappiness + buildings + policies + luxuries + faithful + wonders + game.CityStateHappiness(player)
                   - UnhappinessPerCity * cities.Count
                   - UnhappinessPerCitizen * cities.Sum(c => c.Population);
        }

        public static int MilitaryUnitCount(GameState game, Player player) =>
            game.Armies.Where(a => a.OwnerId == player.Id)
                .Sum(a => a.Units.Count(u => u.Def.IsMilitary && u.BoundToCityId < 0));

        public static int UnitUpkeep(GameState game, Player player)
        {
            int cities = game.Cities.Count(c => c.OwnerId == player.Id);
            int free = FreeUnitsBase + FreeUnitsPerCity * cities;
            return Math.Max(0, MilitaryUnitCount(game, player) - free);
        }

        public static int BuildingMaintenance(GameState game, Player player) =>
            game.Cities.Where(c => c.OwnerId == player.Id)
                .Sum(c => c.Buildings.Sum(b => game.Content.Building(b).Maintenance));

        /// <summary>Civ V: 25 + (3n)^2.01 culture for the (n+1)th policy, +10% per extra city, rounded down to 5.</summary>
        public static int PolicyCost(GameState game, Player player)
        {
            int n = player.Policies.Count;
            double cost = 25 + Math.Pow(3 * n, 2.01);
            int cities = Math.Max(1, game.Cities.Count(c => c.OwnerId == player.Id));
            cost *= 1 + 0.1 * (cities - 1);
            return (int)(cost / 5) * 5;
        }

        public static bool CanAdopt(GameState game, Player player, PolicyDef policy) =>
            !player.Policies.Contains(policy.Id) &&
            (policy.Requires == null || player.Policies.Contains(policy.Requires));

        /// <summary>Empire-wide yields per turn, with <see cref="Yields.Gold"/> as net gold after upkeep.</summary>
        public static Yields EmpireIncome(GameState game, Player player)
        {
            var total = new Yields();
            foreach (var city in game.Cities.Where(c => c.OwnerId == player.Id)) total += CityYields(game, city);
            total.Gold -= BuildingMaintenance(game, player) + UnitUpkeep(game, player);
            return total;
        }

        // ------------------------------------------------------------------ what can be built

        public static bool CanBuild(GameState game, City city, ProductionItem item)
        {
            var player = game.Player(city.OwnerId);
            if (item.Kind == ProductionKind.Unit && player.IsCityState && game.Content.Unit(item.Id).Id == DefaultContent.SettlerUnit) return false;
            if (item.Kind == ProductionKind.Project)
            {
                var pr = game.Content.Project(item.Id);
                int done = player.CompletedProjects.TryGetValue(pr.Id, out var n) ? n : 0;
                return !player.IsCityState && done < pr.MaxCount && player.Tech.Has(pr.RequiredTech) &&
                       (pr.RequiresProject == null || player.CompletedProjects.ContainsKey(pr.RequiresProject)) &&
                       (pr.RequiredBuilding == null || city.Has(pr.RequiredBuilding)) &&
                       !game.Cities.Any(c => c != city && c.OwnerId == player.Id && c.CurrentProduction.HasValue &&
                                             c.CurrentProduction.Value.Equals(item) && done + 1 >= pr.MaxCount);
            }
            if (item.Kind == ProductionKind.Building)
            {
                var b = game.Content.Building(item.Id);
                return !city.Has(b.Id) && player.Tech.Has(b.RequiredTech) && (!b.RequiresCoast || IsCoastal(game, city));
            }

            var u = game.Content.Unit(item.Id);
            if (u.Id == DefaultContent.MilitiaUnit || u.SiegeOnly || u.GreatPerson != GreatPersonType.None || !player.Tech.Has(u.RequiredTech)) return false;
            if (u.FactionId != null && u.FactionId != player.Faction.Id) return false;
            // A faction's unique unit replaces the generic one.
            if (game.Content.Units.Any(x => x.FactionId == player.Faction.Id && x.Replaces == u.Id)) return false;
            if (u.Id == DefaultContent.SettlerUnit && city.Population < 2) return false;
            if (u.Domain == UnitDomain.Naval && !IsCoastal(game, city)) return false;
            if (u.Domain == UnitDomain.Air && city.AirUnits.Count >= City.AirCapacity) return false;
            if (u.RequiredResource != null)
            {
                var r = Improvements.Parse(u.RequiredResource);
                if (Improvements.StrategicAvailable(game, player.Id, r) <= Improvements.StrategicUsed(game, player.Id, r)) return false;
            }
            return true;
        }

        /// <summary>
        /// Gold to buy an item outright: 2 gold per missing production point plus a 50% premium on the full
        /// cost, rounded to 5. Progress already made on the city's current item counts.
        /// </summary>
        public static int PurchaseCost(GameState game, City city, ProductionItem item)
        {
            int cost = Cost(game, item);
            bool current = city.CurrentProduction.HasValue && city.CurrentProduction.Value.Equals(item);
            int missing = Math.Max(0, cost - (current ? city.ProductionStored : 0));
            int gold = 2 * missing + cost / 2;
            return (gold + 4) / 5 * 5;
        }

        public static bool IsCoastal(GameState game, City city) =>
            game.Map.NeighborsOf(city.Position).Any(t => t.IsWater);

        public static int Cost(GameState game, ProductionItem item) =>
            item.Kind == ProductionKind.Building ? game.Content.Building(item.Id).ProductionCost
            : item.Kind == ProductionKind.Project ? game.Content.Project(item.Id).ProductionCost
            : game.Content.Unit(item.Id).ProductionCost;

        public static string NameOf(GameState game, ProductionItem item) =>
            item.Kind == ProductionKind.Building ? game.Content.Building(item.Id).Name
            : item.Kind == ProductionKind.Project ? game.Content.Project(item.Id).Name
            : game.Content.Unit(item.Id).Name;
    }
}
