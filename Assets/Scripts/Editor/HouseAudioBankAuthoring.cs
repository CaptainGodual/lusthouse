#if UNITY_EDITOR
using System.IO;
using LustHouse.Audio;
using LustHouse.Core;
using UnityEditor;
using UnityEngine;

namespace LustHouse.EditorTools
{
    /// <summary>
    /// Puts the house music bank together: the always-on bank the audio service keeps
    /// loaded, whose common occupant is the lobby's background bed.
    ///
    /// A bank, not a code reference, is how a recorded track enters the game — the
    /// same rule the sfx follow. The clip goes on an entry, the entry names its cue,
    /// and the service plays it over the synthesised fallback. Swapping the lobby's
    /// music is this menu again with a different file; no C# changes hands.
    ///
    /// The asset lives under a Resources folder so the runtime can load it without a
    /// scene reference: the service is constructed in code (ServicesBootstrap), so
    /// there is no inspector to assign a bank on.
    /// </summary>
    public static class HouseAudioBankAuthoring
    {
        public const string BankPath = "Assets/Resources/Audio/AudioBank_House.asset";
        public const string MusicClipPath = "Assets/Audio/Music/funk.wav";

        [MenuItem("LustHouse/Audio/Author house music bank", priority = 56)]
        public static void AuthorHouseMusicBank()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicClipPath);
            if (clip == null)
            {
                LustLog.Error($"[LustHouse] No clip at {MusicClipPath}; the lobby will " +
                              "play the synthesised bed until one is dropped in there.");
            }

            var bank = AssetDatabase.LoadAssetAtPath<AudioBank>(BankPath);
            if (bank == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(BankPath));
                bank = ScriptableObject.CreateInstance<AudioBank>();
                AssetDatabase.CreateAsset(bank, BankPath);
            }

            var entry = bank.Find(AudioCue.LobbyMusic);
            if (entry == null)
            {
                // A bed sits under the room, not over it: 0.6 so clicks and wins
                // still cut through. It is only ever set on creation — after that
                // the inspector is the player's to tune.
                entry = new AudioBank.Entry { cue = AudioCue.LobbyMusic, volume = 0.6f };
                bank.entries.Add(entry);
            }

            entry.clip = clip;

            // The game screen's own bed. It has no clip of its own yet: the
            // synthesised figure plays until a title brings one on its own bank. The
            // level is set here so the bed sits under the reel loop and the wins
            // rather than over them.
            var slot = bank.Find(AudioCue.SlotMusic);
            if (slot == null)
            {
                slot = new AudioBank.Entry { cue = AudioCue.SlotMusic, volume = 0.5f };
                bank.entries.Add(slot);
            }

            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();

            LustLog.Info(clip != null
                ? $"[LustHouse] House bank authored: LobbyMusic <- {MusicClipPath} " +
                  $"({clip.length:F1}s)."
                : "[LustHouse] House bank authored with no clip; LobbyMusic falls back " +
                  "to the synthesised bed.");
        }
    }
}

#endif
