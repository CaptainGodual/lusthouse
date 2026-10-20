using System;
using System.Collections.Generic;
using LustHouse.Catalog;
using LustHouse.Core;
using LustHouse.Economy;
using LustHouse.Slots;
using LustHouse.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LustHouse.Lobby
{
    /// <summary>
    /// The category chips (§2.7) and the library shelf (§2.8).
    ///
    /// The shelf is a paged row rather than a vertical grid, which is the shape the casino
    /// reference uses: a ribbon header, the games in a row, gold arrows at the ends and
    /// page dots underneath. Four titles fit a page, so paging is real rather than
    /// decorative once the library grows past one screen.
    ///
    /// Greybox implementation: every tile is instantiated up front rather than pooled from
    /// an <c>ObjectPool</c>. At nine games the pool is free, but §2.8 asks for one because
    /// the library will grow — flagging that as remaining work rather than pretending the
    /// pooling is done.
    /// </summary>
    [DisallowMultipleComponent]
    public class LobbyGrid : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Tiles per page in the shelf.")]
        public int columns = 4;

        public float tileWidth = 236f;
        public float tileHeight = 300f;
        public float gap = 26f;

        [Header("Wiring")]
        public LobbyController controller;
        public GameCatalog catalog;

        private GameCategory _active = GameCategory.All;
        private readonly HashSet<string> _favourites = new HashSet<string>();

        private RectTransform _tabStrip;
        private RectTransform _tabUnderline;
        private RectTransform _viewport;
        private RectTransform _content;
        private RectTransform _dotStrip;
        private RectTransform _leftArrow;
        private RectTransform _rightArrow;
        private Text _emptyState;

        private readonly Dictionary<GameCategory, Text> _tabLabels = new Dictionary<GameCategory, Text>();
        private readonly Dictionary<GameCategory, float> _tabCentres = new Dictionary<GameCategory, float>();
        private float _underlineTargetX;
        private float _underlineTargetWidth;

        private int _page;
        private int _pageCount;
        private float _contentTargetX;
        private float _pageSpan;

        public GameCategory ActiveCategory => _active;

        // ---- tabs (§2.7) ------------------------------------------------------

        public void BuildTabs(Transform parent, GameCategory[] categories)
        {
            var go = new GameObject("CategoryTabs", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            _tabStrip = (RectTransform)go.transform;
            UiFactory.PlaceTopLeft(_tabStrip, 0f, 0f, 1920f, 44f);

            const float tabW = 176f;
            const float step = 190f;
            const float underlineW = 120f;

            float x = 0f;
            foreach (var category in categories)
            {
                int count = CountOf(category);
                string label = $"{Label(category)}  {count}";

                // A chip: a rounded pill with the label inside, the way the reference
                // dresses its category filters.
                var chip = UiFactory.NewImage("Chip_" + category, _tabStrip, UiFactory.VelvetRaised);
                chip.sprite = UiFactory.RoundedPanel;
                chip.type = Image.Type.Sliced;
                UiFactory.PlaceTopLeft(chip.rectTransform, x, 0f, tabW, 40f);

                var rim = UiChrome.NewRim("Rim", chip.transform, new Color(1f, 1f, 1f, 0.18f));
                UiFactory.Stretch(rim.rectTransform, 2f);

                var text = UiFactory.NewText("Tab_" + category, chip.transform, label, 16,
                    FontStyle.Bold, UiFactory.TextSecondary, TextAnchor.MiddleCenter);
                UiFactory.Stretch(text.rectTransform, 8f);

                // UiFactory.NewText leaves raycastTarget off, which is right for decoration
                // and fatal for a control: a Button only ever fires when a raycast actually
                // lands on a Graphic in its hierarchy, and a non-raycastable Graphic can
                // never be landed on.
                chip.raycastTarget = true;
                var hit = chip.gameObject.AddComponent<Button>();
                hit.targetGraphic = chip;
                UiFactory.StyleButton(hit);
                var captured = category;
                hit.onClick.AddListener(() => SelectCategory(captured));

                _tabLabels[category] = text;
                _tabCentres[category] = x + (tabW - underlineW) / 2f;

                x += step;
            }

            _tabUnderline = UiFactory.NewImage("ActiveUnderline", _tabStrip, UiFactory.GoldPrimary).rectTransform;
            UiFactory.PlaceLeftMid(_tabUnderline, 0f, underlineW, 4f, -22f);

            SelectCategory(categories[0], instant: true);
        }

        private void Update()
        {
            if (_tabUnderline == null) return;

            // The underline slides between tabs (§2.1 panel transition, 200ms).
            float k = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);

            var size = _tabUnderline.sizeDelta;
            size.x = Mathf.Lerp(size.x, _underlineTargetWidth, k);
            _tabUnderline.sizeDelta = size;

            var pos = _tabUnderline.anchoredPosition;
            pos.x = Mathf.Lerp(pos.x, _underlineTargetX, k);
            _tabUnderline.anchoredPosition = pos;

            if (_content != null)
            {
                var contentPos = _content.anchoredPosition;
                contentPos.x = Mathf.Lerp(contentPos.x, _contentTargetX, k);
                _content.anchoredPosition = contentPos;
            }
        }

        public void SelectCategory(GameCategory category, bool instant = false)
        {
            _active = category;

            foreach (var kv in _tabLabels)
                kv.Value.color = kv.Key == category ? UiFactory.TextPrimary : UiFactory.TextSecondary;

            if (_tabCentres.TryGetValue(category, out float left))
            {
                _underlineTargetX = left;
                _underlineTargetWidth = 120f;

                if (instant)
                {
                    var size = _tabUnderline.sizeDelta;
                    size.x = _underlineTargetWidth;
                    _tabUnderline.sizeDelta = size;
                    var pos = _tabUnderline.anchoredPosition;
                    pos.x = _underlineTargetX;
                    _tabUnderline.anchoredPosition = pos;
                }
            }

            _page = 0;
            RebuildGrid();
        }

        private static string Label(GameCategory c)
        {
            switch (c)
            {
                case GameCategory.All: return "All";
                case GameCategory.Slots: return "Slots";
                case GameCategory.Jackpots: return "Jackpots";
                case GameCategory.New: return "New";
                case GameCategory.Favorites: return "Favorites";
                case GameCategory.HighVolatility: return "High Volatility";
                default: return c.ToString();
            }
        }

        private int CountOf(GameCategory category)
        {
            if (category == GameCategory.Favorites) return _favourites.Count;
            if (catalog == null) return 0;

            int n = 0;
            foreach (var entry in catalog.games)
            {
                if (entry == null) continue;
                if (Matches(entry, category)) n++;
            }
            return n;
        }

        private bool Matches(GameCatalog.Entry entry, GameCategory category)
        {
            switch (category)
            {
                case GameCategory.All: return true;
                case GameCategory.Favorites: return _favourites.Contains(entry.id);
                case GameCategory.Jackpots: return entry.definition != null && entry.definition.progressive;
                case GameCategory.HighVolatility: return entry.definition != null && entry.definition.volatility == Volatility.High;
                case GameCategory.New: return entry.definition != null && entry.definition.volatility == Volatility.Low;
                default: return entry.category == category;
            }
        }

        // ---- the shelf (§2.8) -------------------------------------------------

        /// <summary>
        /// Builds the shelf: a masked viewport, the arrows at either end and the page dots
        /// underneath. The viewport is masked rather than clipped by hand, so the tiles that
        /// are off the page are neither drawn nor clickable.
        /// </summary>
        public void BuildShelf(Transform parent, float hostHeight)
        {
            var host = (RectTransform)parent;

            int perPage = Mathf.Max(1, columns);
            _pageSpan = perPage * (tileWidth + gap);

            float blockWidth = perPage * tileWidth + (perPage - 1) * gap;
            float blockX = Mathf.Max(0f, (host.rect.width - blockWidth) * 0.5f);

            var viewportGo = new GameObject("ShelfViewport", typeof(RectTransform));
            viewportGo.transform.SetParent(host, false);
            _viewport = (RectTransform)viewportGo.transform;
            UiFactory.PlaceTopLeft(_viewport, blockX, 0f, blockWidth, tileHeight);
            _viewport.gameObject.AddComponent<RectMask2D>();

            var contentGo = new GameObject("ShelfContent", typeof(RectTransform));
            contentGo.transform.SetParent(_viewport, false);
            _content = (RectTransform)contentGo.transform;
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(0f, 1f);
            _content.pivot = new Vector2(0f, 1f);
            _content.sizeDelta = new Vector2(_pageSpan, tileHeight);
            _content.anchoredPosition = Vector2.zero;

            // The arrows, in the style of the reference: gold rings either side of the row.
            _leftArrow = BuildArrow("ArrowLeft", host, blockX - 84f, "‹", -1);
            _rightArrow = BuildArrow("ArrowRight", host, blockX + blockWidth + 16f, "›", 1);

            var dotGo = new GameObject("PageDots", typeof(RectTransform));
            dotGo.transform.SetParent(host, false);
            _dotStrip = (RectTransform)dotGo.transform;
            UiFactory.PlaceTopCentre(_dotStrip, tileHeight + 10f, 320f, 26f);
        }

        private RectTransform BuildArrow(string name, Transform parent, float x, string glyph, int direction)
        {
            var button = UiChrome.NewPillButton(name, parent, glyph, 46,
                UiFactory.GoldPrimary, out Text glyphText);
            UiFactory.PlaceTopLeft(button.GetComponent<RectTransform>(), x, tileHeight * 0.5f - 42f, 78f, 84f);
            UiFactory.Stretch(glyphText.rectTransform, 4f);
            button.GetComponent<Image>().color = new Color(0.13f, 0.06f, 0.17f, 0.94f);
            button.onClick.AddListener(() => GoToPage(_page + direction));
            return button.GetComponent<RectTransform>();
        }

        private void GoToPage(int page)
        {
            int pages = Mathf.Max(1, _pageCount);
            _page = Mathf.Clamp(page, 0, pages - 1);
            _contentTargetX = -_page * _pageSpan;
            RefreshArrows();
        }

        private void RefreshArrows()
        {
            if (_leftArrow == null) return;

            var left = _leftArrow.GetComponent<Button>();
            var right = _rightArrow != null ? _rightArrow.GetComponent<Button>() : null;

            if (left != null) left.interactable = _page > 0;
            if (right != null) right.interactable = _page < _pageCount - 1;
        }

        public void RebuildGrid()
        {
            if (_content == null) return;

            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);

            if (_emptyState != null)
            {
                Destroy(_emptyState.gameObject);
                _emptyState = null;
            }

            var matches = new List<GameCatalog.Entry>();
            if (catalog != null)
            {
                foreach (var entry in catalog.games)
                    if (entry != null && Matches(entry, _active)) matches.Add(entry);
            }

            // The shelf never empties silently (§2.7).
            if (matches.Count == 0)
            {
                string message = _active == GameCategory.Favorites
                    ? "Tap the star on any game to save it here"
                    : $"No games in {Label(_active).ToLowerInvariant()}";

                _emptyState = UiFactory.NewText("EmptyState", _content, message, 20,
                    FontStyle.Normal, UiFactory.TextSecondary, TextAnchor.MiddleLeft);
                UiFactory.PlaceTopLeft(_emptyState.rectTransform, 0f, 0f, _content.rect.width, tileHeight);
                RebuildDots(0);
                return;
            }

            int perPage = Mathf.Max(1, columns);
            _pageCount = Mathf.Max(1, Mathf.CeilToInt(matches.Count / (float)perPage));
            _page = Mathf.Clamp(_page, 0, _pageCount - 1);

            for (int i = 0; i < matches.Count; i++)
            {
                float x = i * (tileWidth + gap);
                BuildTile(matches[i], _content, x);
            }

            _contentTargetX = -_page * _pageSpan;
            var pos = _content.anchoredPosition;
            pos.x = _contentTargetX;
            _content.anchoredPosition = pos;

            RebuildDots(_pageCount);
            RefreshArrows();
        }

        /// <summary>
        /// The page dots. The active one is gold and wider, the way the reference marks the
        /// current page.
        /// </summary>
        private void RebuildDots(int pages)
        {
            if (_dotStrip == null) return;

            for (int i = _dotStrip.childCount - 1; i >= 0; i--)
                Destroy(_dotStrip.GetChild(i).gameObject);

            if (pages <= 1) return;

            const float spacing = 26f;
            const float stripWidth = 320f;
            float width = (pages - 1) * spacing + 16f;

            for (int i = 0; i < pages; i++)
            {
                bool active = i == _page;
                var dot = UiFactory.NewImage("Dot", _dotStrip,
                    active ? UiFactory.GoldPrimary : new Color(1f, 1f, 1f, 0.35f));
                dot.sprite = UiFactory.Circle;

                // The strip is centred, so the dots are placed from its left edge and the
                // whole run is pushed back by half its own width to stay centred.
                float x = stripWidth * 0.5f - width * 0.5f + 8f + i * spacing;
                UiFactory.PlaceTopLeft(dot.rectTransform, x, 6f, active ? 16f : 10f, 10f);

                var captured = i;
                dot.raycastTarget = true;
                var hit = dot.gameObject.AddComponent<Button>();
                hit.targetGraphic = dot;
                UiFactory.StyleButton(hit);
                hit.onClick.AddListener(() => GoToPage(captured));
            }
        }

        private void BuildTile(GameCatalog.Entry entry, Transform parent, float x)
        {
            bool affordable = IsAffordable(entry);
            bool favourite = _favourites.Contains(entry.id);

            var tint = entry.definition != null ? entry.definition.themeTint : Color.gray;

            var tile = UiFactory.NewImage("Tile_" + entry.id, parent,
                affordable ? new Color(tint.r * 0.32f, tint.g * 0.32f, tint.b * 0.32f, 1f)
                           : new Color(0.16f, 0.10f, 0.18f, 1f));
            tile.sprite = UiFactory.RoundedPanel;
            tile.type = Image.Type.Sliced;
            tile.raycastTarget = true;
            UiFactory.PlaceTopLeft(tile.rectTransform, x, 0f, tileWidth, tileHeight);

            var button = tile.gameObject.AddComponent<Button>();
            button.targetGraphic = tile;

            // The face: the game's own art inside the frame, so every title in the shelf
            // is recognisable without a single new art file. The theme colour rides on
            // the art: every game shares one art set, so the tint is what tells two
            // tiles apart.
            var art = entry.FaceArt();
            if (art != null)
            {
                var face = UiFactory.NewImage("Face", tile.transform,
                    affordable ? Color.Lerp(Color.white, tint, 0.55f)
                               : new Color(0.45f, 0.40f, 0.48f, 0.85f));
                face.sprite = art;
                face.preserveAspect = true;
                UiFactory.PlaceTopLeft(face.rectTransform, tileWidth * 0.08f, 18f,
                    tileWidth * 0.84f, tileWidth * 0.84f);
            }

            // A light unifying wash over the whole tile.
            var wash = UiFactory.NewImage("Wash", tile.transform,
                affordable ? new Color(tint.r, tint.g, tint.b, 0.18f)
                           : new Color(tint.r * 0.5f, tint.g * 0.5f, tint.b * 0.5f, 0.12f));
            wash.sprite = UiFactory.RoundedPanel;
            wash.type = Image.Type.Sliced;
            UiFactory.Stretch(wash.rectTransform);

            // The scrim under the title.
            var scrim = UiFactory.NewImage("Scrim", tile.transform, new Color(0.05f, 0.02f, 0.07f, 0.90f));
            scrim.sprite = UiChrome.FadeUp;
            var scrimRect = scrim.rectTransform;
            scrimRect.anchorMin = new Vector2(0f, 0f);
            scrimRect.anchorMax = new Vector2(1f, 0f);
            scrimRect.pivot = new Vector2(0.5f, 0f);
            scrimRect.sizeDelta = new Vector2(0f, 92f);
            scrimRect.anchoredPosition = Vector2.zero;

            // A solid band behind the title itself.
            var band = UiFactory.NewImage("TitleBand", tile.transform, new Color(0.06f, 0.02f, 0.08f, 0.88f));
            band.sprite = UiFactory.RoundedPanel;
            band.type = Image.Type.Sliced;
            var bandRect = band.rectTransform;
            bandRect.anchorMin = new Vector2(0f, 0f);
            bandRect.anchorMax = new Vector2(1f, 0f);
            bandRect.pivot = new Vector2(0.5f, 0f);
            bandRect.sizeDelta = new Vector2(0f, 54f);
            bandRect.anchoredPosition = Vector2.zero;

            // The frame: magenta for a jackpot, gold for the rest.
            var rim = UiChrome.NewRim("Rim", tile.transform,
                entry.definition != null && entry.definition.progressive
                    ? UiFactory.NeonMagenta
                    : UiFactory.GoldPrimary);
            UiFactory.Stretch(rim.rectTransform, 2f);

            // Title lockup in the bottom band.
            var title = UiFactory.NewText("Title", tile.transform, entry.Title, 18,
                FontStyle.Bold, UiFactory.TextPrimary, TextAnchor.LowerCenter);
            var titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 0f);
            titleRect.pivot = new Vector2(0.5f, 0f);
            titleRect.offsetMin = new Vector2(8f, 12f);
            titleRect.offsetMax = new Vector2(-8f, 66f);
            title.horizontalOverflow = HorizontalWrapMode.Wrap;

            int playerLevel = Economy.PlayerProgression.Instance != null ? Economy.PlayerProgression.Instance.Level : 1;
            bool levelLocked = !entry.locked && !GameCatalog.IsUnlocked(entry, playerLevel);

            // Access badge, top-left.
            string badge = entry.locked ? "COMING SOON"
                : levelLocked ? $"LEVEL {entry.unlockLevel}"
                : entry.entryCostGems > 0 ? $"{entry.entryCostGems} GEMS"
                : affordable ? "FREE"
                : $"NEEDS {UiFactory.FormatAmount(entry.definition != null ? entry.definition.minBet : 0)} COINS";

            var badgeText = UiFactory.NewText("Badge", tile.transform, badge, 13, FontStyle.Bold,
                entry.locked || levelLocked ? UiFactory.TextDisabled
                    : entry.entryCostGems > 0 ? UiFactory.NeonMagenta
                    : affordable ? UiFactory.NeonCyan : UiFactory.StateDanger,
                TextAnchor.UpperLeft);
            var badgeRect = badgeText.rectTransform;
            badgeRect.anchorMin = new Vector2(0f, 1f);
            badgeRect.anchorMax = new Vector2(0f, 1f);
            badgeRect.pivot = new Vector2(0f, 1f);
            badgeRect.anchoredPosition = new Vector2(12f, -12f);
            badgeRect.sizeDelta = new Vector2(200f, 24f);

            // Favourite star, top-right.
            var star = UiFactory.NewText("Star", tile.transform, favourite ? "★" : "☆", 26,
                FontStyle.Bold, favourite ? UiFactory.GoldGlow : UiFactory.TextSecondary, TextAnchor.UpperRight);
            var starRect = star.rectTransform;
            starRect.anchorMin = new Vector2(1f, 1f);
            starRect.anchorMax = new Vector2(1f, 1f);
            starRect.pivot = new Vector2(1f, 1f);
            starRect.anchoredPosition = new Vector2(-12f, -10f);
            starRect.sizeDelta = new Vector2(40f, 34f);

            star.raycastTarget = true;      // a Button on a non-raycastable Text never fires
            var starHit = star.gameObject.AddComponent<Button>();
            var captured = entry;
            starHit.onClick.AddListener(() =>
            {
                ToggleFavourite(captured.id);
                star.text = _favourites.Contains(captured.id) ? "★" : "☆";
                star.color = _favourites.Contains(captured.id) ? UiFactory.GoldGlow : UiFactory.TextSecondary;
            });

            button.onClick.AddListener(() =>
            {
                if (entry.locked) RaiseLockNotice?.Invoke(entry.Title);
                else if (controller != null) controller.OnGameTilePressed(entry.id);
            });
        }

        /// <summary>Raised when a locked tile is tapped, so the lobby can show a sheet.</summary>
        public event Action<string> RaiseLockNotice;

        private bool IsAffordable(GameCatalog.Entry entry)
        {
            if (entry.locked || entry.definition == null) return false;

            int level = Economy.PlayerProgression.Instance != null ? Economy.PlayerProgression.Instance.Level : 1;
            if (!GameCatalog.IsUnlocked(entry, level)) return false;
            if (entry.entryCostGems > 0)
            {
                return WalletService.Instance != null &&
                       WalletService.Instance.CanAfford(Currency.Gems, entry.entryCostGems);
            }
            return WalletService.Instance == null || WalletService.Instance.CanAfford(Currency.Coins, entry.minBetOrZero());
        }

        public void ToggleFavourite(string gameId)
        {
            if (string.IsNullOrEmpty(gameId)) return;
            if (_favourites.Contains(gameId)) _favourites.Remove(gameId);
            else _favourites.Add(gameId);
        }

        public bool IsFavourite(string gameId) => _favourites.Contains(gameId);
    }

    internal static class CatalogEntryExtensions
    {
        public static long minBetOrZero(this GameCatalog.Entry entry) =>
            entry.definition != null ? entry.definition.minBet : 0L;
    }
}
