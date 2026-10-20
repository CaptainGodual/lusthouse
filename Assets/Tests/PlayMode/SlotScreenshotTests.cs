using System.Collections;
using System.Collections.Generic;
using LustHouse.Slots;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace LustHouse.Tests
{
    /// <summary>
    /// Captures a winning reel to a PNG, so the win presentation can be looked at rather
    /// than guessed at: the gold frame, and the symbol celebrating inside it.
    ///
    /// The symbols carry the real artwork, so the capture shows what a player sees. The
    /// strips are rigged so a line actually wins — the point is the presentation, not the
    /// maths.
    /// </summary>
    public class SlotScreenshotTests
    {
        private const int Reels = 5;
        private const int Rows = 3;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = 0; i < _spawned.Count; i++)
                if (_spawned[i] != null) Object.Destroy(_spawned[i]);
            _spawned.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator CaptureWinningReels()
        {
            var canvasGo = new GameObject("CaptureCanvas", typeof(RectTransform));
            _spawned.Add(canvasGo);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;

            var camGo = new GameObject("CaptureCamera", typeof(Camera));
            _spawned.Add(camGo);
            // The camera came with the GameObject: asking for a second one returns null,
            // because Unity does not allow two cameras on one object.
            var camera = camGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.07f, 0.04f, 0.09f, 1f);
            canvas.worldCamera = camera;

            var canvasRect = (RectTransform)canvasGo.transform;
            canvasRect.anchorMin = canvasRect.anchorMax = new Vector2(0.5f, 0.5f);
            canvasRect.sizeDelta = new Vector2(1400f, 700f);

            var reelsGo = new GameObject("Reels", typeof(RectTransform));
            _spawned.Add(reelsGo);
            reelsGo.transform.SetParent(canvasGo.transform, false);
            var reelsRect = (RectTransform)reelsGo.transform;
            reelsRect.anchorMin = Vector2.zero;
            reelsRect.anchorMax = Vector2.one;
            reelsRect.offsetMin = new Vector2(60f, 60f);
            reelsRect.offsetMax = new Vector2(-60f, -60f);

            var view = reelsGo.AddComponent<ReelsView>();
            view.fitToParent = true;
            view.Configure(BuildRiggedDefinition());

            // A win on the middle row, every reel the same symbol.
            var grid = new int[Reels][];
            var wins = new List<LineWin>();
            for (int r = 0; r < Reels; r++) grid[r] = new[] { 1, 1, 1 };

            view.ShowWinHighlights(new SpinResult
            {
                grid = grid,
                lineWins = new List<LineWin>
                {
                    new LineWin { paylineIndex = 0, count = Reels, symbolId = 1, payout = 250 }
                },
                totalWin = 25000,
                bet = 100
            });

            // Let the pulse get to the top of its first punch.
            yield return new WaitForSecondsRealtime(0.22f);

            ScreenGrab.Capture(canvas, camera, ScreenGrab.LogPath("slot-win.png"));
            Debug.Log("[LustHouse] slot win screenshot written to Logs/slot-win.png");
        }

        /// <summary>
        /// A definition with the real artwork and a rigged strip: the same symbol on every
        /// reel, so a line wins and the frames land on the symbols they belong to.
        /// </summary>
        private static SlotMachineDefinition BuildRiggedDefinition()
        {
            var source = UnityEditor.AssetDatabase.LoadAssetAtPath<SlotMachineDefinition>(
                "Assets/Data/Slots/Slot_VelvetRoom.asset");

            var def = ScriptableObject.CreateInstance<SlotMachineDefinition>();
            def.id = "capture_fixture";
            def.reelCount = Reels;
            def.rowCount = Rows;
            def.minBet = 100;
            def.maxBet = 1000;

            var symbols = new List<SymbolDefinition>();
            foreach (var symbol in source.symbols)
            {
                if (symbol == null) continue;
                symbols.Add(new SymbolDefinition
                {
                    id = symbol.id,
                    symbolName = symbol.symbolName,
                    sprite = symbol.sprite,
                    payoutsByMatch = symbol.payoutsByMatch,
                    isWild = symbol.isWild,
                    isScatter = symbol.isScatter,
                    isSpecial = symbol.isSpecial,
                    animatesOnWin = symbol.animatesOnWin
                });
            }
            def.symbols = symbols;

            // The Kiwi (16) on every stop, so the middle line always matches.
            def.reelStrips = new List<ReelStrip>();
            for (int r = 0; r < Reels; r++)
                def.reelStrips.Add(new ReelStrip
                {
                    symbols = new List<int> { 16, 16, 16, 16, 16, 16 }
                });

            def.paylines = new List<PaylineDefinition>
            {
                new PaylineDefinition { lineName = "Middle", rows = new[] { 1, 1, 1, 1, 1 } }
            };

            return def;
        }
    }
}
