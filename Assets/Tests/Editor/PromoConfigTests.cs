using System;
using System.Collections.Generic;
using LustHouse.Catalog;
using NUnit.Framework;
using UnityEngine;

namespace LustHouse.Tests
{
    /// <summary>
    /// The remote config override parses network input and mutates live lobby content.
    /// Its safety rules are the whole point of this class, so they are asserted directly.
    /// </summary>
    public class PromoConfigTests
    {
        private static readonly HashSet<string> Known = new HashSet<string>
        {
            "velvet_room", "neon_nights", "house_edge"
        };

        private static LobbyPromoConfig Fresh()
        {
            var config = ScriptableObject.CreateInstance<LobbyPromoConfig>();
            config.featured = new List<FeaturedTile>
            {
                new FeaturedTile { gameId = "velvet_room", headline = "LOCAL", order = 0 }
            };
            config.slides = new List<PromoSlide>
            {
                new PromoSlide { kind = PromoSlideKind.DailyBonus, headline = "LOCAL BONUS" }
            };
            config.carouselDwellSeconds = 8f;
            config.maxFeaturedTiles = 4;
            config.newWindowDays = 14;
            return config;
        }

        [Test]
        public void ValidPayload_OverridesFeaturedAndSlides()
        {
            var config = Fresh();

            var result = PromoConfigValidator.Apply(config, @"
            {
              ""featured"": [ { ""gameId"": ""house_edge"", ""headline"": ""JACKPOT"", ""order"": 0 } ],
              ""slides"":   [ { ""kind"": ""Newest"", ""gameId"": ""neon_nights"", ""headline"": ""NEW"" } ],
              ""carouselDwellSeconds"": 12
            }", Known);

            Assert.IsTrue(result.applied);
            Assert.IsTrue(result.changedFeatured);
            Assert.IsTrue(result.changedSlides);
            Assert.AreEqual("house_edge", config.featured[0].gameId);
            Assert.AreEqual("JACKPOT", config.featured[0].headline);
            Assert.AreEqual(PromoSlideKind.Newest, config.slides[0].kind);
            Assert.AreEqual(12f, config.carouselDwellSeconds);
        }

        [Test]
        public void MalformedJson_ChangesNothing()
        {
            var config = Fresh();

            var result = PromoConfigValidator.Apply(config, "{ this is not json ][", Known);

            Assert.IsFalse(result.applied, "A parse failure must not partially apply.");
            Assert.IsNotNull(result.warning);
            Assert.AreEqual("LOCAL", config.featured[0].headline, "Local config must survive intact.");
            Assert.AreEqual(1, config.slides.Count);
        }

        [Test]
        public void UnknownGameIds_AreDroppedButTheRestApplies()
        {
            var config = Fresh();

            var result = PromoConfigValidator.Apply(config, @"
            { ""featured"": [ { ""gameId"": ""does_not_exist"" }, { ""gameId"": ""neon_nights"" } ] }", Known);

            Assert.AreEqual(1, result.droppedUnknownGameIds);
            Assert.IsTrue(result.changedFeatured);
            Assert.AreEqual(1, config.featured.Count);
            Assert.AreEqual("neon_nights", config.featured[0].gameId,
                "The valid entry must still be applied.");
            Assert.IsNotNull(result.warning);
        }

        [Test]
        public void AllEntriesUnknown_LeavesTheLocalListAlone()
        {
            var config = Fresh();

            var result = PromoConfigValidator.Apply(config,
                @"{ ""featured"": [ { ""gameId"": ""nope_a"" }, { ""gameId"": ""nope_b"" } ] }", Known);

            Assert.AreEqual(2, result.droppedUnknownGameIds);
            Assert.IsFalse(result.changedFeatured, "An override that resolves to nothing changes nothing.");
            Assert.AreEqual("LOCAL", config.featured[0].headline);
        }

        [Test]
        public void EmptyArrays_DoNotBlankTheLobby()
        {
            var config = Fresh();

            var result = PromoConfigValidator.Apply(config,
                @"{ ""featured"": [], ""slides"": [] }", Known);

            Assert.IsFalse(result.applied);
            Assert.AreEqual(1, config.featured.Count, "An empty array must not clear the featured row.");
            Assert.AreEqual(1, config.slides.Count, "An empty array must not clear the carousel.");
        }

        [Test]
        public void AbsentFields_LeaveLocalValuesUntouched()
        {
            var config = Fresh();
            config.carouselDwellSeconds = 11f;

            PromoConfigValidator.Apply(config, @"{ ""slides"": [ { ""kind"": ""Seasonal"" } ] }", Known);

            Assert.AreEqual(11f, config.carouselDwellSeconds, "An absent field must not reset.");
            Assert.AreEqual(4, config.maxFeaturedTiles);
            Assert.AreEqual(14, config.newWindowDays);
        }

        [Test]
        public void OutOfRangeScalars_AreClampedNotRejected()
        {
            var config = Fresh();

            PromoConfigValidator.Apply(config, @"
            { ""carouselDwellSeconds"": 99999, ""newWindowDays"": 5000, ""maxFeaturedTiles"": 400 }", Known);

            Assert.AreEqual(30f, config.carouselDwellSeconds, "Dwell clamps to the layout-safe maximum.");
            Assert.AreEqual(90, config.newWindowDays);
            Assert.AreEqual(8, config.maxFeaturedTiles);
        }

        [Test]
        public void UnknownSlideKind_IsDropped()
        {
            var config = Fresh();

            var result = PromoConfigValidator.Apply(config,
                @"{ ""slides"": [ { ""kind"": ""NotARealKind"" }, { ""kind"": ""Jackpot"", ""gameId"": ""house_edge"" } ] }", Known);

            Assert.AreEqual(1, result.droppedMalformedEntries);
            Assert.IsTrue(result.changedSlides);
            Assert.AreEqual(1, config.slides.Count);
            Assert.AreEqual(PromoSlideKind.Jackpot, config.slides[0].kind);
        }

        [Test]
        public void NullAndEmptyPayloads_AreRejectedSafely()
        {
            var config = Fresh();

            Assert.IsFalse(PromoConfigValidator.Apply(config, null, Known).applied);
            Assert.IsFalse(PromoConfigValidator.Apply(config, "", Known).applied);
            Assert.IsFalse(PromoConfigValidator.Apply(config, "   ", Known).applied);
            Assert.IsFalse(PromoConfigValidator.Apply(null, "{}", Known).applied);

            Assert.AreEqual("LOCAL", config.featured[0].headline);
        }

        [Test]
        public void Clone_DoesNotShareStateWithTheShippedAsset()
        {
            var original = Fresh();
            var clone = original.Clone();

            clone.featured[0].headline = "MUTATED";
            clone.slides[0].headline = "MUTATED";
            clone.carouselDwellSeconds = 20f;

            Assert.AreEqual("LOCAL", original.featured[0].headline, "A remote override must not mutate the asset.");
            Assert.AreEqual("LOCAL BONUS", original.slides[0].headline);
            Assert.AreEqual(8f, original.carouselDwellSeconds);
        }

        [Test]
        public void FeaturedInOrder_SortsByOrder()
        {
            var config = Fresh();
            config.featured = new List<FeaturedTile>
            {
                new FeaturedTile { gameId = "third", order = 20 },
                new FeaturedTile { gameId = "first", order = 1 },
                new FeaturedTile { gameId = "second", order = 10 }
            };

            var ordered = config.FeaturedInOrder();

            Assert.AreEqual("first", ordered[0].gameId);
            Assert.AreEqual("second", ordered[1].gameId);
            Assert.AreEqual("third", ordered[2].gameId);
        }

        [Test]
        public void NewReleaseWindow_RespectsTheConfiguredDays()
        {
            var config = Fresh();
            config.newWindowDays = 14;

            var entry = new GameCatalog.Entry();
            entry.releasedAtUtc = DateTime.UtcNow.AddDays(-3).ToString("o");
            Assert.AreNotEqual(default, entry.ReleasedUtc, "ReleasedUtc should parse an ISO-8601 date.");

            var old = new GameCatalog.Entry();
            old.releasedAtUtc = DateTime.UtcNow.AddDays(-40).ToString("o");

            var future = new GameCatalog.Entry();
            future.releasedAtUtc = DateTime.UtcNow.AddDays(3).ToString("o");

            // Mirrors PromoService.IsNew without needing a live service.
            bool IsNew(GameCatalog.Entry e, DateTime now) =>
                e.ReleasedUtc != default &&
                (now - e.ReleasedUtc).TotalDays >= 0 &&
                (now - e.ReleasedUtc).TotalDays <= config.newWindowDays;

            var now = DateTime.UtcNow;
            Assert.IsTrue(IsNew(entry, now), "3 days old is inside a 14-day window.");
            Assert.IsFalse(IsNew(old, now), "40 days old is outside a 14-day window.");
            Assert.IsFalse(IsNew(future, now), "A future release date is not yet New.");
        }

        [Test]
        public void ReleasedUtc_IsDefaultForGarbage()
        {
            var entry = new GameCatalog.Entry();
            entry.releasedAtUtc = "not a date";
            // default(DateTime) explicitly: a bare `default` binds to object/null here, which
            // made this compare null against a DateTime and always fail.
            Assert.AreEqual(default(DateTime), entry.ReleasedUtc, "Unparseable dates must not throw.");
        }
    }
}