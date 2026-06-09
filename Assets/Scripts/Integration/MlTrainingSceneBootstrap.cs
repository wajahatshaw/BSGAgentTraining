using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Adds TaskRagBridge to ML training scene when TaskManager is present (hybrid testing).
/// </summary>
public class MlTrainingSceneBootstrap : MonoBehaviour
{
    const string TrainingSceneName = "ProtoypeSceneMLTraining";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureBridge()
    {
        if (SceneManager.GetActiveScene().name != TrainingSceneName)
            return;
        if (FindObjectOfType<TaskRagBridge>() != null)
            return;
        if (FindObjectOfType<TaskManager>() == null)
            return;
        var go = new GameObject("TaskRagBridge");
        go.AddComponent<TaskRagBridge>();
    }
}
