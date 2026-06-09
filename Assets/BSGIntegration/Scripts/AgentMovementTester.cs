using UnityEngine;

/// <summary>
/// Test script to manually trigger agent completion for testing purposes
/// Add this to any GameObject in the scene to test the completion system
/// </summary>
public class AgentMovementTester : MonoBehaviour
{
    [Header("Test Settings")]
    public string testAgentId = "SIMPLE_Technician_01";
    
    [Header("Debug Info")]
    public bool showDebugInfo = true;
    
    private SkillBasedActionSystem skillSystem;
    private AgentCompletionManager completionManager;
    
    void Start()
    {
        skillSystem = FindObjectOfType<SkillBasedActionSystem>();
        completionManager = FindObjectOfType<AgentCompletionManager>();
        
        if (skillSystem == null)
        {
            Debug.LogError("❌ AgentMovementTester: SkillBasedActionSystem not found!");
        }
        
        if (completionManager == null)
        {
            Debug.LogError("❌ AgentMovementTester: AgentCompletionManager not found!");
        }
        
        Debug.Log("🧪 AgentMovementTester initialized");
    }
    
    [ContextMenu("Test Force Completion")]
    public void TestForceCompletion()
    {
        if (skillSystem == null)
        {
            Debug.LogError("❌ Cannot test - SkillBasedActionSystem not found!");
            return;
        }
        
        Debug.Log($"🧪 Testing force completion for agent: {testAgentId}");
        
        var agentProfiles = skillSystem.GetAllAgentProfiles();
        if (agentProfiles != null && agentProfiles.ContainsKey(testAgentId))
        {
            AgentProfile agent = agentProfiles[testAgentId];
            
            Debug.Log($"🧪 Agent current state: SkillLevel={agent.skillLevel}/{agent.desireLevel}, isCompleted={agent.isCompleted}");
            
            // Force completion
            agent.skillLevel = agent.desireLevel;
            agent.isCompleted = true;
            
            Debug.Log($"🧪 Agent after force completion: SkillLevel={agent.skillLevel}/{agent.desireLevel}, isCompleted={agent.isCompleted}");
            
            // Trigger completion event manually
            skillSystem.TriggerAgentCompletionEvent(testAgentId);
            Debug.Log($"🧪 Completion event triggered for {testAgentId}");
        }
        else
        {
            Debug.LogError($"❌ Agent {testAgentId} not found in profiles!");
        }
    }
    
    [ContextMenu("Force Refresh Agent Finding")]
    public void TestForceRefreshAgentFinding()
    {
        if (completionManager != null)
        {
            completionManager.ForceRefreshAgentFinding();
            Debug.Log("🧪 Force refreshed agent finding");
        }
        else
        {
            Debug.LogError("❌ AgentCompletionManager not found!");
        }
    }
    
    [ContextMenu("Find All Agent Objects")]
    public void FindAllAgentObjects()
    {
        Debug.Log("🧪 ===== SEARCHING FOR ALL AGENT OBJECTS =====");
        
        // Find all GameObjects
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        Debug.Log($"🧪 Total GameObjects in scene: {allObjects.Length}");
        
        // Find objects with movement components
        WorkingAgentMovement[] workingMovements = FindObjectsOfType<WorkingAgentMovement>();
        AgentMovementController[] movementControllers = FindObjectsOfType<AgentMovementController>();
        
        Debug.Log($"🧪 WorkingAgentMovement components: {workingMovements.Length}");
        foreach (WorkingAgentMovement movement in workingMovements)
        {
            Debug.Log($"🧪   - {movement.gameObject.name} (agentName: {movement.agentName})");
        }
        
        Debug.Log($"🧪 AgentMovementController components: {movementControllers.Length}");
        foreach (AgentMovementController movement in movementControllers)
        {
            Debug.Log($"🧪   - {movement.gameObject.name} (agentName: {movement.agentName})");
        }
        
        // Find objects by name patterns
        Debug.Log("🧪 Objects containing 'Agent', 'SIMPLE', 'Technician', or 'Supervisor':");
        foreach (GameObject obj in allObjects)
        {
            if (obj.name.Contains("Agent") || obj.name.Contains("SIMPLE") || 
                obj.name.Contains("Technician") || obj.name.Contains("Supervisor"))
            {
                Debug.Log($"🧪   - {obj.name} (Components: {string.Join(", ", System.Array.ConvertAll(obj.GetComponents<Component>(), c => c.GetType().Name))})");
            }
        }
        
        Debug.Log("🧪 =========================================");
    }
    
    [ContextMenu("Show All Agent Info")]
    public void ShowAllAgentInfo()
    {
        if (skillSystem == null)
        {
            Debug.LogError("❌ SkillBasedActionSystem not found!");
            return;
        }
        
        var agentProfiles = skillSystem.GetAllAgentProfiles();
        if (agentProfiles == null)
        {
            Debug.LogError("❌ No agent profiles found!");
            return;
        }
        
        Debug.Log("🧪 ===== ALL AGENT INFO =====");
        foreach (var agentPair in agentProfiles)
        {
            string agentId = agentPair.Key;
            AgentProfile agent = agentPair.Value;
            
            float percentage = (agent.skillLevel / agent.desireLevel) * 100f;
            
            Debug.Log($"🧪 {agentId}:");
            Debug.Log($"   - SkillLevel: {agent.skillLevel}/{agent.desireLevel} ({percentage:F1}%)");
            Debug.Log($"   - isCompleted: {agent.isCompleted}");
            Debug.Log($"   - Available Skills: {agent.availableSkills?.Length ?? 0}");
        }
        Debug.Log("🧪 ==========================");
    }
    
    void Update()
    {
        if (showDebugInfo && Input.GetKeyDown(KeyCode.T))
        {
            TestForceCompletion();
        }
        
        if (showDebugInfo && Input.GetKeyDown(KeyCode.I))
        {
            ShowAllAgentInfo();
        }
        
        if (showDebugInfo && Input.GetKeyDown(KeyCode.R))
        {
            TestForceRefreshAgentFinding();
        }
    }
    
    void OnGUI()
    {
        if (!showDebugInfo) return;
        
        GUILayout.BeginArea(new Rect(10, 100, 300, 200));
        GUILayout.Label("Agent Movement Tester", GUI.skin.box);
        GUILayout.Label($"Test Agent: {testAgentId}");
        
        if (GUILayout.Button("Test Force Completion"))
        {
            TestForceCompletion();
        }
        
        if (GUILayout.Button("Show All Agent Info"))
        {
            ShowAllAgentInfo();
        }
        
        if (GUILayout.Button("Force Refresh Agent Finding"))
        {
            TestForceRefreshAgentFinding();
        }
        
        GUILayout.Label("Keyboard shortcuts:");
        GUILayout.Label("T - Test Force Completion");
        GUILayout.Label("I - Show All Agent Info");
        GUILayout.Label("R - Force Refresh Agent Finding");
        
        GUILayout.EndArea();
    }
}
