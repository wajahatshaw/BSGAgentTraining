using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages agent movement based on completion status
/// Stops agent movement when they reach 100% of their desireLevel
/// </summary>
public class AgentCompletionManager : MonoBehaviour
{
    private SkillBasedActionSystem skillSystem;
    private Dictionary<string, GameObject> agentObjects = new Dictionary<string, GameObject>();
    
    void Start()
    {
        // Find skill system
        skillSystem = FindObjectOfType<SkillBasedActionSystem>();
        if (skillSystem == null)
        {
            Debug.LogError("❌ AgentCompletionManager: SkillBasedActionSystem not found!");
            return;
        }
        
        // Subscribe to completion event
        skillSystem.OnAgentCompleted += OnAgentCompleted;
        
        // Find all agent objects in the scene
        Invoke("FindAllAgents", 1.5f);
        
        // Check for already completed agents
        Invoke("CheckAlreadyCompletedAgents", 2f);
        
        Debug.Log("✅ AgentCompletionManager initialized");
    }
    
    void OnDestroy()
    {
        if (skillSystem != null)
        {
            skillSystem.OnAgentCompleted -= OnAgentCompleted;
        }
    }
    
    void FindAllAgents()
    {
        Debug.Log("🔍 Starting comprehensive agent search...");
        
        // Clear existing agents
        agentObjects.Clear();
        
        // Find all GameObjects that might be agents
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        Debug.Log($"🔍 Searching through {allObjects.Length} GameObjects...");
        
        foreach (GameObject obj in allObjects)
        {
            string objName = obj.name;
            
            // Check for different agent naming patterns
            if (objName.Contains("SIMPLE_Technician") || objName.Contains("SIMPLE_Supervisor") ||
                objName.Contains("FIXED_Agent_SIMPLE") || objName.Contains("ENHANCED_Agent_SIMPLE") ||
                objName.Contains("Agent_SIMPLE") || objName.Contains("JSON_Agent"))
            {
                // Extract the agent ID from the object name
                string agentId = ExtractAgentIdFromObjectName(objName);
                if (!string.IsNullOrEmpty(agentId))
                {
                    agentObjects[agentId] = obj;
                    Debug.Log($"📍 Found agent by name: {objName} -> {agentId}");
                }
            }
        }
        
        Debug.Log($"✅ Found {agentObjects.Count} agent objects by name");
        
        // Also try to find agents by their movement components
        FindAgentsByMovementComponents();
        
        // Try to find agents by any component that might indicate an agent
        FindAgentsByComponents();
        
        // Log all found agents for debugging
        Debug.Log($"📍 All tracked agents ({agentObjects.Count} total):");
        foreach (var kvp in agentObjects)
        {
            Debug.Log($"   - {kvp.Key} -> {kvp.Value.name} (has {kvp.Value.GetComponents<Component>().Length} components)");
        }
        
        if (agentObjects.Count == 0)
        {
            Debug.LogWarning("⚠️ No agents found! Listing all GameObjects for debugging:");
            foreach (GameObject obj in allObjects)
            {
                if (obj.name.Contains("Agent") || obj.name.Contains("SIMPLE") || obj.name.Contains("Technician") || obj.name.Contains("Supervisor"))
                {
                    Debug.LogWarning($"   - {obj.name} (Components: {string.Join(", ", System.Array.ConvertAll(obj.GetComponents<Component>(), c => c.GetType().Name))})");
                }
            }
        }
    }
    
    string ExtractAgentIdFromObjectName(string objName)
    {
        // Handle different naming patterns:
        // "FIXED_Agent_SIMPLE_Technician_01" -> "SIMPLE_Technician_01"
        // "ENHANCED_Agent_SIMPLE_Supervisor_01" -> "SIMPLE_Supervisor_01"
        // "Agent_SIMPLE_Technician_01" -> "SIMPLE_Technician_01"
        // "SIMPLE_Technician_01" -> "SIMPLE_Technician_01"
        
        if (objName.Contains("SIMPLE_Technician") || objName.Contains("SIMPLE_Supervisor"))
        {
            // Extract the SIMPLE_ part onwards
            int simpleIndex = objName.IndexOf("SIMPLE_");
            if (simpleIndex >= 0)
            {
                return objName.Substring(simpleIndex);
            }
        }
        
        return null;
    }
    
    void FindAgentsByMovementComponents()
    {
        // Find all objects with movement components
        WorkingAgentMovement[] workingMovements = FindObjectsOfType<WorkingAgentMovement>();
        AgentMovementController[] movementControllers = FindObjectsOfType<AgentMovementController>();
        
        foreach (WorkingAgentMovement movement in workingMovements)
        {
            string agentName = movement.agentName;
            if (agentName.Contains("Technician") || agentName.Contains("Supervisor"))
            {
                // Try to map the display name back to agent ID
                string agentId = MapDisplayNameToAgentId(agentName);
                if (!string.IsNullOrEmpty(agentId) && !agentObjects.ContainsKey(agentId))
                {
                    agentObjects[agentId] = movement.gameObject;
                    Debug.Log($"📍 Found agent by WorkingAgentMovement: {movement.gameObject.name} -> {agentId}");
                }
            }
        }
        
        foreach (AgentMovementController movement in movementControllers)
        {
            string agentName = movement.agentName;
            if (agentName.Contains("Technician") || agentName.Contains("Supervisor"))
            {
                // Try to map the display name back to agent ID
                string agentId = MapDisplayNameToAgentId(agentName);
                if (!string.IsNullOrEmpty(agentId) && !agentObjects.ContainsKey(agentId))
                {
                    agentObjects[agentId] = movement.gameObject;
                    Debug.Log($"📍 Found agent by AgentMovementController: {movement.gameObject.name} -> {agentId}");
                }
            }
        }
    }
    
    string MapDisplayNameToAgentId(string displayName)
    {
        // Map display names like "Alex Rodriguez - Senior Technician" back to "SIMPLE_Technician_01"
        if (displayName.Contains("Alex Rodriguez") || displayName.Contains("Senior Technician"))
            return "SIMPLE_Technician_01";
        if (displayName.Contains("Sarah Johnson") || displayName.Contains("Junior Technician"))
            return "SIMPLE_Technician_02";
        if (displayName.Contains("Maria Santos") || displayName.Contains("Operations Supervisor"))
            return "SIMPLE_Supervisor_01";
        if (displayName.Contains("David Kim") || displayName.Contains("Safety Inspector"))
            return "SIMPLE_Supervisor_02";
            
        return null;
    }
    
    void OnAgentCompleted(string agentId)
    {
        Debug.Log($"🎉 ========================================");
        Debug.Log($"🎉 AGENT COMPLETION MANAGER: Agent {agentId} completed!");
        Debug.Log($"🎉 Stopping movement for {agentId}...");
        Debug.Log($"🎉 ========================================");
        
        StopAgentMovement(agentId);
    }
    
    void StopAgentMovement(string agentId)
    {
        GameObject agent = null;
        
        // First try to find from tracked objects
        if (agentObjects.ContainsKey(agentId))
        {
            agent = agentObjects[agentId];
        }
        else
        {
            Debug.LogWarning($"⚠️ AgentCompletionManager: Agent {agentId} not found in tracked objects");
            
            // Try to find it dynamically by searching all objects
            agent = FindAgentObjectDynamically(agentId);
            if (agent != null)
            {
                agentObjects[agentId] = agent;
                Debug.Log($"✅ Found agent {agentId} dynamically: {agent.name}");
            }
        }
        
        if (agent == null)
        {
            Debug.LogError($"❌ Could not find GameObject for agent {agentId}");
            return;
        }
        
        // Stop all movement components
        bool stoppedAny = false;
        
        // Try AgentMovementController
        AgentMovementController movementController = agent.GetComponent<AgentMovementController>();
        if (movementController != null)
        {
            movementController.PauseMovement();
            Debug.Log($"✅ Stopped AgentMovementController for {agentId}");
            stoppedAny = true;
        }
        
        // Try WorkingAgentMovement
        WorkingAgentMovement workingMovement = agent.GetComponent<WorkingAgentMovement>();
        if (workingMovement != null)
        {
            workingMovement.StopMovement();
            Debug.Log($"✅ Stopped WorkingAgentMovement for {agentId}");
            stoppedAny = true;
        }
        
        // Try any other movement scripts using reflection
        MonoBehaviour[] allComponents = agent.GetComponents<MonoBehaviour>();
        foreach (MonoBehaviour component in allComponents)
        {
            System.Type componentType = component.GetType();
            
            // Check for methods to stop movement
            var pauseMethod = componentType.GetMethod("PauseMovement");
            var stopMethod = componentType.GetMethod("StopMovement");
            
            if (pauseMethod != null)
            {
                pauseMethod.Invoke(component, null);
                Debug.Log($"✅ Called PauseMovement on {componentType.Name} for {agentId}");
                stoppedAny = true;
            }
            else if (stopMethod != null)
            {
                stopMethod.Invoke(component, null);
                Debug.Log($"✅ Called StopMovement on {componentType.Name} for {agentId}");
                stoppedAny = true;
            }
        }
        
        // Also try to disable the entire GameObject's movement by disabling common movement-related components
        if (!stoppedAny)
        {
            // Try disabling Rigidbody if present
            Rigidbody rb = agent.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                Debug.Log($"✅ Disabled Rigidbody for {agentId}");
                stoppedAny = true;
            }
            
            // Try disabling NavMeshAgent if present
            UnityEngine.AI.NavMeshAgent navAgent = agent.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (navAgent != null)
            {
                navAgent.enabled = false;
                Debug.Log($"✅ Disabled NavMeshAgent for {agentId}");
                stoppedAny = true;
            }
        }
        
        if (!stoppedAny)
        {
            Debug.LogWarning($"⚠️ No movement components found for {agentId}");
            Debug.LogWarning($"⚠️ Agent object: {agent.name}, Components: {string.Join(", ", System.Array.ConvertAll(agent.GetComponents<Component>(), c => c.GetType().Name))}");
        }
        else
        {
            Debug.Log($"🛑 Successfully stopped movement for {agentId}");
            
            // Add visual indicator that agent is completed
            AddCompletionIndicator(agent);
        }
    }
    
    GameObject FindAgentObjectDynamically(string agentId)
    {
        Debug.Log($"🔍 Dynamically searching for agent: {agentId}");
        
        // Search through all GameObjects in the scene
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        Debug.Log($"🔍 Searching through {allObjects.Length} GameObjects for {agentId}");
        
        foreach (GameObject obj in allObjects)
        {
            string objName = obj.name;
            
            // Check if this object has movement components and matches our agent
            WorkingAgentMovement workingMovement = obj.GetComponent<WorkingAgentMovement>();
            AgentMovementController movementController = obj.GetComponent<AgentMovementController>();
            
            if (workingMovement != null || movementController != null)
            {
                Debug.Log($"🔍 Found movement component on: {objName}");
                
                // Check if this object corresponds to our agent by name
                string objectAgentId = ExtractAgentIdFromObjectName(objName);
                if (objectAgentId == agentId)
                {
                    Debug.Log($"✅ Found agent by object name: {objName} -> {agentId}");
                    return obj;
                }
                
                // Also check by agent name mapping
                string agentName = workingMovement?.agentName ?? movementController?.agentName;
                if (!string.IsNullOrEmpty(agentName))
                {
                    string mappedAgentId = MapDisplayNameToAgentId(agentName);
                    if (mappedAgentId == agentId)
                    {
                        Debug.Log($"✅ Found agent by agent name mapping: {objName} ({agentName}) -> {agentId}");
                        return obj;
                    }
                }
                
                Debug.Log($"🔍 Movement component found but doesn't match {agentId}: {objName} (objectId: {objectAgentId}, agentName: {agentName}, mappedId: {MapDisplayNameToAgentId(agentName)})");
            }
            
            // Also check objects that might be agents by name pattern
            if (objName.Contains(agentId))
            {
                Debug.Log($"✅ Found agent by direct name match: {objName} -> {agentId}");
                return obj;
            }
        }
        
        Debug.LogWarning($"⚠️ Could not find GameObject for agent {agentId} dynamically");
        return null;
    }
    
    void FindAgentsByComponents()
    {
        Debug.Log("🔍 Searching for agents by components...");
        
        // Find all objects with any movement-related components
        WorkingAgentMovement[] workingMovements = FindObjectsOfType<WorkingAgentMovement>();
        AgentMovementController[] movementControllers = FindObjectsOfType<AgentMovementController>();
        
        Debug.Log($"🔍 Found {workingMovements.Length} WorkingAgentMovement components");
        Debug.Log($"🔍 Found {movementControllers.Length} AgentMovementController components");
        
        // Check WorkingAgentMovement components
        foreach (WorkingAgentMovement movement in workingMovements)
        {
            string objName = movement.gameObject.name;
            string agentName = movement.agentName;
            
            Debug.Log($"🔍 WorkingAgentMovement: {objName} (agentName: {agentName})");
            
            // Try to map the agent name to agent ID
            string agentId = MapDisplayNameToAgentId(agentName);
            if (!string.IsNullOrEmpty(agentId) && !agentObjects.ContainsKey(agentId))
            {
                agentObjects[agentId] = movement.gameObject;
                Debug.Log($"📍 Found agent by WorkingAgentMovement: {objName} -> {agentId}");
            }
            
            // Also try to extract from object name
            string objectAgentId = ExtractAgentIdFromObjectName(objName);
            if (!string.IsNullOrEmpty(objectAgentId) && !agentObjects.ContainsKey(objectAgentId))
            {
                agentObjects[objectAgentId] = movement.gameObject;
                Debug.Log($"📍 Found agent by object name: {objName} -> {objectAgentId}");
            }
        }
        
        // Check AgentMovementController components
        foreach (AgentMovementController movement in movementControllers)
        {
            string objName = movement.gameObject.name;
            string agentName = movement.agentName;
            
            Debug.Log($"🔍 AgentMovementController: {objName} (agentName: {agentName})");
            
            // Try to map the agent name to agent ID
            string agentId = MapDisplayNameToAgentId(agentName);
            if (!string.IsNullOrEmpty(agentId) && !agentObjects.ContainsKey(agentId))
            {
                agentObjects[agentId] = movement.gameObject;
                Debug.Log($"📍 Found agent by AgentMovementController: {objName} -> {agentId}");
            }
            
            // Also try to extract from object name
            string objectAgentId = ExtractAgentIdFromObjectName(objName);
            if (!string.IsNullOrEmpty(objectAgentId) && !agentObjects.ContainsKey(objectAgentId))
            {
                agentObjects[objectAgentId] = movement.gameObject;
                Debug.Log($"📍 Found agent by object name: {objName} -> {objectAgentId}");
            }
        }
    }
    
    void CheckAlreadyCompletedAgents()
    {
        if (skillSystem == null) return;
        
        var agentProfiles = skillSystem.GetAllAgentProfiles();
        if (agentProfiles == null) return;
        
        Debug.Log("🔍 Checking for already completed agents...");
        
        foreach (var agentPair in agentProfiles)
        {
            string agentId = agentPair.Key;
            AgentProfile agent = agentPair.Value;
            
            if (agent.isCompleted)
            {
                Debug.Log($"🎉 Agent {agentId} is already completed! Stopping movement...");
                StopAgentMovement(agentId);
            }
        }
    }
    
    [ContextMenu("Force Refresh Agent Finding")]
    public void ForceRefreshAgentFinding()
    {
        Debug.Log("🔄 Force refreshing agent finding...");
        agentObjects.Clear();
        FindAllAgents();
    }
    
    void AddCompletionIndicator(GameObject agent)
    {
        // Create a floating "COMPLETED" indicator above the agent
        GameObject indicatorGO = new GameObject("CompletionIndicator");
        indicatorGO.transform.SetParent(agent.transform);
        indicatorGO.transform.localPosition = new Vector3(0, 3f, 0);
        
        // Add Canvas for 3D text
        Canvas canvas = indicatorGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        
        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(3f, 0.5f);
        
        // Add background panel
        GameObject panelGO = new GameObject("Panel");
        panelGO.transform.SetParent(indicatorGO.transform, false);
        
        UnityEngine.UI.Image panelImage = panelGO.AddComponent<UnityEngine.UI.Image>();
        panelImage.color = new Color(0.2f, 0.7f, 1f, 0.9f);
        
        RectTransform panelRect = panelGO.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.sizeDelta = Vector2.zero;
        
        // Add text
        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(panelGO.transform, false);
        
        UnityEngine.UI.Text text = textGO.AddComponent<UnityEngine.UI.Text>();
        text.text = "✓ COMPLETED";
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 24;
        text.fontStyle = FontStyle.Bold;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;
        
        RectTransform textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        
        // Make it always face camera
        IndicatorBillboard billboard = indicatorGO.AddComponent<IndicatorBillboard>();
        
        Debug.Log($"✅ Added completion indicator for {agent.name}");
    }
}

/// <summary>
/// Makes the completion indicator always face the camera
/// </summary>
public class IndicatorBillboard : MonoBehaviour
{
    private Camera mainCamera;
    
    void Start()
    {
        mainCamera = Camera.main;
    }
    
    void LateUpdate()
    {
        if (mainCamera != null)
        {
            transform.LookAt(transform.position + mainCamera.transform.rotation * Vector3.forward,
                            mainCamera.transform.rotation * Vector3.up);
        }
    }
}

