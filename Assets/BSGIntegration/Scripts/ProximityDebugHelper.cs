using UnityEngine;
using System.Collections.Generic;

public class ProximityDebugHelper : MonoBehaviour
{
    [Header("Debug Settings")]
    public bool showDebugInfo = true;
    public bool logObjectRegistry = true;
    public bool logAgentRegistry = true;
    public bool testProximityZones = true;
    
    private ProximityDetectionSystem proximitySystem;
    private ProximityConfigLoader configLoader;
    
    void Start()
    {
        proximitySystem = FindObjectOfType<ProximityDetectionSystem>();
        configLoader = FindObjectOfType<ProximityConfigLoader>();
        
        if (showDebugInfo)
        {
            StartCoroutine(DelayedDebugInfo());
        }
    }
    
    System.Collections.IEnumerator DelayedDebugInfo()
    {
        // Wait a few frames for everything to initialize
        yield return new WaitForSeconds(1f);
        
        DebugLogSystemStatus();
        
        if (logObjectRegistry)
        {
            LogObjectRegistry();
        }
        
        if (logAgentRegistry)
        {
            LogAgentRegistry();
        }
        
        if (testProximityZones)
        {
            TestProximityZones();
        }
    }
    
    void DebugLogSystemStatus()
    {
        Debug.Log("=== PROXIMITY SYSTEM DEBUG INFO ===");
        
        Debug.Log($"ProximityDetectionSystem found: {proximitySystem != null}");
        Debug.Log($"ProximityConfigLoader found: {configLoader != null}");
        
        if (proximitySystem != null)
        {
            Debug.Log($"Proximity system enabled: {proximitySystem.config.enabled}");
            Debug.Log($"Detection radius: {proximitySystem.config.detectionRadius}");
            Debug.Log($"Update frequency: {proximitySystem.config.updateFrequency}");
            Debug.Log($"Number of proximity zones: {(proximitySystem.config.proximityZones?.Length ?? 0)}");
        }
        
        if (configLoader != null)
        {
            Debug.Log($"Config loaded: {configLoader.loadedProximityConfig != null}");
            if (configLoader.loadedProximityConfig != null)
            {
                Debug.Log($"Loaded config enabled: {configLoader.loadedProximityConfig.enabled}");
                Debug.Log($"Loaded proximity zones: {configLoader.loadedProximityConfig.proximityZones?.Length ?? 0}");
            }
        }
        
        Debug.Log("=== END DEBUG INFO ===");
    }
    
    void LogObjectRegistry()
    {
        Debug.Log("=== OBJECT REGISTRY ===");
        
        if (proximitySystem != null)
        {
            // Use reflection to access private objectRegistry
            var field = typeof(ProximityDetectionSystem).GetField("objectRegistry", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (field != null)
            {
                var objectRegistry = field.GetValue(proximitySystem) as Dictionary<string, GameObject>;
                if (objectRegistry != null)
                {
                    Debug.Log($"Object registry count: {objectRegistry.Count}");
                    foreach (var kvp in objectRegistry)
                    {
                        Debug.Log($"  {kvp.Key}: {kvp.Value?.name ?? "NULL"}");
                    }
                }
            }
        }
        
        Debug.Log("=== END OBJECT REGISTRY ===");
    }
    
    void LogAgentRegistry()
    {
        Debug.Log("=== AGENT REGISTRY ===");
        
        if (proximitySystem != null)
        {
            // Use reflection to access private agentRegistry
            var field = typeof(ProximityDetectionSystem).GetField("agentRegistry", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (field != null)
            {
                var agentRegistry = field.GetValue(proximitySystem) as Dictionary<string, GameObject>;
                if (agentRegistry != null)
                {
                    Debug.Log($"Agent registry count: {agentRegistry.Count}");
                    foreach (var kvp in agentRegistry)
                    {
                        Debug.Log($"  {kvp.Key}: {kvp.Value?.name ?? "NULL"}");
                    }
                }
            }
        }
        
        Debug.Log("=== END AGENT REGISTRY ===");
    }
    
    void TestProximityZones()
    {
        Debug.Log("=== TESTING PROXIMITY ZONES ===");
        
        if (proximitySystem != null && proximitySystem.config.proximityZones != null)
        {
            foreach (var zone in proximitySystem.config.proximityZones)
            {
                if (zone != null)
                {
                    Debug.Log($"Zone: {zone.zoneId}");
                    Debug.Log($"  Center Object: {zone.centerObject}");
                    Debug.Log($"  Radius: {zone.radius}");
                    Debug.Log($"  Affected Agents: {(zone.affectedAgents?.Length ?? 0)}");
                    
                    if (zone.effects != null)
                    {
                        Debug.Log($"  Color Change Enabled: {zone.effects.colorChange?.enabled ?? false}");
                        Debug.Log($"  Direction Change Enabled: {zone.effects.directionChange?.enabled ?? false}");
                        Debug.Log($"  Sound Effect Enabled: {zone.effects.soundEffect?.enabled ?? false}");
                    }
                }
            }
        }
        
        Debug.Log("=== END PROXIMITY ZONES TEST ===");
    }
    
    [ContextMenu("Force Debug Info")]
    public void ForceDebugInfo()
    {
        DebugLogSystemStatus();
        LogObjectRegistry();
        LogAgentRegistry();
        TestProximityZones();
    }
    
    [ContextMenu("Test Object Detection")]
    public void TestObjectDetection()
    {
        Debug.Log("=== TESTING OBJECT DETECTION ===");
        
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        int toolCount = 0;
        int agentCount = 0;
        
        foreach (GameObject obj in allObjects)
        {
            if (obj.name.Contains("tool_") || obj.name.Contains("workbench_"))
            {
                toolCount++;
                Debug.Log($"Found tool/workbench: {obj.name} at {obj.transform.position}");
            }
            else if (obj.name.Contains("agent_"))
            {
                agentCount++;
                Debug.Log($"Found agent: {obj.name} at {obj.transform.position}");
            }
        }
        
        Debug.Log($"Total tools/workbenches found: {toolCount}");
        Debug.Log($"Total agents found: {agentCount}");
        Debug.Log("=== END OBJECT DETECTION TEST ===");
    }
    
    [ContextMenu("Create Test Objects")]
    public void CreateTestObjects()
    {
        Debug.Log("Creating test objects for proximity detection...");
        
        // Create test tool
        GameObject testTool = GameObject.CreatePrimitive(PrimitiveType.Cube);
        testTool.name = "tool_001";
        testTool.transform.position = new Vector3(0, 0.5f, 0);
        testTool.GetComponent<Renderer>().material.color = Color.red;
        
        // Create test agent
        GameObject testAgent = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        testAgent.name = "agent_technician_A";
        testAgent.transform.position = new Vector3(3, 1, 0);
        testAgent.GetComponent<Renderer>().material.color = Color.green;
        
        // Add colliders
        testTool.AddComponent<BoxCollider>();
        testAgent.AddComponent<SphereCollider>();
        testAgent.AddComponent<Rigidbody>();
        
        Debug.Log("Test objects created!");
    }
}
