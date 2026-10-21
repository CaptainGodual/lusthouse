using System.Collections.Generic;
using LustHouse.EditorTools;
using LustHouse.Slots;
using NUnit.Framework;
using UnityEngine;

namespace LustHouse.Tests
{
    /// <summary>
    /// The 2x2 mega block: an opt-in symbol's exact 2x2 morphs into one big sprite and pays
    /// four-of-a-kind x the symbol's multiplier, on top of whatever the paylines through its
    /// cells also won.
    ///
    /// Three properties carry the feature. Exactness: a wild in the quad breaks it, because
    /// a block is a shape and "any four symbols" would make every wild-heavy spin a block.
    /// Opt-in: a symbol with a zero multiplier never pays one, however square its run is.
    /// And the one the ship gate rests on — the closed form
    /// (rows-1) x SUM a(r)xa(r+1) x four-of-a-kind x multiplier matches the game's own
    /// evaluator under simulation, so ExactRtp can keep claiming to be exact.
    /// </summary>
    public class MegaBlockTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _spawned.Count; i++)
                if (_spawned[i] != null) Object.DestroyImmediate(_spawned[i]);
            _spawned.Clear();
        }

        /// <summary>
        /// A three-reel game whose every reel is a block-friendly strip: three Mangos then
        /// one other symbol. Adjacent Mango pairs are common, so blocks land on most spins —
        /// the Monte Carlo in the closed-form test converges in tens of thousands of spins
        /// rather than millions.
        /// </summary>
        private SlotMachineDefinition BuildBlockFixture(float multiplier = 1f)
        {
            var def = ScriptableObject.CreateInstance<SlotMachineDefinition>();
            _spawned.Add(def);
            def.id = "block_fixture";
            def.reelCount = 3;
            def.rowCount = 3;
            def.minBet = 100;
            def.maxBet = 1000;
            def.payType = PayType.Lines;
            def.targetRtp = 0.96f;

            def.symbols = new List<SymbolDefinition>
            {
                new SymbolDefinition
                {
                    id = 0, symbolName = "Wild", isWild = true,
                    payoutsByMatch = new long[] { 0, 0, 0 }
                },
                new SymbolDefinition
                {
                    id = 16, symbolName = "Mango", megaBlockMultiplier = multiplier,
                    payoutsByMatch = new long[] { 5, 20, 50 }
                },
                new SymbolDefinition
                {
                    id = 18, symbolName = "Nine", payoutsByMatch = new long[] { 1, 2, 4 }
                }
            };

            // {16,16,16,18} has two cyclic adjacent Mango pairs in four stops, so a(r) = 0.5.
            var strip = new List<int> { 16, 16, 16, 18 };
            def.reelStrips = new List<ReelStrip>
            {
                new ReelStrip { symbols = new List<int>(strip) },
                new ReelStrip { symbols = new List<int>(strip) },
                new ReelStrip { symbols = new List<int>(strip) }
            };

            def.paylines = new List<PaylineDefinition>
            {
                new PaylineDefinition { lineName = "mid", rows = new[] { 1, 1, 1 } }
            };

            return def;
        }

        private static List<BlockWin> Scan(SlotMachineDefinition def, int[][] grid, out long pay)
        {
            var into = new List<BlockWin>();
            pay = PaylineEvaluator.EvaluateBlocks(def, grid, def.minBet, into);
            return into;
        }

        // ------------------------------------------------------------------- paying ---

        [Test]
        public void Exact2x2_PaysFourOfAKindTimesMultiplier()
        {
            var def = BuildBlockFixture(multiplier: 2f);

            // One Mango 2x2 at (reel 0, row 0); the rest is Nine so no other window forms.
            var grid = new[]
            {
                new[] { 16, 16, 18 },
                new[] { 16, 16, 18 },
                new[] { 18, 18, 18 }
            };

            var blocks = Scan(def, grid, out long pay);

            Assert.AreEqual(1, blocks.Count, "Exactly one 2x2 window of Mangos landed.");
            var block = blocks[0];
            Assert.AreEqual(0, block.reel);
            Assert.AreEqual(0, block.row);
            Assert.AreEqual(16, block.symbolId);
            // Four-of-a-kind (20) x multiplier (2) = 40 bet-multiples = 4000.
            Assert.AreEqual(40 * def.minBet, block.payout,
                "A block pays four-of-a-kind x the symbol's multiplier.");
            Assert.AreEqual(40 * def.minBet, pay, "The scan returns the same total it records.");
        }

        [Test]
        public void Block_PaysOnTopOfTheLineWins()
        {
            var def = BuildBlockFixture(multiplier: 1f);

            // Stops seat a grid with a Mango block at (0,0) and five Mangos on the middle
            // payline... the middle line runs through rows 1, so build the grid so both
            // the block and the line win are real.
            var stops = new[] { 0, 0, 0 };   // every cell is a Mango
            var result = new PaylineEvaluator().Evaluate(def, stops, def.minBet);

            Assert.AreEqual(1, result.lineWins.Count, "The middle line is three Mangos.");
            Assert.AreEqual(5, result.lineWins[0].payout,
                "Three-of-a-kind Mango, straight off the paytable — in bet-multiples, like " +
                "every LineWin.");

            // A 3x3 slab of one symbol is (reels-1) x (rows-1) = 2 x 2 = four windows, and
            // every one of them pays on its own.
            Assert.AreEqual(4, result.blockWins.Count,
                "Four overlapping 2x2 windows form in a full-Mango grid.");

            long blockTotal = 0;
            foreach (var b in result.blockWins) blockTotal += b.payout;
            Assert.AreEqual(4 * 20 * def.minBet, blockTotal,
                "Each of the four windows pays four-of-a-kind x multiplier.");

            Assert.AreEqual(5 * def.minBet + blockTotal, result.totalWin,
                "Blocks add to the line wins; they never replace them.");
        }

        [Test]
        public void WildInTheQuad_BreaksTheBlock()
        {
            var def = BuildBlockFixture(multiplier: 1f);

            // Three Mangos and a wild inside the would-be 2x2. A wild does not substitute:
            // a block is an exact shape, and wilds are simply not part of it.
            var grid = new[]
            {
                new[] { 16, 16, 18 },
                new[] { 0, 16, 18 },
                new[] { 18, 18, 18 }
            };

            var blocks = Scan(def, grid, out long pay);

            Assert.AreEqual(0, blocks.Count,
                "A wild inside the quad means the four cells are not one symbol; no block.");
            Assert.AreEqual(0L, pay);

            // The same window without the wild does pay — the negative above is about the
            // wild, not about the scan being inert.
            var clean = new[]
            {
                new[] { 16, 16, 18 },
                new[] { 16, 16, 18 },
                new[] { 18, 18, 18 }
            };

            blocks = Scan(def, clean, out pay);
            Assert.AreEqual(1, blocks.Count, "Four Mangos in a 2x2 must pay.");
            Assert.AreEqual(20 * def.minBet, pay);
        }

        [Test]
        public void SymbolWithoutMultiplier_NeverBlocks()
        {
            var def = BuildBlockFixture(multiplier: 0f);

            // The full grid is Mangos — as square a run as the fixture can make.
            var grid = new[]
            {
                new[] { 16, 16, 16 },
                new[] { 16, 16, 16 },
                new[] { 16, 16, 16 }
            };

            var blocks = Scan(def, grid, out long pay);

            Assert.AreEqual(0, blocks.Count,
                "megaBlockMultiplier of 0 means the symbol never forms a block.");
            Assert.AreEqual(0L, pay);
        }

        [Test]
        public void Nine_NeverBlocks_EvenInASquare()
        {
            var def = BuildBlockFixture(multiplier: 1f);

            // Nines opted in nowhere (their multiplier is 0 by default), so a 2x2 of them
            // pays nothing as a block however it lands. Mango sits in the last column only,
            // which forms no window of its own.
            var grid = new[]
            {
                new[] { 18, 18, 16 },
                new[] { 18, 18, 16 },
                new[] { 16, 16, 16 }
            };

            var blocks = Scan(def, grid, out _);

            Assert.AreEqual(0, blocks.Count,
                "Only symbols that opted in with a multiplier can block.");
        }

        [Test]
        public void Spin_IncludesBlockWinsInTheTotal()
        {
            var def = BuildBlockFixture(multiplier: 1f);
            var rng = SeededRng.FromSeed(4242);

            // With a(r) = 0.5 on every reel, blocks land on most spins; find one with a
            // block and check the total carries it.
            SpinResult withBlock = default;
            bool found = false;
            for (int i = 0; i < 200 && !found; i++)
            {
                var result = ReelEngine.SpinBaseStatic(def, ref rng, def.minBet);
                if (result.blockWins != null && result.blockWins.Count > 0)
                {
                    withBlock = result;
                    found = true;
                }
            }

            Assert.IsTrue(found, "The fixture's strips make blocks common; 200 spins must hit one.");

            long lineTotal = 0;
            foreach (var line in withBlock.lineWins) lineTotal += line.payout * def.minBet;
            long blockTotal = 0;
            foreach (var block in withBlock.blockWins) blockTotal += block.payout;

            Assert.AreEqual(lineTotal + blockTotal, withBlock.totalWin,
                "The spin's total is lines plus blocks — the block is scored, not decorative.");
        }

        // --------------------------------------------------------------- exact maths ---

        [Test]
        public void ClosedForm_MatchesTheEvaluatorUnderSimulation()
        {
            var def = BuildBlockFixture(multiplier: 1.5f);

            double exact = SimulationEngine.BlockRtp(def);
            Assert.Greater(exact, 0.0,
                "The fixture is built from adjacent Mango pairs; the closed form must see them.");

            // Sample the game's own spins and average the block contribution. A hard-coded
            // tolerance would either be so wide it proves nothing or so tight it flakes:
            // at 100k spins the mean still wobbles by a few hundredths, which is invisible
            // against a value of 30 but fatal to a 0.005 absolute bound. So measure the
            // spread and assert the closed form lands inside five standard errors of it —
            // the same discipline the ship gate uses for the same reason.
            var rng = SeededRng.FromSeed(20261021);
            const long spins = 100_000;
            long blockTotal = 0;
            double sumSquares = 0.0;
            for (long i = 0; i < spins; i++)
            {
                var result = ReelEngine.SpinBaseStatic(def, ref rng, def.minBet);
                long spinBlocks = 0;
                if (result.blockWins != null)
                    foreach (var block in result.blockWins) spinBlocks += block.payout;

                double multiples = spinBlocks / (double)def.minBet;
                blockTotal += spinBlocks;
                sumSquares += multiples * multiples;
            }

            double sampled = blockTotal / (double)(spins * def.minBet);
            double variance = sumSquares / spins - sampled * sampled;
            double standardError = System.Math.Sqrt(System.Math.Max(0.0, variance) / spins);

            Assert.AreEqual(exact, sampled, 5.0 * standardError,
                "The closed form (rows-1) x SUM a(r)xa(r+1) x pay must match the evaluator's " +
                $"own blocks within sampling noise. exact={exact:F5} sampled={sampled:F5} " +
                $"(5 x SE = {5.0 * standardError:F5})");
        }

        [Test]
        public void ExactRtp_IncludesTheBlockTerm()
        {
            var def = BuildBlockFixture(multiplier: 1f);

            double withBlocks = SimulationEngine.ExactRtp(def);
            double blocks = SimulationEngine.BlockRtp(def);

            foreach (var symbol in def.symbols)
                symbol.megaBlockMultiplier = 0f;
            double withoutBlocks = SimulationEngine.ExactRtp(def);

            Assert.AreEqual(blocks, withBlocks - withoutBlocks, 1e-9,
                "ExactRtp must grow by exactly the block term when a symbol opts in — the " +
                "block rides in LineRtp, so the retune's weights carry it too.");
        }

        [Test]
        public void BlockRtp_IsZeroWhenNothingOptsIn()
        {
            var def = BuildBlockFixture(multiplier: 0f);
            Assert.AreEqual(0.0, SimulationEngine.BlockRtp(def), 1e-12,
                "No opted-in symbol means no block RTP, whatever the strips look like.");
        }
    }
}
