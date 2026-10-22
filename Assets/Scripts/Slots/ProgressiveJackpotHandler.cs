using System;
using System.Linq;
using UnityEngine;

namespace LustHouse.Slots
{
    /// <summary>
    /// Runs a progressive jackpot (plan Stage 3.1).
    ///
    /// Trigger: a tier's symbol appears <c>triggerCount</c> or more times anywhere on the
    /// grid, and the base game paid out. Two tiers can fire on one spin and both are paid —
    /// the pots are independent, and a player who lands a Mini and a Major in the same spin
    /// gets both.
    ///
    /// The award is the pot's current value, read through the context's
    /// <see cref="IProgressivePotSource"/>. When no source is wired — the simulator, the
    /// closed-form maths, and headless tests — every pot starts at its seed, so the handler
    /// stays deterministic and the RTP gate stays reproducible.
    ///
    /// Pure: the handler does not touch the wallet, the scene, or the drip. The pot arrives
    /// through the context; the drip and the persistence live in
    /// <see cref="ProgressiveJackpotService"/>, which the runner calls before and after the
    /// base game.
    /// </summary>
    public sealed class ProgressiveJackpotHandler : ISlotFeatureHandler
    {
        public SlotFeature Definition { get; set; }

        private ProgressiveJackpotFeature Config => Definition as ProgressiveJackpotFeature;

        /// <summary>How many of a symbol id are on the base grid.</summary>
        public static int CountOnGrid(SpinResult baseResult, int symbolId)
        {
            var grid = baseResult.grid;
            if (grid == null) return 0;

            int total = 0;
            for (int r = 0; r < grid.Length; r++)
            {
                var reel = grid[r];
                if (reel == null) continue;
                for (int i = 0; i < reel.Length; i++)
                    if (reel[i] == symbolId) total++;
            }
            return total;
        }

        public bool CanTrigger(SlotMachineDefinition game, SpinResult baseResult)
        {
            var c = Config;
            if (c == null || c.tiers == null) return false;
            if (baseResult.totalWin <= 0) return false;   // the base game must have won

            for (int i = 0; i < c.tiers.Count; i++)
            {
                var tier = c.tiers[i];
                if (tier == null || tier.symbolId < 0) continue;
                if (CountOnGrid(baseResult, tier.symbolId) >= tier.triggerCount) return true;
            }
            return false;
        }

        public FeatureResult Run(FeatureContext ctx)
        {
            var c = Config;
            var result = new FeatureResult();
            if (c == null || ctx == null || ctx.game == null) return result;
            if (c.tiers == null) return result;

            result.triggeredBonus = true;

            string gameId = ctx.game.id;
            var potSource = c.potSource;
            long awarded = 0;

            for (int i = 0; i < c.tiers.Count; i++)
            {
                var tier = c.tiers[i];
                if (tier == null || tier.symbolId < 0) continue;
                if (CountOnGrid(ctx.baseResult, tier.symbolId) < tier.triggerCount) continue;

                long pot = potSource != null
                    ? potSource.PotValue(gameId, tier.tierName)
                    : tier.seedValue;

                // The pot is awarded in currency units at the triggering bet. The service
                // resets it to the seed afterwards; the handler never touches it.
                long award = pot;
                awarded += award;

                ctx.baseResult.progressiveWin += award;
                if (string.IsNullOrEmpty(ctx.baseResult.progressiveTier))
                    ctx.baseResult.progressiveTier = tier.tierName;

                result.steps.Add(new FeatureStep
                {
                    kind = FeatureStepKind.Award,
                    amount = (long)Mathf.Round((double)award / Math.Max(1, ctx.bet)),
                    symbolId = tier.symbolId
                });
                result.steps.Add(new FeatureStep
                {
                    kind = FeatureStepKind.Meter,
                    amount = (long)Mathf.Round((double)award / Math.Max(1, ctx.bet)),
                    symbolId = tier.symbolId
                });
            }

            result.totalWin += awarded;
            return result;
        }
    }
}