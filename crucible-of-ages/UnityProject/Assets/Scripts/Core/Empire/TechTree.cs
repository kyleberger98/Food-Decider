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

        /// <summary>Science already put into techs that were switched away from; kept, not lost (Civ V).</summary>
        readonly Dictionary<string, int> _partial = new Dictionary<string, int>();
        public IReadOnlyDictionary<string, int> PartialProgress => _partial;

        public int ProgressOn(string techId) => techId == CurrentResearch ? Progress : _partial.TryGetValue(techId, out var p) ? p : 0;

        public void SetResearch(string techId)
        {
            if (!CanResearch(techId)) throw new InvalidOperationException($"Cannot research '{techId}' yet.");
            if (CurrentResearch == techId) return;
            _partial.TryGetValue(techId, out var kept);
            if (CurrentResearch != null)
            {
                if (Progress > 0) _partial[CurrentResearch] = Progress;
                Progress = kept;
            }
            else Progress += kept; // nothing in progress: overflow from the last tech carries over
            _partial.Remove(techId);
            CurrentResearch = techId;
        }

        /// <summary>A distant tech the player is heading for; its prerequisites are researched first, in order.</summary>
        public string Target { get; private set; }

        /// <summary>
        /// Sets a research goal (any unresearched tech). If nothing is being researched, starts on the
        /// first step now. Null clears it.
        /// </summary>
        public void SetTarget(string techId)
        {
            if (techId != null && Has(techId)) techId = null;
            Target = techId;
            if (Target != null && CurrentResearch == null) SetResearch(NextTowardTarget());
            else if (Target != null && CanResearch(Target)) SetResearch(Target);
            else if (Target != null && !PathTo(Target).Contains(CurrentResearch)) SetResearch(NextTowardTarget());
        }

        /// <summary>Unresearched techs needed for <paramref name="techId"/>, in an order that can be researched (it last).</summary>
        public List<string> PathTo(string techId)
        {
            var order = new List<string>();
            var seen = new HashSet<string>();
            void Visit(string id)
            {
                if (Has(id) || !seen.Add(id)) return;
                foreach (var pre in _content.Tech(id).Prerequisites.OrderBy(x => _content.Tech(x).ScienceCost).ThenBy(x => x, StringComparer.Ordinal))
                    Visit(pre);
                order.Add(id);
            }
            if (techId != null) Visit(techId);
            return order;
        }

        /// <summary>The first researchable tech on the way to the target, or null (no target, or reached).</summary>
        public string NextTowardTarget()
        {
            if (Target != null && Has(Target)) Target = null;
            if (Target == null) return null;
            return PathTo(Target).FirstOrDefault(CanResearch);
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
            _partial.Remove(techId);
            if (CurrentResearch == techId) CurrentResearch = null;
        }

        /// <summary>Save-game restore (bypasses prerequisite checks).</summary>
        internal void Restore(IEnumerable<string> researched, string current, int progress, string target,
                              IEnumerable<KeyValuePair<string, int>> partial)
        {
            _partial.Clear();
            foreach (var kv in partial) _partial[kv.Key] = kv.Value;
            _researched.Clear();
            foreach (var id in researched) _researched.Add(id);
            CurrentResearch = current;
            Progress = progress;
            Target = target;
        }

        public int ArmyCapBonus => _researched.Sum(id => _content.Tech(id).ArmyCapBonus);
    }
}
