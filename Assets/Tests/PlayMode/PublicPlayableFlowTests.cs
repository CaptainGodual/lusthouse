using System;
using System.Collections;
using System.IO;
using LustHouse.Boot;
using LustHouse.Core;
using LustHouse.Lobby;
using LustHouse.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LustHouse.Tests
{
    /// <summary>
    /// The first-run sequence, end to end (plan §1.1, §1.3, §5.7).
    ///
    /// The order matters and is the whole point of the stage: the age gate, then the
    /// probability disclosure, then the free start pack, then the lobby. A player who
    /// has not been shown the odds must not be able to start playing before they have
    /// been, and the offer must not come back after it has been answered.
    ///
    /// The edit mode tests cover what the disclosure says and what the offer does. This
    /// covers the seam only the running game has — the order the player actually meets.
    /// </summary>
    public class PublicPlayableFlowTests
    {
        private const string BootScene = "BootLoader";
        private const string LobbyScene = "Lobby";

        private string _savePath;
        private string _saveBackup;
        private byte[] _savedBytes;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            ServicesBootstrap.EnsureExists();

            // The player's save is the real one. Take a copy so a test run cannot
            // decide what the player's next launch looks like.
            _savePath = Path.Combine(Application.persistentDataPath, "save.json");
            _saveBackup = _savePath + ".flowtest";
            try
            {
                if (File.Exists(_savePath))
                {
                    _savedBytes = File.ReadAllBytes(_savePath);
                    File.WriteAllBytes(_saveBackup, _savedBytes);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LustHouse] Could not back the save up: " + e.Message);
            }

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try
            {
                if (_savedBytes != null)
                {
                    File.WriteAllBytes(_savePath, _savedBytes);
                    if (File.Exists(_saveBackup)) File.Delete(_saveBackup);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LustHouse] Could not restore the save: " + e.Message);
            }

            yield return null;
        }

        // ---- the order ---------------------------------------------------------

        [UnityTest]
        public IEnumerator AFreshProfileSeesTheGateThenTheDisclosureThenTheOfferThenTheLobby()
        {
            yield return LoadBoot();
            MakeProfileFresh();

            // 1. Tapping to enter raises the gate, and nothing else.
            var enter = FindButton("TapToEnter");
            Assert.IsNotNull(enter, "The boot screen has no TAP TO ENTER button.");
            enter.onClick.Invoke();
            yield return null;

            var gate = Object.FindFirstObjectByType<AgeGateOverlay>();
            Assert.IsNotNull(gate,
                "The player tapped to enter and no age gate came up. The lobby is " +
                "reachable without ever answering the age question.");
            Assert.IsNull(Object.FindFirstObjectByType<ProbabilityDisclosureSheet>(),
                "The disclosure was shown before the age gate was answered.");

            // 2. Answering the gate shows the disclosure.
            var confirm = FindButtonIn(gate.gameObject, "ConfirmAge");
            Assert.IsNotNull(confirm, "The age gate has no way to say you are 18.");
            confirm.onClick.Invoke();
            yield return null;

            var disclosure = Object.FindFirstObjectByType<ProbabilityDisclosureSheet>();
            Assert.IsNotNull(disclosure,
                "The player answered the age gate and no probability disclosure came up. " +
                "The odds have to be published before the player can play.");

            yield return WaitForMeasurement(disclosure);

            // 3. Closing the disclosure shows the offer.
            var close = FindButtonIn(disclosure.gameObject, "Close");
            Assert.IsNotNull(close, "The disclosure has no way to close it.");
            close.onClick.Invoke();
            yield return WaitForObject<FirstRunPackPrompt>();

            var prompt = Object.FindFirstObjectByType<FirstRunPackPrompt>();
            Assert.IsNotNull(prompt,
                "The disclosure was closed and the free start pack offer never came up.");
            Assert.IsNull(Object.FindFirstObjectByType<AgeGateOverlay>(),
                "The age gate is still up behind the offer.");

            // 4. Answering the offer opens the lobby.
            var decline = FindButtonByLabel(prompt.gameObject, "NO THANKS");
            Assert.IsNotNull(decline, "The offer gives the player no way to decline it.");
            decline.onClick.Invoke();
            yield return null;

            var thanks = FindButtonByLabel(prompt.gameObject, "LET'S PLAY");
            Assert.IsNotNull(thanks, "The offer never confirmed what the player was given.");
            thanks.onClick.Invoke();

            yield return WaitForScene(LobbyScene);
            Assert.AreEqual(LobbyScene, SceneManager.GetActiveScene().name,
                "The player answered the offer and the lobby never opened.");
        }

        [UnityTest]
        public IEnumerator TheDisclosureAndTheOfferAreNotShownTwice()
        {
            yield return LoadBoot();
            MakeProfileFresh();

            var enter = FindButton("TapToEnter");
            enter.onClick.Invoke();
            yield return null;

            var confirm = FindButtonIn(Object.FindFirstObjectByType<AgeGateOverlay>().gameObject, "ConfirmAge");
            confirm.onClick.Invoke();
            yield return null;

            var disclosure = Object.FindFirstObjectByType<ProbabilityDisclosureSheet>();
            Assert.IsNotNull(disclosure, "The disclosure did not come up for a fresh profile.");
            yield return WaitForMeasurement(disclosure);

            FindButtonIn(disclosure.gameObject, "Close").onClick.Invoke();
            yield return WaitForObject<FirstRunPackPrompt>();

            var prompt = Object.FindFirstObjectByType<FirstRunPackPrompt>();
            Assert.IsNotNull(prompt, "The offer did not come up for a fresh profile.");

            FindButtonByLabel(prompt.gameObject, "NO THANKS").onClick.Invoke();
            yield return null;
            FindButtonByLabel(prompt.gameObject, "LET'S PLAY").onClick.Invoke();

            yield return WaitForScene(LobbyScene);

            // Back to the boot screen: neither may come up again.
            yield return LoadBoot();

            var enterAgain = FindButton("TapToEnter");
            Assert.IsNotNull(enterAgain, "The boot screen has no TAP TO ENTER button.");
            enterAgain.onClick.Invoke();
            yield return null;

            Assert.IsNull(Object.FindFirstObjectByType<ProbabilityDisclosureSheet>(),
                "The probability disclosure was shown a second time. It is a one-time " +
                "thing before the lobby, and it stays reachable from the lobby.");
            Assert.IsNull(Object.FindFirstObjectByType<FirstRunPackPrompt>(),
                "The free start pack offer was asked a second time.");
        }

        // ---- the self-exclusion ------------------------------------------------

        [UnityTest]
        public IEnumerator ASelfExcludedPlayerCannotOpenAGame()
        {
            yield return SceneManager.LoadSceneAsync(LobbyScene);
            yield return null;
            yield return null;

            var controller = Object.FindFirstObjectByType<LobbyController>();
            Assert.IsNotNull(controller, "There is no lobby controller.");
            Assert.IsNotNull(controller.catalog, "The lobby has no catalogue.");
            Assert.Greater(controller.catalog.games.Count, 0, "The catalogue is empty.");

            var progression = LustHouse.Economy.PlayerProgression.Instance;
            Assert.IsNotNull(progression, "There is no player progression.");
            progression.SetSelfExclusion(SelfExclusion.PermanentUtc);

            Assert.IsTrue(ResponsiblePlayService.Instance != null &&
                          ResponsiblePlayService.Instance.IsExcluded,
                "A permanent self-exclusion did not take effect.");

            // The tile press.
            controller.OnGameTilePressed(controller.catalog.games[0].id);
            yield return null;

            Assert.AreEqual(LobbyScene, SceneManager.GetActiveScene().name,
                "A self-excluded player opened a game from the tile.");

            // And the launch itself, which is a separate door.
            controller.Launch(controller.catalog.games[0]);
            yield return null;
            yield return null;

            Assert.AreEqual(LobbyScene, SceneManager.GetActiveScene().name,
                "A self-excluded player opened a game from the launch path.");
        }

        // ---- the lobby's permanent entries --------------------------------------

        [UnityTest]
        public IEnumerator TheLobbyKeepsTheOddsAndTheResponsiblePlayReachable()
        {
            yield return SceneManager.LoadSceneAsync(LobbyScene);
            yield return null;
            yield return null;

            Assert.IsNotNull(FindButton("OddsChip"),
                "The lobby has no odds button, so the probability disclosure cannot be " +
                "reached after the first minute.");
            Assert.IsNotNull(FindButton("ResponsiblePlayChip"),
                "The lobby has no responsible play button, so the self-exclusion cannot " +
                "be reached.");
        }

        // ---- helpers ------------------------------------------------------------



        /// <summary>
        /// Loads the boot screen with its waiting beats cut short.
        /// </summary>
        private IEnumerator LoadBoot()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            yield return SceneManager.LoadSceneAsync(BootScene);
            SceneManager.sceneLoaded -= OnSceneLoaded;

            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                var sequence = root.GetComponent<BootSequence>();
                if (sequence == null) continue;
                sequence.splashSeconds = 0.01f;
                sequence.loungeSeconds = 0.01f;
                sequence.minimumScreenTime = 0.01f;
            }

            yield return null;
            yield return WaitForBootReady();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != BootScene) return;

            foreach (var root in scene.GetRootGameObjects())
            {
                var sequence = root.GetComponent<BootSequence>();
                if (sequence == null) continue;
                sequence.splashSeconds = 0.01f;
                sequence.loungeSeconds = 0.01f;
                sequence.minimumScreenTime = 0.01f;
            }
        }

        private static IEnumerator WaitForBootReady(float maxSeconds = 20f)
        {
            float deadline = Time.realtimeSinceStartup + maxSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                var sequence = Object.FindFirstObjectByType<BootSequence>();
                if (sequence != null && sequence.ReadyToEnter) yield break;
                yield return null;
            }
        }

        private static IEnumerator WaitForScene(string sceneName, float maxSeconds = 10f)
        {
            float deadline = Time.realtimeSinceStartup + maxSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (SceneManager.GetActiveScene().name == sceneName) yield break;
                yield return null;
            }
        }

        /// <summary>
        /// Waits for an object to appear, on the wall clock rather than on frames.
        ///
        /// A frame count is not a duration: on a headless test host the frames run far
        /// faster than the animation they are animating, so a wait in frames can end
        /// before the animation has finished.
        /// </summary>
        private static IEnumerator WaitForObject<T>(float maxSeconds = 8f) where T : MonoBehaviour
        {
            float deadline = Time.realtimeSinceStartup + maxSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (Object.FindFirstObjectByType<T>() != null) yield break;
                yield return null;
            }
        }

        /// <summary>
        /// Waits for the disclosure to finish measuring.
        ///
        /// The sheet measures the games it publishes, in chunks, across frames. Closing
        /// it before the measurement finishes would prove nothing about the content.
        /// </summary>
        private static IEnumerator WaitForMeasurement(ProbabilityDisclosureSheet sheet,
                                                      float maxSeconds = 30f)
        {
            float deadline = Time.realtimeSinceStartup + maxSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (sheet == null) yield break;

                var status = sheet.transform.Find("Panel/Status");
                if (status != null && !status.gameObject.activeSelf) yield break;

                yield return null;
            }
        }

        /// <summary>
        /// Puts the profile back to its first-run state: no age answer, no disclosure
        /// seen, no offer answered.
        /// </summary>
        private static void MakeProfileFresh()
        {
            var progression = LustHouse.Economy.PlayerProgression.Instance;
            Assert.IsNotNull(progression, "There is no player progression.");

            var state = progression.Capture();
            state.ageConfirmed = false;
            state.disclosureSeen = false;
            state.selfExcludedUntilUtc = "";
            progression.RestoreFrom(state);

            // The offer is once-ever, and "once-ever" lives in the ledger — so a
            // profile that has already been given the pack cannot be made fresh
            // without taking the grant back out of the ledger.
            var wallet = WalletService.Instance;
            if (wallet != null)
            {
                foreach (var currency in new[] { Currency.Coins, Currency.Gems })
                {
                    var account = wallet.Account(currency);
                    if (account == null) continue;

                    account.ledger.RemoveAll(entry =>
                        entry.note == ServicesBootstrap.FirstRunPackNote ||
                        entry.note == ServicesBootstrap.FirstRunStakeNote);
                    account.SetBalanceDirect(account.ReplayBalance());
                }
            }

            var save = SaveSystem.Default;
            var loaded = save.Load();
            if (!loaded.ok) return;

            loaded.document.player.ageConfirmed = false;
            loaded.document.player.disclosureSeen = false;
            loaded.document.player.selfExcludedUntilUtc = "";
            save.Save(loaded.document);
        }

        private static Button FindButton(string name)
        {
            foreach (var button in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
                if (button.name == name) return button;
            return null;
        }

        private static Button FindButtonIn(GameObject root, string name)
        {
            if (root == null) return null;

            foreach (var button in root.GetComponentsInChildren<Button>(true))
                if (button.name == name) return button;
            return null;
        }

        /// <summary>Finds a button by the text on its label.</summary>
        private static Button FindButtonByLabel(GameObject root, string label)
        {
            if (root == null) return null;

            foreach (var button in root.GetComponentsInChildren<Button>(true))
            {
                foreach (var text in button.GetComponentsInChildren<Text>(true))
                    if (text.text == label) return button;
            }
            return null;
        }
    }
}
