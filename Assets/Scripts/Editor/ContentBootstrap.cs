#if UNITY_EDITOR
using System.Collections.Generic;
using LustHouse.Catalog;
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
                    blurb = "The house tutorial game. Art deco, suits, gold filigree.",
                    locked = false,
                    entryCostGems = 0
                });
                EditorUtility.SetDirty(_catalog);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[LustHouse] Starter content ready ({(velvet != null ? "Velvet Room" : "existing assets reused")}).");
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