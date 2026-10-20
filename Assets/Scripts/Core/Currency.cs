namespace LustHouse.Core
{
    /// <summary>The three wallet currencies. See plan §4.1.</summary>
    public enum Currency
    {
        Coins,
        Gems,
        LustPoints
    }

    /// <summary>
    /// Structural currency rules. These live here rather than at call sites so that a
    /// violation is impossible to write by accident — plan §5.2 ("Lust Points are
    /// structurally non-spendable").
    /// </summary>
    public static class CurrencyRules
    {
        /// <summary>1 Gem converts to this many Coins. One-directional; see plan §4.3 rule 2.</summary>
        public const long GemsToCoinsRate = 1000;

        /// <summary>Hard cap on Gems→Coins conversions per day. Kills coin-washing.</summary>
        public const int MaxConversionsPerDay = 3;

        public static bool IsSpendable(Currency c) => c != Currency.LustPoints;

        public static bool IsWagerable(Currency c) => c == Currency.Coins;

        /// <summary>Only Gems are purchasable with real money. Never Coins.</summary>
        public static bool IsPremium(Currency c) => c == Currency.Gems;

        public static bool CanBeDebited(Currency c, long amount) =>
            amount > 0 && IsSpendable(c);

        public static string Symbol(Currency c)
        {
            switch (c)
            {
                case Currency.Coins: return "◎";
                case Currency.Gems: return "⬡";
                default: return "♥";
            }
        }
    }
}