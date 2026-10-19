using System.Collections.Generic;

namespace LustHouse.Slots
{
    /// <summary>
    /// <see cref="PayType.Ways"/> — wins pay for any 3+ matching symbols anywhere on the
    /// grid, with no fixed paylines. This is what "243 ways to win" style games (usually
    /// six reels) actually mean.
    ///
    /// A win is a choice of one position per reel for k >= 3 reels, all resolving to the
    /// same symbol. Wilds may stand in for any paying symbol, so on reel r the number of
    /// positions that can serve symbol s is (occurrences of s) + (occurrences of wild).
    /// Choosing k reels therefore yields the product of those counts, and summing over
    /// every k-subset is an elementary symmetric polynomial.
    ///
    /// Closed form, unlike cluster pays — which is why the RTP gate can verify this
    /// exactly and cannot do the same for <see cref="ClusterEvaluator"/>.
    /// </summary>
    public class WaysEvaluator : IReelEvaluator
    {
        public SpinResult Evaluate(SlotMachineDefinition def, int[] stops, long bet)
        {
            var result = new SpinResult
            {
                stops = stops,
                bet = bet,
                grid = ReelEngine.BuildGrid(def, stops),
                lineWins = new List<LineWin>(),
                totalWin = 0
            };

            var grid = result.grid;
            if (grid == null || grid.Length == 0) return result;

            long totalMultiples = 0;

            // Wilds are tracked separately: they both substitute and pay on their own.
            int wildId = FindWildId(def);
            var wildPerReel = new int[grid.Length];
            if (wildId >= 0)
                for (int r = 0; r < grid.Length; r++)
                    wildPerReel[r] = CountOnReel(def, grid[r], wildId);

            for (int i = 0; i < def.symbols.Count; i++)
            {
                var symbol = def.symbols[i];
                if (symbol == null || symbol.isScatter) continue;

                if (symbol.isWild)
                {
                    // All-wild win: every chosen reel must land a wild.
                    totalMultiples += WinForCounts(def, symbol, wildPerReel, out long ways);
                    result.waysWon += ways;
                    continue;
                }

                var counts = new int[grid.Length];
                for (int r = 0; r < grid.Length; r++)
                    counts[r] = CountOnReel(def, grid[r], symbol.id) + wildPerReel[r];

                totalMultiples += WinForCounts(def, symbol, counts, out long w);
                result.waysWon += w;
            }

            result.scatterCount = ReelEngine.CountScatters(def, grid);
            if (def.hasFreeSpins && result.scatterCount >= def.freeSpinScatterCount)
            {
                result.triggeredFreeSpins = true;
                result.freeSpinsAwarded = def.freeSpinAward;
                totalMultiples += (long)def.freeSpinAward * def.freeSpinBetMultiplier;
            }

            result.totalWin = totalMultiples * bet;
            return result;
        }

        /// <summary>Total payout in bet-multiples for one symbol, summed over match lengths.</summary>
        public static long WinForCounts(SlotMachineDefinition def, SymbolDefinition symbol,
                                        int[] counts, out long waysWon)
        {
            waysWon = 0;
            long total = 0;

            // e[k] = sum over all k-subsets of reels of the product of their counts.
            var e = ElementarySymmetric(counts);

            for (int k = 3; k < e.Length && k < counts.Length + 1; k++)
            {
                long payout = symbol.PayoutForMatch(k);
                if (payout <= 0 || e[k] <= 0) continue;
                total += e[k] * payout;
                waysWon += e[k];
            }

            return total;
        }

        /// <summary>
        /// e[k] = sum over k-subsets of the product of the chosen entries, for k = 0..n.
        /// Standard in-place DP; O(n^2) and exact for the reel counts we support.
        /// </summary>
        public static long[] ElementarySymmetric(int[] values)
        {
            int n = values?.Length ?? 0;
            var e = new long[n + 1];
            if (n == 0) return e;

            e[0] = 1;
            for (int i = 0; i < n; i++)
            {
                int v = values[i];
                for (int k = i + 1; k >= 1; k--)
                    e[k] += e[k - 1] * v;
            }

            return e;
        }

        /// <summary>
        /// Expected occurrence counts per reel for <paramref name="symbolId"/>, given per-reel
        /// probabilities. Used by the exact RTP path, which works in expectations rather
        /// than on a concrete grid.
        /// </summary>
        public static double[] ExpectedCounts(SlotMachineDefinition def, int symbolId,
                                              System.Collections.Generic.IReadOnlyList<System.Collections.Generic.KeyValuePair<int, double>>[] reelProbs)
        {
            var counts = new double[reelProbs.Length];
            for (int r = 0; r < reelProbs.Length; r++)
            {
                double p = 0.0;
                foreach (var kv in reelProbs[r])
                    if (kv.Key == symbolId) { p = kv.Value; break; }
                counts[r] = p * def.rowCount;
            }
            return counts;
        }

        private static int FindWildId(SlotMachineDefinition def)
        {
            for (int i = 0; i < def.symbols.Count; i++)
                if (def.symbols[i] != null && def.symbols[i].isWild)
                    return def.symbols[i].id;
            return -1;
        }

        private static int CountOnReel(SlotMachineDefinition def, int[] reel, int symbolId)
        {
            int n = 0;
            for (int row = 0; row < reel.Length; row++)
                if (reel[row] == symbolId) n++;
            return n;
        }
    }
}