using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using LustHouse.Audio;
using LustHouse.Slots;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace LustHouse.Tests
{
    /// <summary>
    /// The sound of a game, end to end: the scene has a listener, the reels make a
    /// sound while they turn and a sound when they land, the spin button says
    /// something, and none of it keeps playing after the spin is over.
    ///
    /// These are the seams an edit mode test cannot reach. Every component here can
    /// pass on its own while the game is silent — a scene with no audio listener
    /// plays nothing at all and looks perfectly correct while it does it.
    /// </summary>
    public class SlotAudioTests
    {
        private const string SlotScene = "Slot";
        private const string LobbyScene = "Lobby";
        private const string BootScene = "BootLoader";

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            AudioFeedback.Reset();
            SlotLaunchPayload.Payload = null;
            yield return null;
        }

        /// <summary>Loads the slot scene with a definition, the way the lobby hands one over.</summary>
        private static IEnumerator LoadSlot()
        {
            SlotLaunchPayload.Payload = BuildDefinition();

            yield return SceneManager.LoadSceneAsync(SlotScene);
            yield return null;
            yield return null;
        }

        private static SlotMachineDefinition BuildDefinition()
        {
            var def = ScriptableObject.CreateInstance<SlotMachineDefinition>();
            def.id = "audio_fixture";
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
                "slot audio test");
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
        public IEnumerator EverySceneHasAnAudioListener()
        {
            // Without a listener nothing plays, and nothing looks wrong. The cameras in
            // this project are built in code, and a camera built in code is not given
            // one, so this is a real hole rather than a theoretical one.
            foreach (var sceneName in new[] { SlotScene, LobbyScene, BootScene })
            {
                yield return SceneManager.LoadSceneAsync(sceneName);
                yield return null;
                yield return null;

                var listener = Object.FindFirstObjectByType<AudioListener>();
                Assert.IsNotNull(listener,
                    $"'{sceneName}' has no AudioListener, so the game is completely silent " +
                    "there while looking perfectly normal.");
            }
        }

        [UnityTest]
        public IEnumerator ASpin_PlaysTheLoopAndOneStopPerReel()
        {
            yield return LoadSlot();

            var recorder = new Recorder();
            AudioFeedback.SetOutput(recorder);

            var presenter = Object.FindFirstObjectByType<SlotPresenter>();
            Assert.IsNotNull(presenter, "The Slot scene has no SlotPresenter.");

            Fund();

            var spin = FindButton("Spin");
            Assert.IsNotNull(spin, "The slot screen has no spin button.");

            spin.onClick.Invoke();
            yield return null;

            Assert.IsTrue(presenter.IsSpinning, "The spin did not start.");

            yield return WaitForSpin(presenter);

            Assert.IsFalse(presenter.IsSpinning, "The spin never finished.");

            Assert.IsTrue(recorder.loops.Contains(AudioCue.ReelSpinLoop),
                "The reels turned and nothing sounded.");

            Assert.AreEqual(5, recorder.TimesPlayed(AudioCue.ReelStop),
                "Each reel should make one sound as it lands, and no more.");

            Assert.IsTrue(recorder.stopped.Contains(AudioCue.ReelSpinLoop),
                "The reels stopped and the loop kept playing.");
        }

        [UnityTest]
        public IEnumerator TheSpinButtonMakesItsOwnSound()
        {
            yield return LoadSlot();

            var recorder = new Recorder();
            AudioFeedback.SetOutput(recorder);

            Fund();

            var spin = FindButton("Spin");
            Assert.IsNotNull(spin, "The slot screen has no spin button.");

            spin.onClick.Invoke();
            yield return null;

            Assert.IsTrue(recorder.Played(AudioCue.SpinPress),
                "The spin button made no sound. It is the one press that spends the " +
                "player's money, and it should not sound like tapping a label.");
        }

        [UnityTest]
        public IEnumerator TheWatchdog_StopsTheLoop()
        {
            yield return LoadSlot();

            var recorder = new Recorder();
            AudioFeedback.SetOutput(recorder);

            var presenter = Object.FindFirstObjectByType<SlotPresenter>();
            Assert.IsNotNull(presenter, "The Slot scene has no SlotPresenter.");

            presenter.spinWatchdogSeconds = 0.05f;

            Fund();

            var spin = FindButton("Spin");
            Assert.IsNotNull(spin, "The slot screen has no spin button.");

            // The watchdog says what happened.
            LogAssert.Expect(LogType.Error, new Regex("A spin ran for"));

            spin.onClick.Invoke();
            yield return null;

            yield return WaitForSpin(presenter);

            Assert.IsTrue(recorder.stopped.Contains(AudioCue.ReelSpinLoop),
                "A spin the watchdog gave up on left the reel sound playing over a " +
                "machine that has stopped.");
        }

        /// <summary>Records the cues instead of playing them.</summary>
        private sealed class Recorder : IAudioOutput
        {
            public readonly List<AudioCue> played = new List<AudioCue>();
            public readonly List<AudioCue> loops = new List<AudioCue>();
            public readonly List<AudioCue> stopped = new List<AudioCue>();

            public void Play(AudioCue cue, float pitch = 1f) => played.Add(cue);
            public void PlayLoop(AudioCue cue) => loops.Add(cue);
            public void StopLoop(AudioCue cue) => stopped.Add(cue);

            public readonly List<AudioCue> music = new List<AudioCue>();
            public readonly List<AudioCue> musicStopped = new List<AudioCue>();

            public void PlayMusic(AudioCue cue) => music.Add(cue);
            public void StopMusic(AudioCue cue) => musicStopped.Add(cue);

            public bool Played(AudioCue cue) => played.Contains(cue);

            public int TimesPlayed(AudioCue cue)
            {
                int count = 0;
                for (int i = 0; i < played.Count; i++)
                    if (played[i] == cue) count++;
                return count;
            }
        }
    }
}
