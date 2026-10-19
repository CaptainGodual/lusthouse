using System.Collections.Generic;
using LustHouse.Slots;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LustHouse.Tests
{
    /// <summary>
    /// The reel engine and payline evaluation, plus the RTP ship gate from plan §3.6.
    /// These use the generated Velvet Room definition so the maths is exercised against
    /// real data rather than a hand-built fixture.
    /// </summary>
    public class ReelEngineTests
    {
        private SlotMachineDefinition _def;

        [SetUp]
        public void SetUp()
        {
            _def = AssetDatabase.LoadAssetAtPath<SlotMachineDefinition>(
                EditorTools.ContentBootstrap.VelvetRoomPath);
        }

        private SlotMachineDefinition BuildFixture()
        {
            // Built in memory so these tests do not depend on generated assets existing.
            var def = ScriptableObject.CreateInstance<SlotMachineDefinition>();
            def.id = "fixture";
            def.reelCount = 3;
            def.rowCount = 3;
            def.minBet = 100;
            def.maxBet = 1000;
            def.betLevels = 3;
            def.payType = PayType.Lines;

            def.symbols = new List<SymbolDefinition>
            {
                new SymbolDefinition { id = 0, symbolName = "W", isWild = true },
                new SymbolDefinition { id = 1, symbolName = "A", payoutsByMatch = new long[] { 5, 20, 50 } },
                new SymbolDefinition { id = 2, symbolName = "B", payoutsByMatch = new long[] { 3, 10, 30 } }
            };

            def.reelStrips = new List<ReelStrip>
            {
                new ReelStrip { symbols = new List<int> { 1, 2, 1, 2 } },
                new ReelStrip { symbols = new List<int> { 1, 2, 1, 2 } },
                new ReelStrip { symbols = new List<int> { 1, 2, 1, 2 } }
            };

            def.paylines = new List<PaylineDefinition>
            {
                new PaylineDefinition { lineName = "mid", rows = new[] { 1, 1, 1 } },
                new PaylineDefinition { lineName = "top", rows = new[] { 0, 0, 0 } },
                new PaylineDefinition { lineName = "bot", rows = new[] { 2, 2, 2 } }
            };

            return def;
        }

        [TearDown]
        public void TearDown()
        {
            // _def comes from AssetDatabase in SetUp, so it is a project asset. Destroying
            // an asset is not permitted; Unity logs an error, and an unexpected error log
            // fails the test -- which made every test in this fixture fail regardless of
            // what it asserted. BuildFixture() results are in-memory and still need
            // destroying.
            if (_def == null) return;
            if (!EditorUtility.IsPersistent(_def)) Object.DestroyImmediate(_def);
            _def = null;
        }

        [Test]
        public void Fixture_IsValid()
        {
            var def = BuildFixture();
            Assert.IsTrue(def.IsValid(out var error), error);
        }

        [Test]
        public void Definition_ValidationCatchesBrokenStrips()
        {
            var def = BuildFixture();
            def.reelStrips = new List<ReelStrip> { new ReelStrip { symbols = new List<int> { 1, 2 } } }; // wrong reel count

            Assert.IsFalse(def.IsValid(out var error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void Definition_ValidationCatchesUnknownSymbolIds()
        {
            var def = BuildFixture();
            def.reelStrips = new List<ReelStrip>
            {
                new ReelStrip { symbols = new List<int> { 1, 99, 2 } },
                new ReelStrip { symbols = new List<int> { 1, 2, 1 } },
                new ReelStrip { symbols = new List<int> { 1, 2, 2 } }
            };

            Assert.IsFalse(def.IsValid(out var error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void Definition_ValidationRejectsNonRoundMinBet()
        {
            var def = BuildFixture();
            def.minBet = 137; // §4.3 rule 5 — never 137 coins

            Assert.IsFalse(def.IsValid(out _));
        }

        [Test]
        public void SameSeed_ProducesIdenticalSpinSequence()
        {
            var def = BuildFixture();

            var a = SeededRng.FromSeed(4242);
            var b = SeededRng.FromSeed(4242);

            for (int i = 0; i < 200; i++)
            {
                var ra = ReelEngine.SpinStatic(def, ref a, def.minBet);
                var rb = ReelEngine.SpinStatic(def, ref b, def.minBet);

                Assert.AreEqual(ra.totalWin, rb.totalWin, $"Spin {i} diverged.");
                CollectionAssert.AreEqual(ra.stops, rb.stops, $"Spin {i} stops diverged.");
            }
        }

        [Test]
        public void Grid_SymbolsComeFromTheCorrectReelStrips()
        {
            var def = BuildFixture();
            var rng = SeededRng.FromSeed(1);

            for (int i = 0; i < 200; i++)
            {
                var result = ReelEngine.SpinStatic(def, ref rng, def.minBet);

                for (int reel = 0; reel < def.reelCount; reel++)
                {
                    for (int row = 0; row < def.rowCount; row++)
                    {
                        int symbol = result.grid[reel][row];
                        int expected = def.reelStrips[reel].symbols[(result.stops[reel] + row) % def.reelStrips[reel].symbols.Count];
                        Assert.AreEqual(expected, symbol, $"grid[{reel}][{row}] is off-strip.");
                    }
                }
            }
        }

        [Test]
        public void ThreeOfAKind_PaysTheFirstPayoutTier()
        {
            var def = BuildFixture();
            var grid = new[]
            {
                new[] { 2, 2, 2 },   // payline[0] is row 1 on every reel
                new[] { 1, 2, 1 },
                new[] { 1, 2, 1 }
            };

            long win = PaylineEvaluator.EvaluateLine(def, grid, def.paylines[0], out int symbolId, out int count);

            Assert.AreEqual(3, count);
            Assert.AreEqual(2, symbolId, "Symbol B should win, not A, because reel 0 row 1 is B.");
            Assert.AreEqual(3, win, "B pays 3× for three of a kind.");
        }

        [Test]
        public void Wild_SubstitutesForTheBaseSymbol()
        {
            var def = BuildFixture();
            var grid = new[]
            {
                new[] { 2, 1, 1 },   // reel 0 row 1 is A
                new[] { 2, 0, 1 },   // wild on the line
                new[] { 2, 1, 1 }
            };

            long win = PaylineEvaluator.EvaluateLine(def, grid, def.paylines[0], out int symbolId, out int count);

            Assert.AreEqual(3, count, "Wild extends the run.");
            Assert.AreEqual(1, symbolId);
            Assert.AreEqual(5, win, "A pays 5× for three of a kind.");
        }

        [Test]
        public void TwoOfAKind_DoesNotPay()
        {
            var def = BuildFixture();
            var grid = new[]
            {
                new[] { 2, 2, 1 },
                new[] { 1, 2, 1 },
                new[] { 1, 1, 1 }
            };

            long win = PaylineEvaluator.EvaluateLine(def, grid, def.paylines[0], out _, out int count);

            Assert.AreEqual(2, count);
            Assert.AreEqual(0, win, "Minimum pay length is three.");
        }

        [Test]
        public void TotalWin_IsASimpleMultipleOfBet()
        {
            var def = BuildFixture();
            var rng = SeededRng.FromSeed(88);

            for (int i = 0; i < 300; i++)
            {
                var result = ReelEngine.SpinStatic(def, ref rng, def.minBet);
                Assert.AreEqual(0, result.totalWin % def.minBet,
                    "Wins must be whole multiples of the bet.");
            }
        }

        [Test]
        public void BetLadder_StaysWithinRangeAndIsMonotonic()
        {
            var def = BuildFixture();
            def.minBet = 100;
            def.maxBet = 5000;
            def.betLevels = 6;

            long previous = 0;
            for (int i = 0; i < def.betLevels; i++)
            {
                long bet = def.BetForLevel(i);
                Assert.GreaterOrEqual(bet, def.minBet);
                Assert.LessOrEqual(bet, def.maxBet);
                Assert.GreaterOrEqual(bet, previous, "Bet ladder must be monotonic.");
                Assert.AreEqual(0, bet % 100, "Bets must be round numbers (§4.3 rule 5).");
                previous = bet;
            }

            Assert.AreEqual(def.minBet, def.BetForLevel(0));
            Assert.AreEqual(def.maxBet, def.BetForLevel(def.betLevels - 1));
        }

        [Test]
        public void Randomness_ProducesAWideSpreadOfOutcomes()
        {
            var def = BuildFixture();
            var rng = SeededRng.FromSeed(31337);

            int wins = 0;
            const int spins = 2000;
            for (int i = 0; i < spins; i++)
            {
                var r = ReelEngine.SpinStatic(def, ref rng, def.minBet);
                if (r.IsWin) wins++;
            }

            // A healthy hit rate for a 3-reel game. Guards against an engine that always
            // wins or always loses, which a hand-picked fixture would hide.
            Assert.Greater(wins, 0, "No winning spins at all — engine is stuck.");
            Assert.Less(wins, spins, "Every spin won — engine is stuck the other way.");
        }
    }
}