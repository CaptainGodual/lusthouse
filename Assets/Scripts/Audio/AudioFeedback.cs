using UnityEngine;
using UnityEngine.UI;

namespace LustHouse.Audio
{
    /// <summary>
    /// The one place sound leaves the game from.
    ///
    /// Everything that wants to make a noise calls here, and this decides where the
    /// noise goes: the live audio service if there is one, a test's recorder if a test
    /// has put one in, and silence if there is neither.
    ///
    /// The indirection is not ceremony. The slot presenter has to be able to announce a
    /// win without knowing how a sound is made, and a test has to be able to assert
    /// "the player was told they won" without a speaker attached.
    /// </summary>
    public static class AudioFeedback
    {
        private static IAudioOutput _override;

        /// <summary>
        /// Where cues go. A test's override wins; otherwise the live service; otherwise
        /// silence.
        /// </summary>
        public static IAudioOutput Output =>
            _override ?? (IAudioOutput)AudioService.Instance ?? NullAudioOutput.Instance;

        /// <summary>Sends every cue to a recorder instead of to the speakers.</summary>
        public static void SetOutput(IAudioOutput output) => _override = output;

        /// <summary>Goes back to the live service.</summary>
        public static void Reset() => _override = null;

        /// <summary>Plays a one-shot cue.</summary>
        public static void Play(AudioCue cue, float pitch = 1f)
        {
            if (cue == AudioCue.None) return;
            Output.Play(cue, pitch);
        }

        /// <summary>Starts a looping cue.</summary>
        public static void PlayLoop(AudioCue cue)
        {
            if (cue == AudioCue.None) return;
            Output.PlayLoop(cue);
        }

        /// <summary>Stops a looping cue.</summary>
        public static void StopLoop(AudioCue cue)
        {
            if (cue == AudioCue.None) return;
            Output.StopLoop(cue);
        }

        /// <summary>
        /// Gives a button a click sound.
        ///
        /// The cue is read when the button is pressed, not when it is wired, so the
        /// same button can be given a different sound later without rewiring it — the
        /// SPIN button asks for <see cref="AudioCue.SpinPress"/> after the generic pass
        /// has already given it a click.
        /// </summary>
        public static void AttachClickSound(Button button, AudioCue cue = AudioCue.UiClick)
        {
            if (button == null) return;

            var sound = button.GetComponent<ButtonClickSound>();
            if (sound == null) sound = button.gameObject.AddComponent<ButtonClickSound>();
            sound.cue = cue;
        }
    }
}
