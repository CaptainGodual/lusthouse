using LustHouse.Core;
using NUnit.Framework;

namespace LustHouse.Tests
{
    /// <summary>
    /// The economic guardrails, enforced automatically rather than by walking every
    /// button (plan §2.17, §5.8).
    /// </summary>
    public class GuardrailTests
    {
        [Test]
        public void LustPoints_IsStructurallyNonSpendable()
        {
            Assert.IsFalse(CurrencyRules.IsSpendable(Currency.LustPoints),
                "Lust Points must be non-spendable (plan §4.1).");
            Assert.IsTrue(CurrencyRules.IsSpendable(Currency.Coins));
            Assert.IsTrue(CurrencyRules.IsSpendable(Currency.Gems));
        }

        [Test]
        public void CurrencyAccount_RefusesDebitWhenNotSpendable()
        {
            var account = new CurrencyAccount(Currency.LustPoints);

            Assert.IsFalse(account.spendable);

            account.Apply(500, LedgerReason.Admin, "test");
            Assert.AreEqual(500, account.balance);

            // The account itself rejects it, so no caller can bypass WalletService.
            Assert.IsFalse(account.Apply(-100, LedgerReason.Spin, "should fail"));
            Assert.AreEqual(500, account.balance, "Balance must be untouched after a refused debit.");
            Assert.AreEqual(1, account.ledger.Count, "A refused debit must not write a ledger entry.");
        }

        [Test]
        public void CurrencyAccount_RefusesToOverdraw()
        {
            var account = new CurrencyAccount(Currency.Coins);

            account.Apply(100, LedgerReason.DailyGift);

            Assert.IsFalse(account.Apply(-150, LedgerReason.Spin));
            Assert.AreEqual(100, account.balance);
        }

        [Test]
        public void Balance_IsDerivedFromTheLedger()
        {
            var account = new CurrencyAccount(Currency.Coins);

            account.Apply(5000, LedgerReason.DailyGift);
            account.Apply(-1000, LedgerReason.Spin);
            account.Apply(300, LedgerReason.SpinPayout);

            Assert.AreEqual(account.balance, account.ReplayBalance(),
                "§4.5 requires the balance to equal the sum of ledger deltas.");
        }

        [Test]
        public void Ledger_TrimsButNeverRewritesHistory()
        {
            var account = new CurrencyAccount(Currency.Coins);

            for (int i = 0; i < CurrencyAccount.MaxPersistedLedgerEntries + 50; i++)
                account.Apply(1, LedgerReason.Spin, "spin " + i);

            Assert.LessOrEqual(account.ledger.Count, CurrencyAccount.MaxPersistedLedgerEntries);
            Assert.AreEqual(CurrencyAccount.MaxPersistedLedgerEntries + 50, account.balance,
                "Trimming the trail must not change the balance.");
        }

        [Test]
        public void OnlyGems_ArePremiumCurrency()
        {
            // Coins are never purchasable — that is guardrail §2.
            Assert.IsTrue(CurrencyRules.IsPremium(Currency.Gems));
            Assert.IsFalse(CurrencyRules.IsPremium(Currency.Coins));
            Assert.IsFalse(CurrencyRules.IsPremium(Currency.LustPoints));
        }

        [Test]
        public void Conversion_IsOneDirectional_AndCapped()
        {
            Assert.AreEqual(100, CurrencyRules.GemsToCoinsRate);
            Assert.AreEqual(3, CurrencyRules.MaxConversionsPerDay);

            // There is no Coins→Gems entry point anywhere: the rule lives as a data
            // constant, and WalletService exposes only TryConvertGemsToCoins.
            var methods = typeof(WalletService).GetMethods();
            foreach (var m in methods)
            {
                string name = m.Name.ToLowerInvariant();
                Assert.IsFalse(name.Contains("cointogem"),
                    "A Coins→Gems conversion would violate plan §4.3 rule 2.");
            }
        }

        [Test]
        public void CosmeticModel_ExposesNoCoinPriceField()
        {
            // §5.3 rule 1: the absence of the field is the enforcement.
            var fields = typeof(Economy.CosmeticDefinition).GetFields();
            foreach (var f in fields)
            {
                Assert.IsFalse(f.Name.ToLowerInvariant().Contains("coin"),
                    $"CosmeticDefinition must not expose a coin price field, found '{f.Name}'.");
            }
        }

        [Test]
        public void CosmeticModel_ExposesNoGameplayModifiers()
        {
            // §5.3 rule 3: cosmetics change appearance only.
            string[] banned = { "rtp", "odds", "payout", "betmultiplier", "wager", "power", "bonus" };
            foreach (var f in typeof(Economy.CosmeticDefinition).GetFields())
            {
                foreach (var b in banned)
                {
                    Assert.IsFalse(f.Name.ToLowerInvariant().Contains(b),
                        $"CosmeticDefinition must not carry a gameplay field, found '{f.Name}'.");
                }
            }
        }
    }
}