using System;

namespace LustHouse.Slots
{
    /// <summary>
    /// Deterministic, serializable PRNG (xorshift128+).
    ///
    /// Guardrail §6 requires client-side RNG that is seeded per session and reproducible
    /// in QA. <see cref="UnityEngine.Random"/> is explicitly *not* usable here: it is
    /// global mutable state shared with animation, so a given spin cannot be replayed.
    ///
    /// State is fully captured in four longs, so a save can persist the exact RNG
    /// position and a QA repro reproduces the identical sequence.
    /// </summary>
    [Serializable]
    public struct SeededRng
    {
        private ulong _s0;
        private ulong _s1;

        public ulong State0 => _s0;
        public ulong State1 => _s1;

        public SeededRng(ulong seed)
        {
            // SplitMix64 expansion so that adjacent seeds (e.g. session 1, session 2)
            // do not produce correlated streams.
            _s0 = SplitMix64(ref seed);
            _s1 = SplitMix64(ref seed);
            if (_s0 == 0 && _s1 == 0) _s1 = 0x9E3779B97F4A7C15UL;
        }

        private static ulong SplitMix64(ref ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            ulong z = x;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public static SeededRng FromSeed(int seed) => new SeededRng(unchecked((ulong)seed));

        /// <summary>Restores an exact stream position captured earlier.</summary>
        public static SeededRng FromState(ulong s0, ulong s1)
        {
            var rng = new SeededRng();
            rng._s0 = s0;
            rng._s1 = s1;
            if (rng._s0 == 0 && rng._s1 == 0) rng._s1 = 0x9E3779B97F4A7C15UL;
            return rng;
        }

        /// <summary>Next raw 64-bit value.</summary>
        public ulong NextULong()
        {
            unchecked
            {
                ulong s1 = _s0;
                ulong s0 = _s1;
                ulong result = s0 + s1;

                _s0 = s0;
                s1 ^= s1 << 23;
                _s1 = s1 ^ s0 ^ (s1 >> 18) ^ (s0 >> 5);

                return result;
            }
        }

        /// <summary>Uniform integer in [0, exclusiveMax). Rejection-sampled, so unbiased.</summary>
        public int NextInt(int exclusiveMax)
        {
            if (exclusiveMax <= 1) return 0;

            // Discard the high tail so the modulo does not bias low values.
            uint bound = (uint)exclusiveMax;
            uint threshold = (uint)(-(int)bound) % bound; // 2^32 % bound

            while (true)
            {
                uint r = (uint)(NextULong() >> 32);
                if (r >= threshold) return (int)(r % bound);
            }
        }

        public int NextInt(int inclusiveMin, int exclusiveMax) =>
            inclusiveMin + NextInt(Math.Max(1, exclusiveMax - inclusiveMin));

        /// <summary>Uniform double in [0, 1).</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);
    }
}