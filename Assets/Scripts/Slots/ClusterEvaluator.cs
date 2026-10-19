using System.Collections.Generic;

namespace LustHouse.Slots
{
    /// <summary>
    /// <see cref="PayType.Cluster"/> — wins pay for groups of adjacent symbols. Winning
    /// cells clear, the grid refills, and the cascade repeats until a step pays nothing.
    ///
    /// Unlike <see cref="WaysEvaluator"/> this has no closed form: each refill is a fresh
    /// draw, so expected RTP cannot be enumerated and must be sampled. The RTP gate says
    /// so rather than pretending otherwise.
    /// </summary>
    public class ClusterEvaluator : IReelEvaluator
    {
        public SpinResult Evaluate(SlotMachineDefinition def, int[] stops, long bet)
        {
            var result = new SpinResult
            {
                stops = stops,
                bet = bet,
                grid = ReelEngine.BuildGrid(def, stops),
                lineWins = new List<LineWin>(),
                totalWin = 0
            };

            if (def.reelStrips == null || def.reelStrips.Count != def.reelCount) return result;

            long totalMultiples = 0;
            int cascadeSteps = 0;

            // Working copy: cascades destroy and refill cells.
            var grid = new int[result.grid.Length][];
            for (int r = 0; r < result.grid.Length; r++)
            {
                grid[r] = new int[result.grid[r].Length];
                System.Array.Copy(result.grid[r], grid[r], result.grid[r].Length);
            }

            var rng = result.rng.HasValue ? result.rng.Value : SeededRng.FromSeed(def.id.GetHashCode());
            int guard = 0;
            const int MaxSteps = 64;   // a pathological strip must not spin forever

            while (guard++ < MaxSteps)
            {
                var clusters = FindClusters(def, grid);
                if (clusters.Count == 0) break;

                cascadeSteps++;
                long stepMultiples = 0;

                foreach (var cluster in clusters)
                {
                    var symbol = def.SymbolById(cluster.symbolId);
                    if (symbol == null) continue;

                    long payout = symbol.PayoutForCluster(cluster.size, def.cascadeClusterSize);
                    if (payout <= 0) continue;

                    stepMultiples += payout;
                    result.clustersWon++;
                    result.lineWins.Add(new LineWin
                    {
                        paylineIndex = -1,
                        count = cluster.size,
                        symbolId = cluster.symbolId,
                        payout = payout
                    });
                }

                if (stepMultiples <= 0) break;

                totalMultiples += stepMultiples;

                // Safety clamp. A strip made of a single symbol refills to that symbol, so
                // every step clusters again and the cascade would run to MaxSteps and pay
                // dozens of times the max win. Real strips mix symbols; this stops a
                // degenerate one from paying an absurd amount rather than trusting that.
                if (totalMultiples >= def.maxWinMultiplier)
                {
                    totalMultiples = def.maxWinMultiplier;
                    break;
                }

                // Clear the winning cells, then refill from the top of each strip.
                foreach (var cluster in clusters)
                    foreach (var cell in cluster.cells)
                        grid[cell.reel][cell.row] = -1;

                Refill(def, grid, ref rng);
            }

            result.cascadeSteps = cascadeSteps;
            result.grid = grid;
            result.scatterCount = ReelEngine.CountScatters(def, grid);
            result.totalWin = totalMultiples * bet;
            return result;
        }

        private struct Cell
        {
            public int reel;
            public int row;
        }

        private struct Cluster
        {
            public int symbolId;
            public int size;
            public List<Cell> cells;
        }

        /// <summary>Flood fill over orthogonal neighbours. Scatters never join a cluster.</summary>
        private static List<Cluster> FindClusters(SlotMachineDefinition def, int[][] grid)
        {
            int minSize = MathfMax(3, def.cascadeClusterSize);
            var found = new List<Cluster>();
            var seen = new bool[grid.Length][];

            // Allocate every row before scanning: the flood fill visits neighbouring
            // reels, so a reel not yet reached in the scan loop would be a null array.
            for (int r = 0; r < grid.Length; r++)
                seen[r] = new bool[grid[r].Length];

            for (int r = 0; r < grid.Length; r++)
            {
                for (int row = 0; row < grid[r].Length; row++)
                {
                    if (seen[r][row]) continue;
                    int symbolId = grid[r][row];
                    if (symbolId < 0) continue;

                    var symbol = def.SymbolById(symbolId);
                    if (symbol == null || symbol.isScatter) { seen[r][row] = true; continue; }

                    var cells = new List<Cell>();
                    var queue = new Queue<Cell>();
                    queue.Enqueue(new Cell { reel = r, row = row });
                    seen[r][row] = true;

                    while (queue.Count > 0)
                    {
                        var cell = queue.Dequeue();
                        cells.Add(cell);

                        TryVisit(grid, seen, symbolId, cell.reel, cell.row - 1, queue);
                        TryVisit(grid, seen, symbolId, cell.reel, cell.row + 1, queue);
                        TryVisit(grid, seen, symbolId, cell.reel - 1, cell.row, queue);
                        TryVisit(grid, seen, symbolId, cell.reel + 1, cell.row, queue);
                    }

                    if (cells.Count >= minSize)
                        found.Add(new Cluster { symbolId = symbolId, size = cells.Count, cells = cells });
                }
            }

            return found;
        }

        private static void TryVisit(int[][] grid, bool[][] seen, int symbolId,
                                     int reel, int row, Queue<Cell> queue)
        {
            if (reel < 0 || reel >= grid.Length) return;
            if (row < 0 || row >= grid[reel].Length) return;
            if (seen[reel][row]) return;
            if (grid[reel][row] != symbolId) return;

            seen[reel][row] = true;
            queue.Enqueue(new Cell { reel = reel, row = row });
        }

        /// <summary>Drops new symbols into cleared cells from the top of each reel's strip.</summary>
        private static void Refill(SlotMachineDefinition def, int[][] grid, ref SeededRng rng)
        {
            for (int r = 0; r < grid.Length; r++)
            {
                var strip = def.reelStrips[r]?.symbols;
                if (strip == null || strip.Count == 0) continue;

                for (int row = grid[r].Length - 1; row >= 0; row--)
                {
                    if (grid[r][row] >= 0) continue;
                    grid[r][row] = strip[rng.NextInt(strip.Count)];
                }
            }
        }

        private static int MathfMax(int a, int b) => a > b ? a : b;
    }
}