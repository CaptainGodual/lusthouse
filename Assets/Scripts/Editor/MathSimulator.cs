using System;
using System.Collections.Generic;
using LustHouse.Slots;
using UnityEditor;
using UnityEngine;

namespace LustHouse.EditorTools
{
    /// <summary>
    /// Editor window for the ship gate (plan §3.6): confirm RTP is within ±0.5% of
    /// target before a game ships.
    ///
    /// The headline number is <see cref="SimulationEngine.ExactRtp"/>; the simulation
    /// runs alongside it for hit rate, variance and max win, which have no closed form.
    ///
    /// The engine itself lives in the runtime assembly
    /// (<c>Assets/Scripts/Slots/SimulationEngine.cs</c>): the probability disclosure
    /// prints the same numbers at run time, and a class that only exists in the editor
    /// cannot be printed by a build.
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
            bool pass = DrawReport(_report, _definition != null ? _definition.targetRtp : 0f, _tolerance);

            DrawSignOff(pass);
        }

        /// <summary>
        /// Signs the definition off, or reopens it.
        ///
        /// The status lives on the asset (plan §3.7), so signing a game off is a click
        /// rather than an edit to a list in a test file. It can only be signed off on a
        /// passing run — the point of the gate is that the claim and the maths agree.
        /// </summary>
        private void DrawSignOff(bool pass)
        {
            if (_definition == null) return;

            EditorGUILayout.Space();

            if (_definition.rtpStatus == RtpStatus.Measured)
            {
                EditorGUILayout.HelpBox(
                    "This game is signed off. The RTP gate now checks that its maths still " +
                    "hits the target, so a paytable edit that breaks the claim fails the " +
                    "suite until it is measured again.",
                    MessageType.Info);

                if (GUILayout.Button("Reopen — mark as unmeasured", GUILayout.Height(24)))
                {
                    _definition.rtpStatus = RtpStatus.Unmeasured;
                    EditorUtility.SetDirty(_definition);
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[LustHouse] '{_definition.id}' reopened for retuning.");
                }
                return;
            }

            if (!pass)
            {
                EditorGUILayout.HelpBox(
                    "A game can only be signed off on a passing run. Retune the paytable and " +
                    "simulate again.",
                    MessageType.Warning);
                return;
            }

            if (GUILayout.Button("Sign off — mark this game's RTP as measured", GUILayout.Height(28)))
            {
                _definition.rtpStatus = RtpStatus.Measured;
                EditorUtility.SetDirty(_definition);
                AssetDatabase.SaveAssets();
                Debug.Log($"[LustHouse] '{_definition.id}' signed off at " +
                          $"{_report.rtp:P3} against a target of {_definition.targetRtp:P3}.");
            }
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

        private static bool DrawReport(SimulationReport r, float targetRtp, float tolerance)
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

            return pass;
        }

        private static void Row(string label, string value)
        {
            EditorGUILayout.LabelField(label, value);
        }
    }
}