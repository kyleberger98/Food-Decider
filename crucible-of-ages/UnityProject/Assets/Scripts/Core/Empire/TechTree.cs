using System;
using System.Collections.Generic;
using System.Linq;
using Crucible.Core.Content;

namespace Crucible.Core.Empire
{
    /// <summary>One player's research state.</summary>
    [Serializable]
    public sealed class TechTree
    {
        readonly ContentDatabase _content;
        readonly HashSet<string> _researched = new HashSet<string>();

        public string CurrentResearch { get; private set; }
        public int Progress { get; private set; }

        public TechTree(ContentDatabase content)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
        }

        public IReadOnlyCollection<string> Researched => _researched;

        public bool Has(string techId) => techId == null || _researched.Contains(techId);

        public bool CanResearch(string techId) =>
            !_researched.Contains(techId) && _content.Tech(techId).Prerequisites.All(_researched.Contains);

        public IEnumerable<TechDef> Available() => _content.Techs.Where(t => CanResearch(t.Id));

        public void SetResearch(string techId)
        {
            if (!CanResearch(techId)) throw new InvalidOperationException($"Cannot research '{techId}' yet.");
            if (CurrentResearch != techId) Progress = 0;
            CurrentResearch = techId;
        }

        /// <summary>Adds science; returns the tech completed this call, or null. Overflow carries over.</summary>
        public TechDef AddScience(int amount)
        {
            if (CurrentResearch == null || amount <= 0) return null;
            var tech = _content.Tech(CurrentResearch);
            Progress += amount;
            if (Progress < tech.ScienceCost) return null;

            Progress -= tech.ScienceCost;
            Grant(tech.Id);
            CurrentResearch = null;
            return tech;
        }

        /// <summary>Marks a tech researched immediately (starting techs, great scientists, tests).</summary>
        public void Grant(string techId)
        {
            _content.Tech(techId);
            _researched.Add(techId);
            if (CurrentResearch == techId) CurrentResearch = null;
        }

        public int ArmyCapBonus => _researched.Sum(id => _content.Tech(id).ArmyCapBonus);
    }
}
