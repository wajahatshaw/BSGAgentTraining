using UnityEngine;

public class ProximitySystemSetup : MonoBehaviour
{
    [Header("Setup Configuration")]
    public bool autoSetupOnStart = true;
    public bool createProximityManager = true;
    public bool addCollidersToAgents = true;
    public bool addCollidersToTools = true;
    
    [Header("Collider Settings")]
    public float agentColliderRadius = 0.5f;
    public float toolColliderRadius = 1.0f;
    
    [Header("Layer Settings")]
    public string agentLayerName = "Agent";
    public string toolLayerName = "Tool";
    
    private ProximityDetectionSystem proximitySystem;
    private ProximityConfigLoader configLoader;
    
    void Start()
    {
        if (autoSetupOnStart)
        {
            SetupProximitySystem();
        }
    }
    
    [ContextMenu("Setup Proximity System")]
    public void SetupProximitySystem()
    {
        Debug.Log("Setting up Proximity Detection System...");
        
        // Create proximity manager if needed
        if (createProximityManager)
        {
            CreateProximityManager();
        }
        
        // Add colliders to agents
        if (addCollidersToAgents)
        {
            AddCollidersToAgents();
        }
        
        // Add colliders to tools
        if (addCollidersToTools)
        {
            AddCollidersToTools();
        }
        
        // Setup layers
        SetupLayers();
        
        Debug.Log("Proximity Detection System setup complete!");
    }
    
    void CreateProximityManager()
    {
        // Find or create proximity detection system
        proximitySystem = FindObjectOfType<ProximityDetectionSystem>();
        if (proximitySystem == null)
        {
            GameObject managerObject = new GameObject("ProximityDetectionManager");
            proximitySystem = managerObject.AddComponent<ProximityDetectionSystem>();
            Debug.Log("Created ProximityDetectionSystem component");
        }
        
        // Find or create config loader
        configLoader = FindObjectOfType<ProximityConfigLoader>();
        if (configLoader == null)
        {
            GameObject loaderObject = new GameObject("ProximityConfigLoader");
            configLoader = loaderObject.AddComponent<ProximityConfigLoader>();
            Debug.Log("Created ProximityConfigLoader component");
        }
    }
    
    void AddCollidersToAgents()
    {
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        int agentsModified = 0;
        
        foreach (GameObject obj in allObjects)
        {
            if (obj.name.Contains("agent_"))
            {
                // Add sphere collider if not present
                SphereCollider collider = obj.GetComponent<SphereCollider>();
                if (collider == null)
                {
                    collider = obj.AddComponent<SphereCollider>();
                    collider.radius = agentColliderRadius;
                    collider.isTrigger = false; // We want physical collision detection
                    agentsModified++;
                }
                
                // Add rigidbody if not present (for physics-based movement)
                Rigidbody rb = obj.GetComponent<Rigidbody>();
                if (rb == null)
                {
                    rb = obj.AddComponent<Rigidbody>();
                    rb.useGravity = false; // Agents shouldn't fall
                    rb.linearDamping = 2f; // Add some drag for smoother movement
                }
            }
        }
        
        Debug.Log($"Added colliders to {agentsModified} agents");
    }
    
    void AddCollidersToTools()
    {
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        int toolsModified = 0;
        
        foreach (GameObject obj in allObjects)
        {
            if (obj.name.Contains("tool_") || obj.name.Contains("workbench_"))
            {
                // Add box collider if not present
                BoxCollider collider = obj.GetComponent<BoxCollider>();
                if (collider == null)
                {
                    collider = obj.AddComponent<BoxCollider>();
                    collider.size = Vector3.one * toolColliderRadius;
                    toolsModified++;
                }
            }
        }
        
        Debug.Log($"Added colliders to {toolsModified} tools/workbenches");
    }
    
    void SetupLayers()
    {
        // Create layers if they don't exist
        CreateLayerIfNotExists(agentLayerName);
        CreateLayerIfNotExists(toolLayerName);
        
        // Assign objects to appropriate layers
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            if (obj.name.Contains("agent_"))
            {
                int layerIndex = LayerMask.NameToLayer(agentLayerName);
                if (layerIndex != -1)
                {
                    obj.layer = layerIndex;
                }
            }
            else if (obj.name.Contains("tool_") || obj.name.Contains("workbench_"))
            {
                int layerIndex = LayerMask.NameToLayer(toolLayerName);
                if (layerIndex != -1)
                {
                    obj.layer = layerIndex;
                }
            }
        }
        
        Debug.Log($"Setup layers: {agentLayerName}, {toolLayerName}");
    }
    
    void CreateLayerIfNotExists(string layerName)
    {
        int layerIndex = LayerMask.NameToLayer(layerName);
        if (layerIndex == -1)
        {
            Debug.LogWarning($"Layer '{layerName}' does not exist. Please create it in Project Settings > Tags and Layers.");
        }
    }
    
    [ContextMenu("Test Proximity System")]
    public void TestProximitySystem()
    {
        if (proximitySystem == null)
        {
            Debug.LogError("Proximity system not found! Run setup first.");
            return;
        }
        
        Debug.Log("Testing proximity system...");
        
        // Enable the system
        proximitySystem.EnableProximityDetection();
        
        // Log current configuration
        if (configLoader != null)
        {
            configLoader.ValidateConfiguration();
        }
        
        Debug.Log("Proximity system test complete!");
    }
    
    [ContextMenu("Reset All Agent Colors")]
    public void ResetAllAgentColors()
    {
        if (proximitySystem != null)
        {
            proximitySystem.ResetAllAgentColors();
            Debug.Log("Reset all agent colors");
        }
    }
    
    [ContextMenu("Disable Proximity System")]
    public void DisableProximitySystem()
    {
        if (proximitySystem != null)
        {
            proximitySystem.DisableProximityDetection();
            Debug.Log("Proximity system disabled");
        }
    }
    
    [ContextMenu("Enable Proximity System")]
    public void EnableProximitySystem()
    {
        if (proximitySystem != null)
        {
            proximitySystem.EnableProximityDetection();
            Debug.Log("Proximity system enabled");
        }
    }
    
    [ContextMenu("Disable Movement Effects")]
    public void DisableMovementEffects()
    {
        if (proximitySystem != null)
        {
            proximitySystem.DisableDirectionEffects();
            Debug.Log("Movement effects disabled - agents can move normally");
        }
    }
    
    [ContextMenu("Enable Movement Effects")]
    public void EnableMovementEffects()
    {
        if (proximitySystem != null)
        {
            proximitySystem.EnableDirectionEffects();
            Debug.Log("Movement effects enabled");
        }
    }
    
    [ContextMenu("Enable Color Effects Only")]
    public void EnableColorEffectsOnly()
    {
        if (proximitySystem != null)
        {
            proximitySystem.EnableProximityDetection();
            proximitySystem.DisableDirectionEffects();
            Debug.Log("Only color effects enabled - no movement interference");
        }
    }
}
