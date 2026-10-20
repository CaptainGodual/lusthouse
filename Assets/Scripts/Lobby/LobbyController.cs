using System.Collections;
using System.Collections.Generic;
using LustHouse.Catalog;
using LustHouse.Core;
using LustHouse.Economy;
using LustHouse.Slots;
using LustHouse.UI;
using UnityEngine;

namespace LustHouse.Lobby
{
    /// <summary>
    /// The single tile-tap dispatcher (plan §2.8). No tile owns routing logic; every
    /// launch path goes through <see cref="Launch"/> so the states cannot drift.
    /// </summary>
    [DisallowMultipleComponent]
    public class LobbyController : MonoBehaviour
    {
        [Header("Content")]
        public GameCatalog catalog;
        public string slotSceneName = "Slot";

        [Header("Catalog state (plan §2.13)")]
        public bool offline = false;

        [Tooltip("Shown as a non-blocking warn chip, never a modal.")]
        public bool showCatalogWarning = false;

        public string warningMessage = "";

        private readonly HashSet<string> _favourites = new HashSet<string>();

        private LobbyPresenter _presenter;
        private LobbyLoadingScreen _loadingScreen;
        private SlotContentService _content;
        private bool _launching;

        public ServicesBootstrap Services { get; private set; }

        private void Awake()
        {
            Services = ServicesBootstrap.EnsureExists();
            EnsurePresenter();
            EnsureLoadingScreen();
            EnsureContentService();
        }

        /// <summary>
        /// Adds the greybox lobby UI if the scene does not already carry it.
        ///
        /// Lobby.unity was generated before <see cref="LobbyPresenter"/> existed, and a
        /// scene with no presenter is just a black screen — which is exactly what that
        /// generated scene was. Self-healing here means a stale scene still shows a lobby.
        /// </summary>
        private void EnsurePresenter()
        {
            // A null catalog builds an empty grid that looks identical to "no games exist"
            // — the tabs render, the empty state renders, and nothing says why. The scene
            // shipped with this null, so say so loudly rather than failing quietly.
            if (catalog == null)
                LustLog.Warn("[LustHouse] LobbyController.catalog is not assigned; the game " +
                             "grid will render empty. Assign GameCatalog in the inspector.");

            var existing = GetComponent<LobbyPresenter>();
            if (existing == null) existing = gameObject.AddComponent<LobbyPresenter>();
            if (existing.controller == null) existing.controller = this;
            if (existing.catalog == null) existing.catalog = catalog;
            _presenter = existing;

            // The grid is a separate component so it can own filtering, the tab bar, and
            // tile state without bloating the presenter.
            var grid = GetComponent<LobbyGrid>();
            if (grid == null) grid = gameObject.AddComponent<LobbyGrid>();
            grid.controller = this;
            grid.catalog = catalog;

            // Adding LobbyPresenter above runs its OnEnable -> Build() -> RebuildGrid()
            // *before* we get a chance to assign the catalog, so that first pass renders
            // an empty grid. Rebuild now that the content reference is in place; nothing
            // else reliably re-triggered it except an incidental WalletChanged event, which
            // left the grid blank. RebuildGrid early-outs when the grid area has not been
            // built yet, so this is safe whether or not the presenter ran.
            grid.RebuildGrid();
        }

        /// <summary>
        /// Guarantees the interstitial loader exists. It is a separate component so the
        /// lobby scene needs no authored UI for it, and so a missing reference can never
        /// produce a black screen (the failure mode the boot screen was built to avoid).
        /// </summary>
        private void EnsureLoadingScreen()
        {
            var existing = GetComponent<LobbyLoadingScreen>();
            if (existing == null) existing = gameObject.AddComponent<LobbyLoadingScreen>();
            _loadingScreen = existing;
        }

        /// <summary>Guarantees the Addressables content service exists for pack downloads.</summary>
        private void EnsureContentService()
        {
            var existing = GetComponent<SlotContentService>();
            if (existing == null) existing = gameObject.AddComponent<SlotContentService>();
            _content = existing;
        }

        // ---- tile tap routing -------------------------------------------------

        public void OnGameTilePressed(string gameId)
        {
            var entry = catalog != null ? catalog.Find(gameId) : null;
            if (entry == null || entry.definition == null) return;

            int level = PlayerProgression.Instance != null ? PlayerProgression.Instance.Level : 1;

            // Locked (coming soon): teaser, never a purchase path.
            if (entry.locked)
            {
                LustLog.Info($"[LustHouse] '{entry.Title}' is coming soon.");
                _presenter?.ShowEntryBlocked(entry.Title,
                    "This title is marked coming soon. Check back for the season update.");
                return;
            }

            // Level-gated: not a purchase path, just progression.
            if (!GameCatalog.IsUnlocked(entry, level))
            {
                int need = Mathf.Max(1, entry.unlockLevel);
                long xp = PlayerProgression.Instance != null ? PlayerProgression.Instance.Xp : 0;
                long needXp = PlayerProgression.Instance != null ? PlayerProgression.Instance.XpForNextLevel() : 100;
                int lvl = PlayerProgression.Instance != null ? PlayerProgression.Instance.Level : 1;
                LustLog.Info($"[LustHouse] '{entry.Title}' unlocks at level {need} (player level {lvl}).");
                _presenter?.ShowEntryBlocked(entry.Title,
                    $"{entry.Title} unlocks at level {need}.\n\n" +
                    $"You are level {lvl} ({xp} / {needXp} XP).\n" +
                    "Earn XP by winning Coins in the slots.");
                return;
            }

            // Gem-entry games: insufficient Gems opens the Gems sheet with the shortfall.
            if (entry.entryCostGems > 0 && WalletService.Instance != null &&
                !WalletService.Instance.CanAfford(Currency.Gems, entry.entryCostGems))
            {
                long have = WalletService.Instance.Balance(Currency.Gems);
                LustLog.Info($"[LustHouse] Needs {entry.entryCostGems} Gems to enter '{entry.Title}'.");
                _presenter?.ShowEntryBlocked(entry.Title,
                    $"This title costs {entry.entryCostGems} Gems to enter.\n\n" +
                    $"You have {have}. Gems are never bought with Coins.");
                return;
            }

            // Balance below the minimum bet is still tappable — the top-up sheet opens
            // with the exact shortfall. Never a dead end.
            long minBet = entry.definition.minBet;
            if (WalletService.Instance != null && !WalletService.Instance.CanAfford(Currency.Coins, minBet))
            {
                long have = WalletService.Instance.Balance(Currency.Coins);
                LustLog.Info($"[LustHouse] '{entry.Title}' needs {minBet} Coins (shortfall {minBet - have}).");
                _presenter?.ShowEntryBlocked(entry.Title,
                    $"{entry.Title} needs at least {UiFactory.FormatAmount(minBet)} Coins for the " +
                    $"minimum bet.\n\nYou have {UiFactory.FormatAmount(have)}. " +
                    "Coins are earned from daily gifts, missions, and level-ups — never bought.");
                return;
            }

            // The pack is fetched (if needed) and the scene loaded by LaunchRoutine, so the
            // loader can show real download + load progress rather than flipping a bool.
            Launch(entry);
        }

        /// <summary>Loads a game from its catalog entry, so the loader can name it.</summary>
        public void Launch(GameCatalog.Entry entry)
        {
            if (entry == null || entry.definition == null) return;
            Launch(entry.definition, entry.Title);
        }

        /// <summary>Loads the single shared slot scene (plan §2.8).</summary>
        public void Launch(SlotMachineDefinition definition)
        {
            if (definition == null) return;
            Launch(definition, definition.title);
        }

        private void Launch(SlotMachineDefinition definition, string title)
        {
            if (_launching) return;
            StartCoroutine(LaunchRoutine(definition, title));
        }

        /// <summary>
        /// Shows the loader, fetches the game's pack from the remote catalog if the device
        /// lacks it, then loads the shared Slot scene without activating it until it is
        /// ready — so the bar reflects real work and the reveal is seamless.
        /// </summary>
        private IEnumerator LaunchRoutine(SlotMachineDefinition definition, string title)
        {
            _launching = true;
            _loadingScreen?.Show(title);

            // Stage 1 — the pack. A blank key means the game ships in the build; the service
            // returns immediately and the loader never shows a pointless "Downloading".
            string key = definition.addressableKey ?? "";
            if (!string.IsNullOrWhiteSpace(key) && _content != null)
            {
                yield return _content.EnsureDownloaded(key, p =>
                    _loadingScreen?.SetProgress(p, "Downloading " + title + "…"));
            }

            // Stage 2 — the scene. Deferred activation keeps the lobby (and this loader)
            // on screen until the Slot scene is fully loaded.
            _loadingScreen?.SetProgress(0f, "Preparing " + title + "…");

            SlotLaunchPayload.Payload = definition;

            var async = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(
                slotSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
            if (async == null)
            {
                _launching = false;
                _loadingScreen?.Hide();
                yield break;
            }

            async.allowSceneActivation = false;
            while (!async.isDone && async.progress < 0.9f)
            {
                _loadingScreen?.SetProgress(async.progress / 0.9f, "Preparing " + title + "…");
                yield return null;
            }

            async.allowSceneActivation = true;
            _launching = false;
        }

        // ---- favourites -------------------------------------------------------

        public void OnFavouriteToggled(string gameId)
        {
            if (string.IsNullOrEmpty(gameId)) return;

            if (_favourites.Contains(gameId)) _favourites.Remove(gameId);
            else _favourites.Add(gameId);

            Services?.MarkDirty();
        }

        public bool IsFavourite(string gameId) => _favourites.Contains(gameId);

        /// <summary>Empty Favorites is never silent (plan §2.7).</summary>
        public string FavouritesEmptyMessage => "Tap ★ on any game to save it here";

        // ---- currency ---------------------------------------------------------

        /// <summary>
        /// The <c>+</c> button is the only entry point to a top-up sheet. A loss flash must
        /// never open one (plan §2.5).
        /// </summary>
        public void OnCurrencyPlusPressed(Currency c)
        {
            // There is no Coins purchase anywhere in the product (guardrail §2), so the
            // Coins + button shows the Gems→Coins converter instead of a store.
            if (c == Currency.Gems)
            {
                LustLog.Info("[LustHouse] Open Gems store sheet.");
                return;
            }

            // Coins purchase path: Gems→Coins converter, capped daily (§4.3 rule 2).
            if (c == Currency.Coins)
            {
                int remaining = WalletService.Instance != null
                    ? WalletService.Instance.ConversionsRemainingToday()
                    : 0;
                LustLog.Info($"[LustHouse] Open Gems→Coins converter ({remaining} conversions left today).");
            }
        }
    }
}