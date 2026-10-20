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
        private readonly List<GameObject> _highlights = new List<GameObject>();
        private readonly List<GameObject> _locks = new List<GameObject>();
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
            for (int i = 0; i < _highlights.Count; i++)
                if (_highlights[i] != null) SafeDestroy(_highlights[i]);
            _highlights.Clear();

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

            _lastWidth = size.x;
            _lastHeight = size.y;
        }

        // ---- win highlights -------------------------------------------------

        /// <summary>Removes any win highlight boxes drawn by a previous spin.</summary>
        public void ClearHighlights()
        {
            for (int i = 0; i < _highlights.Count; i++)
                if (_highlights[i] != null) SafeDestroy(_highlights[i]);
            _highlights.Clear();
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
        /// Draws a highlight box over every symbol that contributed to a win: the matched
        /// positions on each winning payline, plus any scatter that triggered a feature.
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

        private void HighlightCell(int reelIndex, int row)
        {
            if (reelIndex < 0 || reelIndex >= _reels.Count) return;
            var reel = _reels[reelIndex];
            if (reel == null) return;

            var go = new GameObject("WinHighlight", typeof(RectTransform));
            go.transform.SetParent(reel.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(reel.cellSize, reel.cellSize);
            rt.anchoredPosition = CellPosition(reel, row);

            var image = go.AddComponent<Image>();
            image.sprite = UiFactory.Solid;
            image.color = new Color32(0xE8, 0xC3, 0x6B, 0x55);
            image.raycastTarget = false;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = UiFactory.GoldPrimary;
            outline.effectDistance = new Vector2(3f, -3f);

            _highlights.Add(go);
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