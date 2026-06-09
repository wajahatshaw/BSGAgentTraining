using UnityEngine;

public class SceneSetupHelper : MonoBehaviour
{
    [Header("Setup")]
    public bool autoSetup = true;
    
    void Start()
    {
        if (autoSetup)
        {
            SetupScene();
        }
    }
    
    public void SetupScene()
    {
        Debug.Log("=== SCENE SETUP HELPER ===");
        
        // Check if StandaloneJSONScene exists
        StandaloneJSONScene jsonScene = FindObjectOfType<StandaloneJSONScene>();
        
        if (jsonScene == null)
        {
            Debug.LogError("StandaloneJSONScene not found! Please attach it to a GameObject.");
            CreateQuickSetupInstructions();
        }
        else
        {
            Debug.Log("StandaloneJSONScene found and working!");
        }
    }
    
    void CreateQuickSetupInstructions()
    {
        Debug.Log("=== QUICK SETUP INSTRUCTIONS ===");
        Debug.Log("1. Select 'JSONSceneController' in Hierarchy");
        Debug.Log("2. In Inspector, click circle next to 'None (Script)'");
        Debug.Log("3. Search for 'StandaloneJSONScene' and select it");
        Debug.Log("4. Press Play to see the scene generate!");
    }
    
    void Update()
    {
        // Quick test key
        if (Input.GetKeyDown(KeyCode.H))
        {
            CreateQuickSetupInstructions();
        }
    }
}
