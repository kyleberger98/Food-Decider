using System;
using System.Collections.Generic;
using Crucible.Core.Economy;

namespace Crucible.Core.Content
{
    public enum Era
    {
        Ancient,
        Classical,
        Medieval,
        Renaissance,
        Industrial,
        Modern,
        Atomic,
        Information,
    }

    public enum UnitClass
    {
        Civilian,
        Recon,
        Melee,
        Ranged,
        Mounted,
        AntiCavalry,
        Siege,
        Gunpowder,
        Armor,
        NavalMelee,
        NavalRanged,
        Carrier,
        Fighter,
        Bomber,
        Missile,
    }

    /// <summary>Static data for a unit type. Instances in play are <see cref="Units.Unit"/>.</summary>
    [Serializable]
    public sealed class UnitDef
    {
        public string Id;
        public string Name;
        public UnitClass Class;
        public Era Era;
        public int CombatStrength;
        public int RangedStrength;
        public int Range;
        public int BattleMovement = 2;
        public int WorldMovement = 2;
        public int ProductionCost;
        public string RequiredTech;
        public string RequiredResource;

        /// <summary>Fires over obstacles; ignores line of sight (siege, artillery).</summary>
        public bool IndirectFire;

        /// <summary>Only this faction may build it (unique unit); null for everyone.</summary>
        public string FactionId;

        /// <summary>Unit that this replaces for <see cref="FactionId"/>.</summary>
        public string Replaces;

        // --- Siege warfare (GDD §4.6) ---

        /// <summary>Siege progress needed to build this on the spot while besieging (0 = not possible).</summary>
        public int SiegeProgressCost;

        /// <summary>Only built during sieges (rams, towers), never in cities.</summary>
        public bool SiegeOnly;

        /// <summary>False for units that never attack (siege towers).</summary>
        public bool CanAttack = true;

        /// <summary>Can batter walls but never attacks units (rams).</summary>
        public bool AttacksWallsOnly;

        /// <summary>Adjacent friendly melee units may cross intact walls (siege towers).</summary>
        public bool CarriesOverWalls;

        /// <summary>Multiplier on damage dealt to walls: siege weapons ×2, rams ×3.</summary>
        public double WallDamageMultiplier = 1.0;

        public bool IsRanged => RangedStrength > 0 && Range > 0;
        public bool IsMilitary => Class != UnitClass.Civilian;
    }

    [Serializable]
    public sealed class BuildingDef
    {
        public string Id;
        public string Name;
        public Era Era;
        public int ProductionCost;
        public string RequiredTech;

        /// <summary>Gold per turn to keep it running.</summary>
        public int Maintenance;

        /// <summary>Flat yields added to the city.</summary>
        public Yields Yields;

        /// <summary>Global happiness it provides.</summary>
        public int Happiness;

        /// <summary>Wall tiers added to the city centre (see GDD §4.6).</summary>
        public int WallTiers;
    }

    [Serializable]
    public sealed class TechDef
    {
        public string Id;
        public string Name;
        public Era Era;
        public int ScienceCost;
        public List<string> Prerequisites = new List<string>();

        /// <summary>Added to the owner's army cap once researched (see GDD §4.2).</summary>
        public int ArmyCapBonus;
    }

    [Serializable]
    public sealed class FactionDef
    {
        public string Id;
        public string Name;
        public string AbilityName;
        public string AbilityText;
        public int ArmyCapBonus;

        /// <summary>HP healed by every surviving unit when a battle round ends.</summary>
        public int HealOnRoundEnd;

        /// <summary>Extra world-map movement for mounted units.</summary>
        public int MountedWorldMovementBonus;
    }
}
