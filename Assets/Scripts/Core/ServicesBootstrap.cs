using System;
using System.Collections.Generic;
using LustHouse.Avatar;
using LustHouse.Audio;
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
        public long FirstRunCoins = 50000;

        public long FirstRunGems = 25;

        /// <summary>Ledger note that marks the once-ever first-run grant.</summary>
        public const string FirstRunPackNote = "first-run pack";

        /// <summary>
        /// Ledger note that marks the once-ever starting stake granted to a player who
        /// declines the first-run offer.
        ///
        /// A different note from the pack's, so the two are distinguishable in the
        /// ledger — and so "has the offer been answered?" can be answered from the
        /// ledger alone.
        /// </summary>
        public const string FirstRunStakeNote = "first-run stake";

        [Header("Content")]
        [Tooltip("The catalogue. Loaded from Resources when it is not assigned, so the " +
                 "boot screen can publish the probability disclosure before the lobby exists.")]
        public Catalog.GameCatalog catalog;

        [Header("Autosave")]
        public bool autosave = true;
        public float autosaveDelaySeconds = 2f;

        public SaveSystem Save { get; private set; }
        public static ServicesBootstrap Instance { get; private set; }
        public bool LoadedFromCorruptSave { get; private set; }
        public string LoadWarning { get; private set; }

        /// <summary>
        /// The live progressive pot balances, carried through the save (plan §3.2).
        ///
        /// Keyed "{gameId}:{tierName}". A key that is missing falls back to the tier's
        /// seed value in the content, so the block only ever carries what has been won
        /// away from the seed.
        /// </summary>
        public Dictionary<string, long> ProgressivePots { get; } = new Dictionary<string, long>();

        private GameObject _root;
        private AudioService _audio;
        private ResponsiblePlayService _responsiblePlay;
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

            // The responsible-play service rides here too: the session timer has to
            // keep counting across scene changes, and the self-exclusion has to be
            // checked by every screen that can open a game.
            _responsiblePlay = _root.AddComponent<ResponsiblePlayService>();


            // The sound system rides on the same root, so it survives every scene
            // change and the player's volume settings survive with it.
            _audio = _root.AddComponent<AudioService>();

            // The house bank is the always-on one: the lobby's music and any other
            // cue no game claims. It is loaded from Resources rather than assigned in
            // a scene because this whole root is constructed in code — there is no
            // inspector here to put a reference on. A missing bank is not an error:
            // every cue falls back to the synthesiser, which is the point of it.
            var houseBank = Resources.Load<AudioBank>("Audio/AudioBank_House");
            if (houseBank != null) _audio.SetHouseBank(houseBank);

            // The catalogue comes from Resources when it is not assigned. The boot
            // screen needs the games' maths to publish the probability disclosure, and
            // the boot scene has no reference to the lobby's catalogue — without this
            // the disclosure would have nothing to publish.
            if (catalog == null) catalog = Resources.Load<Catalog.GameCatalog>("Catalog/GameCatalog");
            if (catalog == null)
                LustLog.Warn("[LustHouse] No game catalogue; the probability disclosure has " +
                             "nothing to publish.");


            avatar.SetCatalog(cosmeticCatalog, fallbackBaseId);

            wallet.MarkedDirty = MarkDirty;
            inventory.MarkedDirty = MarkDirty;
            progression.MarkedDirty = MarkDirty;

            Save = SaveSystem.Default;
            LoadProgress();

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
        /// Grants the first-run pack (plan §2.12: "FREE 50,000 COINS + 25 GEMS").
        ///
        /// Written straight to the ledger with <see cref="LedgerReason.DailyGift"/> so it
        /// is auditable and cannot be re-claimed. This is what makes guardrail 1 true — a
        /// fresh player can bet immediately without any purchase.
        ///
        /// The grant is not silent any more: the player is offered it once, in the
        /// prompt that follows the age gate, and declining it grants a starting stake
        /// instead. Either answer is recorded in the ledger, and either one means the
        /// offer is never made again.
        /// </summary>
        public bool GrantFirstRunPack()
        {
            var wallet = WalletService.Instance;
            if (wallet == null) return false;

            // Once ever, ever. Derived from the ledger rather than a saved boolean, so it
            // cannot drift out of sync and cannot be re-claimed by editing a flag.
            if (FirstRunPromptAnswered) return false;

            wallet.TryGrant(Currency.Coins, FirstRunCoins, LedgerReason.DailyGift, FirstRunPackNote);
            wallet.TryGrant(Currency.Gems, FirstRunGems, LedgerReason.DailyGift, FirstRunPackNote);

            MarkDirty();
            SaveNow();
            return true;
        }

        /// <summary>
        /// Grants the starting stake to a player who declined the first-run offer.
        ///
        /// The amount is one minimum bet — enough to place a bet, so declining cannot
        /// leave the player at zero with nothing to do. Declining is a legitimate
        /// answer and must not be a punishment.
        /// </summary>
        public bool GrantStartingStake(long minimumBet)
        {
            var wallet = WalletService.Instance;
            if (wallet == null) return false;

            if (FirstRunPromptAnswered) return false;

            long amount = Math.Max(1L, minimumBet);
            wallet.TryGrant(Currency.Coins, amount, LedgerReason.DailyGift, FirstRunStakeNote);

            MarkDirty();
            SaveNow();
            return true;
        }

        /// <summary>
        /// Has the player answered the first-run offer, either way?
        ///
        /// Answered from the ledger rather than from a saved flag, so it cannot drift
        /// out of sync with the balance and cannot be reset by editing a field.
        /// </summary>
        public bool FirstRunPromptAnswered
        {
            get
            {
                var wallet = WalletService.Instance;
                if (wallet == null) return false;

                var gems = wallet.Account(Currency.Gems);
                if (gems != null && gems.ContainsEntry(LedgerReason.DailyGift, FirstRunPackNote)) return true;

                var coins = wallet.Account(Currency.Coins);
                if (coins != null && coins.ContainsEntry(LedgerReason.DailyGift, FirstRunStakeNote)) return true;

                return false;
            }
        }

        private void LoadProgress()
        {
            var result = Save.Load();

            WalletService.Instance.RestoreState(result.document.wallet);
            InventoryService.Instance.RestoreState(result.document.inventory);
            PlayerProgression.Instance.RestoreFrom(result.document.player);

            // The progressive pots come back with the rest of the profile. A missing
            // key falls back to the tier's seed value in the content, so the block
            // only ever carries what has been won away from the seed.
            ProgressivePots.Clear();
            if (result.document.progressive != null)
            {
                foreach (var pair in result.document.progressive)
                    ProgressivePots[pair.Key] = pair.Value;
            }

            // The volumes were in the save all along and nothing ever read them, so
            // they reset to full every launch. They are read now.
            if (_audio != null) _audio.ApplyVolumesFrom(result.document.settings);

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

            // The progressive pot block travels with the rest of the profile.
            doc.progressive = new Dictionary<string, long>(ProgressivePots);


            // The settings block was never written, so the volumes were thrown away
            // on every save. Only the volume fields are touched — the rest of the
            // settings belong to whatever else owns them.
            if (_audio != null) _audio.CopyVolumesInto(doc.settings);

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
            LustLog.Info($"[LustHouse] ServicesBootstrap.LoadScene('{sceneName}').");
            UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
        }
    }
}