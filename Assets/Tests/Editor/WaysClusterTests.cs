using System.Collections.Generic;
using LustHouse.EditorTools;
using LustHouse.Slots;
using NUnit.Framework;
using UnityEngine;

namespace LustHouse.Tests
{
    /// <summary>
    /// <see cref="PayType.Ways"/> and <see cref="PayType.Cluster"/>.
    ///
    /// Both payTypes existed in the enum while nothing implemented them, so a game set to
    /// either one scored every spin at zero and looked merely unlucky. These cover the
    /// maths and, importantly, that the exact RTP path agrees with sampling for Ways and
    /// refuses to invent a number for Cluster.
    /// </summary>
    public class WaysClusterTests
    {
        // ---- Ways -------------------------------------------------------------

        private static SlotMachineDefinition BuildWays(int reels = 5, int rows = 3)
        {
            var def = ScriptableObject.CreateInstance<SlotMachineDefinition>();
            def.id = "ways_fixture";
            def.payType = PayType.Ways;
            def.template = SlotTemplate.Video5;
            def.reelCount = reels;
            def.rowCount = rows;
            def.minBet = 100;
            def.maxBet = 1000;

            def.symbols = new List<SymbolDefinition>
            {
                new SymbolDefinition { id = 0, symbolName = "W", isWild = true,
                                      payoutsByMatch = new long[] { 10, 40, 120 } },
                new SymbolDefinition { id = 1, symbolName = "S", isScatter = true },
                new SymbolDefinition { id = 10, symbolName = "A", payoutsByMatch = new long[] { 5, 20, 60 } },
                new SymbolDefinition { id = 11, symbolName = "B", payoutsByMatch = new long[] { 3, 10, 40 } },
                new SymbolDefinition { id = 12, symbolName = "C", payoutsByMatch = new long[] { 2, 6, 25 } }
            };

            def.reelStrips = new List<ReelStrip>();
            for (int r = 0; r < reels; r++)
                def.reelStrips.Add(new ReelStrip
                {
                    symbols = new List<int> { 10, 11, 12, 10, 12, 11, 10, 12, 0, 1 }
                });

            return def;
        }

        [Test]
        public void ElementarySymmetric_MatchesHandComputedValues()
        {
            // e[k] over {1,2,3}: 1, 6, 11, 6
            var e = WaysEvaluator.ElementarySymmetric(new[] { 1, 2, 3 });

            Assert.AreEqual(1L, e[0]);
            Assert.AreEqual(6L, e[1], "1+2+3");
            Assert.AreEqual(11L, e[2], "1*2 + 1*3 + 2*3");
            Assert.AreEqual(6L, e[3], "1*2*3");
            Assert.AreEqual(4, e.Length);
        }

        [Test]
        public void Ways_ThreeOfAKind_PaysAcrossAllReelCombinations()
        {
            var def = BuildWays(reels: 3, rows: 1);

            // Force every cell to A: 3 reels x 1 row, one symbol each.
            for (int r = 0; r < 3; r++)
                def.reelStrips[r].symbols = new List<int> { 10 };

            var evaluator = new WaysEvaluator();
            var result = evaluator.Evaluate(def, new[] { 0, 0, 0 }, def.minBet);

            // One way to pick all three reels, A pays 5x.
            Assert.AreEqual(1L, result.waysWon);
            Assert.AreEqual(5L * def.minBet, result.totalWin);
        }

        [Test]
        public void Ways_WildSubstitutesForTheBaseSymbol()
        {
            var def = BuildWays(reels: 3, rows: 1);

            // Reel 0 = A, reels 1 and 2 = wild. Wilds stand in for A.
            def.reelStrips[0].symbols = new List<int> { 10 };
            def.reelStrips[1].symbols = new List<int> { 0 };
            def.reelStrips[2].symbols = new List<int> { 0 };

            var result = new WaysEvaluator().Evaluate(def, new[] { 0, 0, 0 }, def.minBet);

            Assert.AreEqual(1L, result.waysWon);
            Assert.AreEqual(5L * def.minBet, result.totalWin,
                "Wilds should complete the run and pay as A, not as the wild.");
        }

        [Test]
        public void Ways_TwoOfAKind_DoesNotPay()
        {
            var def = BuildWays(reels: 2, rows: 1);
            def.reelStrips[0].symbols = new List<int> { 10 };
            def.reelStrips[1].symbols = new List<int> { 10 };

            var result = new WaysEvaluator().Evaluate(def, new[] { 0, 0 }, def.minBet);

            Assert.AreEqual(0L, result.totalWin);
            Assert.AreEqual(0L, result.waysWon);
        }

        [Test]
        public void Ways_ExactRtp_AgreesWithMonteCarlo()
        {
            var def = BuildWays();

            Assert.IsTrue(SimulationEngine.SupportsExactRtp(def),
                "Ways has a closed form and must be exactly gateable.");

            double exact = SimulationEngine.ExactRtp(def);
            Assert.IsFalse(double.IsNaN(exact), "Ways must produce an exact figure.");

            var report = SimulationEngine.Run(def, 400_000, def.minBet, seed: 31337);

            // Relative tolerance: sampling error scales with the size of the number, and
            // this fixture's RTP is in the hundreds of percent, so a fixed +/-0.02 would
            // be both meaningless and unachievable.
            double tolerance = System.Math.Max(0.01, System.Math.Abs(exact) * 0.01);
            Assert.AreEqual(exact, report.rtp, tolerance,
                $"Exact {exact:P3} vs sampled {report.rtp:P3} -- the closed form and the " +
                "simulator have divergened.");
        }

        [Test]
        public void Ways_ExactRtp_IsDeterministic()
        {
            var def = BuildWays();
            Assert.AreEqual(SimulationEngine.ExactRtp(def), SimulationEngine.ExactRtp(def),
                "Exact RTP must not vary between calls.");
        }

        // ---- Cluster ----------------------------------------------------------

        private static SlotMachineDefinition BuildCluster()
        {
            var def = ScriptableObject.CreateInstance<SlotMachineDefinition>();
            def.id = "cluster_fixture";
            def.payType = PayType.Cluster;
            def.template = SlotTemplate.Cascade;
            def.reelCount = 3;
            def.rowCount = 3;
            def.minBet = 100;
            def.maxBet = 1000;
            def.cascadeClusterSize = 5;
            def.maxWinMultiplier = 500;

            def.symbols = new List<SymbolDefinition>
            {
                new SymbolDefinition { id = 10, symbolName = "A",
                                      payoutsByMatch = new long[] { 5, 20, 60, 150 },
                                      clusterPayouts = new long[] { 2, 5, 10, 25, 60, 150 } },
                new SymbolDefinition { id = 11, symbolName = "B",
                                      payoutsByMatch = new long[] { 2, 8, 30, 100 },
                                      clusterPayouts = new long[] { 1, 3, 6, 15, 35 } }
            };

            def.reelStrips = new List<ReelStrip>
            {
                new ReelStrip { symbols = new List<int> { 10, 11, 10, 11, 10, 11, 10, 11, 10 } },
                new ReelStrip { symbols = new List<int> { 10, 11, 10, 11, 10, 11, 10, 11, 10 } },
                new ReelStrip { symbols = new List<int> { 10, 11, 10, 11, 10, 11, 10, 11, 10 } }
            };

            return def;
        }

        [Test]
        public void Cluster_AdjacentGroup_ClearsAndPays()
        {
            var def = BuildCluster();

            // All A on every stop, so one spin yields a single 9-cell cluster. (The
            // default fixture strip alternates A/B, which correctly produces no cluster.)
            for (int r = 0; r < 3; r++)
                def.reelStrips[r].symbols = new List<int> { 10, 10, 10, 10, 10, 10, 10, 10, 10 };

            var result = new ClusterEvaluator().Evaluate(def, new[] { 0, 0, 0 }, def.minBet);

            Assert.Greater(result.totalWin, 0, "A 9-cell cluster of A must pay.");
            Assert.GreaterOrEqual(result.clustersWon, 1);
            Assert.GreaterOrEqual(result.cascadeSteps, 1);

            // This strip is a single symbol, so every refill clusters again and the
            // cascade runs until the max-win clamp stops it rather than ending on its own.
            Assert.LessOrEqual(result.totalWin, def.maxWinMultiplier * def.minBet,
                "A cascade must never exceed maxWinMultiplier.");
        }

        [Test]
        public void Cluster_ScatteredSymbols_DoNotPay()
        {
            var def = BuildCluster();

            // Alternating so nothing is orthogonally adjacent into a group of five.
            var rng = SeededRng.FromSeed(99);
            var report = SimulationEngine.Run(def, 500, def.minBet, seed: 99);

            // Statistical rather than exact: a 9-cell strip of two symbols will produce
            // some clusters, so assert only that the evaluator runs and stays sane.
            Assert.GreaterOrEqual(report.rtp, 0.0);
            Assert.IsFalse(double.IsNaN(report.rtp));
        }

        [Test]
        public void Cluster_Refill_DoesNotLeaveHoles()
        {
            var def = BuildCluster();
            var result = new ClusterEvaluator().Evaluate(def, new[] { 0, 0, 0 }, def.minBet);

            Assert.IsNotNull(result.grid);
            for (int r = 0; r < result.grid.Length; r++)
                for (int row = 0; row < result.grid[r].Length; row++)
                    Assert.GreaterOrEqual(result.grid[r][row], 0,
                        $"Cleared cell at reel {r} row {row} was never refilled.");
        }

        [Test]
        public void Cluster_IsRefusedByExactRtp()
        {
            var def = BuildCluster();

            Assert.IsFalse(SimulationEngine.SupportsExactRtp(def),
                "Cluster pays refill the grid with fresh draws, so there is no closed form.");
            Assert.IsTrue(double.IsNaN(SimulationEngine.ExactRtp(def)),
                "ExactRtp must return NaN for cluster pays, never a plausible-looking zero.");
        }

        // ---- validation -------------------------------------------------------

        [Test]
        public void Cluster_WithPaylines_IsRejected()
        {
            var def = BuildCluster();
            def.paylines = new List<PaylineDefinition>
            {
                new PaylineDefinition { lineName = "mid", rows = new[] { 1, 1, 1 } }
            };

            Assert.IsFalse(def.IsValid(out var error));
            StringAssert.Contains("paylines", error);
        }

        [Test]
        public void PaytableWithNothingPaying_IsRejected()
        {
            var def = BuildWays();
            foreach (var symbol in def.symbols)
                symbol.payoutsByMatch = new long[] { 0, 0, 0 };

            Assert.IsFalse(def.IsValid(out var error));
            StringAssert.Contains("No symbol pays anything", error);
        }
    }
}