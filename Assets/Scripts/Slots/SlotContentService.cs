using System;
using System.Collections;
using System.Collections.Generic;
using LustHouse.Core;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace LustHouse.Slots
{
    /// <summary>
    /// Makes sure a slot game's pack is present on the device, fetching it from the
    /// Addressables remote catalog (the profile's <c>Remote.LoadPath</c>) when it is not.
    ///
    /// A game's pack is addressed by <see cref="SlotMachineDefinition.addressableKey"/>. A
    /// blank key means the game ships inside the build and there is nothing to fetch — the
    /// call returns immediately so a misconfigured catalog can never dead-end a launch.
    ///
    /// Progress is real: the download handle reports how much of the pack has arrived.
    /// Nothing here is a timer.
    ///
    /// Nothing in here is allowed to throw. The service runs inside the launch coroutine,
    /// and an exception there kills the coroutine — which leaves the player staring at the
    /// loading screen with no way forward. That is not a hypothetical: see
    /// <see cref="EnsureInitialised"/> for the Addressables trap this class exists to avoid.
    /// </summary>
    [DisallowMultipleComponent]
    public class SlotContentService : MonoBehaviour
    {
        [Header("Timeouts (seconds)")]
        [Tooltip("How long a pack download may take before the launch falls back to the " +
                 "bundled content. Zero waits for ever.")]
        public float timeoutSeconds = 30f;

        // ---- Addressables, once per process -------------------------------------
        //
        // Addressables is a process-wide singleton, and initialising it twice is not
        // supported. The second call re-enters AddressablesImpl.InitializeAsync, which
        // hands back the locator it built the first time by reaching into its locator
        // list. When the first attempt failed — a player build with no catalogue in it,
        // which is exactly what a build that never built the Addressables content looks
        // like — that list is empty, and the second call throws IndexOutOfRangeException.
        //
        // The exception escaped into the launch coroutine and killed it. The player saw a
        // loading screen that never went away: the first launch of a session worked, and
        // every launch after coming back from a game did not. The lobby scene is torn
        // down and rebuilt between a game and the lobby, so this service is a brand new
        // instance every time — the guard has to be static, not per instance.
        //
        // So: initialise once per process, remember the outcome, and never ask again.

        private static bool s_InitAttempted;
        private static bool s_Ready;

        /// <summary>How many times Addressables initialisation has been attempted.</summary>
        public static int InitAttempts { get; private set; }

        /// <summary>True when Addressables is up and the catalogue can be queried.</summary>
        public static bool Ready => s_Ready;

        /// <summary>
        /// Initializes Addressables once per process. Safe to call repeatedly.
        ///
        /// The attempt is made at most once for the lifetime of the app: a second
        /// <c>Addressables.InitializeAsync</c> is not a no-op, it is a crash waiting for
        /// the catalogue to be missing.
        /// </summary>
        public IEnumerator EnsureInitialised()
        {
            if (s_InitAttempted) yield break;

            s_InitAttempted = true;

            // Addressables may already be up — something else in the process started it.
            // Asking for it again is the dangerous path, so check first.
            if (HasLocators())
            {
                s_Ready = true;
                yield break;
            }

            InitAttempts++;

            AsyncOperationHandle<IResourceLocator> handle = default;
            bool threw = false;

            try
            {
                handle = Addressables.InitializeAsync();
            }
            catch (Exception e)
            {
                threw = true;
                LustLog.Exception("[LustHouse] Addressables initialisation threw", e);
            }

            if (threw) yield break;

            yield return WaitFor(handle);

            // The handle is released by Addressables the moment it completes, so its own
            // status cannot be trusted by the time we get here. The locator list is the
            // honest answer: it is empty when the initialisation failed.
            s_Ready = HasLocators();

            if (!s_Ready)
                LustLog.Warn("[LustHouse] Addressables has no catalogue; every game will " +
                             "use its bundled content.");
        }

        /// <summary>
        /// Downloads the pack for <paramref name="key"/> if the device does not already have
        /// it. Reports 0..1 as work completes.
        ///
        /// Every failure path still reports 1 and returns: a game that cannot be downloaded
        /// must fall back to its bundled content rather than strand the player on the loader
        /// (plan §1.3 — never block on the network).
        /// </summary>
        public IEnumerator EnsureDownloaded(string key, Action<float> report)
        {
            if (report == null) report = Ignore;

            if (string.IsNullOrWhiteSpace(key))
            {
                report(1f);
                yield break;
            }

            yield return EnsureInitialised();

            if (!s_Ready)
            {
                LustLog.Warn($"[LustHouse] No Addressables catalogue; '{key}' will use its " +
                             "bundled content.");
                report(1f);
                yield break;
            }

            // The pack's locations are read straight from the locator list rather than
            // through Addressables' asynchronous lookup. That lookup releases its handle
            // the instant it completes, so by the time a coroutine could look at the
            // result it has already been cleared — the locator list is the same data and
            // it does not expire.
            IList<IResourceLocation> locations = FindLocations(key);
            if (locations == null || locations.Count == 0)
            {
                LustLog.Warn($"[LustHouse] No addressable pack '{key}'; continuing with the " +
                             "bundled content.");
                report(1f);
                yield break;
            }

            // The download owns its handle rather than having Addressables release it, so
            // the status is still readable when it finishes and a failure can be told
            // from a success.
            AsyncOperationHandle download = default;
            bool threw = false;

            try
            {
                download = Addressables.DownloadDependenciesAsync(locations, false);
            }
            catch (Exception e)
            {
                threw = true;
                LustLog.Exception($"[LustHouse] Could not download pack '{key}'", e);
            }

            if (threw)
            {
                report(1f);
                yield break;
            }

            float elapsed = 0f;
            while (!download.IsDone)
            {
                if (timeoutSeconds > 0f && elapsed >= timeoutSeconds)
                {
                    LustLog.Warn($"[LustHouse] Pack '{key}' timed out after {timeoutSeconds}s; " +
                                 "continuing with the bundled content.");
                    break;
                }

                elapsed += Time.unscaledDeltaTime;
                report(Mathf.Clamp01(download.PercentComplete));
                yield return null;
            }

            if (download.IsValid())
            {
                if (download.Status != AsyncOperationStatus.Succeeded)
                    LustLog.Warn($"[LustHouse] Pack '{key}' failed to download; continuing " +
                                 "with the bundled content.");

                download.Release();
            }

            report(1f);
        }

        /// <summary>
        /// Waits for an Addressables handle without yielding the handle itself.
        ///
        /// <c>yield return handle</c> works only by accident: Addressables releases the
        /// handle the instant it completes, and a released handle reports "not done" for
        /// ever, so the wait either ends a frame early or never ends at all. Polling
        /// <c>IsDone</c> is explicit, and the timeout means a request that never answers
        /// cannot hold the player on the loading screen.
        /// </summary>
        private IEnumerator WaitFor(AsyncOperationHandle handle)
        {
            float elapsed = 0f;

            while (!handle.IsDone)
            {
                if (timeoutSeconds > 0f && elapsed >= timeoutSeconds)
                {
                    LustLog.Warn($"[LustHouse] An Addressables request timed out after " +
                                 $"{timeoutSeconds}s.");
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        /// <summary>
        /// The locations registered for <paramref name="key"/>, or null when there are none.
        /// Mirrors what Addressables' own lookup does, without the handle.
        /// </summary>
        private static IList<IResourceLocation> FindLocations(string key)
        {
            try
            {
                foreach (var locator in Addressables.ResourceLocators)
                {
                    if (locator == null) continue;

                    if (locator.Locate(key, typeof(object), out var locations) &&
                        locations != null && locations.Count > 0)
                    {
                        return locations;
                    }
                }
            }
            catch (Exception e)
            {
                LustLog.Exception($"[LustHouse] Could not look up pack '{key}'", e);
            }

            return null;
        }

        /// <summary>True when Addressables managed to build at least one locator.</summary>
        private static bool HasLocators()
        {
            try
            {
                foreach (IResourceLocator locator in Addressables.ResourceLocators)
                    if (locator != null) return true;
            }
            catch (Exception e)
            {
                LustLog.Exception("[LustHouse] Could not read the Addressables locators", e);
            }

            return false;
        }

        private static void Ignore(float _) { }
    }
}
