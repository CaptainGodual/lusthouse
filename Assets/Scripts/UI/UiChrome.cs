using UnityEngine;
using UnityEngine.UI;

namespace LustHouse.UI
{
    /// <summary>
    /// The casino chrome: the gold-rimmed pills, angled ribbons and soft neon glows the
    /// lobby is built from.
    ///
    /// Everything here is generated at runtime, like the rest of the UI. A production
    /// build would import 9-slice sprites from an artist; until that art exists these
    /// primitives give the lobby the same silhouette — heavy gold rims, angled banners,
    /// soft bloom — instead of flat rectangles. Every sprite is 9-sliced, so a rim or a
    /// ribbon can be stretched to any size without distorting its corners.
    /// </summary>
    public static class UiChrome
    {
        private static Sprite _rim;
        private static Sprite _glow;
        private static Sprite _ribbon;
        private static Sprite _fadeDown;
        private static Sprite _fadeUp;
        private static Sprite _scrimV;

        // ---- sprites -----------------------------------------------------------

        /// <summary>
        /// A rounded rectangle with a transparent middle and a thick coloured ring. Draw it
        /// as a Sliced Image to get a frame of any size.
        /// </summary>
        public static Sprite Rim
        {
            get
            {
                if (_rim != null) return _rim;

                const int size = 64;
                const int radius = 14;     // matches UiFactory.RoundedPanel's corner
                const int thickness = 5;

                var tex = NewTexture("LustHouseRim", size, size);
                var pixels = new Color32[size * size];

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        // The hole is the same shape pulled in by the ring's thickness on
                        // every side. Without the inset the hole spans the same box as the
                        // frame, the difference is empty, and the sprite is invisible.
                        bool outer = InRoundedRect(x, y, size, radius, 0);
                        bool hole = InRoundedRect(x, y, size, radius - thickness, thickness);
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)((outer && !hole) ? 255 : 0));
                    }
                }

                tex.SetPixels32(pixels);
                tex.Apply();

                // The border must cover the rounded corners, or slicing cuts them off.
                _rim = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f,
                    0, SpriteMeshType.FullRect, new Vector4(radius + 2, radius + 2, radius + 2, radius + 2));
                _rim.name = "LustHouseRim";
                return _rim;
            }
        }

        /// <summary>A soft radial bloom, for neon halos behind cards and buttons.</summary>
        public static Sprite Glow
        {
            get
            {
                if (_glow != null) return _glow;

                const int size = 128;
                var tex = NewTexture("LustHouseGlow", size, size);
                var pixels = new Color32[size * size];

                float radius = (size - 1) * 0.5f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = x - radius;
                        float dy = y - radius;
                        float distance = Mathf.Sqrt(dx * dx + dy * dy) / radius;
                        float alpha = Mathf.Clamp01(1f - distance);
                        alpha *= alpha;   // squared falloff reads as a glow, not a disc
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                    }
                }

                tex.SetPixels32(pixels);
                tex.Apply();

                _glow = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
                _glow.name = "LustHouseGlow";
                return _glow;
            }
        }

        /// <summary>
        /// An angled banner — the "TOP GAMES" ribbon. The ends are cut back at an angle
        /// and the middle stretches, so a ribbon of any width keeps the same silhouette.
        /// </summary>
        public static Sprite Ribbon
        {
            get
            {
                if (_ribbon != null) return _ribbon;

                const int width = 64;
                const int height = 32;
                const int slant = 12;

                var tex = NewTexture("LustHouseRibbon", width, height);
                var pixels = new Color32[width * height];

                for (int y = 0; y < height; y++)
                {
                    // The left edge leans in as it rises; the right edge mirrors it.
                    float t = 1f - (float)y / (height - 1);
                    int left = Mathf.RoundToInt(slant * t);
                    int right = width - 1 - Mathf.RoundToInt(slant * t);

                    for (int x = 0; x < width; x++)
                        pixels[y * width + x] = new Color32(255, 255, 255, (byte)(x >= left && x <= right ? 255 : 0));
                }

                tex.SetPixels32(pixels);
                tex.Apply();

                _ribbon = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f,
                    0, SpriteMeshType.FullRect, new Vector4(slant + 2, 0, slant + 2, 0));
                _ribbon.name = "LustHouseRibbon";
                return _ribbon;
            }
        }

        /// <summary>White fading to transparent, top to bottom. A scrim under text.</summary>
        public static Sprite FadeDown
        {
            get
            {
                if (_fadeDown != null) return _fadeDown;

                const int size = 64;
                var tex = NewTexture("LustHouseFadeDown", 4, size);
                var pixels = new Color32[4 * size];

                // A texture's row 0 is its BOTTOM, so the alpha ramps upward in the
                // texture to make the sprite opaque at its top edge.
                for (int y = 0; y < size; y++)
                {
                    byte alpha = (byte)(255f * ((float)y / (size - 1)));
                    for (int x = 0; x < 4; x++) pixels[y * 4 + x] = new Color32(255, 255, 255, alpha);
                }

                tex.SetPixels32(pixels);
                tex.Apply();

                _fadeDown = Sprite.Create(tex, new Rect(0, 0, 4, size), new Vector2(0.5f, 0.5f), 100f);
                _fadeDown.name = "LustHouseFadeDown";
                return _fadeDown;
            }
        }

        /// <summary>Transparent fading to white, top to bottom. A scrim over art.</summary>
        public static Sprite FadeUp
        {
            get
            {
                if (_fadeUp != null) return _fadeUp;

                const int size = 64;
                var tex = NewTexture("LustHouseFadeUp", 4, size);
                var pixels = new Color32[4 * size];

                for (int y = 0; y < size; y++)
                {
                    byte alpha = (byte)(255f * (1f - (float)y / (size - 1)));
                    for (int x = 0; x < 4; x++) pixels[y * 4 + x] = new Color32(255, 255, 255, alpha);
                }

                tex.SetPixels32(pixels);
                tex.Apply();

                _fadeUp = Sprite.Create(tex, new Rect(0, 0, 4, size), new Vector2(0.5f, 0.5f), 100f);
                _fadeUp.name = "LustHouseFadeUp";
                return _fadeUp;
            }
        }

        /// <summary>A vertical two-tone gradient, for backdrops and card fills.</summary>
        public static Sprite GradientV
        {
            get
            {
                if (_scrimV != null) return _scrimV;

                const int height = 128;
                var tex = NewTexture("LustHouseGradientV", 4, height);
                var pixels = new Color32[4 * height];

                for (int y = 0; y < height; y++)
                {
                    // White at the top fading to transparent: the tint colour supplies the
                    // hue, so one sprite serves every gradient the lobby needs.
                    byte alpha = (byte)(255f * ((float)y / (height - 1)));
                    for (int x = 0; x < 4; x++) pixels[y * 4 + x] = new Color32(255, 255, 255, alpha);
                }

                tex.SetPixels32(pixels);
                tex.Apply();

                _scrimV = Sprite.Create(tex, new Rect(0, 0, 4, height), new Vector2(0.5f, 0.5f), 100f);
                _scrimV.name = "LustHouseGradientV";
                return _scrimV;
            }
        }

        // ---- builders ----------------------------------------------------------

        /// <summary>A gold-rimmed frame. The rim is the sprite; the middle stays clear.</summary>
        public static Image NewRim(string name, Transform parent, Color colour, float inset = 0f)
        {
            var image = UiFactory.NewImage(name, parent, colour);
            image.sprite = Rim;
            image.type = Image.Type.Sliced;
            UiFactory.Stretch(image.rectTransform, inset);
            return image;
        }

        /// <summary>A soft halo. Put it behind the thing it lights.</summary>
        public static Image NewGlow(string name, Transform parent, Color colour)
        {
            var image = UiFactory.NewImage(name, parent, colour);
            image.sprite = Glow;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// An angled ribbon with a label. The label rides inside the banner, so the ribbon
        /// can grow with its text without the text drifting off the slant.
        /// </summary>
        public static Image NewRibbon(string name, Transform parent, string label, int fontSize,
                                      Color fill, Color textColour, out Text labelText)
        {
            var image = UiFactory.NewImage(name, parent, fill);
            image.sprite = Ribbon;
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;

            labelText = UiFactory.NewText("Label", image.transform, label, fontSize,
                FontStyle.Bold, textColour);
            UiFactory.Stretch(labelText.rectTransform, 12f);
            labelText.horizontalOverflow = HorizontalWrapMode.Overflow;

            return image;
        }

        /// <summary>
        /// A pill: a dark lozenge with a gold rim and a label. The lobby's main currency
        /// and status readouts are pills, so this is the workhorse.
        /// </summary>
        public static Image NewPill(string name, Transform parent, string label, int fontSize,
                                    Color labelColour, out Text labelText)
        {
            // The fill is an opaque rounded panel; the gold rim is a separate, slightly
            // larger ring laid over it. Using the ring sprite for the fill as well would
            // leave the middle transparent and the pill would read as an outline.
            var image = UiFactory.NewImage(name, parent, UiFactory.VelvetPanel);
            image.sprite = UiFactory.RoundedPanel;
            image.type = Image.Type.Sliced;

            var rim = UiFactory.NewImage("Rim", image.transform, UiFactory.GoldPrimary);
            rim.sprite = Rim;
            rim.type = Image.Type.Sliced;
            UiFactory.Stretch(rim.rectTransform, -3f);
            rim.transform.SetAsFirstSibling();

            labelText = UiFactory.NewText("Label", image.transform, label, fontSize,
                FontStyle.Bold, labelColour, TextAnchor.MiddleCenter);
            UiFactory.Stretch(labelText.rectTransform, 10f);

            return image;
        }

        /// <summary>A pill that can be pressed. The rim stays gold; the fill darkens on press.</summary>
        public static Button NewPillButton(string name, Transform parent, string label, int fontSize,
                                           Color labelColour, out Text labelText)
        {
            var image = NewPill(name, parent, label, fontSize, labelColour, out labelText);

            // A Button only fires when a raycast lands on a Graphic in its hierarchy, and a
            // Graphic with raycastTarget off can never be landed on. NewImage leaves it off
            // for decoration, so a pressable pill has to turn it back on.
            image.raycastTarget = true;

            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            UiFactory.StyleButton(button);
            return button;
        }

        // ---- helpers -----------------------------------------------------------

        private static Texture2D NewTexture(string name, int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            return tex;
        }

        /// <summary>
        /// True when the pixel is inside a rounded square of the given corner radius.
        ///
        /// <paramref name="inset"/> pulls the shape in on every side, which is what turns
        /// a filled shape into a hole with a border around it.
        /// </summary>
        private static bool InRoundedRect(int x, int y, int size, int radius, int inset = 0)
        {
            int left = inset;
            int right = size - 1 - inset;
            int bottom = inset;
            int top = size - 1 - inset;

            if (x < left || x > right || y < bottom || y > top) return false;

            int r = Mathf.Max(0, Mathf.Min(radius, (right - left) / 2, (top - bottom) / 2));

            float dx = x - left < r ? r - (x - left) : (x > right - r ? x - (right - r) : 0f);
            float dy = y - bottom < r ? r - (y - bottom) : (y > top - r ? y - (top - r) : 0f);

            return dx * dx + dy * dy <= (float)r * r;
        }
    }
}
