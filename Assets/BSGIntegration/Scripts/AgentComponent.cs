using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class AgentComponent : MonoBehaviour
{
    [Header("Agent Information")]
    public string agentId;
    public Dictionary<string, float> skillLevels = new Dictionary<string, float>();
    
    [Header("UI Elements")]
    [Tooltip("Creates world-space info panel above agent. Disable for clean scene view.")]
    public bool enableWorldInfoPanel = false;
    public GameObject agentInfoPanel;
    public Text agentNameText;
    public Text agentSkillsText;
    public Text agentStatusText;
    
    [Header("Visual Feedback")]
    public Material lowSkillMaterial;
    public Material mediumSkillMaterial;
    public Material highSkillMaterial;
    
    private Renderer agentRenderer;
    private AgentProfile agentProfile;
    private bool isBusy = false;
    private string currentTask = "Idle";
    
    void Awake()
    {
        agentRenderer = GetComponent<Renderer>();
        if (agentRenderer == null)
        {
            agentRenderer = GetComponentInChildren<Renderer>();
        }
    }
    
    public void Initialize(string id, AgentProfile profile)
    {
        agentId = id;
        agentProfile = profile;
        
        // Apply skills from JSON
        if (profile != null && profile.onetSkillLevels != null)
        {
            skillLevels = new Dictionary<string, float>(profile.onetSkillLevels);
        }
        
        UpdateVisuals();
        if (enableWorldInfoPanel)
        {
            CreateAgentInfoPanel();
        }
        else if (agentInfoPanel != null)
        {
            Destroy(agentInfoPanel);
            agentInfoPanel = null;
        }
    }
    
    void UpdateVisuals()
    {
        if (agentRenderer == null) return;
        
        Material targetMaterial = null;
        float avgSkill = GetAverageSkillLevel();
        
        if (avgSkill >= 4.0f)
        {
            targetMaterial = highSkillMaterial;
        }
        else if (avgSkill >= 2.5f)
        {
            targetMaterial = mediumSkillMaterial;
        }
        else
        {
            targetMaterial = lowSkillMaterial;
        }
        
        // Apply material if available, otherwise use color coding
        if (targetMaterial != null)
        {
            agentRenderer.material = targetMaterial;
        }
        else
        {
            // Fallback color coding based on skill level
            Color agentColor = GetAgentColor(avgSkill);
            agentRenderer.material.color = agentColor;
        }
    }
    
    Color GetAgentColor(float avgSkill)
    {
        // Color gradient from black (low skill) to green (high skill)
        return Color.Lerp(Color.black, Color.green, avgSkill / 5f);
    }
    
    float GetAverageSkillLevel()
    {
        if (skillLevels.Count == 0) return 0f;
        
        float total = 0f;
        foreach (var skill in skillLevels.Values)
        {
            total += skill;
        }
        
        return total / skillLevels.Count;
    }
    
    void CreateAgentInfoPanel()
    {
        if (agentInfoPanel != null) return;
        
        // Create a simple info panel above the agent
        GameObject panel = new GameObject("AgentInfoPanel");
        panel.transform.SetParent(transform);
        panel.transform.localPosition = Vector3.up * 2.5f;
        
        // Add Canvas for UI
        Canvas canvas = panel.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        
        // Add CanvasScaler
        CanvasScaler scaler = panel.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        
        // Add GraphicRaycaster
        panel.AddComponent<GraphicRaycaster>();
        
        // Create background panel
        GameObject background = new GameObject("Background");
        background.transform.SetParent(panel.transform);
        
        Image bgImage = background.AddComponent<Image>();
        bgImage.color = new Color(0, 0, 0, 0.8f);
        bgImage.rectTransform.sizeDelta = new Vector2(250, 150);
        
        // Create agent name text
        GameObject nameGO = new GameObject("AgentName");
        nameGO.transform.SetParent(panel.transform);
        
        agentNameText = nameGO.AddComponent<Text>();
        agentNameText.text = $"Agent: {agentId}";
        agentNameText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        agentNameText.fontSize = 14;
        agentNameText.color = Color.white;
        agentNameText.alignment = TextAnchor.MiddleCenter;
        agentNameText.rectTransform.anchoredPosition = new Vector2(0, 50);
        agentNameText.rectTransform.sizeDelta = new Vector2(230, 20);
        
        // Create status text
        GameObject statusGO = new GameObject("AgentStatus");
        statusGO.transform.SetParent(panel.transform);
        
        agentStatusText = statusGO.AddComponent<Text>();
        agentStatusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        agentStatusText.fontSize = 12;
        agentStatusText.color = Color.white;
        agentStatusText.alignment = TextAnchor.MiddleCenter;
        agentStatusText.rectTransform.anchoredPosition = new Vector2(0, 25);
        agentStatusText.rectTransform.sizeDelta = new Vector2(230, 20);
        
        // Create skills text
        GameObject skillsGO = new GameObject("AgentSkills");
        skillsGO.transform.SetParent(panel.transform);
        
        agentSkillsText = skillsGO.AddComponent<Text>();
        agentSkillsText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        agentSkillsText.fontSize = 10;
        agentSkillsText.color = Color.white;
        agentSkillsText.alignment = TextAnchor.MiddleCenter;
        agentSkillsText.rectTransform.anchoredPosition = new Vector2(0, -10);
        agentSkillsText.rectTransform.sizeDelta = new Vector2(230, 60);
        
        agentInfoPanel = panel;
        UpdateAgentInfo();
    }
    
    void UpdateAgentInfo()
    {
        if (agentNameText != null)
        {
            agentNameText.text = $"Agent: {GetAgentDisplayName(agentId)}";
        }
        
        if (agentStatusText != null)
        {
            string status = isBusy ? $"Busy: {currentTask}" : "Idle";
            agentStatusText.text = $"Status: {status}";
        }
        
        if (agentSkillsText != null)
        {
            string skills = "Skills:\n";
            if (skillLevels.Count > 0)
            {
                foreach (var skill in skillLevels)
                {
                    skills += $"{skill.Key}: {skill.Value:F1}\n";
                }
            }
            else
            {
                skills += "No skills defined";
            }
            
            agentSkillsText.text = skills;
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
    
    // Public methods for external control
    public void SetBusy(bool busy, string task = "")
    {
        isBusy = busy;
        if (!string.IsNullOrEmpty(task))
        {
            currentTask = task;
        }
        UpdateAgentInfo();
    }
    
    public void SetSkillLevel(string skillCode, float level)
    {
        if (skillLevels.ContainsKey(skillCode))
        {
            skillLevels[skillCode] = level;
        }
        else
        {
            skillLevels.Add(skillCode, level);
        }
        
        UpdateVisuals();
        UpdateAgentInfo();
    }
    
    public float GetSkillLevel(string skillCode)
    {
        return skillLevels.ContainsKey(skillCode) ? skillLevels[skillCode] : 0f;
    }
    
    public bool HasRequiredSkill(string skillCode, float requiredLevel)
    {
        float currentLevel = GetSkillLevel(skillCode);
        return currentLevel >= requiredLevel;
    }
    
    public bool CanPerformTask(WorkflowStep step)
    {
        if (step.requiredSkills == null) return true;
        
        foreach (var requiredSkill in step.requiredSkills)
        {
            if (!HasRequiredSkill(requiredSkill.onetSkillCode, requiredSkill.requiredLevel))
            {
                return false;
            }
        }
        
        return true;
    }
    
    public float GetTaskEfficiency(WorkflowStep step)
    {
        if (step.requiredSkills == null) return 1f;
        
        float totalEfficiency = 0f;
        int skillCount = 0;
        
        foreach (var requiredSkill in step.requiredSkills)
        {
            float currentLevel = GetSkillLevel(requiredSkill.onetSkillCode);
            float requiredLevel = requiredSkill.requiredLevel;
            
            // Calculate efficiency: 1.0 if skill level matches requirement, 
            // 0.5 if skill is too low, 1.2 if skill is higher than required
            float efficiency = 1f;
            if (currentLevel < requiredLevel)
            {
                efficiency = 0.5f;
            }
            else if (currentLevel > requiredLevel)
            {
                efficiency = 1.2f;
            }
            
            totalEfficiency += efficiency;
            skillCount++;
        }
        
        return skillCount > 0 ? totalEfficiency / skillCount : 1f;
    }
    
    // Interaction method
    public void Interact()
    {
        Debug.Log($"Interacting with agent: {agentId}");
        Debug.Log($"Current status: {(isBusy ? $"Busy with {currentTask}" : "Idle")}");
        Debug.Log($"Average skill level: {GetAverageSkillLevel():F1}");
    }
    
    void OnMouseDown()
    {
        Interact();
    }
}
