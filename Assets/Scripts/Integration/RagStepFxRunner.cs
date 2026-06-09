using System.Collections;
using UnityEngine;

/// <summary>Coroutine host for networked step FX flashes.</summary>
public class RagStepFxRunner : MonoBehaviour
{
    static RagStepFxRunner _instance;

    public static RagStepFxRunner EnsureInScene()
    {
        if (_instance != null)
            return _instance;

        GameObject go = new GameObject(nameof(RagStepFxRunner));
        _instance = go.AddComponent<RagStepFxRunner>();
        DontDestroyOnLoad(go);
        return _instance;
    }

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    public void RunFlash(Renderer rend, Color flashColor, float duration)
    {
        if (rend == null)
            return;
        StartCoroutine(CoFlash(rend, flashColor, duration));
    }

    static IEnumerator CoFlash(Renderer rend, Color flashColor, float duration)
    {
        Color original = rend.material.color;
        rend.material.color = flashColor;
        yield return new WaitForSeconds(duration);
        if (rend != null)
            rend.material.color = original;
    }
}
