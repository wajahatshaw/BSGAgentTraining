using UnityEngine;

public class AgentProximity : MonoBehaviour
{
    public string agentID;                 // Unique ID for the agent
    private string currentAction;          // Stores the last assigned action
    private Renderer agentRenderer;
    private bool isInProximity = false;    // Track if agent is currently in proximity
    private float lastActionTime = 0f;     // Track when last action was assigned
    private float actionCooldown = 2.0f;     // Cooldown between actions (increased to prevent spam)
    private bool hasPerformedActionInCurrentProximity = false; // Track if action performed in current proximity session
    private Color defaultColor = Color.green; // Store default agent color
    
    private TextMesh actionLabel;          // Simple TextMesh label to show current action
    private GameObject labelObject;        // GameObject that holds the text label

    public float detectionRadius = 3f;     // Radius for proximity detection
    public LayerMask targetLayer;          // Assign "Tools" / "Workbench" / "Boundary" layer in Inspector
    
    // Skill-based learning system integration
    private SkillBasedActionSystem skillSystem;
    private SkillMatchResult lastSkillResult;
    private SimpleStatusBoard statusBoard; // Reference to simple status board for real-time updates
    private bool externalColorOverrideActive = false;
    private bool hasExplicitActionColor = false;
    private Color explicitActionColor = Color.green;

    void Start()
    {
        agentRenderer = GetComponent<Renderer>();
        if (agentRenderer != null)
        {
            // Force set initial green color
            Material initialMaterial = new Material(Shader.Find("Unlit/Color"));
            if (initialMaterial.shader == null)
            {
                initialMaterial = new Material(Shader.Find("Legacy Shaders/Diffuse"));
            }
            if (initialMaterial.shader == null)
            {
                initialMaterial = new Material(Shader.Find("Sprites/Default"));
            }
            
            initialMaterial.color = Color.green;
            agentRenderer.material = initialMaterial;
            defaultColor = Color.green; // Store default color
            
            Debug.Log($"🎯 AgentProximity: {agentID} initialized with green color: {agentRenderer.material.color}");
        }
        else
        {
            Debug.LogWarning($"🎯 AgentProximity: No renderer found on {agentID}");
        }
        
        // Initialize skill-based learning system
        skillSystem = FindObjectOfType<SkillBasedActionSystem>();
        if (skillSystem == null)
        {
            Debug.LogWarning($"🎓 AgentProximity: SkillBasedActionSystem not found for {agentID}");
        }
        
        // Initialize status board reference
        statusBoard = FindObjectOfType<SimpleStatusBoard>();
        if (statusBoard == null)
        {
            Debug.LogWarning($"📊 AgentProximity: SimpleStatusBoard not found for {agentID}");
        }
        
        // Create action label
        CreateActionLabel();
        
        // Update label with initial skill level after skill system is loaded
        if (skillSystem != null)
        {
            // Add a small delay to ensure skill system data is loaded
            StartCoroutine(UpdateLabelAfterDelay());
        }
    }
    
    void CreateActionLabel()
    {
        // Create a new GameObject for the label
        labelObject = new GameObject($"{agentID}_ActionLabel");
        labelObject.transform.SetParent(transform);
        labelObject.transform.localPosition = new Vector3(0, 2.5f, 0); // Position above the agent
        
        // Disable physics on the label to prevent it from falling
        Rigidbody labelRb = labelObject.AddComponent<Rigidbody>();
        labelRb.isKinematic = true; // Make it kinematic so it doesn't fall
        labelRb.useGravity = false; // Disable gravity
        
        // Add simple TextMesh component
        actionLabel = labelObject.AddComponent<TextMesh>();
        actionLabel.text = "None";
        actionLabel.fontSize = 12;
        actionLabel.color = Color.white;
        actionLabel.anchor = TextAnchor.MiddleCenter;
        actionLabel.alignment = TextAlignment.Center;
        actionLabel.fontStyle = FontStyle.Bold;
        
        // Keep label upright and readable without affecting agent movement
        labelObject.transform.rotation = Quaternion.identity;
        
        Debug.Log($"🎯 Created action label for {agentID}");
    }
    
    /// <summary>
    /// Coroutine to update label after a small delay to ensure skill system is loaded
    /// </summary>
    private System.Collections.IEnumerator UpdateLabelAfterDelay()
    {
        // Wait for one frame to ensure skill system data is loaded
        yield return null;
        
        if (skillSystem != null)
        {
            UpdateActionLabel("None");
            Debug.Log($"🎓 AgentProximity: {agentID} initial skill level: {skillSystem.GetAgentSkillLevel(agentID)}");
        }
    }

    void Update()
    {
        if (externalColorOverrideActive)
        {
            // Skip internal color enforcement while an external system (e.g. cognitive flash)
            // controls visual color state for this specific agent.
            return;
        }

        // Ensure skill system is available and label is updated
        if (skillSystem == null)
        {
            skillSystem = FindObjectOfType<SkillBasedActionSystem>();
        }
        
        // Force ensure data exists as safety measure
        if (skillSystem != null)
        {
            SkillBasedActionSystem.ForceEnsureData();
        }
        
        if (skillSystem != null && skillSystem.IsDataReady() && actionLabel != null)
        {
            // Only update label if we're in proximity or have an action
            if (isInProximity || !string.IsNullOrEmpty(currentAction))
            {
                UpdateLabelContinuously();
            }
        }
        
        // Detect objects around agent
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, detectionRadius, targetLayer);
        
        bool currentlyInProximity = false;
        string currentTarget = "";

        // Debug: Log detected objects
        if (hitColliders.Length > 0)
        {
            Debug.Log($"🎯 Agent {agentID} detected {hitColliders.Length} objects: {string.Join(", ", System.Array.ConvertAll(hitColliders, c => c.gameObject.name))}");
        }

        foreach (var hit in hitColliders)
        {
            // Only detect tools and workbenches, not borders or other objects
            if (IsValidTarget(hit.gameObject.name))
            {
                currentlyInProximity = true;
                currentTarget = hit.gameObject.name;
                Debug.Log($"🎯 Agent {agentID} found valid target: {currentTarget}");
                break;
            }
        }

        // If agent just entered proximity - DISABLED: No actions/rewards based on proximity
        if (currentlyInProximity && !isInProximity)
        {
            Debug.Log($"🎯 PROXIMITY ENTRY: {agentID} entered proximity of {currentTarget} (no action/reward)");
            
            // DISABLED: No action assignment - rewards/penalties removed
            // Agents can detect proximity but won't get Positive/Negative actions or rewards
            // if (!hasPerformedActionInCurrentProximity)
            // {
            //     Debug.Log($"🎯 PERFORMING ACTION: {agentID} will perform action in proximity of {currentTarget}");
            //     AssignRandomAction(currentTarget);
            //     hasPerformedActionInCurrentProximity = true;
            //     Debug.Log($"🎯 ACTION COMPLETED: {agentID} performed action in proximity of {currentTarget}");
            // }
            
            isInProximity = true;
        }
        // If agent is still in proximity, maintain current state (no new actions)
        else if (currentlyInProximity && isInProximity)
        {
            // Keep current action and color - no new actions
            Debug.Log($"🎯 PROXIMITY MAINTAIN: {agentID} staying in proximity of {currentTarget} (maintaining current state)");
        }
        // If agent left proximity, reset state and return to default color
        else if (!currentlyInProximity && isInProximity)
        {
            Debug.Log($"🎯 PROXIMITY EXIT: {agentID} left proximity of {currentTarget}");
            
            // Reset proximity state
            isInProximity = false;
            hasPerformedActionInCurrentProximity = false;
            
            // Return to default color immediately
            ReturnToDefaultColor();
            
            // Force update the renderer to ensure color change is applied
            if (agentRenderer != null)
            {
                agentRenderer.enabled = false;
                agentRenderer.enabled = true;
            }
        }
        
        // CRITICAL: Ensure agent color always matches current action
        // Only call this if we're in proximity and have an action
        if (isInProximity && !string.IsNullOrEmpty(currentAction))
        {
            EnsureColorMatchesAction();
        }
        // If not in proximity, ensure agent has default color
        else if (!isInProximity && !string.IsNullOrEmpty(currentAction))
        {
            Debug.LogWarning($"🎯 COLOR SAFETY CHECK: {agentID} not in proximity but has action '{currentAction}' - forcing default color");
            ReturnToDefaultColor();
        }
        else if (!isInProximity && agentRenderer != null)
        {
            // Ensure agent outside proximity has default color
            Color currentColor = agentRenderer.material.color;
            if (Vector3.Distance(new Vector3(currentColor.r, currentColor.g, currentColor.b), 
                                 new Vector3(defaultColor.r, defaultColor.g, defaultColor.b)) > 0.01f)
            {
                Debug.LogWarning($"🎯 COLOR SAFETY CHECK: {agentID} outside proximity but color is {currentColor} instead of {defaultColor} - fixing");
                ReturnToDefaultColor();
            }
        }
    }

    /// <summary>
    /// Allows external systems to temporarily own agent color (e.g. cognitive completion flash).
    /// </summary>
    public void SetExternalColorOverride(bool enabled)
    {
        externalColorOverrideActive = enabled;
    }
    
    private void ReturnToDefaultColor()
    {
        if (agentRenderer != null)
        {
            // Force create a completely new material to ensure color change
            Material defaultMaterial = new Material(Shader.Find("Unlit/Color"));
            if (defaultMaterial.shader == null)
            {
                defaultMaterial = new Material(Shader.Find("Legacy Shaders/Diffuse"));
            }
            if (defaultMaterial.shader == null)
            {
                defaultMaterial = new Material(Shader.Find("Sprites/Default"));
            }
            
            // Set the default color
            defaultMaterial.color = defaultColor;
            
            // Replace the material completely
            agentRenderer.material = defaultMaterial;
            
            // Clear current action
            currentAction = "";
            hasExplicitActionColor = false;
            explicitActionColor = defaultColor;
            
            // Update label to show default state
            UpdateActionLabel("None");
            
            // Force update the label to show default state
            if (actionLabel != null)
            {
                actionLabel.text = GetAgentDisplayName(agentID);
            }
            
            // Update status board to show idle status
            UpdateStatusBoard();
            
            Debug.Log($"🎯 RETURNED TO DEFAULT: {agentID} color reset to {defaultColor} (material replaced)");
        }
    }
    
    private void EnsureColorMatchesAction()
    {
        if (agentRenderer == null) return;
        
        // Get current action (empty string means "None")
        string actionToCheck = string.IsNullOrEmpty(currentAction) ? "None" : currentAction;
        
        // Use explicit ML-provided action color when available.
        Color expectedColor = hasExplicitActionColor ? explicitActionColor : GetActionColor(actionToCheck);
        Color currentColor = agentRenderer.material.color;
        
        // If colors don't match, fix it
        if (Vector3.Distance(new Vector3(currentColor.r, currentColor.g, currentColor.b), 
                             new Vector3(expectedColor.r, expectedColor.g, expectedColor.b)) > 0.01f)
        {
            Debug.Log($"⚠️ COLOR MISMATCH: {agentID} action '{actionToCheck}' expected {expectedColor} but got {currentColor} - fixing...");
            ApplyActionToAgent(actionToCheck);
        }
    }
    
    private bool IsValidTarget(string objectName)
    {
        // Only detect tools and workbenches, not borders, walls, or other objects
        return objectName.Contains("tool_") || 
               objectName.Contains("workbench_") || 
               objectName.Contains("Tool") || 
               objectName.Contains("Workbench") ||
               objectName.Contains("JSON_Tool_") ||
               objectName.Contains("FIXED_Tool_") ||
               objectName.Contains("Tool_tool_") ||
               objectName.Contains("Tool_workbench_");
    }
    
    // Method to manually reset to default color (not called automatically)
    public void ResetToDefaultColor()
    {
        if (agentRenderer != null)
        {
            // Create new material to ensure color change to green
            Material newMaterial = new Material(Shader.Find("Unlit/Color"));
            if (newMaterial.shader == null)
            {
                newMaterial = new Material(Shader.Find("Legacy Shaders/Diffuse"));
            }
            if (newMaterial.shader == null)
            {
                newMaterial = new Material(Shader.Find("Sprites/Default"));
            }
            
            newMaterial.color = Color.green;
            agentRenderer.material = newMaterial;
            
            // Verify color was applied
            Color appliedColor = agentRenderer.material.color;
            Debug.Log($"🎯 Agent {agentID} manually reset to green: {appliedColor}");
            
            currentAction = "";
            UpdateActionLabel("None");
        }
    }
    
    // Method to force sync agent color with current action
    public void ForceSyncColorWithAction()
    {
        if (!string.IsNullOrEmpty(currentAction))
        {
            ApplyActionToAgent(currentAction);
            UpdateActionLabel(currentAction);
            Debug.Log($"🎯 Agent {agentID} force synced - Action: {currentAction}, Agent color: {agentRenderer.material.color}");
        }
    }
    
    private void UpdateActionLabel(string action)
    {
        if (actionLabel != null)
        {
            // Only show agent ID above the agent - detailed status will be shown in corner board
            string displayAction = GetAgentDisplayName(agentID);
            
            string previousAction = actionLabel.text;
            actionLabel.text = displayAction;
            // Keep label text always white for consistency
            actionLabel.color = Color.white;
            
            // Log label update
            if (previousAction != displayAction)
            {
                Debug.Log($"🏷️ LABEL UPDATE: {agentID} label changed from '{previousAction}' to '{displayAction}'");
            }
        }
        else
        {
            Debug.LogWarning($"🏷️ LABEL ERROR: {agentID} - actionLabel is NULL");
        }
    }
    
    /// <summary>
    /// Convert agent ID to display name format (e.g., SIMPLE_Supervisor_01 -> supervisor001)
    /// </summary>
    private string GetAgentDisplayName(string agentId)
    {
        if (string.IsNullOrEmpty(agentId))
            return "Unknown";
            
        // Convert SIMPLE_Supervisor_01 -> supervisor001
        // Convert SIMPLE_Technician_01 -> technician001
        if (agentId.Contains("SIMPLE_"))
        {
            string[] parts = agentId.Split('_');
            if (parts.Length >= 3)
            {
                string role = parts[1].ToLower(); // supervisor, technician
                string number = parts[2]; // 01, 02, etc.
                
                // Convert 01 -> 001, 02 -> 002, etc.
                if (number.Length == 2)
                {
                    number = "0" + number;
                }
                
                return $"{role}{number}";
            }
        }
        
        // Fallback to original ID if parsing fails
        return agentId;
    }

    private void AssignRandomAction(string targetName)
    {
        Debug.Log($"🎯 Agent {agentID} AssignRandomAction called for target: {targetName}");
        
        // No cooldown check here - proximity logic handles when to call this
        
        // Use skill-based action system instead of random actions
        if (skillSystem != null)
        {
            // Get skill level BEFORE action
            float skillLevelBefore = skillSystem.GetAgentSkillLevel(agentID);
            Debug.Log($"🎯 BEFORE ACTION: {agentID} skill level: {skillLevelBefore:F2}");
            
            // Extract the correct tool ID from target name
            string toolId = ExtractToolIdFromTargetName(targetName);
            Debug.Log($"🎯 Extracted tool ID: {toolId} from target: {targetName}");
            
            // Determine action based on agent skills and tool requirements
            lastSkillResult = skillSystem.DetermineAction(agentID, toolId);
            
            // Apply skill progression
            skillSystem.ApplySkillProgression(agentID, lastSkillResult);
            
            // Get skill level AFTER action
            float skillLevelAfter = skillSystem.GetAgentSkillLevel(agentID);
            Debug.Log($"🎯 AFTER ACTION: {agentID} skill level: {skillLevelAfter:F2} (change: {skillLevelAfter - skillLevelBefore:F2})");
            
            // Set action based on skill result
            currentAction = lastSkillResult.actionType.ToString();
            
            Debug.Log($"🎓 SKILL-BASED ACTION: {agentID} -> {currentAction} ({lastSkillResult.reason})");
        }
        else
        {
            // Fallback to random action if skill system not available
            string[] actions = { "Positive", "Neutral", "Negative" };
            int randomIndex = Random.Range(0, actions.Length);
            currentAction = actions[randomIndex];
            Debug.Log($"🎯 FALLBACK RANDOM ACTION: {agentID} selected '{currentAction}' near {targetName}");
        }
        
        lastActionTime = Time.time;

        // Apply action to BOTH agent color and label using the SAME action data
        ApplyActionToAgent(currentAction);
        
        // Update label immediately
        ForceUpdateLabel();
        
        // Update status board with current action
        UpdateStatusBoard();
        
        Debug.Log($"✅ ACTION APPLIED: {agentID} is now doing '{currentAction}' (Agent Color: {agentRenderer.material.color}, Label: {currentAction})");
    }
    
    /// <summary>
    /// Continuously update label to show agent ID only
    /// </summary>
    private void UpdateLabelContinuously()
    {
        if (actionLabel != null)
        {
            // Only show agent ID - detailed status will be shown in corner board
            string displayAction = GetAgentDisplayName(agentID);
            
            actionLabel.text = displayAction;
            actionLabel.color = Color.white;
            
            Debug.Log($"🏷️ LABEL UPDATED: {agentID} -> {displayAction}");
        }
    }
    
    /// <summary>
    /// Force update label with agent ID only
    /// </summary>
    private void ForceUpdateLabel()
    {
        if (actionLabel != null)
        {
            // Only show agent ID - detailed status will be shown in corner board
            string displayAction = GetAgentDisplayName(agentID);
            
            actionLabel.text = displayAction;
            actionLabel.color = Color.white;
            
            Debug.Log($"🏷️ FORCE LABEL UPDATE: {agentID} -> {displayAction}");
        }
    }
    
    /// <summary>
    /// Set action from ML-Agents decision (called by BSGMLAgent)
    /// </summary>
    public void SetMLAction(string action, Color actionColor)
    {
        currentAction = action;
        hasExplicitActionColor = true;
        explicitActionColor = actionColor;
        ApplyActionToAgentWithColor(action, actionColor);
        
        // Update label immediately
        ForceUpdateLabel();
        
        // Update status board
        UpdateStatusBoard();
        
        Debug.Log($"🤖 [ML-ACTION-VISUAL] {agentID} → Action: {action}, Color: {actionColor}");
    }
    
    private void ApplyActionToAgent(string action)
    {
        Color actionColor = GetActionColor(action);
        ApplyActionToAgentWithColor(action, actionColor);
    }
    
    private void ApplyActionToAgentWithColor(string action, Color actionColor)
    {
        if (agentRenderer == null) return;
        
        // Get current color before change
        Color previousColor = agentRenderer.material.color;
        
        // Create new material with the action color
        Material newMaterial = new Material(Shader.Find("Unlit/Color"));
        if (newMaterial.shader == null)
        {
            newMaterial = new Material(Shader.Find("Legacy Shaders/Diffuse"));
        }
        if (newMaterial.shader == null)
        {
            newMaterial = new Material(Shader.Find("Sprites/Default"));
        }
        
        newMaterial.color = actionColor;
        agentRenderer.material = newMaterial;
        
        // Verify and log the applied color
        Color appliedColor = agentRenderer.material.color;
        
        // Log color change
        if (previousColor != appliedColor)
        {
            Debug.Log($"🎨 COLOR CHANGE: {agentID} color changed from {previousColor} to {appliedColor} for action '{action}'");
        }
    }
    
    private Color GetActionColor(string action)
    {
        switch (action)
        {
            case "Positive":
                return Color.white;
            case "Neutral":
                return Color.gray;
            case "Negative":
                return Color.black;
            case "None":
            default:
                return Color.green; // Default color for "None" or unknown actions
        }
    }

    /// <summary>
    /// Update the status board with current agent status
    /// </summary>
    private void UpdateStatusBoard()
    {
        if (statusBoard == null)
        {
            // Try to find status board again
            statusBoard = FindObjectOfType<SimpleStatusBoard>();
            if (statusBoard == null)
                return;
        }
        
        string status = "Idle";
        Color statusColor = Color.white;
        
        if (isInProximity && !string.IsNullOrEmpty(currentAction))
        {
            // Determine status based on current action and skill result
            if (lastSkillResult != null)
            {
                switch (lastSkillResult.actionType)
                {
                    case ActionType.Positive:
                        status = $"Learning {lastSkillResult.matchedSkill.skillName}";
                        statusColor = Color.green;
                        break;
                    case ActionType.Negative:
                        status = "Penalty - Insufficient Skill";
                        statusColor = Color.red;
                        break;
                    case ActionType.Neutral:
                        status = $"Proficient in {lastSkillResult.matchedSkill.skillName}";
                        statusColor = Color.yellow;
                        break;
                }
            }
            else
            {
                status = currentAction;
                statusColor = GetActionColor(currentAction);
            }
        }
        
        // Update status board
        statusBoard.UpdateAgentStatus(agentID, status, statusColor);
        
        Debug.Log($"📊 STATUS BOARD UPDATE: {agentID} -> {status} ({statusColor})");
    }
    
    // To visualize detection radius in Scene view
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
    
    /// <summary>
    /// Extract the correct tool ID from various target name patterns
    /// </summary>
    string ExtractToolIdFromTargetName(string targetName)
    {
        // Handle different naming patterns used by various scene generators
        
        // Pattern 1: Direct names (tool_001, workbench_001)
        if (targetName.Contains("tool_") || targetName.Contains("workbench_"))
        {
            if (targetName.Contains("tool_"))
            {
                int startIndex = targetName.IndexOf("tool_");
                string remaining = targetName.Substring(startIndex);
                int endIndex = remaining.IndexOf("_", 5); // Skip "tool_"
                if (endIndex > 0)
                {
                    return remaining.Substring(0, endIndex); // tool_001, tool_002, etc.
                }
                else
                {
                    return remaining; // tool_001, tool_002, etc.
                }
            }
            else if (targetName.Contains("workbench_"))
            {
                int startIndex = targetName.IndexOf("workbench_");
                string remaining = targetName.Substring(startIndex);
                int endIndex = remaining.IndexOf("_", 9); // Skip "workbench_"
                if (endIndex > 0)
                {
                    return remaining.Substring(0, endIndex); // workbench_001, etc.
                }
                else
                {
                    return remaining; // workbench_001, etc.
                }
            }
        }
        
        // Pattern 2: Prefixed names (JSON_Tool_tool_001, FIXED_Tool_workbench_001)
        if (targetName.Contains("JSON_Tool_") || targetName.Contains("FIXED_Tool_"))
        {
            if (targetName.Contains("JSON_Tool_tool_"))
            {
                int startIndex = targetName.IndexOf("tool_");
                string remaining = targetName.Substring(startIndex);
                return remaining; // tool_001, tool_002, etc.
            }
            else if (targetName.Contains("JSON_Tool_workbench_") || targetName.Contains("FIXED_Tool_workbench_"))
            {
                int startIndex = targetName.IndexOf("workbench_");
                string remaining = targetName.Substring(startIndex);
                return remaining; // workbench_001, etc.
            }
        }
        
        // Pattern 3: Tool_ prefixed names (Tool_tool_001, Tool_workbench_001)
        if (targetName.Contains("Tool_tool_"))
        {
            int startIndex = targetName.IndexOf("tool_");
            string remaining = targetName.Substring(startIndex);
            return remaining; // tool_001, tool_002, etc.
        }
        else if (targetName.Contains("Tool_workbench_"))
        {
            int startIndex = targetName.IndexOf("workbench_");
            string remaining = targetName.Substring(startIndex);
            return remaining; // workbench_001, etc.
        }
        
        // Fallback: return original name
        Debug.LogWarning($"⚠️ Could not extract tool ID from: {targetName}, using original name");
        return targetName;
    }
}
