using System;
using System.Collections;
using System.Collections.Generic;
using LustHouse.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace LustHouse.Catalog
{
    /// <summary>
    /// Owns the lobby's live content configuration.
    ///
    /// Starts from the shipped <see cref="LobbyPromoConfig"/> asset, then optionally
    /// overlays a remote JSON payload. Boot must never block on the network, so the fetch
    /// runs after the lobby is already usable and a failure is a non-blocking warning
    /// (plan §1.3, §2.13).
    /// </summary>
    [DisallowMultipleComponent]
    public class PromoService : MonoBehaviour
    {
        [Header("Content")]
        public GameCatalog catalog;
        public LobbyPromoConfig localConfig;

        [Header("Reduced motion (plan §2.14)")]
        [Tooltip("When set, the carousel does not auto-advance.")]
        public bool reducedMotion = false;

        /// <summary>The live config. A clone — the shipped asset is never mutated.</summary>
        public LobbyPromoConfig Active { get; private set; }

        /// <summary>Raised when content changes and the lobby should rebuild.</summary>
        public event Action ContentChanged;

        /// <summary>Non-blocking notice, e.g. "remote config unavailable".</summary>
        public event Action<string> Warning;

        private Coroutine _fetch;
        private bool _warnedOffline;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            Reload();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public static PromoService Instance { get; private set; }

        /// <summary>Rebuilds <see cref="Active"/> from the shipped asset.</summary>
        public void Reload()
        {
            Active = localConfig != null ? localConfig.Clone() : LobbyPromoConfig.CreateDefault();
            DeriveDynamicSlides();
            ContentChanged?.Invoke();
        }

        /// <summary>
        /// Fills in the Newest and Jackpot slides from the catalog, so new games surface
        /// automatically without anyone editing the config (the point of the request).
        /// Explicitly configured slides always win.
        /// </summary>
        private void DeriveDynamicSlides()
        {
            if (Active == null || catalog == null) return;

            bool hasNewest = false, hasJackpot = false;
            foreach (var slide in Active.slides)
            {
                if (slide == null) continue;
                if (slide.kind == PromoSlideKind.Newest) hasNewest = true;
                if (slide.kind == PromoSlideKind.Jackpot) hasJackpot = true;
            }

            if (!hasNewest)
            {
                var newest = NewestGame();
                if (newest != null)
                {
                    Active.slides.Add(new PromoSlide
                    {
                        kind = PromoSlideKind.Newest,
                        gameId = newest.id,
                        headline = "N E W   I N   T H E   H O U S E",
                        subline = newest.Title,
                        ctaLabel = "PLAY",
                        accent = new Color32(0x37, 0xE0, 0xD8, 0xFF)
                    });
                }
            }

            if (!hasJackpot)
            {
                var jackpot = ProgressiveGame();
                if (jackpot != null)
                {
                    Active.slides.Add(new PromoSlide
                    {
                        kind = PromoSlideKind.Jackpot,
                        gameId = jackpot.id,
                        headline = "T H E   J A C K P O T",
                        subline = $"{jackpot.Title} · progressive",
                        ctaLabel = "PLAY",
                        accent = new Color32(0xFF, 0x3D, 0x8B, 0xFF)
                    });
                }
            }
        }

        // ---- catalog queries --------------------------------------------------

        public GameCatalog.Entry NewestGame()
        {
            GameCatalog.Entry best = null;
            DateTime bestDate = DateTime.MinValue;

            if (catalog == null) return null;

            foreach (var entry in catalog.games)
            {
                if (entry?.definition == null) continue;
                DateTime released = entry.ReleasedUtc;
                if (released <= bestDate) continue;
                bestDate = released;
                best = entry;
            }

            // Nothing has a release date yet — fall back to catalog order.
            return best ?? (catalog.games.Count > 0 ? catalog.games[0] : null);
        }

        public GameCatalog.Entry ProgressiveGame()
        {
            if (catalog == null) return null;
            foreach (var entry in catalog.games)
                if (entry?.definition != null && entry.definition.progressive) return entry;
            return null;
        }

        /// <summary>Is this entry inside the New window (plan §2.7 'New' tab)?</summary>
        public bool IsNew(GameCatalog.Entry entry, DateTime nowUtc)
        {
            if (entry == null || Active == null) return false;

            DateTime released = entry.ReleasedUtc;
            if (released == default) return false;

            return (nowUtc - released).TotalDays >= 0
                && (nowUtc - released).TotalDays <= Active.newWindowDays;
        }

        public bool IsNew(GameCatalog.Entry entry) => IsNew(entry, DateTime.UtcNow);

        /// <summary>Resolves a configured gameId to a catalog entry, or null.</summary>
        public GameCatalog.Entry Find(string gameId)
        {
            if (catalog == null || string.IsNullOrWhiteSpace(gameId)) return null;
            return catalog.Find(gameId);
        }

        // ---- remote override --------------------------------------------------

        /// <summary>
        /// Fetches and overlays the remote payload. Call after the lobby is visible — the
        /// UI updates in place when it lands, so there is no reason to make anyone wait.
        /// </summary>
        public void FetchRemoteOverride()
        {
            if (Active == null || !Active.remoteOverrideEnabled) return;
            if (string.IsNullOrWhiteSpace(Active.remoteConfigUrl))
            {
                RaiseWarning("Remote config is enabled but no URL is set.");
                return;
            }

            if (_fetch != null) StopCoroutine(_fetch);
            _fetch = StartCoroutine(FetchRoutine());
        }

        private IEnumerator FetchRoutine()
        {
            float timeout = Active != null ? Active.remoteTimeoutSeconds : 3f;

            using (var request = UnityWebRequest.Get(Active.remoteConfigUrl))
            {
                request.timeout = Mathf.CeilToInt(timeout);

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    _warnedOffline = true;
                    RaiseWarning($"Remote config unavailable ({request.error}); using the shipped config.");
                    yield break;
                }

                var result = PromoConfigValidator.Apply(Active, request.downloadHandler.text, KnownGameIds());

                if (result.applied)
                {
                    DeriveDynamicSlides();
                    ContentChanged?.Invoke();
                }

                if (!string.IsNullOrEmpty(result.warning)) RaiseWarning(result.warning);
            }

            _fetch = null;
        }

        private HashSet<string> KnownGameIds()
        {
            var ids = new HashSet<string>();
            if (catalog == null) return ids;
            foreach (var entry in catalog.games)
                if (entry != null && !string.IsNullOrEmpty(entry.id)) ids.Add(entry.id);
            return ids;
        }

        /// <summary>True once a fetch has failed, for the persistent OFFLINE chip (§2.13).</summary>
        public bool IsOffline => _warnedOffline;

        private void RaiseWarning(string message)
        {
            LustLog.Warn($"[LustHouse] {message}");
            Warning?.Invoke(message);
        }
    }
}