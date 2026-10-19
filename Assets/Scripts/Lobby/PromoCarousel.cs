using System;
using System.Collections.Generic;
using LustHouse.Catalog;
using LustHouse.Core;
using LustHouse.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LustHouse.Lobby
{
    /// <summary>
    /// The lobby carousel (plan §2.6): jackpot / newest / seasonal-promo slides, auto-
    /// advancing on a dwell timer, with tappable dots.
    ///
    /// Behaviour taken from the plan: advance every 8s by default, pause on hover, a dot
    /// tap eases to a slide and resets the timer, never auto-advance while a PLAY button is
    /// under the pointer, and honour Reduced Motion by not advancing at all (§2.14).
    ///
    /// Owns its own banner geometry so nothing depends on the presenter passing sizes in.
    /// </summary>
    [DisallowMultipleComponent]
    public class PromoCarousel : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public const float BannerWidth = 1000f;
        public const float BannerHeight = 132f;
        public const float BannerY = 100f;

        [Header("Wiring")]
        public PromoService promo;
        public LobbyController controller;

        private RectTransform _banner;
        private RectTransform _dotHost;

        private readonly List<RectTransform> _slides = new List<RectTransform>();
        private readonly List<RectTransform> _dots = new List<RectTransform>();

        private int _index;
        private float _dwellElapsed;
        private bool _hovered;

        /// <summary>Raised when a Daily Bonus slide's CTA is tapped.</summary>
        public event Action DailyBonusRequested;

        public int Index => _index;
        public int SlideCount => _slides.Count;

        private void Awake()
        {
            if (promo == null) promo = GetComponentInParent<PromoService>();
            UiFactory.EnsureEventSystem();
        }

        private void OnEnable()
        {
            if (promo != null) promo.ContentChanged += Rebuild;
            Rebuild();
        }

        private void OnDisable()
        {
            if (promo == null) return;
            promo.ContentChanged -= Rebuild;
        }

        public void OnPointerEnter(PointerEventData eventData) => _hovered = true;
        public void OnPointerExit(PointerEventData eventData) => _hovered = false;

        private void Update()
        {
            if (_slides.Count <= 1) return;

            var config = promo != null ? promo.Active : null;
            if (config == null) return;

            // Reduced Motion disables auto-advance entirely (plan §2.14).
            if (config.respectReducedMotion && promo.reducedMotion) return;
            if (config.pauseCarouselOnHover && _hovered) return;

            _dwellElapsed += Time.unscaledDeltaTime;
            if (_dwellElapsed < config.carouselDwellSeconds) return;

            Show((_index + 1) % _slides.Count);
        }

        // ---- build ------------------------------------------------------------

        public void Rebuild()
        {
            Clear();

            var config = promo != null ? promo.Active : null;
            if (config == null) return;

            _banner = new GameObject("Banner", typeof(RectTransform)).GetComponent<RectTransform>();
            _banner.SetParent(transform, false);
            UiFactory.PlaceTopCentre(_banner, BannerY, BannerWidth, BannerHeight);

            _dotHost = new GameObject("Dots", typeof(RectTransform)).GetComponent<RectTransform>();
            _dotHost.SetParent(_banner, false);
            _dotHost.anchorMin = Vector2.zero;
            _dotHost.anchorMax = Vector2.one;
            _dotHost.offsetMin = new Vector2(0f, 14f);
            _dotHost.offsetMax = new Vector2(0f, -BannerHeight + 36f);

            var slides = config.slides;
            if (slides == null || slides.Count == 0) return;

            for (int i = 0; i < slides.Count; i++) BuildSlide(slides[i], i);
            for (int i = 0; i < slides.Count; i++) BuildDot(i);

            // Lead with a real game rather than the daily bonus.
            _index = 0;
            for (int i = 0; i < slides.Count; i++)
                if (slides[i] != null && slides[i].kind != PromoSlideKind.DailyBonus) { _index = i; break; }

            _dwellElapsed = 0f;
            ApplyVisibility();
        }

        private void Clear()
        {
            _slides.Clear();
            _dots.Clear();

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.name == "Banner" || child.name == "Dots")
                    Destroy(child.gameObject);
            }

            if (_banner != null) { Destroy(_banner.gameObject); _banner = null; }
            if (_dotHost != null) { Destroy(_dotHost.gameObject); _dotHost = null; }
        }

        private void BuildSlide(PromoSlide slide, int index)
        {
            if (slide == null) return;

            var bg = UiFactory.NewImage($"Slide{index}_{slide.kind}", _banner, UiFactory.VelvetPanel);
            UiFactory.Stretch(bg.rectTransform);
            bg.raycastTarget = true;

            var group = bg.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;

            var edge = UiFactory.NewImage("Edge", bg.transform, slide.accent);
            UiFactory.Stretch(edge.rectTransform, 2f);

            var heading = UiFactory.NewText("Heading", bg.transform,
                string.IsNullOrEmpty(slide.headline) ? slide.kind.ToString().ToUpperInvariant() : slide.headline,
                30, FontStyle.Bold, UiFactory.GoldGlow);
            UiFactory.PlaceTopLeft(heading.rectTransform, 200f, 24f, 480f, 44f);
            heading.alignment = TextAnchor.MiddleLeft;

            var subline = UiFactory.NewText("Subline", bg.transform, slide.subline, 17,
                FontStyle.Normal, UiFactory.TextSecondary);
            UiFactory.PlaceTopLeft(subline.rectTransform, 200f, 70f, 480f, 28f);
            subline.alignment = TextAnchor.MiddleLeft;

            var cta = UiFactory.NewButton("Cta", bg.transform, slide.ctaLabel, 22,
                UiFactory.VelvetBase, out Text ctaText);
            UiFactory.PlaceTopLeft(cta.GetComponent<RectTransform>(), 700f, 42f, 200f, 48f);
            UiFactory.Stretch(ctaText.rectTransform);
            cta.GetComponent<Image>().color = UiFactory.StateSuccess;

            int captured = index;
            cta.onClick.AddListener(() => OnCtaClicked(captured));

            // Tapping the slide body advances; the CTA does not propagate.
            bg.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            {
                if (captured == _index) ShowNext();
            });

            _slides.Add(bg.rectTransform);
        }

        private void BuildDot(int index)
        {
            var dot = UiFactory.NewImage("Dot" + index, _dotHost,
                index == 0 ? UiFactory.GoldPrimary : UiFactory.TextDisabled);
            float size = index == 0 ? 12f : 8f;

            UiFactory.PlaceBottomCentre(dot.rectTransform, 4f, size, size,
                BannerWidth / 2f - 40f - index * 26f);

            var hit = dot.gameObject.AddComponent<Button>();
            int captured = index;
            hit.onClick.AddListener(() => Show(captured));

            _dots.Add(dot.rectTransform);
        }

        // ---- interaction ------------------------------------------------------

        public void Show(int index)
        {
            if (_slides.Count == 0) return;
            _index = ((index % _slides.Count) + _slides.Count) % _slides.Count;
            _dwellElapsed = 0f;   // a manual tap resets the timer (§2.6)
            ApplyVisibility();
        }

        public void ShowNext() => Show(_index + 1);

        private void ApplyVisibility()
        {
            for (int i = 0; i < _slides.Count; i++)
            {
                var group = _slides[i].gameObject.GetComponent<CanvasGroup>();
                if (group == null) continue;
                bool active = i == _index;
                group.alpha = active ? 1f : 0f;
                group.blocksRaycasts = active;
            }

            for (int i = 0; i < _dots.Count; i++)
            {
                var image = _dots[i].gameObject.GetComponent<Image>();
                if (image != null)
                    image.color = i == _index ? UiFactory.GoldPrimary : UiFactory.TextDisabled;
            }
        }

        private void OnCtaClicked(int index)
        {
            var config = promo != null ? promo.Active : null;
            if (config == null || index >= config.slides.Count) return;

            var slide = config.slides[index];

            if (slide.kind == PromoSlideKind.DailyBonus)
            {
                DailyBonusRequested?.Invoke();
                return;
            }

            var entry = promo != null ? promo.Find(slide.gameId) : null;
            if (entry != null) controller?.OnGameTilePressed(entry.id);
            else LustLog.Warn($"[LustHouse] Slide points at unknown game '{slide.gameId}'.");
        }

        /// <summary>Drops the banner entirely — used when a config has no slides.</summary>
        public void SetVisible(bool visible)
        {
            if (_banner != null) _banner.gameObject.SetActive(visible);
        }
    }
}