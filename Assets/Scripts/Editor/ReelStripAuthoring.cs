using System.Collections.Generic;
using LustHouse.Core;
using LustHouse.Slots;
using UnityEditor;
using UnityEngine;

namespace LustHouse.EditorTools
{
    /// <summary>
    /// Writes the tuned reel strips onto a definition and lets Unity serialise them.
    ///
    /// The original field was int[][], which Unity's serializer silently discards: it
    /// never appears in the asset YAML and always reads back empty, so ReelEngine.Stop
    /// indexes into a zero-length array and throws on every spin. The field is now
    /// List&lt;List&lt;int&gt;&gt;. This script exists so the strips are authored through
    /// the serialized API rather than hand-written YAML.
    /// </summary>
    public static class ReelStripAuthoring
    {
        /// <summary>
        /// Velvet Room strip, tuned to 96.2% RTP. Identical on all five reels; only the
        /// symbol counts affect RTP, never the order.
        /// Counts: Wild 2, Scatter 3, Grape 6, Ten 10, Jack 9, Queen 5, King 5, Ace 7, Kiwi 4.
        /// </summary>
        private static readonly int[] Strip =
        {
            0, 0,
            1, 1, 1,
            15, 15, 15, 15, 15, 15,
            14, 14, 14, 14, 14, 14, 14, 14, 14, 14,
            13, 13, 13, 13, 13, 13, 13, 13, 13,
            12, 12, 12, 12, 12,
            11, 11, 11, 11, 11,
            10, 10, 10, 10, 10, 10, 10,
            16, 16, 16, 16
        };

        [MenuItem("LustHouse/Setup/Author reel strips", priority = 30)]
        public static void Author()
        {
            var def = AssetDatabase.LoadAssetAtPath<SlotMachineDefinition>(
                ContentBootstrap.VelvetRoomPath);
            if (def == null)
            {
                Debug.LogError($"[LustHouse] Velvet Room not found at {ContentBootstrap.VelvetRoomPath}.");
                return;
            }

            var strips = new List<ReelStrip>();
            for (int r = 0; r < def.reelCount; r++)
                strips.Add(new ReelStrip { symbols = new List<int>(Strip) });

            def.reelStrips = strips;

            // Honest cap: ten paylines x Kiwi 150x is the real ceiling.
            def.maxWinMultiplier = (int)(def.paylines.Count *
                def.SymbolById(16).PayoutForMatch(5));

            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (!def.IsValid(out var error))
                Debug.LogError($"[LustHouse] '{def.id}' still invalid after authoring: {error}");
            else
                Debug.Log($"[LustHouse] '{def.id}' authored: {def.reelStrips.Count} reels x " +
                          $"{def.reelStrips[0].symbols.Count} stops, maxWin {def.maxWinMultiplier}x.");
        }

        /// <summary>
        /// Rewrites the placeholder definitions' reel strips from the shared generator,
        /// through the serialized API.
        ///
        /// Velvet Room is skipped: its strips are hand-tuned to a measured RTP and the
        /// generator does not know that tuning. The placeholders have no measured RTP
        /// yet, so they are regenerated from the shared rules instead.
        ///
        /// The strips go through the serialized API rather than hand-written YAML for the
        /// reason the class comment describes: Unity's serializer silently discards
        /// jagged arrays, and a hand-edited strip list that fails to round-trip is
        /// invisible until the reel engine indexes past the end of it.
        /// </summary>
        [MenuItem("LustHouse/Setup/Author placeholder reel strips", priority = 31)]
        public static void AuthorPlaceholders()
        {
            var authored = 0;

            foreach (var guid in AssetDatabase.FindAssets(
                         "t:SlotMachineDefinition", new[] { ContentBootstrap.SlotFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == ContentBootstrap.VelvetRoomPath) continue;

                var def = AssetDatabase.LoadAssetAtPath<SlotMachineDefinition>(path);
                if (def == null) continue;

                def.reelStrips = ContentBootstrap.BuildPlaceholderStrips(def);
                EditorUtility.SetDirty(def);
                authored++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            LustLog.Info($"[LustHouse] Authored reel strips for {authored} placeholder game(s).");
        }
    }
}