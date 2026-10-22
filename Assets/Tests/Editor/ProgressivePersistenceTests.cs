using System;
using System.IO;
using LustHouse.Core;
using NUnit.Framework;

namespace LustHouse.Tests
{
    /// <summary>
    /// The schema 1 → 2 bump: the progressive pot block and the public build's
    /// responsible-play flags (plan §Stage 0).
    ///
    /// The contract under test is that the migration is additive, idempotent and
    /// non-destructive — a v1 player keeps everything they had, gains an *empty* pot
    /// block (the seeds live in the content, not the save), and lands at the safe
    /// defaults for the new flags.
    /// </summary>
    public class ProgressivePersistenceTests
    {
        private string _dir;
        private string _path;

        /// <summary>A real v1 save: no progressive block, no disclosure or exclusion flags.</summary>
        private const string V1Save =
            "{\"schemaVersion\":1," +
            "\"savedAtUtc\":\"2026-09-01T10:00:00.0000000Z\"," +
            "\"migrated\":false," +
            "\"player\":{" +
            "\"displayName\":\"Veteran\"," +
            "\"level\":6," +
            "\"xp\":4200," +
            "\"vipTier\":2," +
            "\"lifetimeLustPoints\":800," +
            "\"memberSinceUtc\":\"2026-08-01T00:00:00.0000000Z\"," +
            "\"loginStreak\":5," +
            "\"totalSpins\":311," +
            "\"totalWagered\":3110000," +
            "\"biggestWin\":180000," +
            "\"ageConfirmed\":true," +
            "\"ageConfirmedUtc\":\"2026-08-01T00:01:00.0000000Z\"" +
            "}," +
            "\"wallet\":{\"accounts\":[" +
            "{\"currency\":\"Coins\",\"balance\":240000,\"ledger\":[]}," +
            "{\"currency\":\"Gems\",\"balance\":40,\"ledger\":[]}," +
            "{\"currency\":\"LustPoints\",\"balance\":800,\"ledger\":[]}" +
            "],\"conversionsToday\":0,\"conversionDateUtc\":\"\"}," +
            "\"inventory\":{\"ownedIds\":[\"base_ivory\"],\"equipped\":[],\"starterGrantApplied\":true}," +
            "\"settings\":{\"masterVolume\":0.8,\"musicVolume\":0.5,\"sfxVolume\":1.0," +
            "\"graphicsQuality\":2,\"reducedMotion\":false,\"lastBuildVersion\":\"0.4.0\"}}";

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "lusthouse_pot_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _path = Path.Combine(_dir, "save.json");
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
            catch { /* best effort */ }
        }

        [Test]
        public void V1Save_MigratesToSchema2()
        {
            File.WriteAllText(_path, V1Save);

            var result = new SaveSystem(_path).Load();

            Assert.IsTrue(result.ok, result.warning ?? "");
            Assert.IsNull(result.warning, "A clean v1 save must migrate without a warning.");
            Assert.AreEqual(2, result.document.schemaVersion);
            Assert.AreEqual(SaveDocument.CurrentSchemaVersion, result.document.schemaVersion);
        }

        [Test]
        public void V1Save_MigrationIsNonDestructive_EveryOldFieldSurvives()
        {
            File.WriteAllText(_path, V1Save);

            var doc = new SaveSystem(_path).Load().document;

            Assert.AreEqual("Veteran", doc.player.displayName);
            Assert.AreEqual(6, doc.player.level);
            Assert.AreEqual(4200, doc.player.xp);
            Assert.AreEqual(2, doc.player.vipTier);
            Assert.AreEqual(800, doc.player.lifetimeLustPoints);
            Assert.AreEqual(5, doc.player.loginStreak);
            Assert.AreEqual(311, doc.player.totalSpins);
            Assert.AreEqual(3110000, doc.player.totalWagered);
            Assert.AreEqual(180000, doc.player.biggestWin);
            Assert.IsTrue(doc.player.ageConfirmed);

            Assert.AreEqual(240000, doc.wallet.Find(Currency.Coins).balance);
            Assert.AreEqual(40, doc.wallet.Find(Currency.Gems).balance);
            Assert.AreEqual(800, doc.wallet.Find(Currency.LustPoints).balance);

            Assert.AreEqual(1, doc.inventory.ownedIds.Count);
            Assert.IsTrue(doc.inventory.starterGrantApplied);

            Assert.AreEqual(0.8f, doc.settings.masterVolume, 0.0001f);
            Assert.AreEqual(0.5f, doc.settings.musicVolume, 0.0001f);
            Assert.AreEqual(2, doc.settings.graphicsQuality);
            Assert.AreEqual("0.4.0", doc.settings.lastBuildVersion);
        }

        [Test]
        public void V1Save_MigratesToAnEmptyPotBlock()
        {
            File.WriteAllText(_path, V1Save);

            var doc = new SaveSystem(_path).Load().document;

            Assert.IsNotNull(doc.progressive,
                "The migration must create the progressive block.");
            Assert.AreEqual(0, doc.progressive.Count,
                "The block must be empty: pot seeds live in the content, not the save, " +
                "so a missing key falls back to the tier's seedValue.");
        }

        [Test]
        public void V1Save_NewFlagsLandAtTheirSafeDefaults()
        {
            File.WriteAllText(_path, V1Save);

            var doc = new SaveSystem(_path).Load().document;

            Assert.IsFalse(doc.player.disclosureSeen,
                "A v1 player has never seen the disclosure; the migration must not claim otherwise.");
            Assert.AreEqual("", doc.player.selfExcludedUntilUtc,
                "The migration must never invent a self-exclusion.");
        }

        [Test]
        public void Migration_IsIdempotent_RunningItTwiceChangesNothing()
        {
            // Load twice from the same on-disk v1 file: the first load migrates the
            // in-memory JObject, but the file is only rewritten on Save. Loading again
            // must produce the same document either way.
            File.WriteAllText(_path, V1Save);

            var first = new SaveSystem(_path).Load().document;
            var second = new SaveSystem(_path).Load().document;

            Assert.AreEqual(first.schemaVersion, second.schemaVersion);
            Assert.AreEqual(first.progressive.Count, second.progressive.Count);
            Assert.AreEqual(first.player.disclosureSeen, second.player.disclosureSeen);
            Assert.AreEqual(first.player.selfExcludedUntilUtc, second.player.selfExcludedUntilUtc);
            Assert.AreEqual(first.wallet.Find(Currency.Coins).balance,
                            second.wallet.Find(Currency.Coins).balance);
        }

        [Test]
        public void CorruptProgressiveBlock_FallsBackInsteadOfCrashing()
        {
            // A block that is not an object at all — the shape a hand-edited or
            // truncated save can produce.
            File.WriteAllText(_path,
                "{\"schemaVersion\":2,\"progressive\":\"not-a-block\",\"player\":{\"displayName\":\"X\"}}");

            var result = new SaveSystem(_path).Load();

            Assert.IsTrue(result.ok, "A corrupt pot block must not fail the load.");
            Assert.IsNotNull(result.document.progressive,
                "The load must hand back a usable block so the service falls back to the seeds.");
        }

        [Test]
        public void CorruptProgressiveBlock_KeepsTheRestOfTheProfile()
        {
            // The pots are the one block that can be rebuilt from the content, so a
            // broken one must not cost the player their wallet or their level.
            File.WriteAllText(_path,
                "{\"schemaVersion\":2,\"progressive\":42," +
                "\"player\":{\"displayName\":\"Veteran\",\"level\":6,\"ageConfirmed\":true}," +
                "\"wallet\":{\"accounts\":[{\"currency\":\"Coins\",\"balance\":240000,\"ledger\":[]}]}}");

            var doc = new SaveSystem(_path).Load().document;

            Assert.IsNotNull(doc.progressive);
            Assert.AreEqual(0, doc.progressive.Count);
            Assert.AreEqual("Veteran", doc.player.displayName);
            Assert.AreEqual(6, doc.player.level);
            Assert.AreEqual(240000, doc.wallet.Find(Currency.Coins).balance,
                "A corrupt pot block must not take the wallet with it.");
        }

        [Test]
        public void Timestamps_RoundTripAsWritten()
        {
            // Newtonsoft parses ISO-looking strings into DateTime tokens by default, and
            // a DateTime handed to a string field is reformatted with the current
            // culture. Every timestamp in the save is a string, so they must come back
            // exactly as they were written.
            var save = new SaveSystem(_path);
            var doc = SaveDocument.CreateNew(DateTime.UtcNow);
            doc.player.selfExcludedUntilUtc = "9999-12-31T23:59:59Z";
            doc.player.ageConfirmedUtc = "2026-10-22T03:15:50.1234567Z";
            doc.player.lastDailyGiftUtc = "2026-10-21T22:00:00.0000000Z";
            save.Save(doc);

            var loaded = save.Load().document;

            Assert.AreEqual("9999-12-31T23:59:59Z", loaded.player.selfExcludedUntilUtc);
            Assert.AreEqual("2026-10-22T03:15:50.1234567Z", loaded.player.ageConfirmedUtc);
            Assert.AreEqual("2026-10-21T22:00:00.0000000Z", loaded.player.lastDailyGiftUtc);
            Assert.AreEqual(doc.savedAtUtc, loaded.savedAtUtc);
        }

        [Test]
        public void MissingProgressiveBlock_IsNullGuardedLikeTheOtherBlocks()
        {
            File.WriteAllText(_path, "{\"schemaVersion\":2,\"player\":{\"displayName\":\"X\"}}");

            var doc = new SaveSystem(_path).Load().document;

            Assert.IsNotNull(doc.progressive,
                "A v2 save with no progressive block must be null-guarded the way " +
                "player / wallet / inventory / settings are.");
        }

        [Test]
        public void Schema2Save_RoundTripsPotsAndFlags()
        {
            var save = new SaveSystem(_path);

            var doc = SaveDocument.CreateNew(DateTime.UtcNow);
            doc.player.displayName = "PotHolder";
            doc.player.disclosureSeen = true;
            doc.progressive["velvet_room:Mini"] = 61000;
            doc.progressive["velvet_room:Minor"] = 250500;
            doc.progressive["velvet_room:Major"] = 1000250;
            save.Save(doc);

            var loaded = save.Load();

            Assert.IsTrue(loaded.ok);
            Assert.IsNull(loaded.warning);
            Assert.AreEqual(3, loaded.document.progressive.Count);
            Assert.AreEqual(61000, loaded.document.progressive["velvet_room:Mini"]);
            Assert.AreEqual(250500, loaded.document.progressive["velvet_room:Minor"]);
            Assert.AreEqual(1000250, loaded.document.progressive["velvet_room:Major"]);
            Assert.IsTrue(loaded.document.player.disclosureSeen);
            Assert.AreEqual("PotHolder", loaded.document.player.displayName);
        }

        [Test]
        public void Schema2Save_SelfExclusionRoundTrips()
        {
            var save = new SaveSystem(_path);

            var doc = SaveDocument.CreateNew(DateTime.UtcNow);
            doc.player.selfExcludedUntilUtc = "9999-12-31T23:59:59Z";
            save.Save(doc);

            Assert.AreEqual("9999-12-31T23:59:59Z",
                save.Load().document.player.selfExcludedUntilUtc,
                "A permanent exclusion is the max-UTC sentinel and must survive the round trip.");
        }

        [Test]
        public void FreshDocument_StartsAtSchema2WithEmptyPots()
        {
            var doc = SaveDocument.CreateNew(DateTime.UtcNow);

            Assert.AreEqual(2, doc.schemaVersion);
            Assert.IsNotNull(doc.progressive);
            Assert.AreEqual(0, doc.progressive.Count);
            Assert.IsFalse(doc.player.disclosureSeen);
            Assert.AreEqual("", doc.player.selfExcludedUntilUtc);
        }

        [Test]
        public void TheTwoMigrationsChain_V0SaveReachesSchema2()
        {
            // A pre-schema save must still be able to walk the whole path.
            File.WriteAllText(_path, "{\"coins\":5000,\"gems\":25,\"lust\":300}");

            var result = new SaveSystem(_path).Load();

            Assert.IsTrue(result.ok, result.warning ?? "");
            Assert.AreEqual(SaveDocument.CurrentSchemaVersion, result.document.schemaVersion);
            Assert.AreEqual(5000, result.document.wallet.Find(Currency.Coins).balance);
            Assert.IsNotNull(result.document.progressive);
            Assert.AreEqual(0, result.document.progressive.Count);
        }
    }
}
