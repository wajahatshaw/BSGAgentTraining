using UnityEngine;

/// <summary>
/// Simple test script to verify the Agent Status Board is working correctly
/// </summary>
public class StatusBoardTest : MonoBehaviour
{
    [Header("Test Settings")]
    public bool runTestOnStart = true;
    public float testInterval = 2f;
    
    private AgentStatusBoard statusBoard;
    private SkillBasedActionSystem skillSystem;
    private float lastTestTime = 0f;
    
    void Start()
    {
        if (runTestOnStart)
        {
            Debug.Log("🧪 StatusBoardTest: Starting status board test...");
            FindComponents();
        }
    }
    
    void Update()
    {
        if (runTestOnStart && Time.time - lastTestTime >= testInterval)
        {
            TestStatusBoard();
            lastTestTime = Time.time;
        }
    }
    
    void FindComponents()
    {
        // Find the status board
        statusBoard = FindObjectOfType<AgentStatusBoard>();
        if (statusBoard == null)
        {
            Debug.LogError("🧪 StatusBoardTest: AgentStatusBoard not found!");
            return;
        }
        
        // Find the skill system
        skillSystem = FindObjectOfType<SkillBasedActionSystem>();
        if (skillSystem == null)
        {
            Debug.LogError("🧪 StatusBoardTest: SkillBasedActionSystem not found!");
            return;
        }
        
        Debug.Log("🧪 StatusBoardTest: Found both components successfully!");
    }
    
    void TestStatusBoard()
    {
        if (statusBoard == null || skillSystem == null)
        {
            Debug.LogWarning("🧪 StatusBoardTest: Components not found, skipping test");
            return;
        }
        
        // Test if skill system has data
        bool hasData = skillSystem.IsDataReady();
        Debug.Log($"🧪 StatusBoardTest: Skill system data ready: {hasData}");
        
        if (hasData)
        {
            var agentProfiles = skillSystem.GetAllAgentProfiles();
            Debug.Log($"🧪 StatusBoardTest: Found {agentProfiles.Count} agent profiles");
            
            foreach (var agent in agentProfiles)
            {
                float skillLevel = skillSystem.GetAgentSkillLevel(agent.Key);
                int skillsCount = skillSystem.GetAgentAvailableSkillsCount(agent.Key);
                Debug.Log($"🧪 Agent {agent.Key}: Skill Level = {skillLevel:F1}, Skills Count = {skillsCount}");
            }
        }
        
        // Test status board update
        Debug.Log("🧪 StatusBoardTest: Testing status board update...");
        statusBoard.UpdateAgentStatus("SIMPLE_Technician_01", "Test Status", Color.green);
    }
    
    [ContextMenu("Run Test Now")]
    void RunTestNow()
    {
        Debug.Log("🧪 StatusBoardTest: Manual test triggered");
        FindComponents();
        TestStatusBoard();
    }
    
    [ContextMenu("Show Status Board Info")]
    void ShowStatusBoardInfo()
    {
        if (statusBoard == null)
        {
            Debug.LogWarning("🧪 StatusBoardTest: Status board not found");
            return;
        }
        
        Debug.Log($"🧪 StatusBoardTest: Status Board Info:");
        Debug.Log($"  - Canvas: {(statusBoard.statusBoardCanvas != null ? "Found" : "Missing")}");
        Debug.Log($"  - Panel: {(statusBoard.statusBoardPanel != null ? "Found" : "Missing")}");
        Debug.Log($"  - Agent List Parent: {(statusBoard.agentListParent != null ? "Found" : "Missing")}");
        Debug.Log($"  - Board Position: {statusBoard.boardPosition}");
        Debug.Log($"  - Board Size: {statusBoard.boardSize}");
    }
}
