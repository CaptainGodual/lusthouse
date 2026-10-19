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
            [Tooltip("Gems required to enter. 0 = enter with Coins. Never purchasable with Coins.")]
            public int entryCostGems = 0;

            public string Title
            {
                get
                {
                    if (!string.IsNullOrEmpty(displayTitle)) return displayTitle;
                    if (definition != null && !string.IsNullOrEmpty(definition.title)) return definition.title;
                    return id;
                }
            }
        }

        public List<Entry> games = new List<Entry>();

        public Entry Find(string id)
        {
            for (int i = 0; i < games.Count; i++)
                if (games[i] != null && games[i].id == id) return games[i];
            return null;
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