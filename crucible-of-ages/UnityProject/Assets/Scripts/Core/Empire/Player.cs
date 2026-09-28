using System;
using System.Collections.Generic;
using Crucible.Core.Content;

namespace Crucible.Core.Empire
{
    public enum CityStateType
    {
        Maritime,
        Cultured,
        Mercantile,
        Militaristic,
    }

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

        // --- Strategic AI plan, read by the city governor (GDD §6) ---

        /// <summary>The AI wants more cities: governors may build settlers.</summary>
        public bool AIWantsSettlers { get; set; }

        /// <summary>Military units the AI wants in play; governors build toward it while gold allows.</summary>
        public int AIMilitaryTarget { get; set; }

        /// <summary>Adopted social policies, and culture banked toward the next one.</summary>
        public HashSet<string> Policies { get; } = new HashSet<string>();
        public int PolicyCulture { get; set; }

        // --- City-states (GDD §3) ---

        /// <summary>A minor civilisation: one city, never expands or wars, courted with gold.</summary>
        public bool IsCityState { get; set; }
        public CityStateType CityStateType { get; set; }

        /// <summary>On a city-state: each major player's influence with it.</summary>
        public Dictionary<int, int> Influence { get; } = new Dictionary<int, int>();

        // --- Great people & religion (M8) ---
        public Dictionary<Content.GreatPersonType, int> GreatPersonPoints { get; } = new Dictionary<Content.GreatPersonType, int>();
        public Dictionary<Content.GreatPersonType, int> GreatPeopleBorn { get; } = new Dictionary<Content.GreatPersonType, int>();

        /// <summary>Points toward the next Great General, earned in battle.</summary>
        public int GeneralPoints { get; set; }

        public int Faith { get; set; }
        public int ProphetsBorn { get; set; }

        /// <summary>Religion this player founded, or -1.</summary>
        public int FoundedReligionId { get; set; } = -1;

        // --- Victory progress ---
        public Dictionary<string, int> CompletedProjects { get; } = new Dictionary<string, int>();
        public int SpaceshipPartsBuilt { get; set; }

        /// <summary>Turn the launched spaceship arrives, or -1 if none is in flight.</summary>
        public int SpaceshipArrivalTurn { get; set; } = -1;
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
