using UnityEngine;

/// <summary>
/// Fixes workbench detection and integration issues
/// Ensures workbenches work like tools in the skill system
/// </summary>
public class WorkbenchFixer : MonoBehaviour
{
    [Header("Debug Settings")]
    public bool enableDebugLogs = true;
    
    void Start()
    {
        // Wait a bit for scene to initialize
        Invoke("FixWorkbenchIssues", 2f);
    }
    
    [ContextMenu("Fix Workbench Issues")]
    public void FixWorkbenchIssues()
    {
        Debug.Log("🔧 ===== FIXING WORKBENCH ISSUES =====");
        
        // 1. Find all workbench objects in the scene
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        int workbenchCount = 0;
        
        foreach (GameObject obj in allObjects)
        {
            if (IsWorkbenchObject(obj.name))
            {
                workbenchCount++;
                Debug.Log($"🔧 Found workbench: {obj.name} at {obj.transform.position}");
                
                // 2. Ensure workbench has proper components for proximity detection
                FixWorkbenchComponents(obj);
                
                // 3. Ensure workbench is properly named for skill system
                FixWorkbenchNaming(obj);
            }
        }
        
        Debug.Log($"🔧 Fixed {workbenchCount} workbench objects");
        
        // 4. Verify workbench integration with skill system
        VerifyWorkbenchIntegration();
        
        Debug.Log("🔧 ===== WORKBENCH FIX COMPLETE =====");
    }
    
    bool IsWorkbenchObject(string objectName)
    {
        return objectName.Contains("workbench_") || 
               objectName.Contains("Workbench_") ||
               objectName.Contains("Tool_workbench_") ||
               objectName.Contains("JSON_Tool_workbench_") ||
               objectName.Contains("FIXED_Tool_workbench_");
    }
    
    void FixWorkbenchComponents(GameObject workbench)
    {
        // Ensure workbench has a collider for proximity detection
        Collider collider = workbench.GetComponent<Collider>();
        if (collider == null)
        {
            BoxCollider boxCollider = workbench.AddComponent<BoxCollider>();
            boxCollider.size = Vector3.one * 1.5f;
            Debug.Log($"🔧 Added BoxCollider to {workbench.name}");
        }
        else
        {
            Debug.Log($"🔧 {workbench.name} already has collider: {collider.GetType().Name}");
        }
        
        // Ensure workbench has a renderer
        Renderer renderer = workbench.GetComponent<Renderer>();
        if (renderer == null)
        {
            Debug.LogWarning($"⚠️ {workbench.name} has no renderer component!");
        }
        else
        {
            Debug.Log($"🔧 {workbench.name} has renderer with material: {renderer.material.name}");
        }
        
        // Ensure workbench is on the correct layer for proximity detection
        if (workbench.layer == 0) // Default layer
        {
            // Find the Tools layer or create it
            int toolsLayer = LayerMask.NameToLayer("Tools");
            if (toolsLayer != -1)
            {
                workbench.layer = toolsLayer;
                Debug.Log($"🔧 Set {workbench.name} to Tools layer");
            }
            else
            {
                Debug.LogWarning($"⚠️ Tools layer not found - workbench {workbench.name} remains on default layer");
            }
        }
    }
    
    void FixWorkbenchNaming(GameObject workbench)
    {
        string currentName = workbench.name;
        string expectedName = "";
        
        // Extract the workbench ID from various naming patterns
        if (currentName.Contains("workbench_001"))
        {
            expectedName = "workbench_001";
        }
        else if (currentName.Contains("Tool_workbench_001"))
        {
            expectedName = "workbench_001";
        }
        else if (currentName.Contains("JSON_Tool_workbench_001"))
        {
            expectedName = "workbench_001";
        }
        else if (currentName.Contains("FIXED_Tool_workbench_001"))
        {
            expectedName = "workbench_001";
        }
        
        // Rename if necessary
        if (!string.IsNullOrEmpty(expectedName) && currentName != expectedName)
        {
            Debug.Log($"🔧 Renaming {currentName} to {expectedName}");
            workbench.name = expectedName;
        }
        else
        {
            Debug.Log($"🔧 {workbench.name} naming is correct");
        }
    }
    
    void VerifyWorkbenchIntegration()
    {
        // Check if workbench is properly integrated with skill system
        SkillBasedActionSystem skillSystem = FindObjectOfType<SkillBasedActionSystem>();
        if (skillSystem == null)
        {
            Debug.LogError("❌ SkillBasedActionSystem not found!");
            return;
        }
        
        var toolStates = skillSystem.GetAllToolStates();
        if (toolStates == null)
        {
            Debug.LogError("❌ No tool states found!");
            return;
        }
        
        if (toolStates.ContainsKey("workbench_001"))
        {
            var workbench = toolStates["workbench_001"];
            Debug.Log($"✅ workbench_001 found in skill system:");
            Debug.Log($"   - Name: {workbench.name}");
            Debug.Log($"   - Type: {workbench.type}");
            Debug.Log($"   - Required Skills: {workbench.requiredSkills?.Length ?? 0}");
            
            if (workbench.requiredSkills != null)
            {
                foreach (var skill in workbench.requiredSkills)
                {
                    Debug.Log($"   - Skill: {skill.skillName} (requiredLevel: {skill.requiredLevel}, reward: {skill.reward})");
                }
            }
        }
        else
        {
            Debug.LogError("❌ workbench_001 not found in skill system!");
            Debug.Log($"Available tools: {string.Join(", ", toolStates.Keys)}");
        }
        
        // Check if workbench is detected by proximity systems
        ProximityDetectionSystem proximitySystem = FindObjectOfType<ProximityDetectionSystem>();
        if (proximitySystem != null)
        {
            Debug.Log("✅ ProximityDetectionSystem found - workbench should be detected");
        }
        else
        {
            Debug.LogWarning("⚠️ ProximityDetectionSystem not found");
        }
    }
    
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F))
        {
            FixWorkbenchIssues();
        }
    }
    
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 500, 300, 100));
        GUILayout.Label("Workbench Fixer", GUI.skin.box);
        
        if (GUILayout.Button("Fix Workbench Issues"))
        {
            FixWorkbenchIssues();
        }
        
        GUILayout.Label("Press F to fix workbench issues");
        GUILayout.EndArea();
    }
}
