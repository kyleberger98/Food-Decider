using System;
using System.Collections.Generic;
using Crucible.Core.Hex;

namespace Crucible.Core.Empire
{
    public enum ProductionKind
    {
        Unit,
        Building,
    }

    /// <summary>What a city is building: a unit or a building, by definition id.</summary>
    [Serializable]
    public readonly struct ProductionItem : IEquatable<ProductionItem>
    {
        public readonly ProductionKind Kind;
        public readonly string Id;

        public ProductionItem(ProductionKind kind, string id)
        {
            Kind = kind;
            Id = id;
        }

        public static ProductionItem Unit(string id) => new ProductionItem(ProductionKind.Unit, id);
        public static ProductionItem Building(string id) => new ProductionItem(ProductionKind.Building, id);

        public bool Equals(ProductionItem o) => Kind == o.Kind && Id == o.Id;
        public override bool Equals(object obj) => obj is ProductionItem p && Equals(p);
        public override int GetHashCode() => ((int)Kind * 397) ^ (Id?.GetHashCode() ?? 0);
        public override string ToString() => $"{Kind}:{Id}";
    }

    [Serializable]
    public sealed class City
    {
        /// <summary>Workable radius around the city centre (Civ V: 3 rings).</summary>
        public const int WorkRadius = 3;

        public int Id { get; }
        public string Name { get; set; }
        public int OwnerId { get; set; }
        public HexCoord Position { get; }
        public int Population { get; set; } = 1;

        /// <summary>Capital of the player who founded it (domination victory tracks these).</summary>
        public bool IsOriginalCapital { get; set; }
        public int FounderId { get; }

        // --- Economy (GDD §3 / M3) ---
        public int FoodStored { get; set; }
        public int ProductionStored { get; set; }
        public int CultureStored { get; set; }

        /// <summary>Tiles gained through border growth; each makes the next one cost more culture.</summary>
        public int TilesClaimed { get; set; }

        public ProductionItem? CurrentProduction { get; set; }
        public HashSet<string> Buildings { get; } = new HashSet<string>();
        public HashSet<HexCoord> WorkedTiles { get; } = new HashSet<HexCoord>();

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

        public bool Has(string buildingId) => Buildings.Contains(buildingId);

        public override string ToString() => $"{Name} (P{OwnerId}, pop {Population})";
    }
}
