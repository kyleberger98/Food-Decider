using System;

namespace Crucible.Core.Random
{
    /// <summary>
    /// Seeded SplitMix64 generator. Identical sequences on every platform and runtime, and the
    /// whole state is one <see cref="ulong"/>, so it can be saved and restored with the game.
    /// </summary>
    [Serializable]
    public sealed class DeterministicRng
    {
        public ulong State { get; set; }

        public DeterministicRng(ulong seed)
        {
            State = seed;
        }

        public ulong NextULong()
        {
            unchecked
            {
                State += 0x9E3779B97F4A7C15UL;
                ulong z = State;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>Uniform double in [0, 1).</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

        /// <summary>Uniform double in [min, max).</summary>
        public double Range(double min, double max) => min + (max - min) * NextDouble();

        /// <summary>Uniform int in [0, maxExclusive).</summary>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            return (int)(NextULong() % (ulong)maxExclusive);
        }

        /// <summary>Uniform int in [minInclusive, maxExclusive).</summary>
        public int NextInt(int minInclusive, int maxExclusive) => minInclusive + NextInt(maxExclusive - minInclusive);
    }
}
