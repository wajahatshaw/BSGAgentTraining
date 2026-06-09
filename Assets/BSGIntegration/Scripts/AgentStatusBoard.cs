using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

public class AgentStatusBoard : MonoBehaviour
{
    [Header("UI References")]
    public Canvas statusBoardCanvas;
    public GameObject statusBoardPanel;
    public Transform agentListParent;
    public GameObject agentStatusPrefab;
    
    [Header("Status Board Settings")]
    public Vector2 boardPosition = new Vector2(400, 200); // Top-right corner
    public Vector2 boardSize = new Vector2(350, 500);
    public float updateInterval = 0.5f; // Update every 0.5 seconds
    
    [Header("Visual Settings")]
    public Color backgroundColor = new Color(0, 0, 0, 0.8f);
    public Color headerColor = new Color(0.2f, 0.4f, 0.8f, 1f);
    public Color textColor = Color.white;
    public Color positiveColor = Color.green;
    public Color negativeColor = Color.red;
    public Color neutralColor = Color.yellow;
    
    private SkillBasedActionSystem skillSystem;
    private Dictionary<string, GameObject> agentStatusPanels = new Dictionary<string, GameObject>();
    private float lastUpdateTime = 0f;
    
    void Start()
    {
        // Find skill system
        skillSystem = FindObjectOfType<SkillBasedActionSystem>();
        if (skillSystem == null)
        {
            Debug.LogError("AgentStatusBoard: SkillBasedActionSystem not found!");
            return;
        }
        
        // Create the status board UI
        CreateStatusBoard();
        
        // Initial update with a small delay to ensure everything is created
        StartCoroutine(InitialUpdateAfterDelay());
    }
    
    void Update()
    {
        // Update status board at regular intervals
        if (Time.time - lastUpdateTime >= updateInterval)
        {
            UpdateAgentStatuses();
            lastUpdateTime = Time.time;
        }
    }
    
    void CreateStatusBoard()
    {
        // Create main canvas if not assigned
        if (statusBoardCanvas == null)
        {
            GameObject canvasGO = new GameObject("StatusBoardCanvas");
            statusBoardCanvas = canvasGO.AddComponent<Canvas>();
            statusBoardCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            statusBoardCanvas.sortingOrder = 100; // Ensure it's on top
            
            // Add CanvasScaler
            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            
            // Add GraphicRaycaster
            canvasGO.AddComponent<GraphicRaycaster>();
        }
        
        // Create main panel
        if (statusBoardPanel == null)
        {
            statusBoardPanel = new GameObject("StatusBoardPanel");
            statusBoardPanel.transform.SetParent(statusBoardCanvas.transform, false);
            
            // Add Image component for background
            Image panelImage = statusBoardPanel.AddComponent<Image>();
            panelImage.color = backgroundColor;
            
            // Set position and size
            RectTransform panelRect = statusBoardPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(1, 1); // Top-right anchor
            panelRect.anchorMax = new Vector2(1, 1); // Top-right anchor
            panelRect.anchoredPosition = boardPosition;
            panelRect.sizeDelta = boardSize;
        }
        
        // Create header
        CreateHeader();
        
        // Create agent list parent
        if (agentListParent == null)
        {
            GameObject listParent = new GameObject("AgentListParent");
            listParent.transform.SetParent(statusBoardPanel.transform, false);
            
            RectTransform listRect = listParent.AddComponent<RectTransform>();
            listRect.anchorMin = new Vector2(0, 0);
            listRect.anchorMax = new Vector2(1, 1);
            listRect.offsetMin = new Vector2(10, 50); // Padding from edges
            listRect.offsetMax = new Vector2(-10, -10);
            
            // Add VerticalLayoutGroup
            VerticalLayoutGroup layoutGroup = listParent.AddComponent<VerticalLayoutGroup>();
            layoutGroup.spacing = 5f;
            layoutGroup.padding = new RectOffset(5, 5, 5, 5);
            layoutGroup.childControlHeight = false;
            layoutGroup.childControlWidth = true;
            layoutGroup.childForceExpandWidth = true;
            
            agentListParent = listParent.transform;
        }
        
        Debug.Log("AgentStatusBoard: Status board created successfully");
    }
    
    void CreateHeader()
    {
        // Create header background
        GameObject headerBG = new GameObject("HeaderBackground");
        headerBG.transform.SetParent(statusBoardPanel.transform, false);
        
        Image headerImage = headerBG.AddComponent<Image>();
        headerImage.color = headerColor;
        
        RectTransform headerRect = headerBG.GetComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0, 1);
        headerRect.anchorMax = new Vector2(1, 1);
        headerRect.anchoredPosition = new Vector2(0, -25);
        headerRect.sizeDelta = new Vector2(0, 40);
        
        // Create header text
        GameObject headerText = new GameObject("HeaderText");
        headerText.transform.SetParent(headerBG.transform, false);
        
        Text text = headerText.AddComponent<Text>();
        text.text = "AGENT STATUS BOARD";
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 16;
        text.color = textColor;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        
        RectTransform textRect = headerText.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
    }
    
    /// <summary>
    /// Coroutine to update agent statuses after a delay to ensure UI is fully created
    /// </summary>
    private System.Collections.IEnumerator InitialUpdateAfterDelay()
    {
        // Wait a frame to ensure UI is fully created
        yield return null;
        
        // Wait a bit more for skill system to be ready
        yield return new WaitForSeconds(0.1f);
        
        UpdateAgentStatuses();
    }
    
    void UpdateAgentStatuses()
    {
        if (skillSystem == null || !skillSystem.IsDataReady())
        {
            Debug.LogWarning("AgentStatusBoard: Skill system not ready");
            return;
        }
        
        if (statusBoardPanel == null || agentListParent == null)
        {
            Debug.LogWarning("AgentStatusBoard: Status board UI not ready");
            return;
        }
        
        // Get all agents from the skill system
        var agentProfiles = skillSystem.GetAllAgentProfiles();
        if (agentProfiles == null)
        {
            Debug.LogWarning("AgentStatusBoard: No agent profiles available");
            return;
        }
        
        // Create or update agent status panels
        foreach (var agent in agentProfiles)
        {
            string agentId = agent.Key;
            string displayName = GetAgentDisplayName(agentId);
            
            // Create panel if it doesn't exist
            if (!agentStatusPanels.ContainsKey(agentId))
            {
                CreateAgentStatusPanel(agentId, displayName);
            }
            
            // Update existing panel
            UpdateAgentStatusPanel(agentId, agent.Value);
        }
        
        // Remove panels for agents that no longer exist
        var agentsToRemove = agentStatusPanels.Keys.Where(id => !agentProfiles.ContainsKey(id)).ToList();
        foreach (var agentId in agentsToRemove)
        {
            if (agentStatusPanels.ContainsKey(agentId))
            {
                Destroy(agentStatusPanels[agentId]);
                agentStatusPanels.Remove(agentId);
            }
        }
    }
    
    void CreateAgentStatusPanel(string agentId, string displayName)
    {
        if (agentListParent == null)
        {
            Debug.LogError("AgentStatusBoard: agentListParent is null, cannot create agent panel");
            return;
        }
        
        GameObject panel = new GameObject($"AgentStatus_{agentId}");
        panel.transform.SetParent(agentListParent, false);
        
        // Add background
        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.1f, 0.1f, 0.1f, 0.7f);
        
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.sizeDelta = new Vector2(0, 80); // Fixed height
        
        // Create agent name text
        GameObject nameGO = new GameObject("AgentName");
        nameGO.transform.SetParent(panel.transform, false);
        
        Text nameText = nameGO.AddComponent<Text>();
        nameText.text = displayName;
        nameText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        nameText.fontSize = 14;
        nameText.color = textColor;
        nameText.fontStyle = FontStyle.Bold;
        nameText.alignment = TextAnchor.MiddleLeft;
        
        RectTransform nameRect = nameText.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0, 0.6f);
        nameRect.anchorMax = new Vector2(1, 1);
        nameRect.offsetMin = new Vector2(10, 0);
        nameRect.offsetMax = new Vector2(-10, 0);
        
        // Create skill level text
        GameObject skillGO = new GameObject("SkillLevel");
        skillGO.transform.SetParent(panel.transform, false);
        
        Text skillText = skillGO.AddComponent<Text>();
        skillText.text = "Skill: Loading...";
        skillText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        skillText.fontSize = 12;
        skillText.color = textColor;
        skillText.alignment = TextAnchor.MiddleLeft;
        
        RectTransform skillRect = skillText.GetComponent<RectTransform>();
        skillRect.anchorMin = new Vector2(0, 0.3f);
        skillRect.anchorMax = new Vector2(0.5f, 0.6f);
        skillRect.offsetMin = new Vector2(10, 0);
        skillRect.offsetMax = new Vector2(-5, 0);
        
        // Create status text
        GameObject statusGO = new GameObject("Status");
        statusGO.transform.SetParent(panel.transform, false);
        
        Text statusText = statusGO.AddComponent<Text>();
        statusText.text = "Status: Idle";
        statusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        statusText.fontSize = 12;
        statusText.color = textColor;
        statusText.alignment = TextAnchor.MiddleLeft;
        
        RectTransform statusRect = statusText.GetComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(0.5f, 0.3f);
        statusRect.anchorMax = new Vector2(1, 0.6f);
        statusRect.offsetMin = new Vector2(5, 0);
        statusRect.offsetMax = new Vector2(-10, 0);
        
        // Create skills learned text
        GameObject skillsGO = new GameObject("SkillsLearned");
        skillsGO.transform.SetParent(panel.transform, false);
        
        Text skillsText = skillsGO.AddComponent<Text>();
        skillsText.text = "Skills: None";
        skillsText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        skillsText.fontSize = 10;
        skillsText.color = textColor;
        skillsText.alignment = TextAnchor.MiddleLeft;
        
        RectTransform skillsRect = skillsText.GetComponent<RectTransform>();
        skillsRect.anchorMin = new Vector2(0, 0);
        skillsRect.anchorMax = new Vector2(1, 0.3f);
        skillsRect.offsetMin = new Vector2(10, 0);
        skillsRect.offsetMax = new Vector2(-10, 0);
        
        // Store references for updating
        AgentStatusPanelData panelData = panel.AddComponent<AgentStatusPanelData>();
        panelData.Initialize(nameText, skillText, statusText, skillsText);
        
        agentStatusPanels[agentId] = panel;
        
        Debug.Log($"AgentStatusBoard: Created status panel for {displayName}");
    }
    
    void UpdateAgentStatusPanel(string agentId, AgentProfile agentProfile)
    {
        if (!agentStatusPanels.ContainsKey(agentId))
            return;
            
        GameObject panel = agentStatusPanels[agentId];
        if (panel == null)
            return;
            
        AgentStatusPanelData panelData = panel.GetComponent<AgentStatusPanelData>();
        
        if (panelData == null)
        {
            Debug.LogWarning($"AgentStatusBoard: PanelData is null for agent {agentId}");
            return;
        }
        
        if (skillSystem == null)
        {
            Debug.LogWarning("AgentStatusBoard: SkillSystem is null, cannot update panel");
            return;
        }
        
        // Update skill level
        float skillLevel = skillSystem.GetAgentSkillLevel(agentId);
        panelData.skillLevelText.text = $"Skill: {skillLevel:F1}";
        
        // Color code skill level
        if (skillLevel >= 4.0f)
            panelData.skillLevelText.color = positiveColor;
        else if (skillLevel >= 2.5f)
            panelData.skillLevelText.color = neutralColor;
        else
            panelData.skillLevelText.color = negativeColor;
        
        // Update status (this would need to be integrated with the proximity system)
        string status = GetAgentCurrentStatus(agentId);
        panelData.statusText.text = $"Status: {status}";
        
        // Update skills learned
        string skillsLearned = skillSystem.GetAgentAvailableSkillsString(agentId);
        if (string.IsNullOrEmpty(skillsLearned) || skillsLearned == "No skills learned")
        {
            panelData.skillsLearnedText.text = "Skills: None";
            panelData.skillsLearnedText.color = textColor;
        }
        else
        {
            panelData.skillsLearnedText.text = $"Skills: {skillsLearned}";
            panelData.skillsLearnedText.color = positiveColor;
        }
    }
    
    string GetAgentCurrentStatus(string agentId)
    {
        // This would need to be integrated with the AgentProximity system
        // For now, return a placeholder
        return "Idle";
    }
    
    /// <summary>
    /// Convert agent ID to display name format (e.g., SIMPLE_Supervisor_01 -> supervisor001)
    /// </summary>
    string GetAgentDisplayName(string agentId)
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
    
    /// <summary>
    /// Public method to update agent status from external systems
    /// </summary>
    public void UpdateAgentStatus(string agentId, string status, Color statusColor)
    {
        if (!agentStatusPanels.ContainsKey(agentId))
            return;
            
        GameObject panel = agentStatusPanels[agentId];
        AgentStatusPanelData panelData = panel.GetComponent<AgentStatusPanelData>();
        
        if (panelData != null)
        {
            panelData.statusText.text = $"Status: {status}";
            panelData.statusText.color = statusColor;
        }
    }
}

/// <summary>
/// Helper class to store references to UI elements for each agent status panel
/// </summary>
public class AgentStatusPanelData : MonoBehaviour
{
    public Text agentNameText;
    public Text skillLevelText;
    public Text statusText;
    public Text skillsLearnedText;
    
    public void Initialize(Text name, Text skill, Text status, Text skills)
    {
        agentNameText = name;
        skillLevelText = skill;
        statusText = status;
        skillsLearnedText = skills;
    }
}
