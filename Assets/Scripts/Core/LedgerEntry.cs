using System;

namespace LustHouse.Core
{
    public enum LedgerReason
    {
        DailyGift,
        MissionReward,
        Spin,
        SpinPayout,
        Purchase,
        PurchaseRefund,
        Conversion,
        TournamentEntry,
        /// <summary>
        /// No emitter exists in v1 — the friends list is deferred to 1.1. Retained so a
        /// future gifting feature has a stable code; do not add call sites without the
        /// destination screen. See plan §2.3.
        /// </summary>
        PvpGift,
        VipReward,
        LevelReward,
        ConversionRefund,
        /// <summary>
        /// A progressive jackpot paid out of the house drip, not the paytable. Written as its
        /// own line so a player can tell a base win from a jackpot in the ledger, and so the
        /// RTP gate can measure the base game without the progressive term.
        /// </summary>
        Jackpot,
        Admin
    }

    /// <summary>
    /// One immutable line in the wallet ledger. Every balance change writes exactly one
    /// of these, and the balance is derived from the sum of the deltas (plan §4.5).
    /// </summary>
    [Serializable]
    public struct LedgerEntry
    {
        public long sequence;
        public DateTime timestamp;
        public Currency currency;
        public long delta;
        public LedgerReason reason;
        public long balanceAfter;
        public string note;

        public override string ToString() =>
            $"#{sequence} {timestamp:yyyy-MM-dd HH:mm:ss} {currency} {delta:+#;-#;0} -> {balanceAfter} ({reason})";
    }
}