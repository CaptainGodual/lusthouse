using System.Collections.Generic;
using UnityEngine;

namespace LustHouse.Slots
{
    /// <summary>
    /// Runs a pick-a-prize bonus (plan §3.2 template E): enough trigger symbols land, the player
    /// is shown a row of hidden options and picks some of them, and each pick reveals a prize in
    /// bet-multiples.
    ///
    /// Two properties make this round exactly predictable, which is what lets the ship gate stay
    /// closed-form rather than sampled:
    ///
    ///   * Every option is an independent draw from the weighted prize list, so the *expected*
    ///     value of the whole round is <c>picks x weightedMean(prize)</c> no matter which options
    ///     the player takes. The player picks blind — the prize is behind the option until it is
    ///     revealed — so there is no strategy that beats that mean, and no strategy that loses to
    ///     it either. A pick-a-prize whose outcome depended on player skill would not be
    ///     reproducible in the simulator, and would make the RTP gate a moving target.
    ///   * The trigger is a count of one symbol id anywhere on the grid, so its probability comes
    ///     from per-reel window histograms rather than from a payline, and it is independent of
    ///     what the round then pays.
    ///
    /// Pure and seeded: the same seed yields the same options, the same reveals and the same
    /// total in the game, the simulator and tests alike.
    /// </summary>
    public sealed class BonusGameHandler : ISlotFeatureHandler
    {
        public SlotFeature Definition { get; set; }

        private BonusGameFeature Config => Definition as BonusGameFeature;

        /// <summary>Trigger symbols anywhere on the base grid.</summary>
        public static int CountTriggers(SpinResult baseResult, int triggerSymbolId)
        {
            var grid = baseResult.grid;
            if (grid == null) return 0;

            int total = 0;
            for (int r = 0; r < grid.Length; r++)
            {
                var reel = grid[r];
                if (reel == null) continue;
                for (int i = 0; i < reel.Length; i++)
                    if (reel[i] == triggerSymbolId) total++;
            }
            return total;
        }

        public bool CanTrigger(SlotMachineDefinition game, SpinResult baseResult)
        {
            var c = Config;
            if (c == null || c.triggerCount <= 0) return false;

            // -1 means "any scatter": fall back to the engine's own count so the round can be
            // wired to the existing scatter without minting a symbol id for it.
            int triggers = c.triggerSymbolId >= 0
                ? CountTriggers(baseResult, c.triggerSymbolId)
                : baseResult.scatterCount;

            return triggers >= c.triggerCount;
        }

        public FeatureResult Run(FeatureContext ctx)
        {
            var c = Config;
            var result = new FeatureResult();
            if (c == null || ctx == null || ctx.game == null) return result;

            int choices = Mathf.Max(1, c.choices);
            int picks = Mathf.Clamp(c.picks, 1, choices);

            result.triggeredBonus = true;
            result.bonusTriggers = c.triggerSymbolId >= 0
                ? CountTriggers(ctx.baseResult, c.triggerSymbolId)
                : ctx.baseResult.scatterCount;

            // One prize behind every option, each an independent draw from the weighted list.
            // Drawing them all up front — rather than one per pick — is what makes the round a
            // real pick: the player is choosing among existing prizes, not generating them.
            var prizes = new long[choices];
            var drawRng = ctx.rng;
            for (int i = 0; i < choices; i++)
                prizes[i] = DrawPrize(c, ref drawRng);
            ctx.rng = drawRng;   // keep the stream: SeededRng is a value type.

            long totalMultiples = 0;
            var taken = new HashSet<int>();

            for (int p = 0; p < picks; p++)
            {
                int index = ChooseIndex(c, ctx, choices, taken, p);
                taken.Add(index);

                result.steps.Add(new FeatureStep
                {
                    kind = FeatureStepKind.Pick,
                    choiceIndex = index
                });
                result.steps.Add(new FeatureStep
                {
                    kind = FeatureStepKind.Reveal,
                    choiceIndex = index,
                    amount = prizes[index]
                });

                totalMultiples += prizes[index];
            }

            result.totalWin += totalMultiples * ctx.bet;

            result.steps.Add(new FeatureStep
            {
                kind = FeatureStepKind.Award,
                amount = totalMultiples
            });

            return result;
        }

        /// <summary>
        /// Which option this pick takes. An interactive round asks the player; a simulated one
        /// resolves from the same seeded stream, so the maths never depends on a human.
        ///
        /// A player who repeats a choice, or whose UI hands back something out of range, does not
        /// stall the round or double-count a prize: the pick falls back to a random untaken
        /// option, which keeps the expected value at <c>picks x weightedMean</c> regardless.
        /// </summary>
        private int ChooseIndex(BonusGameFeature c, FeatureContext ctx, int choices,
                                HashSet<int> taken, int pickIndex)
        {
            if (c.interactive && ctx.choice != null)
            {
                int asked = ctx.choice.Choose(choices, ctx.rng);
                if (asked >= 0 && asked < choices && !taken.Contains(asked)) return asked;
            }

            // Untaken options, so the fallback is uniform over what is actually left rather than
            // over all options with duplicates silently collapsed.
            var free = new List<int>(choices);
            for (int i = 0; i < choices; i++)
                if (!taken.Contains(i)) free.Add(i);

            if (free.Count == 0) return 0;

            var rng = ctx.rng;
            int pick = free[rng.NextInt(free.Count)];
            ctx.rng = rng;   // keep the stream: SeededRng is a value type.
            return pick;
        }

        /// <summary>One prize in bet-multiples, drawn by weight. Zero when the table is empty.</summary>
        private long DrawPrize(BonusGameFeature c, ref SeededRng rng)
        {
            var prizes = c.prizes;
            if (prizes == null || prizes.Count == 0) return 0;

            float totalWeight = 0f;
            for (int i = 0; i < prizes.Count; i++)
                if (prizes[i] != null && prizes[i].weight > 0f)
                    totalWeight += prizes[i].weight;

            if (totalWeight <= 0f) return 0;

            float roll = (float)(rng.NextDouble() * totalWeight);
            for (int i = 0; i < prizes.Count; i++)
            {
                var prize = prizes[i];
                if (prize == null || prize.weight <= 0f) continue;

                roll -= prize.weight;
                if (roll < 0f) return prize.payout;
            }

            // Float rounding can leave the roll just shy of zero; the last weighted entry is the
            // nearest thing to a sure thing, so it absorbs the remainder.
            for (int i = prizes.Count - 1; i >= 0; i--)
                if (prizes[i] != null && prizes[i].weight > 0f) return prizes[i].payout;

            return 0;
        }
    }
}
