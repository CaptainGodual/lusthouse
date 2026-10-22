using System;
using LustHouse.Catalog;
using LustHouse.Core;
using LustHouse.Lobby;
using LustHouse.Slots;
using LustHouse.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LustHouse.Tests
{
    /// <summary>
    /// The probability disclosure (plan §1.1).
    ///
    /// The sheet is only worth anything if the numbers on it are the numbers the
    /// maths produces. A disclosure that prints a design target instead of a measured
    /// figure is a decoration, and a store reviewer who checks it will find out.
    ///
    /// These tests pin that: the printed return-to-player figure must be the exact
    /// enumerated one, the hit rate must be measured from a fixed-seed run, and the
    /// paytable must cover every paying combination in the content.
    /// </summary>
    public class DisclosureTests
    {
        private const string VelvetRoomPath = "Assets/Data/Slots/Slot_VelvetRoom.asset";
        private const string CatalogPath = "Assets/Resources/Catalog/GameCatalog.asset";

        private SlotMachineDefinition _velvet;

        [SetUp]
        public void SetUp()
        {
            _velvet = AssetDatabase.LoadAssetAtPath<SlotMachineDefinition>(VelvetRoomPath);
            Assert.IsNotNull(_velvet, "There is no Velvet Room definition to disclose.");
        }

        [Test]
        public void TheShippedGameHasAnExactRtp()
        {
            // The disclosure's headline figure comes from the exact engine. A game
            // whose maths has no closed form cannot be printed as an exact number.
            double exact = SimulationEngine.ExactRtp(_velvet);

            Assert.IsFalse(double.IsNaN(exact),
                "Velvet Room has no exact RTP, so the disclosure cannot publish one.");
            Assert.Less(Math.Abs(exact - _velvet.targetRtp), 0.005,
                "The shipped RTP is outside the ship gate's tolerance.");
        }

        [Test]
        public void TheDisclosurePrintsTheMeasuredRtpAndNotTheDesignTarget()
        {
            var report = DisclosureReport.Build(_velvet, 20261022, 0);
            double exact = SimulationEngine.ExactRtp(_velvet);

            Assert.IsTrue(report.hasExactRtp, "The disclosure has no exact figure to print.");
            Assert.AreEqual(exact, report.exactRtp, 1e-12,
                "The disclosure's RTP is not the one the maths produces.");
            Assert.IsTrue(report.RtpText.Contains(DisclosureReport.Percent(exact)),
                "The sheet does not print the measured figure: " + report.RtpText);
            Assert.IsTrue(report.RtpText.Contains("exact"),
                "The sheet must say the figure is exact, not sampled: " + report.RtpText);
        }

        [Test]
        public void TheHitRateIsMeasuredAndReproducible()
        {
            var first = DisclosureReport.Build(_velvet, 4242, 2000);
            var second = DisclosureReport.Build(_velvet, 4242, 2000);

            Assert.AreEqual(2000, first.sampleSpins);
            Assert.Greater(first.hitRate, 0.0, "A game that never paid would not be a game.");
            Assert.Less(first.hitRate, 1.0, "A game that always paid would not be a game.");
            Assert.AreEqual(first.hitRate, second.hitRate, 1e-12,
                "The same seed must produce the same hit rate, or the sheet changes " +
                "every time it is opened.");
        }

        [Test]
        public void ThePaytableCoversEveryPayingCombination()
        {
            var report = DisclosureReport.Build(_velvet, 1, 0);

            int expected = 0;
            foreach (var symbol in _velvet.symbols)
            {
                if (symbol == null) continue;
                for (int matches = 3; matches <= _velvet.reelCount; matches++)
                    if (symbol.PayoutForMatch(matches) > 0) expected++;
            }

            Assert.AreEqual(expected, report.paytable.Count,
                "The published paytable does not match the content's paying combinations.");
            foreach (var row in report.paytable)
            {
                Assert.Greater(row.payout, 0, "A paytable row that pays nothing is noise.");
                Assert.IsFalse(string.IsNullOrEmpty(row.symbolName),
                    "A paytable row with no symbol name cannot be read.");
            }
        }

        [Test]
        public void ThePaytableNamesTheWildAndTheScatter()
        {
            var report = DisclosureReport.Build(_velvet, 1, 0);

            // The wild pays on a line, so it is a row in the paytable.
            bool wild = false;
            foreach (var row in report.paytable)
                if (row.isWild) wild = true;

            Assert.IsTrue(wild, "The wild is not marked in the published paytable, so a " +
                                "player cannot tell which symbol substitutes.");

            // The scatter does not pay on a line, so it is explained in words instead.
            string notes = string.Join(" ", report.specialNotes);
            Assert.IsTrue(notes.Contains("Scatter"),
                "The scatter is not explained in the published notes: " + notes);
            Assert.IsTrue(notes.Contains("free spins"),
                "The feature round the scatter triggers is not published: " + notes);
        }

        [Test]
        public void TheSheetCarriesTheResponsiblePlayStatement()
        {
            string statement = ProbabilityDisclosureSheet.ResponsiblePlayStatement;

            Assert.IsTrue(statement.Contains("18"),
                "The statement does not say who may play.");
            Assert.IsTrue(statement.ToLowerInvariant().Contains("real money"),
                "The statement does not say that no real money is involved.");
            Assert.IsTrue(statement.ToLowerInvariant().Contains("redeem"),
                "The statement does not say that nothing can be redeemed.");
        }

        [Test]
        public void TheGateLinksToTheDisclosure()
        {
            // The gate's copy promises that every game publishes its prize tables and
            // its return-to-player rate. A promise with no link behind it is the thing
            // a store review calls a lie.
            var host = new GameObject("GateHost");
            try
            {
                AgeGateOverlay.Create(host.transform, null);

                bool found = false;
                foreach (var button in host.GetComponentsInChildren<Button>(true))
                    if (button.name == "OpenDisclosure") found = true;

                Assert.IsTrue(found,
                    "The age gate promises published odds and prize tables but offers no " +
                    "way to see them.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnlyPlayableGamesAreDisclosed()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<GameCatalog>(CatalogPath);
            Assert.IsNotNull(catalog, "There is no game catalogue at " + CatalogPath);

            var games = ProbabilityDisclosureSheet.ShippableGames(catalog);

            Assert.Greater(games.Count, 0, "No game is disclosed, so the sheet is empty.");
            foreach (var game in games)
            {
                Assert.IsNotNull(game, "A null definition ended up in the disclosure.");
                Assert.AreEqual(RtpStatus.Measured, game.rtpStatus,
                    "A game whose maths has not been signed off must not be published as " +
                    "if it had been.");
            }
        }
    }

    /// <summary>
    /// The responsible-play rules (guardrail 4).
    ///
    /// These are the rules that have to be exactly right, so they are tested as pure
    /// functions: the schedule of the reality checks, and the direction the
    /// self-exclusion may move in.
    /// </summary>
    public class ResponsiblePlayRuleTests
    {
        private static readonly int[] Thresholds = { 15, 30, 60 };

        [Test]
        public void TheRealityCheckComesDueAtFifteenMinutes()
        {
            Assert.IsFalse(RealityCheckSchedule.TryGetDue(Thresholds, 0, 14, out _),
                "The reality check came due before a quarter of an hour.");

            Assert.IsTrue(RealityCheckSchedule.TryGetDue(Thresholds, 0, 15, out int due));
            Assert.AreEqual(15, due, "The first reality check is the fifteen-minute one.");
        }

        [Test]
        public void EachRealityCheckComesDueOnceAndInOrder()
        {
            int shown = 0;

            Assert.IsTrue(RealityCheckSchedule.TryGetDue(Thresholds, shown, 30, out int due));
            Assert.AreEqual(15, due, "A player who has been away an hour gets the first " +
                                     "check, not the last one.");
            shown = due;

            Assert.IsFalse(RealityCheckSchedule.TryGetDue(Thresholds, shown, 20, out _),
                "The same check came due twice.");

            Assert.IsTrue(RealityCheckSchedule.TryGetDue(Thresholds, shown, 30, out due));
            Assert.AreEqual(30, due);
            shown = due;

            Assert.IsTrue(RealityCheckSchedule.TryGetDue(Thresholds, shown, 60, out due));
            Assert.AreEqual(60, due);
            shown = due;

            Assert.IsFalse(RealityCheckSchedule.TryGetDue(Thresholds, shown, 600, out _),
                "There is no fourth check.");
            Assert.AreEqual(0, RealityCheckSchedule.NextDue(Thresholds, shown));
        }

        [Test]
        public void AnExclusionCannotBeCleared()
        {
            Assert.IsFalse(SelfExclusion.CanApply("2026-10-22T00:00:00Z", ""),
                "An exclusion can be cleared, so it was never one.");
            Assert.IsFalse(SelfExclusion.CanApply(SelfExclusion.PermanentUtc, ""),
                "A permanent exclusion can be cleared, so it was never one.");
        }

        [Test]
        public void AnExclusionCannotBeShortened()
        {
            Assert.IsFalse(SelfExclusion.CanApply("2026-10-22T00:00:00Z", "2026-10-21T00:00:00Z"),
                "An exclusion can be shortened, so it was never one.");
            Assert.IsFalse(SelfExclusion.CanApply(SelfExclusion.PermanentUtc, "2026-10-22T00:00:00Z"),
                "A permanent exclusion can be shortened.");

            Assert.IsTrue(SelfExclusion.CanApply("", "2026-10-22T00:00:00Z"),
                "The first exclusion was refused.");
            Assert.IsTrue(SelfExclusion.CanApply("2026-10-22T00:00:00Z", "2026-10-23T00:00:00Z"),
                "Lengthening an exclusion was refused.");
        }

        [Test]
        public void APermanentExclusionNeverExpires()
        {
            Assert.IsTrue(SelfExclusion.IsPermanent(SelfExclusion.PermanentUtc));
            Assert.IsTrue(SelfExclusion.IsActive(SelfExclusion.PermanentUtc, DateTime.UtcNow));
            Assert.IsTrue(SelfExclusion.IsActive(SelfExclusion.PermanentUtc, new DateTime(9999, 1, 1)),
                "A permanent exclusion expired.");
        }

        [Test]
        public void AOneDayExclusionLiftsItself()
        {
            string yesterday = DateTime.UtcNow.AddDays(-1).ToString("o");

            Assert.IsFalse(SelfExclusion.IsActive(yesterday, DateTime.UtcNow),
                "An exclusion that has run its course is still excluding the player.");
            Assert.IsTrue(SelfExclusion.IsActive(DateTime.UtcNow.AddDays(1).ToString("o"), DateTime.UtcNow),
                "An exclusion that has not run its course is not excluding the player.");
        }
    }

    /// <summary>
    /// The first-run offer (plan §1.3).
    /// </summary>
    public class FirstRunOfferTests
    {
        private const string CatalogPath = "Assets/Resources/Catalog/GameCatalog.asset";

        [Test]
        public void TheStartingStakeIsABetThePlayerCanActuallyPlace()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<GameCatalog>(CatalogPath);
            Assert.IsNotNull(catalog, "There is no game catalogue at " + CatalogPath);

            long stake = FirstRunPackPrompt.MinimumBetFor(catalog);

            Assert.Greater(stake, 0, "A decliner would be given nothing to play with.");

            foreach (var entry in catalog.games)
            {
                if (entry == null || entry.locked || entry.definition == null) continue;
                Assert.LessOrEqual(stake, entry.definition.minBet,
                    "The starting stake is larger than a bet the player can place, so it " +
                    "is not a stake they can use.");
            }
        }

        [Test]
        public void TheStakeIsOneBetEvenWhenThereIsNoCatalogue()
        {
            long stake = FirstRunPackPrompt.MinimumBetFor(null);

            Assert.Greater(stake, 0,
                "With no catalogue the decliner would be given nothing to play with.");
        }
    }
}
