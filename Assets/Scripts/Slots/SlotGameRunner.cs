using UnityEngine;
using UnityEngine.SceneManagement;
using LustHouse.Core;

namespace LustHouse.Slots
{
    public class SlotGameRunner : MonoBehaviour
    {
        public SlotMachineDefinition definition;
        public ReelEngine reelEngine;

        private bool _launching;

        public void OnSpinPressed(long bet)
        {
            if (_launching) return;
            if (WalletService.Instance == null) return;
            if (!WalletService.Instance.TryDebit(Currency.Coins, bet, LedgerReason.Spin, definition.id))
                return; // insufficient funds -> UI handles top-up prompt

            _launching = true;
            var result = reelEngine.Spin(definition);
            _launching = false;

            if (result.totalWin > 0)
                WalletService.Instance.Grant(Currency.Coins, result.totalWin, LedgerReason.Spin, definition.id);
        }
    }
}