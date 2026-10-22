using System;
using System.Collections.Generic;
using UnityEngine;

namespace LustHouse.Slots
{
    /// <summary>Aggregated results of a simulation run (plan §3.6, §5.2).</summary>
    public struct SimulationReport
    {
        public string gameId;
        public long spins;
        public long totalBet;
        public long totalWin;
        public double rtp;
        public double hitRate;            // fraction of spins with any win
        public long biggestWin;
        public double biggestWinMultiplier;
        public double meanWin;
        public double stdDevWin;         // volatility proxy
        public int freeSpinTriggers;
        public double freeSpinRate;
        public double maxWinBreaches;    // spins exceeding maxWinMultiplier
        public double secondsElapsed;

        public bool WithinTolerance(float target, float tolerance) =>
            Math.Abs(rtp - target) <= tolerance;
    }

    /// <summary>
    /// Headless RTP / volatility simulator for any <see cref="SlotMachineDefinition"/>.
    ///
    /// Uses <see cref="SeededRng"/> rather than <c>UnityEngine.Random</c>, so a run is
    /// exactly reproducible from its seed — which is the whole point of guardrail §6 and
    /// of the ±0.5% ship gate in plan §3.6.
    ///
    /// The simulation path is static and Editor-free so it can be called from a test.
    /// </summary>
    public static class SimulationEngine
    {
        /// <summary>Runs <paramref name="spins"/> spins at a fixed bet and reports.</summary>
        public static SimulationReport Run(SlotMachineDefinition def, long spins, long bet, int seed)
        {
            var report = new SimulationReport
            {
                gameId = def != null ? def.id : "(null)",
                // 0 until the loop actually runs. Echoing the requested count here made
                // a null definition report "100 spins, 0 RTP" when it simulated nothing.
                spins = 0,
                biggestWin = 0
            };

            if (def == null || spins <= 0 || bet <= 0) return report;

            var rng = SeededRng.FromSeed(seed);

            long totalBet = 0;
            long totalWin = 0;
            long hits = 0;
            long biggest = 0;
            int freeSpins = 0;
            long maxWinBreaches = 0;
            double sumOfSquares = 0.0;

            var started = DateTime.UtcNow;

            for (long i = 0; i < spins; i++)
            {
                var result = ReelEngine.SpinStatic(def, ref rng, bet);

                totalBet += bet;
                totalWin += result.totalWin;
                sumOfSquares += (double)result.totalWin * result.totalWin;

                if (result.IsWin) hits++;
                if (result.totalWin > biggest) biggest = result.totalWin;
                if (result.triggeredFreeSpins) freeSpins++;

                if (result.totalWin > bet * def.maxWinMultiplier) maxWinBreaches++;
            }

            double mean = totalWin / (double)spins;
            double variance = sumOfSquares / spins - mean * mean;

            report.totalBet = totalBet;
            report.totalWin = totalWin;
            report.spins = spins;   // actual spins simulated, not the requested count
            report.rtp = totalBet > 0 ? totalWin / (double)totalBet : 0.0;
            report.hitRate = hits / (double)spins;
            report.biggestWin = biggest;
            report.biggestWinMultiplier = bet > 0 ? biggest / (double)bet : 0.0;
            report.meanWin = mean;
            report.stdDevWin = Math.Sqrt(Math.Max(0.0, variance));
            report.freeSpinTriggers = freeSpins;
            report.freeSpinRate = freeSpins / (double)spins;
            report.maxWinBreaches = maxWinBreaches;
            report.secondsElapsed = (DateTime.UtcNow - started).TotalSeconds;

            return report;
        }

        /// <summary>
        /// Runs every bet the game can take so the ladder cannot hide a bad rung.
        ///
        /// It walks the whole ladder, not the bets one level unlocks: the maths is the
        /// same at every step, and a rung that only level 9 can reach still has to be
        /// right.
        /// </summary>
        public static SimulationReport RunAllBets(SlotMachineDefinition def, long spinsPerLevel, int seed)
        {
            if (def == null) return default;

            long accumulatedBet = 0, accumulatedWin = 0, biggest = 0, total = 0;
            double sq = 0.0;
            var started = DateTime.UtcNow;

            var bets = def.AllBetOptions();
            for (int level = 0; level < bets.Count; level++)
            {
                long bet = bets[level];
                var r = Run(def, spinsPerLevel, bet, seed + level);

                accumulatedBet += r.totalBet;
                accumulatedWin += r.totalWin;
                total += r.spins;
                if (r.biggestWin > biggest) biggest = r.biggestWin;
                sq += r.stdDevWin * r.stdDevWin * r.spins;
            }

            double mean = total > 0 ? accumulatedWin / (double)total : 0;
            double variance = total > 0 ? sq / total - mean * mean : 0;

            return new SimulationReport
            {
                gameId = def.id,
                spins = total,
                totalBet = accumulatedBet,
                totalWin = accumulatedWin,
                rtp = accumulatedBet > 0 ? accumulatedWin / (double)accumulatedBet : 0,
                hitRate = 0,
                biggestWin = biggest,
                biggestWinMultiplier = 0,
                meanWin = mean,
                stdDevWin = Math.Sqrt(Math.Max(0, variance)),
                freeSpinTriggers = 0,
                freeSpinRate = 0,
                maxWinBreaches = 0,
                secondsElapsed = (DateTime.UtcNow - started).TotalSeconds
            };
        }
    /// <summary>
        /// Exact RTP, by enumerating symbol combinations rather than sampling.
        ///
        /// Monte Carlo cannot resolve the §3.6 ±0.5% gate on a high-variance game. On
        /// Velvet Room (max win 1500x) a 2,000,000-spin run still swings between 0.956
        /// and 0.973 depending on the seed -- wider than the tolerance it is supposed to
        /// police. The expectation is linear in the outcome, so enumerating symbol types
        /// per reel gives the true RTP deterministically and instantly.
        ///
        /// The total is <see cref="LineRtp"/> plus <see cref="BonusRtp"/>. The bonus pays a
        /// fixed expected amount per trigger that does not scale with the paytable, so
        /// keeping it as a separate affine term is what stops the retune from counting it
        /// once per paytable entry instead of once per spin.
        ///
        /// This is the number the ship gate must use. Simulation stays useful for hit
        /// rate, variance and max-win behaviour, which have no closed form here.
        /// </summary>
        public static double ExactRtp(SlotMachineDefinition def)
        {
            double line = LineRtp(def);
            if (double.IsNaN(line)) return double.NaN;
            return line + BonusRtp(def);
        }

        /// <summary>
        /// Exact RTP from the base game — paylines plus 2x2 mega blocks — excluding every
        /// feature round. This is what the retune measures, so its weights stay linear in
        /// the payouts. Blocks live here and not in the affine bonus term because their
        /// pay is four-of-a-kind × a multiplier, i.e. linear in one paytable entry.
        /// </summary>
        public static double LineRtp(SlotMachineDefinition def)
        {
            if (def == null || def.reelCount <= 0) return double.NaN;
            if (def.reelStrips == null || def.reelStrips.Count != def.reelCount) return double.NaN;

            var probs = new Dictionary<int, double>[def.reelCount];
            for (int r = 0; r < def.reelCount; r++)
            {
                var strip = def.reelStrips[r]?.symbols;
                if (strip == null || strip.Count == 0) return 0.0;

                var counts = new Dictionary<int, int>();
                foreach (int id in strip)
                    counts[id] = counts.TryGetValue(id, out int c) ? c + 1 : 1;

                probs[r] = new Dictionary<int, double>();
                foreach (var kv in counts)
                    probs[r][kv.Key] = (double)kv.Value / strip.Count;
            }

            if (def.payType == PayType.Ways) return ExactRtpWays(def, probs);

            // Cluster pays refill the grid with fresh draws, so there is no closed form.
            // NaN, never a plausible zero -- callers check SupportsExactRtp first.
            if (def.payType == PayType.Cluster) return double.NaN;

            if (def.paylines == null || def.paylines.Count == 0) return double.NaN;

            // Every (reel, row) cell is uniform over that reel's multiset, so the row a
            // payline visits does not change its expectation: all lines share one value.
            int width = def.paylines[0].rows?.Length ?? def.reelCount;
            var ids = new int[width];
            return def.paylines.Count * SumLine(def, probs, 0, width, ids, 1.0) + BlockRtp(def);
        }

        /// <summary>
        /// Exact RTP contribution of 2x2 mega blocks, in bet-multiples per spin:
        ///
        ///     B = (rows−1) × Σ_r a(r)×a(r+1) × four-of-a-kind × multiplier
        ///
        /// summed over every symbol that opted in with <c>megaBlockMultiplier &gt; 0</c>.
        ///
        /// A reel's visible rows are consecutive stops, so "rows j and j+1 are both symbol
        /// s" means two cyclically adjacent stops are both s — and that chance is the same
        /// for every window row, since shifting the stop just cyclically shifts which
        /// adjacent pair is sampled. Hence a(r) = (adjacent s-pairs on reel r) / (strip
        /// length), and the two reels of a block window are independent.
        ///
        /// Like the payline term this is linear in the paytable (four-of-a-kind is one
        /// entry), so it belongs in the weight the retune measures rather than in the
        /// affine bonus constant. It also counts overlapping windows each on their own —
        /// a 2x3 slab of one symbol is two blocks — matching PaylineEvaluator.EvaluateBlocks.
        ///
        /// The evaluator rounds each block's currency product; this uses the unrounded
        /// value, so the two differ by at most half a currency unit per block per spin.
        /// </summary>
        public static double BlockRtp(SlotMachineDefinition def)
        {
            if (def?.reelStrips == null || def.reelStrips.Count != def.reelCount) return 0.0;
            if (def.rowCount <= 1 || def.reelCount <= 1) return 0.0;

            double total = 0.0;
            for (int i = 0; i < def.symbols.Count; i++)
            {
                var symbol = def.symbols[i];
                if (symbol == null) continue;

                double pay = symbol.MegaBlockPayoutExact();
                if (pay <= 0.0) continue;

                double reelPairs = 0.0;
                for (int r = 0; r + 1 < def.reelCount; r++)
                    reelPairs += AdjacentPairRatio(def, r, symbol.id) *
                                 AdjacentPairRatio(def, r + 1, symbol.id);

                total += (def.rowCount - 1) * reelPairs * pay;
            }

            return total;
        }

        /// <summary>Fraction of cyclically adjacent stop pairs on one reel that are both <paramref name="id"/>.</summary>
        private static double AdjacentPairRatio(SlotMachineDefinition def, int reel, int id)
        {
            var strip = def.reelStrips[reel]?.symbols;
            if (strip == null || strip.Count == 0) return 0.0;

            int length = strip.Count;
            int pairs = 0;
            for (int i = 0; i < length; i++)
                if (strip[i] == id && strip[(i + 1) % length] == id) pairs++;

            return (double)pairs / length;
        }

        /// <summary>
        /// Exact RTP contribution of a pick-a-prize bonus, in bet-multiples per spin:
        ///
        ///     B = P(trigger) × picks × weightedMean(prize)
        ///
        /// Every factor is independent of the paytable, which is what makes this an affine
        /// term the retune can subtract from the target and leave alone:
        ///
        ///   * The trigger is a count of one symbol id anywhere on the grid, so its chance
        ///     depends only on the strips. It is <em>not</em> a payline expectation: the
        ///     cells a reel shows are consecutive stops of one strip, so they are not
        ///     independent draws. Each reel's distribution of "how many triggers in my
        ///     visible window" is built by walking that reel's stop positions, then the
        ///     reels are convolved. Treating the 15 cells as i.i.d. would be wrong here in
        ///     a way it is not for paylines.
        ///   * Each prize behind each option is an independent draw from the weighted
        ///     list, and the player picks blind, so the expected total of N picks is
        ///     exactly N × weightedMean regardless of strategy.
        ///
        /// Returns 0 when the game declares no bonus, so games without one are unaffected.
        /// </summary>
        public static double BonusRtp(SlotMachineDefinition def)
        {
            var bonus = def?.Find<BonusGameFeature>();
            if (bonus == null || bonus.picks <= 0 || bonus.triggerCount <= 0) return 0.0;

            double mean = WeightedMeanPrize(bonus.prizes);
            if (mean <= 0.0) return 0.0;

            return TriggerChance(def, bonus) * bonus.picks * mean;
        }

        /// <summary>Expected prize per pick, in bet-multiples, from the weighted prize list.</summary>
        private static double WeightedMeanPrize(List<BonusPrize> prizes)
        {
            if (prizes == null || prizes.Count == 0) return 0.0;

            double total = 0.0, weight = 0.0;
            for (int i = 0; i < prizes.Count; i++)
            {
                var prize = prizes[i];
                if (prize == null || prize.weight <= 0f) continue;
                total += prize.payout * prize.weight;
                weight += prize.weight;
            }
            return weight > 0.0 ? total / weight : 0.0;
        }

        /// <summary>
        /// P(at least <c>triggerCount</c> trigger symbols on the grid).
        ///
        /// A reel shows one contiguous window of its strip, so its cells are correlated:
        /// two triggers in the window means two triggers within <c>rowCount</c> consecutive
        /// stops. Each reel therefore contributes a histogram over "triggers in my window"
        /// — one entry per stop position — and the exact grid distribution is the
        /// convolution of those histograms across reels. Independent cells would overcount
        /// clustered triggers badly on strips this short.
        /// </summary>
        private static double TriggerChance(SlotMachineDefinition def, BonusGameFeature bonus)
        {
            if (def.reelStrips == null || def.reelStrips.Count != def.reelCount) return 0.0;

            // distribution[n] = P(exactly n triggers on the grid so far).
            var distribution = new double[1];
            distribution[0] = 1.0;

            for (int r = 0; r < def.reelCount; r++)
            {
                var strip = def.reelStrips[r]?.symbols;
                if (strip == null || strip.Count == 0) return 0.0;

                int length = strip.Count;
                int window = Mathf.Max(1, def.rowCount);

                // histogram[k] = P(this reel's window holds k triggers).
                var histogram = new double[window + 1];
                for (int stop = 0; stop < length; stop++)
                {
                    int count = 0;
                    for (int row = 0; row < window; row++)
                        if (IsTrigger(strip[(stop + row) % length], bonus, def)) count++;

                    if (count > window) count = window;
                    histogram[count] += 1.0 / length;
                }

                var convolved = new double[distribution.Length + window];
                for (int a = 0; a < distribution.Length; a++)
                {
                    if (distribution[a] == 0.0) continue;
                    for (int b = 0; b < histogram.Length; b++)
                        convolved[a + b] += distribution[a] * histogram[b];
                }
                distribution = convolved;
            }

            double chance = 0.0;
            for (int n = bonus.triggerCount; n < distribution.Length; n++)
                chance += distribution[n];

            return chance > 1.0 ? 1.0 : chance;
        }

        /// <summary>Is this symbol id a bonus trigger? -1 means "count any scatter".</summary>
        private static bool IsTrigger(int symbolId, BonusGameFeature bonus, SlotMachineDefinition def)
        {
            if (bonus.triggerSymbolId >= 0) return symbolId == bonus.triggerSymbolId;
            return def.SymbolById(symbolId)?.isScatter == true;
        }

        /// <summary>
        /// True when <see cref="ExactRtp"/> can produce a trustworthy number. Cluster pays
        /// have no closed form, so their RTP can only be sampled and the gate must not
        /// present a sampled figure as though it were exact.
        /// </summary>
        public static bool SupportsExactRtp(SlotMachineDefinition def) =>
            def != null && def.payType != PayType.Cluster;

        /// <summary>
        /// Exact RTP for <see cref="PayType.Ways"/>. Each reel contributes an expected
        /// count of qualifying positions per symbol; wins are elementary symmetric
        /// polynomials over those counts, so this is closed form rather than enumeration.
        /// </summary>
        private static double ExactRtpWays(SlotMachineDefinition def, Dictionary<int, double>[] probs)
        {
            double total = 0.0;
            int wildId = -1;
            for (int i = 0; i < def.symbols.Count; i++)
                if (def.symbols[i] != null && def.symbols[i].isWild) { wildId = def.symbols[i].id; break; }

            double wildPerReel = wildId >= 0 ? probs[0].GetValueOrDefault(wildId) * def.rowCount : 0.0;

            for (int i = 0; i < def.symbols.Count; i++)
            {
                var symbol = def.symbols[i];
                if (symbol == null || symbol.isScatter) continue;

                var counts = new double[def.reelCount];
                for (int r = 0; r < def.reelCount; r++)
                {
                    if (symbol.isWild)
                    {
                        counts[r] = wildPerReel;
                    }
                    else
                    {
                        // A wild on a reel can stand in for this symbol on that reel.
                        counts[r] = probs[r].GetValueOrDefault(symbol.id) * def.rowCount + wildPerReel;
                    }
                }

                // Elementary symmetric polynomials over expected counts. Linear in the
                // outcome, so the expectation of the product is the product of expectations.
                var e = new double[def.reelCount + 1];
                e[0] = 1.0;
                for (int idx = 0; idx < def.reelCount; idx++)
                {
                    double v = counts[idx];
                    for (int k = idx + 1; k >= 1; k--)
                        e[k] += e[k - 1] * v;
                }

                for (int k = 3; k <= def.reelCount; k++)
                {
                    long payout = symbol.PayoutForMatch(k);
                    if (payout > 0) total += e[k] * payout;
                }
            }

            var freeSpins = def.Find<FreeSpinsFeature>();
            if (freeSpins != null)
            {
                // Scatter triggers are counted over the whole grid.
                double scatterPerReel = 0.0;
                for (int i = 0; i < def.symbols.Count; i++)
                {
                    if (def.symbols[i] == null || !def.symbols[i].isScatter) continue;
                    for (int r = 0; r < def.reelCount; r++)
                        scatterPerReel = Math.Max(scatterPerReel, probs[r].GetValueOrDefault(def.symbols[i].id));
                }

                if (scatterPerReel > 0)
                    total += ScatterTriggerChance(def, scatterPerReel, freeSpins.scatterCount) *
                             freeSpins.award * freeSpins.betMultiplier;
            }

            return total;
        }

        /// <summary>P(scatters on the grid &gt;= needed), summed over trigger counts.</summary>
        private static double ScatterTriggerChance(SlotMachineDefinition def, double pPerCell,
                                                   int scatterCount)
        {
            double pAnyOnReel = 1.0 - Math.Pow(1.0 - pPerCell, def.rowCount);

            // Small grid: enumerate the scatter count distribution exactly.
            var dist = new double[scatterCount + 1];
            dist[0] = 1.0;
            for (int i = 0; i < def.reelCount; i++)
            {
                var next = new double[dist.Length];
                for (int c = 0; c < dist.Length; c++)
                {
                    next[c] += dist[c] * (1.0 - pAnyOnReel);
                    if (c + 1 < dist.Length) next[c + 1] += dist[c] * pAnyOnReel;
                }
                dist = next;
            }

            double chance = 0.0;
            for (int c = scatterCount; c < dist.Length; c++)
                chance += dist[c];
            return chance;
        }

        private static double SumLine(SlotMachineDefinition def, Dictionary<int, double>[] probs,
                                      int depth, int width, int[] ids, double weight)
        {
            if (depth == width) return weight * LineWin(def, ids);

            double total = 0.0;
            foreach (var kv in probs[depth])
            {
                ids[depth] = kv.Key;
                total += SumLine(def, probs, depth + 1, width, ids, weight * kv.Value);
            }
            return total;
        }

        /// <summary>Mirrors PaylineEvaluator.EvaluateLine for one concrete line.</summary>
        private static double LineWin(SlotMachineDefinition def, int[] ids)
        {
            // The base symbol is the first non-wild, non-scatter symbol on the line.
            // A wild on reel 0 must not invalidate the whole line -- it substitutes.
            int baseId = -1;
            for (int r = 0; r < ids.Length; r++)
            {
                var d = def.SymbolById(ids[r]);
                if (d == null) break;
                if (d.isScatter) return 0.0;   // scatters never pay on a line
                if (d.isWild) continue;
                baseId = ids[r];
                break;
            }

            // All wilds: pay as the wild itself.
            if (baseId == -1)
            {
                var wd = def.SymbolById(ids[0]);
                if (wd == null || !wd.isWild) return 0.0;
                baseId = ids[0];
            }

            var baseDef = def.SymbolById(baseId);
            if (baseDef == null) return 0.0;

            bool sawWild = false;
            for (int r = 0; r < ids.Length; r++)
            {
                var d = def.SymbolById(ids[r]);
                if (d != null && d.isWild) sawWild = true;
            }

            int matched = 0;
            for (int r = 0; r < ids.Length; r++)
            {
                var other = def.SymbolById(ids[r]);
                if (other == null) break;

                if (ids[r] == baseId || other.isWild) { matched++; continue; }
                break;
            }

            if (matched < 3) return 0.0;

            int payId = baseId;
            if (sawWild)
            {
                payId = -1;
                for (int r = 0; r < ids.Length; r++)
                {
                    var s = def.SymbolById(ids[r]);
                    if (s == null || s.isWild || s.isScatter) continue;
                    payId = s.id;
                    break;
                }

                // All-wild line: fall back to the wild's own payout.
                if (payId < 0)
                    for (int i = 0; i < def.symbols.Count; i++)
                        if (def.symbols[i] != null && def.symbols[i].isWild)
                        {
                            payId = def.symbols[i].id;
                            break;
                        }
            }

            var payDef = def.SymbolById(payId);
            if (payDef == null) return 0.0;

            return payDef.PayoutForMatch(matched);
        }
    }
}

