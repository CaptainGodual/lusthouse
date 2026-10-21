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
    ///
    /// The jagged array below is a *static* constant in an editor class, not a serialized
    /// field, so it is safe here — the discard problem only bit the definition's own field.
    /// </summary>
    public static class ReelStripAuthoring
    {
        /// <summary>
        /// Velvet Room's authored per-reel strips. Kept so a definition whose strips
        /// failed to serialize can be rebuilt to something playable, and so
        /// <see cref="SlotSymbolExpansion"/> has one place to reset to before it places
        /// symbols.
        ///
        /// Commercial slots use 20-30 stops per reel, so these are per-reel and ramp
        /// 24/25/26/27/28 rather than one identical 51-stop strip. Reel 0 is the cheapest
        /// and decides a line's base symbol; Wild thins toward the right so five-of-a-kind
        /// is rare. Only the symbol counts affect RTP, never the order — and because every
        /// cell on a reel is uniform over that reel's multiset, a payline's expectation
        /// does not depend on which row it visits.
        ///
        /// Two invariants the counts were chosen to hold:
        ///   * Every paying symbol sits on every reel. A symbol absent from reel 0 can
        ///     never pay at all — a line's base is reel 0's cell, and a Wild base followed
        ///     by a different symbol breaks immediately — so dropping one there would leave
        ///     a paytable entry the player can never win with.
        ///   * The six low symbols (Nine..Plum) stay at one stop each. They are ~2% of the
        ///     tuned original; giving them more stops collapses RTP without buying feel.
        ///
        /// The Bonus (id 2) sits on reels 1-4 only, one stop each, taken from the heaviest
        /// paying symbol on that reel. It carries no line payout — it pays through the pick
        /// round — so keeping it off reel 0 means it is never a line's base symbol and can
        /// never block a win where the line has most of its leverage. Its stop is part of
        /// the table so a reset restores it; placing it by hand afterwards would drift.
        ///
        /// Counts (reel: Wild Scatter Bonus Ace King Queen Jack Ten Grape Mango Nine Eight
        /// Cherry Lemon Orange Plum):
        ///   reel 0 (24): 2 2 0 2 2 2 3 2 2 1 1 1 1 1 1 1 1
        ///   reel 1 (25): 2 2 1 2 2 2 3 3 2 1 1 1 1 1 1 1 1
        ///   reel 2 (26): 2 2 1 2 2 2 3 3 2 2 1 1 1 1 1 1 1
        ///   reel 3 (27): 2 2 1 3 2 2 3 3 2 2 1 1 1 1 1 1 1
        ///   reel 4 (28): 1 2 1 3 3 2 3 3 3 2 1 1 1 1 1 1 1
        ///
        /// Measured line RTP with the authored (untuned) paytable on these strips is
        /// 0.5995, and the pick-a-prize bonus adds a further 0.0528 that no paytable
        /// edit can move. The retune therefore scales the paytable by ×1.517 to land the
        /// line game on 0.9093, giving 0.9093 + 0.0528 = 0.9621 against the 0.962 target.
        /// The scale is uniform, so the designer's tier shape survives it — only the
        /// integer rounding of each payout deviates from the exact proportion.
        /// </summary>
        public static readonly int[][] VelvetRoomStrips =
        {
            new[] { 0, 0, 1, 1, 10, 10, 11, 11, 12, 12, 13, 13, 13, 14, 14, 15, 15, 16, 18, 19, 20, 21, 22, 23 },
            new[] { 0, 0, 1, 1, 10, 10, 11, 11, 12, 12, 2, 13, 13, 14, 14, 14, 15, 15, 16, 18, 19, 20, 21, 22, 23 },
            new[] { 0, 0, 1, 1, 10, 10, 11, 11, 12, 12, 2, 13, 13, 14, 14, 14, 15, 15, 16, 16, 18, 19, 20, 21, 22, 23 },
            new[] { 0, 0, 1, 1, 2, 10, 10, 11, 11, 12, 12, 13, 13, 13, 14, 14, 14, 15, 15, 16, 16, 18, 19, 20, 21, 22, 23 },
            new[] { 0, 1, 1, 2, 10, 10, 11, 11, 11, 12, 12, 13, 13, 13, 14, 14, 14, 15, 15, 15, 16, 16, 18, 19, 20, 21, 22, 23 }
        };

        /// <summary>Strips as the serialized model wants them: one List&lt;int&gt; per reel.</summary>
        public static List<ReelStrip> BuildVelvetRoomStrips()
        {
            var strips = new List<ReelStrip>();
            for (int r = 0; r < VelvetRoomStrips.Length; r++)
                strips.Add(new ReelStrip { symbols = new List<int>(VelvetRoomStrips[r]) });
            return strips;
        }

        /// <summary>
        /// True when every reel has a non-empty strip. The repair path below only runs
        /// when this is false.
        /// </summary>
        private static bool StripsAreIntact(SlotMachineDefinition def)
        {
            if (def?.reelStrips == null || def.reelStrips.Count != def.reelCount) return false;

            for (int r = 0; r < def.reelStrips.Count; r++)
            {
                var strip = def.reelStrips[r]?.symbols;
                if (strip == null || strip.Count == 0) return false;
            }

            return true;
        }

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

            // The strips are the shipped, measured content. The table above is only a
            // repair fallback for a definition whose strips failed to serialize — over
            // writing strips that are already intact would silently throw away every
            // symbol somebody has since added to the game, and the RTP with it.
            if (StripsAreIntact(def))
            {
                Debug.Log($"[LustHouse] '{def.id}' already has intact strips " +
                          $"({def.reelStrips[0].symbols.Count} stops); nothing to author. " +
                          $"Use 'Author Velvet Room strips (per-reel)' to deliberately rewrite them.");
                return;
            }

            def.reelStrips = BuildVelvetRoomStrips();

            // Honest cap: ten paylines x the top symbol's five-of-a-kind.
            SlotSymbolExpansion.ReconcileMaxWin(def);

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
        /// Deliberately rewrites Velvet Room's strips to the authored per-reel table,
        /// regardless of what is on the asset now, then retunes the paytable back onto the
        /// declared target RTP and reconciles the max-win cap.
        ///
        /// Separate from <see cref="Author"/>, which is a repair path and refuses to touch
        /// intact strips. Changing strips changes every symbol's probability, so the RTP is
        /// no longer signed off the moment this lands — the retune below is what puts it
        /// back, and it is part of the same menu so the two can never drift apart.
        /// </summary>
        [MenuItem("LustHouse/Content/Author Velvet Room strips (per-reel, 20-30 stops)", priority = 53)]
        public static void AuthorVelvetRoomPerReel()
        {
            var def = AssetDatabase.LoadAssetAtPath<SlotMachineDefinition>(
                ContentBootstrap.VelvetRoomPath);
            if (def == null)
            {
                Debug.LogError($"[LustHouse] Velvet Room not found at {ContentBootstrap.VelvetRoomPath}.");
                return;
            }

            def.reelStrips = BuildVelvetRoomStrips();

            string report;
            bool tuned = SlotSymbolExpansion.RetuneToDeclaredTarget(def, out report);
            if (tuned) SlotSymbolExpansion.ReconcileMaxWin(def);

            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string lengths = string.Join("/", def.reelStrips.ConvertAll(r => r.symbols.Count));
            string status = def.IsValid(out var error) ? "valid" : $"INVALID ({error})";
            Debug.Log($"[LustHouse] '{def.id}' per-reel strips authored ({lengths} stops), " +
                      $"maxWin {def.maxWinMultiplier}x, {status}. {report}");

            if (!tuned)
                Debug.LogError("[LustHouse] Velvet Room did not land on its declared target " +
                               "RTP — it is no longer ship-gated. " + report);
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