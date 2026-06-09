using UnityEngine;

public class FixSceneGeneration : MonoBehaviour
{
    [Header("Auto Fix")]
    public bool fixOnStart = true;
    
    void Start()
    {
        if (fixOnStart)
        {
            Debug.Log("🔧 FIXING SCENE GENERATION...");
            FixJSONSceneController();
        }
    }
    
    void FixJSONSceneController()
    {
        // Find JSONSceneController
        GameObject jsonController = GameObject.Find("JSONSceneController");
        
        if (jsonController == null)
        {
            Debug.LogError("❌ JSONSceneController not found in scene!");
            Debug.Log("Please make sure you have a GameObject named 'JSONSceneController' in your hierarchy");
            return;
        }
        
        Debug.Log("✅ Found JSONSceneController");
        
        // Remove any existing scene generation scripts
        Component[] existingScripts = jsonController.GetComponents<MonoBehaviour>();
        foreach (Component script in existingScripts)
        {
            if (script.GetType().Name.Contains("JSON") || 
                script.GetType().Name.Contains("Scene") || 
                script.GetType().Name.Contains("Generator"))
            {
                Debug.Log($"🗑️ Removing old script: {script.GetType().Name}");
                DestroyImmediate(script);
            }
        }
        
        // Add the working script
        WorkingJSONSceneGenerator workingScript = jsonController.GetComponent<WorkingJSONSceneGenerator>();
        if (workingScript == null)
        {
            workingScript = jsonController.AddComponent<WorkingJSONSceneGenerator>();
            Debug.Log("✅ Added WorkingJSONSceneGenerator script");
        }
        else
        {
            Debug.Log("✅ WorkingJSONSceneGenerator already attached");
        }
        
        // Configure the script
        workingScript.generateOnStart = true;
        workingScript.jsonFileName = "basicUi.json";
        workingScript.showDebugLogs = true;
        
        Debug.Log("✅ SCENE GENERATION FIXED!");
        Debug.Log("🚀 The scene should now generate automatically when you play!");
        
        // Destroy this fix script as it's no longer needed
        Destroy(this.gameObject);
    }
}
