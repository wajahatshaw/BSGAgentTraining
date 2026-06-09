using UnityEngine;

public class SetupFixedDynamicScene : MonoBehaviour
{
    [Header("Auto Setup Fixed Dynamic Scene")]
    public bool setupOnStart = true;
    
    void Start()
    {
        if (setupOnStart)
        {
            Debug.Log("🔧 SETTING UP FIXED DYNAMIC SCENE...");
            SetupFixedGenerator();
        }
    }
    
    void SetupFixedGenerator()
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
                scriptName.Contains("Enhanced") ||
                scriptName == "WorkingJSONSceneGenerator" ||
                scriptName == "EnhancedJSONSceneGenerator" ||
                scriptName == "SimpleJSONPlanGenerator" ||
                scriptName == "ForceSceneGeneration")
            {
                Debug.Log($"🗑️ Removing old script: {scriptName}");
                DestroyImmediate(script);
            }
        }
        
        // Add the fixed dynamic script
        FixedDynamicSceneGenerator fixedScript = jsonController.GetComponent<FixedDynamicSceneGenerator>();
        if (fixedScript == null)
        {
            fixedScript = jsonController.AddComponent<FixedDynamicSceneGenerator>();
            Debug.Log("✅ Added FixedDynamicSceneGenerator script");
        }
        else
        {
            Debug.Log("✅ FixedDynamicSceneGenerator already attached");
        }
        
        // Configure the fixed script
        fixedScript.generateOnStart = true;
        fixedScript.jsonFileName = "basicUi.json";
        fixedScript.showDebugLogs = true;
        
        Debug.Log("✅ FIXED DYNAMIC SCENE SETUP COMPLETE!");
        Debug.Log("🚀 The scene will now generate with:");
        Debug.Log("   • ✅ WORKING agent movement (agents will actually move!)");
        Debug.Log("   • ✅ PROPER materials (no more pink materials!)");
        Debug.Log("   • ✅ CORRECT colors from JSON hex codes");
        Debug.Log("   • ✅ STANDARD shader compatibility (works everywhere)");
        Debug.Log("   • ✅ VISIBLE movement paths in Scene view");
        Debug.Log("   • ✅ DETAILED console logging for debugging");
        
        // Destroy this setup script as it's no longer needed
        Destroy(this.gameObject);
    }
}
