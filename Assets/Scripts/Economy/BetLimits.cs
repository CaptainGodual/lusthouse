using UnityEngine;

namespace LustHouse.Economy
{
    /// <summary>
    /// The house's bet ladder and the bet ceiling each player level unlocks.
    ///
    /// Two rules, and they are the same in every slot:
    ///
    /// 1. **One ladder.** Every game offers the same denominations, so a player who
    ///    learns that 10,000 is the comfortable stake in one slot knows it in the next.
    ///    The steps are 1-2-5 (with 75,000 and 2,400,000 on top) because those are the
    ///    numbers a player reads as round.
    ///
    /// 2. **The level raises the ceiling, not the floor.** A new player can bet 1,000
    ///    and work up to 75,000; the top of the ladder opens as the level rises, and
    ///    the last step (2,400,000) is the house maximum. A player is never locked out
    ///    of a bet they can afford — they are locked out of the *big* ones, which is
    ///    what makes the next level worth reaching.
    ///
    /// The floor never moves. Betting 1,000 at level 1 and 1,000 at level 10 are the
    /// same risk, so the ladder always means the same thing.
    /// </summary>
    public static class BetLimits
    {
        /// <summary>
        /// The house's shared denominations, ascending.
        ///
        /// Every step is a multiple of 100, because §4.3 rule 5 says round numbers only
        /// and a bet of 137 coins is not a bet, it is a rounding error. The small end
        /// (100 / 500) exists for the maths fixtures and for any future low-stakes mode;
        /// the shipped games start at 1,000.
        /// </summary>
        public static readonly long[] Ladder =
        {
            100, 500,
            1_000, 2_500, 5_000, 10_000, 25_000, 50_000, 75_000, 100_000, 250_000,
            500_000, 750_000, 1_000_000, 1_500_000, 2_000_000, 2_400_000
        };

        /// <summary>
        /// The largest bet each level unlocks. Index 0 is level 1.
        ///
        /// The ramp is roughly a doubling every level, so the top of the ladder opens
        /// over nine levels and a player at level 1 has seven denominations to choose
        /// from rather than twenty.
        /// </summary>
        public static readonly long[] LevelCaps =
        {
            75_000,        // level 1
            100_000,       // level 2
            250_000,       // level 3
            500_000,       // level 4
            750_000,       // level 5
            1_000_000,     // level 6
            1_500_000,     // level 7
            2_000_000,     // level 8
            2_400_000,     // level 9
            2_400_000      // level 10 -- the house maximum
        };

        /// <summary>The largest bet any player can ever place.</summary>
        public const long HouseMaxBet = 2_400_000;

        /// <summary>The bet ceiling for a level. Levels past the table stay at the maximum.</summary>
        public static long MaxBetForLevel(int level)
        {
            if (LevelCaps.Length == 0) return HouseMaxBet;
            int index = Mathf.Clamp(level - 1, 0, LevelCaps.Length - 1);
            return LevelCaps[index];
        }

        /// <summary>The player's current level, or 1 before progression exists.</summary>
        public static int PlayerLevel =>
            PlayerProgression.Instance != null ? PlayerProgression.Instance.Level : 1;

        /// <summary>
        /// The next level that opens a bigger bet, or -1 when the ladder is maxed out.
        ///
        /// The slot screen shows it, because a ceiling the player cannot see is a ceiling
        /// they will never reach: the whole point of the ladder is that the next rung is
        /// worth the next level.
        /// </summary>
        public static int NextCapLevel(int level)
        {
            long current = MaxBetForLevel(level);
            for (int next = level + 1; next <= LevelCaps.Length; next++)
            {
                if (MaxBetForLevel(next) > current) return next;
            }
            return -1;
        }
    }
}
