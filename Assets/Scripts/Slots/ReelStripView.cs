using System;
using System.Collections;
using System.Collections.Generic;
using LustHouse.Core;
using LustHouse.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LustHouse.Slots
{
    /// <summary>
    /// One reel column: a masked viewport of symbol cells that scrolls through a single
    /// reel's strip.
    ///
    /// The maths in <see cref="ReelEngine.BuildGrid"/> already decides what every row shows:
    /// <c>grid[reel][row] == strip[(stop + row) % stripLength]</c>. This view is only the
    /// animation of that rule, so the invariant it must never break is that a reel sitting
    /// at stop <c>s</c> shows exactly the grid the engine built — a reel that displays
    /// prettier symbols than the maths scored is a game that pays wrong.
    ///
    /// It holds one cell per visible row plus one hidden row either end, and recycles a cell
    /// that scrolls out of the viewport by moving it a whole cell-count further down its own
    /// strip. That keeps the tiling seamless and costs no allocation per frame.
    /// </summary>
    [DisallowMultipleComponent]
    public class ReelStripView : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Edge length of one symbol cell in reference pixels. ReelsView overwrites " +
                 "this when fitToParent is on.")]
        public float cellSize = 128f;

        [Tooltip("Gap between cells, in reference pixels.")]
        public float cellGap = 8f;

        [Range(0.1f, 1f)]
        [Tooltip("Icon size as a fraction of its cell. The remainder is the symbol's padding.")]
        public float symbolFill = 0.88f;

        [Header("Motion")]
        [Tooltip("Whole turns of the strip before the reel settles. Never fewer than one, so " +
                 "a spin always reads as a spin rather than a snap.")]
        public int minimumLaps = 3;

        [Tooltip("Eases the travel from the start stop to the final one. The default spends " +
                 "most of the spin fast and slows into the stop.")]
        public AnimationCurve stopCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        /// <summary>Placeholder fills for symbols that have no sprite yet (Art/Slots is empty).</summary>
        private static readonly Color[] PlaceholderPalette =
        {
            new Color32(0x6E, 0x2A, 0x5A, 0xFF),   // plum
            new Color32(0x2A, 0x5E, 0x6E, 0xFF),   // teal
            new Color32(0x6E, 0x5A, 0x2A, 0xFF),   // brass
            new Color32(0x4A, 0x2A, 0x6E, 0xFF),   // violet
            new Color32(0x6E, 0x3A, 0x2A, 0xFF),   // rust
            new Color32(0x2A, 0x6E, 0x45, 0xFF)    // emerald
        };

        private sealed class Cell
        {
            public RectTransform rect;
            public Image icon;
            public Text glyph;

            /// <summary>The soft halo behind a winning symbol.</summary>
            public Image glow;

            /// <summary>Unwrapped index into the strip. Grows without bound as the reel turns.</summary>
            public int stripIndex;

            /// <summary>Last strip position painted, so a cell is not re-tinted every frame.</summary>
            public int painted = int.MinValue;
        }

        private readonly List<Cell> _cells = new List<Cell>();
        private List<int> _strip;
        private SlotMachineDefinition _definition;
        private RectTransform _stripRoot;
        private Coroutine _routine;
        private int _rowCount;
        private int _reelIndex;
        private float _scroll;

        /// <summary>Running win animations, so the next spin can stop them.</summary>
        private readonly List<Coroutine> _winRoutines = new List<Coroutine>();
        private readonly HashSet<Cell> _animatingCells = new HashSet<Cell>();

        private float _travelFrom, _travelTo, _duration, _elapsed;
        private bool _spinning;

        /// <summary>Which reel of the definition this column shows.</summary>
        public int ReelIndex => _reelIndex;

        /// <summary>Strip length of this reel. Zero until <see cref="Configure"/> has run.</summary>
        public int StopCount => _strip != null ? _strip.Count : 0;

        /// <summary>Where the reel is resting, wrapped back into the strip.</summary>
        public int CurrentStop => StopCount > 0 ? Mod(Mathf.FloorToInt(_scroll), StopCount) : 0;

        /// <summary>
        /// Positive modulo. <c>%</c> goes negative for a negative dividend, and a strip index
        /// that has scrolled far past the start of a reel is exactly that.
        /// </summary>
        private static int Mod(int value, int length) =>
            length <= 0 ? 0 : (value % length + length) % length;

        /// <summary>Cells live: one per row, plus one hidden row above and one below.</summary>
        public int CellCount => _cells.Count;

        public bool IsSpinning => _spinning;

        /// <summary>
        /// Destroy in play mode, immediately in the editor. <c>Destroy</c> from edit mode —
        /// which the EditMode tests do on every reconfigure — logs an error and defers the
        /// teardown, leaving the old cells parented and drawn.
        /// </summary>
        private static void SafeDestroy(UnityEngine.Object target)
        {
            if (target == null) return;

            // Destroy is deferred to the end of the frame in play mode; a still-active strip
            // would be drawn alongside the one replacing it.
            if (target is GameObject go) go.SetActive(false);
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        /// <summary>Continuous position along the strip. Whole numbers are at-rest stops.</summary>
        public float ScrollPosition => _scroll;

        // ---- construction ---------------------------------------------------

        /// <summary>
        /// Builds the cells for one reel of a definition. Safe to call again: the cells are
        /// discarded and rebuilt rather than appended to.
        /// </summary>
        public void Configure(SlotMachineDefinition definition, int reelIndex)
        {
            // Loud, not blank: a definition whose reelStrips disagrees with reelCount would
            // otherwise leave an empty viewport that reads as a dead game rather than a bug.
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (reelIndex < 0 || reelIndex >= definition.reelStrips.Count ||
                definition.reelStrips[reelIndex]?.symbols == null)
                throw new ArgumentOutOfRangeException(nameof(reelIndex),
                    $"Definition '{definition.id}' has no strip for reel {reelIndex}.");

            _definition = definition;
            _reelIndex = reelIndex;
            _rowCount = Mathf.Max(1, definition.rowCount);
            _strip = definition.reelStrips[reelIndex].symbols;

            var viewport = (RectTransform)transform;

            var back = GetComponent<Image>();
            if (back == null)
            {
                back = gameObject.AddComponent<Image>();
                back.raycastTarget = false;
            }
            back.sprite = UiFactory.Solid;

            if (GetComponent<RectMask2D>() == null) gameObject.AddComponent<RectMask2D>();

            if (_stripRoot != null) SafeDestroy(_stripRoot.gameObject);
            _stripRoot = new GameObject("Strip", typeof(RectTransform)).GetComponent<RectTransform>();
            _stripRoot.SetParent(viewport, false);
            UiFactory.Stretch(_stripRoot);

            _cells.Clear();
            for (int i = 0; i < _rowCount + 2; i++) _cells.Add(NewCell(i));

            SnapTo(0);
        }

        private Cell NewCell(int index)
        {
            // Four objects per cell: the container that rides the strip, the icon inside it
            // (so symbolFill can pad the symbol without touching the strip pitch), the
            // fallback glyph, and the halo a winning symbol lights up in. A pool this
            // small is not worth the indirection of a prefab.
            var go = new GameObject("Cell" + index, typeof(RectTransform));
            go.transform.SetParent(_stripRoot, false);
            var container = (RectTransform)go.transform;

            // The halo sits behind the icon, so a winning symbol is lit from behind
            // rather than painted over.
            var glowGo = new GameObject("Glow", typeof(RectTransform));
            glowGo.transform.SetParent(container, false);
            var glow = glowGo.AddComponent<Image>();
            glow.sprite = UiChrome.Glow;
            glow.color = new Color(1f, 0.86f, 0.55f, 0f);
            glow.raycastTarget = false;
            glow.preserveAspect = true;

            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(container, false);
            var icon = iconGo.AddComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;      // non-square art must not stretch to the cell

            var glyph = UiFactory.NewText("Glyph", iconGo.transform, "", 44, FontStyle.Bold,
                UiFactory.TextPrimary);
            UiFactory.Stretch(glyph.rectTransform);

            return new Cell
            {
                rect = container,
                icon = icon,
                glow = glow,
                glyph = glyph,
                stripIndex = index
            };
        }

        /// <summary>
        /// Re-seats the cells against a stop with no animation. Only valid when the reel is
        /// at rest — it re-seeds the strip indices rather than scrolling to them.
        /// </summary>
        public void SnapTo(int stopIndex)
        {
            _spinning = false;
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            _scroll = stopIndex;
            int baseIndex = stopIndex;
            for (int i = 0; i < _cells.Count; i++) _cells[i].stripIndex = baseIndex + i;
            Layout();
        }

        // ---- win animation ----------------------------------------------------

        /// <summary>
        /// Plays the win animation for the symbol in a visible row.
        ///
        /// A symbol decides for itself whether it moves: artwork that has nothing to
        /// animate should not jump, because a static emblem that pulses reads as a
        /// glitch rather than a win. The artwork author turns it off per symbol.
        ///
        /// Returns false when there was nothing to animate, so a caller can tell a
        /// symbol that opted out from a symbol that was never there.
        /// </summary>
        public bool AnimateWin(int row)
        {
            if (_definition == null || _cells.Count == 0) return false;

            var symbol = SymbolAt(row);
            if (symbol == null || !symbol.animatesOnWin) return false;

            var cell = CellAt(row);
            if (cell == null) return false;

            // A symbol that is already celebrating does not start again: two pulses on
            // one cell fight over its scale.
            if (_animatingCells.Contains(cell)) return true;

            _animatingCells.Add(cell);
            _winRoutines.Add(StartCoroutine(Pulse(cell, symbol)));
            return true;
        }

        /// <summary>
        /// Stops every running win animation and puts the cells back.
        ///
        /// Called when the next spin clears the highlights. An animation that outlives
        /// its win leaves a symbol pulsing on a losing spin, which is the sort of thing
        /// that makes a player stop trusting the machine.
        ///
        /// Stopping the pulse is not enough on its own. An authored clip drives the
        /// icon's sprite and loops, so the <c>Animation</c> component has to be stopped
        /// too — and the clip leaves the icon on whatever frame it stopped on, so the
        /// symbol's own frame goes back on.
        /// </summary>
        public void StopWinAnimations()
        {
            for (int i = 0; i < _winRoutines.Count; i++)
                if (_winRoutines[i] != null) StopCoroutine(_winRoutines[i]);

            _winRoutines.Clear();
            _animatingCells.Clear();

            for (int i = 0; i < _cells.Count; i++)
            {
                var cell = _cells[i];
                cell.icon.rectTransform.localScale = Vector3.one;
                cell.glow.color = new Color(1f, 0.86f, 0.55f, 0f);

                // The clip drove the icon's sprite too, and it loops. Stopping the pulse
                // is not enough: the clip has to be stopped as well.
                var animation = cell.icon.GetComponent<Animation>();
                if (animation != null)
                {
                    animation.Stop();
                    animation.clip = null;
                }

                // The symbol's own frame goes back on. A sprite-sheet animation owns the
                // icon's sprite while it plays, and the clip leaves it on whatever frame
                // it stopped on — so this is not just for the frames.
                if (_strip == null || _definition == null || StopCount == 0) continue;

                int position = Mod(cell.stripIndex, StopCount);
                cell.painted = position;
                Paint(cell, _strip[position]);
            }
        }

        /// <summary>The symbol shown in a visible row, or null when the row is empty.</summary>
        public SymbolDefinition SymbolAt(int row)
        {
            if (_strip == null || StopCount == 0 || _definition == null) return null;
            if (row < 0 || row >= _rowCount) return null;

            int position = Mod(Mathf.FloorToInt(_scroll) + row, StopCount);
            return _definition.SymbolById(_strip[position]);
        }

        /// <summary>The cell showing a visible row, or null.</summary>
        private Cell CellAt(int row)
        {
            int whole = Mathf.FloorToInt(_scroll);
            for (int i = 0; i < _cells.Count; i++)
                if (_cells[i].stripIndex == whole + row) return _cells[i];
            return null;
        }

        /// <summary>
        /// The standard win pulse: the symbol punches up and the halo behind it lights.
        ///
        /// Two ways to animate a winning symbol. The frames are the simple one: a sprite
        /// sheet imported as Multiple sprites, dragged into the symbol, played here. The
        /// clip is the older path — it needs a legacy AnimationClip, which the editor
        /// does not make for you any more — so it is only used when there are no frames.
        ///
        /// A symbol with its own clip plays that instead — an authored animation can
        /// swap the sprite for a frame sequence, which is how a symbol that really does
        /// have an animation in it shows it.
        /// </summary>
        private IEnumerator Pulse(Cell cell, SymbolDefinition symbol)
        {
            float seconds = Mathf.Max(0.1f, symbol.winAnimationSeconds);
            float elapsed = 0f;

            var iconRect = cell.icon.rectTransform;
            Vector3 baseScale = iconRect.localScale;
            Color glowOff = new Color(1f, 0.86f, 0.55f, 0f);

            Sprite[] frames = symbol.winAnimationFrames;
            bool frameAnimation = frames != null && frames.Length > 1;

            if (!frameAnimation && symbol.winAnimation != null)
            {
                var animation = cell.icon.gameObject.GetComponent<Animation>();
                if (animation == null)
                {
                    animation = cell.icon.gameObject.AddComponent<Animation>();
                    animation.wrapMode = WrapMode.Loop;
                }

                // The legacy Animation component only plays a legacy clip, and the editor
                // no longer makes one for you: an AnimationClip authored in the Animation
                // window is a Mecanim clip, and handing it here does nothing but log
                // "must be marked as Legacy". The frames above are the path that works.
                if (!symbol.winAnimation.legacy)
                {
                    LustLog.Warn($"[LustHouse] '{symbol.symbolName}' has a win animation " +
                                 "clip that is not a legacy clip, so it cannot play. Use " +
                                 "'Win Animation Frames' instead, or mark the clip legacy.");
                }
                else
                {
                    animation.clip = symbol.winAnimation;
                    animation.Play();
                }
            }

            int frameIndex = 0;
            float frameClock = 0f;
            float frameDuration = 1f / Mathf.Max(1f, symbol.winAnimationFramesPerSecond);

            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;

                if (frameAnimation)
                {
                    frameClock += Time.unscaledDeltaTime;
                    while (frameClock >= frameDuration)
                    {
                        frameClock -= frameDuration;
                        frameIndex = (frameIndex + 1) % frames.Length;
                    }

                    cell.icon.sprite = frames[frameIndex];
                }

                // One up-and-down per half of the window, so the symbol punches twice
                // inside the time it is given.
                float k = Mathf.PingPong(elapsed / (seconds * 0.5f), 1f);
                float punch = Mathf.Sin(k * Mathf.PI);

                iconRect.localScale = baseScale * (1f + 0.14f * punch);
                cell.glow.color = new Color(1f, 0.86f, 0.55f, 0.6f * punch);

                yield return null;
            }

            iconRect.localScale = baseScale;
            cell.glow.color = glowOff;

            // The frames took over the icon's sprite; the symbol's own frame goes back on.
            if (frameAnimation && symbol.sprite != null) cell.icon.sprite = symbol.sprite;
        }

        // ---- layout ---------------------------------------------------------

        /// <summary>
        /// Sizes and positions the cells for the current cell size. Called by
        /// <see cref="ReelsView"/> whenever the parent rect changes, not just on configure.
        /// </summary>
        public void Layout()
        {
            if (_stripRoot == null || _cells.Count == 0) return;

            float pitch = cellSize + cellGap;
            float icon = cellSize * Mathf.Clamp(symbolFill, 0.1f, 1f);

            for (int i = 0; i < _cells.Count; i++)
            {
                var cell = _cells[i];
                cell.rect.sizeDelta = new Vector2(cellSize, cellSize);
                cell.icon.rectTransform.sizeDelta = new Vector2(icon, icon);
                cell.glow.rectTransform.sizeDelta = new Vector2(icon * 1.7f, icon * 1.7f);

                // A resize in the middle of a pulse would otherwise leave the symbol
                // scaled up for good.
                cell.icon.rectTransform.localScale = Vector3.one;

                // The fallback initial has to track the cell or it is unreadable at the small
                // end of the fit-to-parent range.
                cell.glyph.fontSize = Mathf.RoundToInt(icon * 0.42f);
            }

            ScrollTo(_scroll);
        }

        /// <summary>
        /// Moves the strip to a continuous position. Whole numbers land on stops; fractions
        /// are the blur in between. Cells that leave the viewport are recycled down the
        /// strip, so a reel of any length scrolls for as long as you like without allocating.
        /// </summary>
        public void ScrollTo(float position)
        {
            if (_cells.Count == 0 || StopCount == 0) return;

            _scroll = position;
            float pitch = cellSize + cellGap;
            float rowZero = (_rowCount - 1) * 0.5f * pitch;      // centre of the top row
            int whole = Mathf.FloorToInt(position);
            float fraction = position - whole;

            for (int i = 0; i < _cells.Count; i++)
            {
                var cell = _cells[i];
                int row = cell.stripIndex - whole;

                // Recycle out-of-viewport cells by exactly one cell-count, which keeps the
                // row and strip indices in step and the strip seamless however many times it
                // loops. The valid row band is 0..rowCount+1; the window itself is 0..rowCount-1.
                while (row < 0) { row += _cells.Count; cell.stripIndex += _cells.Count; }
                while (row > _rowCount + 1) { row -= _cells.Count; cell.stripIndex -= _cells.Count; }

                cell.rect.anchoredPosition = new Vector2(0f, rowZero - (row - fraction) * pitch);

                int position_on_strip = Mod(cell.stripIndex, StopCount);
                if (position_on_strip != cell.painted)
                {
                    cell.painted = position_on_strip;
                    Paint(cell, _strip[position_on_strip]);
                }
            }
        }

        private void Paint(Cell cell, int symbolId)
        {
            var symbol = _definition.SymbolById(symbolId);

            // No art yet: a tinted tile with the symbol's initial, so the grid is readable
            // before anyone has drawn a sprite. Assigning SymbolDefinition.sprite replaces
            // this with no other change.
            if (symbol != null && symbol.sprite != null)
            {
                cell.icon.sprite = symbol.sprite;
                cell.icon.color = Color.white;
                if (cell.glyph.gameObject.activeSelf) cell.glyph.gameObject.SetActive(false);
                return;
            }

            cell.icon.sprite = UiFactory.Solid;
            cell.icon.color = PlaceholderColor(symbol, symbolId);
            if (!cell.glyph.gameObject.activeSelf) cell.glyph.gameObject.SetActive(true);
            cell.glyph.text = GlyphFor(symbol, symbolId);
            cell.glyph.color = UiFactory.TextPrimary;
        }

        private static Color PlaceholderColor(SymbolDefinition symbol, int symbolId)
        {
            if (symbol == null) return UiFactory.VelvetRaised;
            if (symbol.isWild) return UiFactory.GoldPrimary;
            if (symbol.isScatter) return UiFactory.NeonCyan;
            if (symbol.isSpecial) return UiFactory.NeonMagenta;
            return PlaceholderPalette[Mathf.Abs(symbolId) % PlaceholderPalette.Length];
        }

        private static string GlyphFor(SymbolDefinition symbol, int symbolId)
        {
            if (symbol == null) return "?";
            if (string.IsNullOrEmpty(symbol.symbolName)) return "#" + symbolId;
            return char.ToUpperInvariant(symbol.symbolName[0]).ToString();
        }

        // ---- motion ---------------------------------------------------------

        /// <summary>
        /// Starts the reel turning toward a stop. Always travels at least
        /// <see cref="minimumLaps"/> whole turns, even when the stop it lands on is the one it
        /// is already on, so a repeat stop still reads as a spin.
        /// </summary>
        public void BeginSpin(int toStop, float durationSeconds)
        {
            if (StopCount == 0 || _cells.Count == 0) return;

            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            int laps = Mathf.Max(1, minimumLaps);
            int forward = StopCount > 0
                ? Mod(toStop - CurrentStop, StopCount)
                : 0;

            _travelFrom = _scroll;
            _travelTo = _travelFrom + laps * StopCount + forward;
            _duration = Mathf.Max(0.05f, durationSeconds);
            _elapsed = 0f;
            _spinning = true;
            _routine = StartCoroutine(SpinRoutine());
        }

        /// <summary>Ends the spin where it stands and snaps onto the nearest stop.</summary>
        public void StopSpin()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
            _spinning = false;
            SnapTo(CurrentStop);
        }

        private IEnumerator SpinRoutine()
        {
            while (_elapsed < _duration)
            {
                _elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(_elapsed / _duration);
                ScrollTo(Mathf.LerpUnclamped(_travelFrom, _travelTo, stopCurve.Evaluate(t)));
                yield return null;
            }

            ScrollTo(_travelTo);
            _scroll = _travelTo;
            _spinning = false;
            _routine = null;
        }

        // ---- introspection (tests + the presenter's debug readout) ----------

        /// <summary>
        /// The symbol currently painted in a visible row, or -1 if the reel is not configured.
        /// A reel at rest must agree with <see cref="ReelEngine.BuildGrid"/>.
        /// </summary>
        public int DisplayedSymbol(int row)
        {
            if (_cells.Count == 0 || StopCount == 0) return -1;

            int whole = Mathf.FloorToInt(_scroll);
            foreach (var cell in _cells)
                if (cell.stripIndex - whole == row)
                    return _strip[Mod(cell.stripIndex, StopCount)];

            return -1;
        }

        /// <summary>The unwrapped strip index of a cell. Bounded by the tests.</summary>
        public int CellStripIndex(int index) =>
            index >= 0 && index < _cells.Count ? _cells[index].stripIndex : int.MinValue;
    }
}