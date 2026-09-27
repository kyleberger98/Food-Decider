using System.Collections.Generic;
using System.Linq;
using Crucible.Core.Empire;
using Crucible.Core.Game;
using Crucible.Core.World;

namespace Crucible.Core.Economy
{
    public enum ResourceKind
    {
        None,
        Bonus,
        Strategic,
        Luxury,
    }

    /// <summary>Resource and tile-improvement rules (GDD §2.3, §3). Civ V values where an analogue exists.</summary>
    public static class Improvements
    {
        /// <summary>Units each improved strategic source supports.</summary>
        public const int StrategicPerSource = 4;

        /// <summary>Happiness per distinct improved luxury (Civ V: 4).</summary>
        public const int HappinessPerLuxury = 4;

        public static ResourceKind KindOf(ResourceType r)
        {
            switch (r)
            {
                case ResourceType.Wheat:
                case ResourceType.Cattle:
                case ResourceType.Fish:
                    return ResourceKind.Bonus;
                case ResourceType.Horses:
                case ResourceType.Iron:
                case ResourceType.Oil:
                    return ResourceKind.Strategic;
                case ResourceType.Wine:
                case ResourceType.Silk:
                case ResourceType.Gems:
                case ResourceType.Furs:
                    return ResourceKind.Luxury;
                default:
                    return ResourceKind.None;
            }
        }

        /// <summary>Yield a resource adds on its own (bonus resources don't need an improvement).</summary>
        public static Yields ResourceYields(ResourceType r)
        {
            switch (r)
            {
                case ResourceType.Wheat: return new Yields(food: 1);
                case ResourceType.Cattle: return new Yields(food: 1);
                case ResourceType.Fish: return new Yields(food: 2);
                case ResourceType.Horses: return new Yields(production: 1);
                case ResourceType.Iron: return new Yields(production: 1);
                case ResourceType.Oil: return new Yields(production: 1);
                case ResourceType.Wine:
                case ResourceType.Silk:
                case ResourceType.Gems:
                case ResourceType.Furs:
                    return new Yields(gold: 2);
                default: return new Yields();
            }
        }

        /// <summary>The improvement that "connects" a resource, making strategics and luxuries count.</summary>
        public static ImprovementType ImprovementFor(ResourceType r)
        {
            switch (r)
            {
                case ResourceType.Wheat: return ImprovementType.Farm;
                case ResourceType.Cattle:
                case ResourceType.Horses: return ImprovementType.Pasture;
                case ResourceType.Iron:
                case ResourceType.Gems: return ImprovementType.Mine;
                case ResourceType.Oil: return ImprovementType.Well;
                case ResourceType.Wine:
                case ResourceType.Silk: return ImprovementType.Plantation;
                case ResourceType.Furs: return ImprovementType.Camp;
                default: return ImprovementType.None;
            }
        }

        public static Yields ImprovementYields(ImprovementType i)
        {
            switch (i)
            {
                case ImprovementType.Farm: return new Yields(food: 1);
                case ImprovementType.Mine: return new Yields(production: 1);
                case ImprovementType.Pasture: return new Yields(production: 1);
                case ImprovementType.Plantation: return new Yields(gold: 1);
                case ImprovementType.Camp: return new Yields(gold: 1);
                case ImprovementType.Well: return new Yields(production: 1);
                default: return new Yields();
            }
        }

        public static int BuildTurns(ImprovementType i)
        {
            switch (i)
            {
                case ImprovementType.Mine: return 6;
                case ImprovementType.Well: return 7;
                default: return 5;
            }
        }

        public static string RequiredTech(ImprovementType i)
        {
            switch (i)
            {
                case ImprovementType.Farm: return "agriculture";
                case ImprovementType.Mine: return "mining";
                case ImprovementType.Pasture: return "animal_husbandry";
                case ImprovementType.Camp: return "animal_husbandry";
                case ImprovementType.Plantation: return "calendar";
                case ImprovementType.Well: return "combustion";
                default: return null;
            }
        }

        /// <summary>Whether the improvement fits this tile's terrain/resource (tech is checked separately).</summary>
        public static bool Fits(Tile t, ImprovementType i)
        {
            if (t == null || !t.IsPassableForLand || t.HasCity || i == ImprovementType.None) return false;
            if (ImprovementFor(t.Resource) == i) return true;
            bool hills = t.Elevation >= 2;
            switch (i)
            {
                case ImprovementType.Farm:
                    return !hills && t.Terrain != TerrainType.Snow &&
                           (t.Feature == FeatureType.None || t.Feature == FeatureType.Floodplain) &&
                           (t.Terrain != TerrainType.Desert || t.Feature == FeatureType.Floodplain);
                case ImprovementType.Mine:
                    return hills && t.Feature == FeatureType.None;
                default:
                    return false; // pastures, plantations, camps and wells need their resource
            }
        }

        public static bool CanBuild(Player player, Tile t, ImprovementType i) =>
            Fits(t, i) && t.Improvement != i && player.Tech.Has(RequiredTech(i)) && t.OwnerPlayerId == player.Id;

        /// <summary>The most valuable improvement this player can build on the tile, or None.</summary>
        public static ImprovementType Best(Player player, Tile t)
        {
            var resourceImp = ImprovementFor(t.Resource);
            if (resourceImp != ImprovementType.None && CanBuild(player, t, resourceImp)) return resourceImp;
            foreach (var i in new[] { ImprovementType.Mine, ImprovementType.Farm })
                if (CanBuild(player, t, i)) return i;
            return ImprovementType.None;
        }

        /// <summary>Resource is in the player's territory and improved with the right improvement.</summary>
        public static bool IsConnected(Tile t) =>
            t.Resource != ResourceType.None && t.Improvement != ImprovementType.None &&
            ImprovementFor(t.Resource) == t.Improvement;

        public static int StrategicAvailable(GameState game, int playerId, ResourceType r) =>
            game.Map.Tiles.Count(t => t.OwnerPlayerId == playerId && t.Resource == r && IsConnected(t)) * StrategicPerSource;

        /// <summary>Units in play that need the resource (one each, Civ V style).</summary>
        public static int StrategicUsed(GameState game, int playerId, ResourceType r)
        {
            string id = ResourceId(r);
            return game.Armies.Where(a => a.OwnerId == playerId)
                .Sum(a => a.Units.Count(u => u.Def.RequiredResource == id));
        }

        public static IEnumerable<ResourceType> ConnectedLuxuries(GameState game, int playerId) =>
            game.Map.Tiles
                .Where(t => t.OwnerPlayerId == playerId && KindOf(t.Resource) == ResourceKind.Luxury && IsConnected(t))
                .Select(t => t.Resource)
                .Distinct();

        public static string ResourceId(ResourceType r) => r.ToString().ToLowerInvariant();

        public static ResourceType Parse(string id)
        {
            foreach (ResourceType r in System.Enum.GetValues(typeof(ResourceType)))
                if (ResourceId(r) == id) return r;
            return ResourceType.None;
        }
    }
}
