using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace LustHouse.Core
{
    /// <summary>
    /// A single ordered step in the save schema upgrade path.
    ///
    /// Migrations operate on the raw <see cref="JObject"/> rather than
    /// <see cref="SaveDocument"/> so they can read fields that no longer exist on the
    /// typed model — that is the whole point of a migration.
    /// </summary>
    public interface ISaveMigration
    {
        int FromVersion { get; }
        int ToVersion { get; }
        string Name { get; }
        void Apply(JObject root);
    }

    /// <summary>
    /// Runs registered migrations in ascending version order. Anything that cannot be
    /// brought up to <see cref="SaveDocument.CurrentSchemaVersion"/> is reported so the
    /// caller can fall back to defaults rather than crash (plan §2.13).
    /// </summary>
    public static class SaveMigrationRunner
    {
        private static readonly List<ISaveMigration> Migrations = new List<ISaveMigration>
        {
            new Migration_0_To_1(),
            new Migration_1_To_2()
        };

        public static IReadOnlyList<ISaveMigration> Registered => Migrations;

        public static void Register(ISaveMigration migration)
        {
            if (migration == null) return;
            if (Migrations.Exists(m => m.FromVersion == migration.FromVersion))
                throw new InvalidOperationException(
                    $"A migration from schema v{migration.FromVersion} is already registered.");
            Migrations.Add(migration);
            Migrations.Sort((a, b) => a.FromVersion.CompareTo(b.FromVersion));
        }

        /// <summary>
        /// Upgrades <paramref name="root"/> in place. Returns false when the document is
        /// from a future schema this build cannot understand — the caller must not
        /// overwrite it.
        /// </summary>
        public static bool Run(JObject root, out string error)
        {
            error = null;

            int version = root["schemaVersion"] != null
                ? root["schemaVersion"].Value<int>()
                : 0; // pre-schema saves are version 0

            if (version > SaveDocument.CurrentSchemaVersion)
            {
                error = $"Save is schema v{version}; this build understands up to " +
                        $"v{SaveDocument.CurrentSchemaVersion}.";
                return false;
            }

            if (version == SaveDocument.CurrentSchemaVersion) return true;

            for (int i = 0; i < Migrations.Count; i++)
            {
                var m = Migrations[i];
                if (m.FromVersion != version) continue;

                m.Apply(root);
                root["schemaVersion"] = m.ToVersion;
                version = m.ToVersion;
            }

            if (version != SaveDocument.CurrentSchemaVersion)
            {
                error = $"No migration path from schema v{version} to " +
                        $"v{SaveDocument.CurrentSchemaVersion}.";
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// v0 → v1. The original scaffold wrote a flat, unversioned document. This lifts it
    /// into the versioned <c>wallet.accounts</c> shape, moving any legacy flat balances
    /// across and keeping them auditable with a <see cref="LedgerReason.Admin"/> entry.
    /// </summary>
    public sealed class Migration_0_To_1 : ISaveMigration
    {
        public int FromVersion => 0;
        public int ToVersion => 1;
        public string Name => "AdoptVersionedWalletAccounts";

        public void Apply(JObject root)
        {
            if (root["wallet"] == null)
                root["wallet"] = new JObject();

            var wallet = (JObject)root["wallet"];
            if (wallet["accounts"] == null)
                wallet["accounts"] = new JArray();

            // Accounts must exist *before* legacy balances are adopted, otherwise there is
            // nothing to adopt them into and the balance is silently dropped.
            EnsureAccountPerCurrency(wallet);

            // Legacy top-level flat balances, e.g. { "coins": 5000, "gems": 0, "lust": 0 }
            AdoptLegacyBalance(root, wallet, "coins", Currency.Coins);
            AdoptLegacyBalance(root, wallet, "gems", Currency.Gems);
            AdoptLegacyBalance(root, wallet, "lust", Currency.LustPoints);

            if (wallet["conversionsToday"] == null) wallet["conversionsToday"] = 0;
            if (wallet["conversionDateUtc"] == null) wallet["conversionDateUtc"] = "";

            if (root["player"] == null) root["player"] = new JObject();
            if (root["inventory"] == null) root["inventory"] = new JObject();
            if (root["settings"] == null) root["settings"] = new JObject();
        }

        /// <summary>Guarantees exactly one account entry per currency, at balance 0.</summary>
        private static void EnsureAccountPerCurrency(JObject wallet)
        {
            var accounts = (JArray)wallet["accounts"];

            foreach (Currency c in Enum.GetValues(typeof(Currency)))
            {
                if (FindAccount(accounts, c) != null) continue;

                accounts.Add(new JObject
                {
                    ["currency"] = c.ToString(),
                    ["balance"] = 0,
                    ["ledger"] = new JArray()
                });
            }
        }

        private static JObject FindAccount(JArray accounts, Currency currency)
        {
            foreach (var token in accounts)
            {
                var jo = token as JObject;
                if (jo == null || jo["currency"] == null) continue;
                if (jo["currency"].ToString() == currency.ToString()) return jo;
            }
            return null;
        }

        private static void AdoptLegacyBalance(JObject root, JObject wallet, string legacyKey, Currency currency)
        {
            var legacy = root[legacyKey];
            if (legacy == null) return;

            root.Remove(legacyKey);
            if (legacy.Type != JTokenType.Integer && legacy.Type != JTokenType.Float) return;

            long amount = legacy.Value<long>();
            if (amount == 0) return;

            var account = FindAccount((JArray)wallet["accounts"], currency);
            if (account == null) return;

            long balance = account["balance"] != null ? account["balance"].Value<long>() : 0;
            account["balance"] = balance + amount;

            var ledger = account["ledger"] as JArray;
            if (ledger == null)
            {
                ledger = new JArray();
                account["ledger"] = ledger;
            }

            ledger.Add(new JObject
            {
                ["sequence"] = ledger.Count + 1,
                ["timestamp"] = DateTime.UtcNow.ToString("o"),
                ["currency"] = currency.ToString(),
                ["delta"] = amount,
                ["reason"] = LedgerReason.Admin.ToString(),
                ["balanceAfter"] = balance + amount,
                ["note"] = "migrated from pre-schema save"
            });
        }
    }

    /// <summary>
    /// v1 → v2. Adds the progressive pot block and the public build's player flags
    /// (plan §Stage 0).
    ///
    /// Additive and idempotent by construction: the pot seeds live in the content, not
    /// the save, so this creates nothing but an *empty* progressive block — a missing
    /// key falls back to the tier's seedValue at read time. The new player flags are
    /// left at their safe defaults (disclosure not seen, not excluded), which is what
    /// a v1 player's state actually was.
    /// </summary>
    public sealed class Migration_1_To_2 : ISaveMigration
    {
        public int FromVersion => 1;
        public int ToVersion => 2;
        public string Name => "AddProgressiveBlockAndResponsiblePlayFlags";

        public void Apply(JObject root)
        {
            if (root["progressive"] == null || root["progressive"].Type != JTokenType.Object)
                root["progressive"] = new JObject();

            if (root["player"] == null || root["player"].Type != JTokenType.Object)
                root["player"] = new JObject();

            var player = (JObject)root["player"];

            // Safe defaults only — never invent an exclusion, never claim the
            // disclosure was seen.
            if (player["disclosureSeen"] == null) player["disclosureSeen"] = false;
            if (player["selfExcludedUntilUtc"] == null) player["selfExcludedUntilUtc"] = "";
        }
    }
}