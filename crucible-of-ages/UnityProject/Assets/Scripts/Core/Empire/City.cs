using System;
using Crucible.Core.Hex;

namespace Crucible.Core.Empire
{
    [Serializable]
    public sealed class City
    {
        public int Id { get; }
        public string Name { get; set; }
        public int OwnerId { get; set; }
        public HexCoord Position { get; }
        public int Population { get; set; } = 1;

        /// <summary>Capital of the player who founded it (domination victory tracks these).</summary>
        public bool IsOriginalCapital { get; set; }
        public int FounderId { get; }

        // --- Siege state (GDD §4.6) ---
        public bool IsBesieged => BesiegedSinceTurn >= 0;
        public int BesiegedSinceTurn { get; set; } = -1;

        /// <summary>Siege progress banked by the besiegers, spent on rams/towers/catapults.</summary>
        public int SiegeProgress { get; set; }

        public City(int id, string name, int ownerId, HexCoord position)
        {
            Id = id;
            Name = name;
            OwnerId = ownerId;
            FounderId = ownerId;
            Position = position;
        }

        /// <summary>Militia spawned when a siege begins: ⌊pop/4⌋ + 1.</summary>
        public int MilitiaCount => Population / 4 + 1;

        public override string ToString() => $"{Name} (P{OwnerId}, pop {Population})";
    }
}
