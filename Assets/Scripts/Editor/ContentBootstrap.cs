#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using LustHouse.Catalog;
using LustHouse.Core;
using LustHouse.Slots;
using UnityEditor;
using UnityEngine;

namespace LustHouse.EditorTools
{
    /// <summary>
    /// Generates the first playable content assets (plan §0.4 P5 / §3.4 game #1).
    ///
    /// Art is deliberately absent — this is a maths-and-loop asset. Symbols exist as ids
    /// with a paytable so <c>MathSimulator</c> can measure a real RTP; sprites get filled
    /// in when the art pipeline lands.
    /// </summary>
    public static class ContentBootstrap
    {
        public const string SlotFolder = "Assets/Data/Slots";
        public const string CatalogPath = "Assets/Data/Catalog/GameCatalog.asset";
        public const string VelvetRoomPath = SlotFolder + "/Slot_VelvetRoom.asset";

        private static GameCatalog _catalog;

        public static GameCatalog Catalog
        {
            get
            {
                if (_catalog == null) _catalog = AssetDatabase.LoadAssetAtPath<GameCatalog>(CatalogPath);
                return _catalog;
            }
        }

        [MenuItem("LustHouse/Setup/Create starter content", priority = 40)]
        public static void CreateStarterContent()
        {
            var velvet = CreateVelvetRoom();
            CreatePlaceholderGames();

            if (_catalog == null)
            {
                _catalog = AssetDatabase.LoadAssetAtPath<GameCatalog>(CatalogPath);
                if (_catalog == null)
                {
                    _catalog = ScriptableObject.CreateInstance<GameCatalog>();
                    AssetDatabase.CreateAsset(_catalog, CatalogPath);
                }
            }

            if (velvet != null && _catalog.Find("velvet_room") == null)
            {
                _catalog.games.Add(new GameCatalog.Entry
                {
                    id = "velvet_room",
                    definition = velvet,
                    category = GameCategory.Slots,
                    displayTitle = "Velvet Room",
                    blurb = "The house tutorial game. Art deco, suits, gold filigree.",
                    locked = false,
                    entryCostGems = 0
                });
            }

            RegisterPlaceholderGames();
            StampReleaseDates();
            CreatePromoConfig();

            EditorUtility.SetDirty(_catalog);
            AssetDatabase.SaveAssets();
            LustLog.Info($"[LustHouse] Starter content ready ({_catalog.games.Count} game(s)).");
        }

        /// <summary>
        /// Stamps a release date on each catalog entry so the "New" window and the NEW badge
        /// have something to work with. Existing dates are left alone.
        /// </summary>
        private static void StampReleaseDates()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<GameCatalog>(CatalogPath);
            if (catalog == null) return;

            // Descending: the last-launched game is the newest.
            var now = DateTime.UtcNow;
            bool changed = false;

            for (int i = 0; i < catalog.games.Count; i++)
            {
                var entry = catalog.games[i];
                if (entry == null || !string.IsNullOrEmpty(entry.releasedAtUtc)) continue;

                entry.releasedAtUtc = now.AddDays(-i * 5).ToString("o");
                changed = true;
            }

            if (changed) EditorUtility.SetDirty(catalog);
        }

        /// <summary>
        /// Creates the lobby promo config if absent: one daily-bonus slide plus featured
        /// entries. The Newest and Jackpot slides are derived from the catalog at runtime,
        /// so new games appear without touching this asset.
        /// </summary>
        public const string PromoConfigPath = "Assets/Data/Economy/LobbyPromoConfig.asset";

        public static LobbyPromoConfig CreatePromoConfig()
        {
            var existing = AssetDatabase.LoadAssetAtPath<LobbyPromoConfig>(PromoConfigPath);
            if (existing != null) return existing;

            var config = LobbyPromoConfig.CreateDefault();
            AssetDatabase.CreateAsset(config, PromoConfigPath);

            var catalog = AssetDatabase.LoadAssetAtPath<GameCatalog>(CatalogPath);
            if (catalog != null)
            {
                int order = 0;
                foreach (var entry in catalog.games)
                {
                    if (entry == null) continue;
                    config.featured.Add(new FeaturedTile
                    {
                        gameId = entry.id,
                        headline = entry.Title.ToUpperInvariant(),
                        order = order++
                    });
                }
            }

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            LustLog.Info($"[LustHouse] Created {PromoConfigPath}");
            return config;
        }

        /// <summary>
        /// One placeholder definition per remaining mechanic template (B, C, D, E) from
        /// plan §3.2, so the lobby grid and category tabs have real content and the
        /// simulator has models to exercise.
        ///
        /// These are PLACEHOLDERS. Their RTP targets are plan band midpoints and have not
        /// been tuned or verified — each must pass a 100k-spin simulation within ±0.5%
        /// (plan §3.6) before it could ship. Do not read the target as an achieved value.
        /// </summary>
        private static void CreatePlaceholderGames()
        {
            Create("Neon Nights", "neon_nights", SlotTemplate.Video5, Volatility.Medium, 0.955f, 2500, 5, 3, 20);
            Create("Midnight Masquerade", "midnight_masquerade", SlotTemplate.Cascade, Volatility.High, 0.945f, 10000, 5, 3, 20);
            Create("Diamond Hold", "diamond_hold", SlotTemplate.HoldAndSpin, Volatility.Medium, 0.955f, 2500, 5, 3, 20);
            Create("House Edge", "house_edge", SlotTemplate.Progressive, Volatility.High, 0.945f, 10000, 5, 3, 20);
        }

        private static SlotMachineDefinition Create(string title, string id, SlotTemplate template,
            Volatility volatility, float targetRtp, int maxWin, int reels, int rows, int paylines)
        {
            string path = $"{SlotFolder}/Slot_{id}.asset";
            if (AssetDatabase.LoadAssetAtPath<SlotMachineDefinition>(path) != null) return null;

            var def = ScriptableObject.CreateInstance<SlotMachineDefinition>();
            def.id = id;
            def.title = title;
            def.template = template;
            def.volatility = volatility;
            def.themeTint = PlaceholderTint(id);

            def.reelCount = reels;
            def.rowCount = rows;
            def.payType = PayType.Lines;

            def.minBet = 100;
            def.maxBet = 5000;
            def.betLevels = 6;
            def.targetRtp = targetRtp;
            def.maxWinMultiplier = maxWin;

            def.hasFreeSpins = template != SlotTemplate.Cascade;
            def.hasCascade = template == SlotTemplate.Cascade;
            def.hasHoldAndSpin = template == SlotTemplate.HoldAndSpin;
            def.progressive = template == SlotTemplate.Progressive;
            def.progressiveSeed = 250000;

            def.symbols = BuildSymbols();
            def.paylines = BuildPaylines(reels, rows);
            def.reelStrips = BuildReelStrips(reels);

            AssetDatabase.CreateAsset(def, path);
            AssetDatabase.SaveAssets();

            if (!def.IsValid(out var error))
                LustLog.Error($"[LustHouse] Generated {id} is invalid: {error}", def);

            return def;
        }

        private static Color PlaceholderTint(string id)
        {
            switch (id)
            {
                case "neon_nights": return new Color32(0x37, 0xE0, 0xD8, 0xFF);
                case "midnight_masquerade": return new Color32(0x8B, 0x3A, 0xF0, 0xFF);
                case "diamond_hold": return new Color32(0x4A, 0x6C, 0xFF, 0xFF);
                default: return new Color32(0xFF, 0x3D, 0x8B, 0xFF);
            }
        }

        private static void RegisterPlaceholderGames()
        {
            var specs = new (string id, string title, GameCategory category)[]
            {
                ("neon_nights", "Neon Nights", GameCategory.Slots),
                ("midnight_masquerade", "Midnight Masquerade", GameCategory.Slots),
                ("diamond_hold", "Diamond Hold", GameCategory.Slots),
                ("house_edge", "House Edge", GameCategory.Jackpots)
            };

            foreach (var spec in specs)
            {
                if (_catalog.Find(spec.id) != null) continue;

                var def = AssetDatabase.LoadAssetAtPath<SlotMachineDefinition>($"{SlotFolder}/Slot_{spec.id}.asset");
                if (def == null) continue;

                _catalog.games.Add(new GameCatalog.Entry
                {
                    id = spec.id,
                    definition = def,
                    category = spec.category,
                    displayTitle = spec.title,
                    locked = false,
                    entryCostGems = 0
                });
            }
        }

        /// <summary>
        /// "Velvet Room" — template A, 3 reels, low volatility, 9 paylines. This is the
        /// onboarding game from plan §3.4 and the first proof of the reel engine.
        /// </summary>
        public static SlotMachineDefinition CreateVelvetRoom()
        {
            var existing = AssetDatabase.LoadAssetAtPath<SlotMachineDefinition>(VelvetRoomPath);
            if (existing != null) return existing;

            var def = ScriptableObject.CreateInstance<SlotMachineDefinition>();
            def.id = "velvet_room";
            def.title = "Velvet Room";
            def.tagline = "Where the house teaches.";
            def.template = SlotTemplate.Classic3;
            def.volatility = Volatility.Low;
            def.themeTint = new Color32(0xE8, 0xC3, 0x6B, 0xFF);

            def.reelCount = 3;
            def.rowCount = 3;
            def.payType = PayType.Lines;

            def.minBet = 100;
            def.maxBet = 5000;
            def.betLevels = 6;
            def.targetRtp = 0.962f;   // low band from plan §3.3
            def.maxWinMultiplier = 500; // low band from plan §3.3

            def.spinDurationSeconds = 1.4f;

            def.symbols = BuildSymbols();
            def.paylines = BuildPaylines();
            def.reelStrips = BuildReelStrips();

            AssetDatabase.CreateAsset(def, VelvetRoomPath);
            AssetDatabase.SaveAssets();

            if (!def.IsValid(out var error))
                Debug.LogError($"[LustHouse] Generated Velvet Room is invalid: {error}", def);
            else
                Debug.Log($"[LustHouse] Created {VelvetRoomPath}");

            return def;
        }

        private static List<SymbolDefinition> BuildSymbols()
        {
            // Ids are stable save-data keys. Payouts are in bet-multiples and index 0 == 3 of a kind.
            var symbols = new List<SymbolDefinition>
            {
                new SymbolDefinition
                {
                    id = 0, symbolName = "Wild", isWild = true,
                    payoutsByMatch = new long[] { 0, 0, 0, 0 } // wins as the substituted symbol
                },
                new SymbolDefinition
                {
                    id = 1, symbolName = "Scatter", isScatter = true,
                    payoutsByMatch = new long[] { 0, 0, 0 }
                },
                new SymbolDefinition
                {
                    id = 10, symbolName = "Ace",
                    payoutsByMatch = new long[] { 5, 20, 60 }
                },
                new SymbolDefinition
                {
                    id = 11, symbolName = "King",
                    payoutsByMatch = new long[] { 4, 15, 50 }
                },
                new SymbolDefinition
                {
                    id = 12, symbolName = "Queen",
                    payoutsByMatch = new long[] { 3, 12, 40 }
                },
                new SymbolDefinition
                {
                    id = 13, symbolName = "Jack",
                    payoutsByMatch = new long[] { 2, 8, 25 }
                },
                new SymbolDefinition
                {
                    id = 14, symbolName = "Ten",
                    payoutsByMatch = new long[] { 1, 5, 15 }
                },
                new SymbolDefinition
                {
                    id = 15, symbolName = "Nine",
                    payoutsByMatch = new long[] { 1, 4, 12 }
                },
                new SymbolDefinition
                {
                    id = 16, symbolName = "Filigree",
                    payoutsByMatch = new long[] { 20, 80, 250 }
                }
            };

            return symbols;
        }

        /// <summary>The classic nine-line set on a 3x3 grid.</summary>
        private static List<PaylineDefinition> BuildPaylines()
        {
            return new List<PaylineDefinition>
            {
                Line("Middle",            1, 1, 1),
                Line("Top",               0, 0, 0),
                Line("Bottom",            2, 2, 2),
                Line("V1",                0, 1, 2),
                Line("V2",                2, 1, 0),
                Line("V3",                1, 0, 1),
                Line("V4",                1, 2, 1),
                Line("Diagonal A",        0, 1, 2),
                Line("Diagonal B",        2, 1, 0)
            };
        }

        private static PaylineDefinition Line(string name, int r0, int r1, int r2) =>
            new PaylineDefinition { lineName = name, rows = new[] { r0, r1, r2 } };

        /// <summary>
        /// Builds a generic set of paylines covering horizontal, V, and diagonal shapes for
        /// any reel count. Placeholder geometry only — real games get hand-authored lines.
        /// </summary>
        private static List<PaylineDefinition> BuildPaylines(int reels, int rows)
        {
            var lines = new List<PaylineDefinition>();

            for (int row = 0; row < rows; row++)
            {
                var indices = new int[reels];
                for (int r = 0; r < reels; r++) indices[r] = row;
                lines.Add(new PaylineDefinition { lineName = $"H{row}", rows = indices });
            }

            // Zig-zag down then back up, and the reverse.
            var down = new int[reels];
            for (int r = 0; r < reels; r++) down[r] = r % rows;
            lines.Add(new PaylineDefinition { lineName = "V-down", rows = down });

            var up = new int[reels];
            for (int r = 0; r < reels; r++) up[r] = (reels - 1 - r) % rows;
            lines.Add(new PaylineDefinition { lineName = "V-up", rows = up });

            return lines;
        }

        /// <summary>
        /// Generic per-reel strips. Reel 0 is the cheapest so it decides the base symbol
        /// more often; the last reel carries the premium symbol sparingly.
        /// </summary>
        private static int[][] BuildReelStrips(int reels)
        {
            var strips = new int[reels][];

            for (int r = 0; r < reels; r++)
            {
                var strip = new List<int>();
                for (int i = 0; i < 16; i++)
                {
                    int symbol;
                    if (r == 0) symbol = 12 + (i % 4);              // J, 10, 9, Filigree-lite
                    else if (r == reels - 1) symbol = (i == 3 || i == 11) ? 16 : 11 + (i % 5);
                    else symbol = 11 + (i % 6);

                    strip.Add(symbol);
                }
                strips[r] = strip.ToArray();
            }

            return strips;
        }

        /// <summary>
        /// Per-reel strips. Reel 1 is the cheap strip (it is the one that decides the
        /// base symbol), reels 2 and 3 are richer — standard practice so the RTP lands
        /// where the paytable says it does.
        /// </summary>
        private static int[][] BuildReelStrips()
        {
            return new int[][]
            {
                new[] { 13, 14, 15, 15, 14, 13, 16, 15, 14, 13, 15, 14, 16, 13, 15, 14 },
                new[] { 12, 13, 14, 15, 12, 13, 16, 14, 13, 12, 16, 15, 14, 13, 12, 16 },
                new[] { 11, 12, 13, 14, 15, 10, 16, 12, 13, 16, 11, 14, 12, 16, 13, 10 }
            };
        }
    }
}
#endif
