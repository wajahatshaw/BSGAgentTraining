using UnityEngine;

public class ProximityEntryExitTest : MonoBehaviour
{
    [Header("Proximity Entry/Exit Test")]
    public bool enableTest = true;
    public bool showDebugInfo = true;
    public float testInterval = 1f;
    
    private ProximityDetectionSystem proximitySystem;
    private float lastTestTime;
    
    void Start()
    {
        proximitySystem = FindObjectOfType<ProximityDetectionSystem>();
        
        if (enableTest)
        {
            StartCoroutine(DelayedTest());
        }
    }
    
    System.Collections.IEnumerator DelayedTest()
    {
        yield return new WaitForSeconds(3f);
        
        Debug.Log("=== PROXIMITY ENTRY/EXIT TEST STARTING ===");
        LogProximityStatus();
    }
    
    void Update()
    {
        if (enableTest && showDebugInfo && Time.time - lastTestTime >= testInterval)
        {
            LogProximityStatus();
            lastTestTime = Time.time;
        }
    }
    
    void LogProximityStatus()
    {
        if (proximitySystem == null) return;
        
        Debug.Log("=== CURRENT PROXIMITY STATUS ===");
        
        // Use reflection to access private fields
        var agentsInProximityField = typeof(ProximityDetectionSystem).GetField("agentsInProximity", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        var agentInAnyProximityField = typeof(ProximityDetectionSystem).GetField("agentInAnyProximity", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (agentsInProximityField != null)
        {
            var agentsInProximity = agentsInProximityField.GetValue(proximitySystem) as System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>>;
            
            if (agentsInProximity != null)
            {
                foreach (var zone in agentsInProximity)
                {
                    Debug.Log($"Zone {zone.Key}: {zone.Value.Count} agents");
                    foreach (var agentId in zone.Value)
                    {
                        Debug.Log($"  - Agent {agentId} is in proximity");
                    }
                }
            }
        }
        
        if (agentInAnyProximityField != null)
        {
            var agentInAnyProximity = agentInAnyProximityField.GetValue(proximitySystem) as System.Collections.Generic.Dictionary<string, bool>;
            
            if (agentInAnyProximity != null)
            {
                Debug.Log($"Agents in any proximity: {agentInAnyProximity.Count}");
                foreach (var agent in agentInAnyProximity)
                {
                    Debug.Log($"  - Agent {agent.Key}: {agent.Value}");
                }
            }
        }
        
        Debug.Log("=== END PROXIMITY STATUS ===");
    }
    
    [ContextMenu("Test Agent Movement")]
    public void TestAgentMovement()
    {
        Debug.Log("=== TESTING AGENT MOVEMENT ===");
        
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            if (IsAgent(obj.name))
            {
                Debug.Log($"Found agent: {obj.name} at {obj.transform.position}");
                
                // Move agent to a random position
                Vector3 randomPosition = new Vector3(
                    Random.Range(-10f, 10f),
                    obj.transform.position.y,
                    Random.Range(-10f, 10f)
                );
                
                obj.transform.position = randomPosition;
                Debug.Log($"Moved {obj.name} to {randomPosition}");
            }
        }
        
        Debug.Log("=== AGENT MOVEMENT TEST COMPLETE ===");
    }
    
    [ContextMenu("Move Agents Near Tools")]
    public void MoveAgentsNearTools()
    {
        Debug.Log("=== MOVING AGENTS NEAR TOOLS ===");
        
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        // Find tools
        GameObject[] tools = new GameObject[5];
        int toolIndex = 0;
        
        foreach (GameObject obj in allObjects)
        {
            if (IsTool(obj.name) && toolIndex < 5)
            {
                tools[toolIndex] = obj;
                toolIndex++;
            }
        }
        
        // Move agents near tools
        int agentIndex = 0;
        foreach (GameObject obj in allObjects)
        {
            if (IsAgent(obj.name) && agentIndex < 5)
            {
                if (tools[agentIndex] != null)
                {
                    Vector3 toolPosition = tools[agentIndex].transform.position;
                    Vector3 nearPosition = toolPosition + new Vector3(
                        Random.Range(-2f, 2f),
                        0,
                        Random.Range(-2f, 2f)
                    );
                    
                    obj.transform.position = nearPosition;
                    Debug.Log($"Moved {obj.name} near {tools[agentIndex].name} to {nearPosition}");
                }
                agentIndex++;
            }
        }
        
        Debug.Log("=== AGENTS MOVED NEAR TOOLS ===");
    }
    
    [ContextMenu("Move Agents Away From Tools")]
    public void MoveAgentsAwayFromTools()
    {
        Debug.Log("=== MOVING AGENTS AWAY FROM TOOLS ===");
        
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            if (IsAgent(obj.name))
            {
                Vector3 awayPosition = new Vector3(
                    Random.Range(-15f, 15f),
                    obj.transform.position.y,
                    Random.Range(-15f, 15f)
                );
                
                obj.transform.position = awayPosition;
                Debug.Log($"Moved {obj.name} away to {awayPosition}");
            }
        }
        
        Debug.Log("=== AGENTS MOVED AWAY FROM TOOLS ===");
    }
    
    bool IsAgent(string objectName)
    {
        return objectName.Contains("SIMPLE_Technician") ||
               objectName.Contains("SIMPLE_Supervisor") ||
               objectName.Contains("Technician_") ||
               objectName.Contains("Supervisor_") ||
               objectName.Contains("agent_");
    }
    
    bool IsTool(string objectName)
    {
        return objectName.Contains("tool_") ||
               objectName.Contains("workbench_") ||
               objectName.Contains("JSON_Tool_") ||
               objectName.Contains("Tool_");
    }
}
