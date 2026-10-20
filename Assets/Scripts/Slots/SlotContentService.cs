using System;
using System.Collections;
using LustHouse.Core;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

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
    /// Progress is real: <c>GetDownloadSizeAsync</c> reports how many bytes are missing, and
    /// the download handle reports bytes transferred. Nothing here is a timer.
    /// </summary>
    [DisallowMultipleComponent]
    public class SlotContentService : MonoBehaviour
    {
        private bool _initialised;

        /// <summary>Initializes Addressables once, lazily. Safe to call repeatedly.</summary>
        public IEnumerator EnsureInitialised()
        {
            if (_initialised) yield break;
            _initialised = true;

            // Auto-release: this is a one-shot global init, not a handle we keep.
            yield return Addressables.InitializeAsync();
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

            // An unknown key is a configuration mistake, not a crash. Addressables throws
            // InvalidKeyException out of GetDownloadSizeAsync, and the loader must fall back
            // to the bundled content rather than strand the player (plan §1.3). The locations
            // call does not throw for a missing key -- it comes back empty -- so the check
            // has to happen before the size call.
            var locations = Addressables.LoadResourceLocationsAsync(key);
            yield return locations;

            if (!locations.IsValid() || locations.Status != AsyncOperationStatus.Succeeded ||
                locations.Result == null || locations.Result.Count == 0)
            {
                LustLog.Warn($"[LustHouse] No addressable pack '{key}'; continuing with the " +
                             "bundled content.");
                report(1f);
                yield break;
            }

            var size = Addressables.GetDownloadSizeAsync(key);
            yield return size;

            if (!size.IsValid() || size.Status != AsyncOperationStatus.Succeeded)
            {
                LustLog.Warn($"[LustHouse] Could not resolve pack '{key}'; continuing with the " +
                             "bundled content.");
                report(1f);
                yield break;
            }

            long missing = size.Result is long l ? l : 0L;
            if (missing <= 0L)
            {
                report(1f);
                yield break;
            }

            var download = Addressables.DownloadDependenciesAsync(key, true);
            while (!download.IsDone)
            {
                report(Mathf.Clamp01(download.PercentComplete));
                yield return null;
            }

            if (download.Status != AsyncOperationStatus.Succeeded)
            {
                LustLog.Warn($"[LustHouse] Pack '{key}' failed to download; continuing with the " +
                             "bundled content.");
            }

            report(1f);
        }

        private static void Ignore(float _) { }
    }
}
