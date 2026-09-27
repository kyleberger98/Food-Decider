using System;
using Crucible.Core.Hex;

namespace Crucible.Core.World
{
    public enum TerrainType
    {
        Ocean,
        Coast,
        Grassland,
        Plains,
        Desert,
        Tundra,
        Snow,
    }

    public enum FeatureType
    {
        None,
        Forest,
        Jungle,
        Marsh,
        Floodplain,
    }

    public enum ResourceType
    {
        None,
        // Bonus: always add yields
        Wheat,
        Cattle,
        Fish,
        // Strategic: gate units once improved
        Horses,
        Iron,
        Oil,
        // Luxury: +happiness once improved
        Wine,
        Silk,
        Gems,
        Furs,
    }

    public enum ImprovementType
    {
        None,
        Farm,
        Mine,
        Pasture,
        Plantation,
        Camp,
        Well,
    }

    [Serializable]
    public sealed class Tile
    {
        /// <summary>Highest land elevation; tiles at this level are mountains.</summary>
        public const int MountainElevation = 4;

        public HexCoord Coord { get; }
        public TerrainType Terrain { get; set; }
        public FeatureType Feature { get; set; }

        /// <summary>−2 ocean, −1 coast, 0 lowland … 4 mountain. Drives movement, LOS and combat.</summary>
        public int Elevation { get; set; }

        /// <summary>Bit i set = a river runs along the edge in hex direction i.</summary>
        public byte RiverEdges { get; set; }

        /// <summary>Wall tier on this tile (city centres); adds defence and blocks melee.</summary>
        public int WallTier { get; set; }

        public int OwnerPlayerId { get; set; } = -1;

        /// <summary>City whose territory this tile is (its citizens may work it), or -1.</summary>
        public int OwnerCityId { get; set; } = -1;

        /// <summary>City founded on this tile, or -1.</summary>
        public int CityId { get; set; } = -1;

        public ResourceType Resource { get; set; }
        public ImprovementType Improvement { get; set; }

        /// <summary>Improvement a worker is currently building here, and turns of work done on it.</summary>
        public ImprovementType ImprovementInProgress { get; set; }
        public int ImprovementProgress { get; set; }

        public Tile(HexCoord coord)
        {
            Coord = coord;
        }

        public bool IsWater => Terrain == TerrainType.Ocean || Terrain == TerrainType.Coast;
        public bool IsMountain => !IsWater && Elevation >= MountainElevation;
        public bool IsPassableForLand => !IsWater && !IsMountain;
        public bool IsRoughTerrain => Feature == FeatureType.Forest || Feature == FeatureType.Jungle;
        public bool HasCity => CityId >= 0;

        /// <summary>Height used for line-of-sight: forests and jungles count as one level taller.</summary>
        public int SightHeight => Elevation + (IsRoughTerrain ? 1 : 0);

        public bool HasRiver(int direction) => (RiverEdges & (1 << direction)) != 0;
        internal void SetRiver(int direction) => RiverEdges |= (byte)(1 << direction);

        public override string ToString() =>
            $"{Coord} {Terrain}/{Feature} elev {Elevation}" +
            (Resource != ResourceType.None ? $" [{Resource}]" : "") +
            (Improvement != ImprovementType.None ? $" +{Improvement}" : "") +
            (ImprovementInProgress != ImprovementType.None ? $" (building {ImprovementInProgress} {ImprovementProgress})" : "");
    }
}
