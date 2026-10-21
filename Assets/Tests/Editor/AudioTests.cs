using System;
using System.Collections.Generic;
using System.IO;
using LustHouse.Audio;
using LustHouse.Core;
using LustHouse.Slots;
using NUnit.Framework;
using UnityEngine;

namespace LustHouse.Tests
{
    /// <summary>
    /// The sound system: the sounds themselves, the mix, and the mapping from a game
    /// event to what the player hears.
    ///
    /// The two properties worth protecting are the ones a listener would notice
    /// first: a loop that clicks once a revolution, and a sound that is different every
    /// time the game is launched. Both are checked here rather than trusted.
    /// </summary>
    public class AudioTests
    {
        private readonly List<AudioCue> _oneShots = new List<AudioCue>
        {
            AudioCue.UiClick,
            AudioCue.SpinPress,
            AudioCue.GameWelcome,
            AudioCue.ReelStop,
            AudioCue.WinSmall,
            AudioCue.WinMedium,
            AudioCue.WinBig,
            AudioCue.BonusTrigger,
        };

        [TearDown]
        public void TearDown() => AudioFeedback.Reset();

        // ---- the sounds ------------------------------------------------------

        [Test]
        public void EveryCue_MakesAClip()
        {
            foreach (AudioCue cue in Enum.GetValues(typeof(AudioCue)))
            {
                if (cue == AudioCue.None) continue;

                var clip = ProceduralAudio.ClipFor(cue);
                Assert.IsNotNull(clip, $"'{cue}' made no clip.");
                Assert.Greater(clip.samples, 0, $"'{cue}' has no samples.");
                Assert.Greater(clip.length, 0f, $"'{cue}' has no length.");
            }
        }

        [Test]
        public void TheReelLoopHasNoSeam()
        {
            // The reel sound loops for as long as the reels turn. A loop whose end
            // does not meet its start clicks once a revolution, and a click once a
            // revolution is worse than no sound at all.
            var clip = ProceduralAudio.ClipFor(AudioCue.ReelSpinLoop);
            Assert.IsNotNull(clip, "There is no reel loop.");

            var data = new float[clip.samples];
            clip.GetData(data, 0);
            Assert.Greater(data.Length, 2, "The reel loop is too short to loop.");

            float seam = Mathf.Abs(data[0] - data[data.Length - 1]);
            Assert.Less(seam, 0.15f,
                "The reel loop does not join up with itself. It will click once every " +
                "revolution — " + seam.ToString("F3") + " of a jump at the seam.");
        }

        [Test]
        public void NoSoundEndsOnAStep()
        {
            // A sound cut off while it is still loud ends on a step, and a step is a
            // click. Every one-shot has to arrive at silence.
            foreach (AudioCue cue in _oneShots)
            {
                var clip = ProceduralAudio.ClipFor(cue);
                Assert.IsNotNull(clip, $"'{cue}' made no clip.");

                var data = new float[clip.samples];
                clip.GetData(data, 0);
                Assert.Greater(data.Length, 2, $"'{cue}' is too short to be a sound.");

                float step = Mathf.Abs(data[data.Length - 1] - data[data.Length - 2]);
                Assert.Less(step, 0.05f,
                    $"'{cue}' ends on a step of {step:F3}. It will click as it stops.");
            }
        }

        [Test]
        public void TheSoundsAreTheSameEveryTime()
        {
            // A sound that changes from launch to launch cannot be tested and cannot
            // be reported by QA. The noise is generated from a fixed seed, never from
            // the shared random state.
            var first = ProceduralAudio.ClipFor(AudioCue.ReelSpinLoop);
            var firstData = new float[first.samples];
            first.GetData(firstData, 0);

            ProceduralAudio.Clear();

            var second = ProceduralAudio.ClipFor(AudioCue.ReelSpinLoop);
            var secondData = new float[second.samples];
            second.GetData(secondData, 0);

            Assert.AreEqual(firstData.Length, secondData.Length,
                "The reel loop came out a different length the second time.");

            for (int i = 0; i < firstData.Length; i++)
            {
                Assert.AreEqual(firstData[i], secondData[i],
                    $"The reel loop is not reproducible: sample {i} differs between runs.");
            }
        }

        // ---- the mix ---------------------------------------------------------

        [Test]
        public void TheVolumesCompose()
        {
            Assert.AreEqual(1f, AudioService.EffectiveVolume(1f, 1f, false), 0.001f,
                "Full volume on both buses should be full volume.");
            Assert.AreEqual(0.5f, AudioService.EffectiveVolume(0.5f, 1f, false), 0.001f,
                "Halving the master should halve the sound.");
            Assert.AreEqual(0.25f, AudioService.EffectiveVolume(0.5f, 0.5f, false), 0.001f,
                "Both buses apply to the sound.");
        }

        [Test]
        public void MuteSilencesEverything()
        {
            Assert.AreEqual(0f, AudioService.EffectiveVolume(1f, 1f, true), 0.001f,
                "A muted game must make no sound at all.");
            Assert.AreEqual(0f, AudioService.EffectiveVolume(0f, 1f, false), 0.001f,
                "A silent master must make no sound at all.");
        }

        [Test]
        public void TheVolumesAreClamped()
        {
            Assert.AreEqual(1f, AudioService.EffectiveVolume(4f, 1f, false), 0.001f,
                "A volume over one must be clamped, not amplified past full scale.");
            Assert.AreEqual(0f, AudioService.EffectiveVolume(-1f, 1f, false), 0.001f,
                "A negative volume must be clamped to silence.");
        }

        // ---- the save --------------------------------------------------------

        [Test]
        public void TheSaveRoundTripsTheVolumes()
        {
            // The volumes were in the save shape from the start and nothing ever wrote
            // them, so every launch threw them away.
            string dir = Path.Combine(Path.GetTempPath(),
                "lusthouse_audio_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            try
            {
                var save = new SaveSystem(Path.Combine(dir, "save.json"));

                var doc = SaveDocument.CreateNew(new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc));
                doc.settings.masterVolume = 0.4f;
                doc.settings.musicVolume = 0.6f;
                doc.settings.sfxVolume = 0.8f;
                save.Save(doc);

                var loaded = save.Load();
                Assert.IsTrue(loaded.ok, "The save did not load: " + loaded.warning);
                Assert.AreEqual(0.4f, loaded.document.settings.masterVolume, 0.001f);
                Assert.AreEqual(0.6f, loaded.document.settings.musicVolume, 0.001f);
                Assert.AreEqual(0.8f, loaded.document.settings.sfxVolume, 0.001f);
            }
            finally
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
                catch { /* best effort */ }
            }
        }

        // ---- what a game event sounds like -----------------------------------

        [Test]
        public void ALosingSpin_MakesNoWinSound()
        {
            var recorder = new Recorder();
            AudioFeedback.SetOutput(recorder);

            SlotAudio.WinResolved(new SpinResult { totalWin = 0, bet = 1000 }, null);

            Assert.IsFalse(recorder.Played(AudioCue.WinSmall));
            Assert.IsFalse(recorder.Played(AudioCue.WinMedium));
            Assert.IsFalse(recorder.Played(AudioCue.WinBig));
        }

        [Test]
        public void ASmallWin_MakesTheSmallSound()
        {
            var recorder = new Recorder();
            AudioFeedback.SetOutput(recorder);

            SlotAudio.WinResolved(new SpinResult { totalWin = 5000, bet = 1000 }, null);

            Assert.IsTrue(recorder.Played(AudioCue.WinSmall),
                "A five times bet win should make the small win sound.");
        }

        [Test]
        public void AMiddleWin_MakesTheMiddleSound()
        {
            var recorder = new Recorder();
            AudioFeedback.SetOutput(recorder);

            SlotAudio.WinResolved(new SpinResult { totalWin = 50_000, bet = 1000 }, null);

            Assert.IsTrue(recorder.Played(AudioCue.WinMedium),
                "A fifty times bet win should make the middle win sound.");
        }

        [Test]
        public void ABigWin_MakesTheBigSound()
        {
            var recorder = new Recorder();
            AudioFeedback.SetOutput(recorder);

            SlotAudio.WinResolved(new SpinResult { totalWin = 500_000, bet = 1000 }, null);

            Assert.IsTrue(recorder.Played(AudioCue.WinBig),
                "A five hundred times bet win should make the big win sound.");
        }

        [Test]
        public void TheWinTierComesFromTheGame()
        {
            // The celebration thresholds are the game's own data. A game that
            // celebrates differently has to get different sounds without this file
            // knowing anything about it.
            var def = ScriptableObject.CreateInstance<SlotMachineDefinition>();
            def.id = "tier_fixture";
            def.winCelebrationThresholds = new long[] { 1, 2, 4, 8, 16 };

            try
            {
                Assert.AreEqual(AudioCue.WinSmall,
                    SlotAudio.WinCueFor(new SpinResult { totalWin = 500, bet = 1000 }, def),
                    "A win under the first threshold is a small win.");

                Assert.AreEqual(AudioCue.WinMedium,
                    SlotAudio.WinCueFor(new SpinResult { totalWin = 3000, bet = 1000 }, def),
                    "A win between the first and the third threshold is a middle win.");

                Assert.AreEqual(AudioCue.WinBig,
                    SlotAudio.WinCueFor(new SpinResult { totalWin = 10_000, bet = 1000 }, def),
                    "A win over the third threshold is a big win.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(def);
            }
        }

        [Test]
        public void AFeatureTrigger_MakesTheBonusSound()
        {
            var recorder = new Recorder();
            AudioFeedback.SetOutput(recorder);

            SlotAudio.FeatureTriggered(new SpinResult { triggeredBonus = true });

            Assert.IsTrue(recorder.Played(AudioCue.BonusTrigger),
                "A bonus round that starts should be announced.");
        }

        [Test]
        public void AnOrdinarySpin_MakesNoBonusSound()
        {
            var recorder = new Recorder();
            AudioFeedback.SetOutput(recorder);

            SlotAudio.FeatureTriggered(new SpinResult { totalWin = 5000, bet = 1000 });

            Assert.IsFalse(recorder.Played(AudioCue.BonusTrigger),
                "A spin with no feature in it should not announce a bonus.");
        }

        [Test]
        public void TheReelsFallInPitchAsTheyLand()
        {
            // A run of identical knocks is a run of identical knocks. Dropping the
            // pitch a little each time turns it into a descending run.
            var recorder = new Recorder();
            AudioFeedback.SetOutput(recorder);

            SlotAudio.ReelStopped(0);
            SlotAudio.ReelStopped(3);

            Assert.AreEqual(2, recorder.pitches.Count);
            Assert.Greater(recorder.pitches[0], recorder.pitches[1],
                "Each reel should land a little lower than the one before it.");
        }

        [Test]
        public void TheSpinLoopStartsAndStops()
        {
            var recorder = new Recorder();
            AudioFeedback.SetOutput(recorder);

            SlotAudio.SpinStarted();
            Assert.IsTrue(recorder.loops.Contains(AudioCue.ReelSpinLoop),
                "The reels turned and nothing sounded.");

            SlotAudio.SpinEnded();
            Assert.IsTrue(recorder.stopped.Contains(AudioCue.ReelSpinLoop),
                "The reels stopped and the loop kept going.");
        }

        /// <summary>Records the cues instead of playing them.</summary>
        private sealed class Recorder : IAudioOutput
        {
            public readonly List<AudioCue> played = new List<AudioCue>();
            public readonly List<AudioCue> loops = new List<AudioCue>();
            public readonly List<AudioCue> stopped = new List<AudioCue>();
            public readonly List<float> pitches = new List<float>();

            public void Play(AudioCue cue, float pitch = 1f)
            {
                played.Add(cue);
                pitches.Add(pitch);
            }

            public void PlayLoop(AudioCue cue) => loops.Add(cue);
            public void StopLoop(AudioCue cue) => stopped.Add(cue);

            public bool Played(AudioCue cue) => played.Contains(cue);
        }
    }
}
