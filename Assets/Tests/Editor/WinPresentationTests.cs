using System.Collections.Generic;
using LustHouse.Slots;
using LustHouse.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace LustHouse.Tests
{
    /// <summary>
    /// How a win is presented: a frame around the symbol, and the symbol's own
    /// animation.
    ///
    /// The marker used to be a translucent gold square laid over the symbol, which hid
    /// the one thing a win is about. It is a frame now, and the symbol animates inside it
    /// — unless the artwork has nothing to animate, which the symbol decides for itself.
    /// </summary>
    public class WinPresentationTests
    {
        private const int Reels = 5;
        private const int Rows = 3;

        private readonly List<UnityEngine.Object> _spawned = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _spawned.Count; i++)
                if (_spawned[i] != null) Object.DestroyImmediate(_spawned[i]);
            _spawned.Clear();
        }

        private static SlotMachineDefinition BuildFixture(bool animatesOnWin = true)
        {
            var def = ScriptableObject.CreateInstance<SlotMachineDefinition>();
            def.id = "win_fixture";
            def.reelCount = Reels;
            def.rowCount = Rows;
            def.minBet = 100;
            def.maxBet = 1000;

            def.symbols = new List<SymbolDefinition>
            {
                new SymbolDefinition
                {
                    id = 0, symbolName = "Wild", isWild = true, animatesOnWin = animatesOnWin
                },
                new SymbolDefinition
                {
                    id = 1, symbolName = "Ace", payoutsByMatch = new long[] { 5, 20, 50 },
                    animatesOnWin = animatesOnWin
                },
                new SymbolDefinition
                {
                    id = 2, symbolName = "King", payoutsByMatch = new long[] { 3, 10, 30 },
                    animatesOnWin = animatesOnWin
                }
            };

            def.reelStrips = new List<ReelStrip>();
            for (int r = 0; r < Reels; r++)
                def.reelStrips.Add(new ReelStrip
                {
                    symbols = new List<int> { r % 3, (r + 1) % 3, (r + 2) % 3, r % 3 }
                });

            def.paylines = new List<PaylineDefinition>
            {
                new PaylineDefinition { lineName = "Middle", rows = new[] { 1, 1, 1, 1, 1 } }
            };

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

        [Test]
        public void TheWinMarkerIsAFrame_NotAFill()
        {
            // A filled marker sits on top of the symbol and hides it. A frame is hollow:
            // the artwork shows through, and the animation is visible.
            var def = BuildFixture();
            var view = MakeView(def);

            var result = new SpinResult
            {
                grid = new[] { new[] { 0, 0, 0 }, new[] { 0, 0, 0 }, new[] { 0, 0, 0 }, new[] { 0, 0, 0 }, new[] { 0, 0, 0 } },
                lineWins = new List<LineWin>
                {
                    new LineWin { paylineIndex = 0, count = 3, symbolId = 0, payout = 500 }
                },
                totalWin = 500,
                bet = 100
            };

            view.ShowWinHighlights(result);

            // The win covers three reels, so three frames — one per matched symbol.
            Assert.AreEqual(3, view.WinFrameCount, "A three-symbol win should draw three frames.");

            var frame = view.transform.Find("Reel1/WinFrame");
            Assert.IsNotNull(frame, "The win frame was not created on the reel.");

            var image = frame.GetComponent<Image>();
            Assert.IsNotNull(image, "The win frame has no image.");
            Assert.AreEqual(UiChrome.Rim, image.sprite,
                "The win marker must be a hollow frame. A filled marker covers the symbol and " +
                "hides the win animation.");
            Assert.AreEqual(Image.Type.Sliced, image.type, "The frame must stretch to the cell.");

            // The frame is sized against the reel's cell, not against whatever the cell
            // happened to be when the win was drawn: the reels re-fit when the canvas
            // resolves, and a stale frame ends up two rows too big.
            var reel = frame.GetComponentInParent<ReelStripView>();
            Assert.IsNotNull(reel, "The frame is not on a reel.");
            var frameRect = (RectTransform)frame.transform;
            Assert.AreEqual(reel.cellSize * 1.1f, frameRect.sizeDelta.x, 0.5f,
                "The win frame must be sized against the reel's current cell size.");
        }

        [Test]
        public void ASymbolThatAnimates_Animates()
        {
            var def = BuildFixture(animatesOnWin: true);
            var view = MakeView(def);

            var reel = view.GetComponentInChildren<ReelStripView>();
            Assert.IsNotNull(reel, "The view built no reel.");

            Assert.IsTrue(reel.AnimateWin(0),
                "A symbol that animates on a win must animate.");
        }

        [Test]
        public void ASymbolThatOptsOut_DoesNotAnimate()
        {
            // The artwork decides. A static emblem that jumps reads as a glitch, not a win.
            var def = BuildFixture(animatesOnWin: false);
            var view = MakeView(def);

            var reel = view.GetComponentInChildren<ReelStripView>();
            Assert.IsNotNull(reel, "The view built no reel.");

            Assert.IsFalse(reel.AnimateWin(0),
                "A symbol with animatesOnWin off must not animate.");
        }

        [Test]
        public void Symbols_DefaultToAnimating()
        {
            // A new symbol with no art of its own still gets the standard pulse, so a
            // winning symbol always does something.
            var symbol = new SymbolDefinition { id = 1, symbolName = "Ace" };
            Assert.IsTrue(symbol.animatesOnWin,
                "Symbols must animate on a win unless they explicitly opt out.");
        }

        [Test]
        public void ClearingTheHighlights_StopsTheWinAnimation()
        {
            // An authored clip loops, and it drives the symbol's sprite. If it is not
            // stopped when the highlights are cleared, the symbol keeps animating over
            // the next spin — and it is left on whatever frame the clip stopped on.
            var def = BuildFixture(animatesOnWin: true);

            var clip = new AnimationClip { name = "WinClip" };
            clip.legacy = true;      // the legacy Animation component only plays these
            clip.SetCurve("", typeof(Transform), "localScale.x",
                AnimationCurve.Constant(0f, 1f, 1f));
            def.symbols[0].winAnimation = clip;

            var view = MakeView(def);

            var reel = view.GetComponentInChildren<ReelStripView>();
            Assert.IsNotNull(reel, "The view built no reel.");

            var cell = reel.transform.Find("Strip/Cell0");
            Assert.IsNotNull(cell, "The reel built no cell.");

            var icon = cell.Find("Icon");
            Assert.IsNotNull(icon, "The cell built no icon.");

            reel.AnimateWin(0);

            var animation = icon.GetComponent<Animation>();
            Assert.IsNotNull(animation,
                "The win animation added no Animation component to the symbol, so an " +
                "authored clip has nothing to play on.");

            Assert.AreSame(clip, animation.clip,
                "The authored clip should be attached to the symbol while it celebrates.");

            view.ClearHighlights();

            Assert.IsNull(animation.clip,
                "The win animation is still attached to the symbol after the highlights " +
                "were cleared, so it keeps animating over the next spin.");
        }

        [Test]
        public void ASymbolWithFrames_PlaysThemOnAWin()
        {
            // A sprite sheet is frames, not a clip. This is the path that needs no
            // authored AnimationClip at all: the frames go in the symbol and they play.
            var def = BuildFixture(animatesOnWin: true);

            var still = MakeSprite("Still");
            var frameA = MakeSprite("FrameA");
            var frameB = MakeSprite("FrameB");

            def.symbols[0].sprite = still;
            def.symbols[0].winAnimationFrames = new[] { frameA, frameB };

            var view = MakeView(def);

            var reel = view.GetComponentInChildren<ReelStripView>();
            Assert.IsNotNull(reel, "The view built no reel.");

            var cell = reel.transform.Find("Strip/Cell0");
            Assert.IsNotNull(cell, "The reel built no cell.");

            var icon = cell.Find("Icon")?.GetComponent<Image>();
            Assert.IsNotNull(icon, "The cell built no icon.");

            reel.AnimateWin(0);

            Assert.IsTrue(icon.sprite == frameA || icon.sprite == frameB,
                "The win animation did not put one of the frames on the symbol, so the " +
                "sprite sheet is not being played.");

            // The frames own the icon's sprite while they play, and the symbol's own
            // frame has to come back when they stop.
            view.ClearHighlights();

            Assert.AreSame(still, icon.sprite,
                "The symbol is still on an animation frame after the win ended.");
        }

        [Test]
        public void ASymbolWithNoFramesAndNoClip_JustPulses()
        {
            // The fallback: no artwork to animate, so the symbol scales and lights up
            // rather than swapping sprites.
            var def = BuildFixture(animatesOnWin: true);
            def.symbols[0].winAnimationFrames = new Sprite[0];

            var view = MakeView(def);
            var reel = view.GetComponentInChildren<ReelStripView>();
            Assert.IsNotNull(reel, "The view built no reel.");

            var cell = reel.transform.Find("Strip/Cell0");
            var icon = cell.Find("Icon");
            Assert.IsNotNull(icon, "The cell built no icon.");

            reel.AnimateWin(0);

            Assert.IsNull(icon.GetComponent<Animation>(),
                "A symbol with no clip should not get an Animation component.");
        }

        private Sprite MakeSprite(string name)
        {
            var texture = new Texture2D(4, 4) { name = name + "Texture" };
            _spawned.Add(texture);

            var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name;
            _spawned.Add(sprite);
            return sprite;
        }

        [Test]
        public void TheRimSprite_ActuallyHasAFrame()
        {
            // The rim is generated, not drawn by hand, and a generated frame can come out
            // empty: the hole and the frame spanned the same box, so the difference was
            // nothing and every frame in the game was invisible. It has to be checked.
            var rim = UiChrome.Rim;
            Assert.IsNotNull(rim, "The rim sprite is missing.");
            Assert.IsNotNull(rim.texture, "The rim sprite has no texture.");

            var texture = rim.texture;
            var pixels = texture.GetPixels();
            Assert.Greater(pixels.Length, 0, "The rim texture has no pixels.");

            int opaque = 0;
            int clearCentre = 0;
            int centre = texture.width / 2;

            for (int y = 0; y < texture.height; y++)
            {
                for (int x = 0; x < texture.width; x++)
                {
                    var pixel = pixels[y * texture.width + x];
                    bool solid = pixel.a > 0.5f;
                    if (solid) opaque++;

                    // The middle of the frame has to be clear, or it covers the symbol
                    // it is supposed to be framing.
                    if (solid && Mathf.Abs(x - centre) < 4 && Mathf.Abs(y - centre) < 4) clearCentre++;
                }
            }

            Assert.Greater(opaque, 0, "The rim sprite is entirely transparent, so no frame draws.");
            Assert.AreEqual(0, clearCentre,
                "The middle of the rim sprite must be transparent so the symbol shows through.");
        }
    }
}
