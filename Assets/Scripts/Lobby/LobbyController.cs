using UnityEngine;
using LustHouse.Core;

namespace LustHouse.Lobby
{
    public class LobbyController : MonoBehaviour
    {
        public void OnGameTilePressed(string gameId)
        {
            // Routing handled by a single dispatcher; see plan §2.8.
        }

        public void OnCurrencyPlusPressed(Currency c)
        {
            // Opens the top-up sheet for c. Never opens IAP from a loss flash.
        }
    }
}