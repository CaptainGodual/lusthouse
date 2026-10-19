using System;
using LustHouse.Core;
using UnityEngine;

namespace LustHouse.Economy
{
    /// <summary>
    /// Account level + XP, and the seven VIP tiers driven by non-spendable Lust Points
    /// (plan §2.5, §4.1).
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerProgression : MonoBehaviour
    {
        public static PlayerProgression Instance { get; private set; }

        [Tooltip("XP required to advance from the given level to the next.")]
        public int[] xpCurve = { 100, 150, 220, 300, 400, 520, 660, 820, 1000, 1200 };

        [Tooltip("Coins granted on each level-up.")]
        public long levelRewardCoins = 1000;

        /// <summary>The seven tiers, in order. Lust Points thresholds are placeholders to be tuned in alpha.</summary>
        [Serializable]
        public struct VipTier
        {
            public string name;
            public long lustThreshold;
            public Color accent;
            [Tooltip("Animated prestige treatment — frame and ignition animation.")]
            public bool animated;
        }

        public VipTier[] vipTiers =
        {
            new VipTier { name = "Bronze",     lustThreshold = 0,       accent = new Color(0.804f, 0.498f, 0.196f), animated = false },
            new VipTier { name = "Silver",     lustThreshold = 15000,   accent = new Color(0.753f, 0.753f, 0.753f), animated = false },
            new VipTier { name = "Gold",       lustThreshold = 45000,   accent = new Color(0.910f, 0.765f, 0.420f), animated = false },
            new VipTier { name = "Platinum",   lustThreshold = 110000,  accent = new Color(0.898f, 0.894f, 0.886f), animated = false },
            new VipTier { name = "Obsidian",   lustThreshold = 250000,  accent = new Color(0.169f, 0.141f, 0.220f), animated = true  },
            new VipTier { name = "Ruby",       lustThreshold = 500000,  accent = new Color(0.824f, 0.220f, 0.298f), animated = true  },
            new VipTier { name = "The House",  lustThreshold = 1000000, accent = new Color(1.000f, 0.239f, 0.545f), animated = true  }
        };

        /// <summary>Set by the bootstrap so a mutation marks the save dirty.</summary>
        public Action MarkedDirty;

        public string DisplayName { get; private set; } = "Player";
        public int Level { get; private set; } = 1;
        public long Xp { get; private set; }
        public int VipTierIndex { get; private set; } = 1;
        public long LifetimeLustPoints { get; private set; }
        public bool AgeConfirmed { get; private set; }
        public long TotalSpins { get; private set; }
        public long TotalWagered { get; private set; }
        public long BiggestWin { get; private set; }
        public int LoginStreak { get; private set; }
        public bool F2PPromptPending { get; private set; } = true;
        public string LastDailyGiftUtc { get; private set; } = "";

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public VipTier CurrentVipTier => TierAt(VipTierIndex);

        public VipTier TierAt(int oneBasedIndex)
        {
            if (vipTiers == null || vipTiers.Length == 0) return default;
            int i = Mathf.Clamp(oneBasedIndex - 1, 0, vipTiers.Length - 1);
            return vipTiers[i];
        }

        public int XpForNextLevel()
        {
            if (xpCurve == null || xpCurve.Length == 0) return 100;
            int i = Mathf.Clamp(Level - 1, 0, xpCurve.Length - 1);
            return xpCurve[i];
        }

        // ---- mutation ---------------------------------------------------------

        /// <summary>
        /// Records Lust Points from a bet. Re-evaluates the VIP tier and fires an ignition
        /// event on change.
        /// </summary>
        public void AddLustPoints(long amount)
        {
            if (amount <= 0) return;
            LifetimeLustPoints += amount;

            int tier = ResolveTier(LifetimeLustPoints);
            if (tier != VipTierIndex)
            {
                VipTierIndex = tier;
                EventBus.Publish(new VipTierChanged
                {
                    newTier = tier,
                    tierName = TierAt(tier).name
                });
            }

            MarkedDirty?.Invoke();
        }

        public void AddXp(long amount)
        {
            if (amount <= 0) return;
            Xp += amount;

            // Level up while there is enough XP for the next step.
            while (Xp >= XpForNextLevel())
            {
                Xp -= XpForNextLevel();
                Level++;

                EventBus.Publish(new LevelUp { newLevel = Level, xpReward = levelRewardCoins });

                if (WalletService.Instance != null)
                    WalletService.Instance.TryGrant(Currency.Coins, levelRewardCoins, LedgerReason.LevelReward, $"level {Level}");
            }

            MarkedDirty?.Invoke();
        }

        public void RecordSpin(long bet, long win)
        {
            TotalSpins++;
            TotalWagered += bet;
            if (win > BiggestWin) BiggestWin = win;
            MarkedDirty?.Invoke();
        }

        public void ConfirmAge()
        {
            AgeConfirmed = true;
            MarkedDirty?.Invoke();
        }

        public void SetDisplayName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            DisplayName = name.Length > 16 ? name.Substring(0, 16) : name;
            MarkedDirty?.Invoke();
        }

        public void ConsumeF2PPrompt() => F2PPromptPending = false;

        public void NoteDailyGiftClaim(string utc) => LastDailyGiftUtc = utc ?? "";

        public void ResetDailyGiftClaim() => LastDailyGiftUtc = "";

        private int ResolveTier(long lustPoints)
        {
            int tier = 1;
            for (int i = 0; i < vipTiers.Length; i++)
                if (lustPoints >= vipTiers[i].lustThreshold) tier = i + 1;
            return tier;
        }

        // ---- persistence ------------------------------------------------------

        public void RestoreFrom(PlayerState state)
        {
            if (state == null) return;

            DisplayName = string.IsNullOrEmpty(state.displayName) ? "Player" : state.displayName;
            Level = Mathf.Max(1, state.level);
            Xp = state.xp;
            VipTierIndex = Mathf.Clamp(state.vipTier, 1, vipTiers.Length);
            LifetimeLustPoints = state.lifetimeLustPoints;
            AgeConfirmed = state.ageConfirmed;
            TotalSpins = state.totalSpins;
            TotalWagered = state.totalWagered;
            BiggestWin = state.biggestWin;
            LoginStreak = state.loginStreak;
            LastDailyGiftUtc = state.lastDailyGiftUtc ?? "";

            // A hand-edited save must not be able to grant a tier without the points.
            VipTierIndex = ResolveTier(LifetimeLustPoints);
        }

        public PlayerState Capture() => new PlayerState
        {
            displayName = DisplayName,
            level = Level,
            xp = Xp,
            vipTier = VipTierIndex,
            lifetimeLustPoints = LifetimeLustPoints,
            memberSinceUtc = DateTime.UtcNow.ToString("o"),
            loginStreak = LoginStreak,
            lastDailyGiftUtc = LastDailyGiftUtc,
            totalSpins = TotalSpins,
            totalWagered = TotalWagered,
            biggestWin = BiggestWin,
            ageConfirmed = AgeConfirmed,
            ageConfirmedUtc = AgeConfirmed ? DateTime.UtcNow.ToString("o") : ""
        };
    }
}