using System;
using System.Collections.Generic;

namespace LustHouse.Core
{
    /// <summary>
    /// The single save document. Wallet, inventory and player state live in one object so
    /// a migration can never half-apply them (plan §5.2).
    /// </summary>
    [Serializable]
    public class SaveDocument
    {
        /// <summary>Bump when the shape changes and add a migration in <see cref="SaveMigration"/>.</summary>
        public const int CurrentSchemaVersion = 2;

        public int schemaVersion = CurrentSchemaVersion;
        public string savedAtUtc = "";
        public bool migrated = false;

        public PlayerState player = new PlayerState();
        public WalletState wallet = new WalletState();
        public InventoryState inventory = new InventoryState();
        public SettingsState settings = new SettingsState();

        /// <summary>
        /// Live progressive pot balances, keyed "{gameId}:{tierName}". A key that is
        /// missing falls back to the tier's seedValue in the content — the save carries
        /// only what has been won away from the seed (plan §3.2).
        /// </summary>
        public Dictionary<string, long> progressive = new Dictionary<string, long>();

        public static SaveDocument CreateNew(DateTime nowUtc)
        {
            return new SaveDocument
            {
                schemaVersion = CurrentSchemaVersion,
                savedAtUtc = nowUtc.ToString("o"),
                player = PlayerState.CreateNew(nowUtc),
                wallet = WalletState.CreateNew(),
                inventory = new InventoryState(),
                settings = new SettingsState(),
                progressive = new Dictionary<string, long>()
            };
        }
    }

    [Serializable]
    public class PlayerState
    {
        public string displayName = "Player";
        public int level = 1;
        public long xp = 0;
        public int vipTier = 1;
        public long lifetimeLustPoints = 0;
        public string memberSinceUtc = "";
        public int loginStreak = 0;
        public string lastDailyGiftUtc = "";

        // Stats block (plan §2.10)
        public long totalSpins = 0;
        public long totalWagered = 0;
        public long biggestWin = 0;
        public string favouriteGameId = "";

        // Guardrail 3
        public bool ageConfirmed = false;
        public string ageConfirmedUtc = "";

        // Public build: disclosure + responsible play (plan §1.1, §1.2)
        /// <summary>True once the probability disclosure has been shown. Never re-shown.</summary>
        public bool disclosureSeen = false;

        /// <summary>
        /// Self-exclusion window. Empty means not excluded. A permanent exclusion is
        /// "9999-12-31T23:59:59Z" — one field, no special cases, and it is irreversible
        /// because there is no in-game path that writes an empty string back.
        /// </summary>
        public string selfExcludedUntilUtc = "";

        public static PlayerState CreateNew(DateTime nowUtc) => new PlayerState
        {
            memberSinceUtc = nowUtc.ToString("o")
        };
    }

    [Serializable]
    public class WalletState
    {
        public List<AccountState> accounts = new List<AccountState>();
        public int conversionsToday = 0;
        public string conversionDateUtc = "";

        public static WalletState CreateNew()
        {
            var s = new WalletState();
            foreach (Currency c in Enum.GetValues(typeof(Currency)))
                s.accounts.Add(new AccountState { currency = c });
            return s;
        }

        public AccountState Find(Currency c)
        {
            for (int i = 0; i < accounts.Count; i++)
                if (accounts[i].currency == c) return accounts[i];
            return null;
        }
    }

    [Serializable]
    public class AccountState
    {
        public Currency currency;
        public long balance;
        public List<LedgerEntry> ledger = new List<LedgerEntry>();
    }

    [Serializable]
    public class InventoryState
    {
        public List<string> ownedIds = new List<string>();
        /// <summary>One equipped id per category, keyed by enum name for schema stability.</summary>
        public List<EquippedSlotState> equipped = new List<EquippedSlotState>();
        public bool starterGrantApplied = false;
    }

    [Serializable]
    public class EquippedSlotState
    {
        public string category;
        public string itemId;
    }

    [Serializable]
    public class SettingsState
    {
        public float masterVolume = 1f;
        public float musicVolume = 1f;
        public float sfxVolume = 1f;
        public int graphicsQuality = -1;   // -1 = auto
        public bool reducedMotion = false;
        public string lastBuildVersion = "";
    }
}