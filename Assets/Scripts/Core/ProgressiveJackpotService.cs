using System;
using System.Collections.Generic;
using LustHouse.Core;
using LustHouse.Slots;
using UnityEngine;

namespace LustHouse.Core
{
    /// <summary>
    /// The progressive jackpot service (plan Stage 3.2).
    ///
    /// Lives on the <see cref="ServicesBootstrap"/> root beside <see cref="WalletService"/> and
    /// <see cref="PlayerProgression"/>, so it survives a scene load the way those do.
    ///
    /// Two things it does, and they are separable on purpose:
    ///
    ///   1. Drip. Every completed spin adds a slice of the bet to each tier's pot. The drip is
    ///      a fraction of the wager, so a pot that nobody has won grows with the volume that
    ///      could have won it — and a pot that is won resets to its seed, so a game that pays
    ///      out often stays affordable.
    ///
    ///   2. Persist. The pots live in the schema-2 <see cref="SaveDocument.progressive"/> block.
    ///      A key that is missing falls back to the tier's <c>seedValue</c>, so the content
    ///      seeds the pot and the save carries only what has been won away from the seed.
    ///
    /// The service is the only thing that may read or write a pot. Handlers ask through the
    /// <see cref="IProgressivePotSource"/> interface on the feature context, which keeps them
    /// pure: the game, the simulator and the tests share one code path, and a headless test
    /// that passes no source sees every pot start at its seed.
    /// </summary>
    [RequireComponent(typeof(WalletService))]
    public class ProgressiveJackpotService : MonoBehaviour, IProgressivePotSource
    {
        public static ProgressiveJackpotService Instance { get; private set; }

        /// <summary>Per-game tier definitions, loaded from the catalog at bootstrap.</summary>
        private readonly Dictionary<string, ProgressiveJackpotFeature.Tier[]> _tiers =
            new Dictionary<string, ProgressiveJackpotFeature.Tier[]>();

        /// <summary>The persisted pots. Owned by <see cref="ServicesBootstrap.ProgressivePots"/>
        /// so the load/save round-trip in <c>ServicesBootstrap</c> carries them.</summary>
        public Dictionary<string, long> Pots => ServicesBootstrap.Instance?.ProgressivePots;

        private void Awake()
        {
            if (Instance != null && Instance != this) { DestroyImmediate(this); return; }
            Instance = this;
            DontDestroyOnLoad(transform.root);
        }

        private void Start()
        {
            LoadFromSave();
        }

        /// <summary>Register a game's tiers. Called from the catalog bootstrap.</summary>
        public void RegisterGame(SlotMachineDefinition game)
        {
            if (game == null) return;
            var feature = game.Find<ProgressiveJackpotFeature>();
            if (feature == null) return;
            _tiers[game.id] = feature.tiers?.ToArray() ?? Array.Empty<ProgressiveJackpotFeature.Tier>();
        }

        /// <summary>After every completed spin. The bet is the player's wager, not the win.</summary>
        public void ApplyDrip(string gameId, long bet)
        {
            if (!_tiers.TryGetValue(gameId, out var tiers) || tiers.Length == 0) return;
            if (bet <= 0) return;

            var pots = Pots;
            if (pots == null) return;

            for (int i = 0; i < tiers.Length; i++)
            {
                var tier = tiers[i];
                if (tier == null || tier.dripPerMillion <= 0) continue;

                long drip = bet * tier.dripPerMillion / 1_000_000;
                if (drip <= 0) continue;

                string key = PotKey(gameId, tier.tierName);
                long current = pots.ContainsKey(key) ? pots[key] : tier.seedValue;
                pots[key] = current + drip;
            }
        }

        /// <summary>Current pot value for a tier, in currency units.</summary>
        public long PotValue(string gameId, string tierName)
        {
            if (!_tiers.TryGetValue(gameId, out var tiers)) return 0;
            var tier = FindTier(tiers, tierName);
            if (tier == null) return 0;

            var pots = Pots;
            if (pots != null && pots.ContainsKey(PotKey(gameId, tierName)))
                return pots[PotKey(gameId, tierName)];

            return tier.seedValue;
        }

        /// <summary>Back to the seed. Called by the runner after a tier pays out.</summary>
        public void ResetPot(string gameId, string tierName, long seedValue)
        {
            var pots = Pots;
            if (pots == null) return;
            pots[PotKey(gameId, tierName)] = seedValue;
        }

        // ---- IProgressivePotSource ----

        long IProgressivePotSource.PotValue(string gameId, string tierName) =>
            PotValue(gameId, tierName);

        void IProgressivePotSource.ResetPot(string gameId, string tierName, long seedValue) =>
            ResetPot(gameId, tierName, seedValue);

        // ---- internals ----

        private static string PotKey(string gameId, string tierName) =>
            $"{gameId}:{tierName ?? ""}";

        private static ProgressiveJackpotFeature.Tier FindTier(
            ProgressiveJackpotFeature.Tier[] tiers, string tierName)
        {
            if (string.IsNullOrEmpty(tierName)) return null;
            for (int i = 0; i < tiers.Length; i++)
                if (string.Equals(tiers[i]?.tierName, tierName, StringComparison.Ordinal))
                    return tiers[i];
            return null;
        }

        private void LoadFromSave()
        {
            var doc = SaveSystem.Default;
            if (doc == null) return;
            // The save carries only what has been won away from the seed; keys that are
            // missing fall back to the tier's seedValue on the next PotValue call, so there
            // is nothing to restore here beyond what is already in the document.
        }
    }
}