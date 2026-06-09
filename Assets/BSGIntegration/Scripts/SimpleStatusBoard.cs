using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class SimpleStatusBoard : MonoBehaviour
{
    [Header("Status Board Settings")]
    public Vector3 boardPosition = new Vector3(0, 14, 0); // Moved upward from 8 to 14
    public float updateInterval = 0.1f; // Update every 0.1 seconds for real-time reward updates
    public float fontSize = 1.2f; // Larger font size for readability
    public bool showBackground = false; // Disabled by default to avoid pink
    public bool enableConsoleLogs = true; // Log updates to Unity Console continuously

    [Header("Visual Settings")]
    public Color textColor = Color.white;
    public Color backgroundColor = new Color(0, 0, 0, 0.0f); // Fully transparent by default
    
    private SkillBasedActionSystem skillSystem;
    private GameObject statusBoardObject;
    private TextMesh statusText;
    private GameObject backgroundObject;
    private float lastUpdateTime = 0f;
    private Dictionary<string, float> lastLoggedSkillLevel = new Dictionary<string, float>(); // Track changes to avoid spam
    
    void Start()
    {
        Debug.Log("📊 SimpleStatusBoard: Starting...");
        
        // Find skill system
        skillSystem = FindObjectOfType<SkillBasedActionSystem>();
        if (skillSystem == null)
        {
            Debug.LogError("📊 SimpleStatusBoard: SkillBasedActionSystem not found!");
            return;
        }
        
        // Create the status board
        CreateStatusBoard();
        
        Debug.Log("📊 SimpleStatusBoard: Created successfully");
    }
    
    void Update()
    {
        // Update status board at regular intervals
        if (Time.time - lastUpdateTime >= updateInterval)
        {
            UpdateStatusDisplay();
            lastUpdateTime = Time.time;
        }
        
        // Ensure the status board is always facing the camera and properly aligned
        if (statusBoardObject != null)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                // Make it face the camera properly without mirroring
                Vector3 lookDirection = mainCamera.transform.position - statusBoardObject.transform.position;
                lookDirection.y = 0; // Keep it horizontal, no vertical rotation
                if (lookDirection != Vector3.zero)
                {
                    statusBoardObject.transform.rotation = Quaternion.LookRotation(lookDirection);
                    // Add 180 degrees rotation to fix mirroring
                    statusBoardObject.transform.Rotate(0, 180, 0);
                }
            }
            
            // Lock position to prevent it from moving
            if (Vector3.Distance(statusBoardObject.transform.position, boardPosition) > 0.1f)
            {
                statusBoardObject.transform.position = boardPosition;
            }
        }
    }
    
    void CreateStatusBoard()
    {
        // Create main status board object
        statusBoardObject = new GameObject("SimpleStatusBoard");
        statusBoardObject.transform.position = boardPosition;
        
        // Create background only if enabled and not transparent
        if (showBackground && backgroundColor.a > 0.01f)
        {
            backgroundObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            backgroundObject.name = "StatusBoardBackground";
            backgroundObject.transform.SetParent(statusBoardObject.transform);
            backgroundObject.transform.localPosition = Vector3.zero;
            backgroundObject.transform.localScale = new Vector3(12, 4, 0.1f); // Larger background for normal font
            
            // Set background color with proper shader - try multiple shaders
            Renderer bgRenderer = backgroundObject.GetComponent<Renderer>();
            Material bgMaterial = new Material(Shader.Find("Standard"));
            if (bgMaterial.shader == null)
            {
                bgMaterial = new Material(Shader.Find("Legacy Shaders/Diffuse"));
            }
            if (bgMaterial.shader == null)
            {
                bgMaterial = new Material(Shader.Find("Unlit/Color"));
            }
            if (bgMaterial.shader == null)
            {
                bgMaterial = new Material(Shader.Find("Sprites/Default"));
            }
            bgMaterial.color = backgroundColor;
            bgRenderer.material = bgMaterial;
            
            // Remove collider from background
            Collider bgCollider = backgroundObject.GetComponent<Collider>();
            if (bgCollider != null)
            {
                DestroyImmediate(bgCollider);
            }
        }
        else
        {
            Debug.Log("📊 SimpleStatusBoard: Background disabled or transparent - no background created");
        }
        
        // Create text
        statusText = statusBoardObject.AddComponent<TextMesh>();
        statusText.text = "Loading...";
        statusText.fontSize = Mathf.RoundToInt(fontSize * 12); // Larger font size for readability
        statusText.color = textColor;
        statusText.anchor = TextAnchor.MiddleCenter;
        statusText.alignment = TextAlignment.Center;
        statusText.fontStyle = FontStyle.Normal; // Normal style for clarity
        statusText.characterSize = 0.2f; // Larger character size for readability
        statusText.lineSpacing = 1.2f; // Better line spacing
        statusText.offsetZ = 2.5f; // Set offsetZ to 2.5 as requested
        
        // Position text in center of background
        statusText.transform.localPosition = new Vector3(0, 0, -0.1f);
        
        Debug.Log("📊 SimpleStatusBoard: Status board created at position " + boardPosition);
    }
    
    void UpdateStatusDisplay()
    {
        if (skillSystem == null || !skillSystem.IsDataReady())
        {
            statusText.text = "Skill System Not Ready";
            return;
        }
        
        var agentProfiles = skillSystem.GetAllAgentProfiles();
        if (agentProfiles == null || agentProfiles.Count == 0)
        {
            statusText.text = "No Agents Found";
            return;
        }
        
        // Separate supervisors and technicians into two lines
        List<string> supervisors = new List<string>();
        List<string> technicians = new List<string>();
        
        foreach (var agent in agentProfiles)
        {
            string agentId = agent.Key;
            string displayName = GetAgentDisplayName(agentId);
            float skillLevel = skillSystem.GetAgentSkillLevel(agentId);
            int skillsCount = skillSystem.GetAgentAvailableSkillsCount(agentId);
            
            string agentInfo = $"{displayName}(S:{skillLevel:F1})";
            
            if (displayName.StartsWith("supervisor"))
            {
                supervisors.Add(agentInfo);
            }
            else if (displayName.StartsWith("technician"))
            {
                technicians.Add(agentInfo);
            }
        }
        
        // Create two-line display
        string statusDisplay = "AGENT STATUS:\n";
        
        if (supervisors.Count > 0)
        {
            statusDisplay += "SUPERVISORS: " + string.Join(" ", supervisors) + "\n";
        }
        
        if (technicians.Count > 0)
        {
            statusDisplay += "TECHNICIANS: " + string.Join(" ", technicians);
        }
        
        statusText.text = statusDisplay;
        
        // Continuous console logging when enabled (only when skill levels change significantly)
        if (enableConsoleLogs)
        {
            bool hasChanges = false;
            string consoleOutput = $"[STATUS-BOARD] Updated (from ML-Agents learning):\n";
            
            foreach (var agent in agentProfiles)
            {
                string agentId = agent.Key;
                string displayName = GetAgentDisplayName(agentId);
                float skillLevel = skillSystem.GetAgentSkillLevel(agentId);
                float desireLevel = agent.Value.desireLevel;
                float percentage = (skillLevel / desireLevel) * 100f;
                int skillsCount = skillSystem.GetAgentAvailableSkillsCount(agentId);
                
                // Check if skill level changed significantly (more than 0.1 points)
                if (!lastLoggedSkillLevel.ContainsKey(agentId))
                {
                    lastLoggedSkillLevel[agentId] = skillLevel;
                    hasChanges = true;
                }
                else if (Mathf.Abs(skillLevel - lastLoggedSkillLevel[agentId]) > 0.1f)
                {
                    lastLoggedSkillLevel[agentId] = skillLevel;
                    hasChanges = true;
                }
                
                consoleOutput += $"  {displayName}: Skill {skillLevel:F1}/{desireLevel:F1} ({percentage:F1}%) | Learned Skills: {skillsCount}\n";
            }
            
            // Only log if there are changes or it's been a while since last log
            if (hasChanges || Time.time - lastUpdateTime >= 2f)
            {
                Debug.Log(consoleOutput.TrimEnd());
            }
        }
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
        
        // Legacy support for old agent IDs
        switch (agentId)
        {
            case "agent_technician_A":
                return "technician001";
            case "agent_supervisor_B":
                return "supervisor001";
            case "agent_inspector_C":
                return "inspector001";
            default:
                return agentId;
        }
    }
    
    /// <summary>
    /// Public method to update agent status from external systems
    /// </summary>
    public void UpdateAgentStatus(string agentId, string status, Color statusColor)
    {
        // This method can be called by other systems to update specific agent status
        // For now, we'll just trigger a full update
        UpdateStatusDisplay();
    }
    
    [ContextMenu("Test Status Board")]
    public void TestStatusBoard()
    {
        Debug.Log("📊 SimpleStatusBoard: Manual test triggered");
        UpdateStatusDisplay();
    }
    
    [ContextMenu("Show Debug Info")]
    public void ShowDebugInfo()
    {
        Debug.Log($"📊 SimpleStatusBoard Debug Info:");
        Debug.Log($"  - Position: {transform.position}");
        Debug.Log($"  - Skill System: {(skillSystem != null ? "Found" : "Missing")}");
        Debug.Log($"  - Status Text: {(statusText != null ? "Found" : "Missing")}");
        Debug.Log($"  - Background: {(backgroundObject != null ? "Found" : "Missing")}");
        Debug.Log($"  - Show Background: {showBackground}");
        Debug.Log($"  - Font Size: {fontSize}");
        Debug.Log($"  - Current Text: {(statusText != null ? statusText.text : "No text")}");
    }
}
