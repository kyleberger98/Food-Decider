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
        public bool InBattle => BattleId >= 0;

        /// <summary>World movement of the slowest unit (plus a per-unit bonus, e.g. faction ability).</summary>
        public int MaxWorldMovement(Func<Unit, int> bonus = null) =>
            _units.Count == 0 ? 0 : _units.Min(u => u.Def.WorldMovement + (bonus?.Invoke(u) ?? 0));

        /// <summary>Adds a unit if the army is below <paramref name="armyCap"/>.</summary>
        public bool TryAdd(Unit unit, int armyCap)
        {
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            if (unit.OwnerId != OwnerId) throw new InvalidOperationException("Cannot add another player's unit.");
            if (_units.Count >= armyCap || _units.Contains(unit)) return false;
            _units.Add(unit);
            return true;
        }

        public bool Remove(Unit unit) => _units.Remove(unit);

        public int RemoveDead() => _units.RemoveAll(u => !u.IsAlive);

        public override string ToString() => $"Army#{Id} P{OwnerId} @{Position} [{string.Join(", ", _units)}]";
    }
}
