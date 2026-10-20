using System.Collections.Generic;
using LustHouse.Slots;
using NUnit.Framework;
using UnityEngine;

namespace LustHouse.Tests
{
    /// <summary>
    /// The reel view draws what the engine scored, and keeps drawing it while it spins.
    ///
    /// <see cref="ReelEngine.BuildGrid"/> fixes the rule — row r of reel s shows
    /// strip[(stop + r) % stripLength] — and the view is free to recycle cells however it
    /// likes to get there. That freedom is exactly the risk: a reel that animates beautifully
    /// onto a grid one row off from the engine's is a game paying the wrong amount with no
    /// error anywhere. So these assert the display against the engine, not against itself.
    /// </summary>
    public class ReelsViewTests
    {
        private const int Reels = 5;
        private const int Rows = 3;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _spawned.Count; i++)
                if (_spawned[i] != null) Object.DestroyImmediate(_spawned[i]);
            _spawned.Clear();
        }

        /// <summary>A four-stop strip: every symbol id appears twice, so a wrong offset that
        /// lands two rows out still reads as a plausible grid unless the strip is compared.</summary>
        private static SlotMachineDefinition BuildFixture()
        {
            var def = ScriptableObject.CreateInstance<SlotMachineDefinition>();
            def.id = "reels_fixture";
            def.reelCount = Reels;
            def.rowCount = Rows;
            def.minBet = 100;
            def.maxBet = 1000;
            def.betLevels = 3;

            def.symbols = new List<SymbolDefinition>
            {
                new SymbolDefinition { id = 0, symbolName = "Wild", isWild = true },
                new SymbolDefinition { id = 1, symbolName = "Ace", payoutsByMatch = new long[] { 5, 20, 50 } },
                new SymbolDefinition { id = 2, symbolName = "King", payoutsByMatch = new long[] { 3, 10, 30 } }
            };

            def.reelStrips = new List<ReelStrip>();
            for (int r = 0; r < Reels; r++)
                def.reelStrips.Add(new ReelStrip
                {
                    symbols = new List<int> { r % 3, (r + 1) % 3, (r + 2) % 3, r % 3 }
                });

            return def;
        }

        private ReelsView MakeView(SlotMachineDefinition def)
        {
            var go = new GameObject("Reels", typeof(RectTransform));
            go.transform.SetParent(null, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1240f, 440f);
            _spawned.Add(go);

            var view = go.AddComponent<ReelsView>();
            view.fitToParent = true;
            view.Configure(def);
            return view;
        }

        private static void AssertShowsGrid(ReelsView view, SlotMachineDefinition def, int[] stops)
        {
            view.ShowStops(stops);
            var grid = ReelEngine.BuildGrid(def, stops);

            for (int r = 0; r < def.reelCount; r++)
            {
                for (int row = 0; row < def.rowCount; row++)
                {
                    Assert.AreEqual(grid[r][row], view.DisplayedSymbol(r, row),
                        $"Reel {r} row {row}: the reels are showing a different symbol from the " +
                        "grid the engine built and scored.");
                }
            }
        }

        [Test]
        public void AtRest_TheReelsMatchTheEngineGrid()
        {
            var def = BuildFixture();
            var view = MakeView(def);

            AssertShowsGrid(view, def, new[] { 0, 1, 2, 3, 0 });
        }

        [Test]
        public void AtRest_TheReelsMatchTheEngineGrid_AtEveryStopOnEveryReel()
        {
            var def = BuildFixture();
            var view = MakeView(def);

            for (int reel = 0; reel < def.reelCount; reel++)
            {
                for (int stop = 0; stop < def.reelStrips[reel].symbols.Count; stop++)
                {
                    var stops = new int[def.reelCount];
                    for (int r = 0; r < def.reelCount; r++)
                        stops[r] = (r + stop) % def.reelStrips[r].symbols.Count;

                    AssertShowsGrid(view, def, stops);
                }
            }
        }

        [Test]
        public void ScrollingPastTheWrap_KeepsShowingTheSameStrip()
        {
            // The wrap is where a recycled cell shows the wrong symbol: the strip indexes back
            // to zero while the cell's own index keeps counting. Walk a reel several laps in
            // quarter-stop steps and check every whole position against the engine.
            var def = BuildFixture();
            var view = MakeView(def);

            var reel = view.GetComponentInChildren<ReelStripView>();
            Assert.IsNotNull(reel, "ReelsView built no reel columns.");

            int stopCount = def.reelStrips[0].symbols.Count;
            int cellCount = reel.CellCount;
            Assert.AreEqual(def.rowCount + 2, cellCount,
                "A reel needs one cell per visible row plus one hidden row either end.");

            for (int quarter = 0; quarter < stopCount * 4 * 3; quarter++)
            {
                float position = quarter * 0.25f;
                reel.ScrollTo(position);

                if (quarter % 4 != 0) continue;

                int stop = (int)System.Math.Floor(position) % stopCount;
                for (int row = 0; row < def.rowCount; row++)
                {
                    int expected = def.reelStrips[0].symbols[(stop + row) % stopCount];
                    Assert.AreEqual(expected, reel.DisplayedSymbol(row),
                        $"Reel 0 drifted off the strip after scrolling to {position}: row {row} " +
                        "should be the engine's symbol but is not.");
                }
            }
        }

        [Test]
        public void Recycling_KeepsTheCellCountAndBoundsTheStripIndices()
        {
            var def = BuildFixture();
            var view = MakeView(def);
            var reel = view.GetComponentInChildren<ReelStripView>();

            int stopCount = def.reelStrips[0].symbols.Count;

            for (int step = 0; step <= stopCount * 10; step++)
            {
                reel.ScrollTo(step);
                Assert.AreEqual(def.rowCount + 2, reel.CellCount,
                    "Scrolling must recycle cells, never add or drop them.");
            }

            // Every cell must sit within one window of the scroll position. A cell left far
            // behind is one that will pop back into view as the wrong symbol much later.
            int whole = Mathf.FloorToInt(stopCount * 10);
            for (int i = 0; i < reel.CellCount; i++)
            {
                int row = reel.CellStripIndex(i) - whole;
                Assert.GreaterOrEqual(row, 0, $"Cell {i} drifted above the viewport.");
                Assert.LessOrEqual(row, def.rowCount + 1, $"Cell {i} drifted below the viewport.");
            }
        }

        [Test]
        public void CellsCoverEveryVisibleRow()
        {
            // One row with nothing painted in it is a blank reel that no assertion on the
            // symbol ids would catch, because the lookup would return -1 rather than a
            // wrong symbol.
            var def = BuildFixture();
            var view = MakeView(def);

            for (int r = 0; r < def.reelCount; r++)
                for (int row = 0; row < def.rowCount; row++)
                    Assert.AreNotEqual(-1, view.DisplayedSymbol(r, row),
                        $"Reel {r} row {row} has no cell at all.");
        }

        [Test]
        public void SymbolsWithoutSpritesFallBackToAPlaceholderTile()
        {
            // Every shipped definition has null sprites today (Art/Slots is empty). If the
            // fallback ever stops working the grid renders as a row of invisible quads,
            // which looks exactly like a black screen and a dead game.
            var def = BuildFixture();
            foreach (var symbol in def.symbols) symbol.sprite = null;

            var view = MakeView(def);
            AssertShowsGrid(view, def, new[] { 3, 2, 1, 0, 3 });

            var cell = view.GetComponentInChildren<ReelStripView>()
                .transform.Find("Strip")
                .GetComponentInChildren<UnityEngine.UI.Image>();
            Assert.IsNotNull(cell, "A reel cell has no Image to draw its tile into.");
            Assert.IsNotNull(cell.sprite, "A placeholder cell left its sprite null and would draw nothing.");
        }

        [Test]
        public void ConfiguringWithNoDefinitionClearsTheReels()
        {
            var def = BuildFixture();
            var view = MakeView(def);
            Assert.AreEqual(def.reelCount, view.ReelCount);

            view.Configure(null);
            Assert.AreEqual(0, view.ReelCount, "A null definition must not leave reels behind.");
        }

        [Test]
        public void HeldCells_AreDrawnUntilTheNextRoundClearsThem()
        {
            var def = BuildFixture();
            var view = MakeView(def);

            view.ShowLock(1, 2, 2, 5);
            var locked = view.transform.Find("Reel2/HeldCell");

            Assert.IsNotNull(locked, "A hold & spin lock must leave a visible tile on its reel.");
            Assert.IsNotNull(locked.Find("Value"),
                "A held symbol carrying a prize should label that prize.");

            view.ClearLocks();
            Assert.IsNull(view.transform.Find("Reel2/HeldCell"),
                "Held tiles must not survive into the next round.");
        }

        [Test]
        public void ReconfiguringTheSameDefinitionKeepsTheReels()
        {
            // The wallet event calls Refresh on every balance change, which reconfigures. If
            // that rebuilt the cells, a spin in flight would be wiped mid-turn.
            var def = BuildFixture();
            var view = MakeView(def);

            var reel = view.GetComponentInChildren<ReelStripView>();
            view.ShowStops(new[] { 1, 1, 1, 1, 1 });
            reel.ScrollTo(1.5f);

            view.Configure(def);

            Assert.AreEqual(def.reelCount, view.ReelCount);
            Assert.AreSame(reel, view.GetComponentInChildren<ReelStripView>(),
                "The same definition must reuse its reels rather than rebuild them.");
            Assert.AreEqual(1.5f, reel.ScrollPosition, 0.001f,
                "Reconfiguring knocked the reel off its position mid-spin.");
        }
    }
}