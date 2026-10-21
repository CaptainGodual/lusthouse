using LustHouse.Audio;
using UnityEngine;

namespace LustHouse.Slots
{
    /// <summary>
    /// What the player hears when a game does something.
    ///
    /// The mapping lives here rather than in the presenter so that the meaning of a
    /// moment — a reel landing, a win being paid, a feature starting — is decided in
    /// one place. The presenter says *what happened*; this says what that sounds like.
    ///
    /// Nothing here touches an <c>AudioSource</c>, so a test can watch the whole
    /// vocabulary without a speaker attached.
    /// </summary>
    public static class SlotAudio
    {
        /// <summary>
        /// How much lower each successive reel's stop lands.
        ///
        /// Reels land one after another, and a run of identical knocks is a run of
        /// identical knocks. Dropping the pitch a little each time turns it into a
        /// descending run, which is what a row of reels stopping sounds like.
        /// </summary>
        private const float ReelStopPitchStep = 0.06f;

        /// <summary>A game has opened and is ready to play.</summary>
        public static void GameEntered() => AudioFeedback.Play(AudioCue.GameWelcome);

        /// <summary>The reels are about to turn.</summary>
        public static void SpinStarted() => AudioFeedback.PlayLoop(AudioCue.ReelSpinLoop);

        /// <summary>The reels have stopped.</summary>
        public static void SpinEnded() => AudioFeedback.StopLoop(AudioCue.ReelSpinLoop);

        /// <summary>One reel has landed.</summary>
        public static void ReelStopped(int reelIndex)
        {
            float pitch = 1f - Mathf.Max(0, reelIndex) * ReelStopPitchStep;
            AudioFeedback.Play(AudioCue.ReelStop, Mathf.Clamp(pitch, 0.5f, 1.5f));
        }

        /// <summary>
        /// Whether this spin set a feature off.
        ///
        /// The bonus buy counts: the player paid for that round, and it deserves the
        /// same announcement as one they earned.
        /// </summary>
        public static bool HasFeatureTriggered(SpinResult result) =>
            result.triggeredFreeSpins ||
            result.triggeredHoldAndSpin ||
            result.triggeredBonus ||
            result.triggeredBonusBuy;

        /// <summary>
        /// A feature round has started.
        ///
        /// Called when the player can see the trigger, not when the maths found it.
        /// The maths resolves the whole feature round before a single reel has moved,
        /// so announcing it then would be announcing it a second before the player
        /// has any idea why.
        /// </summary>
        public static void FeatureTriggered(SpinResult result)
        {
            if (!HasFeatureTriggered(result)) return;
            AudioFeedback.Play(AudioCue.BonusTrigger);
        }

        /// <summary>A win has been paid out.</summary>
        public static void WinResolved(SpinResult result, SlotMachineDefinition definition)
        {
            if (result.totalWin <= 0) return;
            AudioFeedback.Play(WinCueFor(result, definition));
        }

        /// <summary>
        /// Which win sound a payout earns.
        ///
        /// The boundaries come from the game's own celebration thresholds rather than
        /// from numbers written here, so a game that celebrates differently gets
        /// different sounds without this file knowing about it.
        /// </summary>
        public static AudioCue WinCueFor(SpinResult result, SlotMachineDefinition definition)
        {
            float multiplier = result.Multiplier(result.bet);
            long[] thresholds = definition != null ? definition.winCelebrationThresholds : null;

            float smallCeiling = 10f;
            float bigCeiling = 200f;
            if (thresholds != null)
            {
                if (thresholds.Length > 0) smallCeiling = thresholds[0];
                if (thresholds.Length > 2) bigCeiling = thresholds[2];
            }

            if (multiplier < smallCeiling) return AudioCue.WinSmall;
            if (multiplier < bigCeiling) return AudioCue.WinMedium;
            return AudioCue.WinBig;
        }
    }
}
