namespace LustHouse.Audio
{
    /// <summary>
    /// Where a sound goes out to.
    ///
    /// Gameplay code talks to this and never to an <c>AudioSource</c>. That keeps the
    /// slot maths and the presentation independent of the sound system, and it lets a
    /// test replace the output with a recorder and assert on what the player was told
    /// they heard, without a speaker, an audio listener, or a play mode session.
    /// </summary>
    public interface IAudioOutput
    {
        /// <summary>Plays a one-shot. A pitch of 1 plays the clip as authored.</summary>
        void Play(AudioCue cue, float pitch = 1f);

        /// <summary>Starts a looping cue. Starting a loop that is already running is a
        /// no-op rather than a restart — a loop that restarts on every call stutters.</summary>
        void PlayLoop(AudioCue cue);

        /// <summary>Stops a looping cue, with a short fade so it does not click off.</summary>
        void StopLoop(AudioCue cue);
    }

    /// <summary>
    /// The output that plays nothing.
    ///
    /// Used whenever there is no audio service: a scene opened on its own for QA, an
    /// edit mode test, a headless build. Silently doing nothing is the right failure
    /// here — a missing sound is a missing sound, and it must never be an exception in
    /// the middle of a spin.
    /// </summary>
    public sealed class NullAudioOutput : IAudioOutput
    {
        public static readonly NullAudioOutput Instance = new NullAudioOutput();

        public void Play(AudioCue cue, float pitch = 1f) { }
        public void PlayLoop(AudioCue cue) { }
        public void StopLoop(AudioCue cue) { }
    }
}
