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
    /// Symbols exist as ids with a paytable so <c>MathSimulator</c> can measure a real RTP.
    /// Sprites are optional: when the shared art is present under <see cref="ArtFolder"/>
    /// the generator wires it in, and a missing sprite still falls back to the reels'
    /// tinted placeholder tile.
    /// </summary>
    public static class ContentBootstrap
    {
        public const string SlotFolder = "Assets/Data/Slots";
        public const string CatalogPath = "Assets/Data/Catalog/GameCatalog.asset";
        public const string VelvetRoomPath = SlotFolder + "/Slot_VelvetRoom.asset";

        public const string ArtFolder = "Assets/Art/Slots";

        // The shared symbol set. Every game in the catalogue uses the same id for the
        // same symbol, so one art table serves all of them: the five card ranks are
        // cut as gold-framed glyphs, and the four character illustrations fill the
        // Wild, the Scatter and the two premium fruit symbols.
        //
        // These are symbol ids, not filenames. Keep this table identical to the sprite
        // references on the shipped assets -- a regenerated game that came back with
        // different art would look like a different game.
        public const string WildArt = ArtFolder + "/00036-3449701744.png";        // blueberry lingerie
        public const string ScatterArt = ArtFolder + "/00035-3449701744.png";     // blueberry dress
        public const string AceArt = ArtFolder + "/ace.png";
        public const string KingArt = ArtFolder + "/king.png";
        public const string QueenArt = ArtFolder + "/queen.png";
        public const string JackArt = ArtFolder + "/j.png";
        public const string TenArt = ArtFolder + "/10.png";
        public const string GrapeArt = ArtFolder + "/00040-2458849210.png";
        public const string KiwiArt = ArtFolder + "/00041-1051490492.png";

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
                    entryCostGems = 0,
                    unlockLevel = 1
                });
            }

            RegisterPlaceholderGames();
            StampReleaseDates();
            CreatePromoConfig();
            MigrateSlotContent();

            EditorUtility.SetDirty(_catalog);
            AssetDatabase.SaveAssets();
            LustLog.Info($"[LustHouse] Starter content ready ({_catalog.games.Count} game(s)).");
        }

        /// <summary>
        /// Brings pre-feature slot assets up to the current serialized shape: reel strips,
        /// hold/spin and free-spin symbols, and the shared art. Idempotent and
        /// non-destructive — existing sprites, paytables and strips are left alone; only
        /// missing data is filled in.
        ///
        /// The four placeholder assets predate the reel-strip serialization fix, so without
        /// this they fail <see cref="SlotMachineDefinition.IsValid"/> and cannot be opened.
        /// </summary>
        [MenuItem("LustHouse/Setup/Migrate slot content", priority = 41)]
        public static void MigrateSlotContent()
        {
            var migrated = new List<string>();

            foreach (var guid in AssetDatabase.FindAssets("t:SlotMachineDefinition", new[] { SlotFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<SlotMachineDefinition>(path);
                if (def == null) continue;

                if (MigrateDefinition(def))
                {
                    EditorUtility.SetDirty(def);
                    migrated.Add(def.id);
                }

                if (!def.IsValid(out var error))
                    LustLog.Error($"[LustHouse] {def.id} is still invalid after migration: {error}", def);
            }

            if (migrated.Count > 0) AssetDatabase.SaveAssets();

            LustLog.Info(migrated.Count == 0
                ? "[LustHouse] Slot content already migrated."
                : $"[LustHouse] Migrated {migrated.Count} slot definition(s): {string.Join(", ", migrated)}");
        }

        private static bool MigrateDefinition(SlotMachineDefinition def)
        {
            bool changed = false;

            if (def.symbols == null || def.symbols.Count == 0)
            {
                // No hold symbol: a game gets one only if it has a hold & spin feature,
                // and the feature block below adds it.
                def.symbols = BuildSymbols(includeHoldSymbol: false);
                changed = true;
            }

            // GetFeatures() gives the populated feature list precedence over the legacy
            // flags, so migration repairs exactly the features the engine will run.
            var activeFeatures = def.GetFeatures();
            bool hasHold = activeFeatures.Exists(f => f is HoldAndSpinFeature);
            bool hasFree = activeFeatures.Exists(f => f is FreeSpinsFeature);

            // Fill only missing sprites. A designer's explicit choice always wins, and a
            // missing art file is allowed to fall through to the placeholder glyph.
            for (int i = 0; i < def.symbols.Count; i++)
            {
                var symbol = def.symbols[i];
                if (symbol == null || symbol.sprite != null) continue;

                var art = ArtForSymbol(symbol.id);
                if (art == null) continue;

                symbol.sprite = art;
                changed = true;
            }

            // A hold & spin game needs a lockable symbol, and every reel needs a chance to
            // show one. Reuse the first existing special; otherwise add the shared Diamond.
            int holdSymbolId = -1;
            if (hasHold)
            {
                holdSymbolId = FirstSpecialSymbolId(def);
                if (holdSymbolId < 0)
                {
                    var diamond = def.SymbolById(17);
                    if (diamond == null)
                    {
                        diamond = new SymbolDefinition
                        {
                            id = 17,
                            symbolName = "Diamond",
                            isSpecial = true,
                            payoutsByMatch = new long[] { 1, 4, 10 },
                            holdSpinValue = 2
                        };
                        def.symbols.Add(diamond);
                    }
                    else
                    {
                        diamond.isSpecial = true;
                        if (diamond.holdSpinValue <= 0) diamond.holdSpinValue = 2;
                        if (diamond.payoutsByMatch == null || diamond.payoutsByMatch.Length == 0)
                            diamond.payoutsByMatch = new long[] { 1, 4, 10 };
                    }

                    holdSymbolId = 17;
                    changed = true;
                }
            }

            // Free spins need a scatter symbol, and every reel needs a chance to show one.
            int scatterSymbolId = -1;
            if (hasFree)
            {
                scatterSymbolId = FirstScatterSymbolId(def);
                if (scatterSymbolId < 0)
                {
                    var scatter = def.SymbolById(1);
                    if (scatter == null)
                    {
                        scatter = new SymbolDefinition
                        {
                            id = 1,
                            symbolName = "Scatter",
                            isScatter = true,
                            payoutsByMatch = new long[] { 0, 0, 0 }
                        };
                        def.symbols.Add(scatter);
                    }
                    else
                    {
                        scatter.isScatter = true;
                    }

                    scatterSymbolId = 1;
                    changed = true;
                }
            }

            if (def.reelStrips == null || def.reelStrips.Count != def.reelCount)
            {
                def.reelStrips = BuildReelStrips(def.reelCount, holdSymbolId, scatterSymbolId);
                changed = true;
            }
            else
            {
                if (hasHold && InjectSymbol(def.reelStrips, holdSymbolId, 5)) changed = true;
                if (hasFree && InjectSymbol(def.reelStrips, scatterSymbolId, 3)) changed = true;
            }

            return changed;
        }

        private static int FirstSpecialSymbolId(SlotMachineDefinition def)
        {
            for (int i = 0; i < def.symbols.Count; i++)
                if (def.symbols[i] != null && def.symbols[i].isSpecial)
                    return def.symbols[i].id;
            return -1;
        }

        private static int FirstScatterSymbolId(SlotMachineDefinition def)
        {
            for (int i = 0; i < def.symbols.Count; i++)
                if (def.symbols[i] != null && def.symbols[i].isScatter)
                    return def.symbols[i].id;
            return -1;
        }

        /// <summary>
        /// Replaces one stop per reel with the feature symbol, but only where the strip does
        /// not already carry it. One stop in sixteen is enough to trigger; it is sparse
        /// enough to remain a feature rather than the base game.
        /// </summary>
        private static bool InjectSymbol(List<ReelStrip> strips, int symbolId, int preferredIndex)
        {
            if (strips == null || symbolId < 0) return false;

            bool changed = false;
            for (int r = 0; r < strips.Count; r++)
            {
                var strip = strips[r];
                if (strip?.symbols == null || strip.symbols.Count < 2) continue;
                if (strip.symbols.Contains(symbolId)) continue;

                int index = Mathf.Clamp(preferredIndex, 0, strip.symbols.Count - 1);
                strip.symbols[index] = symbolId;
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// The lobby face of each title: the symbol whose artwork represents it. Every game
        /// shares one symbol set, so this is what tells two lobby tiles apart until they
        /// have real key art.
        /// </summary>
        private static int FaceSymbolFor(string id) => id switch
        {
            "velvet_room" => 16,          // the mint character
            "neon_nights" => 10,           // Ace
            "midnight_masquerade" => 11,  // King
            "diamond_hold" => 12,          // Queen
            "house_edge" => 13,            // Jack
            _ => 0
        };

        /// <summary>
        /// The art for one symbol id, shared by every game. Returns null when the art
        /// file is not in the project, which is allowed: the symbol then draws as a
        /// tinted placeholder tile instead of failing.
        /// </summary>
        private static Sprite ArtForSymbol(int id)
        {
            switch (id)
            {
                case 0: return AssetDatabase.LoadAssetAtPath<Sprite>(WildArt);
                case 1: return AssetDatabase.LoadAssetAtPath<Sprite>(ScatterArt);
                case 10: return AssetDatabase.LoadAssetAtPath<Sprite>(AceArt);
                case 11: return AssetDatabase.LoadAssetAtPath<Sprite>(KingArt);
                case 12: return AssetDatabase.LoadAssetAtPath<Sprite>(QueenArt);
                case 13: return AssetDatabase.LoadAssetAtPath<Sprite>(JackArt);
                case 14: return AssetDatabase.LoadAssetAtPath<Sprite>(TenArt);
                case 15: return AssetDatabase.LoadAssetAtPath<Sprite>(GrapeArt);
                case 16: return AssetDatabase.LoadAssetAtPath<Sprite>(KiwiArt);
                default: return null;
            }
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

            def.features = BuildFeatures(template);
            def.progressive = template == SlotTemplate.Progressive;
            def.progressiveSeed = 250000;

            def.symbols = BuildSymbols(includeHoldSymbol: template == SlotTemplate.HoldAndSpin);
            def.faceSymbolId = FaceSymbolFor(id);
            def.paylines = BuildPaylines(reels, rows);
            def.reelStrips = BuildReelStrips(reels,
                template == SlotTemplate.HoldAndSpin ? 17 : -1,
                template == SlotTemplate.Cascade ? -1 : 1);

            AssetDatabase.CreateAsset(def, path);
            AssetDatabase.SaveAssets();

            if (!def.IsValid(out var error))
                LustLog.Error($"[LustHouse] Generated {id} is invalid: {error}", def);

            return def;
        }

        /// <summary>The features a generated template gets. Data, not code (plan §3.2).</summary>
        private static List<SlotFeature> BuildFeatures(SlotTemplate template)
        {
            var features = new List<SlotFeature>();

            if (template != SlotTemplate.Cascade)
                features.Add(new FreeSpinsFeature
                {
                    id = "free_spins",
                    displayName = "Free Spins",
                    scatterCount = 3,
                    award = 10,
                    betMultiplier = 1
                });

            if (template == SlotTemplate.HoldAndSpin)
                features.Add(new HoldAndSpinFeature
                {
                    id = "hold_spin",
                    displayName = "Hold & Spin",
                    initialLocked = 3,
                    maxRespins = 10
                });

            return features;
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

        private static int UnlockLevelFor(string id)
        {
            switch (id)
            {
                case "velvet_room": return 1;
                case "neon_nights": return 3;
                case "diamond_hold": return 6;
                case "midnight_masquerade": return 9;
                case "house_edge": return 12;
                default: return 1;
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
                    entryCostGems = 0,
                    unlockLevel = UnlockLevelFor(spec.id)
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

            def.symbols = BuildSymbols(includeHoldSymbol: false);
            def.faceSymbolId = FaceSymbolFor(def.id);
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

        /// <summary>
        /// The shared symbol set. Every game gets the same ids for the same symbols so
        /// the catalogue reads as one set and one art table serves all of them.
        ///
        /// The hold &amp; spin Diamond is opt-in: a game with no hold &amp; spin feature has
        /// no symbol 17, and a paytable symbol that can never reach a reel is dead
        /// weight that still has to be maintained.
        /// </summary>
        /// <param name="includeHoldSymbol">Adds the id 17 Diamond hold &amp; spin symbol.</param>
        private static List<SymbolDefinition> BuildSymbols(bool includeHoldSymbol)
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
                    id = 15, symbolName = "Grape",
                    payoutsByMatch = new long[] { 1, 4, 12 }
                },
                new SymbolDefinition
                {
                    id = 16, symbolName = "Kiwi",
                    payoutsByMatch = new long[] { 20, 80, 250 }
                }
            };

            if (includeHoldSymbol)
            {
                symbols.Add(new SymbolDefinition
                {
                    // The hold & spin symbol. It pays like a low symbol so a strip position
                    // carrying it is not dead in the base game, and carries a prize when held.
                    id = 17, symbolName = "Diamond", isSpecial = true,
                    payoutsByMatch = new long[] { 1, 4, 10 },
                    holdSpinValue = 2
                });
            }

            // The shared art is optional. Ids without art keep sprite = null and draw as
            // tinted placeholder tiles, so content still works before art lands.
            for (int i = 0; i < symbols.Count; i++)
                symbols[i].sprite = ArtForSymbol(symbols[i].id);

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
        private static List<ReelStrip> BuildReelStrips(int reels, int holdSymbolId,
                                                       int scatterSymbolId)
        {
            var strips = new List<ReelStrip>();

            for (int r = 0; r < reels; r++)
            {
                var symbols = new List<int>();
                for (int i = 0; i < 16; i++)
                {
                    int symbol;
                    if (r == 0) symbol = 12 + (i % 4);              // Queen, Jack, Ten, Grape
                    else if (r == reels - 1) symbol = (i == 3 || i == 11) ? 16 : 11 + (i % 5);
                    else symbol = 11 + (i % 6);

                    symbols.Add(symbol);
                }

                // The Wild and the Ace are the two symbols a plain id cycle skips, and a
                // symbol that can never reach a reel is dead weight in the paytable -- a
                // Wild that never lands is a feature that does not exist. Both take a stop
                // on every reel.
                if (symbols.Count >= 2)
                {
                    symbols[0] = 0;   // Wild
                    symbols[1] = 10;  // Ace
                }

                // One feature symbol per reel. A 16-stop strip with a 3-row window sees it
                // often enough to be demonstrable, but rarely enough to remain a feature.
                // A feature with no trigger symbol on the strips is a dead feature.
                if (scatterSymbolId >= 0 && symbols.Count > 3) symbols[3] = scatterSymbolId;
                if (holdSymbolId >= 0 && symbols.Count > 5) symbols[5] = holdSymbolId;

                strips.Add(new ReelStrip { symbols = symbols });
            }

            return strips;
        }

        /// <summary>
        /// The reel strips for a definition, regenerated from the shared generator.
        ///
        /// The feature symbols are taken from the definition itself: a hold &amp; spin game
        /// puts its special on every reel, and a cascade game gets no scatter because it
        /// has no free spins to trigger.
        /// </summary>
        public static List<ReelStrip> BuildPlaceholderStrips(SlotMachineDefinition def)
        {
            if (def == null) return new List<ReelStrip>();

            int holdSymbolId = -1;
            for (int i = 0; i < def.symbols.Count; i++)
            {
                var s = def.symbols[i];
                if (s != null && s.isSpecial) { holdSymbolId = s.id; break; }
            }

            int scatterSymbolId = -1;
            if (def.template != SlotTemplate.Cascade)
            {
                for (int i = 0; i < def.symbols.Count; i++)
                {
                    var s = def.symbols[i];
                    if (s != null && s.isScatter) { scatterSymbolId = s.id; break; }
                }
            }

            return BuildReelStrips(def.reelCount, holdSymbolId, scatterSymbolId);
        }

        /// <summary>
        /// Per-reel strips. Reel 1 is the cheap strip (it is the one that decides the
        /// base symbol), reels 2 and 3 are richer — standard practice so the RTP lands
        /// where the paytable says it does.
        /// </summary>
        private static List<ReelStrip> BuildReelStrips()
        {
            ReelStrip Reel(params int[] ids) =>
                new ReelStrip { symbols = new List<int>(ids) };

            return new List<ReelStrip>
            {
                Reel(13, 14, 15, 15, 14, 13, 16, 15, 14, 13, 15, 14, 16, 13, 15, 14),
                Reel(12, 13, 14, 15, 12, 13, 16, 14, 13, 12, 16, 15, 14, 13, 12, 16),
                Reel(11, 12, 13, 14, 15, 10, 16, 12, 13, 16, 11, 14, 12, 16, 13, 10)
            };
        }
    }
}
#endif
