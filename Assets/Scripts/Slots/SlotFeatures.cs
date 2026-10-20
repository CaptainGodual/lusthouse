using System;
using System.Collections.Generic;
using UnityEngine;

namespace LustHouse.Slots
{
    /// <summary>
    /// Base type for a game feature.
    ///
    /// A game declares the features it has as data — a <c>List&lt;SlotFeature&gt;</c> on its
    /// definition — rather than the engine branching on flags. Adding a feature to a game is a
    /// content change; adding a new feature <em>type</em> means a new definition class plus a
    /// handler (plan §3.5: no new C# per game).
    /// </summary>
    [Serializable]
    public abstract class SlotFeature
    {
        public string id = "";
        public string displayName = "";
    }

    /// <summary>Scatter-triggered free spins (plan §3.2 template B).</summary>
    [Serializable]
    public sealed class FreeSpinsFeature : SlotFeature
    {
        [Tooltip("Number of scatters anywhere on the grid that triggers the feature.")]
        public int scatterCount = 3;

        [Tooltip("Spins awarded on trigger.")]
        public int award = 10;

        [Tooltip("Scatters during free spins add more spins.")]
        public bool retrigger = true;

        public int retriggerAward = 10;

        [Tooltip("Free-spin bet as a multiple of the triggering bet. Free spins are never debited.")]
        public int betMultiplier = 1;

        [Tooltip("Each free spin raises the win multiplier by multiplierStep.")]
        public bool multiplierGrows = false;

        public int multiplierStep = 1;
        public int maxMultiplier = 5;

        [Tooltip("Symbol id injected onto the reels during free spins. -1 = none.")]
        public int extraWildId = -1;

        [Tooltip("Reel-set id to use during free spins. Blank = the base reels.")]
        public string reelSetId = "";
    }

    /// <summary>Hold &amp; spin: lock special symbols and respin (plan §3.2 template D).</summary>
    [Serializable]
    public sealed class HoldAndSpinFeature : SlotFeature
    {
        [Tooltip("Symbol id that locks and carries a prize. -1 = any isSpecial symbol.")]
        public int specialSymbolId = -1;

        public int initialLocked = 3;
        public int maxRespins = 10;

        [Tooltip("Award a fixed jackpot when the grid fills.")]
        public bool jackpotTiers = false;

        [Tooltip("Jackpot tiers in bet-multiples. Filling the grid pays the last entry (the top tier).")]
        public long[] jackpotValues = { 0, 0, 0 };
    }

    public enum BonusKind { PickPrize, Wheel, Trail, CardFlip }

    /// <summary>One weighted prize in a bonus game.</summary>
    [Serializable]
    public sealed class BonusPrize
    {
        /// <summary>Payout in bet-multiples.</summary>
        public long payout;
        public float weight = 1f;
    }

    /// <summary>An interactive or auto-resolved bonus round.</summary>
    [Serializable]
    public sealed class BonusGameFeature : SlotFeature
    {
        public BonusKind kind = BonusKind.PickPrize;

        [Tooltip("Symbol id that triggers the bonus. -1 = any scatter.")]
        public int triggerSymbolId = -1;

        public int triggerCount = 3;

        [Tooltip("How many options the player chooses from.")]
        public int choices = 12;

        [Tooltip("How many picks the player gets.")]
        public int picks = 3;

        [Tooltip("Player picks; the handler receives the choice. Off = the engine resolves it.")]
        public bool interactive = true;

        public List<BonusPrize> prizes = new List<BonusPrize>();
    }

    /// <summary>
    /// Feature lookup helpers. Features are data, so callers ask for what they need rather than
    /// branching on a flag.
    /// </summary>
    public static class SlotFeatureExtensions
    {
        /// <summary>The first feature of type <typeparamref name="T"/>, or null.</summary>
        public static T Find<T>(this SlotMachineDefinition def) where T : SlotFeature
        {
            if (def == null) return null;
            foreach (var feature in def.GetFeatures())
                if (feature is T typed) return typed;
            return null;
        }
    }
}
