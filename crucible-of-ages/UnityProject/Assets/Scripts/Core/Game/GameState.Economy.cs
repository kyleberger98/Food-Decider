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

            var garrison = ArmyAt(city.Position);
            if (garrison != null && garrison.OwnerId == city.OwnerId && !garrison.InBattle && garrison.TryAdd(unit, cap))
                return unit;

            var hex = new[] { city.Position }.Concat(city.Position.Neighbors())
                .Where(h => ArmyAt(h) == null && BattleCovering(h) == null && Map.Get(h) is Tile t && t.IsPassableForLand)
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
