using LustHouse.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LustHouse.UI
{
    /// <summary>
    /// Shared uGUI construction helpers.
    ///
    /// All runtime UI in this project is built in code rather than authored into scenes.
    /// Scene-authored UI fails *silently* when a reference does not resolve — a missing
    /// script reference on a Text simply renders nothing, which is how the first greybox
    /// looked like a black screen with no way forward. Building in code means the failure
    /// mode is a loud exception, not a blank screen.
    /// </summary>
    public static class UiFactory
    {
        private static Font _font;
        private static Sprite _solid;
        private static Sprite _rounded;
        private static Sprite _circle;

        // ---- shared assets ----------------------------------------------------

        public static Font Font
        {
            get
            {
                if (_font != null) return _font;

                // Unity renamed the built-in font; try the modern name first.
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (_font == null)
                    Core.LustLog.Warn("[LustHouse] No built-in font available; runtime text will not render.");

                return _font;
            }
        }

        /// <summary>A 4x4 white sprite so Images render a solid colour with no asset.</summary>
        public static Sprite Solid
        {
            get
            {
                if (_solid != null) return _solid;

                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false)
                {
                    name = "LustHouseSolid",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };

                var pixels = new Color32[16];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
                tex.SetPixels32(pixels);
                tex.Apply();

                _solid = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
                _solid.name = "LustHouseSolid";
                return _solid;
            }
        }

        /// <summary>
        /// A rounded white sprite, so buttons can read as dark rounded panels.
        ///
        /// The sprite carries a 9-slice border, so an Image can stretch it to any size
        /// without distorting the corners. The radius matches <see cref="UI.UiChrome.Rim"/>'s
        /// so a gold rim laid over one of these panels follows its corners exactly.
        /// </summary>
        public static Sprite RoundedPanel
        {
            get
            {
                if (_rounded != null) return _rounded;

                const int size = 128;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "LustHouseRounded",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };

                const int radius = 28;
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        // Distance to the nearest corner centre; inside the radius -> opaque.
                        float dx = x < radius ? radius - x : (x > size - 1 - radius ? x - (size - 1 - radius) : 0f);
                        float dy = y < radius ? radius - y : (y > size - 1 - radius ? y - (size - 1 - radius) : 0f);
                        bool inside = dx * dx + dy * dy <= radius * radius;
                        pixels[y * size + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply();

                _rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f,
                    0, SpriteMeshType.FullRect, new Vector4(radius + 2, radius + 2, radius + 2, radius + 2));
                _rounded.name = "LustHouseRounded";
                return _rounded;
            }
        }

        /// <summary>A soft-edged white disc for the round bet and spin controls.</summary>
        public static Sprite Circle
        {
            get
            {
                if (_circle != null) return _circle;

                const int size = 128;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "LustHouseCircle",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };

                const float radius = (size - 1) * 0.5f;
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = x - radius;
                        float dy = y - radius;
                        float distance = Mathf.Sqrt(dx * dx + dy * dy);
                        float alpha = Mathf.Clamp01(radius + 0.5f - distance);
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply();

                _circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
                _circle.name = "LustHouseCircle";
                return _circle;
            }
        }

        // ---- palette (plan §2.1) ---------------------------------------------

        public static readonly Color VelvetBase = new Color32(0x14, 0x0A, 0x14, 0xFF);
        public static readonly Color VelvetPanel = new Color32(0x1E, 0x10, 0x20, 0xFF);
        public static readonly Color VelvetRaised = new Color32(0x2A, 0x17, 0x30, 0xFF);
        public static readonly Color GoldPrimary = new Color32(0xE8, 0xC3, 0x6B, 0xFF);
        public static readonly Color GoldGlow = new Color32(0xFF, 0xD9, 0x8A, 0xFF);
        public static readonly Color BarBrown = new Color32(0x17, 0x0D, 0x0B, 0xF2);
        public static readonly Color BetPanelBlue = new Color32(0x0D, 0x18, 0x33, 0xFF);
        public static readonly Color WinPanelBlue = new Color32(0x0B, 0x2A, 0x5A, 0xFF);
        public static readonly Color BetBlue = new Color32(0x2E, 0x7C, 0xE6, 0xFF);
        public static readonly Color MaxPurple = new Color32(0x6B, 0x3F, 0xA0, 0xFF);
        public static readonly Color SpinGreen = new Color32(0x22, 0xB2, 0x4C, 0xFF);
        public static readonly Color NeonMagenta = new Color32(0xFF, 0x3D, 0x8B, 0xFF);
        public static readonly Color NeonCyan = new Color32(0x37, 0xE0, 0xD8, 0xFF);
        public static readonly Color TextPrimary = new Color32(0xF5, 0xE9, 0xF2, 0xFF);
        public static readonly Color TextSecondary = new Color32(0xB7, 0x9B, 0xB4, 0xFF);
        public static readonly Color TextDisabled = new Color32(0x6E, 0x5A, 0x6D, 0xFF);
        public static readonly Color StateSuccess = new Color32(0x5B, 0xD9, 0x8C, 0xFF);
        public static readonly Color StateWarn = new Color32(0xFF, 0xC4, 0x4D, 0xFF);
        public static readonly Color StateDanger = new Color32(0xFF, 0x5C, 0x5C, 0xFF);

        // ---- construction -----------------------------------------------------

        public static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        public static Canvas CreateCanvas(Transform parent, string name, int sortingOrder = 0)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);   // plan §2.1
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>
        /// The canvas a screen's UI belongs in: one under <paramref name="context"/>, else one
        /// authored in the same scene, else a new one.
        ///
        /// The middle case matters. ProjectSetup creates each scene's Canvas as a <em>scene
        /// root</em>, a sibling of the controller rather than its child, so
        /// <c>GetComponentInChildren&lt;Canvas&gt;()</c> returns null and every screen quietly
        /// built a second overlay canvas alongside it. Two ScreenSpaceOverlay canvases at the
        /// same sortingOrder have no meaningful order between them, so which one receives a
        /// click is undefined — and the scene's stray full-quad graphic won.
        /// </summary>
        /// <param name="sortingOrder">Applied to whichever canvas is used, so the requested
        /// order is guaranteed rather than inherited from whatever was authored.</param>
        public static Canvas ResolveCanvas(Transform context, string name, int sortingOrder = 0)
        {
            Canvas canvas = context != null ? context.GetComponentInChildren<Canvas>() : null;

            if (canvas == null && context != null)
            {
                foreach (var candidate in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    if (candidate.gameObject.scene == context.gameObject.scene)
                    {
                        canvas = candidate;
                        break;
                    }
                }
            }

            canvas ??= CreateCanvas(context, name, sortingOrder);
            canvas.sortingOrder = sortingOrder;
            return canvas;
        }

        public static void Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        public static Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);

            var image = go.AddComponent<Image>();
            image.sprite = Solid;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static Text NewText(string name, Transform parent, string content,
                                   int size, FontStyle style, Color color, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);

            var text = go.AddComponent<Text>();
            text.font = Font;
            text.text = content;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        public static void Place(RectTransform rt, Vector2 anchor, Vector2 size, Vector2 anchoredPosition)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPosition;
        }

        // ---- layout convention -------------------------------------------------
        //
        // Every rect is positioned by its TOP-LEFT corner in reference pixels, measured
        // from the top-left of its parent. The previous greybox placed things by centre
        // with mixed pivots, which pushed the featured row off the left edge and dropped
        // the first tab bar label outside its own strip.
        //
        //   anchor (0,1) + pivot (0,1) -> anchoredPosition (x, -y) == "x from left, y from top"
        //
        // Use these helpers rather than computing anchors by hand.

        /// <summary>Top-left corner at (x, y) from the parent's top-left.</summary>
        public static void PlaceTopLeft(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(x, -y);
        }

        /// <summary>Top edge centred horizontally, y from the parent's top.</summary>
        public static void PlaceTopCentre(RectTransform rt, float y, float width, float height,
                                          float xOffset = 0f)
        {
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(xOffset, -y);
        }

        /// <summary>Full-width strip at the top, y from the parent's top.</summary>
        public static void PlaceTopStrip(RectTransform rt, float y, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(0f, height);
            rt.anchoredPosition = new Vector2(0f, -y);
        }

        /// <summary>Left edge, vertically centred in a band. Used for tab bar labels.</summary>
        public static void PlaceLeftMid(RectTransform rt, float x, float width, float height, float y = 0f)
        {
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(x, y);
        }

        /// <summary>Left edge at the bottom, y measured up from the parent's bottom edge.</summary>
        public static void PlaceBottomLeft(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(x, y);
        }

        /// <summary>Bottom-centre, y measured up from the parent's bottom edge.</summary>
        public static void PlaceBottomCentre(RectTransform rt, float y, float width, float height,
                                              float xOffset = 0f)
        {
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(xOffset, y);
        }

        /// <summary>Shared press/hover feedback for buttons built from sprites.</summary>
        public static void StyleButton(Button button)
        {
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            colors.fadeDuration = 0.12f;      // micro interaction, plan §2.1
            button.colors = colors;
        }

        /// <summary>
        /// A button with a visible label. Returns the Button; the label is a child so the
        /// caller can restyle it without re-plumbing the click handler.
        /// </summary>
        public static Button NewButton(string name, Transform parent, string label,
                                       int fontSize, Color textColor, out Text labelText)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);

            var image = go.AddComponent<Image>();
            image.sprite = RoundedPanel;
            image.color = VelvetPanel;
            image.raycastTarget = true;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            // Legible on the default raised panel colour.
            StyleButton(button);

            // Every button the player can press says something. A button that is
            // silent is a button the player has to look at to find.
            AudioFeedback.AttachClickSound(button);

            labelText = NewText("Label", go.transform, label, fontSize, FontStyle.Bold, textColor);
            Stretch(labelText.rectTransform);

            return button;
        }

        /// <summary>Formats a coin-style amount, abbreviating past a million (plan §2.5).</summary>
        public static string FormatAmount(long value)
        {
            if (value < 0) return "-" + FormatAmount(-value);
            if (value >= 1_000_000) return (value / 1_000_000.0).ToString("0.#") + "M";
            if (value >= 100_000) return (value / 1000.0).ToString("0.#") + "K";
            return value.ToString("N0");
        }
    }
}
