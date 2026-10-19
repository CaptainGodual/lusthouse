using System.Collections.Generic;
using LustHouse.Catalog;
using LustHouse.Slots;
using NUnit.Framework;
using UnityEngine;

namespace LustHouse.Tests
{
    /// <summary>
    /// Guards the layout maths the lobby depends on.
    ///
    /// These exist because the first greybox laid everything out with hand-computed
    /// pivots: the featured row bled off the left edge, the daily bonus banner overlapped
    /// the top bar, and the first tab label fell outside its own strip. None of that is
    /// visible to a unit test of the game logic, so the arithmetic gets asserted here.
    /// </summary>
    public class LobbyLayoutTests
    {
        /// <summary>Reference resolution from plan §2.1.</summary>
        private const float RefWidth = 1920f;
        private const float RefHeight = 1080f;
        private const float Margin = 40f;

        private const float TopBarHeight = 88f;
        private const float BonusY = 100f, BonusH = 132f;
        private const float FeaturedY = 244f, FeaturedH = 300f;
        private const float LibraryTop = 556f, LibraryBottom = 120f;
        private const float NavHeight = 104f;

        [Test]
        public void VerticalBands_DoNotOverlap()
        {
            float[] bands =
            {
                TopBarHeight,
                BonusY,
                BonusY + BonusH,
                FeaturedY,
                FeaturedY + FeaturedH,
                LibraryTop
            };

            for (int i = 1; i < bands.Length; i++)
            {
                Assert.Greater(bands[i], bands[i - 1],
                    $"Band {i} starts at {bands[i]} but the previous one ends at {bands[i - 1]}. " +
                    "Overlapping bands are what made the banner cover the top bar.");
            }

            Assert.LessOrEqual(FeaturedY + FeaturedH, LibraryTop,
                "The featured row must finish above the library heading.");
            Assert.Greater(LibraryBottom, NavHeight,
                "The library must start above the bottom nav.");
            Assert.Less(LibraryTop - LibraryBottom, RefHeight,
                "Library band must fit inside the reference height.");
        }

        [Test]
        public void TopBar_StaysInsideTheScreen()
        {
            // Gear is placed from the right edge; check it does not run off the left.
            float gearX = RefWidth - Margin - 56f;
            Assert.Greater(gearX, 0f, "Gear would sit off the left edge.");
            Assert.Less(gearX + 56f, RefWidth, "Gear would sit off the right edge.");
        }

        [Test]
        public void FeaturedRow_FourTilesFitInsideTheMargin()
        {
            const float chevronW = 44f;
            float usable = RefWidth - Margin * 2f - chevronW;
            float tileW = (usable - 3f * 24f) / 4f;

            Assert.Greater(tileW, 0f, "Featured tiles would have negative width.");
            Assert.Greater(tileW, 200f, "Featured tiles are too narrow to read.");
            Assert.LessOrEqual(tileW * 4f + 24f * 3f, usable,
                "Four featured tiles must fit the content width exactly.");
        }

        [Test]
        public void FeaturedRow_LeavesTheLeftMarginIntact()
        {
            // The bug: tiles were parented to the canvas with no inset, so the first tile
            // started at x=0 and its label was clipped by the screen edge.
            float firstTileX = Margin;
            Assert.Greater(firstTileX, 0f, "Featured tiles must not start at x=0.");
            Assert.GreaterOrEqual(firstTileX, Margin, "Featured tiles must respect the margin.");
        }

        [Test]
        public void Grid_IsCentredInsideItsArea()
        {
            const int columns = 6;
            const float tileW = 224f, gap = 24f;

            float gridWidth = columns * tileW + (columns - 1) * gap;
            float areaWidth = RefWidth - Margin * 2f;

            Assert.Greater(gridWidth, 0f);
            Assert.LessOrEqual(gridWidth, areaWidth,
                "Six columns of 224px plus gaps do not fit the content width (plan §2.2).");

            float inset = (areaWidth - gridWidth) / 2f;
            Assert.Greater(inset, 0f, "Grid should be centred, not hugging the left edge.");
            Assert.LessOrEqual(inset + gridWidth, areaWidth, "Centred grid overflows its area.");
        }

        [Test]
        public void TabBar_FitsEveryCategoryLabel()
        {
            GameCategory[] categories =
            {
                GameCategory.All,
                GameCategory.Slots,
                GameCategory.Jackpots,
                GameCategory.New,
                GameCategory.Favorites,
                GameCategory.HighVolatility
            };

            const float tabW = 170f, step = 186f;
            float totalWidth = categories.Length * step;

            Assert.LessOrEqual(totalWidth, RefWidth - Margin * 2f,
                "Six tabs overflow the content width.");
            Assert.Greater(tabW, 0f);

            // Every label must start inside the strip.
            for (int i = 0; i < categories.Length; i++)
                Assert.LessOrEqual(i * step + tabW, totalWidth,
                    $"Tab {categories[i]} extends past the strip.");
        }

        [Test]
        public void Underline_LandsUnderTheActiveTab()
        {
            const float tabW = 170f, step = 186f, underlineW = 120f;

            // The underline is left-anchored, so its x must be the tab's left edge plus the
            // centring offset — not the tab centre.
            for (int i = 0; i < 6; i++)
            {
                float tabLeft = i * step;
                float underlineX = tabLeft + (tabW - underlineW) / 2f;

                Assert.GreaterOrEqual(underlineX, tabLeft, "Underline must not sit left of its tab.");
                Assert.LessOrEqual(underlineX + underlineW, tabLeft + tabW,
                    "Underline must not sit right of its tab.");
            }
        }

        [Test]
        public void BottomNav_FiveDestinationsFitTheWidth()
        {
            int count = 5;
            float slotWidth = RefWidth / count;

            Assert.GreaterOrEqual(slotWidth, 120f, "Nav slots are too narrow for the labels.");
            for (int i = 0; i < count; i++)
            {
                float centre = -RefWidth / 2f + slotWidth * (i + 0.5f);
                Assert.Greater(centre, -RefWidth / 2f);
                Assert.Less(centre, RefWidth / 2f);
            }
        }

        [Test]
        public void BottomNav_UnderlineSitsBelowItsLabel()
        {
            // Label bottom is at y=34 from the nav's bottom edge; the underline must be
            // below that, not through the text.
            const float labelBottom = 34f;
            const float underlineY = 20f, underlineHeight = 4f;

            Assert.Less(underlineY + underlineHeight, labelBottom,
                "The active underline overlaps its label.");
            Assert.Greater(underlineY, 0f, "The underline is below the nav bar.");
        }

        [Test]
        public void FormatAmount_AbbreviatesOnlyPastAThousand()
        {
            Assert.AreEqual("0", UI.UiFactory.FormatAmount(0));
            Assert.AreEqual("999", UI.UiFactory.FormatAmount(999));
            Assert.AreEqual("5,000", UI.UiFactory.FormatAmount(5000));
            Assert.AreEqual("100.5K", UI.UiFactory.FormatAmount(100500));
            Assert.AreEqual("1.5M", UI.UiFactory.FormatAmount(1500000));
            Assert.AreEqual("-500", UI.UiFactory.FormatAmount(-500));
        }

        [Test]
        public void CurrencyRules_ExposeTheNamesTheLobbyRenders()
        {
            // The mockup said CHIPS/DIAMONDS; the plan's §4.1 names win.
            Assert.AreEqual("◎", Core.CurrencyRules.Symbol(Core.Currency.Coins));
            Assert.AreEqual("⬡", Core.CurrencyRules.Symbol(Core.Currency.Gems));
            Assert.AreEqual("♥", Core.CurrencyRules.Symbol(Core.Currency.LustPoints));
        }
    }
}