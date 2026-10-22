using System;
using System.Collections.Generic;

namespace LustHouse.Slots
{
    /// <summary>
    /// Template A/B base evaluator: counts paying paylines with wild substitution.
    ///
    /// This is the strategy behind <see cref="PayType.Lines"/>. The other templates
    /// delegate here and layer their feature handling on top, so there is exactly one
    /// payout code path (plan §5.2).
    /// </summary>
    public class PaylineEvaluator : IReelEvaluator
    {
        public virtual SpinResult Evaluate(SlotMachineDefinition def, int[] stops, long bet)
        {
            var result = new SpinResult
            {
                stops = stops,
                bet = bet,
                grid = ReelEngine.BuildGrid(def, stops),
                lineWins = new List<LineWin>(),
                totalWin = 0
            };

            var grid = result.grid;
            long totalMultiples = 0;
            result.blockWins = new List<BlockWin>();
            long blockWin = 0;

            if (def.payType == PayType.Lines)
            {
                for (int i = 0; i < def.paylines.Count; i++)
                {
                    var line = def.paylines[i];
                    long win = EvaluateLine(def, grid, line, out int symbolId, out int count);
                    if (win <= 0) continue;

                    totalMultiples += win;
                    result.lineWins.Add(new LineWin
                    {
                        paylineIndex = i,
                        count = count,
                        symbolId = symbolId,
                        payout = win
                    });
                }
            }

            blockWin = EvaluateBlocks(def, grid, bet, result.blockWins);

            result.scatterCount = ReelEngine.CountScatters(def, grid);
            // Features are not scored here: FeatureRunner runs them after the base game so a
            // free-spin round is actually played rather than folded into one lump.
            result.totalWin = totalMultiples * bet + blockWin;
            return result;
        }

        /// <summary>
        /// Scans every 2x2 window on the grid for a mega block and pays the ones that land.
        ///
        /// The match is exact: all four cells must be the same symbol id, and that symbol
        /// must have opted in with <c>megaBlockMultiplier &gt; 0</c>. A wild does not
        /// substitute — a block is a shape, not a line, and "any four symbols" would turn
        /// every wild-heavy spin into a block. Overlapping windows each pay on their own
        /// (a 2x3 slab of mangoes is two blocks), which is what the closed-form term in
        /// MathSimulator.BlockRtp assumes.
        ///
        /// Returns the total in currency units, rounding each block once so a fractional
        /// bet-multiple cannot leak a sub-unit win into the total.
        /// </summary>
        public static long EvaluateBlocks(SlotMachineDefinition def, int[][] grid, long bet,
                                          List<BlockWin> into)
        {
            if (def == null || grid == null || into == null) return 0L;

            long total = 0L;
            for (int r = 0; r + 1 < grid.Length; r++)
            {
                for (int row = 0; row + 1 < grid[r].Length; row++)
                {
                    int id = grid[r][row];
                    if (grid[r][row + 1] != id) continue;
                    if (grid[r + 1][row] != id) continue;
                    if (grid[r + 1][row + 1] != id) continue;

                    var symbol = def.SymbolById(id);
                    if (symbol == null) continue;

                    double exact = symbol.MegaBlockPayoutExact();
                    if (exact <= 0.0) continue;

                    long pay = (long)Math.Round(exact * bet, MidpointRounding.AwayFromZero);
                    if (pay <= 0) continue;

                    into.Add(new BlockWin { reel = r, row = row, symbolId = id, payout = pay });
                    total += pay;
                }
            }

            return total;
        }

        /// <summary>
        /// Walks a payline left to right. A wild substitutes for any non-scatter symbol;
        /// the line wins on the longest prefix of matching symbols, minimum three.
        /// </summary>
        public static long EvaluateLine(SlotMachineDefinition def, int[][] grid, PaylineDefinition line,
                                        out int symbolId, out int count)
        {
            symbolId = -1;
            count = 0;
            if (line?.rows == null) return 0;

            int reelCount = line.rows.Length;

            // The base symbol is the first non-wild, non-scatter symbol on the line.
            // A wild on reel 0 must not invalidate the whole line -- it substitutes.
            int baseSymbol = -1;
            for (int r = 0; r < reelCount; r++)
            {
                int id = grid[r][line.rows[r]];
                var d = def.SymbolById(id);
                if (d == null) break;
                if (d.isScatter) return 0;   // scatters never pay on a line
                if (d.isWild) continue;
                baseSymbol = id;
                break;
            }

            // All wilds: pay as the wild itself.
            if (baseSymbol == -1)
            {
                int w = grid[0][line.rows[0]];
                var wd = def.SymbolById(w);
                if (wd == null || !wd.isWild) return 0;
                baseSymbol = w;
            }

            var baseDef = def.SymbolById(baseSymbol);
            if (baseDef == null) return 0;

            bool sawWild = false;
            for (int r = 0; r < reelCount; r++)
            {
                int id = grid[r][line.rows[r]];
                var d = def.SymbolById(id);
                if (d != null && d.isWild) sawWild = true;
            }

            var reel0Def = def.SymbolById(grid[0][line.rows[0]]);
            if (reel0Def != null && reel0Def.isScatter) return 0;

            int matched = 0;
            for (int r = 0; r < reelCount; r++)
            {
                int symbol = grid[r][line.rows[r]];
                var def2 = def.SymbolById(symbol);
                if (def2 == null) break;

                if (symbol == baseSymbol || def2.isWild) { matched++; continue; }
                break;
            }

            if (matched == 0) return 0;

            // Report the run length even when the line does not pay, so a near miss is
            // distinguishable from no match at all. Harmless to payout maths: callers
            // gate on win > 0, and symbolId stays -1 when nothing pays.
            count = matched;
            if (matched < 3) return 0;

            // A wild pays as the symbol it stands in for -- the first real symbol on the
            // line -- not as the most expensive symbol in the game. Paying the priciest
            // symbol turned every wild into a Filigree, which put the RTP floor for this
            // paytable at 579% and made the declared target unreachable at all. An
            // all-wild line falls back to the Wild's own payoutsByMatch.
            symbolId = sawWild ? SubstituteSymbol(def, grid, line) : baseSymbol;

            var payDef = def.SymbolById(symbolId);
            if (payDef == null) return 0;

            return payDef.PayoutForMatch(matched);
        }

        /// <summary>
        /// The symbol a wild-assisted line pays as: the first non-wild, non-scatter symbol
        /// the wild covers, or the wild itself when the line is entirely wilds.
        /// </summary>
        private static int SubstituteSymbol(SlotMachineDefinition def, int[][] grid,
                                            PaylineDefinition line)
        {
            for (int r = 0; r < line.rows.Length; r++)
            {
                int id = grid[r][line.rows[r]];
                var symbol = def.SymbolById(id);
                if (symbol == null || symbol.isWild || symbol.isScatter) continue;
                return id;
            }

            for (int i = 0; i < def.symbols.Count; i++)
                if (def.symbols[i] != null && def.symbols[i].isWild)
                    return def.symbols[i].id;

            return -1;
        }
    }

    // Templates C/D/E used to have stub evaluator subclasses here. They are gone: the base game
    // is chosen by payType alone, and each template's mechanic is a SlotFeatureHandler run by
    // FeatureRunner (cluster pays is PayType.Cluster's ClusterEvaluator).
}