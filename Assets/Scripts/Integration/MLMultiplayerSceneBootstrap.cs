using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Optional bootstrap for ProtoypeSceneMultiplayerML (legacy duplicate scene).
/// Primary merge path: RagMultiplayerSceneBootstrap on ProtoypeSceneMultiplayer.
/// </summary>
public class MLMultiplayerSceneBootstrap : MonoBehaviour
{
    const string MlMultiplayerSceneName = "ProtoypeSceneMultiplayerML";

    [SerializeField] private bool enableMlBotWorker = true;
    [SerializeField] private TaskRagBridge taskRagBridge;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoBootstrapForMlScene()
    {
        if (SceneManager.GetActiveScene().name != MlMultiplayerSceneName)
            return;
        if (FindObjectOfType<MLMultiplayerSceneBootstrap>() != null)
            return;
        var go = new GameObject("MLMultiplayerSceneBootstrap");
        go.AddComponent<MLMultiplayerSceneBootstrap>();
    }

    void Awake()
    {
        if (taskRagBridge == null)
            taskRagBridge = FindObjectOfType<TaskRagBridge>();

        if (taskRagBridge == null && enableMlBotWorker)
        {
            var go = new GameObject("TaskRagBridge");
            taskRagBridge = go.AddComponent<TaskRagBridge>();
        }
    }
}
