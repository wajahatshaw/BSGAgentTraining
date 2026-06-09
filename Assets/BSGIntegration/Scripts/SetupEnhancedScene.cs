using UnityEngine;

public class SetupEnhancedScene : MonoBehaviour
{
    [Header("Auto Setup Enhanced Scene")]
    public bool setupOnStart = true;
    
    void Start()
    {
        if (setupOnStart)
        {
            Debug.Log("🔧 SETTING UP ENHANCED SCENE...");
            SetupEnhancedGenerator();
        }
    }
    
    void SetupEnhancedGenerator()
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
            string scriptName = script.GetType().Name;
            if (scriptName.Contains("JSON") || 
                scriptName.Contains("Scene") || 
                scriptName.Contains("Generator") ||
                scriptName == "WorkingJSONSceneGenerator" ||
                scriptName == "SimpleJSONPlanGenerator" ||
                scriptName == "ForceSceneGeneration")
            {
                Debug.Log($"🗑️ Removing old script: {scriptName}");
                DestroyImmediate(script);
            }
        }
        
        // Add the enhanced script
        EnhancedJSONSceneGenerator enhancedScript = jsonController.GetComponent<EnhancedJSONSceneGenerator>();
        if (enhancedScript == null)
        {
            enhancedScript = jsonController.AddComponent<EnhancedJSONSceneGenerator>();
            Debug.Log("✅ Added EnhancedJSONSceneGenerator script");
        }
        else
        {
            Debug.Log("✅ EnhancedJSONSceneGenerator already attached");
        }
        
        // Configure the enhanced script
        enhancedScript.generateOnStart = true;
        enhancedScript.jsonFileName = "basicUi.json";
        enhancedScript.useURPMaterials = true;
        enhancedScript.showMovementPaths = true;
        enhancedScript.showDebugLogs = true;
        
        Debug.Log("✅ ENHANCED SCENE SETUP COMPLETE!");
        Debug.Log("🚀 The enhanced scene with moving agents and URP materials will generate automatically!");
        Debug.Log("📋 Features enabled:");
        Debug.Log("   • Agent movement with waypoint navigation");
        Debug.Log("   • URP materials with unique colors for each entity");
        Debug.Log("   • Enhanced visual effects and pulsing highlights");
        Debug.Log("   • Improved camera positioning and UI");
        
        // Destroy this setup script as it's no longer needed
        Destroy(this.gameObject);
    }
}
