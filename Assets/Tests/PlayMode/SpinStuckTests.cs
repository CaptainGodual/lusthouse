using System.Collections;
using System.Collections.Generic;
using LustHouse.Core;
using LustHouse.Slots;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace LustHouse.Tests
{
    /// <summary>
    /// A spin always ends.
    ///
    /// The slot screen disables every control while a spin is in flight, so a spin that
    /// never finishes is a machine the player cannot do anything with: no spin, no bet,
    /// no bet up or down. Two things could leave it in that state, and both happened on a
    /// device.
    ///
    /// A level-up is published from inside the spin — the maths raises it the moment the
    /// win is credited — and the presenter used to call StopAllCoroutines to restart the
    /// level banner's timer. That killed the spin in flight.
    ///
    /// And a feature round is long: a free-spins round is dozens of spins and a
    /// retriggering strip runs to hundreds. Animating every one of them at the full spin
    /// length is forty seconds to four minutes of reels turning with the controls dead.
    /// </summary>
    public class SpinStuckTests
    {
        private const string SlotScene = "Slot";

        private readonly List<Object> _spawned = new List<Object>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = 0; i < _spawned.Count; i++)
                if (_spawned[i] != null) Object.Destroy(_spawned[i]);
            _spawned.Clear();

            SlotLaunchPayload.Payload = null;
            yield return null;
        }

        /// <summary>Loads the slot scene with a definition, the way the lobby hands one over.</summary>
        private IEnumerator LoadSlot()
        {
            SlotLaunchPayload.Payload = BuildDefinition();

            yield return SceneManager.LoadSceneAsync(SlotScene);
            yield return null;
            yield return null;
        }

        private static SlotMachineDefinition BuildDefinition()
        {
            var def = ScriptableObject.CreateInstance<SlotMachineDefinition>();
            def.id = "stuck_fixture";
            def.reelCount = 5;
            def.rowCount = 3;
            def.minBet = 1000;
            def.maxBet = 100000;
            def.spinDurationSeconds = 0.4f;

            def.symbols = new List<SymbolDefinition>
            {
                new SymbolDefinition { id = 0, symbolName = "Wild", isWild = true },
                new SymbolDefinition { id = 1, symbolName = "Ace", payoutsByMatch = new long[] { 5, 20, 50 } },
                new SymbolDefinition { id = 2, symbolName = "King", payoutsByMatch = new long[] { 3, 10, 30 } }
            };

            def.reelStrips = new List<ReelStrip>();
            for (int r = 0; r < 5; r++)
                def.reelStrips.Add(new ReelStrip
                {
                    symbols = new List<int> { r % 3, (r + 1) % 3, (r + 2) % 3, r % 3 }
                });

            def.paylines = new List<PaylineDefinition>
            {
                new PaylineDefinition { lineName = "Middle", rows = new[] { 1, 1, 1, 1, 1 } }
            };

            return def;
        }

        private static Button FindButton(string name)
        {
            foreach (var button in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
                if (button.name == name) return button;
            return null;
        }

        private static void Fund()
        {
            Core.ServicesBootstrap.EnsureExists();

            var wallet = Core.WalletService.Instance;
            Assert.IsNotNull(wallet, "There is no WalletService.");
            wallet.TryGrant(Core.Currency.Coins, 10_000_000, Core.LedgerReason.Admin,
                "spin stuck test");
        }

        /// <summary>Waits for the spin to finish, or gives up.</summary>
        private static IEnumerator WaitForSpin(SlotPresenter presenter, int maxFrames = 1200)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (!presenter.IsSpinning) yield break;
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator ALevelUpDuringASpin_DoesNotFreezeTheGame()
        {
            yield return LoadSlot();

            var presenter = Object.FindFirstObjectByType<SlotPresenter>();
            Assert.IsNotNull(presenter, "The Slot scene has no SlotPresenter.");

            Fund();

            var spin = FindButton("Spin");
            Assert.IsNotNull(spin, "The slot screen has no spin button.");

            spin.onClick.Invoke();
            yield return null;

            Assert.IsTrue(presenter.IsSpinning, "The spin did not start.");

            // The maths raises a level-up from inside the spin, the moment the win is
            // credited. This is exactly what a real level-up looks like to the presenter.
            EventBus.Publish(new LevelUp { newLevel = 2, xpReward = 0 });

            yield return WaitForSpin(presenter);

            Assert.IsFalse(presenter.IsSpinning,
                "The spin never finished. A level-up called StopAllCoroutines, which killed " +
                "the spin in flight and left every control disabled for good.");

            Assert.IsTrue(spin.interactable,
                "The spin button is still disabled after the spin finished.");
        }

        [UnityTest]
        public IEnumerator AFeatureRound_CannotOutlastItsBudget()
        {
            yield return LoadSlot();

            var presenter = Object.FindFirstObjectByType<SlotPresenter>();
            Assert.IsNotNull(presenter, "The Slot scene has no SlotPresenter.");

            // Measured on the shipped content: an average feature round is 27 spins and the
            // worst case seen over 20,000 spins was over 160. At the full spin length each
            // of those is a minute or more of the player watching reels turn with every
            // control disabled — which reads as a frozen machine, not a bonus.
            Assert.LessOrEqual(presenter.FeatureRoundSeconds(160), 10f,
                "A feature round of 160 spins must not take longer than ten seconds to show.");

            Assert.LessOrEqual(presenter.FeatureRoundSeconds(1024), 10f,
                "A feature round must stay inside its budget however long the maths ran.");
        }

        [UnityTest]
        public IEnumerator TheSpinWatchdog_TakesTheControlsBack()
        {
            yield return LoadSlot();

            var presenter = Object.FindFirstObjectByType<SlotPresenter>();
            Assert.IsNotNull(presenter, "The Slot scene has no SlotPresenter.");

            // The backstop: whatever goes wrong in a spin, the player gets the buttons back.
            presenter.spinWatchdogSeconds = 0.05f;

            Fund();

            var spin = FindButton("Spin");
            Assert.IsNotNull(spin, "The slot screen has no spin button.");

            // The watchdog says what happened. A spin that overran its budget is an error,
            // even though the game recovers from it.
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "A spin ran for"));

            spin.onClick.Invoke();
            yield return null;

            Assert.IsTrue(presenter.IsSpinning, "The spin did not start.");

            yield return WaitForSpin(presenter);

            Assert.IsFalse(presenter.IsSpinning,
                "A spin that overran its budget never gave the controls back. A player with " +
                "no button to press has no way out.");

            Assert.IsTrue(spin.interactable,
                "The spin button is still disabled after the watchdog fired.");
        }
    }
}
