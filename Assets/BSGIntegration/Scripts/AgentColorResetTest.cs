using UnityEngine;

public class AgentColorResetTest : MonoBehaviour
{
    [Header("Agent Color Reset Test")]
    public bool enableTest = true;
    public float testInterval = 3f;
    
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
        yield return new WaitForSeconds(2f);
        
        Debug.Log("=== AGENT COLOR RESET TEST STARTING ===");
        TestAgentDetection();
    }
    
    void Update()
    {
        if (enableTest && Time.time - lastTestTime >= testInterval)
        {
            TestAgentDetection();
            lastTestTime = Time.time;
        }
    }
    
    void TestAgentDetection()
    {
        Debug.Log("=== TESTING AGENT DETECTION ===");
        
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            if (IsAgent(obj.name))
            {
                Debug.Log($"Found agent: {obj.name}");
                
                // Test color reset
                ResetAgentColor(obj);
            }
        }
        
        Debug.Log("=== AGENT DETECTION TEST COMPLETE ===");
    }
    
    void ResetAgentColor(GameObject agent)
    {
        Renderer renderer = agent.GetComponent<Renderer>();
        if (renderer != null)
        {
            Color defaultColor = GetDefaultAgentColor(agent.name);
            
            Material newMaterial = new Material(renderer.material);
            newMaterial.color = defaultColor;
            renderer.material = newMaterial;
            
            Debug.Log($"*** RESET COLOR: {agent.name} reset to {defaultColor} ***");
        }
    }
    
    Color GetDefaultAgentColor(string agentName)
    {
        if (agentName.Contains("technician") || agentName.Contains("Technician"))
        {
            return Color.green;
        }
        else if (agentName.Contains("supervisor") || agentName.Contains("Supervisor"))
        {
            return Color.blue;
        }
        else if (agentName.Contains("inspector") || agentName.Contains("Inspector"))
        {
            return Color.magenta;
        }
        else
        {
            return Color.white;
        }
    }
    
    bool IsAgent(string objectName)
    {
        return objectName.Contains("SIMPLE_Technician") ||
               objectName.Contains("SIMPLE_Supervisor") ||
               objectName.Contains("SIMPLE_Inspector") ||
               objectName.Contains("Technician_") ||
               objectName.Contains("Supervisor_") ||
               objectName.Contains("Inspector_") ||
               objectName.Contains("agent_");
    }
    
    [ContextMenu("Force Reset All Agent Colors")]
    public void ForceResetAllAgentColors()
    {
        Debug.Log("=== FORCE RESET ALL AGENT COLORS ===");
        
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            if (IsAgent(obj.name))
            {
                ResetAgentColor(obj);
            }
        }
        
        Debug.Log("=== FORCE RESET COMPLETE ===");
    }
    
    [ContextMenu("Test Proximity System Reset")]
    public void TestProximitySystemReset()
    {
        if (proximitySystem != null)
        {
            proximitySystem.ResetAllAgentColors();
            Debug.Log("*** PROXIMITY SYSTEM RESET CALLED ***");
        }
        else
        {
            Debug.LogError("ProximityDetectionSystem not found!");
        }
    }
}
