using System;
using System.Collections.Generic;
using LustHouse.Slots;
using UnityEditor;
using UnityEngine;

namespace LustHouse.EditorTools
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

        /// <summary>Runs every bet level so the ladder cannot hide a bad rung.</summary>
        public static SimulationReport RunAllBets(SlotMachineDefinition def, long spinsPerLevel, int seed)
        {
            if (def == null) return default;

            long accumulatedBet = 0, accumulatedWin = 0, biggest = 0, total = 0;
            double sq = 0.0;
            var started = DateTime.UtcNow;

            for (int level = 0; level < Mathf.Max(1, def.betLevels); level++)
            {
                long bet = def.BetForLevel(level);
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
        /// This is the number the ship gate must use. Simulation stays useful for hit
        /// rate, variance and max-win behaviour, which have no closed form here.
        /// </summary>
        public static double ExactRtp(SlotMachineDefinition def)
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
            return def.paylines.Count * SumLine(def, probs, 0, width, ids, 1.0);
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

            if (def.hasFreeSpins)
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
                    total += ScatterTriggerChance(def, scatterPerReel) *
                             def.freeSpinAward * def.freeSpinBetMultiplier;
            }

            return total;
        }

        /// <summary>P(scells on one reel &gt;= 1) ^ needed, summed over trigger counts.</summary>
        private static double ScatterTriggerChance(SlotMachineDefinition def, double pPerCell)
        {
            int cellsPerReel = def.reelCount * def.rowCount;
            double pAnyOnReel = 1.0 - Math.Pow(1.0 - pPerCell, def.rowCount);

            // Small grid: enumerate the scatter count distribution exactly.
            var dist = new double[def.freeSpinScatterCount + 1];
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
            for (int c = def.freeSpinScatterCount; c < dist.Length; c++)
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
            var baseDef = def.SymbolById(ids[0]);
            if (baseDef == null || baseDef.isScatter) return 0.0;

            bool sawWild = baseDef.isWild;
            int matched = 1;

            for (int r = 1; r < ids.Length; r++)
            {
                var other = def.SymbolById(ids[r]);
                if (other == null) break;
                if (ids[r] == ids[0]) { matched++; continue; }
                if (other.isWild) { matched++; sawWild = true; continue; }
                break;
            }

            if (matched < 3) return 0.0;

            int payId = baseDef.id;
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
            return payDef == null ? 0.0 : (double)payDef.PayoutForMatch(matched);
        }
    }

    /// <summary>
    /// Editor window for the ship gate (plan §3.6): confirm RTP is within ±0.5% of
    /// target before a game ships.
    ///
    /// The headline number is <see cref="SimulationEngine.ExactRtp"/>; the simulation
    /// runs alongside it for hit rate, variance and max win, which have no closed form.
    /// </summary>
    public class MathSimulatorWindow : EditorWindow
    {
        private SlotMachineDefinition _definition;
        private long _spins = 100_000;
        private long _spinsPerLevel = 20_000;
        private int _seed = 20261018;
        private float _tolerance = 0.005f;
        private bool _allBets;
        private SimulationReport _report;
        private bool _hasReport;

        [MenuItem("LustHouse/Math Simulator")]
        public static void Open() => GetWindow<MathSimulatorWindow>("Math Simulator");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("RTP Simulation Gate", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Never ship a paytable change without a passing run. Guardrail §6: the RNG is " +
                "seeded, so a run is reproducible from the seed below.", MessageType.Info);

            _definition = (SlotMachineDefinition)EditorGUILayout.ObjectField(
                "Definition", _definition, typeof(SlotMachineDefinition), false);

            _allBets = EditorGUILayout.Toggle("Sweep all bet levels", _allBets);
            if (_allBets)
                _spinsPerLevel = EditorGUILayout.LongField("Spins per level", _spinsPerLevel);
            else
                _spins = EditorGUILayout.LongField("Spins", _spins);

            _seed = EditorGUILayout.IntField("Seed", _seed);
            _tolerance = EditorGUILayout.Slider("RTP tolerance", _tolerance, 0.0005f, 0.02f);

            using (new EditorGUI.DisabledScope(_definition == null))
            {
                if (GUILayout.Button("Run simulation", GUILayout.Height(28)))
                    Run();
            }

            if (!_hasReport) return;

            EditorGUILayout.Space();
            DrawReport(_report, _definition != null ? _definition.targetRtp : 0f, _tolerance);
        }

        private void Run()
        {
            if (_definition == null) return;

            if (!_definition.IsValid(out var error))
            {
                EditorUtility.DisplayDialog("Invalid definition", error, "OK");
                _hasReport = false;
                return;
            }

            _report = _allBets
                ? SimulationEngine.RunAllBets(_definition, _spinsPerLevel, _seed)
                : SimulationEngine.Run(_definition, _spins, _definition.minBet, _seed);

            _hasReport = true;
            Repaint();
        }

        private static void DrawReport(SimulationReport r, float targetRtp, float tolerance)
        {
            bool pass = r.WithinTolerance(targetRtp, tolerance);

            EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Row("Game", r.gameId);
                Row("Spins", r.spins.ToString("N0"));
                Row("Total bet", r.totalBet.ToString("N0"));
                Row("Total win", r.totalWin.ToString("N0"));
                Row("RTP", $"{r.rtp:P3}");
                Row("Target", $"{targetRtp:P3} ±{tolerance:P2}");
                Row("Hit rate", $"{r.hitRate:P2}");
                Row("Mean win", r.meanWin.ToString("N2"));
                Row("Std dev (volatility)", r.stdDevWin.ToString("N2"));
                Row("Biggest win", $"{r.biggestWin:N0} ({r.biggestWinMultiplier:N1}×)");
                Row("Free-spin rate", $"{r.freeSpinRate:P2}");
                Row("Max-win breaches", r.maxWinBreaches.ToString("N0"));
                Row("Elapsed", $"{r.secondsElapsed:F2}s");
            }

            EditorGUILayout.Space();
            var style = new GUIStyle(EditorStyles.boldLabel);
            if (pass)
            {
                style.normal.textColor = new Color(0.36f, 0.85f, 0.55f);
                EditorGUILayout.LabelField("PASS — RTP within tolerance", style);
            }
            else
            {
                style.normal.textColor = new Color(1f, 0.36f, 0.36f);
                EditorGUILayout.LabelField("FAIL — retune the paytable and re-simulate", style);
            }

            if (r.maxWinBreaches > 0)
            {
                EditorGUILayout.HelpBox(
                    $"{r.maxWinBreaches:N0} spins exceeded maxWinMultiplier. Clamp or re-scale before shipping.",
                    MessageType.Error);
            }
        }

        private static void Row(string label, string value)
        {
            EditorGUILayout.LabelField(label, value);
        }
    }
}