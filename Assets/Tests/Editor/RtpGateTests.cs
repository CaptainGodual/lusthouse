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
    /// <see cref="SlotMachineDefinition"/> on disk must actually validate and actually
    /// return within ±0.5% of its declared target, so the gate blocks a build rather
    /// than being advisory (§5.8).
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

        [Test]
        public void PlaceholderDefinitions_AreReportedNotSilentlyPassed()
        {
            // M1 ships one playable title (Velvet Room). The other definitions are
            // placeholders with no reel strips yet, so they are out of RTP scope --
            // but they must never be silently mistaken for gated content.
            var skipped = new List<string>();
            foreach (var def in LoadAll())
                if (!def.IsValid(out var error))
                    skipped.Add($"{def.id}: {error}");

            Debug.Log($"[RtpGateTests] {skipped.Count} definition(s) not yet in RTP scope:\n  " +
                      string.Join("\n  ", skipped));
            Assert.IsNotEmpty(skipped,
                "Expected the placeholder definitions to still be invalid; if they all " +
                "validate now, promote them into the RTP gate below.");
        }

        [Test]
        [Category("RtpGate")]
        public void VelvetRoom_HitsItsDeclaredRtp()
        {
            var def = AssetDatabase.LoadAssetAtPath<SlotMachineDefinition>(
                ContentBootstrap.VelvetRoomPath);

            Assert.IsNotNull(def, $"Velvet Room missing at {ContentBootstrap.VelvetRoomPath}.");
            if (!def.IsValid(out var error))
                Assert.Fail($"Velvet Room is invalid: {error}");

            // Exact, not sampled. A Monte Carlo run cannot resolve +/-0.5% on this game:
            // at 2,000,000 spins the estimate still swings 0.956..0.973 by seed, because
            // one spin can pay 1500x. Simulation below covers hit rate and max win only.
            double exact = SimulationEngine.ExactRtp(def);
            double drift = System.Math.Abs(exact - def.targetRtp);

            Debug.Log($"[RtpGateTests] {def.id}: exact RTP {exact:P4} " +
                      $"(target {def.targetRtp:P4}, drift {drift:P4})");

            Assert.LessOrEqual(drift, Tolerance,
                $"Exact RTP {exact:P4} is outside ±0.5% of target {def.targetRtp:P4} " +
                "(plan §3.6 ship gate).");

            var report = SimulationEngine.Run(def, Spins, def.minBet, seed: 20251018);
            Debug.Log($"[RtpGateTests] {def.id}: {Spins:N0} simulated spins -> " +
                      $"RTP {report.rtp:P3}, hit {report.hitRate:P2}, " +
                      $"max {report.biggestWinMultiplier:0.#}x");

            Assert.AreEqual(0, report.maxWinBreaches,
                $"A spin paid more than maxWinMultiplier ({def.maxWinMultiplier}x).");
        }
    }
}