using System;
using System.Collections.Generic;
using UnityEngine;

namespace LustHouse.Catalog
{
    public enum GameCategory { All, Slots, Jackpots, New, Favorites, HighVolatility }

    /// <summary>
    /// The library the lobby grid renders (plan §2.8). Data bound to a
    /// ScriptableObject so adding a game requires no code.
    /// </summary>
    [CreateAssetMenu(menuName = "LustHouse/Game Catalog", fileName = "GameCatalog")]
    public class GameCatalog : ScriptableObject
    {
        [System.Serializable]
        public class Entry
        {
            public string id = "";

            [Tooltip("Overrides the definition's title when set.")]
            public string displayTitle = "";

            public Slots.SlotMachineDefinition definition;
            public GameCategory category = GameCategory.Slots;
            public Sprite keyArt;
            [TextArea(1, 2)]
            public string blurb = "";
            public bool locked = false;
            public string lockedMessage = "";

            [Tooltip("Minimum player level needed to enter. 1 = available immediately.")]
            public int unlockLevel = 1;

            [Tooltip("ISO-8601 release date. Drives the 'New' tab and the NEW badge " +
                     "(plan §2.7). Blank means not released / never 'New'.")]
            public string releasedAtUtc = "";

            [Tooltip("Fallback for the featured row when the promo config does not list " +
                     "this game. Order still comes from the config.")]
            public bool isFeatured = false;

            public int featuredOrder = 0;

            [Tooltip("Gems required to enter. 0 = enter with Coins. Never purchasable with Coins.")]
            public int entryCostGems = 0;

            /// <summary>Parsed release date, or default when unset or unparseable.</summary>
            public DateTime ReleasedUtc =>
                DateTime.TryParse(releasedAtUtc, System.Globalization.CultureInfo.InvariantCulture,
                                  System.Globalization.DateTimeStyles.AdjustToUniversal |
                                  System.Globalization.DateTimeStyles.AssumeUniversal,
                                  out var parsed)
                    ? parsed
                    : default;

            public string Title
            {
                get
                {
                    if (!string.IsNullOrEmpty(displayTitle)) return displayTitle;
                    if (definition != null && !string.IsNullOrEmpty(definition.title)) return definition.title;
                    return id;
                }
            }

            /// <summary>
            /// The game's face: the artwork of the symbol the lobby shows for it.
            ///
            /// Key art is a per-title asset and most titles do not have one yet, so the
            /// lobby falls back to a symbol from the definition. The face symbol is what
            /// tells two lobby tiles apart — every game shares one symbol set, so without
            /// it the shelf is five copies of the same picture.
            /// </summary>
            public Sprite FaceArt()
            {
                if (definition == null || definition.symbols == null) return keyArt;

                if (definition.faceSymbolId != 0)
                {
                    var face = definition.SymbolById(definition.faceSymbolId);
                    if (face != null && face.sprite != null) return face.sprite;
                }

                Sprite best = null;
                long bestPay = -1;

                foreach (var symbol in definition.symbols)
                {
                    if (symbol == null || symbol.sprite == null) continue;

                    long pay = 0;
                    if (symbol.payoutsByMatch != null && symbol.payoutsByMatch.Length > 0)
                        pay = symbol.payoutsByMatch[symbol.payoutsByMatch.Length - 1];

                    if (pay > bestPay)
                    {
                        bestPay = pay;
                        best = symbol.sprite;
                    }
                }

                return best != null ? best : keyArt;
            }
        }

        public List<Entry> games = new List<Entry>();

        public Entry Find(string id)
        {
            for (int i = 0; i < games.Count; i++)
                if (games[i] != null && games[i].id == id) return games[i];
            return null;
        }

        public static bool IsUnlocked(Entry entry, int playerLevel)
        {
            if (entry == null) return false;
            if (entry.locked) return false;
            return playerLevel >= Mathf.Max(1, entry.unlockLevel);
        }

        public List<Entry> UnlockedGames(int playerLevel, GameCategory category = GameCategory.All)
        {
            var result = new List<Entry>();
            foreach (var entry in games)
            {
                if (entry == null) continue;
                if (!IsUnlocked(entry, playerLevel)) continue;
                if (category != GameCategory.All && entry.category != category) continue;
                result.Add(entry);
            }
            return result;
        }

        public int CountOf(GameCategory category)
        {
            if (category == GameCategory.All) return games.Count;
            int n = 0;
            for (int i = 0; i < games.Count; i++)
                if (games[i] != null && games[i].category == category) n++;
            return n;
        }
    }
}