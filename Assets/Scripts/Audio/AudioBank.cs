using System;
using System.Collections.Generic;
using UnityEngine;

namespace LustHouse.Audio
{
    /// <summary>
    /// A set of sound overrides for one game.
    ///
    /// The game's own sounds are synthesised, so a bank is optional. It exists so real
    /// recorded audio can replace any cue without touching a line of code: drop the
    /// clip on an entry, and the game plays that instead. A cue with no entry falls
    /// back to the synthesised clip, so a half-filled bank is a perfectly good bank.
    ///
    /// This is what keeps "adding a game needs no new C#" true of audio as well: a
    /// title can ship its own voice without a programmer.
    /// </summary>
    [CreateAssetMenu(menuName = "LustHouse/Audio Bank", fileName = "AudioBank_")]
    public class AudioBank : ScriptableObject
    {
        /// <summary>One cue's clip and how it is played.</summary>
        [Serializable]
        public class Entry
        {
            public AudioCue cue = AudioCue.None;

            [Tooltip("The recorded clip. Empty falls back to the synthesised one.")]
            public AudioClip clip;

            [Range(0f, 1f)]
            public float volume = 1f;

            [Range(0.5f, 2f)]
            public float pitch = 1f;
        }

        public List<Entry> entries = new List<Entry>();

        /// <summary>The entry for a cue, or null when the cue is not overridden.</summary>
        public Entry Find(AudioCue cue)
        {
            if (entries == null) return null;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry != null && entry.cue == cue) return entry;
            }
            return null;
        }
    }
}
