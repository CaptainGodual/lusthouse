using System.Collections.Generic;
using LustHouse.Slots;
using NUnit.Framework;
using UnityEngine;

namespace LustHouse.Tests
{
    /// <summary>
    /// The feature framework: a game declares features as data, the registry dispatches each to
    /// a handler, and the runner plays them after the base game. Free spins is the reference
    /// handler; hold &amp; spin and bonus games follow the same shape.
    /// </summary>
    public class FeatureRunnerTests
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
        /// Every stop is a scatter, so a free-spins feature triggers on every spin. That makes
        /// the feature path deterministic without depending on a lucky seed.
        /// </summary>
        private SlotMachineDefinition BuildScatterFixture(bool withFeature)
        {
            var def = ScriptableObject.CreateInstance<SlotMachineDefinition>();
            _spawned.Add(def);
            def.id = "feature_fixture";
            def.reelCount = 3;
            def.rowCount = 3;
            def.minBet = 100;
            def.maxBet = 1000;
            def.payType = PayType.Lines;

            def.symbols = new List<SymbolDefinition>
            {
                new SymbolDefinition { id = 0, symbolName = "S", isScatter = true },
                new SymbolDefinition { id = 1, symbolName = "A", payoutsByMatch = new long[] { 5, 20, 50 } }
            };

            def.reelStrips = new List<ReelStrip>
            {
                new ReelStrip { symbols = new List<int> { 0, 0, 0 } },
                new ReelStrip { symbols = new List<int> { 0, 0, 0 } },
                new ReelStrip { symbols = new List<int> { 0, 0, 0 } }
            };

            def.paylines = new List<PaylineDefinition>
            {
                new PaylineDefinition { lineName = "mid", rows = new[] { 1, 1, 1 } }
            };

            if (withFeature)
                def.features = new List<SlotFeature>
                {
                    new FreeSpinsFeature
                    {
                        id = "free_spins",
                        displayName = "Free Spins",
                        scatterCount = 3,
                        award = 5,
                        retrigger = false
                    }
                };

            return def;
        }

        [Test]
        public void Registry_ResolvesShippedHandlersOnly()
        {
            SlotFeatureRegistry.EnsureDefaults();

            Assert.IsNotNull(SlotFeatureRegistry.Create(new FreeSpinsFeature()),
                "Free spins has a shipped handler and must resolve.");
            Assert.IsNotNull(SlotFeatureRegistry.Create(new HoldAndSpinFeature()),
                "Hold & spin has a shipped handler and must resolve.");
            Assert.IsNull(SlotFeatureRegistry.Create(new BonusGameFeature()),
                "Bonus games have no handler yet; an unregistered feature must not run.");
            Assert.IsNull(SlotFeatureRegistry.Create(null));
        }

        [Test]
        public void NoFeatures_RunsOnlyTheBaseSpin()
        {
            var def = BuildScatterFixture(withFeature: false);
            var rng = SeededRng.FromSeed(1);

            var result = ReelEngine.SpinStatic(def, ref rng, def.minBet);

            Assert.AreEqual(1, result.spinsRun);
            Assert.IsFalse(result.triggeredFreeSpins);
            Assert.IsTrue(result.featureSteps == null || result.featureSteps.Count == 0);
        }

        [Test]
        public void FreeSpinsFeature_PlaysTheAwardedSpins()
        {
            var def = BuildScatterFixture(withFeature: true);
            var rng = SeededRng.FromSeed(1);

            var result = ReelEngine.SpinStatic(def, ref rng, def.minBet);

            Assert.IsTrue(result.triggeredFreeSpins);
            Assert.AreEqual(5, result.freeSpinsAwarded);
            Assert.AreEqual(6, result.spinsRun, "1 base spin + 5 awarded free spins.");
            Assert.IsNotNull(result.featureSteps);
            Assert.AreEqual(5, result.featureSteps.Count);
            bool allSpin = true;
            foreach (var step in result.featureSteps)
                if (step.kind != FeatureStepKind.Spin) allSpin = false;
            Assert.IsTrue(allSpin, "Every free-spin step is a spin the presenter must animate.");
        }

        [Test]
        public void FreeSpins_RetriggerAddsSpins()
        {
            var def = BuildScatterFixture(withFeature: true);
            var free = (FreeSpinsFeature)def.features[0];
            free.retrigger = true;
            free.retriggerAward = 2;
            var rng = SeededRng.FromSeed(1);

            var result = ReelEngine.SpinStatic(def, ref rng, def.minBet);

            // Every free spin is also all-scatter, so each one retriggers. The guard caps it.
            Assert.Greater(result.spinsRun, 6);
        }

        [Test]
        public void LegacyFlags_AreStillHonouredUntilMigrated()
        {
            var def = BuildScatterFixture(withFeature: false);
            def.hasFreeSpins = true;
            def.freeSpinScatterCount = 3;
            def.freeSpinAward = 5;
            def.freeSpinBetMultiplier = 1;

            var features = def.GetFeatures();

            Assert.AreEqual(1, features.Count);
            Assert.IsInstanceOf<FreeSpinsFeature>(features[0]);
            Assert.AreEqual(5, ((FreeSpinsFeature)features[0]).award);
        }

        [Test]
        public void ExplicitFeatures_WinOverLegacyFlags()
        {
            var def = BuildScatterFixture(withFeature: true);
            def.hasFreeSpins = false;   // must be ignored: features is non-empty

            var features = def.GetFeatures();

            Assert.AreEqual(1, features.Count);
            Assert.IsInstanceOf<FreeSpinsFeature>(features[0]);
        }

        // ---- hold & spin ---------------------------------------------------

        /// <summary>
        /// A 3x2 hold &amp; spin fixture. When <paramref name="stripsAreSpecial"/> is false the
        /// reels contain only a paying line symbol, so every respin lands a base-game win that
        /// the feature must ignore.
        /// </summary>
        private SlotMachineDefinition BuildHoldFixture(bool stripsAreSpecial, int specialValue,
                                                       int initialLocked, int maxRespins,
                                                       bool jackpotTiers = false)
        {
            var def = ScriptableObject.CreateInstance<SlotMachineDefinition>();
            _spawned.Add(def);
            def.id = "hold_fixture";
            def.reelCount = 3;
            def.rowCount = 2;
            def.minBet = 100;
            def.maxBet = 1000;
            def.payType = PayType.Lines;

            def.symbols = new List<SymbolDefinition>
            {
                new SymbolDefinition
                {
                    id = 0, symbolName = "A",
                    payoutsByMatch = new long[] { 10, 20, 50 }
                },
                new SymbolDefinition
                {
                    id = 7, symbolName = "Diamond", isSpecial = true,
                    holdSpinValue = specialValue
                },
                new SymbolDefinition { id = 1, symbolName = "Scatter", isScatter = true }
            };

            int stripId = stripsAreSpecial ? 7 : 0;
            def.reelStrips = new List<ReelStrip>();
            for (int r = 0; r < def.reelCount; r++)
                def.reelStrips.Add(new ReelStrip { symbols = new List<int> { stripId } });

            def.paylines = new List<PaylineDefinition>
            {
                new PaylineDefinition { lineName = "top", rows = new[] { 0, 0, 0 } }
            };

            def.features = new List<SlotFeature>
            {
                new HoldAndSpinFeature
                {
                    id = "hold_spin",
                    displayName = "Hold & Spin",
                    initialLocked = initialLocked,
                    maxRespins = maxRespins,
                    jackpotTiers = jackpotTiers,
                    jackpotValues = new long[] { 10, 50, 200 }
                }
            };

            return def;
        }

        private static HoldAndSpinHandler CreateHoldHandler(SlotMachineDefinition def)
        {
            SlotFeatureRegistry.EnsureDefaults();
            var handler = SlotFeatureRegistry.Create(def.features[0]) as HoldAndSpinHandler;
            Assert.IsNotNull(handler, "Hold & spin must resolve through the registry.");
            handler.Definition = def.features[0];
            return handler;
        }

        private static FeatureResult RunHold(SlotMachineDefinition def, int[][] grid,
                                             long bet = 100, int seed = 1)
        {
            var ctx = new FeatureContext
            {
                game = def,
                feature = def.features[0],
                baseResult = new SpinResult { grid = grid, bet = bet },
                rng = SeededRng.FromSeed(seed),
                bet = bet
            };

            return CreateHoldHandler(def).Run(ctx);
        }

        private static int CountSteps(FeatureResult result, FeatureStepKind kind)
        {
            int count = 0;
            foreach (var step in result.steps)
                if (step.kind == kind) count++;
            return count;
        }

        private static int UniqueLocks(FeatureResult result)
        {
            var seen = new HashSet<int>();
            foreach (var step in result.steps)
                if (step.kind == FeatureStepKind.Lock)
                    seen.Add(step.reel * 100 + step.row);
            return seen.Count;
        }

        [Test]
        public void HoldAndSpin_NeedsTheInitialLockedCount()
        {
            var def = BuildHoldFixture(stripsAreSpecial: false, specialValue: 5,
                initialLocked: 2, maxRespins: 3);
            var handler = CreateHoldHandler(def);

            var one = new SpinResult
            {
                grid = new[] { new[] { 7, 0 }, new[] { 0, 0 }, new[] { 0, 0 } }
            };
            var two = new SpinResult
            {
                grid = new[] { new[] { 7, 0 }, new[] { 7, 0 }, new[] { 0, 0 } }
            };

            Assert.IsFalse(handler.CanTrigger(def, one));
            Assert.IsTrue(handler.CanTrigger(def, two));
        }

        [Test]
        public void HoldAndSpin_PlaysRespinsAndPaysOnlyLockedPrizes()
        {
            var def = BuildHoldFixture(stripsAreSpecial: false, specialValue: 5,
                initialLocked: 2, maxRespins: 3);
            var grid = new[] { new[] { 7, 0 }, new[] { 7, 0 }, new[] { 0, 0 } };

            var result = RunHold(def, grid);

            Assert.IsTrue(result.triggeredHoldAndSpin);
            Assert.AreEqual(3, result.spinsRun, "Three respins, none of which lock a new cell.");
            Assert.AreEqual(3, CountSteps(result, FeatureStepKind.Spin));
            Assert.AreEqual(2, CountSteps(result, FeatureStepKind.Lock));
            Assert.AreEqual(2 * 5 * 100L, result.totalWin,
                "Only the two locked prizes pay; the winning base line on each respin is ignored.");
            Assert.AreEqual(0, CountSteps(result, FeatureStepKind.Award));
        }

        [Test]
        public void HoldAndSpin_LocksEachCellOnceAndFullGridEndsTheRound()
        {
            var def = BuildHoldFixture(stripsAreSpecial: true, specialValue: 2,
                initialLocked: 1, maxRespins: 3);
            var grid = new[] { new[] { 7, 0 }, new[] { 0, 0 }, new[] { 0, 0 } };

            var result = RunHold(def, grid);

            Assert.AreEqual(1, result.spinsRun,
                "The first respin fills all five free cells, so the round must end there.");
            Assert.AreEqual(6, CountSteps(result, FeatureStepKind.Lock));
            Assert.AreEqual(6, UniqueLocks(result), "A held cell must never be locked or paid twice.");
            Assert.AreEqual(6 * 2 * 100L, result.totalWin);
        }

        [Test]
        public void HoldAndSpin_FullTriggerGridAwardsTheTopJackpot()
        {
            var def = BuildHoldFixture(stripsAreSpecial: true, specialValue: 5,
                initialLocked: 1, maxRespins: 3, jackpotTiers: true);
            var grid = new[]
            {
                new[] { 7, 7 },
                new[] { 7, 7 },
                new[] { 7, 7 }
            };

            var result = RunHold(def, grid);

            Assert.AreEqual(0, result.spinsRun, "A full grid is already complete at trigger.");
            Assert.AreEqual(6, CountSteps(result, FeatureStepKind.Lock));
            Assert.AreEqual(1, CountSteps(result, FeatureStepKind.Award));
            Assert.AreEqual((6 * 5 + 200) * 100L, result.totalWin);
        }

        [Test]
        public void BaseSpin_DoesNotRunFeatures()
        {
            var def = BuildScatterFixture(withFeature: true);
            var free = (FreeSpinsFeature)def.features[0];
            free.scatterCount = 0;   // would trigger on every spin if features ran
            free.award = 1;

            var baseRng = SeededRng.FromSeed(7);
            var baseResult = ReelEngine.SpinBaseStatic(def, ref baseRng, def.minBet);

            Assert.IsFalse(baseResult.triggeredFreeSpins,
                "Feature handlers spin through SpinBaseStatic so they cannot recurse.");
            Assert.AreEqual(1, baseResult.spinsRun);
            Assert.IsTrue(baseResult.featureSteps == null || baseResult.featureSteps.Count == 0);

            var fullRng = SeededRng.FromSeed(7);
            var fullResult = ReelEngine.SpinStatic(def, ref fullRng, def.minBet);
            Assert.IsTrue(fullResult.triggeredFreeSpins);
        }
    }
}
