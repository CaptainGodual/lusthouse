using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace LustHouse.Slots
{
    /// <summary>
    /// One row of a published paytable: a symbol, how many of it, and what that pays.
    ///
    /// The payout is in bet-multiples, because that is how a paytable is read: "5x the
    /// bet", not "5,000 Coins", which would be a different number at every stake.
    /// </summary>
    public struct DisclosurePayRow
    {
        public string symbolName;
        public int matches;
        public long payout;      // bet-multiples
        public bool isWild;
        public bool isScatter;

        /// <summary>The row as it is printed: "3 Aces  5x".</summary>
        public string Label
        {
            get
            {
                string name = string.IsNullOrEmpty(symbolName) ? "Symbol" : symbolName;
                string tag = isWild ? " (wild)" : (isScatter ? " (scatter)" : "");
                return $"{matches} {name}{tag}   {payout}x";
            }
        }
    }

    /// <summary>
    /// What the game actually pays, measured rather than declared (plan §1.1).
    ///
    /// The point of this class is that it cannot drift from the maths. Every number a
    /// player is shown comes out of <see cref="SimulationEngine"/> — the same engine the
    /// ship gate uses — so a retuned paytable changes this sheet on the next read
    /// without anyone editing a string.
    ///
    /// The game may *declare* things that are not maths claims: its volatility band, its
    /// maximum win, its bet range. Those are design facts about the game, and they are
    /// copied straight from the definition. The return-to-player figure and the hit rate
    /// are claims about outcomes, so they are measured.
    /// </summary>
    public class DisclosureReport
    {
        public string gameId = "";
        public string title = "";
        public string tagline = "";

        /// <summary>The exact, enumerated RTP. NaN when the pay type has no closed form.</summary>
        public double exactRtp = double.NaN;

        /// <summary>True when <see cref="exactRtp"/> is a real number.</summary>
        public bool hasExactRtp => !double.IsNaN(exactRtp) && !double.IsInfinity(exactRtp);

        /// <summary>The sampled RTP, from a fixed-seed run. Always present.</summary>
        public double sampledRtp;

        /// <summary>The share of spins that paid anything.</summary>
        public double hitRate;

        /// <summary>How many spins the sampled figures came from.</summary>
        public long sampleSpins;

        /// <summary>The seed the sampled figures came from, so the run is reproducible.</summary>
        public int sampleSeed;

        public Volatility volatility = Volatility.Medium;
        public int maxWinMultiplier;
        public long minBet;
        public long maxBet;
        public PayType payType = PayType.Lines;

        /// <summary>True when the maths has been signed off (plan §3.7).</summary>
        public bool measured;

        public List<DisclosurePayRow> paytable = new List<DisclosurePayRow>();

        /// <summary>Symbols that behave specially, described in words for the sheet.</summary>
        public List<string> specialNotes = new List<string>();

        /// <summary>
        /// The return-to-player figure as it is printed.
        ///
        /// The exact figure when there is one, because a sampled figure presented as
        /// exact is the same class of lie as a jackpot ribbon with no pot behind it. When
        /// there is no closed form, the sampled figure is printed and labelled as
        /// sampled.
        /// </summary>
        public string RtpText => hasExactRtp
            ? Percent(exactRtp) + " (exact)"
            : Percent(sampledRtp) + " (sampled)";

        /// <summary>The hit rate as it is printed, with the sample it came from.</summary>
        public string HitRateText =>
            $"{Percent(hitRate)} of spins paid something ({sampleSpins:N0} spins, seed {sampleSeed})";

        public string VolatilityText => volatility.ToString().ToUpperInvariant();

        public string BetRangeText =>
            $"{minBet:N0} – {maxBet:N0} Coins per spin";

        public string MaxWinText => $"{maxWinMultiplier:N0}x the bet";

        /// <summary>
        /// Measures a game.
        ///
        /// <paramref name="sampleSpins"/> is the size of the fixed-seed run behind the
        /// hit rate. Zero skips the sampling entirely and leaves the sampled figures at
        /// zero — the exact RTP is still there, and the caller can say so.
        /// </summary>
        public static DisclosureReport Build(SlotMachineDefinition def, int seed, long sampleSpins)
        {
            var report = new DisclosureReport
            {
                gameId = def != null ? def.id : "",
                title = def != null ? def.title : "",
                tagline = def != null ? def.tagline : "",
                volatility = def != null ? def.volatility : Volatility.Medium,
                maxWinMultiplier = def != null ? def.maxWinMultiplier : 0,
                minBet = def != null ? def.minBet : 0,
                maxBet = def != null ? def.maxBet : 0,
                payType = def != null ? def.payType : PayType.Lines,
                measured = def != null && def.rtpStatus == RtpStatus.Measured,
                sampleSeed = seed,
                sampleSpins = sampleSpins
            };

            if (def == null) return report;

            report.exactRtp = SimulationEngine.ExactRtp(def);

            if (sampleSpins > 0)
            {
                var sampled = SimulationEngine.Run(def, sampleSpins, Math.Max(1L, def.minBet), seed);
                report.sampledRtp = sampled.rtp;
                report.hitRate = sampled.hitRate;
            }

            BuildPaytable(def, report);
            BuildSpecialNotes(def, report);

            return report;
        }

        /// <summary>
        /// The paytable as published: every symbol, every match count it pays for.
        ///
        /// A symbol that pays nothing at a match count is left out — a paytable full of
        /// zeroes is noise, and a zero row reads as a broken entry rather than as "this
        /// combination does not pay".
        /// </summary>
        private static void BuildPaytable(SlotMachineDefinition def, DisclosureReport report)
        {
            if (def.symbols == null) return;

            int maxMatches = Mathf.Max(3, def.reelCount);

            for (int i = 0; i < def.symbols.Count; i++)
            {
                var symbol = def.symbols[i];
                if (symbol == null) continue;

                if (def.payType == PayType.Cluster && symbol.clusterPayouts != null &&
                    symbol.clusterPayouts.Length > 0)
                {
                    int minSize = Mathf.Max(3, def.cascadeClusterSize);
                    for (int size = minSize; size < minSize + symbol.clusterPayouts.Length; size++)
                    {
                        long payout = symbol.PayoutForCluster(size, minSize);
                        if (payout <= 0) continue;

                        report.paytable.Add(new DisclosurePayRow
                        {
                            symbolName = symbol.symbolName,
                            matches = size,
                            payout = payout,
                            isWild = symbol.isWild,
                            isScatter = symbol.isScatter
                        });
                    }
                    continue;
                }

                for (int matches = 3; matches <= maxMatches; matches++)
                {
                    long payout = symbol.PayoutForMatch(matches);
                    if (payout <= 0) continue;

                    report.paytable.Add(new DisclosurePayRow
                    {
                        symbolName = symbol.symbolName,
                        matches = matches,
                        payout = payout,
                        isWild = symbol.isWild,
                        isScatter = symbol.isScatter
                    });
                }
            }
        }

        /// <summary>
        /// The special-symbol rules and the feature rounds, in words.
        ///
        /// A player reading a paytable needs to know that the wild substitutes, that
        /// the scatter pays anywhere, and what the scatter actually awards — none of
        /// which is visible in a table of numbers, and all of which change what a
        /// symbol is worth.
        /// </summary>
        private static void BuildSpecialNotes(SlotMachineDefinition def, DisclosureReport report)
        {
            if (def.symbols != null)
            {
                for (int i = 0; i < def.symbols.Count; i++)
                {
                    var symbol = def.symbols[i];
                    if (symbol == null) continue;

                    if (symbol.isWild)
                        report.specialNotes.Add(
                            $"{Name(symbol)} substitutes for any symbol except the scatter.");
                    else if (symbol.isScatter)
                        report.specialNotes.Add(
                            $"{Name(symbol)} counts anywhere on the reels. It does not pay on a line.");
                    else if (symbol.megaBlockMultiplier > 0f)
                        report.specialNotes.Add(
                            $"{Name(symbol)} forms a 2x2 block that pays {symbol.megaBlockMultiplier:0.#}x a four-of-a-kind.");
                }
            }

            // The feature rounds are part of the maths the player is buying into: the
            // free spins are paid for out of the same budget as the lines.
            var freeSpins = def.Find<FreeSpinsFeature>();
            if (freeSpins != null)
            {
                report.specialNotes.Add(
                    $"{freeSpins.scatterCount} or more scatters anywhere award " +
                    $"{freeSpins.award} free spins" +
                    (freeSpins.retrigger ? ", and a scatter during free spins adds more." : "."));
            }

            var bonus = def.Find<BonusGameFeature>();
            if (bonus != null)
            {
                report.specialNotes.Add(
                    $"{bonus.triggerCount} matching symbols anywhere award a " +
                    $"{bonus.picks}-pick bonus round.");
            }

            var hold = def.Find<HoldAndSpinFeature>();
            if (hold != null)
            {
                string holder = hold.specialSymbolId >= 0 ? "The special symbol" : "Any special symbol";
                report.specialNotes.Add(
                    $"{holder} locks on the reels and can be respun up to {hold.maxRespins} times.");
            }
        }

        private static string Name(SymbolDefinition symbol)
        {
            if (symbol == null) return "A symbol";
            return string.IsNullOrEmpty(symbol.symbolName) ? "A symbol" : symbol.symbolName;
        }

        /// <summary>
        /// A percentage, formatted for a store listing.
        ///
        /// Invariant culture on purpose: a German or French locale would print
        /// "96,28%" on the same sheet that reads "96.28%" in QA, and a disclosure that
        /// changes shape with the device's language is a disclosure that gets misread.
        /// </summary>
        public static string Percent(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "—";
            return (value * 100.0).ToString("0.00", CultureInfo.InvariantCulture) + "%";
        }
    }
}
