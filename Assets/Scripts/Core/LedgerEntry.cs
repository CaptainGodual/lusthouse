using System;

namespace LustHouse.Core
{
    public enum LedgerReason
    {
        DailyGift, MissionReward, Spin, Purchase, PurchaseRefund,
        Conversion, TournamentEntry, VipReward, Admin
    }

    [Serializable]
    public struct LedgerEntry
    {
        public DateTime timestamp;
        public Currency currency;
        public long delta;
        public LedgerReason reason;
        public long balanceAfter;
        public string note;
    }
}