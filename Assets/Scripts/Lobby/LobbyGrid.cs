using System;
using System.Collections.Generic;
using LustHouse.Catalog;
using LustHouse.Core;
using LustHouse.Slots;
using LustHouse.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LustHouse.Lobby
{
    /// <summary>
    /// The category tab bar (§2.7) and the dense game grid (§2.8).
    ///
    /// Greybox implementation: every tile is instantiated up front rather than pooled from
    /// an <c>ObjectPool</c>. At nine games the pool is free, but §2.8 asks for one because
    /// the library will grow — flagging that as remaining work rather than pretending the
    /// pooling is done.
    /// </summary>
    [DisallowMultipleComponent]
    public class LobbyGrid : MonoBehaviour
    {
        [Header("Layout (plan §2.2)")]
        public int columns = 6;
        public float tileWidth = 224f;
        public float tileHeight = 280f;
        public float gap = 24f;

        [Header("Wiring")]
        public LobbyController controller;
        public GameCatalog catalog;

        private GameCategory _active = GameCategory.All;
        private readonly HashSet<string> _favourites = new HashSet<string>();
        private float _gridWidth;

        private RectTransform _tabStrip;
        private RectTransform _tabUnderline;
        private RectTransform _tileParent;
        private Text _emptyState;

        private readonly Dictionary<GameCategory, Text> _tabLabels = new Dictionary<GameCategory, Text>();
        private readonly Dictionary<GameCategory, float> _tabCentres = new Dictionary<GameCategory, float>();
        private float _underlineTargetX;
        private float _underlineTargetWidth;

        public GameCategory ActiveCategory => _active;

        // ---- tabs (§2.7) ------------------------------------------------------

        public void BuildTabs(Transform parent, GameCategory[] categories)
        {
            var go = new GameObject("CategoryTabs", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            _tabStrip = (RectTransform)go.transform;
            UiFactory.PlaceTopLeft(_tabStrip, 0f, 40f, 1920f, 48f);

            const float tabW = 170f;
            const float step = 186f;
            const float underlineW = 120f;

            float x = 0f;
            foreach (var category in categories)
            {
                int count = CountOf(category);
                string label = $"{Label(category)}  {count}";

                var text = UiFactory.NewText("Tab_" + category, _tabStrip, label, 16,
                    FontStyle.Bold, UiFactory.TextSecondary, TextAnchor.MiddleLeft);
                // Left-anchored with a left pivot, so x really is the left edge. The
                // previous centred pivot pushed the first label outside the strip.
                UiFactory.PlaceLeftMid(text.rectTransform, x, tabW, 48f);

                // UiFactory.NewText leaves raycastTarget off, which is right for decoration and fatal
                // for a control: a Button only ever fires when a raycast actually lands on a
                // Graphic in its hierarchy, and a non-raycastable Text can never be landed on.
                // The tab bar was therefore built out of six buttons that could not be clicked.
                text.raycastTarget = true;
                var hit = text.gameObject.AddComponent<Button>();
                var captured = category;
                hit.onClick.AddListener(() => SelectCategory(captured));

                _tabLabels[category] = text;
                _tabCentres[category] = x + (tabW - underlineW) / 2f;

                x += step;
            }

            _tabUnderline = UiFactory.NewImage("ActiveUnderline", _tabStrip, UiFactory.GoldPrimary).rectTransform;
            UiFactory.PlaceLeftMid(_tabUnderline, 0f, underlineW, 4f, -26f);

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

        // ---- grid (§2.8) ------------------------------------------------------

        public void BuildGridArea(Transform parent)
        {
            var go = new GameObject("GameGrid", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            _tileParent = (RectTransform)go.transform;
            _tileParent.anchorMin = Vector2.zero;
            _tileParent.anchorMax = Vector2.one;
            _tileParent.offsetMin = Vector2.zero;
            _tileParent.offsetMax = Vector2.zero;

            _gridWidth = Mathf.Max(1, columns) * tileWidth + (Mathf.Max(1, columns) - 1) * gap;
        }

        /// <summary>
        /// Horizontal left inset that centres the tile block inside the grid area, so the
        /// grid never hugs the screen edge (§2.2 uses a 48px outer margin).
        /// </summary>
        private float GridInset()
        {
            if (_tileParent == null) return 0f;
            float areaWidth = _tileParent.rect.width;
            if (areaWidth <= 1f) return 0f;
            return Mathf.Max(0f, (areaWidth - _gridWidth) / 2f);
        }

        public void RebuildGrid()
        {
            if (_tileParent == null) return;

            for (int i = _tileParent.childCount - 1; i >= 0; i--)
                Destroy(_tileParent.GetChild(i).gameObject);

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

            // The grid never empties silently (§2.7).
            if (matches.Count == 0)
            {
                string message = _active == GameCategory.Favorites
                    ? "Tap the star on any game to save it here"
                    : $"No games in {Label(_active).ToLowerInvariant()}";

                _emptyState = UiFactory.NewText("EmptyState", _tileParent, message, 20,
                    FontStyle.Normal, UiFactory.TextSecondary, TextAnchor.UpperLeft);
                UiFactory.Stretch(_emptyState.rectTransform);
                return;
            }

            int cols = Mathf.Max(1, columns);
            float inset = GridInset();

            for (int i = 0; i < matches.Count; i++)
            {
                int col = i % cols;
                int row = i / cols;

                float x = inset + col * (tileWidth + gap);
                float y = -row * (tileHeight + gap);

                BuildTile(matches[i], _tileParent, new Vector2(x, y));
            }
        }

        private void BuildTile(GameCatalog.Entry entry, Transform parent, Vector2 position)
        {
            bool affordable = IsAffordable(entry);
            bool favourite = _favourites.Contains(entry.id);

            var tint = entry.definition != null ? entry.definition.themeTint : Color.gray;
            var tile = UiFactory.NewImage("Tile_" + entry.id, parent,
                affordable ? new Color(tint.r * 0.35f, tint.g * 0.35f, tint.b * 0.35f, 1f)
                           : new Color(0.18f, 0.14f, 0.20f, 0.45f));
            tile.raycastTarget = true;

            // Key art, when the catalog entry has one. Image.color multiplies the sprite,
            // so the affordable/unaffordable tinting below still dims the art rather than
            // being thrown away by having art present.
            var art = ResolveKeyArt(entry);
            if (art != null)
            {
                tile.sprite = art;
                tile.preserveAspect = true;
                tile.type = Image.Type.Simple;

                // Opaque dim rather than the flat-tile 0.45 alpha used above. Multiplying
                // key art by a near-black translucent colour reads as "no art at all"
                // against the velvet background, which defeats the point of the art.
                tile.color = affordable ? Color.white : new Color(0.45f, 0.40f, 0.48f, 1f);
            }

            UiFactory.PlaceTopLeft(tile.rectTransform, position.x, -position.y,
                tileWidth, tileHeight);

            var button = tile.gameObject.AddComponent<Button>();
            button.targetGraphic = tile;

            // Jackpot tiles get a magenta accent border (§2.8).
            if (entry.definition != null && entry.definition.progressive)
            {
                var edge = UiFactory.NewImage("JackpotEdge", tile.transform, UiFactory.NeonMagenta);
                UiFactory.Stretch(edge.rectTransform, 3f);
            }

            // Title lockup in the bottom 80px.
            var title = UiFactory.NewText("Title", tile.transform, entry.Title, 18,
                FontStyle.Bold, UiFactory.TextPrimary, TextAnchor.LowerCenter);
            var titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 0f);
            titleRect.pivot = new Vector2(0.5f, 0f);
            titleRect.offsetMin = new Vector2(8f, 12f);
            titleRect.offsetMax = new Vector2(-8f, 72f);
            title.horizontalOverflow = HorizontalWrapMode.Wrap;

            // Access badge, top-left.
            string badge = entry.locked ? "COMING SOON"
                : entry.entryCostGems > 0 ? $"{entry.entryCostGems} GEMS"
                : affordable ? "FREE"
                : $"NEEDS {UiFactory.FormatAmount(entry.definition != null ? entry.definition.minBet : 0)} COINS";

            var badgeText = UiFactory.NewText("Badge", tile.transform, badge, 13, FontStyle.Bold,
                entry.locked ? UiFactory.TextDisabled
                    : entry.entryCostGems > 0 ? UiFactory.NeonMagenta
                    : affordable ? UiFactory.NeonCyan : UiFactory.StateDanger,
                TextAnchor.UpperLeft);
            var badgeRect = badgeText.rectTransform;
            badgeRect.anchorMin = new Vector2(0f, 1f);
            badgeRect.anchorMax = new Vector2(0f, 1f);
            badgeRect.pivot = new Vector2(0f, 1f);
            badgeRect.anchoredPosition = new Vector2(10f, -10f);
            badgeRect.sizeDelta = new Vector2(200f, 24f);

            // Favourite star, top-right.
            var star = UiFactory.NewText("Star", tile.transform, favourite ? "★" : "☆", 26,
                FontStyle.Bold, favourite ? UiFactory.GoldGlow : UiFactory.TextSecondary, TextAnchor.UpperRight);
            var starRect = star.rectTransform;
            starRect.anchorMin = new Vector2(1f, 1f);
            starRect.anchorMax = new Vector2(1f, 1f);
            starRect.pivot = new Vector2(1f, 1f);
            starRect.anchoredPosition = new Vector2(-10f, -8f);
            starRect.sizeDelta = new Vector2(40f, 34f);

            star.raycastTarget = true;      // a Button on a non-raycastable Text never fires
            var starHit = star.gameObject.AddComponent<Button>();
            var starTarget = starHit.targetGraphic;
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

        /// <summary>
        /// Key art for a tile, falling back to the slot definition's own art so a game can be
        /// arted from either asset. Returns null when neither is set, which is the normal
        /// case today — the tile then falls back to the flat theme-tint quad.
        /// </summary>
        private static Sprite ResolveKeyArt(GameCatalog.Entry entry)
        {
            if (entry == null) return null;
            if (entry.keyArt != null) return entry.keyArt;
            return entry.definition != null ? entry.definition.keyArt : null;
        }

        private bool IsAffordable(GameCatalog.Entry entry)
        {
            if (entry.locked || entry.definition == null) return false;
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