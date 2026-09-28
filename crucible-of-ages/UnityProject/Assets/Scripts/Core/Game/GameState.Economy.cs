using System.Linq;
using Crucible.Core.Content;
using Crucible.Core.Economy;
using Crucible.Core.Empire;
using Crucible.Core.Hex;
using Crucible.Core.Units;
using Crucible.Core.World;

namespace Crucible.Core.Game
{
    /// <summary>City and economy commands (M3). The per-turn rules live in <see cref="EconomyProcessor"/>.</summary>
    public sealed partial class GameState
    {
        static readonly string[] CityNames =
        {
            "Vesper", "Caldera", "Northmarch", "Ashford", "Tarsis", "Highwater", "Morrow", "Eastreach",
            "Silverbend", "Kestrel", "Duskhold", "Oakhaven", "Stonegate", "Marrowdale", "Brightwater", "Farhollow",
        };

        /// <summary>Bumped whenever tile ownership changes, so views can redraw borders.</summary>
        public int MapVersion { get; private set; }

        /// <summary>Sets what a city builds next. Progress carries over to the new item (Civ V).</summary>
        public bool SetProduction(City city, ProductionItem item)
        {
            if (!EconomyRules.CanBuild(this, city, item)) return false;
            city.CurrentProduction = item;
            return true;
        }

        // ------------------------------------------------------------------ policies

        /// <summary>Adopts a social policy if enough culture is banked and its prerequisite is held.</summary>
        public bool AdoptPolicy(Player player, string policyId)
        {
            var policy = Content.Policy(policyId);
            int cost = EconomyRules.PolicyCost(this, player);
            if (!EconomyRules.CanAdopt(this, player, policy) || player.PolicyCulture < cost) return false;
            player.PolicyCulture -= cost;
            player.Policies.Add(policy.Id);
            player.PolicyArmyCapBonus += policy.ArmyCapBonus;
            player.Happiness = EconomyRules.Happiness(this, player);
            return true;
        }

        // ------------------------------------------------------------------ workers

        /// <summary>
        /// Orders the army's worker to build an improvement on its hex. Work accrues at the start of
        /// each of the owner's turns while the army stays put; moving cancels the order (progress is kept on the tile).
        /// </summary>
        public bool StartImprovement(Army army, ImprovementType improvement)
        {
            if (army.InBattle || !WorkerAutomation.HasWorker(army)) return false;
            var tile = Map.Get(army.Position);
            if (!Improvements.CanBuild(Player(army.OwnerId), tile, improvement)) return false;
            if (tile.ImprovementInProgress != improvement)
            {
                tile.ImprovementInProgress = improvement;
                tile.ImprovementProgress = 0;
            }
            army.BuildOrder = improvement;
            army.Destination = null;
            army.WorldMovesLeft = 0;
            return true;
        }

        void ProgressImprovements(Player player)
        {
            foreach (var army in _armies.Values.Where(a => a.OwnerId == player.Id && a.BuildOrder != ImprovementType.None))
            {
                var tile = Map.Get(army.Position);
                if (!WorkerAutomation.HasWorker(army) || tile.ImprovementInProgress != army.BuildOrder)
                {
                    army.BuildOrder = ImprovementType.None;
                    continue;
                }
                // Moves stay available: walking away simply cancels the order (see MoveArmy).
                if (++tile.ImprovementProgress < Improvements.BuildTurns(army.BuildOrder)) continue;

                tile.Improvement = army.BuildOrder;
                tile.ImprovementInProgress = ImprovementType.None;
                tile.ImprovementProgress = 0;
                army.BuildOrder = ImprovementType.None;
                MapVersion++;
            }
        }

        /// <summary>Buys an item outright with gold (not while the city is besieged). Returns false if refused.</summary>
        public bool Purchase(City city, ProductionItem item)
        {
            var player = Player(city.OwnerId);
            if (city.IsBesieged || !EconomyRules.CanBuild(this, city, item)) return false;
            int price = EconomyRules.PurchaseCost(this, city, item);
            if (player.Gold < price) return false;

            bool done = item.Kind == ProductionKind.Building ? CompleteBuilding(city, item.Id) : SpawnUnit(city, item.Id) != null;
            if (!done) return false;
            player.Gold -= price;
            if (city.CurrentProduction.HasValue && city.CurrentProduction.Value.Equals(item))
            {
                city.ProductionStored = 0;
                city.CurrentProduction = null;
            }
            return true;
        }

        /// <summary>Ends a player's turn: runs the economy for their cities and treasury.</summary>
        internal void EndPlayerTurn(Player player) => EconomyProcessor.ProcessTurn(this, player);

        /// <summary>Founds a city with a settler from <paramref name="army"/> on the army's hex.</summary>
        public City FoundCityWithSettler(Army army, string name = null)
        {
            if (army.InBattle || army.WorldMovesLeft <= 0) return null;
            var settler = army.Units.FirstOrDefault(u => u.Def.Id == DefaultContent.SettlerUnit);
            if (settler == null || !CanFoundCityAt(army.Position, army.OwnerId)) return null;

            army.Remove(settler);
            if (army.IsEmpty) _armies.Remove(army.Id);
            else army.WorldMovesLeft = 0;

            bool first = !_cities.Values.Any(c => c.FounderId == army.OwnerId);
            return FoundCity(army.OwnerId, army.Position, name ?? NextCityName(), isCapital: first);
        }

        /// <summary>Land, not a mountain, at least 4 hexes from every city, and not inside a foreign border.</summary>
        public bool CanFoundCityAt(HexCoord c, int founderId)
        {
            var t = Map.Get(c);
            if (t == null || !t.IsPassableForLand || t.HasCity) return false;
            if (t.OwnerPlayerId >= 0 && t.OwnerPlayerId != founderId) return false;
            return _cities.Values.All(city => city.Position.DistanceTo(c) >= EconomyRules.MinCityDistance);
        }

        string NextCityName()
        {
            var used = new System.Collections.Generic.HashSet<string>(_cities.Values.Select(c => c.Name));
            var name = CityNames.FirstOrDefault(n => !used.Contains(n));
            return name ?? $"City {_cities.Count + 1}";
        }

        /// <summary>
        /// Places a newly built unit: into the city's garrison if there is room, else a new army on the
        /// centre or an adjacent free hex. Returns null (keep the production) when there is no room.
        /// </summary>
        public Unit SpawnUnit(City city, string unitDefId)
        {
            var unit = CreateUnit(unitDefId, city.OwnerId);
            int cap = Player(city.OwnerId).ArmyCap;

            // Aircraft go into the city's hangar.
            if (unit.Def.Domain == UnitDomain.Air)
            {
                if (city.AirUnits.Count >= Empire.City.AirCapacity) return null;
                city.AirUnits.Add(unit);
                return unit;
            }

            // Ships launch onto adjacent water, joining a friendly fleet there if it has room.
            bool naval = unit.Def.Domain == UnitDomain.Naval;
            if (naval)
            {
                foreach (var n in city.Position.Neighbors())
                    if (ArmyAt(n) is Army fleet && fleet.OwnerId == city.OwnerId && fleet.IsNaval && !fleet.InBattle && fleet.TryAdd(unit, cap))
                        return unit;
            }
            else
            {
                var garrison = ArmyAt(city.Position);
                if (garrison != null && garrison.OwnerId == city.OwnerId && !garrison.InBattle && garrison.TryAdd(unit, cap))
                    return unit;
            }

            var mobility = naval ? new Mobility(true, false, !unit.Def.CoastOnly) : Mobility.Land;
            var hex = (naval ? city.Position.Neighbors() : new[] { city.Position }.Concat(city.Position.Neighbors()))
                .Where(h => ArmyAt(h) == null && BattleCovering(h) == null && TerrainRules.CanStand(mobility, Map.Get(h)))
                .Where(h => CityAt(h) == null || CityAt(h).OwnerId == city.OwnerId)
                .Select(h => (HexCoord?)h)
                .FirstOrDefault();
            if (!hex.HasValue) return null;

            var army = new Army(_nextArmyId++, city.OwnerId, hex.Value);
            army.TryAdd(unit, cap);
            _armies[army.Id] = army;
            RefreshVisibility(city.OwnerId);
            return unit;
        }

        internal bool CompleteBuilding(City city, string buildingId)
        {
            var def = Content.Building(buildingId);
            if (!city.Buildings.Add(def.Id)) return false;
            if (def.WallTiers > 0) Map.Get(city.Position).WallTier += def.WallTiers;
            return true;
        }

        /// <summary>Border growth: claims the best unowned hex touching the city's territory within 3 rings.</summary>
        internal bool ClaimBestTile(City city)
        {
            var best = city.Position.Range(Empire.City.WorkRadius)
                .Select(Map.Get)
                .Where(t => t != null && t.OwnerPlayerId < 0)
                .Where(t => Map.NeighborsOf(t.Coord).Any(n => n.OwnerCityId == city.Id))
                .OrderByDescending(t => CityGovernor.Score(EconomyRules.TileYields(t, Map)))
                .ThenBy(t => t.Coord.DistanceTo(city.Position))
                .ThenBy(t => t.Coord.Q).ThenBy(t => t.Coord.R)
                .FirstOrDefault();
            if (best == null) return false;

            best.OwnerPlayerId = city.OwnerId;
            best.OwnerCityId = city.Id;
            city.TilesClaimed++;
            MapVersion++;
            return true;
        }

        /// <summary>Removes the owner's cheapest military unit (bankruptcy). Units in battle are exempt.</summary>
        internal void DisbandCheapestUnit(int playerId)
        {
            var pick = _armies.Values
                .Where(a => a.OwnerId == playerId && !a.InBattle)
                .SelectMany(a => a.Units.Where(u => u.Def.IsMilitary && u.BoundToCityId < 0).Select(u => (army: a, unit: u)))
                .OrderBy(p => p.unit.Def.ProductionCost).ThenBy(p => p.unit.Id)
                .FirstOrDefault();
            if (pick.unit == null) return;
            pick.army.Remove(pick.unit);
            if (pick.army.IsEmpty) _armies.Remove(pick.army.Id);
        }

        /// <summary>Hands a city's territory to its new owner.</summary>
        void TransferTerritory(City city, int newOwnerId)
        {
            foreach (var t in Map.Tiles.Where(t => t.OwnerCityId == city.Id)) t.OwnerPlayerId = newOwnerId;
            city.WorkedTiles.Clear();
            MapVersion++;
        }

        void ClaimInitialTerritory(City city)
        {
            foreach (var c in city.Position.Range(1))
            {
                var t = Map.Get(c);
                if (t == null || (t.OwnerPlayerId >= 0 && c != city.Position)) continue;
                t.OwnerPlayerId = city.OwnerId;
                t.OwnerCityId = city.Id;
            }
            MapVersion++;
            CityGovernor.AssignCitizens(this, city);
        }
    }
}
