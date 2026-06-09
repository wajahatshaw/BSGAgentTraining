using UnityEngine;
using TMPro;

public class FPSCounter : MonoBehaviour
{
    private TextMeshProUGUI fpsText;
    private float deltaTime = 0.0f;

    void Awake()
    {
        // Get the TMP text on the same GameObject
        fpsText = GetComponent<TextMeshProUGUI>();

        if (fpsText == null)
        {
            Debug.LogError("FPSCounter: No TextMeshProUGUI found on this GameObject!");
        }
    }

    void Update()
    {
        // Smoothed deltaTime
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;

        float fps = 1.0f / deltaTime;
        fpsText.text = Mathf.Ceil(fps).ToString() + " FPS";
    }
}
