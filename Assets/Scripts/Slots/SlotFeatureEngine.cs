using System;
using System.Collections.Generic;

namespace LustHouse.Slots
{
    /// <summary>One observable beat of a feature, for the presenter to animate.</summary>
    public enum FeatureStepKind { Spin, Lock, Pick, Reveal, Award, Meter }

    public sealed class FeatureStep
    {
        public FeatureStepKind kind;
        public SpinResult spin;          // kind == Spin
        public int reel = -1, row = -1;  // kind == Lock
        public int symbolId = -1;        // kind == Lock: the symbol now held in that cell
        public int choiceIndex = -1;     // kind == Pick / Reveal

        /// <summary>
        /// kind == Lock: the cell's prize. kind == Award / Meter: the amount awarded.
        /// Both are in bet-multiples so the presenter can label them without knowing the bet.
        /// </summary>
        public long amount;
    }

    /// <summary>The outcome of one feature run.</summary>
    public sealed class FeatureResult
    {
        public long totalWin;
        public int spinsRun;
        public bool triggeredFreeSpins;
        public int freeSpinsAwarded;
        public bool triggeredHoldAndSpin;
        public bool triggeredBonusBuy;

        /// <summary>A pick-a-prize (or wheel/trail) bonus round ran.</summary>
        public bool triggeredBonus;

        /// <summary>How many trigger symbols landed. Reported so the UI can show the count.</summary>
        public int bonusTriggers;

        public readonly List<FeatureStep> steps = new List<FeatureStep>();
    }

    /// <summary>
    /// Everything a feature needs to run. Pure data — a handler must not touch the wallet or the
    /// scene, so the same code runs in the game, the simulator, and tests.
    /// </summary>
    public sealed class FeatureContext
    {
        public SlotMachineDefinition game;
        public SlotFeature feature;
        public SpinResult baseResult;
        public SeededRng rng;
        public long bet;
        public IFeatureChoice choice;
    }

    /// <summary>
    /// Supplies a player's interactive choice. The UI implements it for the real player; the
    /// simulator implements it deterministically so the RTP gate stays reproducible.
    /// </summary>
    public interface IFeatureChoice
    {
        /// <summary>A choice index in [0, choices).</summary>
        int Choose(int choices, SeededRng rng);
    }

    /// <summary>One feature's behaviour, keyed to its definition by the registry.</summary>
    public interface ISlotFeatureHandler
    {
        SlotFeature Definition { get; set; }

        /// <summary>Whether this feature fires given the base game outcome.</summary>
        bool CanTrigger(SlotMachineDefinition game, SpinResult baseResult);

        /// <summary>Runs the feature to completion. Must not debit or credit the wallet.</summary>
        FeatureResult Run(FeatureContext ctx);
    }

    /// <summary>
    /// Maps a feature definition type to its handler. This is the "how does the engine know"
    /// answer: the game declares data, the registry supplies behaviour.
    /// </summary>
    public static class SlotFeatureRegistry
    {
        private static readonly Dictionary<Type, Func<ISlotFeatureHandler>> Factories =
            new Dictionary<Type, Func<ISlotFeatureHandler>>();

        private static bool _defaultsRegistered;

        public static void Register<TDef, THandler>()
            where TDef : SlotFeature
            where THandler : ISlotFeatureHandler, new()
        {
            Factories[typeof(TDef)] = () => new THandler();
        }

        public static ISlotFeatureHandler Create(SlotFeature def)
        {
            if (def == null) return null;
            return Factories.TryGetValue(def.GetType(), out var factory) ? factory() : null;
        }

        /// <summary>Registers the shipped handlers. Idempotent.</summary>
        public static void EnsureDefaults()
        {
            if (_defaultsRegistered) return;
            _defaultsRegistered = true;

            Register<FreeSpinsFeature, FreeSpinsHandler>();
            Register<HoldAndSpinFeature, HoldAndSpinHandler>();
            Register<BonusGameFeature, BonusGameHandler>();
        }
    }

    /// <summary>Aggregated result of running every triggered feature on a spin.</summary>
    public sealed class FeatureRunResult
    {
        public long totalWin;
        public int spinsRun;
        public bool triggeredFreeSpins;
        public int freeSpinsAwarded;
        public bool triggeredHoldAndSpin;
        public bool triggeredBonusBuy;

        /// <summary>At least one bonus round ran. Distinct from the buy path, which skips the trigger.</summary>
        public bool triggeredBonus;

        public bool anyTriggered;
        public readonly List<FeatureStep> steps = new List<FeatureStep>();
    }

    /// <summary>
    /// Runs a spin's features in declaration order, after the base game. Static so the game, the
    /// simulator and tests share one path and one RNG stream.
    /// </summary>
    public static class FeatureRunner
    {
        public static FeatureRunResult Run(SlotMachineDefinition game, SpinResult baseResult,
                                           long bet, ref SeededRng rng, IFeatureChoice choice)
        {
            var result = new FeatureRunResult();
            if (game == null) return result;

            var features = game.GetFeatures();
            if (features == null || features.Count == 0) return result;

            SlotFeatureRegistry.EnsureDefaults();

            var ctx = new FeatureContext
            {
                game = game,
                baseResult = baseResult,
                rng = rng,
                bet = bet,
                choice = choice
            };

            for (int i = 0; i < features.Count; i++)
            {
                var feature = features[i];
                if (feature == null) continue;

                var handler = SlotFeatureRegistry.Create(feature);
                if (handler == null) continue;

                handler.Definition = feature;
                ctx.feature = feature;

                if (!handler.CanTrigger(game, baseResult)) continue;

                var fr = handler.Run(ctx);
                if (fr == null) continue;

                result.totalWin += fr.totalWin;
                result.spinsRun += fr.spinsRun;
                result.triggeredFreeSpins |= fr.triggeredFreeSpins;
                result.freeSpinsAwarded += fr.freeSpinsAwarded;
                result.triggeredHoldAndSpin |= fr.triggeredHoldAndSpin;
                result.triggeredBonusBuy |= fr.triggeredBonusBuy;
                result.triggeredBonus |= fr.triggeredBonus;
                result.anyTriggered = true;
                result.steps.AddRange(fr.steps);
            }

            return result;
        }

        /// <summary>Folds a feature run into a <see cref="SpinResult"/> in place.</summary>
        public static void Apply(ref SpinResult result, FeatureRunResult features)
        {
            if (features == null) return;

            result.totalWin += features.totalWin;
            result.spinsRun += features.spinsRun;
            result.triggeredFreeSpins |= features.triggeredFreeSpins;
            result.freeSpinsAwarded += features.freeSpinsAwarded;
            result.triggeredHoldAndSpin |= features.triggeredHoldAndSpin;
            result.triggeredBonusBuy |= features.triggeredBonusBuy;
            result.triggeredBonus |= features.triggeredBonus;

            if (features.steps.Count > 0)
            {
                if (result.featureSteps == null) result.featureSteps = new List<FeatureStep>();
                result.featureSteps.AddRange(features.steps);
            }
        }
    }
}
