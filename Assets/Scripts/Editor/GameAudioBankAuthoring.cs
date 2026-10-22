#if UNITY_EDITOR
using System.IO;
using LustHouse.Audio;
using LustHouse.Core;
using LustHouse.Slots;
using UnityEditor;
using UnityEngine;

namespace LustHouse.EditorTools
{
    /// <summary>
    /// Puts a game's own music bank together: the theme that title plays instead of
    /// the house's synthesised bed.
    ///
    /// The same shape as the house bank, one level down. A bank, not a code reference,
    /// is how a recorded track enters the game: the clip goes on an entry, the entry
    /// names its cue, and the service plays it over the synthesised fallback. Swapping
    /// a title's music is this menu again with a different file; no C# changes hands.
    ///
    /// The bank is hung on the game's definition, because that is what the slot scene
    /// hands to the audio system when the game opens. A bank nobody holds is a bank
    /// that never plays.
    /// </summary>
    public static class GameAudioBankAuthoring
    {
        public const string BankFolder = "Assets/Audio/Banks";
        public const string MusicFolder = "Assets/Audio/Music";
        public const string DefinitionFolder = "Assets/Data/Slots";

        /// <summary>The bank path for a game id.</summary>
        public static string BankPath(string gameId) => $"{BankFolder}/AudioBank_{gameId}.asset";

        /// <summary>The definition path for a game id.</summary>
        public static string DefinitionPath(string gameId) => $"{DefinitionFolder}/Slot_{gameId}.asset";

        /// <summary>
        /// Authors a game's bed: the clip on the <see cref="AudioCue.SlotMusic"/>
        /// entry, and the bank hung on the game's definition.
        ///
        /// Safe to run again: the clip and the level are refreshed, and everything
        /// else about the bank is left alone.
        /// </summary>
        /// <param name="gameId">The game's id, as in its definition and its addressable key.</param>
        /// <param name="clipName">The music file's name inside <see cref="MusicFolder"/>, without the extension.</param>
        /// <param name="volume">The level the bed sits at under the machine.</param>
        public static AudioBank Author(string gameId, string clipName, float volume = 0.5f)
        {
            string clipPath = $"{MusicFolder}/{clipName}.wav";
            var clip = LoadMusic(clipPath);
            if (clip == null)
            {
                LustLog.Error($"[LustHouse] No clip at {clipPath}; {gameId} will play the " +
                              "synthesised bed until one is dropped in there.");
                return null;
            }

            // The definition is found by its id, not by its file name: the asset is
            // named after the title, and a title can be spelled three ways and
            // renamed without warning.
            var definition = FindDefinition(gameId);
            if (definition == null)
            {
                LustLog.Error($"[LustHouse] No definition with the id '{gameId}' under " +
                              $"{DefinitionFolder}; the bank was not written.");
                return null;
            }

            string bankPath = BankPath(definition);
            var bank = AssetDatabase.LoadAssetAtPath<AudioBank>(bankPath);
            if (bank == null)
            {
                Directory.CreateDirectory(BankFolder);
                bank = ScriptableObject.CreateInstance<AudioBank>();
                AssetDatabase.CreateAsset(bank, bankPath);
            }

            var entry = bank.Find(AudioCue.SlotMusic);
            if (entry == null)
            {
                // A bed sits under the machine, not over it: 0.5, the same level the
                // house bank holds the game's bed at, so the reel sounds and the wins
                // still cut through. It is only ever set on creation — after that
                // the inspector is the player's to tune.
                entry = new AudioBank.Entry { cue = AudioCue.SlotMusic, volume = volume };
                bank.entries.Add(entry);
            }

            entry.clip = clip;
            entry.pitch = 1f;

            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();

            // The bank is only worth anything if the game is holding it.
            definition.audioBank = bank;
            definition.audioBankId = gameId;
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();

            LustLog.Info($"[LustHouse] {gameId} bank authored: SlotMusic <- {clipPath} " +
                         $"({clip.length:F1}s), on {bankPath}.");
            return bank;
        }

        /// <summary>The definition with this id, wherever it is filed.</summary>
        private static SlotMachineDefinition FindDefinition(string gameId)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:SlotMachineDefinition", new[] { DefinitionFolder }))
            {
                var def = AssetDatabase.LoadAssetAtPath<SlotMachineDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (def != null && def.id == gameId) return def;
            }
            return null;
        }

        /// <summary>
        /// The bank's path, named after the definition it belongs to: a bank called
        /// <c>AudioBank_VelvetRoom.asset</c> sits beside its <c>Slot_VelvetRoom.asset</c>.
        /// </summary>
        private static string BankPath(SlotMachineDefinition definition)
        {
            string file = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(definition));
            const string prefix = "Slot_";

            string name = file.StartsWith(prefix) ? file.Substring(prefix.Length) : file;
            return $"{BankFolder}/AudioBank_{name}.asset";
        }

        [MenuItem("LustHouse/Audio/Author Velvet Room music bank", priority = 57)]
        public static void AuthorVelvetRoomBank() => Author("velvet_room", "Velvet");

        /// <summary>
        /// Batch entry point, so the bank can be authored without a human in the editor.
        /// </summary>
        public static void AuthorVelvetRoomBankBatch() => Author("velvet_room", "Velvet");

        /// <summary>
        /// Loads a music file and gives it the same import settings as the house bed.
        ///
        /// A bed is minutes long, so it is streamed rather than held in memory, and
        /// compressed rather than kept as PCM. Loading it as PCM would put a hundred
        /// megabytes on a phone for one loop.
        /// </summary>
        private static AudioClip LoadMusic(string path)
        {
            if (!File.Exists(path)) return null;

            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer != null)
            {
                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.Streaming;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 1f;
                settings.preloadAudioData = false;
                importer.defaultSampleSettings = settings;

                importer.forceToMono = false;
                importer.loadInBackground = false;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
    }
}
#endif
