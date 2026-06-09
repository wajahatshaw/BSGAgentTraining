using UnityEngine;
using System.Collections.Generic;
using System;

public class ActionSelectionSystem : MonoBehaviour
{
    [Header("Action Selection Settings")]
    public float actionSelectionInterval = 3.0f; // How often agents select actions
    public bool enableRandomActionSelection = true;
    
    public enum ActionType
    {
        Positive,
        Neutral,
        Negative
    }
    
    [System.Serializable]
    public class ActionData
    {
        public string actionId;
        public string actionName;
        public ActionType actionType;
        public Color actionColor;
        public string zoneId;
    }
    
    [Header("Current Action State")]
    public ActionData currentAction;
    public string currentZoneId = "";
    public bool isInProximity = false;
    
    // Action selection probabilities (can be adjusted)
    [Header("Action Selection Probabilities")]
    [Range(0f, 1f)] public float positiveActionProbability = 0.5f;
    [Range(0f, 1f)] public float neutralActionProbability = 0.3f;
    [Range(0f, 1f)] public float negativeActionProbability = 0.2f;
    
    private float lastActionSelectionTime;
    private Dictionary<string, List<ActionData>> availableActionsByZone;
    private Renderer agentRenderer;
    private Color defaultColor;
    
    void Start()
    {
        agentRenderer = GetComponent<Renderer>();
        if (agentRenderer != null)
        {
            defaultColor = agentRenderer.material.color;
        }
        
        availableActionsByZone = new Dictionary<string, List<ActionData>>();
        lastActionSelectionTime = Time.time;
        
        Debug.Log($"🎯 ActionSelectionSystem initialized for {gameObject.name}");
    }
    
    void Update()
    {
        // DISABLED: Let ProximityDetectionSystem handle all color changes
        // This prevents ActionSelectionSystem from overriding colors
        return;
        
        // Check if it's time to select a new action
        if (Time.time - lastActionSelectionTime >= actionSelectionInterval)
        {
            if (isInProximity && !string.IsNullOrEmpty(currentZoneId))
            {
                SelectRandomAction();
            }
            else
            {
                // Reset to default color when not in proximity
                ResetToDefaultColor();
            }
            
            lastActionSelectionTime = Time.time;
        }
    }
    
    public void SetAvailableActions(string zoneId, List<ActionData> actions)
    {
        if (actions != null && actions.Count > 0)
        {
            availableActionsByZone[zoneId] = actions;
            currentZoneId = zoneId;
            isInProximity = true;
            
            Debug.Log($"🎯 {gameObject.name}: Available actions set for zone {zoneId}: {actions.Count} actions");
            
            // Immediately select and apply an action when entering proximity
            SelectRandomAction();
        }
    }
    
    public void ClearAvailableActions()
    {
        isInProximity = false;
        currentZoneId = "";
        currentAction = null;
        
        Debug.Log($"🎯 {gameObject.name}: Available actions cleared, resetting to default");
        
        // DISABLED: Let ProximityDetectionSystem handle color resets
        // ResetToDefaultColor();
    }
    
    void SelectRandomAction()
    {
        if (!availableActionsByZone.ContainsKey(currentZoneId) || 
            availableActionsByZone[currentZoneId].Count == 0)
        {
            Debug.LogWarning($"🎯 {gameObject.name}: No available actions for zone {currentZoneId}");
            return;
        }
        
        List<ActionData> availableActions = availableActionsByZone[currentZoneId];
        
        // Select action based on probabilities
        ActionData selectedAction = SelectActionByProbability(availableActions);
        
        if (selectedAction != null)
        {
            currentAction = selectedAction;
            ApplyActionColor(selectedAction);
            
            Debug.Log($"🎯 {gameObject.name}: Selected action '{selectedAction.actionName}' ({selectedAction.actionType}) in zone {currentZoneId} with color {selectedAction.actionColor}");
        }
        else
        {
            Debug.LogWarning($"🎯 {gameObject.name}: Failed to select action from {availableActions.Count} available actions");
        }
    }
    
    ActionData SelectActionByProbability(List<ActionData> actions)
    {
        float randomValue = UnityEngine.Random.Range(0f, 1f);
        float cumulativeProbability = 0f;
        
        // First, try to select based on action type probabilities
        foreach (ActionData action in actions)
        {
            float actionProbability = GetActionTypeProbability(action.actionType);
            cumulativeProbability += actionProbability;
            
            if (randomValue <= cumulativeProbability)
            {
                return action;
            }
        }
        
        // Fallback: select random action if probabilities don't work
        return actions[UnityEngine.Random.Range(0, actions.Count)];
    }
    
    float GetActionTypeProbability(ActionType actionType)
    {
        switch (actionType)
        {
            case ActionType.Positive:
                return positiveActionProbability;
            case ActionType.Neutral:
                return neutralActionProbability;
            case ActionType.Negative:
                return negativeActionProbability;
            default:
                return 0.33f; // Equal probability fallback
        }
    }
    
    void ApplyActionColor(ActionData action)
    {
        if (agentRenderer != null)
        {
            // Create new material with action color
            Material newMaterial = new Material(agentRenderer.material);
            newMaterial.color = action.actionColor;
            agentRenderer.material = newMaterial;
            
            Debug.Log($"🎨 {gameObject.name}: Applied color {action.actionColor} for action '{action.actionName}' ({action.actionType})");
        }
        else
        {
            Debug.LogWarning($"🎨 {gameObject.name}: No renderer found to apply color {action.actionColor}");
        }
    }
    
    void ResetToDefaultColor()
    {
        if (agentRenderer != null)
        {
            // Create new material with default color
            Material newMaterial = new Material(agentRenderer.material);
            newMaterial.color = defaultColor;
            agentRenderer.material = newMaterial;
            
            currentAction = null;
            Debug.Log($"🎨 {gameObject.name}: Reset to default color {defaultColor}");
        }
    }
    
    // Public methods for external systems
    public ActionData GetCurrentAction()
    {
        return currentAction;
    }
    
    public bool HasActiveAction()
    {
        return currentAction != null;
    }
    
    public string GetCurrentActionName()
    {
        return currentAction?.actionName ?? "None";
    }
    
    public ActionType GetCurrentActionType()
    {
        return currentAction?.actionType ?? ActionType.Neutral;
    }
    
    // Method to manually set action (for testing or external control)
    public void SetAction(ActionData action)
    {
        if (action != null)
        {
            currentAction = action;
            ApplyActionColor(action);
            Debug.Log($"🎯 {gameObject.name}: Manually set action '{action.actionName}' ({action.actionType})");
        }
    }
    
    // Method to force reset to default
    public void ForceResetToDefault()
    {
        ResetToDefaultColor();
        ClearAvailableActions();
    }
}
