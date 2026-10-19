using System;
using System.IO;
using LustHouse.Core;
using NUnit.Framework;
using UnityEngine;

namespace LustHouse.Tests
{
    /// <summary>
    /// Save persistence: round-trip, atomic write, corrupt recovery, and the v0→v1
    /// migration (plan §0.4 P3, §5.8).
    /// </summary>
    public class SaveSystemTests
    {
        private string _dir;
        private string _path;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "lusthouse_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _path = Path.Combine(_dir, "save.json");
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
            catch { /* best effort */ }
        }

        private SaveDocument SampleDocument()
        {
            var doc = SaveDocument.CreateNew(new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc));
            doc.player.displayName = "TestPlayer";
            doc.player.level = 7;
            doc.player.ageConfirmed = true;
            doc.wallet.Find(Currency.Coins).balance = 12345;
            doc.wallet.Find(Currency.Gems).balance = 250;
            doc.inventory.ownedIds.Add("base_ivory");
            doc.inventory.ownedIds.Add("fit_houseuniform");
            return doc;
        }

        [Test]
        public void SaveThenLoad_RoundTripsEveryField()
        {
            var save = new SaveSystem(_path);
            save.Save(SampleDocument());

            Assert.IsTrue(save.Exists, "Save file should exist after Save.");

            var result = save.Load();

            Assert.IsTrue(result.ok);
            Assert.IsNull(result.warning, "A clean round-trip must not warn.");
            Assert.AreEqual("TestPlayer", result.document.player.displayName);
            Assert.AreEqual(7, result.document.player.level);
            Assert.IsTrue(result.document.player.ageConfirmed);
            Assert.AreEqual(12345, result.document.wallet.Find(Currency.Coins).balance);
            Assert.AreEqual(250, result.document.wallet.Find(Currency.Gems).balance);
            Assert.AreEqual(2, result.document.inventory.ownedIds.Count);
            Assert.AreEqual(SaveDocument.CurrentSchemaVersion, result.document.schemaVersion);
        }

        [Test]
        public void MissingSave_ReturnsFreshDocumentWithoutWarning()
        {
            var save = new SaveSystem(_path);
            var result = save.Load();

            Assert.IsTrue(result.ok);
            Assert.IsNull(result.warning);
            Assert.IsNotNull(result.document);
            Assert.AreEqual(SaveDocument.CurrentSchemaVersion, result.document.schemaVersion);
        }

        [Test]
        public void CorruptSave_RecoversToDefaultsAndBacksUp()
        {
            File.WriteAllText(_path, "{ this is not json ][");

            var save = new SaveSystem(_path);
            var result = save.Load();

            Assert.IsTrue(result.ok, "Corruption must never surface as a hard failure.");
            Assert.IsNotNull(result.document);
            Assert.IsNotNull(result.warning, "Corruption must be reported as a warn chip.");
            Assert.IsTrue(File.Exists(_path + ".bak"), "The corrupt file must be backed up before rebuild.");
        }

        [Test]
        public void TruncatedSave_RecoversToDefaults()
        {
            File.WriteAllText(_path, "{\"schemaVersion\":1,\"player\":{\"displ");

            var result = new SaveSystem(_path).Load();

            Assert.IsTrue(result.ok);
            Assert.IsNotNull(result.document);
            Assert.IsNotNull(result.warning);
        }

        [Test]
        public void Save_IsAtomic_NoTempFileLeftBehind()
        {
            var save = new SaveSystem(_path);

            for (int i = 0; i < 5; i++)
            {
                var doc = SampleDocument();
                doc.player.level = i + 1;
                save.Save(doc);
            }

            Assert.IsFalse(File.Exists(_path + ".tmp"), "Temp file must be renamed, not left behind.");

            var result = save.Load();
            Assert.AreEqual(5, result.document.player.level, "Latest write must win.");
        }

        [Test]
        public void Migration_PreSchemaFlatBalancesBecomeLedgerEntries()
        {
            // The original scaffold wrote an unversioned, flat document.
            File.WriteAllText(_path,
                "{\"coins\":5000,\"gems\":25,\"lust\":300}");

            var result = new SaveSystem(_path).Load();

            Assert.IsTrue(result.ok);
            Assert.IsNull(result.warning);

            var doc = result.document;
            Assert.AreEqual(SaveDocument.CurrentSchemaVersion, doc.schemaVersion);

            var coins = doc.wallet.Find(Currency.Coins);
            Assert.AreEqual(5000, coins.balance, "Legacy flat balance must be preserved.");
            Assert.AreEqual(1, coins.ledger.Count, "Migrated balance must be auditable.");
            Assert.AreEqual(5000, coins.ledger[0].delta);
            Assert.AreEqual(5000, coins.ledger[0].balanceAfter);
            Assert.AreEqual(25, doc.wallet.Find(Currency.Gems).balance);
            Assert.AreEqual(300, doc.wallet.Find(Currency.LustPoints).balance);
        }

        [Test]
        public void Migration_FromFutureSchema_DoesNotClobberTheFile()
        {
            var future = "{\"schemaVersion\":" + (SaveDocument.CurrentSchemaVersion + 5) +
                         ",\"player\":{\"displayName\":\"FromTheFuture\"}}";
            File.WriteAllText(_path, future);

            var result = new SaveSystem(_path).Load();

            Assert.IsTrue(result.ok);
            Assert.IsNotNull(result.warning, "A newer schema must warn, not silently overwrite.");
            Assert.AreEqual(future, File.ReadAllText(_path),
                "The on-disk save from a newer build must be left untouched.");
        }

        [Test]
        public void Delete_BacksUpBeforeRemoving()
        {
            var save = new SaveSystem(_path);
            save.Save(SampleDocument());

            save.Delete();

            Assert.IsFalse(File.Exists(_path), "Save must be removed.");
            Assert.IsTrue(File.Exists(_path + ".bak"), "Delete must keep a backup for support.");
        }

        [Test]
        public void SavedAtUtc_IsStampedOnEveryWrite()
        {
            var save = new SaveSystem(_path);
            save.Save(SampleDocument());

            var doc = save.Load().document;

            Assert.IsFalse(string.IsNullOrWhiteSpace(doc.savedAtUtc));
            DateTime parsed;
            Assert.IsTrue(DateTime.TryParse(doc.savedAtUtc, out parsed),
                $"savedAtUtc should parse, got '{doc.savedAtUtc}'");
        }
    }
}