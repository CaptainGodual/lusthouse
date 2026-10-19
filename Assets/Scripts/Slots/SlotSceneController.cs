using LustHouse.Core;
using UnityEngine;

namespace LustHouse.Slots
{
    /// <summary>
    /// Lives in the single shared <c>Slot.unity</c> scene. Reads the definition handed over
    /// by the lobby and configures the runner — no per-game scene, no per-game code.
    /// </summary>
    [DisallowMultipleComponent]
    public class SlotSceneController : MonoBehaviour
    {
        [Header("Wiring")]
        public SlotGameRunner runner;
        public ReelEngine reelEngine;

        [Header("RNG (guardrail §6)")]
        [Tooltip("0 = seed from the clock at boot. QA can pin a seed to reproduce a session exactly.")]
        public int sessionSeed = 0;

        private SlotMachineDefinition _definition;

        public SlotMachineDefinition Definition => _definition;

        private void Start()
        {
            EnsurePresenter();

            _definition = SlotLaunchPayload.Payload;
            SlotLaunchPayload.Payload = null;

            if (_definition == null)
            {
                LustLog.Warn("[LustHouse] Slot scene entered with no definition; returning to lobby.");
                UnityEngine.SceneManagement.SceneManager.LoadScene("Lobby");
                return;
            }

            if (!_definition.IsValid(out var error))
                LustLog.Error($"[LustHouse] Invalid definition '{_definition.id}': {error}", _definition);

            if (reelEngine != null) reelEngine.Seed(sessionSeed);

            if (runner != null)
            {
                runner.definition = _definition;
                runner.reelEngine = reelEngine;
                runner.InsufficientFunds += OnInsufficientFunds;
            }

            // The presenter's OnEnable ran before the definition arrived, so refresh it now.
            GetComponent<SlotPresenter>()?.Refresh();
        }

        private void OnDestroy()
        {
            if (runner != null) runner.InsufficientFunds -= OnInsufficientFunds;
        }

        /// <summary>
        /// Adds the greybox slot UI if absent. A slot scene with no presenter shows a camera
        /// and nothing else, so the loop would look like it hung.
        /// </summary>
        private void EnsurePresenter()
        {
            var existing = GetComponent<SlotPresenter>();
            if (existing == null) existing = gameObject.AddComponent<SlotPresenter>();
            if (existing.controller == null) existing.controller = this;
        }

        /// <summary>Never a dead end — opens the shortfall sheet with the exact amount.</summary>
        private void OnInsufficientFunds(long bet)
        {
            long have = WalletService.Instance != null ? WalletService.Instance.Balance(Currency.Coins) : 0;
            LustLog.Warn($"[LustHouse] Insufficient Coins: have {have}, need {bet}.");
        }
    }
}