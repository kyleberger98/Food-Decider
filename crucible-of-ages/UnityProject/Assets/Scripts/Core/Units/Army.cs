using System;
using System.Collections.Generic;
using System.Linq;
using Crucible.Core.Hex;

namespace Crucible.Core.Units
{
    /// <summary>A stack of units sharing one world tile (GDD §4.2). One military army per tile.</summary>
    [Serializable]
    public sealed class Army
    {
        readonly List<Unit> _units = new List<Unit>();

        public int Id { get; }
        public int OwnerId { get; }
        public HexCoord Position { get; set; }
        public int WorldMovesLeft { get; set; }

        /// <summary>Standing move order, continued automatically at the start of each of the owner's turns.</summary>
        public HexCoord? Destination { get; set; }

        /// <summary>Improvement this army's worker is building on its hex (cleared when it moves).</summary>
        public Crucible.Core.World.ImprovementType BuildOrder { get; set; }

        /// <summary>Workers in this army pick their own jobs each turn.</summary>
        public bool AutomatedWorkers { get; set; }

        /// <summary>Id of the battle this army is currently locked into, or -1.</summary>
        public int BattleId { get; set; } = -1;

        public Army(int id, int ownerId, HexCoord position)
        {
            Id = id;
            OwnerId = ownerId;
            Position = position;
        }

        public IReadOnlyList<Unit> Units => _units;
        public int Count => _units.Count;
        public bool IsEmpty => _units.Count == 0;

        /// <summary>A fleet: every unit is a ship. Fleets and land armies never mix.</summary>
        public bool IsNaval => _units.Count > 0 && _units[0].Def.Domain == Content.UnitDomain.Naval;
        public bool InBattle => BattleId >= 0;

        /// <summary>World movement of the slowest unit (plus a per-unit bonus, e.g. faction ability).</summary>
        public int MaxWorldMovement(Func<Unit, int> bonus = null) =>
            _units.Count == 0 ? 0 : _units.Min(u => u.Def.WorldMovement + (bonus?.Invoke(u) ?? 0));

        /// <summary>Adds a unit if the army is below <paramref name="armyCap"/>.</summary>
        public bool TryAdd(Unit unit, int armyCap)
        {
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            if (unit.OwnerId != OwnerId) throw new InvalidOperationException("Cannot add another player's unit.");
            // A Great General leads the army without taking a slot: effectively army cap +1 (GDD §4.8).
            bool general = unit.Def.GreatPerson == Content.GreatPersonType.General;
            int counted = _units.Count(u => u.Def.GreatPerson != Content.GreatPersonType.General);
            if ((!general && counted >= armyCap) || _units.Contains(unit)) return false;
            if (general && _units.Any(u => u.Def.GreatPerson == Content.GreatPersonType.General)) return false;
            if (unit.Def.Domain == Content.UnitDomain.Air) return false; // aircraft are based in cities
            if (_units.Count > 0 && (unit.Def.Domain == Content.UnitDomain.Naval) != IsNaval) return false;
            _units.Add(unit);
            return true;
        }

        public bool Remove(Unit unit) => _units.Remove(unit);

        public int RemoveDead() => _units.RemoveAll(u => !u.IsAlive);

        public override string ToString() => $"Army#{Id} P{OwnerId} @{Position} [{string.Join(", ", _units)}]";
    }
}
