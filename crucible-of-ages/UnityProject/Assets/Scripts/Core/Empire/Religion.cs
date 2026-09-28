using System;

namespace Crucible.Core.Empire
{
    /// <summary>A founded religion (GDD §3). Its beliefs are fixed for now (see <see cref="Game.GameState"/>).</summary>
    [Serializable]
    public sealed class Religion
    {
        public int Id { get; }
        public string Name { get; }
        public int FounderId { get; }
        public int HolyCityId { get; }

        public Religion(int id, string name, int founderId, int holyCityId)
        {
            Id = id;
            Name = name;
            FounderId = founderId;
            HolyCityId = holyCityId;
        }

        public override string ToString() => Name;
    }
}
