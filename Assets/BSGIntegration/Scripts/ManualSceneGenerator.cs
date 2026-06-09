using UnityEngine;
using UnityEngine.UI;

public class ManualSceneGenerator : MonoBehaviour
{
    [Header("UI Buttons")]
    public Button generateButton;
    public Button forceGenerateButton;
    public Button clearButton;
    public Button testButton;
    
    [Header("Status Display")]
    public Text statusText;
    
    private SceneGenerator sceneGenerator;
    private SceneUILoader sceneLoader;
    private JSONUIGenerator uiGenerator;
    
    void Start()
    {
        // Find the components
        sceneGenerator = FindObjectOfType<SceneGenerator>();
        sceneLoader = FindObjectOfType<SceneUILoader>();
        uiGenerator = FindObjectOfType<JSONUIGenerator>();
        
        SetupButtons();
        UpdateStatus();
    }
    
    void SetupButtons()
    {
        if (generateButton != null)
        {
            generateButton.onClick.AddListener(() => GenerateScene());
        }
        
        if (forceGenerateButton != null)
        {
            forceGenerateButton.onClick.AddListener(() => ForceGenerateScene());
        }
        
        if (clearButton != null)
        {
            clearButton.onClick.AddListener(() => ClearScene());
        }
        
        if (testButton != null)
        {
            testButton.onClick.AddListener(() => TestGeneration());
        }
    }
    
    public void GenerateScene()
    {
        Debug.Log("=== MANUAL SCENE GENERATION TRIGGERED ===");
        
        if (sceneGenerator == null)
        {
            Debug.LogError("SceneGenerator not found!");
            UpdateStatus("ERROR: SceneGenerator not found!");
            return;
        }
        
        UpdateStatus("Generating scene...");
        sceneGenerator.GenerateScene();
        UpdateStatus("Scene generation complete!");
    }
    
    public void ForceGenerateScene()
    {
        Debug.Log("=== FORCE SCENE GENERATION TRIGGERED ===");
        
        if (sceneGenerator == null)
        {
            Debug.LogError("SceneGenerator not found!");
            UpdateStatus("ERROR: SceneGenerator not found!");
            return;
        }
        
        UpdateStatus("Force generating scene...");
        sceneGenerator.ForceGenerateScene();
        UpdateStatus("Force generation complete!");
    }
    
    public void ClearScene()
    {
        Debug.Log("=== CLEARING SCENE TRIGGERED ===");
        
        if (sceneGenerator == null)
        {
            Debug.LogError("SceneGenerator not found!");
            UpdateStatus("ERROR: SceneGenerator not found!");
            return;
        }
        
        UpdateStatus("Clearing scene...");
        sceneGenerator.ClearExistingScene();
        UpdateStatus("Scene cleared!");
    }
    
    public void TestGeneration()
    {
        Debug.Log("=== TEST GENERATION TRIGGERED ===");
        
        if (sceneGenerator == null)
        {
            Debug.LogError("SceneGenerator not found!");
            UpdateStatus("ERROR: SceneGenerator not found!");
            return;
        }
        
        UpdateStatus("Creating test scene...");
        sceneGenerator.CreateBasicScene();
        UpdateStatus("Test scene created!");
    }
    
    void UpdateStatus(string message = "")
    {
        if (statusText == null) return;
        
        if (string.IsNullOrEmpty(message))
        {
            // Show component status
            string status = "=== MANUAL SCENE GENERATOR ===\n\n";
            
            status += $"SceneGenerator: {(sceneGenerator != null ? "✓ Found" : "✗ Missing")}\n";
            status += $"SceneUILoader: {(sceneLoader != null ? "✓ Found" : "✗ Missing")}\n";
            status += $"JSONUIGenerator: {(uiGenerator != null ? "✓ Found" : "✗ Missing")}\n\n";
            
            if (sceneGenerator != null)
            {
                status += $"Generated Tools: {sceneGenerator.GetGeneratedToolsCount()}\n";
                status += $"Generated Agents: {sceneGenerator.GetGeneratedAgentsCount()}\n";
            }
            
            status += "\n=== CONTROLS ===\n";
            status += "• Generate: Normal JSON generation\n";
            status += "• Force Generate: Creates test objects\n";
            status += "• Clear: Removes generated objects\n";
            status += "• Test: Creates basic test scene\n";
            
            statusText.text = status;
        }
        else
        {
            statusText.text = message;
            
            // Auto-clear status after 3 seconds
            Invoke("UpdateStatus", 3f);
        }
    }
    
    void Update()
    {
        // Handle keyboard shortcuts
        if (Input.GetKeyDown(KeyCode.G))
        {
            GenerateScene();
        }
        
        if (Input.GetKeyDown(KeyCode.F))
        {
            ForceGenerateScene();
        }
        
        if (Input.GetKeyDown(KeyCode.C))
        {
            ClearScene();
        }
        
        if (Input.GetKeyDown(KeyCode.T))
        {
            TestGeneration();
        }
    }
}
