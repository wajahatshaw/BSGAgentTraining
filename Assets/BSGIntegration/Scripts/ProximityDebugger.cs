using UnityEngine;
using System.Collections.Generic;

public class ProximityDebugger : MonoBehaviour
{
    [Header("Debug Settings")]
    public bool enableDebugMode = true;
    public bool showDetailedLogs = true;
    public bool testProximityDetection = true;
    public float debugUpdateInterval = 1.0f;
    
    private ProximityDetectionSystem proximitySystem;
    private ProximityConfigLoader configLoader;
    private float lastDebugTime;
    
    void Start()
    {
        Debug.Log("=== PROXIMITY DEBUGGER STARTING ===");
        
        if (enableDebugMode)
        {
            StartCoroutine(DelayedDebug());
        }
    }
    
    System.Collections.IEnumerator DelayedDebug()
    {
        // Wait for other systems to initialize
        yield return new WaitForSeconds(2f);
        
        DebugSystemStatus();
        DebugObjectDetection();
        DebugProximityZones();
        
        if (testProximityDetection)
        {
            StartCoroutine(ContinuousDebugging());
        }
    }
    
    System.Collections.IEnumerator ContinuousDebugging()
    {
        while (true)
        {
            yield return new WaitForSeconds(debugUpdateInterval);
            
            if (showDetailedLogs)
            {
                DebugProximityDetection();
            }
        }
    }
    
    void DebugSystemStatus()
    {
        Debug.Log("=== SYSTEM STATUS DEBUG ===");
        
        proximitySystem = FindObjectOfType<ProximityDetectionSystem>();
        configLoader = FindObjectOfType<ProximityConfigLoader>();
        
        Debug.Log($"ProximityDetectionSystem found: {proximitySystem != null}");
        Debug.Log($"ProximityConfigLoader found: {configLoader != null}");
        
        if (proximitySystem != null)
        {
            Debug.Log($"Proximity system enabled: {proximitySystem.config?.enabled ?? false}");
            Debug.Log($"Proximity zones count: {(proximitySystem.config?.proximityZones?.Length ?? 0)}");
        }
        
        if (configLoader != null)
        {
            Debug.Log($"Config loaded: {configLoader.loadedProximityConfig != null}");
            if (configLoader.loadedProximityConfig != null)
            {
                Debug.Log($"Loaded zones count: {configLoader.loadedProximityConfig.proximityZones?.Length ?? 0}");
            }
        }
        
        Debug.Log("=== END SYSTEM STATUS ===");
    }
    
    void DebugObjectDetection()
    {
        Debug.Log("=== OBJECT DETECTION DEBUG ===");
        
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        int toolCount = 0;
        int agentCount = 0;
        
        foreach (GameObject obj in allObjects)
        {
            if (IsToolOrWorkbench(obj.name))
            {
                toolCount++;
                Debug.Log($"TOOL/WORKBENCH: {obj.name} at {obj.transform.position}");
                
                // Check if it has collider
                Collider collider = obj.GetComponent<Collider>();
                Debug.Log($"  - Has Collider: {collider != null}");
                if (collider != null)
                {
                    Debug.Log($"  - Collider Type: {collider.GetType().Name}");
                }
            }
            else if (IsAgent(obj.name))
            {
                agentCount++;
                Debug.Log($"AGENT: {obj.name} at {obj.transform.position}");
                
                // Check if it has collider and rigidbody
                Collider collider = obj.GetComponent<Collider>();
                Rigidbody rigidbody = obj.GetComponent<Rigidbody>();
                Debug.Log($"  - Has Collider: {collider != null}");
                Debug.Log($"  - Has Rigidbody: {rigidbody != null}");
                
                if (collider != null)
                {
                    Debug.Log($"  - Collider Type: {collider.GetType().Name}");
                }
            }
        }
        
        Debug.Log($"Total tools/workbenches found: {toolCount}");
        Debug.Log($"Total agents found: {agentCount}");
        Debug.Log("=== END OBJECT DETECTION ===");
    }
    
    void DebugProximityZones()
    {
        Debug.Log("=== PROXIMITY ZONES DEBUG ===");
        
        if (proximitySystem != null && proximitySystem.config != null && proximitySystem.config.proximityZones != null)
        {
            foreach (var zone in proximitySystem.config.proximityZones)
            {
                if (zone != null)
                {
                    Debug.Log($"Zone: {zone.zoneId}");
                    Debug.Log($"  - Center Object: {zone.centerObject}");
                    Debug.Log($"  - Radius: {zone.radius}");
                    Debug.Log($"  - Affected Agents: {string.Join(", ", zone.affectedAgents ?? new string[0])}");
                    
                    if (zone.effects != null)
                    {
                        Debug.Log($"  - Color Change: {zone.effects.colorChange?.enabled ?? false}");
                        Debug.Log($"  - Direction Change: {zone.effects.directionChange?.enabled ?? false}");
                    }
                }
            }
        }
        else
        {
            Debug.LogWarning("No proximity zones configured!");
        }
        
        Debug.Log("=== END PROXIMITY ZONES ===");
    }
    
    void DebugProximityDetection()
    {
        Debug.Log("=== PROXIMITY DETECTION DEBUG ===");
        
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            if (IsToolOrWorkbench(obj.name))
            {
                Vector3 toolPosition = obj.transform.position;
                Collider[] nearbyObjects = Physics.OverlapSphere(toolPosition, 3.0f);
                
                Debug.Log($"Tool {obj.name} at {toolPosition} - Nearby objects: {nearbyObjects.Length}");
                
                foreach (var nearby in nearbyObjects)
                {
                    if (nearby.gameObject != obj && IsAgent(nearby.gameObject.name))
                    {
                        float distance = Vector3.Distance(toolPosition, nearby.transform.position);
                        Debug.Log($"  - Agent {nearby.gameObject.name} at distance {distance:F2}");
                        
                        if (distance <= 3.0f)
                        {
                            Debug.Log($"  *** AGENT IN PROXIMITY ZONE! ***");
                        }
                    }
                }
            }
        }
        
        Debug.Log("=== END PROXIMITY DETECTION ===");
    }
    
    bool IsToolOrWorkbench(string objectName)
    {
        return objectName.Contains("tool_") || 
               objectName.Contains("workbench_") ||
               objectName.Contains("JSON_Tool_") ||
               objectName.Contains("Tool_") ||
               objectName.Contains("Workbench_");
    }
    
    bool IsAgent(string objectName)
    {
        return objectName.Contains("agent_") ||
               objectName.Contains("Technician_") ||
               objectName.Contains("Supervisor_") ||
               objectName.Contains("Inspector_") ||
               objectName.Contains("Agent_");
    }
    
    [ContextMenu("Force Debug All")]
    public void ForceDebugAll()
    {
        DebugSystemStatus();
        DebugObjectDetection();
        DebugProximityZones();
        DebugProximityDetection();
    }
    
    [ContextMenu("Test Manual Proximity")]
    public void TestManualProximity()
    {
        Debug.Log("=== MANUAL PROXIMITY TEST ===");
        
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            if (IsToolOrWorkbench(obj.name))
            {
                Vector3 toolPosition = obj.transform.position;
                
                foreach (GameObject agentObj in allObjects)
                {
                    if (IsAgent(agentObj.name))
                    {
                        float distance = Vector3.Distance(toolPosition, agentObj.transform.position);
                        
                        if (distance <= 3.0f)
                        {
                            Debug.Log($"*** MANUAL TEST: Agent {agentObj.name} is {distance:F2} units from tool {obj.name} ***");
                            
                            // Force color change
                            Renderer renderer = agentObj.GetComponent<Renderer>();
                            if (renderer != null)
                            {
                                Material newMaterial = new Material(renderer.material);
                                newMaterial.color = Color.red;
                                renderer.material = newMaterial;
                                Debug.Log($"*** Changed {agentObj.name} color to red ***");
                            }
                            
                            // Force position change
                            Vector3 randomDirection = new Vector3(
                                Random.Range(-1f, 1f),
                                0,
                                Random.Range(-1f, 1f)
                            ).normalized;
                            
                            agentObj.transform.position += randomDirection * 2f;
                            Debug.Log($"*** Moved {agentObj.name} in direction {randomDirection} ***");
                        }
                    }
                }
            }
        }
        
        Debug.Log("=== END MANUAL TEST ===");
    }
    
    [ContextMenu("Enable Proximity System")]
    public void EnableProximitySystem()
    {
        if (proximitySystem != null)
        {
            proximitySystem.EnableProximityDetection();
            Debug.Log("✓ Proximity system enabled");
        }
        else
        {
            Debug.LogError("ProximityDetectionSystem not found!");
        }
    }
    
    [ContextMenu("Enable Direction Effects")]
    public void EnableDirectionEffects()
    {
        if (proximitySystem != null)
        {
            proximitySystem.EnableDirectionEffects();
            Debug.Log("✓ Direction effects enabled");
        }
        else
        {
            Debug.LogError("ProximityDetectionSystem not found!");
        }
    }
}
