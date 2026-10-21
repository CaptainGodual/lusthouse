using UnityEngine;
using UnityEngine.UI;

namespace LustHouse.Audio
{
    /// <summary>
    /// Gives a button a click sound.
    ///
    /// The cue is read at the moment of the press rather than when the button is
    /// wired, so a button can be given a different sound after the fact — the SPIN
    /// button asks for its own heavier press after the generic pass has already
    /// given it a click.
    ///
    /// The listener is registered in <c>Awake</c>, once. A component that registered
    /// its listener every time it woke up would click twice on the second click.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public class ButtonClickSound : MonoBehaviour
    {
        /// <summary>The sound this button makes when it is pressed.</summary>
        public AudioCue cue = AudioCue.UiClick;

        private void Awake()
        {
            var button = GetComponent<Button>();
            if (button == null) return;
            button.onClick.AddListener(OnClicked);
        }

        private void OnClicked() => AudioFeedback.Play(cue);
    }
}
