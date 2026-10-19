using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LustHouse.Boot
{
    public class BootSequence : MonoBehaviour
    {
        public string lobbySceneName = "Lobby";

        public IEnumerator Run()
        {
            yield return new WaitForSeconds(1.9f); // splash minimum
            // TODO: addressables download, atlas bake, config fetch
            yield return new WaitForSeconds(0.6f);
            SceneManager.LoadScene(lobbySceneName);
        }
    }
}