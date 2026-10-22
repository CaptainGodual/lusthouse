using System;
using System.Globalization;

namespace LustHouse.Core
{
    /// <summary>
    /// The self-exclusion rules, as pure functions (guardrail 4).
    ///
    /// They live here rather than inside the service because they are the part that
    /// has to be *right*: an exclusion that can be shortened, cleared by a mis-set
    /// field, or interpreted differently in two places is not an exclusion. Keeping
    /// them as functions over strings means they can be tested without a player, a
    /// wallet or an editor.
    /// </summary>
    public static class SelfExclusion
    {
        /// <summary>
        /// The sentinel for a permanent exclusion: the maximum UTC value.
        ///
        /// One field, one direction. "Excluded until" needs no second field for
        /// "forever", and a permanent exclusion cannot expire by accident.
        /// </summary>
        public const string PermanentUtc = "9999-12-31T23:59:59Z";

        private static readonly DateTime PermanentEnd = ParsePermanent();

        private static DateTime ParsePermanent()
        {
            DateTime parsed;
            DateTime.TryParse(PermanentUtc, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed);
            return parsed;
        }

        /// <summary>Parses an exclusion stamp into the moment it lifts.</summary>
        public static bool TryGetEnd(string stamp, out DateTime untilUtc)
        {
            untilUtc = default;
            if (string.IsNullOrWhiteSpace(stamp)) return false;

            return DateTime.TryParse(stamp, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out untilUtc);
        }

        /// <summary>
        /// Is the exclusion in force at this moment?
        ///
        /// An expired exclusion is not an exclusion: a one-day exclusion lifts itself,
        /// and a player is expected to be able to come back.
        /// </summary>
        public static bool IsActive(string stamp, DateTime nowUtc) =>
            TryGetEnd(stamp, out var until) && nowUtc < until;

        /// <summary>Does this exclusion never lift?</summary>
        public static bool IsPermanent(string stamp) =>
            TryGetEnd(stamp, out var until) && until >= PermanentEnd;

        /// <summary>
        /// May a new stamp replace the current one?
        ///
        /// Only ever forwards. An empty stamp would clear the exclusion and is refused;
        /// an earlier one would shorten it and is refused. The only permitted change is
        /// to a later moment.
        /// </summary>
        public static bool CanApply(string current, string next)
        {
            if (string.IsNullOrEmpty(next)) return false;
            if (string.IsNullOrEmpty(current)) return true;

            // Ordinal comparison of ISO-8601 UTC stamps is a comparison of time.
            if (string.CompareOrdinal(current, next) >= 0) return false;

            // A stamp that does not parse must not be able to displace a real one.
            return TryGetEnd(current, out _) && TryGetEnd(next, out _);
        }
    }

    /// <summary>
    /// When the reality check is due.
    ///
    /// The thresholds are 15, 30 and 60 minutes of play. Each one comes due once, and
    /// the schedule does not care whether the player was looking: a check that could be
    /// skipped by not opening the game is not a check.
    /// </summary>
    public static class RealityCheckSchedule
    {
        /// <summary>The next threshold that has not been shown yet, or 0 when there is none.</summary>
        public static int NextDue(int[] thresholds, int lastShownMinute)
        {
            if (thresholds == null) return 0;

            int best = 0;
            foreach (int threshold in thresholds)
            {
                if (threshold <= lastShownMinute) continue;
                if (best == 0 || threshold < best) best = threshold;
            }
            return best;
        }

        /// <summary>
        /// The threshold that has come due, if any.
        ///
        /// The lowest unshown threshold that the elapsed time has reached, so a player
        /// who has been away for an hour gets the 15-minute check first rather than
        /// being handed the 60-minute one and never seeing the others.
        /// </summary>
        public static bool TryGetDue(int[] thresholds, int lastShownMinute, int elapsedMinutes,
                                     out int dueMinute)
        {
            dueMinute = 0;
            if (thresholds == null) return false;

            foreach (int threshold in thresholds)
            {
                if (threshold <= lastShownMinute) continue;
                if (threshold > elapsedMinutes) continue;

                dueMinute = threshold;
                return true;
            }
            return false;
        }
    }
}
