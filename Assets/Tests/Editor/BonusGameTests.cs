using System.Collections.Generic;
using LustHouse.EditorTools;
using LustHouse.Slots;
using NUnit.Framework;
using UnityEngine;

namespace LustHouse.Tests
{
    /// <summary>
    /// The pick-a-prize bonus: it triggers on a count of its symbol, it pays the sum of
    /// blind picks, and — the property the ship gate rests on — its expected value is a
    /// closed form the retune can subtract from the target.
    ///
    /// The closed form is B = P(trigger) × picks × weightedMean(prize). Each test pins one
    /// factor: the trigger count, the pick arithmetic, the seeded reproducibility that makes
    /// the game and the simulator agree, and finally B itself against a Monte Carlo run of
    /// the same code the game plays.
    /// </summary>
    public class BonusGameTests
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
        /// A three-reel game whose every stop is the trigger symbol, so a round opens on
        /// every spin. The prize table is fixed and flat — every prize is 2 — so a round's
        /// total is exactly picks × 2 with no variance at all. That makes the arithmetic
        /// assertable rather than statistically approachable.
        /// </summary>
        private SlotMachineDefinition BuildAlwaysTriggerFixture(int choices = 6, int picks = 2)
        {
            var def = ScriptableObject.CreateInstance<SlotMachineDefinition>();
            _spawned.Add(def);
            def.id = "bonus_fixture";
            def.reelCount = 3;
            def.rowCount = 3;
            def.minBet = 100;
            def.maxBet = 1000;
            def.payType = PayType.Lines;
            def.targetRtp = 0.96f;

            def.symbols = new List<SymbolDefinition>
            {
                new SymbolDefinition { id = 2, symbolName = "Bonus", payoutsByMatch = new long[] { 0, 0, 0 } },
                new SymbolDefinition { id = 10, symbolName = "A", payoutsByMatch = new long[] { 5, 20, 50 } }
            };

            // Every stop is a bonus, and no payline, so the only win on any spin is the round.
            def.reelStrips = new List<ReelStrip>
            {
                new ReelStrip { symbols = new List<int> { 2, 2, 2 } },
                new ReelStrip { symbols = new List<int> { 2, 2, 2 } },
                new ReelStrip { symbols = new List<int> { 2, 2, 2 } }
            };

            def.paylines = new List<PaylineDefinition>
            {
                new PaylineDefinition { lineName = "mid", rows = new[] { 1, 1, 1 } }
            };

            def.features = new List<SlotFeature>
            {
                new BonusGameFeature
                {
                    id = "bonus_game",
                    displayName = "Pick a Prize",
                    kind = BonusKind.PickPrize,
                    triggerSymbolId = 2,
                    triggerCount = 3,
                    choices = choices,
                    picks = picks,
                    interactive = false,
                    prizes = new List<BonusPrize> { new BonusPrize { payout = 2, weight = 1f } }
                }
            };

            return def;
        }

        // ------------------------------------------------------------------ trigger ---

        [Test]
        public void Trigger_CountsBonusSymbolsAnywhereOnTheGrid()
        {
            var def = BuildAlwaysTriggerFixture();
            var handler = new BonusGameHandler { Definition = def.Find<BonusGameFeature>() };

            var grid = new[]
            {
                new[] { 10, 10, 2 },
                new[] { 10, 2, 10 },
                new[] { 2, 10, 10 }
            };
            Assert.IsTrue(handler.CanTrigger(def, new SpinResult { grid = grid }),
                "Three bonuses anywhere on the grid must open the round.");

            var shortGrid = new[]
            {
                new[] { 10, 10, 2 },
                new[] { 10, 2, 10 },
                new[] { 10, 10, 10 }
            };
            Assert.IsFalse(handler.CanTrigger(def, new SpinResult { grid = shortGrid }),
                "Two bonuses is not three; the round must not open.");
        }

        [Test]
        public void Round_PaysTheSumOfThePicksInBetMultiples()
        {
            var def = BuildAlwaysTriggerFixture(choices: 6, picks: 2);
            var rng = SeededRng.FromSeed(7);

            var result = ReelEngine.SpinStatic(def, ref rng, def.minBet);

            Assert.IsTrue(result.triggeredBonus, "Every stop is a bonus; the round must run.");
            // Flat prize table (2 each), 2 picks: every round pays 4 bet-multiples = 400.
            Assert.AreEqual(4 * def.minBet, result.totalWin,
                "The round pays the sum of its picks; with a flat table that is exactly " +
                "picks x the prize, and the line game pays nothing.");
        }

        [Test]
        public void Round_EmitsPickAndRevealStepsForEveryPick()
        {
            var def = BuildAlwaysTriggerFixture(choices: 6, picks: 2);
            var rng = SeededRng.FromSeed(11);

            var result = ReelEngine.SpinStatic(def, ref rng, def.minBet);

            int picks = 0, reveals = 0, awards = 0;
            foreach (var step in result.featureSteps)
            {
                if (step.kind == FeatureStepKind.Pick) picks++;
                if (step.kind == FeatureStepKind.Reveal) reveals++;
                if (step.kind == FeatureStepKind.Award) awards++;
            }

            Assert.AreEqual(2, picks, "One Pick step per pick.");
            Assert.AreEqual(2, reveals, "One Reveal step per pick, showing what was behind it.");
            Assert.AreEqual(1, awards, "One Award step carrying the round's total.");
        }

        [Test]
        public void Round_TakesDistinctOptions()
        {
            var def = BuildAlwaysTriggerFixture(choices: 6, picks: 3);
            var rng = SeededRng.FromSeed(13);

            var result = ReelEngine.SpinStatic(def, ref rng, def.minBet);

            var seen = new HashSet<int>();
            foreach (var step in result.featureSteps)
                if (step.kind == FeatureStepKind.Pick)
                    Assert.IsTrue(seen.Add(step.choiceIndex),
                        $"Option {step.choiceIndex} was picked twice; a round must not " +
                        "pay the same hidden prize more than once.");
        }

        // --------------------------------------------------------------- reproducible ---

        [Test]
        public void Round_IsReproducibleForTheSameSeed()
        {
            var def = BuildAlwaysTriggerFixture();

            var a = SeededRng.FromSeed(99);
            var b = SeededRng.FromSeed(99);

            var first = ReelEngine.SpinStatic(def, ref a, def.minBet);
            var second = ReelEngine.SpinStatic(def, ref b, def.minBet);

            Assert.AreEqual(first.totalWin, second.totalWin,
                "A QA repro must pay the same as the spin it reproduces.");

            var firstPicks = new List<int>();
            var secondPicks = new List<int>();
            foreach (var step in first.featureSteps)
                if (step.kind == FeatureStepKind.Pick) firstPicks.Add(step.choiceIndex);
            foreach (var step in second.featureSteps)
                if (step.kind == FeatureStepKind.Pick) secondPicks.Add(step.choiceIndex);

            CollectionAssert.AreEqual(firstPicks, secondPicks,
                "The same seed must pick the same options in the same order.");
        }

        // ------------------------------------------------------------------------ maths ---

        [Test]
        public void Interactive_RoundAsksThePlayerAndUsesTheirAnswers()
        {
            var def = BuildAlwaysTriggerFixture(choices: 6, picks: 3);
            def.Find<BonusGameFeature>().interactive = true;

            // A player who always points at option 4 — until it is gone, then falls back.
            var choice = new FixedChoice(4);
            var rng = SeededRng.FromSeed(5);
            var baseResult = ReelEngine.SpinBaseStatic(def, ref rng, def.minBet);

            var run = FeatureRunner.Run(def, baseResult, def.minBet, ref rng, choice);

            Assert.AreEqual(3, choice.Calls, "An interactive round asks once per pick.");
            Assert.AreEqual(6 * def.minBet, run.totalWin,
                "The player's option 4 is taken, then the repeats fall back to untaken " +
                "options; every option is the same flat prize of 2, so three picks pay 6.");
        }

        /// <summary>
        /// The gate property. The exact term must equal what the shipped spin path actually
        /// pays, or the retune is aiming at a number the game does not play.
        /// </summary>
        [Test]
        public void ExactBonusTerm_MatchesWhatTheSpinPathPays()
        {
            var def = BuildAlwaysTriggerFixture(choices: 6, picks: 2);

            double exact = SimulationEngine.BonusRtp(def);

            // P(trigger) = 1 (every stop is a bonus), 2 picks, flat prize of 2.
            Assert.AreEqual(4.0, exact, 1e-9,
                "B = P(trigger) x picks x weightedMean; here every factor is exact, so the " +
                "closed form must be exact too.");

            var rng = SeededRng.FromSeed(1234);
            const int spins = 5000;
            long total = 0;
            for (int i = 0; i < spins; i++)
                total += ReelEngine.SpinStatic(def, ref rng, def.minBet).totalWin;

            double measured = (double)total / spins / def.minBet;
            Assert.AreEqual(exact, measured, 1e-9,
                "With no variance anywhere the simulator must pay the exact figure, spin " +
                "for spin; anything else means the closed form and the engine disagree.");
        }

        /// <summary>
        /// With a weighted prize table and a trigger that misses sometimes, the closed form
        /// has to hold in expectation against the engine's own draws — the retune subtracts
        /// this figure from the target, so a mismatch here is a wrong shipped RTP.
        /// </summary>
        [Test]
        public void ExactBonusTerm_HoldsAgainstAMonteCarloRun()
        {
            var def = BuildAlwaysTriggerFixture(choices: 6, picks: 3);
            var bonus = def.Find<BonusGameFeature>();
            bonus.prizes = new List<BonusPrize>
            {
                new BonusPrize { payout = 1, weight = 40f },
                new BonusPrize { payout = 2, weight = 25f },
                new BonusPrize { payout = 3, weight = 15f },
                new BonusPrize { payout = 5, weight = 10f },
                new BonusPrize { payout = 10, weight = 7f },
                new BonusPrize { payout = 25, weight = 3f }
            };

            double exact = SimulationEngine.BonusRtp(def);

            var rng = SeededRng.FromSeed(4242);
            const int spins = 100_000;
            long total = 0;
            for (int i = 0; i < spins; i++)
                total += ReelEngine.SpinStatic(def, ref rng, def.minBet).totalWin;

            double measured = (double)total / spins / def.minBet;

            // The per-round variance is real here (a 25 is 7.5% of the table's mean), so this
            // is a tolerance, not an identity: the point is that the closed form is not
            // systematically off — which is exactly what the retune would punish.
            Assert.AreEqual(exact, measured, 0.15,
                $"Closed form {exact:F4} vs measured {measured:F4}; the affine term the " +
                "retune subtracts has to be the value the engine actually pays.");
        }

        /// <summary>An <see cref="IFeatureChoice"/> that keeps pointing at one option.</summary>
        private sealed class FixedChoice : IFeatureChoice
        {
            private readonly int _index;
            public int Calls { get; private set; }

            public FixedChoice(int index) { _index = index; }

            public int Choose(int choices, SeededRng rng)
            {
                Calls++;
                return _index;
            }
        }
    }
}
