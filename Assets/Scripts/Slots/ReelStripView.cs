using System;
using System.Collections;
using System.Collections.Generic;
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
            // Three objects per cell: the container that rides the strip, the icon inside it
            // (so symbolFill can pad the symbol without touching the strip pitch), and the
            // fallback glyph. A pool this small is not worth the indirection of a prefab.
            var go = new GameObject("Cell" + index, typeof(RectTransform));
            go.transform.SetParent(_stripRoot, false);
            var container = (RectTransform)go.transform;

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