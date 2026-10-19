using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LustHouse.Boot
{
    /// <summary>
    /// Builds and drives the loading screen UI (plan §1.2, §1.5).
    ///
    /// The UI is constructed in code rather than authored into the scene on purpose:
    /// scene-authored UI silently fails to appear when a reference is missing, which is
    /// exactly how the first greybox ended up as a black screen with no way forward.
    /// Building it here means the only way to get a blank screen is for this component to
    /// be absent from the scene entirely.
    ///
    /// Layout is anchored from a 1920x1080 reference (§2.1) and scaled by CanvasScaler.
    /// </summary>
    [DisallowMultipleComponent]
    public class BootPresenter : MonoBehaviour
    {
        [Header("Wiring")]
        public BootSequence sequence;

        [Header("Palette (plan §2.1)")]
        public Color velvetBase = new Color32(0x14, 0x0A, 0x14, 0xFF);
        public Color velvetRaised = new Color32(0x2A, 0x17, 0x30, 0xFF);
        public Color goldPrimary = new Color32(0xE8, 0xC3, 0x6B, 0xFF);
        public Color goldGlow = new Color32(0xFF, 0xD9, 0x8A, 0xFF);
        public Color neonMagenta = new Color32(0xFF, 0x3D, 0x8B, 0xFF);
        public Color textPrimary = new Color32(0xF5, 0xE9, 0xF2, 0xFF);
        public Color textSecondary = new Color32(0xB7, 0x9B, 0xB4, 0xFF);

        [Header("Timing")]
        [Tooltip("Soft-pulse period for the TAP TO ENTER prompt, seconds.")]
        public float pulsePeriod = 1.4f;

        private Image _fillImage;
        private Text _stageLabel;
        private Text _title;
        private Text _subtitle;
        private CanvasGroup _enterGroup;
        private Button _enterButton;
        private Canvas _canvas;

        private bool _ready;

        // ---- construction -----------------------------------------------------

        private void Awake()
        {
            if (sequence == null) sequence = GetComponent<BootSequence>();

            EnsureEventSystem();
            _canvas = BuildCanvas();
            BuildUI();
        }

        private void OnEnable()
        {
            if (sequence == null) return;

            sequence.ProgressChanged += OnProgress;
            sequence.Ready += OnReady;

            // Catch up if the sequence finished before this subscribed.
            OnProgress(sequence.Progress01, sequence.StageLabel);
            if (sequence.ReadyToEnter) OnReady();
        }

        private void OnDisable()
        {
            if (sequence == null) return;
            sequence.ProgressChanged -= OnProgress;
            sequence.Ready -= OnReady;
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            go.transform.SetParent(null);
        }

        private Canvas BuildCanvas()
        {
            var existing = GetComponentInChildren<Canvas>();
            if (existing != null) return existing;

            var go = new GameObject("BootCanvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        private void BuildUI()
        {
            var font = LoadFont();
            var root = _canvas.transform as RectTransform;
            Sprite white = SolidSprite();

            // Backdrop.
            var bg = NewImage("Backdrop", root, white, velvetBase);
            Stretch(bg.rectTransform);

            // A soft magenta bloom behind the wordmark (plan §1.2, neon ignition).
            var bloom = NewImage("NeonBloom", root, white, new Color(neonMagenta.r, neonMagenta.g, neonMagenta.b, 0.18f));
            var bloomRect = bloom.rectTransform;
            bloomRect.anchorMin = new Vector2(0.5f, 0.62f);
            bloomRect.anchorMax = new Vector2(0.5f, 0.62f);
            bloomRect.sizeDelta = new Vector2(900, 220);

            _title = NewText("Title", root, font, "L U S T   H O U S E", 64, FontStyle.Bold, textPrimary);
            var titleRect = _title.rectTransform;
            titleRect.anchorMin = new Vector2(0.5f, 0.62f);
            titleRect.anchorMax = new Vector2(0.5f, 0.62f);
            titleRect.sizeDelta = new Vector2(1000, 100);
            // +4% tracking on display type (§2.1).
            _title.supportRichText = false;

            _subtitle = NewText("Subtitle", root, font, "Play money only.  18+", 20, FontStyle.Normal, textSecondary);
            var subRect = _subtitle.rectTransform;
            subRect.anchorMin = new Vector2(0.5f, 0.53f);
            subRect.anchorMax = new Vector2(0.5f, 0.53f);
            subRect.sizeDelta = new Vector2(900, 40);

            // ---- progress bar, bottom-centre, 420px wide (§1.2 beat 3) ----
            const float barWidth = 420f;
            const float barHeight = 8f;
            const float barY = 180f;

            var track = NewImage("ProgressTrack", root, white, velvetRaised);
            var trackRect = track.rectTransform;
            trackRect.anchorMin = new Vector2(0.5f, 0f);
            trackRect.anchorMax = new Vector2(0.5f, 0f);
            trackRect.pivot = new Vector2(0.5f, 0f);
            trackRect.anchoredPosition = new Vector2(0, barY);
            trackRect.sizeDelta = new Vector2(barWidth, barHeight);

            _fillImage = NewImage("ProgressFill", trackRect, white, goldPrimary);
            Stretch(_fillImage.rectTransform, 2f);
            _fillImage.type = Image.Type.Filled;
            _fillImage.fillMethod = Image.FillMethod.Horizontal;
            _fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            _fillImage.fillAmount = 0f;

            _stageLabel = NewText("StageLabel", root, font, "Starting…", 18, FontStyle.Normal, textSecondary);
            var stageRect = _stageLabel.rectTransform;
            stageRect.anchorMin = new Vector2(0.5f, 0f);
            stageRect.anchorMax = new Vector2(0.5f, 0f);
            stageRect.pivot = new Vector2(0.5f, 0f);
            stageRect.anchoredPosition = new Vector2(0, barY - 44f);
            stageRect.sizeDelta = new Vector2(barWidth + 240, 30);

            // ---- TAP TO ENTER ----
            var enterGo = new GameObject("TapToEnter", typeof(RectTransform));
            enterGo.transform.SetParent(root, false);
            var enterRect = (RectTransform)enterGo.transform;
            Stretch(enterRect);

            _enterGroup = enterGo.AddComponent<CanvasGroup>();
            _enterGroup.alpha = 0f;
            _enterGroup.interactable = false;
            _enterGroup.blocksRaycasts = false;

            // Full-screen transparent button: a tap anywhere advances, so the greybox
            // never dead-ends. The visible prompt is a child of it.
            var buttonImage = enterGo.AddComponent<Image>();
            buttonImage.sprite = white;
            buttonImage.color = new Color(0f, 0f, 0f, 0f);   // invisible hit area
            buttonImage.raycastTarget = true;

            _enterButton = enterGo.AddComponent<Button>();
            _enterButton.targetGraphic = buttonImage;
            _enterButton.onClick.AddListener(OnEnterClicked);

            var prompt = NewText("Prompt", enterRect, font, "T A P   T O   E N T E R", 40, FontStyle.Bold, goldGlow);
            var promptRect = prompt.rectTransform;
            promptRect.anchorMin = new Vector2(0.5f, 0.5f);
            promptRect.anchorMax = new Vector2(0.5f, 0.5f);
            promptRect.sizeDelta = new Vector2(900, 80);
        }

        // ---- events -----------------------------------------------------------

        private void OnProgress(float progress, string label)
        {
            if (_fillImage != null) _fillImage.fillAmount = Mathf.Clamp01(progress);

            if (_stageLabel == null) return;

            // Compose the string fresh each time. Assigning to .text twice would append
            // the percentage to the previous frame's text.
            if (string.IsNullOrEmpty(label)) label = "Starting…";
            _stageLabel.text = Mathf.RoundToInt(Mathf.Clamp01(progress) * 100f) + "%  ·  " + label;
        }

        private void OnReady()
        {
            if (_ready) return;
            _ready = true;

            if (_enterGroup != null)
            {
                _enterGroup.alpha = 1f;
                _enterGroup.interactable = true;
                _enterGroup.blocksRaycasts = true;
            }
        }

        private void OnEnterClicked()
        {
            if (!_ready || sequence == null) return;
            _enterButton.interactable = false;
            sequence.EnterLobby();
        }

        private void Update()
        {
            float t = Time.unscaledTime;

            // Beat 1 — the neon sign flickers on in two stutters, then holds (§1.2).
            if (_title != null)
            {
                float ignition = Mathf.Clamp01(t / 1.2f);
                bool stutter = ignition < 1f &&
                               (Mathf.PingPong(t * 9f, 1f) > 0.62f || ignition < 0.35f);
                var c = goldGlow;
                c.a = stutter ? 0.35f : Mathf.Lerp(0.6f, 1f, ignition);
                _title.color = c;
            }

            // Soft pulse on the prompt (§1.2 beat 4). Cheap: one ping-pong, no allocation.
            if (!_ready || _enterGroup == null) return;

            float pulse = Mathf.PingPong(t / pulsePeriod, 1f);
            _enterGroup.alpha = Mathf.Lerp(0.55f, 1f, pulse);
        }

        // ---- helpers ----------------------------------------------------------

        private static Font LoadFont()
        {
            // Unity renamed the built-in font; try the modern name first, then the old one.
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font == null) LustHouse.Core.LustLog.Warn("[LustHouse] No built-in font; boot text will not render.");
            return font;
        }

        private static Sprite SolidSprite()
        {
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

            return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
        }

        private static void Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        private static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text NewText(string name, Transform parent, Font font, string content,
                                    int size, FontStyle style, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = font;
            text.text = content;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }
    }
}
