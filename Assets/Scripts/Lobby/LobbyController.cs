using System.Collections;
using System.Collections.Generic;
using LustHouse.Audio;
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

        [Header("Launch")]
        [Tooltip("How long a game may take to load before the lobby stops waiting for it. " +
                 "A load that never finishes must not strand the player on the loader.")]
        public float sceneLoadTimeoutSeconds = 45f;

        [Tooltip("The backstop. A launch that has been running this long has something " +
                 "stuck in it, and the player must not be left looking at the loader.")]
        public float launchStuckSeconds = 90f;

        private readonly HashSet<string> _favourites = new HashSet<string>();

        private LobbyPresenter _presenter;
        private LobbyLoadingScreen _loadingScreen;
        private SlotContentService _content;
        private bool _launching;
        private float _launchElapsed;

        public ServicesBootstrap Services { get; private set; }

        private void Awake()
        {
            Services = ServicesBootstrap.EnsureExists();
            EnsurePresenter();
            EnsureLoadingScreen();
            EnsureContentService();
        }

        private void Start()
        {
            // The lobby has a room tone: a bed of music that starts when the player
            // arrives and fades when they leave for a game. It is asked for by cue, so
            // what the bed actually is — a recorded track on the house bank, or the
            // synthesised fallback — is decided by the audio system, not here.
            AudioFeedback.PlayMusic(AudioCue.LobbyMusic);
        }

        private void OnDestroy()
        {
            // Leaving for a game unloads this scene. The bed fades rather than cuts:
            // the music channel outlives the lobby, so without this the funk would
            // follow the player all the way into the slot screen.
            AudioFeedback.StopMusic(AudioCue.LobbyMusic);
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

            // A self-excluded player cannot open a game. The check comes before the
            // tile is even looked at: an exclusion that only blocks some of the doors
            // is not an exclusion.
            if (BlockedBySelfExclusion()) return;

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
            if (BlockedBySelfExclusion()) return;
            Launch(entry.definition, entry.Title);
        }

        /// <summary>Loads the single shared slot scene (plan §2.8).</summary>
        public void Launch(SlotMachineDefinition definition)
        {
            if (definition == null) return;
            if (BlockedBySelfExclusion()) return;
            Launch(definition, definition.title);
        }

        /// <summary>
        /// Is this player excluded from playing?
        ///
        /// Both launch paths check it — the tile press and the launch itself — because
        /// a launch can be started from somewhere other than a tile press (a deep
        /// link, a test, a script), and an exclusion that only guards one door is
        /// guarding the wrong one.
        /// </summary>
        private bool BlockedBySelfExclusion()
        {
            var service = ResponsiblePlayService.Instance;
            if (service == null || !service.IsExcluded) return false;

            LustLog.Info("[LustHouse] The player is self-excluded; the game will not open.");

            string body = service.IsPermanentlyExcluded
                ? "You have excluded yourself from Lust House permanently.\n\n" +
                  "No title will open. This cannot be changed from inside the game."
                : "You have excluded yourself from Lust House.\n\n" +
                  $"No title will open until the exclusion lifts on its own" +
                  (service.ExclusionEndsUtc.HasValue
                      ? $" ({service.ExclusionEndsUtc.Value:yyyy-MM-dd HH:mm} UTC)."
                      : ".");

            _presenter?.ShowEntryBlocked("PLAYING IS PAUSED", body);
            return true;
        }

        private void Launch(SlotMachineDefinition definition, string title)
        {
            if (_launching) return;
            _launchElapsed = 0f;
            StartCoroutine(LaunchRoutine(definition, title));
        }

        /// <summary>
        /// The backstop. Everything in the launch path has its own timeout, but a bug
        /// anywhere in it must not be able to leave the player looking at the loader for
        /// ever: the loader comes down, the guard is released so the next tap works, and
        /// the failure is said out loud.
        /// </summary>
        private void Update()
        {
            if (!_launching) return;

            _launchElapsed += Time.unscaledDeltaTime;
            if (_launchElapsed < launchStuckSeconds) return;

            LustLog.Error($"[LustHouse] A launch has been running for more than " +
                          $"{launchStuckSeconds}s; the loader has been dismissed.");
            _launching = false;
            _loadingScreen?.Hide();
            _presenter?.ShowEntryBlocked("Loading",
                "The game took too long to load. Please try again.");
        }

        /// <summary>
        /// Shows the loader, fetches the game's pack from the remote catalog if the device
        /// lacks it, then loads the shared Slot scene without activating it until it is
        /// ready — so the bar reflects real work and the reveal is seamless.
        ///
        /// Every path out of this routine releases the launch guard and leaves the player
        /// with something they can act on. A launch that dies quietly used to leave the
        /// loader on screen for ever, which is the one outcome a player cannot act on.
        /// </summary>
        private IEnumerator LaunchRoutine(SlotMachineDefinition definition, string title)
        {
            _launching = true;
            _loadingScreen?.Show(title);

            // Stage 1 — the pack. A blank key means the game ships in the build; the service
            // returns immediately and the loader never shows a pointless "Downloading".
            // The service swallows its own failures: a pack that cannot be fetched falls
            // back to the bundled content rather than killing this coroutine (plan §1.3).
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
                FailLaunch(title, "The game could not be opened. Please try again.");
                yield break;
            }

            async.allowSceneActivation = false;

            float elapsed = 0f;
            while (!async.isDone && async.progress < 0.9f)
            {
                if (sceneLoadTimeoutSeconds > 0f && elapsed >= sceneLoadTimeoutSeconds)
                {
                    // A scene load that never finishes must not hold the player on the
                    // loader. Let it through and say so: a late game beats a dead screen.
                    LustLog.Warn($"[LustHouse] '{title}' did not finish loading within " +
                                 $"{sceneLoadTimeoutSeconds}s; activating it anyway.");
                    async.allowSceneActivation = true;
                    _launching = false;
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                _loadingScreen?.SetProgress(async.progress / 0.9f, "Preparing " + title + "…");
                yield return null;
            }

            async.allowSceneActivation = true;
            _launching = false;
        }

        /// <summary>
        /// A launch that cannot finish must never leave the player on the loading screen.
        /// The loader comes down, the guard is released so the next tap works, and the
        /// failure is said out loud rather than swallowed (plan §1.2 — never a dead end).
        /// </summary>
        private void FailLaunch(string title, string reason)
        {
            _launching = false;
            _loadingScreen?.Hide();
            LustLog.Error($"[LustHouse] Could not launch '{title}': {reason}");
            _presenter?.ShowEntryBlocked(title, reason);
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