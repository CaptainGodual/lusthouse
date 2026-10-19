using System.Collections.Generic;
using LustHouse.Catalog;
using LustHouse.Core;
using LustHouse.Slots;
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

        public ServicesBootstrap Services { get; private set; }

        private void Awake()
        {
            Services = FindFirstObjectByType<ServicesBootstrap>();
            EnsurePresenter();
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
            var existing = GetComponent<LobbyPresenter>();
            if (existing == null) existing = gameObject.AddComponent<LobbyPresenter>();
            if (existing.controller == null) existing.controller = this;
            if (existing.catalog == null) existing.catalog = catalog;

            // The grid is a separate component so it can own filtering, the tab bar, and
            // tile state without bloating the presenter.
            var grid = GetComponent<LobbyGrid>();
            if (grid == null) grid = gameObject.AddComponent<LobbyGrid>();
            grid.controller = this;
            grid.catalog = catalog;
        }

        // ---- tile tap routing -------------------------------------------------

        public void OnGameTilePressed(string gameId)
        {
            var entry = catalog != null ? catalog.Find(gameId) : null;
            if (entry == null || entry.definition == null) return;

            // Locked (coming soon): teaser, never a purchase path.
            if (entry.locked)
            {
                LustLog.Info($"[LustHouse] '{entry.Title}' is coming soon.");
                return;
            }

            // Gem-entry games: insufficient Gems opens the Gems sheet with the shortfall.
            if (entry.entryCostGems > 0 && WalletService.Instance != null &&
                !WalletService.Instance.CanAfford(Currency.Gems, entry.entryCostGems))
            {
                LustLog.Info($"[LustHouse] Needs {entry.entryCostGems} Gems to enter '{entry.Title}'.");
                return;
            }

            // Balance below the minimum bet is still tappable — the top-up sheet opens
            // with the exact shortfall. Never a dead end.
            long minBet = entry.definition.minBet;
            if (WalletService.Instance != null && !WalletService.Instance.CanAfford(Currency.Coins, minBet))
            {
                LustLog.Info($"[LustHouse] '{entry.Title}' needs {minBet} Coins (shortfall {minBet - WalletService.Instance.Balance(Currency.Coins)}).");
                return;
            }

            Launch(entry.definition);
        }

        /// <summary>Fade-to-black, then load the single shared slot scene (plan §2.8).</summary>
        public void Launch(SlotMachineDefinition definition)
        {
            if (definition == null) return;

            SlotLaunchPayload.Payload = definition;
            UnityEngine.SceneManagement.SceneManager.LoadScene(slotSceneName);
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