using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kuluobishi.Sokoban
{
    public sealed class SokobanSceneStartup : MonoBehaviour
    {
        private void Awake()
        {
            SokobanRuntimeBootstrap.ActivateForScene(SceneManager.GetActiveScene());
        }
    }
}
