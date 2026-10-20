using LustHouse.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LustHouse.Lobby
{
    /// <summary>
    /// Full-screen interstitial shown while a slot game is fetched and loaded.
    ///
    /// It lives in the lobby scene rather than a third scene: the launch loads the shared
    /// Slot scene with <c>allowSceneActivation = false</c>, so the lobby stays visible until
    /// the game is ready and this overlay is the progress surface in between. The bar reports
    /// real progress — download bytes, then scene-load progress — never a timer (plan §1.2).
    ///
    /// The overlay is built into the lobby's own canvas as the last child, so it draws on top
    /// of the lobby UI without a second canvas and its sorting-order fights.
    /// </summary>
    [DisallowMultipleComponent]
    public class LobbyLoadingScreen : MonoBehaviour
    {
        [Header("Palette (plan §2.1)")]
        public Color velvetBase = UiFactory.VelvetBase;
        public Color velvetRaised = UiFactory.VelvetRaised;
        public Color goldPrimary = UiFactory.GoldPrimary;
        public Color textPrimary = UiFactory.TextPrimary;
        public Color textSecondary = UiFactory.TextSecondary;

        private RectTransform _overlay;
        private Image _fill;
        private Text _title;
        private Text _stage;

        private void OnEnable() => UiFactory.EnsureEventSystem();

        /// <summary>Shows the loader for <paramref name="title"/>, at 0%.</summary>
        public void Show(string title)
        {
            if (_overlay == null) Build();

            if (_title != null) _title.text = title.ToUpperInvariant();
            SetProgress(0f, "Loading…");

            _overlay.gameObject.SetActive(true);
            _overlay.SetAsLastSibling();   // above the lobby UI within the same canvas
        }

        /// <summary>Updates the bar and the stage line ("Downloading Velvet Room…").</summary>
        public void SetProgress(float progress, string stage)
        {
            if (_fill != null) _fill.fillAmount = Mathf.Clamp01(progress);
            if (_stage == null) return;
            if (string.IsNullOrEmpty(stage)) stage = "Loading…";
            _stage.text = Mathf.RoundToInt(Mathf.Clamp01(progress) * 100f) + "%  ·  " + stage;
        }

        public void Hide()
        {
            if (_overlay != null) _overlay.gameObject.SetActive(false);
        }

        private void Build()
        {
            var canvas = UiFactory.ResolveCanvas(transform, "LobbyCanvas");
            var root = canvas.transform as RectTransform;

            var overlayGo = new GameObject("LoadingOverlay", typeof(RectTransform));
            overlayGo.transform.SetParent(root, false);
            _overlay = (RectTransform)overlayGo.transform;
            UiFactory.Stretch(_overlay);

            // Full-screen hit area as a Button: it swallows taps during the transition so a
            // double-tap cannot launch twice, and it is a deliberate control (guardrail §8)
            // rather than a bare full-screen Graphic.
            var blocker = UiFactory.NewImage("Blocker", _overlay, new Color(0f, 0f, 0f, 0f));
            UiFactory.Stretch(blocker.rectTransform);
            blocker.raycastTarget = true;
            blocker.gameObject.AddComponent<Button>();

            var backdrop = UiFactory.NewImage("Backdrop", _overlay, velvetBase);
            UiFactory.Stretch(backdrop.rectTransform);

            _title = UiFactory.NewText("Title", _overlay, "", 44, FontStyle.Bold, textPrimary);
            var titleRect = _title.rectTransform;
            titleRect.anchorMin = new Vector2(0.5f, 0.62f);
            titleRect.anchorMax = new Vector2(0.5f, 0.62f);
            titleRect.sizeDelta = new Vector2(1400f, 70f);

            const float barWidth = 520f, barHeight = 10f, barY = 220f;

            var track = UiFactory.NewImage("Track", _overlay, velvetRaised);
            var trackRect = track.rectTransform;
            trackRect.anchorMin = new Vector2(0.5f, 0f);
            trackRect.anchorMax = new Vector2(0.5f, 0f);
            trackRect.pivot = new Vector2(0.5f, 0f);
            trackRect.anchoredPosition = new Vector2(0f, barY);
            trackRect.sizeDelta = new Vector2(barWidth, barHeight);

            _fill = UiFactory.NewImage("Fill", trackRect, goldPrimary);
            UiFactory.Stretch(_fill.rectTransform, 2f);
            _fill.type = Image.Type.Filled;
            _fill.fillMethod = Image.FillMethod.Horizontal;
            _fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _fill.fillAmount = 0f;

            _stage = UiFactory.NewText("Stage", _overlay, "", 20, FontStyle.Normal, textSecondary);
            var stageRect = _stage.rectTransform;
            stageRect.anchorMin = new Vector2(0.5f, 0f);
            stageRect.anchorMax = new Vector2(0.5f, 0f);
            stageRect.pivot = new Vector2(0.5f, 0f);
            stageRect.anchoredPosition = new Vector2(0f, barY - 44f);
            stageRect.sizeDelta = new Vector2(barWidth + 240f, 30f);
        }
    }
}
