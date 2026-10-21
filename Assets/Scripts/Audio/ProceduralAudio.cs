using System.Collections.Generic;
using UnityEngine;

namespace LustHouse.Audio
{
    /// <summary>
    /// Makes every sound the game has, out of maths.
    ///
    /// The project ships no audio files, and the UI is built the same way — every
    /// sprite in <c>UiFactory</c> is generated at runtime rather than imported. The
    /// sounds are generated the same way: a click is a short decaying sine, a reel
    /// stop is a damped thud, a win is a bell.
    ///
    /// Two rules make the output trustworthy:
    ///
    ///   * **It is deterministic.** The noise comes from a fixed-seed generator, never
    ///     from <c>UnityEngine.Random</c>, which is shared global state and cannot be
    ///     replayed (guardrail 5). The same build always produces the same sounds, so
    ///     a QA report about a sound is a report about *that* sound.
    ///   * **The loop has no seam.** The reel sound loops for as long as the reels
    ///     turn, and a loop that clicks once a revolution is worse than no loop at
    ///     all. It is built out of whole cycles of whole frequencies, so the end of the
    ///     buffer meets the start of it exactly.
    ///
    /// Real recorded audio replaces any of this through an <see cref="AudioBank"/>
    /// without changing a line of the game.
    /// </summary>
    public static class ProceduralAudio
    {
        /// <summary>Sample rate of every generated clip.</summary>
        public const int SampleRate = 44100;

        /// <summary>How long a one-shot fades out at its end, in seconds. A sound cut
        /// off mid-decay ends on a step, and a step is a click.</summary>
        private const float TailFadeSeconds = 0.006f;

        private static readonly Dictionary<AudioCue, AudioClip> _clips = new Dictionary<AudioCue, AudioClip>();

        /// <summary>The clip for a cue, generated the first time it is asked for.</summary>
        public static AudioClip ClipFor(AudioCue cue)
        {
            if (cue == AudioCue.None) return null;
            if (_clips.TryGetValue(cue, out var clip) && clip != null) return clip;

            clip = Build(cue);
            if (clip != null) _clips[cue] = clip;
            return clip;
        }

        /// <summary>
        /// Generates every clip up front.
        ///
        /// Called during boot, so the very first button press does not pay for the
        /// synthesis of its own click.
        /// </summary>
        public static void WarmUp()
        {
            foreach (AudioCue cue in System.Enum.GetValues(typeof(AudioCue)))
                if (cue != AudioCue.None) ClipFor(cue);
        }

        /// <summary>Drops the cached clips. Editor use.</summary>
        public static void Clear() => _clips.Clear();

        // ---- construction ---------------------------------------------------

        private static AudioClip Build(AudioCue cue)
        {
            float[] samples;
            switch (cue)
            {
                case AudioCue.UiClick: samples = BuildClick(); break;
                case AudioCue.SpinPress: samples = BuildSpinPress(); break;
                case AudioCue.GameWelcome: samples = BuildGameWelcome(); break;
                case AudioCue.ReelSpinLoop: samples = BuildSpinLoop(); break;
                case AudioCue.ReelStop: samples = BuildReelStop(); break;
                case AudioCue.WinSmall: samples = BuildWinSmall(); break;
                case AudioCue.WinMedium: samples = BuildWinMedium(); break;
                case AudioCue.WinBig: samples = BuildWinBig(); break;
                case AudioCue.BonusTrigger: samples = BuildBonusTrigger(); break;
                case AudioCue.LobbyMusic: samples = BuildLobbyMusic(); break;
                default: return null;
            }

            // The loops are left exactly as built: a fade at their end would put a
            // hole in them, and the whole point of them is that they join up with
            // themselves.
            if (cue != AudioCue.ReelSpinLoop && cue != AudioCue.LobbyMusic)
                ApplyTailFade(samples);

            return MakeClip(cue, samples);
        }

        private static AudioClip MakeClip(AudioCue cue, float[] samples)
        {
            float peak = 0f;
            for (int i = 0; i < samples.Length; i++)
                peak = Mathf.Max(peak, Mathf.Abs(samples[i]));

            // Normalised to just under full scale, so a loud cue is loud without ever
            // clipping into a crackle.
            if (peak > 0.0001f)
            {
                float gain = 0.9f / peak;
                for (int i = 0; i < samples.Length; i++) samples[i] *= gain;
            }

            var clip = AudioClip.Create(cue.ToString(), samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);

            // The clip is generated, not imported, so it is not an asset and must not
            // be reported as a leak when the editor leaves play mode.
            clip.hideFlags = HideFlags.HideAndDontSave;
            return clip;
        }

        // ---- the sounds ------------------------------------------------------

        /// <summary>A button: a short bright blip with a click of noise on top.</summary>
        private static float[] BuildClick()
        {
            var buffer = Buffer(0.04f);
            AddTone(buffer, 1200.0, 0.0, 0.04f, 0.45f, 70f);
            AddNoise(buffer, 0.0, 0.012f, 0.35f, 220f, 11u, 1f);
            return buffer;
        }

        /// <summary>
        /// The spin button: lower and heavier than a click, because this is the press
        /// that spends the player's money and it should feel like it.
        /// </summary>
        private static float[] BuildSpinPress()
        {
            var buffer = Buffer(0.09f);
            AddTone(buffer, 320.0, 0.0, 0.09f, 0.60f, 30f);
            AddTone(buffer, 160.0, 0.0, 0.09f, 0.35f, 22f);
            AddNoise(buffer, 0.0, 0.010f, 0.30f, 250f, 12u, 1f);
            return buffer;
        }

        /// <summary>
        /// A reel landing: a damped wooden knock. The low-passed noise is the knock
        /// itself; the tone underneath is the reel frame settling.
        /// </summary>
        private static float[] BuildReelStop()
        {
            var buffer = Buffer(0.14f);
            AddTone(buffer, 165.0, 0.0, 0.14f, 0.70f, 28f);
            AddNoise(buffer, 0.0, 0.05f, 0.50f, 55f, 13u, 0.35f);
            AddTone(buffer, 2400.0, 0.0, 0.02f, 0.20f, 180f);
            return buffer;
        }

        /// <summary>
        /// The reels turning: a low rumble with a rattle of knocks on top.
        ///
        /// Every frequency in here is a whole number of hertz, and the loop is a whole
        /// number of seconds, so every one of them completes a whole number of cycles
        /// before the buffer comes back round. The rattle is a train of sixteen knocks
        /// a eighth of a second apart, each one over and gone before the next starts.
        /// The result is periodic by construction, and a periodic buffer loops without
        /// a click.
        /// </summary>
        private static float[] BuildSpinLoop()
        {
            const float loopSeconds = 2f;
            var buffer = Buffer(loopSeconds);

            // The rumble: partials of a 40 Hz fundamental, falling away quickly.
            for (int k = 1; k <= 16; k++)
                AddSustained(buffer, 40.0 * k, 0.40f / Mathf.Pow(k, 1.3f));

            // The rattle: the strip ticking past the window. Each knock is over well
            // before the next one starts, and it dies away to nothing as it does, so
            // the train is periodic and each knock has no step at its end.
            for (int tick = 0; tick < 16; tick++)
                AddNoise(buffer, tick * 0.125, 0.06f, 0.22f, 90f, (uint)(101 + tick), 0.5f);

            // A slow swell — six whole cycles across the loop, so it is periodic too.
            for (int i = 0; i < buffer.Length; i++)
            {
                float t = i / (float)SampleRate;
                buffer[i] *= 0.78f + 0.22f * Mathf.Sin(2f * Mathf.PI * 3f * t);
            }

            return buffer;
        }

        /// <summary>A small win: two notes of a bell.</summary>
        private static float[] BuildWinSmall()
        {
            var buffer = Buffer(0.6f);
            AddBell(buffer, 659.25, 0.00f, 0.55f, 6.0f);    // E5
            AddBell(buffer, 987.77, 0.12f, 0.45f, 5.0f);    // B5
            AddNoise(buffer, 0.0, 0.03f, 0.12f, 90f, 21u, 0.8f);
            return buffer;
        }

        /// <summary>A middle win: a four-note run up the scale, with a shimmer on top.</summary>
        private static float[] BuildWinMedium()
        {
            var buffer = Buffer(1.0f);
            double[] notes = { 523.25, 659.25, 783.99, 1046.50 };   // C5 E5 G5 C6
            for (int i = 0; i < notes.Length; i++)
                AddBell(buffer, notes[i], i * 0.09f, 0.45f, 4.5f);

            AddTone(buffer, 2093.0, 0.10f, 0.85f, 0.07f, 3.0f);
            AddNoise(buffer, 0.0, 0.04f, 0.14f, 70f, 22u, 0.9f);
            return buffer;
        }

        /// <summary>A big win: a longer fanfare, and a tail that keeps ringing.</summary>
        private static float[] BuildWinBig()
        {
            var buffer = Buffer(1.8f);
            double[] notes = { 523.25, 659.25, 783.99, 1046.50, 1318.51, 1567.98 };
            for (int i = 0; i < notes.Length; i++)
                AddBell(buffer, notes[i], i * 0.12f, 0.50f, 3.2f);

            // The shimmer that stays up after the last note.
            AddTone(buffer, 2093.0, 0.30f, 1.45f, 0.06f, 2.0f);
            AddTone(buffer, 3136.0, 0.30f, 1.45f, 0.04f, 2.2f);
            AddNoise(buffer, 0.0, 0.06f, 0.15f, 60f, 31u, 0.9f);
            return buffer;
        }

        /// <summary>
        /// A feature round starting: a riser that tightens, then a chord landing on it.
        ///
        /// The riser is the point. The player has to be told something is happening
        /// before they know what it is.
        /// </summary>
        private static float[] BuildBonusTrigger()
        {
            var buffer = Buffer(1.4f);

            const float riseSeconds = 0.85f;
            int riseSamples = (int)(riseSeconds * SampleRate);

            // Noise that gets brighter as it climbs.
            var rng = new Rng(41u);
            float lowpass = 0f;
            for (int i = 0; i < riseSamples && i < buffer.Length; i++)
            {
                float t = i / (float)SampleRate;
                float climb = t / riseSeconds;
                lowpass += (rng.Next() - lowpass) * Mathf.Lerp(0.05f, 0.9f, climb);
                buffer[i] += 0.30f * (0.2f + 0.8f * climb * climb) * Attack(t) * lowpass;
            }

            // A tone rising under it.
            for (int i = 0; i < riseSamples && i < buffer.Length; i++)
            {
                float t = i / (float)SampleRate;
                float climb = t / riseSeconds;
                float frequency = Mathf.Lerp(220f, 1320f, climb);
                buffer[i] += 0.18f * (0.25f + 0.75f * climb) * Attack(t) *
                             Mathf.Sin(2f * Mathf.PI * frequency * t);
            }

            // The landing: a chord.
            AddBell(buffer, 523.25, 0.85f, 0.50f, 3.0f);
            AddBell(buffer, 659.25, 0.85f, 0.45f, 3.0f);
            AddBell(buffer, 783.99, 0.85f, 0.45f, 3.0f);
            AddBell(buffer, 1046.50, 0.85f, 0.40f, 3.2f);

            // The riser is faded into the chord rather than cut off. A riser that
            // stops dead at full volume ends on a step, and the step is a click
            // right on the beat the player is meant to hear the chord land on.
            const int fadeSamples = (int)(0.08f * SampleRate);
            for (int i = riseSamples - fadeSamples; i < riseSamples; i++)
            {
                if (i < 0 || i >= buffer.Length) continue;
                float t = (float)(i - (riseSamples - fadeSamples)) / fadeSamples;
                buffer[i] *= 1f - t;
            }

            return buffer;
        }

        /// <summary>
        /// The lobby's music bed: a short funk loop — kick, syncopated bass, clav-ish
        /// stabs, hats — that the recorded track (or any track on a bank) replaces.
        ///
        /// It obeys the same two rules as the reel loop. It is deterministic: every
        /// noise burst comes from a fixed seed. And it is seamless: the buffer is a
        /// whole number of seconds, the pad underneath is whole-number hertz so it
        /// comes back round to where it started, and every hit — kick, pluck, stab,
        /// hat — is over and gone before the buffer ends. A music bed that clicks
        /// every four bars is a music bed nobody can listen to.
        /// </summary>
        private static float[] BuildLobbyMusic()
        {
            const float loopSeconds = 4f;               // two bars of 4/4 at 120 bpm
            var buffer = Buffer(loopSeconds);

            // The pad: whole-number hertz so it is periodic across the loop. F2 and
            // C3, felt more than heard, holding the room together under the hits.
            AddSustained(buffer, 87.0, 0.05f);
            AddSustained(buffer, 131.0, 0.035f);

            // The kick: low and short, on the beat with a couple of off-beat pushes
            // so it grooves rather than marches.
            double[] kicks = { 0.0, 0.75, 1.5, 2.0, 2.75, 3.5 };
            foreach (double at in kicks)
            {
                AddTone(buffer, 58.0, at, 0.18f, 0.85f, 26f);
                AddNoise(buffer, at, 0.012f, 0.30f, 200f, 7u, 1f);
            }

            // The bass: a syncopated line in F minor pentatonic. Each pluck decays to
            // nothing well inside its window, so none of them straddles the seam.
            double[] bassNotes =
            {
                0.0, 0.25, 0.75, 1.25, 1.5,           // bar one
                2.0, 2.25, 2.75, 3.25, 3.5,           // bar two
            };
            double[] bassTones = { 87.31, 87.31, 130.81, 103.83, 87.31,
                                   87.31, 87.31, 155.56, 130.81, 87.31 };
            for (int i = 0; i < bassNotes.Length; i++)
            {
                AddTone(buffer, bassTones[i], bassNotes[i], 0.30f, 0.42f, 14f);
                AddTone(buffer, bassTones[i] * 2.0, bassNotes[i], 0.12f, 0.12f, 26f);
            }

            // The stabs: an Fm7 chord on the off-beats, bright and quick, like a clav
            // with the lid on it. Higher and shorter than the bass so the two never
            // mud each other.
            double[] stabs = { 0.375, 1.375, 2.375, 3.375 };
            foreach (double at in stabs)
            {
                AddTone(buffer, 349.23, at, 0.16f, 0.20f, 30f);   // F4
                AddTone(buffer, 415.30, at, 0.16f, 0.16f, 30f);   // Ab4
                AddTone(buffer, 523.25, at, 0.16f, 0.14f, 32f);   // C5
                AddTone(buffer, 622.25, at, 0.14f, 0.10f, 34f);   // Eb5
            }

            // The hats: a tick every eighth, louder on the beat. Over and gone in
            // thirty milliseconds each — the last one lands at 3.75s and is silent
            // long before the loop comes round.
            for (int tick = 0; tick < 16; tick++)
            {
                float at = tick * 0.25f;
                float amplitude = tick % 2 == 0 ? 0.16f : 0.09f;
                AddNoise(buffer, at, 0.030f, amplitude, 130f, (uint)(201 + tick), 1f);
            }

            return buffer;
        }

        /// <summary>
        /// A game opening: a slow chord swelling under a bell.
        ///
        /// The slow attack is the whole point. A welcome that arrives instantly reads
        /// as a notification; one that takes half a second to arrive reads as a room
        /// settling down around the player.
        /// </summary>
        private static float[] BuildGameWelcome()
        {
            var buffer = Buffer(2.0f);
            AddSwell(buffer, 261.63, 0.00f, 0.45f, 1.95f, 0.35f);   // C4
            AddSwell(buffer, 392.00, 0.05f, 0.45f, 1.90f, 0.30f);   // G4
            AddSwell(buffer, 523.25, 0.10f, 0.45f, 1.85f, 0.28f);   // C5
            AddSwell(buffer, 783.99, 0.15f, 0.45f, 1.80f, 0.16f);   // G5
            AddBell(buffer, 1046.50, 0.45f, 0.35f, 2.2f);
            AddBell(buffer, 1567.98, 0.50f, 0.22f, 2.4f);
            return buffer;
        }

        // ---- the parts -------------------------------------------------------

        private static float[] Buffer(float seconds) =>
            new float[Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate))];

        /// <summary>
        /// A decaying tone. <paramref name="decay"/> is how fast it dies: 6 leaves
        /// almost nothing after a second, 30 is over in a moment.
        /// </summary>
        private static void AddTone(float[] buffer, double frequency, double start,
                                    float duration, float amplitude, float decay)
        {
            int startSample = Mathf.Max(0, (int)(start * SampleRate));
            int count = Mathf.Min(buffer.Length - startSample, (int)(duration * SampleRate));
            if (count <= 0) return;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float envelope = Mathf.Exp(-decay * t) * Attack(t);
                buffer[startSample + i] += amplitude * envelope *
                    Mathf.Sin((float)(2.0 * Mathf.PI * frequency * t));
            }
        }

        /// <summary>
        /// A tone that holds for the whole buffer. Used only where the buffer is a
        /// loop, where the frequency is a whole number of hertz and the length is a
        /// whole number of seconds — so the tone comes back round to where it started.
        /// </summary>
        private static void AddSustained(float[] buffer, double frequency, float amplitude)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                float t = i / (float)SampleRate;
                buffer[i] += amplitude * Mathf.Sin((float)(2.0 * Mathf.PI * frequency * t));
            }
        }

        /// <summary>
        /// A bell: a tone plus two inharmonic partials. Real bells are not harmonic,
        /// and the extra partials are what make it read as metal rather than a beep.
        /// </summary>
        private static void AddBell(float[] buffer, double frequency, double start,
                                    float amplitude, float decay)
        {
            AddTone(buffer, frequency, start, 2.0f, amplitude, decay);
            AddTone(buffer, frequency * 2.76, start, 1.6f, amplitude * 0.50f, decay * 1.2f);
            AddTone(buffer, frequency * 5.40, start, 1.0f, amplitude * 0.25f, decay * 1.6f);
        }

        /// <summary>
        /// A swell: a tone that fades in over <paramref name="attack"/> and then decays
        /// away. The slow fade in is what makes it a swell rather than a note.
        ///
        /// The decay is worked out from the length rather than given, so the tone has
        /// faded to nothing by the time its window closes. A tone that stops while it
        /// is still loud ends on a step, and a step in the middle of a sound is a
        /// click — this is the one place the envelope has to be exactly right.
        /// </summary>
        private static void AddSwell(float[] buffer, double frequency, double start,
                                     float attack, float duration, float amplitude)
        {
            int startSample = Mathf.Max(0, (int)(start * SampleRate));
            int count = Mathf.Min(buffer.Length - startSample, (int)(duration * SampleRate));
            if (count <= 0) return;

            // Reaches about one percent of its height by the end of the window.
            float decayRate = 4.6f / Mathf.Max(0.05f, duration - attack);

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float fadeIn = Mathf.Clamp01((float)(t / Mathf.Max(0.001f, (float)attack)));
                float decay = Mathf.Exp(-decayRate * Mathf.Max(0f, t - (float)attack));
                buffer[startSample + i] += amplitude * fadeIn * decay *
                    Mathf.Sin((float)(2.0 * Mathf.PI * frequency * t));
            }
        }

        /// <summary>
        /// A burst of noise, low-passed by <paramref name="lowpass"/> (1 is raw noise,
        /// lower is duller) and decaying over the burst.
        /// </summary>
        private static void AddNoise(float[] buffer, double start, float duration,
                                     float amplitude, float decay, uint seed, float lowpass)
        {
            var rng = new Rng(seed);
            int startSample = Mathf.Max(0, (int)(start * SampleRate));
            int count = Mathf.Min(buffer.Length - startSample, (int)(duration * SampleRate));
            if (count <= 0) return;

            float filtered = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                filtered += (rng.Next() - filtered) * Mathf.Clamp01(lowpass);
                float envelope = Mathf.Exp(-decay * t) * Attack(t);
                buffer[startSample + i] += amplitude * envelope * filtered;
            }
        }

        /// <summary>
        /// A two millisecond fade in at the start of every sound.
        ///
        /// Without it a sound that starts at full amplitude begins on a step, and a
        /// step is a click — the exact thing the sounds are trying not to be.
        /// </summary>
        private static float Attack(float t) => Mathf.Clamp01(t / 0.002f);

        /// <summary>Fades the last few milliseconds of a one-shot to nothing.</summary>
        private static void ApplyTailFade(float[] samples)
        {
            int fade = Mathf.Min(samples.Length, Mathf.RoundToInt(TailFadeSeconds * SampleRate));
            for (int i = 0; i < fade; i++)
            {
                int index = samples.Length - fade + i;
                samples[index] *= 1f - (float)i / fade;
            }
        }

        /// <summary>
        /// A fixed-seed generator.
        ///
        /// <c>UnityEngine.Random</c> is shared global state and cannot be replayed
        /// (guardrail 5), and an unpredictable sound is a sound nobody can test. This
        /// one always produces the same noise.
        /// </summary>
        private struct Rng
        {
            private uint _state;

            public Rng(uint seed) => _state = seed == 0u ? 1u : seed;

            /// <summary>The next value, in [-1, 1).</summary>
            public float Next()
            {
                _state = _state * 1664525u + 1013904223u;
                return (_state >> 8) * (2f / 16777216f) - 1f;
            }
        }
    }
}
