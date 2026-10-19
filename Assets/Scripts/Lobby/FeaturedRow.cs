using System.Collections.Generic;
using LustHouse.Catalog;
using LustHouse.Core;
using LustHouse.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LustHouse.Lobby
{
    /// <summary>
    /// The featured row (the mockup's large boxes).
    ///
    /// Fully data-driven: the tile set comes from <see cref="LobbyPromoConfig"/>, which can
    /// itself be overridden by the remote JSON payload. A new game surfaces by adding it to
    /// the catalog and flipping <c>isFeatured</c> (or listing it in the config) — no code
    /// change, and no rebuild for content-only edits once remote override is live.
    ///
    /// Fallback chain, so the row is never empty:
    ///   configured ids -> entries flagged isFeatured -> newest releases -> nothing
    /// </summary>
    [DisallowMultipleComponent]
    public class FeaturedRow : MonoBehaviour
    {
        [Header("Wiring")]
        public PromoService promo;
        public LobbyController controller;

        [Header("Layout")]
        public float tileHeight = 300f;
        public float gap = 24f;
        public float chevronWidth = 44f;
        public float rightMargin = 40f;

        private RectTransform _row;
        private Text _emptyNotice;

        /// <summary>Raised when a locked teaser tile is tapped.</summary>
        public event System.Action<string, string> TeaserTapped;

        private void Awake()
        {
            if (promo == null) promo = GetComponentInParent<PromoService>();
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

            // Last resort: the newest releases, so the row is never blank.
            var newest = promo != null ? promo.NewestGame() : null;
            if (newest != null)
                resolved.Add(new FeaturedTile { gameId = newest.id, headline = "N E W", order = 0 });

            return resolved;
        }

        // ---- build ------------------------------------------------------------

        public void Rebuild()
        {
            if (_row != null) { Destroy(_row.gameObject); _row = null; }
            if (_emptyNotice != null) { Destroy(_emptyNotice.gameObject); _emptyNotice = null; }

            _row = new GameObject("FeaturedTiles", typeof(RectTransform)).GetComponent<RectTransform>();
            _row.SetParent(transform, false);

            var catalog = promo != null ? promo.catalog : null;
            var tiles = ResolveTiles(catalog);

            if (tiles.Count == 0)
            {
                _emptyNotice = UiFactory.NewText("NoFeatured", _row,
                "No featured games. Set isFeatured on a catalog entry, or add one to the promo config.",
                    18, FontStyle.Normal, UiFactory.TextSecondary, TextAnchor.MiddleLeft);
                UiFactory.Stretch(_emptyNotice.rectTransform);
                return;
            }

            float usable = 1920f - rightMargin * 2f - chevronWidth;
            float tileWidth = (usable - (tiles.Count - 1) * gap) / tiles.Count;

            for (int i = 0; i < tiles.Count; i++)
            {
                var tile = tiles[i];
                var entry = promo != null ? promo.Find(tile.gameId) : null;

                bool locked = entry == null || entry.locked;
                var accent = entry != null && entry.definition != null
                    ? entry.definition.themeTint
                    : UiFactory.NeonMagenta;

                float x = i * (tileWidth + gap);

                var box = UiFactory.NewImage("Featured_" + tile.gameId, _row,
                    locked ? new Color(0.18f, 0.14f, 0.20f, 0.5f)
                           : new Color(accent.r * 0.30f, accent.g * 0.30f, accent.b * 0.30f, 1f));
                box.raycastTarget = true;
                UiFactory.PlaceTopLeft(box.rectTransform, x, 0f, tileWidth, tileHeight);

                var edge = UiFactory.NewImage("Edge", box.transform, locked ? UiFactory.TextDisabled : accent);
                UiFactory.Stretch(edge.rectTransform, 3f);

                string headline = !string.IsNullOrEmpty(tile.headline)
                    ? tile.headline
                    : entry != null ? entry.Title.ToUpperInvariant() : "—";

                var label = UiFactory.NewText("Headline", box.transform, headline, 24,
                    FontStyle.Bold, locked ? UiFactory.TextDisabled : UiFactory.TextPrimary,
                    TextAnchor.LowerCenter);
                var labelRect = label.rectTransform;
                labelRect.anchorMin = new Vector2(0f, 0f);
                labelRect.anchorMax = new Vector2(1f, 0f);
                labelRect.pivot = new Vector2(0.5f, 0f);
                labelRect.offsetMin = new Vector2(8f, 16f);
                labelRect.offsetMax = new Vector2(-8f, 62f);
                label.horizontalOverflow = HorizontalWrapMode.Wrap;

                if (!string.IsNullOrEmpty(tile.subline))
                {
                    var sub = UiFactory.NewText("Subline", box.transform, tile.subline, 14,
                        FontStyle.Normal, UiFactory.TextSecondary, TextAnchor.UpperCenter);
                    var subRect = sub.rectTransform;
                    subRect.anchorMin = new Vector2(0f, 1f);
                    subRect.anchorMax = new Vector2(1f, 1f);
                    subRect.pivot = new Vector2(0.5f, 1f);
                    subRect.offsetMin = new Vector2(10f, -46f);
                    subRect.offsetMax = new Vector2(-10f, -12f);
                }

                // NEW / COMING SOON badge, top-left.
                bool isNew = entry != null && promo != null && promo.IsNew(entry);
                if (isNew || locked)
                {
                    var badge = UiFactory.NewText("Badge", box.transform,
                        locked ? "COMING SOON" : "NEW", 13, FontStyle.Bold,
                        locked ? UiFactory.TextDisabled : UiFactory.NeonCyan, TextAnchor.UpperLeft);
                    UiFactory.PlaceTopLeft(badge.rectTransform, 12f, 12f, 160f, 22f);
                }

                var capturedId = tile.gameId;
                var capturedHeadline = headline;
                var capturedLocked = locked;
                box.gameObject.AddComponent<Button>().onClick.AddListener(() =>
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

            // Right chevron affordance, as in the mockup.
            var chevron = UiFactory.NewText("Chevron", _row, "›", 60, FontStyle.Bold,
                UiFactory.TextSecondary, TextAnchor.MiddleLeft);
            UiFactory.PlaceTopLeft(chevron.rectTransform, usable, 0f, chevronWidth, tileHeight);
        }

        private void OnWarning(string message) =>
            LustLog.Info($"[LustHouse] featured row warning: {message}");
    }
}