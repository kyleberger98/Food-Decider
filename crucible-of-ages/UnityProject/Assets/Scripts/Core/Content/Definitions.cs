using System;
using System.Collections.Generic;

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

        public bool IsRanged => RangedStrength > 0 && Range > 0;
        public bool IsMilitary => Class != UnitClass.Civilian;
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
