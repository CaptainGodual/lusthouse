using System.Linq;
using LustHouse.Slots;
using NUnit.Framework;

namespace LustHouse.Tests
{
    /// <summary>
    /// Guardrail §6: the RNG is seeded and reproducible. A test that cannot be replayed
    /// from a seed proves nothing about RTP.
    /// </summary>
    public class SeededRngTests
    {
        [Test]
        public void SameSeed_ProducesIdenticalStream()
        {
            var a = SeededRng.FromSeed(12345);
            var b = SeededRng.FromSeed(12345);

            for (int i = 0; i < 1000; i++)
                Assert.AreEqual(a.NextULong(), b.NextULong(), $"Diverged at draw {i}.");
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentStreams()
        {
            var a = SeededRng.FromSeed(1);
            var b = SeededRng.FromSeed(2);

            bool anyDifference = false;
            for (int i = 0; i < 32 && !anyDifference; i++)
                anyDifference = a.NextULong() != b.NextULong();

            Assert.IsTrue(anyDifference, "Adjacent seeds must not produce correlated streams.");
        }

        [Test]
        public void StateRoundTrip_ResumesTheExactStream()
        {
            var rng = SeededRng.FromSeed(999);
            for (int i = 0; i < 37; i++) rng.NextULong();

            var (s0, s1) = (rng.State0, rng.State1);
            ulong expected = rng.NextULong();

            var resumed = SeededRng.FromState(s0, s1);
            Assert.AreEqual(expected, resumed.NextULong(),
                "A restored RNG must continue the identical sequence (QA repro).");
        }

        [Test]
        public void NextInt_StaysInRange()
        {
            var rng = SeededRng.FromSeed(7);

            for (int i = 0; i < 10000; i++)
            {
                int v = rng.NextInt(16);
                Assert.GreaterOrEqual(v, 0);
                Assert.Less(v, 16);
            }
        }

        [Test]
        public void NextInt_CoversTheWholeRange()
        {
            var rng = SeededRng.FromSeed(11);
            var seen = new bool[16];

            for (int i = 0; i < 20000; i++) seen[rng.NextInt(16)] = true;

            for (int i = 0; i < 16; i++)
                Assert.IsTrue(seen[i], $"Value {i} was never produced.");
        }

        [Test]
        public void NextInt_IsReasonablyUniform()
        {
            var rng = SeededRng.FromSeed(3);
            const int buckets = 8;
            const int draws = 80000;
            var counts = new int[buckets];

            for (int i = 0; i < draws; i++) counts[rng.NextInt(buckets)]++;

            double expected = draws / (double)buckets;
            foreach (var c in counts)
            {
                // Rejection sampling should keep every bucket within a few percent.
                Assert.Less(System.Math.Abs(c - expected) / expected, 0.10,
                    "Rejection sampling should give a flat distribution.");
            }
        }

        [Test]
        public void NextInt_HandlesDegenerateBounds()
        {
            var rng = SeededRng.FromSeed(5);

            Assert.AreEqual(0, rng.NextInt(0));
            Assert.AreEqual(0, rng.NextInt(1));
        }

        [Test]
        public void NextDouble_IsInUnitInterval()
        {
            var rng = SeededRng.FromSeed(21);

            for (int i = 0; i < 5000; i++)
            {
                double d = rng.NextDouble();
                Assert.GreaterOrEqual(d, 0.0);
                Assert.Less(d, 1.0);
            }
        }
    }
}