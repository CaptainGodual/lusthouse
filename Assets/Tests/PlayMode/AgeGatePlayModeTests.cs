using System.Collections;
using LustHouse.Boot;
using LustHouse.Core;
using LustHouse.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace LustHouse.Tests
{
    /// <summary>
    /// The age gate, end to end: the player taps to enter, the gate comes up, and the
    /// lobby stays shut until they have answered it.
    ///
    /// The edit mode tests cover the policy and the wording. This covers the seam only
    /// the running game has: that the tap which enters the game is the tap which
    /// raises the gate, and that the gate is the only way past it. A gate that is
    /// closed in a unit test and open in the build is the worst failure there is, and
    /// it is invisible to every test that does not press the button.
    /// </summary>
    public class AgeGatePlayModeTests
    {
        private const string BootScene = "BootLoader";
        private const string LobbyScene = "Lobby";

        private bool _wasConfirmed;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            ServicesBootstrap.EnsureExists();
            _wasConfirmed = AgeGate.IsConfirmed;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Put the answer back the way it was, and write it out, so a test run does
            // not decide what the player's next launch looks like.
            var progression = LustHouse.Economy.PlayerProgression.Instance;
            if (progression != null)
            {
                var state = progression.Capture();
                state.ageConfirmed = _wasConfirmed;
                progression.RestoreFrom(state);
            }

            var save = SaveSystem.Default;
            var loaded = save.Load();
            if (loaded.ok)
            {
                loaded.document.player.ageConfirmed = _wasConfirmed;
                save.Save(loaded.document);
            }

            yield return null;
        }

        // ---- the boot path ------------------------------------------------------

        [UnityTest]
        public IEnumerator TappingToEnter_AsksThePlayerTheirAge()
        {
            yield return LoadBoot();
            ForceUnconfirmed();

            var enter = FindButton("TapToEnter");
            Assert.IsNotNull(enter, "The boot screen has no TAP TO ENTER button.");

            enter.onClick.Invoke();
            yield return null;

            var gate = Object.FindFirstObjectByType<AgeGateOverlay>();
            Assert.IsNotNull(gate,
                "The player tapped to enter and no age gate came up. The lobby is " +
                "reachable without ever answering the age question.");

            Assert.AreEqual(BootScene, SceneManager.GetActiveScene().name,
                "The age gate came up and the lobby was entered anyway. The gate is " +
                "supposed to be the only way into the game.");
        }

        [UnityTest]
        public IEnumerator AnsweringTheGate_OpensTheLobby()
        {
            yield return LoadBoot();
            ForceUnconfirmed();

            var enter = FindButton("TapToEnter");
            Assert.IsNotNull(enter, "The boot screen has no TAP TO ENTER button.");
            enter.onClick.Invoke();
            yield return null;

            var gate = Object.FindFirstObjectByType<AgeGateOverlay>();
            Assert.IsNotNull(gate, "The player tapped to enter and no age gate came up.");

            var confirm = FindButtonIn(gate, "ConfirmAge");
            Assert.IsNotNull(confirm, "The age gate has no way for the player to say " +
                                      "they are 18.");

            confirm.onClick.Invoke();
            yield return null;

            // The gate is only the first thing on the way in. The disclosure and the
            // offer come next for a player who has not seen them (plan §1.1, §1.3),
            // and the lobby waits behind those.
            yield return CompleteFirstRunSequence();

            yield return WaitForScene(LobbyScene);

            Assert.AreEqual(LobbyScene, SceneManager.GetActiveScene().name,
                "The player confirmed they are 18 and the lobby never opened. The gate is " +
                "supposed to let them in, not strand them on the loading screen.");

            Assert.IsTrue(AgeGate.IsConfirmed,
                "The player answered the gate and the answer was not kept, so they will " +
                "be asked the same question on every launch.");
        }

        /// <summary>
        /// Answers the disclosure and the offer if they are still outstanding.
        ///
        /// A profile that has already seen them skips straight through, so this works
        /// whatever state the player's save is in.
        /// </summary>
        private static IEnumerator CompleteFirstRunSequence()
        {
            for (int i = 0; i < 60; i++)
            {
                var disclosure = Object.FindFirstObjectByType<LustHouse.Lobby.ProbabilityDisclosureSheet>();
                if (disclosure != null)
                {
                    var close = FindButtonIn(disclosure.gameObject, "Close");
                    if (close != null) close.onClick.Invoke();
                    yield return null;
                    continue;
                }

                var prompt = Object.FindFirstObjectByType<LustHouse.Lobby.FirstRunPackPrompt>();
                if (prompt != null)
                {
                    var take = FindButtonByLabel(prompt.gameObject, "TAKE IT");
                    if (take != null) take.onClick.Invoke();
                    yield return null;

                    var thanks = FindButtonByLabel(prompt.gameObject, "THANK YOU");
                    if (thanks != null) thanks.onClick.Invoke();
                    yield return null;
                    continue;
                }

                yield break;
            }
        }

        // ---- the lobby chip ----------------------------------------------------

        [UnityTest]
        public IEnumerator TheLobbysEighteenChip_ReopensTheGate()
        {
            yield return SceneManager.LoadSceneAsync(LobbyScene);
            yield return null;
            yield return null;

            var chip = FindButton("AgeChip");
            Assert.IsNotNull(chip,
                "The lobby has no 18+ chip, so the player cannot re-check their answer " +
                "without leaving the game.");

            chip.onClick.Invoke();
            yield return null;

            var gate = Object.FindFirstObjectByType<AgeGateOverlay>();
            Assert.IsNotNull(gate,
                "The player pressed the 18+ chip and no gate came up. The chip is " +
                "supposed to open the age gate.");

            var confirm = FindButtonIn(gate, "ConfirmAge");
            Assert.IsNotNull(confirm, "The age gate has no way for the player to say " +
                                      "they are 18.");

            confirm.onClick.Invoke();
            yield return null;

            Assert.IsFalse(gate.gameObject.activeInHierarchy,
                "The player answered the gate and it stayed on screen. The lobby has no " +
                "way back once the gate is up.");
        }

        // ---- helpers ------------------------------------------------------------

        /// <summary>
        /// Loads the boot screen with its waiting beats cut short.
        ///
        /// The boot screen is a waiting screen, not the thing under test, and it spends
        /// six seconds on a progress bar by design. The timings are set as the scene
        /// arrives, which is before the sequence's own Start, so the beats are short
        /// from the first frame rather than from the second one.
        /// </summary>
        private IEnumerator LoadBoot()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            yield return SceneManager.LoadSceneAsync(BootScene);
            SceneManager.sceneLoaded -= OnSceneLoaded;

            // Set them again in case the sequence started before the handler ran.
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

        /// <summary>Waits for the boot to offer the player a way in.</summary>
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

        /// <summary>Waits for a scene to finish loading, or gives up.</summary>
        private static IEnumerator WaitForScene(string sceneName, int maxFrames = 600)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (SceneManager.GetActiveScene().name == sceneName) yield break;
                yield return null;
            }
        }

        /// <summary>Records that the player has not answered the age question.</summary>
        private static void ForceUnconfirmed()
        {
            var progression = LustHouse.Economy.PlayerProgression.Instance;
            Assert.IsNotNull(progression, "There is no PlayerProgression, so the age " +
                                          "gate has nothing to read.");

            var state = progression.Capture();
            state.ageConfirmed = false;
            progression.RestoreFrom(state);

            Assert.IsFalse(AgeGate.IsConfirmed,
                "The player has not answered the age question, and the gate thinks they " +
                "have.");
        }

        private static Button FindButton(string name)
        {
            foreach (var button in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
                if (button.name == name) return button;
            return null;
        }

        private static Button FindButtonIn(AgeGateOverlay gate, string name)
        {
            if (gate == null) return null;

            foreach (var button in gate.GetComponentsInChildren<Button>(true))
                if (button.name == name) return button;
            return null;
        }

        /// <summary>Finds a button under any root, by name.</summary>
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
