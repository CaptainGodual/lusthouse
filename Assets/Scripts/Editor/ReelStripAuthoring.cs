using System.Collections.Generic;
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
        /// Counts: Wild 2, Scatter 3, Nine 6, Ten 10, Jack 9, Queen 5, King 5, Ace 7, Filigree 4.
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

            // Honest cap: ten paylines x Filigree 150x is the real ceiling.
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
    }
}