using System;
using System.Collections.Generic;
using Crucible.Core.Content;

namespace Crucible.Core.Empire
{
    [Serializable]
    public sealed class Player
    {
        public const int BaseArmyCap = 4;

        public int Id { get; }
        public string Name { get; }
        public FactionDef Faction { get; }
        public bool IsAI { get; }
        public TechTree Tech { get; }
        public bool IsEliminated { get; set; }

        public int Gold { get; set; }
        public int Happiness { get; set; }

        /// <summary>Extra army cap from policies, great generals held in reserve, etc.</summary>
        public int PolicyArmyCapBonus { get; set; }

        /// <summary>Adopted social policies, and culture banked toward the next one.</summary>
        public HashSet<string> Policies { get; } = new HashSet<string>();
        public int PolicyCulture { get; set; }

        // --- Victory progress (systems land in M8; the checker reads these today) ---
        public int SpaceshipPartsLanded { get; set; }
        public int Tourism { get; set; }
        public int LifetimeCulture { get; set; }
        public bool WonWorldLeaderVote { get; set; }

        /// <summary>Tourism this player has accumulated against each other player.</summary>
        public Dictionary<int, int> TourismAgainst { get; } = new Dictionary<int, int>();

        public Player(int id, string name, FactionDef faction, bool isAI, ContentDatabase content)
        {
            Id = id;
            Name = name;
            Faction = faction ?? throw new ArgumentNullException(nameof(faction));
            IsAI = isAI;
            Tech = new TechTree(content);
        }

        /// <summary>Max units per army: 4 + tech + faction + policy bonuses (GDD §4.2).</summary>
        public int ArmyCap => BaseArmyCap + Tech.ArmyCapBonus + Faction.ArmyCapBonus + PolicyArmyCapBonus;

        /// <summary>Civ V: at −10 happiness or lower, units fight at −3 CS.</summary>
        public bool IsVeryUnhappy => Happiness <= -10;

        public override string ToString() => $"P{Id} {Name} ({Faction.Name})";
    }
}
