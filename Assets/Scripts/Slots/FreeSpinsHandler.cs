using UnityEngine;

namespace LustHouse.Slots
{
    /// <summary>
    /// Runs the free-spin round: the awarded spins plus any retriggers, at the free-spin bet
    /// with no debit. Pure and seeded, so the game, the simulator and tests agree.
    /// </summary>
    public sealed class FreeSpinsHandler : ISlotFeatureHandler
    {
        public SlotFeature Definition { get; set; }

        private FreeSpinsFeature Config => Definition as FreeSpinsFeature;

        public bool CanTrigger(SlotMachineDefinition game, SpinResult baseResult)
        {
            var c = Config;
            return c != null && baseResult.scatterCount >= c.scatterCount;
        }

        public FeatureResult Run(FeatureContext ctx)
        {
            var c = Config;
            var result = new FeatureResult();
            if (c == null || ctx == null || ctx.game == null) return result;

            result.triggeredFreeSpins = true;
            result.freeSpinsAwarded = c.award;

            long freeBet = ctx.bet * Mathf.Max(1, c.betMultiplier);
            int spinsRemaining = c.award;
            int multiplier = 1;
            int guard = 0;

            // A retrigger adds spins back onto the countdown, so one loop covers the award and
            // every retrigger. The guard stops a degenerate strip from running forever.
            while (spinsRemaining > 0 && guard++ < 1024)
            {
                // Base only: a free spin that lands scatters adds to the retrigger count
                // below. Running the feature pipeline here would recurse into another
                // free-spin round and pay the same trigger twice.
                var spin = ReelEngine.SpinBaseStatic(ctx.game, ref ctx.rng, freeBet);

                if (multiplier > 1)
                    spin.totalWin *= multiplier;

                result.totalWin += spin.totalWin;
                result.spinsRun++;
                result.steps.Add(new FeatureStep { kind = FeatureStepKind.Spin, spin = spin });
                spinsRemaining--;

                if (c.retrigger && spin.scatterCount >= c.scatterCount)
                    spinsRemaining += c.retriggerAward;

                if (c.multiplierGrows && multiplier < c.maxMultiplier)
                    multiplier += Mathf.Max(1, c.multiplierStep);
            }

            return result;
        }
    }
}
