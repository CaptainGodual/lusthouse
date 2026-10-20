using System.Collections.Generic;
using LustHouse.Catalog;
using LustHouse.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LustHouse.Lobby
{
    /// <summary>
    /// The lobby backdrop: a deep violet hall with soft neon bloom and the library's own
    /// art floating in it, heavily dimmed.
    ///
    /// The art comes from the games themselves — every definition carries the sprites for
    /// its symbols, and those sprites are already in the build as scene dependencies — so
    /// the backdrop needs no extra asset and always matches the library.
    ///
    /// Nothing here is raycastable. A full-screen Graphic with <c>raycastTarget</c> on
    /// takes every click in the game (guardrail §8), and the backdrop covers the whole
    /// screen.
    /// </summary>
    [DisallowMultipleComponent]
    public class LobbyBackdrop : MonoBehaviour
    {
        [Header("Wiring")]
        public GameCatalog catalog;

        /// <summary>Scattered art slots: x, y, scale, rotation.</summary>
        private static readonly (float x, float y, float scale, float tilt)[] Placement =
        {
            (-420f, -260f, 2.6f, -8f),
            (1560f, -180f, 2.8f, 6f),
            (-380f, 640f, 2.4f, 5f),
            (1420f, 700f, 2.6f, -6f),
            (620f, 840f, 2.2f, 9f),
            (-260f, 120f, 2.0f, -4f),
        };

        private static readonly Color HallTop = new Color32(0x4A, 0x1A, 0x63, 0xFF);
        private static readonly Color HallBottom = new Color32(0x14, 0x08, 0x1E, 0xFF);

        public static LobbyBackdrop Create(Transform parent, GameCatalog catalog)
        {
            var go = new GameObject("Backdrop", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var backdrop = go.AddComponent<LobbyBackdrop>();
            backdrop.catalog = catalog;
            backdrop.Build();
            return backdrop;
        }

        private void Build()
        {
            var root = (RectTransform)transform;

            // The hall: a deep violet that falls to near-black at the bottom.
            var baseLayer = UiFactory.NewImage("Hall", root, HallBottom);
            UiFactory.Stretch(baseLayer.rectTransform);

            var topHaze = UiFactory.NewImage("TopHaze", root, HallTop);
            topHaze.sprite = UiChrome.FadeDown;
            UiFactory.Stretch(topHaze.rectTransform);

            // Bloom. Three big soft halos in the house colours, well off centre so the
            // middle of the screen stays readable behind the tiles.
            AddGlow(root, "BloomMagenta", new Color(1f, 0.24f, 0.55f, 0.22f), -240f, 160f, 1500f);
            AddGlow(root, "BloomViolet", new Color(0.45f, 0.20f, 0.95f, 0.26f), 1500f, -180f, 1700f);
            AddGlow(root, "BloomCyan", new Color(0.22f, 0.88f, 0.85f, 0.10f), 700f, 760f, 1200f);

            // The montage: the library's own art, dimmed to a suggestion of depth.
            AddArtwork(root);

            // A veil over the montage. The art is bright enough to read as a picture
            // rather than as depth, and the tiles have to stay the brightest thing on the
            // screen.
            var veil = UiFactory.NewImage("Veil", root, new Color(0.06f, 0.02f, 0.09f, 0.70f));
            UiFactory.Stretch(veil.rectTransform);

            // A final darkening at the very bottom so the bottom bar reads against it.
            var floor = UiFactory.NewImage("Floor", root, new Color(0.05f, 0.02f, 0.08f, 0.55f));
            floor.sprite = UiChrome.FadeUp;
            var floorRect = floor.rectTransform;
            floorRect.anchorMin = new Vector2(0f, 0f);
            floorRect.anchorMax = new Vector2(1f, 0f);
            floorRect.pivot = new Vector2(0.5f, 0f);
            floorRect.sizeDelta = new Vector2(0f, 420f);
            floorRect.anchoredPosition = Vector2.zero;
        }

        private static void AddGlow(RectTransform root, string name, Color colour, float x, float y, float size)
        {
            var glow = UiChrome.NewGlow(name, root, colour);
            UiFactory.PlaceTopLeft(glow.rectTransform, x, y, size, size);
        }

        /// <summary>
        /// Scatters the house's own character illustrations across the hall. The positions
        /// are a fixed table, not random: the lobby must look the same on every launch.
        ///
        /// These are the four character plates — the Wild, the Scatter and the two premium
        /// fruit symbols — not the per-game faces: the montage is the house's art, while
        /// the faces belong to the titles.
        /// </summary>
        private void AddArtwork(RectTransform root)
        {
            var art = CharacterArt();
            if (art == null || art.Count == 0) return;

            for (int i = 0; i < art.Count; i++)
            {
                var slot = Placement[i % Placement.Length];

                var image = UiFactory.NewImage("Art_" + i, root, new Color(1f, 1f, 1f, 0.07f));
                image.sprite = art[i];
                image.preserveAspect = true;
                image.raycastTarget = false;

                float size = 900f * slot.scale;
                UiFactory.PlaceTopLeft(image.rectTransform, slot.x, slot.y, size, size);
                image.rectTransform.localRotation = Quaternion.Euler(0f, 0f, slot.tilt);
            }
        }

        /// <summary>
        /// The four character plates, in a fixed order: the Wild, the Scatter, Grape and
        /// the Kiwi. They live on the symbols every game shares, so any definition will do.
        /// </summary>
        private List<Sprite> CharacterArt()
        {
            var result = new List<Sprite>();
            if (catalog == null) return result;

            int[] wanted = { 0, 1, 15, 16 };

            foreach (var entry in catalog.games)
            {
                if (entry?.definition == null) continue;

                foreach (var id in wanted)
                {
                    var symbol = entry.definition.SymbolById(id);
                    if (symbol != null && symbol.sprite != null && !result.Contains(symbol.sprite))
                        result.Add(symbol.sprite);
                }

                if (result.Count >= wanted.Length) break;
            }

            return result;
        }
    }
}
