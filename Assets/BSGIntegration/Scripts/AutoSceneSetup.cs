using UnityEngine;

[System.Serializable]
public class AutoSceneSetup : MonoBehaviour
{
    [Header("Auto Setup")]
    public bool autoAttachScript = true;
    
    void Start()
    {
        if (autoAttachScript)
        {
            SetupJSONSceneController();
        }
    }
    
    void SetupJSONSceneController()
    {
        // Find JSONSceneController
        GameObject jsonController = GameObject.Find("JSONSceneController");
        
        if (jsonController == null)
        {
            Debug.LogError("JSONSceneController not found! Creating one...");
            jsonController = new GameObject("JSONSceneController");
        }
        
        // Check if StandaloneJSONScene is already attached
        StandaloneJSONScene existingScript = jsonController.GetComponent<StandaloneJSONScene>();
        
        if (existingScript == null)
        {
            Debug.Log("Attaching StandaloneJSONScene script to JSONSceneController...");
            jsonController.AddComponent<StandaloneJSONScene>();
            Debug.Log("✅ StandaloneJSONScene script attached successfully!");
        }
        else
        {
            Debug.Log("✅ StandaloneJSONScene script already attached!");
        }
        
        // Destroy this setup script as it's no longer needed
        Destroy(this);
    }
}
