using System.Collections;
using System.Collections.Generic;
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
    /// The music bed: the game screen's own bed, and the channel it lives on.
    ///
    /// Two things are worth protecting. The first is what the player asked for: the
    /// bed is there while the reels are turning, and it fades away when they are not.
    /// The second is the channel itself. The lobby's bed and the game's bed share one
    /// channel, and a room that unloads late must not take the next room's bed down
    /// with it — which is exactly what happens when a lobby hands over to a game.
    /// </summary>
    public class MusicBedTests
    {
        private const string SlotScene = "Slot";

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            AudioFeedback.Reset();
            SlotLaunchPayload.Payload = null;

            // Leave the channel quiet for whoever runs next.
            var service = AudioService.Instance;
            if (service != null)
            {
                service.StopMusic(AudioCue.SlotMusic);
                service.StopMusic(AudioCue.LobbyMusic);
            }

            yield return null;
        }

        // ---- the bed, in the room ----------------------------------------------

        [UnityTest]
        public IEnumerator TheRoomComesUpWithABed()
        {
            var recorder = new Recorder();
            AudioFeedback.SetOutput(recorder);

            yield return LoadSlot();

            Assert.IsTrue(recorder.music.Contains(AudioCue.SlotMusic),
                "The game screen came up with no bed. The room has to have a pulse the " +
                "moment the player is standing in it.");
        }

        [UnityTest]
        public IEnumerator TheBedFadesAwayWhenThePlayerIsIdle()
        {
            var recorder = new Recorder();
            AudioFeedback.SetOutput(recorder);

            yield return LoadSlot();

            var presenter = Object.FindFirstObjectByType<SlotPresenter>();
            Assert.IsNotNull(presenter, "The Slot scene has no SlotPresenter.");

            // The player walks up to the machine and does nothing with it.
            presenter.musicIdleSeconds = 0.2f;

            float deadline = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < deadline &&
                   !recorder.musicStopped.Contains(AudioCue.SlotMusic))
                yield return null;

            Assert.IsTrue(recorder.musicStopped.Contains(AudioCue.SlotMusic),
                "The player left the machine alone and the bed kept playing. A bed that " +
                "never fades makes the game feel like it is hurrying the player.");
        }

        [UnityTest]
        public IEnumerator TheReelsBringTheBedBack()
        {
            var recorder = new Recorder();
            AudioFeedback.SetOutput(recorder);

            yield return LoadSlot();

            var presenter = Object.FindFirstObjectByType<SlotPresenter>();
            Assert.IsNotNull(presenter, "The Slot scene has no SlotPresenter.");

            // Let the bed fade away first, so there is something to bring back.
            presenter.musicIdleSeconds = 0.2f;

            float deadline = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < deadline &&
                   !recorder.musicStopped.Contains(AudioCue.SlotMusic))
                yield return null;

            Assert.IsTrue(recorder.musicStopped.Contains(AudioCue.SlotMusic),
                "The bed never faded, so there is nothing to bring back.");

            int before = recorder.music.Count;

            Fund();

            var spin = FindButton("Spin");
            Assert.IsNotNull(spin, "The slot screen has no spin button.");
            spin.onClick.Invoke();
            yield return null;

            Assert.IsTrue(recorder.music.Count > before,
                "The reels turned and the bed did not come back. The player spins, the " +
                "bed fades, the player spins again — and the machine is silent while it " +
                "is being played.");

            yield return WaitForSpin(presenter);
        }

        // ---- the channel -------------------------------------------------------

        [UnityTest]
        public IEnumerator TheLobbyBedIsNotTakenDownByTheGamesBed()
        {
            var service = LiveService();
            var music = MusicSource(service);
            Assert.IsNotNull(music, "The audio service has no music channel.");

            service.PlayMusic(AudioCue.LobbyMusic);
            yield return null;

            var lobbyClip = music.clip;
            Assert.IsNotNull(lobbyClip, "The lobby's bed did not start.");

            service.PlayMusic(AudioCue.SlotMusic);
            yield return null;

            Assert.IsTrue(music.isPlaying, "The game's bed did not take the channel.");
            Assert.AreNotEqual(lobbyClip, music.clip,
                "The game's bed did not replace the lobby's on the channel.");

            // The lobby's teardown asks for its own bed to be taken down. The game is
            // already playing by then, so the game's bed has to survive it.
            service.StopMusic(AudioCue.LobbyMusic);

            float deadline = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < deadline) yield return null;

            Assert.IsTrue(music.isPlaying,
                "The lobby took the game's bed down with it. The music channel outlives " +
                "the scene, so a late stop from the old room silences the new one.");
        }

        [UnityTest]
        public IEnumerator ABedThatIsFadingOutComesBackWhenItIsAskedForAgain()
        {
            var service = LiveService();
            var music = MusicSource(service);
            Assert.IsNotNull(music, "The audio service has no music channel.");

            float originalFade = service.musicFadeSeconds;
            float originalVolume = service.musicVolume;
            service.musicFadeSeconds = 0.2f;
            service.musicVolume = 1f;
            service.ApplyVolumes();

            try
            {
                service.PlayMusic(AudioCue.SlotMusic);
                yield return null;
                yield return null;

                service.StopMusic(AudioCue.SlotMusic);
                yield return null;

                // The player spins again before the bed has finished leaving.
                service.PlayMusic(AudioCue.SlotMusic);

                float deadline = Time.realtimeSinceStartup + 2f;
                while (Time.realtimeSinceStartup < deadline) yield return null;

                Assert.IsTrue(music.isPlaying,
                    "The bed was asked for again while it was fading out, and it was " +
                    "stopped anyway. The player spins, the bed fades, the player spins " +
                    "again — and the machine goes quiet in the middle of the round.");

                Assert.Greater(music.volume, 0.05f,
                    "The bed was asked for again while it was fading out, and it stayed " +
                    "silent. A bed that has been asked for must be heard.");
            }
            finally
            {
                service.musicFadeSeconds = originalFade;
                service.musicVolume = originalVolume;
                service.ApplyVolumes();
            }
        }

        [UnityTest]
        public IEnumerator TheBedTakesUpTheGamesOwnThemeWhenTheBankArrives()
        {
            var service = LiveService();
            var music = MusicSource(service);
            Assert.IsNotNull(music, "The audio service has no music channel.");

            // The room is built and asks for its bed before the definition has been
            // read, so what comes up first is the house's bed.
            service.SetBank(null);
            service.PlayMusic(AudioCue.SlotMusic);
            yield return null;

            Assert.IsNotNull(music.clip, "The bed did not start.");

            // The definition arrives, and with it the game's own theme.
            var theme = AudioClip.Create("theme", 64, 1, 44100, false);
            var bank = ScriptableObject.CreateInstance<AudioBank>();
            bank.entries.Add(new AudioBank.Entry
            {
                cue = AudioCue.SlotMusic,
                clip = theme,
                volume = 0.5f
            });

            try
            {
                service.SetBank(bank);
                yield return null;

                Assert.AreSame(theme, music.clip,
                    "The game's own theme was not taken up. The room asks for its music " +
                    "before the game says what its music is, so the bed has to be " +
                    "resolved again when the bank arrives — otherwise a title that " +
                    "brings its own theme plays the house bed for the whole game.");
            }
            finally
            {
                service.SetBank(null);
                Object.Destroy(theme);
                Object.Destroy(bank);
            }
        }

        // ---- helpers ------------------------------------------------------------

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
            def.id = "music_fixture";
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
                "music bed test");
        }

        private static IEnumerator WaitForSpin(SlotPresenter presenter, int maxFrames = 1200)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (!presenter.IsSpinning) yield break;
                yield return null;
            }
        }

        private static AudioService LiveService()
        {
            Core.ServicesBootstrap.EnsureExists();

            var service = AudioService.Instance;
            Assert.IsNotNull(service, "There is no AudioService.");
            return service;
        }

        /// <summary>The music channel's source. The service keeps it under a known name.</summary>
        private static AudioSource MusicSource(AudioService service)
        {
            if (service == null) return null;

            foreach (var source in service.GetComponentsInChildren<AudioSource>(true))
                if (source.name == "Music") return source;
            return null;
        }

        /// <summary>Records the cues instead of playing them.</summary>
        private sealed class Recorder : IAudioOutput
        {
            public readonly List<AudioCue> played = new List<AudioCue>();
            public readonly List<AudioCue> loops = new List<AudioCue>();
            public readonly List<AudioCue> stopped = new List<AudioCue>();
            public readonly List<AudioCue> music = new List<AudioCue>();
            public readonly List<AudioCue> musicStopped = new List<AudioCue>();

            public void Play(AudioCue cue, float pitch = 1f) => played.Add(cue);
            public void PlayLoop(AudioCue cue) => loops.Add(cue);
            public void StopLoop(AudioCue cue) => stopped.Add(cue);
            public void PlayMusic(AudioCue cue) => music.Add(cue);
            public void StopMusic(AudioCue cue) => musicStopped.Add(cue);
        }
    }
}
