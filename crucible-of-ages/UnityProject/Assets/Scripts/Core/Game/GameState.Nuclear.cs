using System;
using System.Collections.Generic;
using System.Linq;
using Crucible.Core.Economy;
using Crucible.Core.Empire;
using Crucible.Core.Hex;
using Crucible.Core.Units;

namespace Crucible.Core.Game
{
    /// <summary>What a nuclear strike did, for notifications and the view.</summary>
    public sealed class NuclearStrike
    {
        public int PlayerId;
        public string Weapon;
        public HexCoord Target;
        public int Radius;
        public int Turn;
        public int UnitsDestroyed;
        public int UnitsDamaged;
        public int AircraftDestroyed;
        public int ImprovementsDestroyed;

        /// <summary>(city, citizens lost) for every city caught in the blast.</summary>
        public readonly List<(City city, int lost)> Cities = new List<(City, int)>();
    }

    /// <summary>
    /// Nuclear weapons (Civ V, GDD §4.7): an Atomic Bomb or Nuclear Missile based in a city is launched
    /// at any hex within its range. Everything within its blast radius is hit: units at ground zero are
    /// destroyed and the rest badly hurt, cities lose citizens, aircraft in a city at ground zero are
    /// lost, improvements are wrecked and the land is left under fallout for <see cref="FalloutTurns"/>
    /// world turns. Every other civ remembers the strike (<see cref="Diplomacy.NuclearPenalty"/>).
    /// A strike may only touch players the launcher is at war with (and never city-states).
    /// </summary>
    public sealed partial class GameState
    {
        public const int FalloutTurns = 10;

        /// <summary>Raised after a nuclear strike has been resolved.</summary>
        public event Action<NuclearStrike> NuclearStrikeLaunched;

        /// <summary>Nuclear weapons based in the city.</summary>
        public IEnumerable<Unit> NukesIn(City city) => city.AirUnits.Where(u => u.IsAlive && u.Def.IsNuclear);

        /// <summary>Hexes a weapon with this radius would hit at the target (only those on the map).</summary>
        public IEnumerable<HexCoord> BlastArea(HexCoord target, int radius) =>
            Map.Tiles.Where(t => t.Coord.DistanceTo(target) <= radius).Select(t => t.Coord);

        /// <summary>Players (other than the launcher) whose units, cities or land lie in the blast.</summary>
        public IEnumerable<int> PlayersInBlast(int launcherId, HexCoord target, int radius)
        {
            var area = new HashSet<HexCoord>(BlastArea(target, radius));
            var owners = new HashSet<int>();
            foreach (var h in area)
            {
                var t = Map.Get(h);
                if (t.OwnerPlayerId >= 0) owners.Add(t.OwnerPlayerId);
            }
            foreach (var a in _armies.Values.Where(a => area.Contains(a.Position))) owners.Add(a.OwnerId);
            foreach (var c in _cities.Values.Where(c => area.Contains(c.Position))) owners.Add(c.OwnerId);
            owners.Remove(launcherId);
            return owners.OrderBy(id => id);
        }

        /// <summary>Why the strike can't be launched, or null if it can.</summary>
        public string NukeBlocker(City from, Unit weapon, HexCoord target)
        {
            if (from == null || weapon == null || !weapon.Def.IsNuclear || !from.AirUnits.Contains(weapon) || !weapon.IsAlive)
                return "No nuclear weapon there.";
            if (Map.Get(target) == null) return "Off the map.";
            if (from.Position.DistanceTo(target) > weapon.Def.Range) return $"Out of range ({weapon.Def.Range} hexes).";
            int radius = weapon.Def.BlastRadius;
            if (_battles.Values.Any(b => BlastArea(target, radius).Any(b.Contains)))
                return "A battle is being fought there.";
            foreach (var id in PlayersInBlast(from.OwnerId, target, radius))
            {
                var p = Player(id);
                if (p.IsCityState) return $"The blast would hit {p.Name}, a city-state.";
                if (!AtWar(from.OwnerId, id)) return $"You are at peace with {p.Name}. Declare war first.";
            }
            return null;
        }

        /// <summary>Launches the weapon at the target and resolves the blast. Null if it can't (see <see cref="NukeBlocker"/>).</summary>
        public NuclearStrike LaunchNuke(City from, Unit weapon, HexCoord target)
        {
            if (NukeBlocker(from, weapon, target) != null) return null;
            int radius = weapon.Def.BlastRadius;
            var strike = new NuclearStrike { PlayerId = from.OwnerId, Weapon = weapon.Def.Name, Target = target, Radius = radius, Turn = Turn };
            from.AirUnits.Remove(weapon); // spent

            // Armies: ground zero is annihilated; the blast weakens with distance.
            foreach (var army in _armies.Values.Where(a => a.Position.DistanceTo(target) <= radius).OrderBy(a => a.Id).ToList())
            {
                int d = army.Position.DistanceTo(target);
                foreach (var unit in army.Units.OrderBy(u => u.Id))
                {
                    unit.TakeDamage(BlastDamage(radius, d));
                    if (unit.IsAlive) strike.UnitsDamaged++;
                    else strike.UnitsDestroyed++;
                }
                army.RemoveDead();
                if (army.IsEmpty) _armies.Remove(army.Id);
            }

            // Cities lose citizens; aircraft in a city at ground zero are destroyed.
            foreach (var city in _cities.Values.Where(c => c.Position.DistanceTo(target) <= radius).OrderBy(c => c.Id).ToList())
            {
                int d = city.Position.DistanceTo(target);
                double share = d > 0 ? 0.25 : radius >= 2 ? 0.65 : 0.5;
                int lost = Math.Min(city.Population - 1, (int)Math.Ceiling(city.Population * share));
                city.Population -= Math.Max(0, lost);
                if (d == 0)
                {
                    strike.AircraftDestroyed += city.AirUnits.Count;
                    city.AirUnits.Clear();
                }
                strike.Cities.Add((city, Math.Max(0, lost)));
                CityGovernor.AssignCitizens(this, city);
            }

            // The land: improvements wrecked, fallout everywhere in the blast.
            foreach (var h in BlastArea(target, radius))
            {
                var t = Map.Get(h);
                if (t.Improvement != World.ImprovementType.None) strike.ImprovementsDestroyed++;
                t.Improvement = World.ImprovementType.None;
                t.ImprovementInProgress = World.ImprovementType.None;
                t.ImprovementProgress = 0;
                if (!t.IsWater && !t.HasCity) t.Fallout = FalloutTurns;
            }
            foreach (var army in _armies.Values.Where(a => a.BuildOrder != World.ImprovementType.None && a.Position.DistanceTo(target) <= radius))
                army.BuildOrder = World.ImprovementType.None;

            Diplomacy.NuclearStrikes.Add((from.OwnerId, Turn));
            MapVersion++;
            foreach (var p in _players) RefreshVisibility(p.Id);
            NuclearStrikeLaunched?.Invoke(strike);
            return strike;
        }

        /// <summary>Damage to a unit d hexes from ground zero: lethal at 0, then falling off to the rim.</summary>
        int BlastDamage(int radius, int d)
        {
            if (d == 0) return Unit.MaxHp;
            int core = 100 * (radius + 1 - d) / (radius + 1);
            return Math.Max(10, core + Rng.NextInt(-10, 11));
        }

        /// <summary>World turn: fallout slowly clears.</summary>
        void DecayFallout()
        {
            bool changed = false;
            foreach (var t in Map.Tiles.Where(t => t.Fallout > 0))
            {
                t.Fallout--;
                changed = true;
            }
            if (changed) MapVersion++;
        }
    }
}
