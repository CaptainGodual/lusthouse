using System.Collections.Generic;
using LustHouse.EditorTools;
using LustHouse.Slots;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LustHouse.Tests
{
    /// <summary>
    /// The RTP ship gate (plan §3.6 / §5.8) applied to the real content.
    ///
    /// <see cref="MathSimulatorTests"/> exercises the simulator's mechanics against a
    /// synthetic fixture and deliberately does not assert a landing RTP — that is the
    /// right scope for it. This is the other half: every shipped
    /// <see cref="SlotMachineDefinition"/> on disk must validate, and every definition
    /// claiming to be ship-gated must return within ±0.5% of its declared target, so the
    /// gate blocks a build rather than being advisory (§5.8). Valid but untuned
    /// placeholders are listed in <c>Deferred</c> so they cannot silently escape review.
    ///
    /// Uses <see cref="AssetDatabase"/> rather than Resources/Addressables so it runs
    /// headless under -runTests.
    /// </summary>
    public class RtpGateTests
    {
        private const int Spins = 100_000;
        private const float Tolerance = 0.005f;   // §3.6: ±0.5%

        private static IEnumerable<string> SlotAssetPaths()
        {
            return AssetDatabase.FindAssets("t:SlotMachineDefinition", new[] { "Assets" });
        }

        private static List<SlotMachineDefinition> LoadAll()
        {
            var found = new List<SlotMachineDefinition>();
            foreach (var guid in SlotAssetPaths())
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<SlotMachineDefinition>(path);
                if (def != null) found.Add(def);
            }
            return found;
        }

        /// <summary>
        /// Every definition on disk has to validate, and every one of them has to say
        /// whether its maths is signed off.
        ///
        /// The status lives on the asset rather than in a list here (plan §3.7). That is
        /// the whole point: a game that was added and never signed off is *visibly*
        /// unmeasured, and adding a game touches no code — not even this file. A hardcoded
        /// list was one more thing to forget, and a suite that fails because somebody made
        /// a new game is a suite people learn to ignore.
        /// </summary>
        [Test]
        public void EveryDefinition_IsValidAndSaysWhetherItsMathsIsSignedOff()
        {
            var problems = new List<string>();
            var measured = new List<string>();
            var unmeasured = new List<string>();

            foreach (var def in LoadAll())
            {
                if (!def.IsValid(out var error))
                {
                    problems.Add($"{def.id}: invalid ({error})");
                    continue;
                }

                if (def.rtpStatus == RtpStatus.Measured) measured.Add(def.id);
                else unmeasured.Add(def.id);
            }

            Debug.Log($"[RtpGateTests] measured: {string.Join(", ", measured)}; " +
                      $"unmeasured: {string.Join(", ", unmeasured)}");

            Assert.IsEmpty(problems,
                "Every definition must validate:\n  " + string.Join("\n  ", problems));
        }

        /// <summary>
        /// Every game that claims to be measured has to measure up.
        ///
        /// The claim is checked exactly, not sampled: a Monte Carlo run cannot resolve
        /// ±0.5% on this game — at 2,000,000 spins the estimate still swings 0.956..0.973
        /// by seed, because one spin can pay 1500x. Simulation below covers hit rate and
        /// max win only.
        /// </summary>
        [Test]
        [Category("RtpGate")]
        public void EveryMeasuredGame_HitsItsDeclaredRtp()
        {
            var checkedGames = 0;

            foreach (var def in LoadAll())
            {
                if (def.rtpStatus != RtpStatus.Measured) continue;
                checkedGames++;

                Assert.IsTrue(def.IsValid(out var error),
                    $"{def.id} claims a measured RTP but does not validate: {error}");

                double exact = SimulationEngine.ExactRtp(def);
                double drift = System.Math.Abs(exact - def.targetRtp);

                Debug.Log($"[RtpGateTests] {def.id}: exact RTP {exact:P4} " +
                          $"(target {def.targetRtp:P4}, drift {drift:P4})");

                Assert.LessOrEqual(drift, Tolerance,
                    $"{def.id} is signed off but its exact RTP {exact:P4} is outside ±0.5% of " +
                    $"target {def.targetRtp:P4} (plan §3.6 ship gate).");

                var report = SimulationEngine.Run(def, Spins, def.minBet, seed: 20251018);
                Debug.Log($"[RtpGateTests] {def.id}: {Spins:N0} simulated spins -> " +
                          $"RTP {report.rtp:P3}, hit {report.hitRate:P2}, " +
                          $"max {report.biggestWinMultiplier:0.#}x");

                Assert.AreEqual(0, report.maxWinBreaches,
                    $"A spin paid more than maxWinMultiplier ({def.maxWinMultiplier}x).");
            }

            Assert.Greater(checkedGames, 0,
                "No game is signed off. A gate that measures nothing passes everything.");
        }
    }
}