using System.Collections;
using System.Collections.Generic;
using LustHouse.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LustHouse.Slots
{
    /// <summary>
    /// The reel grid: a frame, one <see cref="ReelStripView"/> per reel, and the stop order
    /// for a spin.
    ///
    /// Every visual choice a designer needs — cell size, gap, icon padding, how many turns a
    /// reel takes, how far apart the reel starts are — is a field on this component, so the
    /// whole look of a game's reels can be tuned in the inspector without touching C#.
    /// Which icons appear is data, not code: assign <c>SymbolDefinition.sprite</c> on the
    /// definition and the reels draw them. Symbols without a sprite get a tinted tile with
    /// their initial, which is what the grid looks like today because Art/Slots is still empty.
    ///
    /// This component draws. It never evaluates: the stops come from
    /// <see cref="SpinResult.stops"/> and the grid those stops imply is the grid the maths
    /// already scored.
    /// </summary>
    [DisallowMultipleComponent]
    public class ReelsView : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Derive the cell size from this rect instead of using the field below. Keeps " +
                 "the grid correct across resolutions without re-tuning per screen.")]
        public bool fitToParent = true;

        [Tooltip("Edge length of one symbol cell in reference pixels. Used when fitToParent " +
                 "is off, and as the starting value before the rect has a size.")]
        public float cellSize = 128f;

        [Tooltip("Gap between cells, in reference pixels.")]
        public float cellGap = 10f;

        [Range(0.1f, 1f)]
        [Tooltip("Icon size as a fraction of its cell.")]
        public float symbolFill = 0.88f;

        public Color frameColor = new Color(0f, 0f, 0f, 0f);
        public Color reelBackColor = new Color32(0x0A, 0x05, 0x0B, 0xFF);

        [Header("Motion")]
        [Tooltip("Whole turns each reel makes before settling.")]
        public int minimumLaps = 3;

        [Tooltip("Share of the spin spent staggering the reel starts, so the reels land one " +
                 "after another instead of together. 0 starts them all at once.")]
        [Range(0f, 0.9f)]
        public float staggerFraction = 0.45f;

        [Tooltip("Eases a reel's travel. The default spends most of the spin fast and slows " +
                 "into the stop.")]
        public AnimationCurve stopCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("Hold on the definition's anticipationSeconds once the last reel has landed. " +
                 "Off by default — at 0.9s that is a dead pause on every spin.")]
        public bool playAnticipation = false;

        private readonly List<ReelStripView> _reels = new List<ReelStripView>();
        /// <summary>
        /// A win frame, remembered with the reel and row it belongs to so it can be re-fitted
        /// when the reels resize.
        /// </summary>
        private sealed class WinFrame
        {
            public GameObject go;
            public int reelIndex;
            public int row;
        }

        private readonly List<WinFrame> _winFrames = new List<WinFrame>();
        private readonly List<GameObject> _locks = new List<GameObject>();
        private RectTransform _winLinesRoot;
        private SlotMachineDefinition _definition;
        private float _lastWidth = -1f;
        private float _lastHeight = -1f;

        public SlotMachineDefinition Definition => _definition;

        public int ReelCount => _reels.Count;

        /// <summary>True while any reel is still turning.</summary>
        public bool IsSpinning
        {
            get
            {
                for (int i = 0; i < _reels.Count; i++)
                    if (_reels[i].IsSpinning) return true;
                return false;
            }
        }

        // ---- construction ---------------------------------------------------

        /// <summary>
        /// Builds the reels for a definition. Re-entrant: calling it again with the same
        /// definition (the wallet event does this on every balance change) only re-runs the
        /// layout instead of rebuilding the cells.
        /// </summary>
        public void Configure(SlotMachineDefinition definition)
        {
            if (definition == null)
            {
                Clear();
                return;
            }

            if (_definition == definition && _reels.Count == definition.reelCount)
            {
                Layout();
                return;
            }

            Clear();

            _definition = definition;

            var frame = GetComponent<Image>();
            if (frame == null)
            {
                frame = gameObject.AddComponent<Image>();
                frame.raycastTarget = false;
            }
            frame.sprite = UiFactory.Solid;
            frame.color = frameColor;

            for (int r = 0; r < definition.reelCount; r++)
            {
                var go = new GameObject("Reel" + (r + 1), typeof(RectTransform));
                go.transform.SetParent(transform, false);

                var reel = go.AddComponent<ReelStripView>();
                reel.cellSize = cellSize;
                reel.cellGap = cellGap;
                reel.symbolFill = symbolFill;
                reel.minimumLaps = minimumLaps;
                reel.stopCurve = stopCurve;
                reel.Configure(definition, r);
                reel.GetComponent<Image>().color = reelBackColor;

                _reels.Add(reel);
            }

            Layout();
        }

        private void Clear()
        {
            ClearHighlights();
            ClearLocks();

            for (int i = 0; i < _reels.Count; i++)
                if (_reels[i] != null) SafeDestroy(_reels[i].gameObject);
            _reels.Clear();
            _definition = null;
            _lastWidth = -1f;
            _lastHeight = -1f;
        }

        /// <summary>
        /// Destroy in play mode, immediately in the editor. <c>Destroy</c> from edit mode
        /// logs an error and defers the teardown, so the old reels stay parented and drawn.
        /// </summary>
        private static void SafeDestroy(GameObject target)
        {
            if (target == null) return;

            // Destroy is deferred to the end of the frame in play mode, so a teardown during a
            // reconfigure would draw the old reels and the new ones in the same frame.
            target.SetActive(false);
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        // ---- layout ---------------------------------------------------------

        private void LateUpdate()
        {
            // The canvas does not have a size until it has been laid out once, so an early
            // Configure gets a zero rect. Re-fit when it arrives rather than sticking at the
            // fallback cell size until the next spin.
            if (!fitToParent || _definition == null) return;

            var rect = (RectTransform)transform;
            if (Mathf.Abs(rect.rect.width - _lastWidth) < 0.5f &&
                Mathf.Abs(rect.rect.height - _lastHeight) < 0.5f) return;

            Layout();
        }

        /// <summary>Fits the cells to this rect and lays the reels out in a row.</summary>
        public void Layout()
        {
            if (_definition == null || _reels.Count == 0) return;

            var rect = (RectTransform)transform;
            Vector2 size = rect.rect.size;
            if (size.x <= 0f || size.y <= 0f) return;   // canvas not resolved yet

            float cell = cellSize;
            if (fitToParent)
            {
                int reels = _definition.reelCount;
                int rows = Mathf.Max(1, _definition.rowCount);
                float byWidth = (size.x - cellGap * (reels + 1)) / reels;
                float byHeight = (size.y - cellGap * (rows + 1)) / rows;
                cell = Mathf.Max(8f, Mathf.Min(byWidth, byHeight));
            }

            float width = _reels.Count * cell + (_reels.Count - 1) * cellGap;
            float left = -width * 0.5f;
            float height = Mathf.Max(1, _definition.rowCount) * (cell + cellGap) - cellGap;

            for (int i = 0; i < _reels.Count; i++)
            {
                var reel = _reels[i];
                reel.cellSize = cell;
                reel.cellGap = cellGap;
                reel.symbolFill = symbolFill;

                UiFactory.Place((RectTransform)reel.transform, new Vector2(0.5f, 0.5f),
                    new Vector2(cell, height),
                    new Vector2(left + cell * 0.5f + i * (cell + cellGap), 0f));

                reel.Layout();
            }

            LayoutWinFrames();

            _lastWidth = size.x;
            _lastHeight = size.y;
        }

        // ---- win highlights -------------------------------------------------

        /// <summary>
        /// Removes every win decoration drawn by a previous spin: the symbol highlights and
        /// the lines connecting the paying symbols.
        /// </summary>
        public void ClearHighlights()
        {
            for (int i = 0; i < _winFrames.Count; i++)
                if (_winFrames[i].go != null) SafeDestroy(_winFrames[i].go);
            _winFrames.Clear();
            ClearWinLines();
        }

        /// <summary>Number of paying lines currently drawn. Exposed for tests and debug UI.</summary>
        public int WinLineCount => _winLinesRoot != null ? _winLinesRoot.childCount : 0;

        /// <summary>Number of win frames currently drawn. Exposed for tests and debug UI.</summary>
        public int WinFrameCount => _winFrames.Count;

        private void ClearWinLines()
        {
            if (_winLinesRoot == null) return;
            SafeDestroy(_winLinesRoot.gameObject);
            _winLinesRoot = null;
        }

        // ---- hold & spin ----------------------------------------------------

        /// <summary>
        /// Removes every held-cell overlay. Called when a new round starts, never between
        /// respins: a lock has to survive the respin that follows it.
        /// </summary>
        public void ClearLocks()
        {
            for (int i = 0; i < _locks.Count; i++)
                if (_locks[i] != null) SafeDestroy(_locks[i]);
            _locks.Clear();
        }

        /// <summary>
        /// Draws a held cell: an opaque tile carrying the symbol and its prize, over the
        /// reel. Because it is parented to the column rather than the scrolling strip, the
        /// tile stays put while the reel keeps turning behind it.
        /// </summary>
        /// <param name="betMultiple">The cell's prize in bet-multiples. 0 hides the label.</param>
        public void ShowLock(int reelIndex, int row, int symbolId, long betMultiple)
        {
            if (_definition == null || reelIndex < 0 || reelIndex >= _reels.Count) return;
            if (row < 0 || row >= _definition.rowCount) return;

            var reel = _reels[reelIndex];
            if (reel == null) return;

            var go = new GameObject("HeldCell", typeof(RectTransform));
            go.transform.SetParent(reel.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            float size = Mathf.Max(8f, reel.cellSize - 8f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = CellPosition(reel, row);

            var back = go.AddComponent<Image>();
            back.sprite = UiFactory.Solid;
            back.color = UiFactory.VelvetRaised;
            back.raycastTarget = false;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = UiFactory.GoldPrimary;
            outline.effectDistance = new Vector2(4f, -4f);

            var symbol = _definition.SymbolById(symbolId);
            if (symbol != null && symbol.sprite != null)
            {
                var iconGo = new GameObject("Icon", typeof(RectTransform));
                iconGo.transform.SetParent(go.transform, false);
                var icon = iconGo.AddComponent<Image>();
                icon.sprite = symbol.sprite;
                icon.color = Color.white;
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                UiFactory.Stretch((RectTransform)iconGo.transform, size * 0.08f);
            }
            else
            {
                string glyph = symbol != null && !string.IsNullOrEmpty(symbol.symbolName)
                    ? char.ToUpperInvariant(symbol.symbolName[0]).ToString()
                    : "#" + symbolId;

                var text = UiFactory.NewText("Glyph", go.transform, glyph,
                    Mathf.RoundToInt(size * 0.42f), FontStyle.Bold, UiFactory.GoldPrimary);
                UiFactory.Stretch(text.rectTransform, size * 0.08f);
            }

            if (betMultiple > 0)
            {
                var value = UiFactory.NewText("Value", go.transform, "×" + betMultiple,
                    18, FontStyle.Bold, UiFactory.GoldPrimary, TextAnchor.LowerRight);
                UiFactory.Stretch(value.rectTransform, 6f);
            }

            _locks.Add(go);
        }

        /// <summary>Anchored position of one cell inside a reel column.</summary>
        private Vector2 CellPosition(ReelStripView reel, int row)
        {
            float pitch = reel.cellSize + reel.cellGap;
            float rowZero = (_definition.rowCount - 1) * 0.5f * pitch;
            return new Vector2(0f, rowZero - row * pitch);
        }

        /// <summary>
        /// Draws the win presentation: a highlight box over every symbol that contributed to
        /// a win, and a line connecting the matched symbols on each paying payline. Scatters
        /// that triggered a feature are highlighted as well, without a connecting line.
        /// </summary>
        public void ShowWinHighlights(SpinResult result)
        {
            ClearHighlights();
            if (_definition == null || result.grid == null) return;
            if (!result.IsWin && !result.triggeredFreeSpins) return;

            // Payline wins: walk the first `count` reels of each winning line.
            if (result.lineWins != null)
            {
                for (int w = 0; w < result.lineWins.Count; w++)
                {
                    var win = result.lineWins[w];
                    if (win.paylineIndex < 0 || win.paylineIndex >= _definition.paylines.Count) continue;
                    var line = _definition.paylines[win.paylineIndex];
                    if (line == null || line.rows == null) continue;

                    int count = Mathf.Clamp(win.count, 0, line.rows.Length);
                    for (int r = 0; r < count; r++)
                        HighlightCell(r, line.rows[r]);

                    DrawWinLine(win.paylineIndex, count);
                }
            }

            // Scatters: highlight every scatter on the grid, but only when they actually
            // triggered a feature, so a stray scatter on a losing spin is not marked as a win.
            if (result.triggeredFreeSpins)
            for (int r = 0; r < _reels.Count && r < result.grid.Length; r++)
            {
                for (int row = 0; row < _definition.rowCount && row < result.grid[r].Length; row++)
                {
                    var symbol = _definition.SymbolById(result.grid[r][row]);
                    if (symbol != null && symbol.isScatter) HighlightCell(r, row);
                }
            }
        }

        // ---- win lines ------------------------------------------------------

        private static readonly Color WinLineGlow = new Color(1f, 0.78f, 0.28f, 0.34f);
        private static readonly Color WinLineCore = new Color(1f, 0.97f, 0.84f, 0.96f);
        private const float WinLineGlowWidth = 24f;
        private const float WinLineCoreWidth = 7f;

        /// <summary>
        /// Connects the matched cells of one winning payline with a glowing line. The line is
        /// drawn in this view's space rather than inside a reel column so it can cross the
        /// gaps between reels; the outer reel mask still clips it to the machine window.
        /// </summary>
        private void DrawWinLine(int paylineIndex, int count)
        {
            if (_definition == null || _definition.paylines == null) return;
            if (paylineIndex < 0 || paylineIndex >= _definition.paylines.Count) return;

            var line = _definition.paylines[paylineIndex];
            if (line?.rows == null) return;

            int reels = Mathf.Min(count, Mathf.Min(line.rows.Length, _reels.Count));
            if (reels < 2) return;

            if (_winLinesRoot == null)
            {
                var rootGo = new GameObject("WinLines", typeof(RectTransform));
                rootGo.transform.SetParent(transform, false);
                _winLinesRoot = (RectTransform)rootGo.transform;
                _winLinesRoot.anchorMin = new Vector2(0.5f, 0.5f);
                _winLinesRoot.anchorMax = new Vector2(0.5f, 0.5f);
                _winLinesRoot.pivot = new Vector2(0.5f, 0.5f);
                _winLinesRoot.sizeDelta = Vector2.zero;
                _winLinesRoot.anchoredPosition = Vector2.zero;
                _winLinesRoot.SetAsLastSibling();
            }

            var lineGo = new GameObject("Line" + (_winLinesRoot.childCount + 1),
                typeof(RectTransform));
            lineGo.transform.SetParent(_winLinesRoot, false);
            var lineRect = (RectTransform)lineGo.transform;
            lineRect.anchorMin = new Vector2(0.5f, 0.5f);
            lineRect.anchorMax = new Vector2(0.5f, 0.5f);
            lineRect.pivot = new Vector2(0.5f, 0.5f);
            lineRect.sizeDelta = Vector2.zero;
            lineRect.anchoredPosition = Vector2.zero;

            var from = CellCentreInView(0, line.rows[0]);
            AddWinJoint(lineRect, from);

            for (int r = 1; r < reels; r++)
            {
                var to = CellCentreInView(r, line.rows[r]);
                AddWinSegment(lineRect, from, to);
                AddWinJoint(lineRect, to);
                from = to;
            }
        }

        private Vector2 CellCentreInView(int reelIndex, int row)
        {
            var reel = _reels[reelIndex];
            var reelRect = (RectTransform)reel.transform;
            return reelRect.anchoredPosition + CellPosition(reel, row);
        }

        private static void AddWinSegment(RectTransform parent, Vector2 from, Vector2 to)
        {
            var delta = to - from;
            float length = delta.magnitude;
            if (length <= 0.01f) return;

            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            var midpoint = (from + to) * 0.5f;

            // A wide translucent halo around a narrow bright core. The joint discs hide the
            // square ends of the rectangle and give every corner a rounded elbow.
            AddWinLinePart(parent, "Glow", midpoint, length, WinLineGlowWidth,
                angle, WinLineGlow, UiFactory.Solid);
            AddWinLinePart(parent, "Core", midpoint, length, WinLineCoreWidth,
                angle, WinLineCore, UiFactory.Solid);
        }

        private static void AddWinJoint(RectTransform parent, Vector2 position)
        {
            AddWinLinePart(parent, "GlowJoint", position, WinLineGlowWidth, WinLineGlowWidth,
                0f, WinLineGlow, UiFactory.Circle);
            AddWinLinePart(parent, "CoreJoint", position, WinLineCoreWidth, WinLineCoreWidth,
                0f, WinLineCore, UiFactory.Circle);
        }

        private static void AddWinLinePart(RectTransform parent, string name, Vector2 position,
                                           float width, float height, float angle, Color color,
                                           Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = position;
            rect.localEulerAngles = new Vector3(0f, 0f, angle);

            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
        }

        private void HighlightCell(int reelIndex, int row)
        {
            if (reelIndex < 0 || reelIndex >= _reels.Count) return;
            var reel = _reels[reelIndex];
            if (reel == null) return;

            var go = new GameObject("WinFrame", typeof(RectTransform));
            go.transform.SetParent(reel.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            // A shade larger than the cell. The symbol punches up past the cell while it
            // celebrates, and a frame at the cell's own size would end up behind the
            // symbol it is supposed to be framing.
            float frame = reel.cellSize * 1.1f;
            rt.sizeDelta = new Vector2(frame, frame);
            rt.anchoredPosition = CellPosition(reel, row);

            // A frame, not a fill. The old marker was a translucent gold square with an
            // outline, and it sat on top of the symbol — which hid the very thing the
            // win animation is for. A ring frames the symbol and leaves it visible.
            var image = go.AddComponent<Image>();
            image.sprite = UiChrome.Rim;
            image.type = Image.Type.Sliced;
            image.color = UiFactory.GoldPrimary;
            image.raycastTarget = false;

            _winFrames.Add(new WinFrame { go = go, reelIndex = reelIndex, row = row });

            // The symbol itself does the celebrating, if it has an animation to play.
            reel.AnimateWin(row);
        }

        /// <summary>
        /// Re-fits the win frames to the reels.
        ///
        /// The reels resize when the canvas finally resolves, and a frame sized against the
        /// old cell ends up two rows too big — a gold band across the reel instead of a
        /// frame around the symbol.
        /// </summary>
        private void LayoutWinFrames()
        {
            for (int i = 0; i < _winFrames.Count; i++)
            {
                var frame = _winFrames[i];
                if (frame.go == null) continue;
                if (frame.reelIndex < 0 || frame.reelIndex >= _reels.Count) continue;

                var reel = _reels[frame.reelIndex];
                if (reel == null) continue;

                var rt = (RectTransform)frame.go.transform;
                float size = reel.cellSize * 1.1f;
                rt.sizeDelta = new Vector2(size, size);
                rt.anchoredPosition = CellPosition(reel, frame.row);
            }
        }

        // ---- presentation ---------------------------------------------------

        /// <summary>Seats every reel on its stop with no animation.</summary>
        public void ShowStops(int[] stops)
        {
            if (stops == null) return;
            for (int r = 0; r < _reels.Count && r < stops.Length; r++)
                _reels[r].SnapTo(stops[r]);
        }

        /// <summary>Where the reels are resting right now, wrapped back into the strips.</summary>
        public int[] CurrentStops()
        {
            var stops = new int[_reels.Count];
            for (int r = 0; r < _reels.Count; r++) stops[r] = _reels[r].CurrentStop;
            return stops;
        }

        /// <summary>
        /// Spins to a set of stops and returns once every reel has landed. <paramref name="stops"/>
        /// comes from <see cref="SpinResult.stops"/>, so the reels settle on exactly the grid
        /// the engine evaluated.
        /// </summary>
        public IEnumerator PlaySpin(int[] stops, float durationScale = 1f)
        {
            if (stops == null || _reels.Count == 0) yield break;

            float total = Mathf.Max(0.1f, _definition.spinDurationSeconds * Mathf.Max(0.05f, durationScale));
            float lastStart = 0f;

            for (int r = 0; r < _reels.Count && r < stops.Length; r++)
            {
                float start = _reels.Count > 1 ? total * staggerFraction * r / (_reels.Count - 1) : 0f;
                if (start > lastStart)
                {
                    yield return new WaitForSeconds(start - lastStart);
                    lastStart = start;
                }

                _reels[r].BeginSpin(stops[r], Mathf.Max(0.05f, total - start));
            }

            float tail = total - lastStart;
            if (tail > 0f) yield return new WaitForSeconds(tail);

            if (playAnticipation && _definition.anticipationSeconds > 0f)
                yield return new WaitForSeconds(_definition.anticipationSeconds);
        }

        /// <summary>Cuts every reel short, seated on the nearest stop.</summary>
        public void StopSpin()
        {
            for (int r = 0; r < _reels.Count; r++) _reels[r].StopSpin();
        }

        // ---- introspection --------------------------------------------------

        /// <summary>
        /// The symbol painted in a grid cell, for tests and for asserting that what the player
        /// sees is what the maths scored.
        /// </summary>
        public int DisplayedSymbol(int reel, int row) =>
            reel >= 0 && reel < _reels.Count ? _reels[reel].DisplayedSymbol(row) : -1;
    }
}