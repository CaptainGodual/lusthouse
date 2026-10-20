using System;
using System.Collections.Generic;
using LustHouse.Catalog;
using LustHouse.Core;
using LustHouse.Economy;
using LustHouse.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LustHouse.Lobby
{
    /// <summary>
    /// The feature row: the portrait cards above the library.
    ///
    /// Fully data-driven. The tile set comes from <see cref="LobbyPromoConfig"/>, which can
    /// itself be overridden by the remote JSON payload, and the row leads with the promo
    /// slide — the reference puts its "Welcome" card in the same place. A new game surfaces
    /// by adding it to the catalog and flipping <c>isFeatured</c> (or listing it in the
    /// config); no code change, and no rebuild for content-only edits once remote override
    /// is live.
    ///
    /// Fallback chain, so the row is never empty:
    ///   promo slide -> configured ids -> entries flagged isFeatured -> newest releases
    /// </summary>
    [DisallowMultipleComponent]
    public class FeaturedRow : MonoBehaviour
    {
        [Header("Wiring")]
        public PromoService promo;
        public LobbyController controller;

        [Header("Layout")]
        public float tileHeight = 314f;
        public float gap = 26f;

        [Tooltip("Height of the ribbon tab strip above the cards.")]
        public float tabHeight = 32f;

        [Tooltip("Cards are this fraction of their height wide: portrait, like the reference.")]
        public float aspect = 0.76f;

        private RectTransform _row;
        private Text _emptyNotice;

        /// <summary>Raised when the leading promo card is tapped.</summary>
        public event Action<PromoSlide> PromoTapped;

        /// <summary>Raised when a locked teaser tile is tapped.</summary>
        public event Action<string, string> TeaserTapped;

        private void Awake()
        {
            if (promo == null) promo = GetComponentInParent<PromoService>();
        }

        /// <summary>
        /// Wires the row's content sources and builds it.
        ///
        /// The row is added at runtime, and a component's Awake and OnEnable both run the
        /// moment it is added — before the caller can assign its fields. Building here
        /// instead is the only way to get a row that knows where its content comes from.
        /// </summary>
        public void SetPromo(PromoService service, LobbyController tileController)
        {
            if (promo != null)
            {
                promo.ContentChanged -= Rebuild;
                promo.Warning -= OnWarning;
            }

            promo = service;
            controller = tileController;

            if (promo != null)
            {
                promo.ContentChanged += Rebuild;
                promo.Warning += OnWarning;
            }

            Rebuild();
        }

        private void OnEnable()
        {
            if (promo != null)
            {
                promo.ContentChanged += Rebuild;
                promo.Warning += OnWarning;
            }
            Rebuild();
        }

        private void OnDisable()
        {
            if (promo == null) return;
            promo.ContentChanged -= Rebuild;
            promo.Warning -= OnWarning;
        }

        /// <summary>Resolves the tile list from config, then flags, then newest releases.</summary>
        public List<FeaturedTile> ResolveTiles(GameCatalog catalog)
        {
            var config = promo != null ? promo.Active : null;
            int limit = config != null ? Mathf.Clamp(config.maxFeaturedTiles, 1, 8) : 4;
            var resolved = new List<FeaturedTile>();

            if (config != null && catalog != null)
            {
                foreach (var tile in config.FeaturedInOrder())
                {
                    if (resolved.Count >= limit) break;
                    if (tile == null || !tile.IsValid()) continue;

                    // A configured id that no longer exists is skipped, not rendered broken.
                    if (promo.Find(tile.gameId) == null)
                    {
                        LustLog.Warn($"[LustHouse] Featured tile references unknown game '{tile.gameId}'.");
                        continue;
                    }

                    resolved.Add(tile);
                }
            }

            if (resolved.Count > 0 || catalog == null) return resolved;

            // Fallback: entries explicitly flagged, in their declared order.
            var flagged = new List<GameCatalog.Entry>();
            foreach (var entry in catalog.games)
                if (entry != null && entry.isFeatured) flagged.Add(entry);

            flagged.Sort((a, b) => a.featuredOrder.CompareTo(b.featuredOrder));

            foreach (var entry in flagged)
            {
                if (resolved.Count >= limit) break;
                resolved.Add(new FeaturedTile { gameId = entry.id, order = resolved.Count });
            }

            if (resolved.Count > 0) return resolved;

            // Last resort: the newest releases, so the row is never blank. The reference
            // fills its top row with the house's picks; with nothing flagged, the newest
            // titles are the best guess.
            if (catalog != null)
            {
                var ranked = new List<GameCatalog.Entry>();
                for (int i = 0; i < catalog.games.Count; i++)
                {
                    var entry = catalog.games[i];
                    if (entry?.definition != null) ranked.Add(entry);
                }

                // Newest first, catalog order for ties -- an unstable sort would reshuffle
                // the row between launches.
                var order = new Dictionary<GameCatalog.Entry, int>();
                for (int i = 0; i < ranked.Count; i++) order[ranked[i]] = i;

                ranked.Sort((a, b) =>
                {
                    int byDate = b.ReleasedUtc.CompareTo(a.ReleasedUtc);
                    return byDate != 0 ? byDate : order[a].CompareTo(order[b]);
                });

                foreach (var entry in ranked)
                {
                    if (resolved.Count >= limit) break;
                    resolved.Add(new FeaturedTile { gameId = entry.id, order = resolved.Count });
                }
            }

            return resolved;
        }

        /// <summary>The promo slide that leads the row, if there is one.</summary>
        private PromoSlide FirstSlide()
        {
            var config = promo != null ? promo.Active : null;
            if (config == null || config.slides == null || config.slides.Count == 0) return null;
            return config.slides[0];
        }

        // ---- build ------------------------------------------------------------

        public void Rebuild()
        {
            if (_row != null) { Destroy(_row.gameObject); _row = null; }
            if (_emptyNotice != null) { Destroy(_emptyNotice.gameObject); _emptyNotice = null; }

            _row = new GameObject("FeaturedTiles", typeof(RectTransform)).GetComponent<RectTransform>();
            _row.SetParent(transform, false);

            // The row spans its parent, so the cards can be centred inside it.
            _row.anchorMin = Vector2.zero;
            _row.anchorMax = Vector2.one;
            _row.offsetMin = Vector2.zero;
            _row.offsetMax = Vector2.zero;

            var catalog = promo != null ? promo.catalog : null;
            var tiles = ResolveTiles(catalog);
            var slide = FirstSlide();

            int count = tiles.Count + (slide != null ? 1 : 0);
            if (count == 0)
            {
                _emptyNotice = UiFactory.NewText("NoFeatured", _row,
                "No featured games. Set isFeatured on a catalog entry, or add one to the promo config.",
                    18, FontStyle.Normal, UiFactory.TextSecondary, TextAnchor.MiddleLeft);
                UiFactory.Stretch(_emptyNotice.rectTransform);
                return;
            }

            float tileWidth = tileHeight * aspect;
            float total = count * tileWidth + (count - 1) * gap;
            float startX = Mathf.Max(0f, (_row.rect.width - total) * 0.5f);

            int index = 0;
            if (slide != null)
            {
                BuildPromoCard(slide, startX + index * (tileWidth + gap), tileWidth);
                index++;
            }

            foreach (var tile in tiles)
            {
                BuildGameCard(tile, catalog, startX + index * (tileWidth + gap), tileWidth);
                index++;
            }
        }

        /// <summary>
        /// The leading card: the promo slide. It is the reference's "Welcome" card — the one
        /// that is not a game, and the one the eye lands on first.
        /// </summary>
        private void BuildPromoCard(PromoSlide slide, float x, float width)
        {
            var card = BuildCardShell("PromoCard", x, width, slide.accent, false, null);
            var body = card.transform;

            var headline = UiFactory.NewText("Headline", body,
                string.IsNullOrEmpty(slide.headline) ? "F E A T U R E D" : slide.headline,
                20, FontStyle.Bold, UiFactory.TextPrimary, TextAnchor.UpperCenter);
            UiFactory.PlaceTopLeft(headline.rectTransform, 8f, 30f, width - 16f, 66f);
            headline.horizontalOverflow = HorizontalWrapMode.Wrap;

            if (!string.IsNullOrEmpty(slide.subline))
            {
                var sub = UiFactory.NewText("Subline", body, slide.subline, 15,
                    FontStyle.Normal, UiFactory.TextSecondary, TextAnchor.UpperCenter);
                UiFactory.PlaceTopLeft(sub.rectTransform, 12f, 100f, width - 24f, 56f);
                sub.horizontalOverflow = HorizontalWrapMode.Wrap;
            }

            var cta = UiChrome.NewPillButton("Cta", body,
                string.IsNullOrEmpty(slide.ctaLabel) ? "PLAY" : slide.ctaLabel, 18,
                UiFactory.VelvetBase, out Text ctaText);
            UiFactory.PlaceTopCentre(cta.GetComponent<RectTransform>(), tileHeight - 86f, width - 56f, 46f);
            cta.GetComponent<Image>().color = slide.accent;
            UiFactory.Stretch(ctaText.rectTransform);

            var captured = slide;
            cta.onClick.AddListener(() => PromoTapped?.Invoke(captured));
        }

        private void BuildGameCard(FeaturedTile tile, GameCatalog catalog, float x, float width)
        {
            var entry = promo != null ? promo.Find(tile.gameId) : null;

            int playerLevel = Economy.PlayerProgression.Instance != null ? Economy.PlayerProgression.Instance.Level : 1;
            bool levelLocked = entry != null && !entry.locked && !GameCatalog.IsUnlocked(entry, playerLevel);
            bool locked = entry == null || entry.locked || levelLocked;
            var accent = entry != null && entry.definition != null
                ? entry.definition.themeTint
                : UiFactory.NeonMagenta;

            var card = BuildCardShell("Hero_" + tile.gameId, x, width, accent, locked,
                entry != null ? entry.FaceArt() : null);
            var body = card.transform;

            string headline = !string.IsNullOrEmpty(tile.headline)
                ? tile.headline
                : entry != null ? entry.Title.ToUpperInvariant() : "—";

            var label = UiFactory.NewText("Headline", body, headline, 22,
                FontStyle.Bold, locked ? UiFactory.TextDisabled : UiFactory.TextPrimary,
                TextAnchor.LowerCenter);
            var labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.offsetMin = new Vector2(10f, 18f);
            labelRect.offsetMax = new Vector2(-10f, 66f);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;

            // The ribbon tab above the card: JACKPOTS for a progressive, NEW for a release.
            bool isNew = entry != null && promo != null && promo.IsNew(entry);
            bool isJackpot = entry != null && entry.definition != null && entry.definition.progressive;

            string tab = locked ? (levelLocked ? $"LEVEL {entry.unlockLevel}" : "COMING SOON")
                     : isJackpot ? "J A C K P O T S"
                     : isNew ? "N E W"
                     : null;

            if (tab != null)
            {
                Color tabColour = locked ? UiFactory.TextDisabled
                              : isJackpot ? UiFactory.NeonMagenta
                              : UiFactory.NeonCyan;

                var tabRibbon = UiChrome.NewRibbon("Tab", card.transform, tab, 15,
                    new Color(tabColour.r * 0.35f, tabColour.g * 0.35f, tabColour.b * 0.35f, 1f),
                    tabColour, out Text tabText);
                UiFactory.PlaceTopCentre(tabRibbon.rectTransform, -tabHeight + 6f, width - 30f, 30f);
            }

            var capturedId = tile.gameId;
            var capturedHeadline = headline;
            var capturedLocked = locked;
            card.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            {
                if (capturedLocked)
                {
                    TeaserTapped?.Invoke(capturedHeadline,
                        "This title is not available yet.");
                    return;
                }

                if (controller != null) controller.OnGameTilePressed(capturedId);
            });
        }

        /// <summary>
        /// The card body: a dark panel, a soft bloom behind it, the game's face, a scrim
        /// under the title and a gold rim on top. The bloom sits on the parent rather than
        /// the card so it reads as light around the card, not a halo painted on it.
        /// </summary>
        private GameObject BuildCardShell(string name, float x, float width, Color accent,
                                          bool dimmed, Sprite face)
        {
            var bloom = UiChrome.NewGlow("Bloom", transform, new Color(accent.r, accent.g, accent.b, dimmed ? 0.10f : 0.30f));
            UiFactory.PlaceTopLeft(bloom.rectTransform, x - 26f, tabHeight - 26f, width + 52f, tileHeight + 52f);
            bloom.transform.SetAsFirstSibling();

            var card = UiFactory.NewImage(name, transform, dimmed ? new Color(0.14f, 0.08f, 0.16f, 0.92f)
                                                                  : new Color(0.20f, 0.10f, 0.24f, 1f));
            card.sprite = UiFactory.RoundedPanel;
            card.type = Image.Type.Sliced;
            card.raycastTarget = true;
            UiFactory.PlaceTopLeft(card.rectTransform, x, tabHeight, width, tileHeight);

            // The card's own colour, under the art.
            var baseLayer = UiFactory.NewImage("Base", card.transform,
                new Color(accent.r * 0.35f, accent.g * 0.35f, accent.b * 0.35f, dimmed ? 0.25f : 0.85f));
            baseLayer.sprite = UiFactory.RoundedPanel;
            baseLayer.type = Image.Type.Sliced;
            UiFactory.Stretch(baseLayer.rectTransform);

            // The face goes under the scrim, so the scrim still darkens the art the title
            // sits on. The theme colour rides on the art itself: every game shares one
            // art set, so the tint is the only thing that tells two cards apart.
            if (face != null)
            {
                var faceImage = UiFactory.NewImage("Face", card.transform,
                    dimmed ? new Color(0.45f, 0.40f, 0.48f, 0.85f)
                           : Color.Lerp(Color.white, accent, 0.55f));
                faceImage.sprite = face;
                faceImage.preserveAspect = true;
                UiFactory.PlaceTopLeft(faceImage.rectTransform, width * 0.08f, 26f,
                    width * 0.84f, width * 0.84f);
            }

            // A light unifying wash over the whole card.
            var wash = UiFactory.NewImage("Wash", card.transform,
                new Color(accent.r, accent.g, accent.b, dimmed ? 0.10f : 0.18f));
            wash.sprite = UiFactory.RoundedPanel;
            wash.type = Image.Type.Sliced;
            UiFactory.Stretch(wash.rectTransform);

            // The scrim under the title, so the text reads over any art.
            var scrim = UiFactory.NewImage("Scrim", card.transform, new Color(0.05f, 0.02f, 0.07f, 0.92f));
            scrim.sprite = UiChrome.FadeUp;
            var scrimRect = scrim.rectTransform;
            scrimRect.anchorMin = new Vector2(0f, 0f);
            scrimRect.anchorMax = new Vector2(1f, 0f);
            scrimRect.pivot = new Vector2(0.5f, 0f);
            scrimRect.sizeDelta = new Vector2(0f, 104f);
            scrimRect.anchoredPosition = Vector2.zero;

            // A solid band behind the title itself. A gradient alone leaves the title
            // sitting on a mid-tone, which is where the contrast goes.
            var band = UiFactory.NewImage("TitleBand", card.transform, new Color(0.06f, 0.02f, 0.08f, 0.88f));
            band.sprite = UiFactory.RoundedPanel;
            band.type = Image.Type.Sliced;
            var bandRect = band.rectTransform;
            bandRect.anchorMin = new Vector2(0f, 0f);
            bandRect.anchorMax = new Vector2(1f, 0f);
            bandRect.pivot = new Vector2(0.5f, 0f);
            bandRect.sizeDelta = new Vector2(0f, 58f);
            bandRect.anchoredPosition = Vector2.zero;

            // The gold rim, last so it sits over everything.
            var rim = UiChrome.NewRim("Rim", card.transform, dimmed ? UiFactory.TextDisabled : UiFactory.GoldPrimary);
            UiFactory.Stretch(rim.rectTransform, 2f);

            return card.gameObject;
        }

        private void OnWarning(string message) =>
            LustLog.Info($"[LustHouse] featured row warning: {message}");
    }
}
