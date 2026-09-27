using System;
using Crucible.Core.Content;

namespace Crucible.Core.Units
{
    [Serializable]
    public sealed class Unit
    {
        public const int MaxHp = 100;

        public int Id { get; }
        public UnitDef Def { get; }
        public int OwnerId { get; set; }
        public int Hp { get; set; } = MaxHp;
        public int Xp { get; set; }

        /// <summary>Only defends inside this city; never leaves it (besieged-city militia).</summary>
        public int BoundToCityId { get; set; } = -1;

        // --- Per-battle-turn state (reset at the start of the owning side's battle turn) ---
        public int BattleMovesLeft { get; set; }
        public bool HasAttacked { get; set; }
        public bool ActedThisTurn { get; set; }

        /// <summary>Set when the unit spent a whole battle turn without acting (+3 CS defence).</summary>
        public bool Fortified { get; set; }

        public Unit(int id, UnitDef def, int ownerId)
        {
            Id = id;
            Def = def ?? throw new ArgumentNullException(nameof(def));
            OwnerId = ownerId;
        }

        public bool IsAlive => Hp > 0;

        public void TakeDamage(int amount) => Hp = Math.Max(0, Hp - Math.Max(0, amount));

        public void Heal(int amount) => Hp = Math.Min(MaxHp, Hp + Math.Max(0, amount));

        public override string ToString() => $"{Def.Name}#{Id} ({Hp} HP)";
    }
}
