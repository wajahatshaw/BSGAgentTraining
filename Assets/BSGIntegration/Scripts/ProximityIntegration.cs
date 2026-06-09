using UnityEngine;
using System.Collections.Generic;

public class ProximityIntegration : MonoBehaviour
{
    [Header("Integration Settings")]
    public bool autoIntegrateOnStart = true;
    public bool integrateWithStandaloneJSON = true;
    public bool integrateWithSceneGenerator = true;
    
    [Header("Object Naming Patterns")]
    public string toolNamePattern = "tool_";
    public string agentNamePattern = "agent_";
    public string workbenchNamePattern = "workbench_";
    
    private StandaloneJSONScene standaloneJSONScene;
    private ProximityDetectionSystem proximitySystem;
    private ProximityConfigLoader configLoader;
    private ProximitySystemSetup setupSystem;
    
    void Start()
    {
        if (autoIntegrateOnStart)
        {
            IntegrateProximitySystem();
        }
    }
    
    [ContextMenu("Integrate Proximity System")]
    public void IntegrateProximitySystem()
    {
        Debug.Log("Integrating Proximity Detection System with existing scene...");
        
        // Find existing systems
        FindExistingSystems();
        
        // Setup proximity system
        SetupProximitySystem();
        
        // Integrate with existing scene generation
        if (integrateWithStandaloneJSON)
        {
            IntegrateWithStandaloneJSON();
        }
        
        if (integrateWithSceneGenerator)
        {
            IntegrateWithSceneGenerator();
        }
        
        // Apply proximity configuration
        ApplyProximityConfiguration();
        
        // Enable movement effects for testing
        if (proximitySystem != null)
        {
            proximitySystem.EnableDirectionEffects();
            Debug.Log("Movement effects enabled for testing");
        }
        
        Debug.Log("Proximity system integration complete!");
    }
    
    void FindExistingSystems()
    {
        standaloneJSONScene = FindObjectOfType<StandaloneJSONScene>();
        proximitySystem = FindObjectOfType<ProximityDetectionSystem>();
        configLoader = FindObjectOfType<ProximityConfigLoader>();
        setupSystem = FindObjectOfType<ProximitySystemSetup>();
        
        Debug.Log($"Found systems - StandaloneJSON: {standaloneJSONScene != null}, " +
                 $"ProximitySystem: {proximitySystem != null}, " +
                 $"ConfigLoader: {configLoader != null}, " +
                 $"SetupSystem: {setupSystem != null}");
    }
    
    void SetupProximitySystem()
    {
        // Create proximity system if it doesn't exist
        if (proximitySystem == null)
        {
            GameObject proximityManager = new GameObject("ProximityDetectionManager");
            proximitySystem = proximityManager.AddComponent<ProximityDetectionSystem>();
            Debug.Log("Created ProximityDetectionSystem");
        }
        
        // Create config loader if it doesn't exist
        if (configLoader == null)
        {
            GameObject configLoaderObj = new GameObject("ProximityConfigLoader");
            configLoader = configLoaderObj.AddComponent<ProximityConfigLoader>();
            Debug.Log("Created ProximityConfigLoader");
        }
        
        // Create setup system if it doesn't exist
        if (setupSystem == null)
        {
            GameObject setupObj = new GameObject("ProximitySystemSetup");
            setupSystem = setupObj.AddComponent<ProximitySystemSetup>();
            Debug.Log("Created ProximitySystemSetup");
        }
    }
    
    void IntegrateWithStandaloneJSON()
    {
        if (standaloneJSONScene == null) return;
        
        Debug.Log("Integrating with StandaloneJSONScene...");
        
        // Hook into the StandaloneJSONScene's entity creation
        // We'll modify the existing objects to work with proximity detection
        
        // Find all objects created by StandaloneJSONScene
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            // Check if this is a tool/workbench created by StandaloneJSONScene
            if (IsToolOrWorkbench(obj.name))
            {
                PrepareObjectForProximity(obj, "tool");
            }
            // Check if this is an agent created by StandaloneJSONScene
            else if (IsAgent(obj.name))
            {
                PrepareObjectForProximity(obj, "agent");
            }
        }
        
        Debug.Log("StandaloneJSONScene integration complete");
    }
    
    void IntegrateWithSceneGenerator()
    {
        Debug.Log("Integrating with SceneGenerator systems...");
        
        // Find objects created by SceneGenerator
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            // Check for objects with JSON_ prefix (created by SceneGenerator)
            if (obj.name.StartsWith("JSON_"))
            {
                if (obj.name.Contains("Tool") || obj.name.Contains("Workbench"))
                {
                    PrepareObjectForProximity(obj, "tool");
                }
                else if (obj.name.Contains("Agent") || obj.name.Contains("Technician"))
                {
                    PrepareObjectForProximity(obj, "agent");
                }
            }
        }
        
        Debug.Log("SceneGenerator integration complete");
    }
    
    void PrepareObjectForProximity(GameObject obj, string type)
    {
        if (type == "tool")
        {
            // Add collider if not present
            if (obj.GetComponent<Collider>() == null)
            {
                BoxCollider collider = obj.AddComponent<BoxCollider>();
                collider.size = Vector3.one * 1.5f;
                Debug.Log($"Added collider to tool: {obj.name}");
            }
        }
        else if (type == "agent")
        {
            // Add sphere collider if not present
            if (obj.GetComponent<Collider>() == null)
            {
                SphereCollider collider = obj.AddComponent<SphereCollider>();
                collider.radius = 0.5f;
                Debug.Log($"Added collider to agent: {obj.name}");
            }
            
            // Add rigidbody if not present
            if (obj.GetComponent<Rigidbody>() == null)
            {
                Rigidbody rb = obj.AddComponent<Rigidbody>();
                rb.useGravity = false;
                rb.linearDamping = 2f;
                Debug.Log($"Added rigidbody to agent: {obj.name}");
            }
        }
    }
    
    bool IsToolOrWorkbench(string objectName)
    {
        return objectName.Contains(toolNamePattern) || 
               objectName.Contains(workbenchNamePattern) ||
               objectName.Contains("JSON_Tool_") ||
               objectName.Contains("Tool_") ||
               objectName.Contains("Workbench_");
    }
    
    bool IsAgent(string objectName)
    {
        return objectName.Contains(agentNamePattern) ||
               objectName.Contains("Technician_") ||
               objectName.Contains("Supervisor_") ||
               objectName.Contains("Inspector_") ||
               objectName.Contains("Agent_");
    }
    
    void ApplyProximityConfiguration()
    {
        if (configLoader != null)
        {
            // Load configuration from JSON
            configLoader.LoadProximityConfiguration();
            
            // Validate configuration
            if (configLoader.ValidateConfiguration())
            {
                Debug.Log("Proximity configuration applied successfully");
            }
            else
            {
                Debug.LogError("Proximity configuration validation failed");
            }
        }
    }
    
    [ContextMenu("Test Integration")]
    public void TestIntegration()
    {
        Debug.Log("Testing proximity system integration...");
        
        // Count objects
        int toolCount = 0;
        int agentCount = 0;
        
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            if (IsToolOrWorkbench(obj.name))
            {
                toolCount++;
                Debug.Log($"Found tool/workbench: {obj.name}");
            }
            else if (IsAgent(obj.name))
            {
                agentCount++;
                Debug.Log($"Found agent: {obj.name}");
            }
        }
        
        Debug.Log($"Integration test complete - Found {toolCount} tools/workbenches and {agentCount} agents");
        
        // Test proximity system
        if (proximitySystem != null)
        {
            proximitySystem.EnableProximityDetection();
            Debug.Log("Proximity system enabled for testing");
        }
    }
    
    [ContextMenu("Reset Integration")]
    public void ResetIntegration()
    {
        Debug.Log("Resetting proximity system integration...");
        
        if (proximitySystem != null)
        {
            proximitySystem.DisableProximityDetection();
            proximitySystem.ResetAllAgentColors();
        }
        
        Debug.Log("Integration reset complete");
    }
    
    [ContextMenu("Fix Agent Movement")]
    public void FixAgentMovement()
    {
        Debug.Log("Fixing agent movement issues...");
        
        if (proximitySystem != null)
        {
            proximitySystem.DisableDirectionEffects();
            Debug.Log("Movement effects disabled - agents should move normally now");
        }
        
        // Also reset any agent colors that might be stuck
        if (proximitySystem != null)
        {
            proximitySystem.ResetAllAgentColors();
            Debug.Log("Agent colors reset");
        }
    }
    
    // Method to be called by StandaloneJSONScene after creating entities
    public void OnEntitiesCreated()
    {
        Debug.Log("Entities created - applying proximity integration...");
        
        // Wait a frame for objects to be fully created
        StartCoroutine(DelayedIntegration());
    }
    
    System.Collections.IEnumerator DelayedIntegration()
    {
        yield return null; // Wait one frame
        
        // Apply integration to newly created objects
        IntegrateWithStandaloneJSON();
        ApplyProximityConfiguration();
        
        Debug.Log("Delayed integration complete");
    }
}
