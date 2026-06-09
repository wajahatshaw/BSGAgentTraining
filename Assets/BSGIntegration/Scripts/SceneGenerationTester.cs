using UnityEngine;
using UnityEngine.UI;

public class SceneGenerationTester : MonoBehaviour
{
    [Header("Test Controls")]
    public KeyCode testGenerationKey = KeyCode.G;
    public KeyCode forceGenerationKey = KeyCode.F;
    public KeyCode clearSceneKey = KeyCode.C;
    
    [Header("UI Buttons (Optional)")]
    public Button testGenerationButton;
    public Button forceGenerationButton;
    public Button clearSceneButton;
    
    [Header("Debug Info")]
    public Text debugText;
    
    private SceneGenerator sceneGenerator;
    private SceneUILoader sceneLoader;
    private JSONUIGenerator uiGenerator;
    
    void Start()
    {
        // Get references to the components
        sceneGenerator = GetComponent<SceneGenerator>();
        sceneLoader = GetComponent<SceneUILoader>();
        uiGenerator = GetComponent<JSONUIGenerator>();
        
        if (sceneGenerator == null)
        {
            Debug.LogError("SceneGenerationTester: SceneGenerator not found!");
        }
        
        if (sceneLoader == null)
        {
            Debug.LogError("SceneGenerationTester: SceneUILoader not found!");
        }
        
        if (uiGenerator == null)
        {
            Debug.LogError("SceneGenerationTester: JSONUIGenerator not found!");
        }
        
        // Setup UI buttons if provided
        SetupButtons();
        
        // Update debug info
        UpdateDebugInfo();
    }
    
    void SetupButtons()
    {
        if (testGenerationButton != null)
        {
            testGenerationButton.onClick.AddListener(() => TestSceneGeneration());
        }
        
        if (forceGenerationButton != null)
        {
            forceGenerationButton.onClick.AddListener(() => ForceSceneGeneration());
        }
        
        if (clearSceneButton != null)
        {
            clearSceneButton.onClick.AddListener(() => ClearGeneratedScene());
        }
    }
    
    void Update()
    {
        // Handle keyboard input
        if (Input.GetKeyDown(testGenerationKey))
        {
            TestSceneGeneration();
        }
        
        if (Input.GetKeyDown(forceGenerationKey))
        {
            ForceSceneGeneration();
        }
        
        if (Input.GetKeyDown(clearSceneKey))
        {
            ClearGeneratedScene();
        }
    }
    
    public void TestSceneGeneration()
    {
        Debug.Log("=== TESTING SCENE GENERATION ===");
        
        if (sceneGenerator == null)
        {
            Debug.LogError("SceneGenerator not found!");
            return;
        }
        
        // Try normal generation
        sceneGenerator.GenerateScene();
        
        UpdateDebugInfo();
    }
    
    public void ForceSceneGeneration()
    {
        Debug.Log("=== FORCING SCENE GENERATION ===");
        
        if (sceneGenerator == null)
        {
            Debug.LogError("SceneGenerator not found!");
            return;
        }
        
        // Force generation even if there are errors
        sceneGenerator.ForceGenerateScene();
        
        UpdateDebugInfo();
    }
    
    public void ClearGeneratedScene()
    {
        Debug.Log("=== CLEARING GENERATED SCENE ===");
        
        if (sceneGenerator == null)
        {
            Debug.LogError("SceneGenerator not found!");
            return;
        }
        
        // Clear the generated scene
        sceneGenerator.ClearExistingScene();
        
        UpdateDebugInfo();
    }
    
    public void RegenerateUI()
    {
        Debug.Log("=== REGENERATING UI ===");
        
        if (uiGenerator == null)
        {
            Debug.LogError("JSONUIGenerator not found!");
            return;
        }
        
        uiGenerator.RegenerateUI();
    }
    
    void UpdateDebugInfo()
    {
        if (debugText == null) return;
        
        string info = "=== SCENE GENERATION TESTER ===\n\n";
        
        // Component status
        info += $"SceneGenerator: {(sceneGenerator != null ? "✓ Found" : "✗ Missing")}\n";
        info += $"SceneUILoader: {(sceneLoader != null ? "✓ Found" : "✗ Missing")}\n";
        info += $"JSONUIGenerator: {(uiGenerator != null ? "✓ Found" : "✗ Missing")}\n\n";
        
        // Scene data status
        if (sceneLoader != null)
        {
            var sceneData = sceneLoader.GetSceneData();
            if (sceneData != null)
            {
                info += $"Scene ID: {sceneData.scene_id}\n";
                info += $"Tools: {sceneData.initialStates?.Count ?? 0}\n";
                info += $"Agents: {sceneData.agentProfiles?.Count ?? 0}\n";
                info += $"Steps: {sceneData.sequence?.Length ?? 0}\n";
            }
            else
            {
                info += "Scene Data: ✗ Not loaded\n";
            }
        }
        
        // Generated objects count
        if (sceneGenerator != null)
        {
            info += $"\nGenerated Tools: {sceneGenerator.GetGeneratedToolsCount()}\n";
            info += $"Generated Agents: {sceneGenerator.GetGeneratedAgentsCount()}\n";
        }
        
        // Controls
        info += $"\n=== CONTROLS ===\n";
        info += $"Press {testGenerationKey} to test generation\n";
        info += $"Press {forceGenerationKey} to force generation\n";
        info += $"Press {clearSceneKey} to clear scene\n";
        
        debugText.text = info;
    }
    
    // Public method to refresh debug info
    public void RefreshDebugInfo()
    {
        UpdateDebugInfo();
    }
}
