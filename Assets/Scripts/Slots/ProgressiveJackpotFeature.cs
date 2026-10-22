using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LustHouse.Slots
{
    /// <summary>
    /// A three-tier progressive jackpot (plan Stage 3.1).
    ///
    /// The tiers are ordinary symbols on the reels — the link between a tier and its symbol
    /// lives here, on the feature data, not on the symbol definition. That keeps the paytable
    /// honest: a Mini symbol pays like any other symbol when it lands in a line, and it also
    /// triggers the Mini pot when it lands anywhere on a winning spin.
    ///
    /// The pot values are seeded in the content and grown by a drip on every spin. They are
    /// persisted in the schema-2 <c>progressive</c> block, so a player who closes the app mid-
    /// drip does not watch the pots reset to the seed.
    ///
    /// Progressive awards are excluded from the declared RTP. The ship gate measures the base
    /// game; the jackpot is an affine term on top, paid out of the drip, not out of the
    /// paytable budget.
    /// </summary>
    [Serializable]
    public sealed class ProgressiveJackpotFeature : SlotFeature
    {
        /// <summary>One tier of the jackpot.</summary>
        [Serializable]
        public sealed class Tier
        {
            [Tooltip("Symbol id that triggers this tier. Must match a symbol on the reels.")]
            public int symbolId;

            [Tooltip("Display name: Mini, Minor, Major, Grand... whatever the theme wants.")]
            public string tierName = "";

            [Tooltip("The pot starts here. In currency units at the game's minimum bet.")]
            public long seedValue = 0;

            [Tooltip("How much of every bet drips into this pot, in millionths. 1000 = 0.1%.",
                    )]
            public int dripPerMillion = 1000;

            [Tooltip("How many of the trigger symbol must be on the grid to fire.")]
            public int triggerCount = 1;
        }

        public List<Tier> tiers = new List<Tier>();

        /// <summary>The pot source the handler reads. Null falls back to each tier's seed.</summary>
        [NonSerialized] public IProgressivePotSource potSource;

        /// <summary>Look up a tier by symbol id, or null.</summary>
        public Tier TierForSymbol(int symbolId) =>
            tiers.FirstOrDefault(t => t.symbolId == symbolId);
    }

    /// <summary>
    /// Supplies a progressive pot at run time. The service implements this on the bootstrap
    /// root; the simulator and tests pass null, which makes every handler deterministic and
    /// every pot start at its seed.
    ///
    /// Splitting it into an interface is what keeps the handler pure: it still cannot touch
    /// the wallet or the scene, so the game, the simulator and the tests share one code path.
    /// </summary>
    public interface IProgressivePotSource
    {
        long PotValue(string gameId, string tierName);
        void ResetPot(string gameId, string tierName, long seedValue);
    }
}