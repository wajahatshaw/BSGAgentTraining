using UnityEngine;

/// <summary>
/// Keeps JSON/RAG scene playback running when the game window loses focus.
/// Unity's PlayerSettings handles builds, but this runtime guard also covers
/// Editor Play Mode and scene reloads.
/// </summary>
public class RunInBackgroundEnforcer : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void EnableRunInBackground()
    {
        Application.runInBackground = true;
    }

    void Awake()
    {
        Application.runInBackground = true;
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (!Application.runInBackground)
            Application.runInBackground = true;
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (!Application.runInBackground)
            Application.runInBackground = true;
    }
}
