using System;
using System.Collections.Generic;
using LustHouse.Avatar;
using LustHouse.Economy;
using UnityEngine;

namespace LustHouse.Core
{
    /// <summary>
    /// Constructs and wires the persistent services, then loads the save.
    ///
    /// This exists because <see cref="WalletService"/>, <see cref="InventoryService"/> and
    /// friends are <c>DontDestroyOnLoad</c> singletons whose <c>Awake</c> only runs if they
    /// are present in a loaded scene — nothing was creating them, so
    /// <c>AvatarComposer.Compose</c> returned null and <c>SlotGameRunner</c> survived only
    /// via a null guard (plan §0.2).
    ///
    /// Lives in <c>BootLoader.unity</c> as the very first thing that runs.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public class ServicesBootstrap : MonoBehaviour
    {
        [Header("Scene names")]
        public string bootSceneName = "BootLoader";
        public string lobbySceneName = "Lobby";
        public string slotSceneName = "Slot";

        [Header("Content")]
        [Tooltip("Cosmetic catalog. Empty in the greybox pass; the starter grant is skipped if unset.")]
        public List<CosmeticDefinition> cosmeticCatalog = new List<CosmeticDefinition>();

        [Tooltip("Id of the fallback Base item (plan §5.4: base_ivory).")]
        public string fallbackBaseId = "base_ivory";

        [Header("First run (plan §2.12)")]
        [Tooltip("Granted once, ever, on a brand-new profile. Never re-granted.")]
        public long FirstRunCoins = 5000;

        public long FirstRunGems = 25;

        /// <summary>Ledger note that marks the once-ever first-run grant.</summary>
        public const string FirstRunPackNote = "first-run pack";

        [Header("Autosave")]
        public bool autosave = true;
        public float autosaveDelaySeconds = 2f;

        public SaveSystem Save { get; private set; }
        public static ServicesBootstrap Instance { get; private set; }
        public bool LoadedFromCorruptSave { get; private set; }
        public string LoadWarning { get; private set; }

        private GameObject _root;
        private float _dirtyTimer = float.MaxValue;
        private bool _dirty;

        private void Awake()
        {
            _root = new GameObject("~LustHouseServices");
            DontDestroyOnLoad(_root);

            var wallet = _root.AddComponent<WalletService>();
            var inventory = _root.AddComponent<InventoryService>();
            var avatar = _root.AddComponent<AvatarComposer>();
            var progression = _root.AddComponent<PlayerProgression>();
            var dailyBonus = _root.AddComponent<DailyBonusService>();

            avatar.SetCatalog(cosmeticCatalog, fallbackBaseId);

            wallet.MarkedDirty = MarkDirty;
            inventory.MarkedDirty = MarkDirty;
            progression.MarkedDirty = MarkDirty;

            Save = SaveSystem.Default;
            LoadProgress();
            GrantFirstRunPack();

            Instance = this;

            inventory.GrantStarterSet(cosmeticCatalog);

            if (!string.IsNullOrEmpty(LoadWarning))
                EventBus.Publish(new SaveRecovered { warning = LoadWarning });
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Creates the services if they are not already alive.
        ///
        /// Services live in <c>BootLoader.unity</c> and persist across scene loads, but
        /// opening <c>Lobby</c> or <c>Slot</c> directly for QA must not produce a null
        /// <c>WalletService.Instance</c> and a cascade of silent no-ops.
        /// </summary>
        public static ServicesBootstrap EnsureExists()
        {
            if (Instance != null) return Instance;

            var existing = FindFirstObjectByType<ServicesBootstrap>();
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }

            var host = new GameObject("Services");
            return host.AddComponent<ServicesBootstrap>();
        }

        /// <summary>
        /// Grants the first-run pack (plan §2.12: "FREE 5,000 COINS + 25 GEMS").
        ///
        /// Written straight to the ledger with <see cref="LedgerReason.DailyGift"/> so it
        /// is auditable and cannot be re-claimed. This is what makes guardrail 1 true — a
        /// fresh player can bet immediately without any purchase.
        /// </summary>
        private void GrantFirstRunPack()
        {
            var wallet = WalletService.Instance;
            if (wallet == null) return;

            // Once ever, ever. Derived from the ledger rather than a saved boolean, so it
            // cannot drift out of sync and cannot be re-claimed by editing a flag.
            var gems = wallet.Account(Currency.Gems);
            if (gems != null && gems.ContainsEntry(LedgerReason.DailyGift, FirstRunPackNote)) return;

            wallet.TryGrant(Currency.Coins, FirstRunCoins, LedgerReason.DailyGift, FirstRunPackNote);
            wallet.TryGrant(Currency.Gems, FirstRunGems, LedgerReason.DailyGift, FirstRunPackNote);

            MarkDirty();
            SaveNow();
        }

        private void LoadProgress()
        {
            var result = Save.Load();

            WalletService.Instance.RestoreState(result.document.wallet);
            InventoryService.Instance.RestoreState(result.document.inventory);
            PlayerProgression.Instance.RestoreFrom(result.document.player);

            LoadedFromCorruptSave = !string.IsNullOrEmpty(result.warning);
            LoadWarning = result.warning;
        }

        // ---- save plumbing ----------------------------------------------------

        public void MarkDirty()
        {
            _dirty = true;
            _dirtyTimer = 0f;
        }

        private void Update()
        {
            if (!autosave || !_dirty) return;

            _dirtyTimer += Time.unscaledDeltaTime;
            if (_dirtyTimer < autosaveDelaySeconds) return;

            SaveNow();
        }

        private void OnApplicationPause(bool paused)
        {
            // Mobile can kill the process without ever calling OnApplicationQuit.
            if (paused && _dirty) SaveNow();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && _dirty) SaveNow();
        }

        private void OnApplicationQuit()
        {
            if (_dirty) SaveNow();
        }

        public void SaveNow()
        {
            if (Save == null) return;

            var doc = SaveDocument.CreateNew(DateTime.UtcNow);
            doc.player = PlayerProgression.Instance != null
                ? PlayerProgression.Instance.Capture()
                : doc.player;
            doc.wallet = WalletService.Instance != null
                ? WalletService.Instance.CaptureState()
                : doc.wallet;
            doc.inventory = InventoryService.Instance != null
                ? InventoryService.Instance.CaptureState()
                : doc.inventory;

            Save.Save(doc);
            _dirty = false;
            _dirtyTimer = float.MaxValue;
        }

        /// <summary>
        /// Club → "delete account". Irreversible (plan §2.10). The save file is backed up
        /// rather than erased outright so a support round-trip is still possible.
        /// </summary>
        public void DeleteAccount()
        {
            Save.Delete();

            if (WalletService.Instance != null)
                WalletService.Instance.RestoreState(WalletState.CreateNew());
            if (InventoryService.Instance != null)
                InventoryService.Instance.RestoreState(new InventoryState());
            if (PlayerProgression.Instance != null)
                PlayerProgression.Instance.RestoreFrom(PlayerState.CreateNew(DateTime.UtcNow));

            MarkDirty();
            SaveNow();
        }

        public void LoadScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
        }
    }
}