using UnityEngine;

public class ProximityColorTest : MonoBehaviour
{
    [Header("Test Settings")]
    public bool enableColorTest = true;
    public bool logProximityEvents = true;
    public float testRadius = 3.0f;
    
    private ProximityDetectionSystem proximitySystem;
    
    void Start()
    {
        proximitySystem = FindObjectOfType<ProximityDetectionSystem>();
        
        if (enableColorTest)
        {
            StartCoroutine(DelayedTest());
        }
    }
    
    System.Collections.IEnumerator DelayedTest()
    {
        // Wait for systems to initialize
        yield return new WaitForSeconds(2f);
        
        TestProximityDetection();
    }
    
    void TestProximityDetection()
    {
        Debug.Log("=== TESTING PROXIMITY COLOR CHANGES ===");
        
        if (proximitySystem == null)
        {
            Debug.LogError("ProximityDetectionSystem not found!");
            return;
        }
        
        // Find all tools and agents
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            if (obj.name.Contains("JSON_Tool_") || obj.name.Contains("tool_"))
            {
                Debug.Log($"Found tool: {obj.name} at {obj.transform.position}");
                TestToolProximity(obj);
            }
            else if (obj.name.Contains("Technician_") || obj.name.Contains("agent_"))
            {
                Debug.Log($"Found agent: {obj.name} at {obj.transform.position}");
            }
        }
        
        Debug.Log("=== PROXIMITY TEST COMPLETE ===");
    }
    
    void TestToolProximity(GameObject tool)
    {
        Vector3 toolPosition = tool.transform.position;
        
        // Find agents near this tool
        Collider[] nearbyObjects = Physics.OverlapSphere(toolPosition, testRadius);
        
        foreach (var collider in nearbyObjects)
        {
            if (collider.gameObject != tool && 
                (collider.gameObject.name.Contains("Technician_") || 
                 collider.gameObject.name.Contains("agent_")))
            {
                float distance = Vector3.Distance(toolPosition, collider.transform.position);
                Debug.Log($"Agent {collider.gameObject.name} is {distance:F2} units from tool {tool.name}");
                
                if (distance <= testRadius)
                {
                    Debug.Log($"✓ Agent {collider.gameObject.name} should change color!");
                    
                    // Force color change for testing
                    Renderer agentRenderer = collider.GetComponent<Renderer>();
                    if (agentRenderer != null)
                    {
                        agentRenderer.material.color = Color.red;
                        Debug.Log($"✓ Changed {collider.gameObject.name} color to red");
                    }
                }
            }
        }
    }
    
    [ContextMenu("Force Color Test")]
    public void ForceColorTest()
    {
        Debug.Log("=== FORCING COLOR TEST ===");
        
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            if (obj.name.Contains("Technician_") || obj.name.Contains("agent_"))
            {
                Renderer renderer = obj.GetComponent<Renderer>();
                if (renderer != null)
                {
                    // Change to a bright test color
                    renderer.material.color = Color.magenta;
                    Debug.Log($"✓ Changed {obj.name} color to magenta for testing");
                }
            }
        }
        
        Debug.Log("=== COLOR TEST COMPLETE ===");
    }
    
    [ContextMenu("Reset Agent Colors")]
    public void ResetAgentColors()
    {
        Debug.Log("=== RESETTING AGENT COLORS ===");
        
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            if (obj.name.Contains("Technician_") || obj.name.Contains("agent_"))
            {
                Renderer renderer = obj.GetComponent<Renderer>();
                if (renderer != null)
                {
                    // Reset to original colors based on agent type
                    if (obj.name.Contains("technician"))
                    {
                        renderer.material.color = Color.green;
                    }
                    else if (obj.name.Contains("supervisor"))
                    {
                        renderer.material.color = Color.blue;
                    }
                    else if (obj.name.Contains("inspector"))
                    {
                        renderer.material.color = Color.magenta;
                    }
                    else
                    {
                        renderer.material.color = Color.white;
                    }
                    
                    Debug.Log($"✓ Reset {obj.name} color");
                }
            }
        }
        
        Debug.Log("=== COLOR RESET COMPLETE ===");
    }
    
    [ContextMenu("Enable Proximity System")]
    public void EnableProximitySystem()
    {
        if (proximitySystem != null)
        {
            proximitySystem.EnableProximityDetection();
            Debug.Log("✓ Proximity system enabled");
        }
    }
    
    [ContextMenu("Disable Proximity System")]
    public void DisableProximitySystem()
    {
        if (proximitySystem != null)
        {
            proximitySystem.DisableProximityDetection();
            Debug.Log("✓ Proximity system disabled");
        }
    }
    
    // Debug visualization
    void OnDrawGizmosSelected()
    {
        if (enableColorTest)
        {
            Gizmos.color = Color.yellow;
            
            GameObject[] allObjects = FindObjectsOfType<GameObject>();
            
            foreach (GameObject obj in allObjects)
            {
                if (obj.name.Contains("JSON_Tool_") || obj.name.Contains("tool_"))
                {
                    Gizmos.DrawWireSphere(obj.transform.position, testRadius);
                }
            }
        }
    }
}
