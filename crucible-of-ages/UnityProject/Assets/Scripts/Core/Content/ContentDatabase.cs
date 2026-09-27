using System;
using System.Collections.Generic;

namespace Crucible.Core.Content
{
    /// <summary>
    /// Read-only lookup of every definition in the game. Built once at startup from
    /// <see cref="DefaultContent"/> today; later from JSON / ScriptableObjects (GDD §7).
    /// </summary>
    public sealed class ContentDatabase
    {
        readonly Dictionary<string, UnitDef> _units = new Dictionary<string, UnitDef>();
        readonly Dictionary<string, TechDef> _techs = new Dictionary<string, TechDef>();
        readonly Dictionary<string, FactionDef> _factions = new Dictionary<string, FactionDef>();

        public IEnumerable<UnitDef> Units => _units.Values;
        public IEnumerable<TechDef> Techs => _techs.Values;
        public IEnumerable<FactionDef> Factions => _factions.Values;

        public ContentDatabase Add(UnitDef def) { AddUnique(_units, def.Id, def); return this; }
        public ContentDatabase Add(TechDef def) { AddUnique(_techs, def.Id, def); return this; }
        public ContentDatabase Add(FactionDef def) { AddUnique(_factions, def.Id, def); return this; }

        public UnitDef Unit(string id) => Lookup(_units, id, "unit");
        public TechDef Tech(string id) => Lookup(_techs, id, "tech");
        public FactionDef Faction(string id) => Lookup(_factions, id, "faction");

        public bool TryGetTech(string id, out TechDef def) => _techs.TryGetValue(id, out def);

        /// <summary>Throws if any tech prerequisite or unit tech requirement points at a missing tech.</summary>
        public void Validate()
        {
            foreach (var tech in _techs.Values)
                foreach (var pre in tech.Prerequisites)
                    if (!_techs.ContainsKey(pre))
                        throw new InvalidOperationException($"Tech '{tech.Id}' requires unknown tech '{pre}'.");
            foreach (var unit in _units.Values)
            {
                if (unit.RequiredTech != null && !_techs.ContainsKey(unit.RequiredTech))
                    throw new InvalidOperationException($"Unit '{unit.Id}' requires unknown tech '{unit.RequiredTech}'.");
                if (unit.FactionId != null && !_factions.ContainsKey(unit.FactionId))
                    throw new InvalidOperationException($"Unit '{unit.Id}' belongs to unknown faction '{unit.FactionId}'.");
            }
        }

        static void AddUnique<T>(Dictionary<string, T> dict, string id, T def)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Definition id is required.");
            if (dict.ContainsKey(id)) throw new InvalidOperationException($"Duplicate definition id '{id}'.");
            dict[id] = def;
        }

        static T Lookup<T>(Dictionary<string, T> dict, string id, string kind)
        {
            if (dict.TryGetValue(id, out var def)) return def;
            throw new KeyNotFoundException($"Unknown {kind} '{id}'.");
        }
    }
}
