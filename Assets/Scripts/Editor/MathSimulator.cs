using System;
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
                spins = spins,
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
    }

    /// <summary>
    /// Editor window for the ship gate (plan §3.6): run 100,000 spins and confirm RTP is
    /// within ±0.5% of target before a game ships.
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