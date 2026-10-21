using LustHouse.Economy;
using UnityEngine;

namespace LustHouse.Core
{
    /// <summary>
    /// The age gate (plan §2.3a): the store requirement that this game is 18+.
    ///
    /// The rule is one sentence long — the player confirms they are 18 or over, the
    /// answer is written to the save, and nothing in the game is reachable until they
    /// have. A rule that short is easy to get subtly wrong, and a rule that is wrong
    /// here is wrong in a store review, so the whole policy lives in one place with no
    /// state of its own.
    ///
    /// **No save-schema change was needed.** <c>PlayerState.ageConfirmed</c> and
    /// <c>ageConfirmedUtc</c> have been in the save shape since the beginning and
    /// <see cref="PlayerProgression.ConfirmAge"/> already existed. Only the blocking
    /// flow was missing, which is why this can land without touching the schema the
    /// jackpot work is about to move.
    /// </summary>
    public static class AgeGate
    {
        /// <summary>The age the gate asks for.</summary>
        public const int MinimumAge = 18;

        /// <summary>
        /// Has this player confirmed they are old enough?
        ///
        /// False when there is no player loaded. An unconfirmed player and a player who
        /// has not been read from the save look the same from here, and both of them
        /// mean the same thing to the caller: ask.
        /// </summary>
        public static bool IsConfirmed =>
            PlayerProgression.Instance != null && PlayerProgression.Instance.AgeConfirmed;

        /// <summary>
        /// Records the confirmation and saves it.
        ///
        /// Returns false only when there is no player to record the answer against. A
        /// gate that cannot keep its answer is a gate that asks the same question on
        /// every launch, so the caller treats a false as "keep asking" rather than as
        /// "carry on".
        /// </summary>
        public static bool Confirm()
        {
            var progression = PlayerProgression.Instance;
            if (progression == null)
            {
                LustLog.Error("[LustHouse] The age gate was answered with no player loaded; " +
                              "the answer could not be kept.");
                return false;
            }

            progression.ConfirmAge();

            // Saved now rather than left to the autosave timer: the answer is what
            // decides whether the player is asked again, and a launch that ends before
            // the autosave fires would ask again for no reason.
            var services = ServicesBootstrap.Instance;
            if (services != null) services.SaveNow();

            LustLog.Info("[LustHouse] The player confirmed they are 18 or over.");
            return true;
        }

        /// <summary>
        /// Closes the game.
        ///
        /// There is no way back from this, and that is the point: the player has said
        /// they are under 18, and a game that keeps serving gambling content to
        /// someone who says they are under 18 is the exact thing this gate exists to
        /// prevent.
        /// </summary>
        public static void Leave()
        {
            LustLog.Info("[LustHouse] The player is under 18. The game closes.");
            Application.Quit();
        }
    }
}
