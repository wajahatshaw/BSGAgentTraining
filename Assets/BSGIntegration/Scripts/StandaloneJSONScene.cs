using UnityEngine;
using UnityEngine.UI;
using System.IO;

public class StandaloneJSONScene : MonoBehaviour
{
    [Header("UI Elements")]
    public Text workerText;
    public Text taskText;
    public Text detailsText;
    public Text progressText;
    
    [Header("JSON File")]
    public string jsonFileName = "basicUi.json";
    
    private SceneData sceneData;
    private int currentStepIndex = 0;
    
    void Start()
    {
        Debug.Log("=== STANDALONE JSON SCENE STARTING ===");
        LoadJSONData();
        // CreateSimpleUI(); // Disabled UI panels
        CreateEntitiesFromJSON();
        // UpdateUI(); // Disabled UI updates
        
        // Initialize proximity system integration
        InitializeProximityIntegration();
        
        Debug.Log("=== SCENE SETUP COMPLETE ===");
    }
    
    void LoadJSONData()
    {
        try
        {
            string jsonPath = Path.Combine(Application.dataPath, "JsonFile", jsonFileName);
            if (File.Exists(jsonPath))
            {
                string json = File.ReadAllText(jsonPath);
                sceneData = JsonUtility.FromJson<SceneData>(json);
                Debug.Log($"JSON loaded: {sceneData.scene_id}");
            }
            else
            {
                Debug.LogError($"JSON file not found at: {jsonPath}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error loading JSON: {e.Message}");
        }
    }
    
    void CreateSimpleUI()
    {
        // Create Canvas
        GameObject canvasGO = new GameObject("Simple_UI_Canvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        
        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        
        canvasGO.AddComponent<GraphicRaycaster>();
        
        // Create UI panels
        CreateUIPanel(canvas, "Worker_Panel", new Vector2(-400, 200), "WORKER", ref workerText);
        CreateUIPanel(canvas, "Task_Panel", new Vector2(400, 200), "TASK", ref taskText);
        CreateUIPanel(canvas, "Details_Panel", new Vector2(0, -200), "DETAILS", ref detailsText);
        CreateUIPanel(canvas, "Progress_Panel", new Vector2(0, 100), "PROGRESS", ref progressText);
    }
    
    void CreateUIPanel(Canvas canvas, string name, Vector2 position, string title, ref Text contentText)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(canvas.transform, false);
        
        // Background
        Image bg = panel.AddComponent<Image>();
        bg.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(250, 150);
        
        // Title
        CreateText(panel, "Title", title, new Vector2(0, 50), 18, Color.white, true);
        
        // Content text
        GameObject textGO = new GameObject("Content");
        textGO.transform.SetParent(panel.transform, false);
        
        contentText = textGO.AddComponent<Text>();
        contentText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        contentText.fontSize = 14;
        contentText.color = new Color(0.7f, 0.7f, 0.7f);
        contentText.alignment = TextAnchor.MiddleCenter;
        
        RectTransform textRect = contentText.GetComponent<RectTransform>();
        textRect.anchoredPosition = new Vector2(0, 0);
        textRect.sizeDelta = new Vector2(230, 60);
    }
    
    void CreateText(GameObject parent, string name, string text, Vector2 position, int fontSize, Color color, bool bold = false)
    {
        GameObject textGO = new GameObject(name);
        textGO.transform.SetParent(parent.transform, false);
        
        Text textComponent = textGO.AddComponent<Text>();
        textComponent.text = text;
        textComponent.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        textComponent.fontSize = fontSize;
        textComponent.color = color;
        textComponent.alignment = TextAnchor.MiddleCenter;
        
        if (bold)
        {
            textComponent.fontStyle = FontStyle.Bold;
        }
        
        RectTransform rect = textComponent.GetComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(230, fontSize + 10);
    }
    
    void UpdateUI()
    {
        if (sceneData == null) return;
        
        // Update Worker
        if (workerText != null && sceneData.sequence != null && sceneData.sequence.Length > 0)
        {
            string worker = sceneData.sequence[currentStepIndex].person;
            workerText.text = $"Worker: {worker}";
        }
        
        // Update Task
        if (taskText != null && sceneData.sequence != null && sceneData.sequence.Length > 0)
        {
            var step = sceneData.sequence[currentStepIndex];
            taskText.text = $"Task: {step.stepId}\nAction: {step.actionVerb}\nTool: {step.tool}";
        }
        
        // Update Details
        if (detailsText != null && sceneData.sequence != null && sceneData.sequence.Length > 0)
        {
            var step = sceneData.sequence[currentStepIndex];
            string details = $"Duration: {step.estimatedDuration} min\nType: {step.actionType}";
            
            if (step.requiredSkills != null && step.requiredSkills.Length > 0)
            {
                details += $"\nSkills: {step.requiredSkills[0].skillName}";
            }
            
            detailsText.text = details;
        }
        
        // Update Progress
        if (progressText != null && sceneData.sequence != null)
        {
            progressText.text = $"Step {currentStepIndex + 1} of {sceneData.sequence.Length}";
        }
    }
    
    void Update()
    {
        // Note: Input disabled due to Input System conflict
        // Use context menu or inspector buttons instead
    }
    
    [ContextMenu("Next Step")]
    void NextStepMenu()
    {
        NextStep();
    }
    
    [ContextMenu("Previous Step")]
    void PreviousStepMenu()
    {
        PreviousStep();
    }
    
    [ContextMenu("Execute Step")]
    void ExecuteStepMenu()
    {
        ExecuteStep();
    }
    
    void NextStep()
    {
        if (sceneData?.sequence != null && currentStepIndex < sceneData.sequence.Length - 1)
        {
            currentStepIndex++;
            UpdateUI();
        }
    }
    
    void PreviousStep()
    {
        if (currentStepIndex > 0)
        {
            currentStepIndex--;
            UpdateUI();
        }
    }
    
    void ExecuteStep()
    {
        if (sceneData?.sequence != null && currentStepIndex < sceneData.sequence.Length)
        {
            var step = sceneData.sequence[currentStepIndex];
            Debug.Log($"Executing: {step.stepId} - {step.actionVerb} on {step.tool}");
        }
    }
    
    void CreateEntitiesFromJSON()
    {
        Debug.Log("Creating entities from JSON...");
        
        if (sceneData == null)
        {
            Debug.LogWarning("No JSON data available, creating demo entities");
            CreateDemoEntities();
            return;
        }
        
        // CreateEnvironment(); // Disabled JSON walls and floor
        CreateToolsFromJSON();
        CreateAgentsFromJSON();
        
        Debug.Log("Entities creation complete");
    }
    
    void CreateEnvironment()
    {
        Debug.Log("Creating environment...");
        
        // Create floor
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "JSON_Floor";
        floor.transform.position = Vector3.zero;
        floor.transform.localScale = Vector3.one * 2f;
        floor.GetComponent<Renderer>().material.color = new Color(0.3f, 0.3f, 0.3f);
        
        // Create simple walls
        CreateWall("JSON_Wall_North", new Vector3(0, 2, 10), new Vector3(20, 4, 1), Color.blue);
        CreateWall("JSON_Wall_South", new Vector3(0, 2, -10), new Vector3(20, 4, 1), Color.blue);
        CreateWall("JSON_Wall_East", new Vector3(10, 2, 0), new Vector3(1, 4, 20), Color.blue);
        CreateWall("JSON_Wall_West", new Vector3(-10, 2, 0), new Vector3(1, 4, 20), Color.blue);
    }
    
    void CreateWall(string name, Vector3 position, Vector3 scale, Color color)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.position = position;
        wall.transform.localScale = scale;
        wall.GetComponent<Renderer>().material.color = color;
    }
    
    void CreateToolsFromJSON()
    {
        if (sceneData?.initialStates == null)
        {
            Debug.LogWarning("No tools data in JSON");
            return;
        }
        
        Debug.Log($"Creating {sceneData.initialStates.Count} tools from JSON");
        
        int toolIndex = 0;
        foreach (var toolEntry in sceneData.initialStates)
        {
            string toolId = toolEntry.Key;
            ToolState toolState = toolEntry.Value;
            
            CreateToolEntity(toolId, toolState, toolIndex);
            toolIndex++;
        }
    }
    
    void CreateToolEntity(string toolId, ToolState toolState, int index)
    {
        // Create tool GameObject with different shapes based on type
        GameObject tool = CreateToolByType(toolState);
        tool.name = $"JSON_Tool_{toolId}";
        
        // Use position from JSON if available, otherwise use index-based positioning
        Vector3 position;
        if (HasPosition(toolState))
        {
            position = GetPositionFromJSON(toolState);
        }
        else
        {
            position = new Vector3(-4f + (index * 4f), 0.5f, -5f);
        }
        tool.transform.position = position;
        
        // Set color based on tool state and JSON color
        Color toolColor = GetToolColorFromJSON(toolState);
        tool.GetComponent<Renderer>().material.color = toolColor;
        
        // Add highlight sphere
        GameObject highlight = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        highlight.name = "Highlight";
        highlight.transform.SetParent(tool.transform);
        highlight.transform.localPosition = Vector3.up * 2f;
        highlight.transform.localScale = Vector3.one * 0.3f;
        highlight.GetComponent<Renderer>().material.color = Color.yellow;
        
        // Add light
        Light light = highlight.AddComponent<Light>();
        light.color = Color.yellow;
        light.intensity = 2f;
        light.range = 3f;
        
        // Add name label
        CreateNameLabel(tool, GetToolName(toolState));
        
        Debug.Log($"Created tool {toolId} ({GetToolName(toolState)}) at position {position} with color {toolColor}");
    }
    
    GameObject CreateToolByType(ToolState toolState)
    {
        // Create different shapes based on tool type
        string toolType = GetToolType(toolState);
        
        switch (toolType.ToLower())
        {
            case "motor":
                return GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            case "storage":
                return GameObject.CreatePrimitive(PrimitiveType.Cube);
            case "machinery":
                GameObject machinery = GameObject.CreatePrimitive(PrimitiveType.Cube);
                machinery.transform.localScale = new Vector3(1.5f, 1f, 1.5f);
                return machinery;
            case "workstation":
                GameObject workstation = GameObject.CreatePrimitive(PrimitiveType.Cube);
                workstation.transform.localScale = new Vector3(2f, 0.5f, 1f);
                return workstation;
            default:
                return GameObject.CreatePrimitive(PrimitiveType.Cube);
        }
    }
    
    bool HasPosition(ToolState toolState)
    {
        // Check if position data exists in JSON (this would need to be added to ToolState class)
        return false; // Simplified for now
    }
    
    Vector3 GetPositionFromJSON(ToolState toolState)
    {
        // Extract position from JSON (this would need to be added to ToolState class)
        return Vector3.zero; // Simplified for now
    }
    
    string GetToolType(ToolState toolState)
    {
        // Extract tool type from JSON (this would need to be added to ToolState class)
        return "default"; // Simplified for now
    }
    
    string GetToolName(ToolState toolState)
    {
        // Extract tool name from JSON (this would need to be added to ToolState class)
        return toolState.objectId; // Simplified for now
    }
    
    void CreateNameLabel(GameObject parent, string name)
    {
        // Create a simple text label above the object
        GameObject labelGO = new GameObject("NameLabel");
        labelGO.transform.SetParent(parent.transform);
        labelGO.transform.localPosition = Vector3.up * 3f;
        
        // Add TextMesh for 3D text
        TextMesh textMesh = labelGO.AddComponent<TextMesh>();
        textMesh.text = name;
        textMesh.fontSize = 20;
        textMesh.color = Color.white;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        
        // Make text face camera
        labelGO.transform.rotation = Quaternion.LookRotation(Camera.main.transform.forward);
    }
    
    Color GetToolColor(ToolState toolState)
    {
        if (toolState.properties != null)
        {
            if (toolState.properties.power == "on")
                return Color.green;
            if (toolState.properties.power == "off")
                return Color.red;
            if (toolState.properties.locked)
                return Color.yellow;
        }
        
        if (!toolState.isAvailable)
            return Color.gray;
            
        return Color.white;
    }
    
    Color GetToolColorFromJSON(ToolState toolState)
    {
        // Enhanced color system that considers both state and JSON color specification
        Color baseColor = GetToolColor(toolState); // Get state-based color
        
        // Override with JSON color if available (this would need to be added to ToolState class)
        // For now, we'll use a mapping based on tool ID
        string toolId = toolState.objectId;
        
        switch (toolId)
        {
            case "tool_001": return Color.red;      // Industrial Motor Unit
            case "tool_002": return Color.yellow;   // Secure Toolbox Alpha
            case "tool_003": return Color.blue;     // Hydraulic Lift Station
            case "tool_004": return Color.green;    // Safety Inspection Station
            case "workbench_001": return new Color(1f, 0.5f, 0f); // Orange for workbench
            default: return baseColor;
        }
    }
    
    void CreateAgentsFromJSON()
    {
        if (sceneData?.agentProfiles == null)
        {
            Debug.LogWarning("No agents data in JSON");
            return;
        }
        
        Debug.Log($"Creating {sceneData.agentProfiles.Count} agents from JSON");
        
        int agentIndex = 0;
        foreach (var agentEntry in sceneData.agentProfiles)
        {
            string agentId = agentEntry.Key;
            AgentProfile agentProfile = agentEntry.Value;
            
            CreateAgentEntity(agentId, agentProfile, agentIndex);
            agentIndex++;
        }
    }
    
    void CreateAgentEntity(string agentId, AgentProfile agentProfile, int index)
    {
        // Create the technician like in the 3rd image
        GameObject agent = CreateTechnicianModel(agentId);
        agent.name = $"Technician_{agentId}";
        
        // Position agents using enhanced positioning
        Vector3 position = GetAgentPosition(agentId, index);
        
        // Create white plan platform for technician to stand on
        GameObject planPlatform = CreatePlanPlatform(position, agentId);
        
        // Position agent on the platform
        agent.transform.position = new Vector3(position.x, position.y + 0.5f, position.z);
        
        // Set color based on agent role and JSON specification
        Color agentColor = GetAgentColorFromJSON(agentId, agentProfile);
        
        // Apply color to all body parts
        Renderer[] renderers = agent.GetComponentsInChildren<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            if (renderer.gameObject.name == "Body" || renderer.gameObject.name == "Head")
            {
                renderer.material.color = agentColor;
            }
            else if (renderer.gameObject.name.Contains("Arm") || renderer.gameObject.name.Contains("Leg"))
            {
                renderer.material.color = Color.Lerp(agentColor, Color.gray, 0.3f); // Slightly darker for limbs
            }
        }
        
        // Add interactive functionality
        AddTechnicianFunctionality(agent, agentId, agentProfile);
        
        // Add name label with role information
        string displayName = GetAgentDisplayName(agentId);
        CreateNameLabel(agent, displayName);
        
        Debug.Log($"Created technician {agentId} ({displayName}) at position {position} with color {agentColor}");
        Debug.Log($"Created plan platform for {agentId} at {position}");
    }
    
    GameObject CreateTechnicianModel(string agentId)
    {
        // Create a more detailed technician model like in the 3rd image
        GameObject technician = new GameObject($"Technician_{agentId}");
        
        // Main body (capsule)
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(technician.transform);
        body.transform.localPosition = Vector3.zero;
        body.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
        
        // Head (sphere)
        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        head.transform.SetParent(technician.transform);
        head.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        head.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
        
        // Arms (cylinders)
        GameObject leftArm = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        leftArm.name = "LeftArm";
        leftArm.transform.SetParent(technician.transform);
        leftArm.transform.localPosition = new Vector3(-0.7f, 0.3f, 0f);
        leftArm.transform.localRotation = Quaternion.Euler(0, 0, 90);
        leftArm.transform.localScale = new Vector3(0.2f, 0.4f, 0.2f);
        
        GameObject rightArm = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rightArm.name = "RightArm";
        rightArm.transform.SetParent(technician.transform);
        rightArm.transform.localPosition = new Vector3(0.7f, 0.3f, 0f);
        rightArm.transform.localRotation = Quaternion.Euler(0, 0, 90);
        rightArm.transform.localScale = new Vector3(0.2f, 0.4f, 0.2f);
        
        // Legs (cylinders)
        GameObject leftLeg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        leftLeg.name = "LeftLeg";
        leftLeg.transform.SetParent(technician.transform);
        leftLeg.transform.localPosition = new Vector3(-0.3f, -1.2f, 0f);
        leftLeg.transform.localScale = new Vector3(0.25f, 0.6f, 0.25f);
        
        GameObject rightLeg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rightLeg.name = "RightLeg";
        rightLeg.transform.SetParent(technician.transform);
        rightLeg.transform.localPosition = new Vector3(0.3f, -1.2f, 0f);
        rightLeg.transform.localScale = new Vector3(0.25f, 0.6f, 0.25f);
        
        return technician;
    }
    
    GameObject CreatePlanPlatform(Vector3 position, string agentId)
    {
        // Create white plan platform for technician to stand on
        GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
        platform.name = $"Plan_Platform_{agentId}";
        platform.transform.position = position;
        platform.transform.localScale = new Vector3(3f, 0.1f, 3f);
        
        // Make it white
        Renderer platformRenderer = platform.GetComponent<Renderer>();
        platformRenderer.material.color = Color.white;
        
        // Add plan details text on the platform
        CreatePlanText(platform, agentId);
        
        return platform;
    }
    
    void CreatePlanText(GameObject platform, string agentId)
    {
        // Create plan text above the platform
        GameObject planTextGO = new GameObject("PlanText");
        planTextGO.transform.SetParent(platform.transform);
        planTextGO.transform.localPosition = new Vector3(0f, 1f, 0f);
        
        TextMesh planText = planTextGO.AddComponent<TextMesh>();
        planText.text = GetPlanText(agentId);
        planText.fontSize = 12;
        planText.color = Color.black;
        planText.anchor = TextAnchor.MiddleCenter;
        planText.alignment = TextAlignment.Center;
        
        // Make text face camera
        planTextGO.transform.rotation = Quaternion.LookRotation(Camera.main.transform.forward);
    }
    
    string GetPlanText(string agentId)
    {
        switch (agentId)
        {
            case "agent_technician_A":
                return "MAINTENANCE PLAN\n• Motor Repair\n• Toolbox Access\n• Final Assembly";
            case "agent_supervisor_B":
                return "SUPERVISION PLAN\n• Hydraulic Check\n• Quality Control\n• Team Coordination";
            case "agent_inspector_C":
                return "INSPECTION PLAN\n• Safety Compliance\n• Risk Assessment\n• Documentation";
            default:
                return "WORK PLAN\n• Task Assignment\n• Progress Monitoring";
        }
    }
    
    void AddTechnicianFunctionality(GameObject technician, string agentId, AgentProfile agentProfile)
    {
        // Add interactive component
        TechnicianController controller = technician.AddComponent<TechnicianController>();
        controller.agentId = agentId;
        controller.agentProfile = agentProfile;
        
        // Add collider for interaction
        BoxCollider collider = technician.AddComponent<BoxCollider>();
        collider.size = new Vector3(2f, 3f, 2f);
        collider.isTrigger = true;
        
        // Add rigidbody for physics
        Rigidbody rb = technician.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = true;
    }
    
    Vector3 GetAgentPosition(string agentId, int index)
    {
        // Position agents based on their roles and JSON specifications
        switch (agentId)
        {
            case "agent_technician_A":
                return new Vector3(-6f, 1f, 0f);
            case "agent_supervisor_B":
                return new Vector3(0f, 1f, 6f);
            case "agent_inspector_C":
                return new Vector3(6f, 1f, 0f);
            default:
                // Fallback to circle positioning
                float angle = index * 120f;
                return new Vector3(
                    Mathf.Cos(angle * Mathf.Deg2Rad) * 6f,
                    1f,
                    Mathf.Sin(angle * Mathf.Deg2Rad) * 6f
                );
        }
    }
    
    Color GetAgentColorFromJSON(string agentId, AgentProfile agentProfile)
    {
        // Color agents based on their roles as specified in JSON
        switch (agentId)
        {
            case "agent_technician_A":
                return Color.green;     // Senior Technician - Green
            case "agent_supervisor_B":
                return Color.blue;      // Operations Supervisor - Blue
            case "agent_inspector_C":
                return Color.magenta;   // Safety Inspector - Purple/Magenta
            default:
                return GetAgentColor(agentProfile); // Fallback to skill-based color
        }
    }
    
    Color GetAgentHighlightColor(string agentId)
    {
        // Different highlight colors for different roles
        switch (agentId)
        {
            case "agent_technician_A":
                return Color.cyan;
            case "agent_supervisor_B":
                return Color.white;
            case "agent_inspector_C":
                return Color.yellow;
            default:
                return Color.cyan;
        }
    }
    
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
    
    Color GetAgentColor(AgentProfile agentProfile)
    {
        if (agentProfile?.onetSkillLevels == null)
            return Color.white;
            
        float avgSkill = 0f;
        int skillCount = 0;
        
        foreach (var skill in agentProfile.onetSkillLevels.Values)
        {
            avgSkill += skill;
            skillCount++;
        }
        
        if (skillCount > 0)
        {
            avgSkill /= skillCount;
            return Color.Lerp(Color.black, Color.green, avgSkill / 5f);
        }
        
        return Color.white;
    }
    
    void CreateDemoEntities()
    {
        Debug.Log("Creating demo entities...");
        
        // Create demo environment
        CreateEnvironment();
        
        // Create demo tools
        CreateDemoTool("demo_repair_tool", new Vector3(-4, 0.5f, -5), Color.red);
        CreateDemoTool("demo_toolbox", new Vector3(0, 0.5f, -5), Color.yellow);
        CreateDemoTool("demo_machine", new Vector3(4, 0.5f, -5), Color.blue);
        
        // Create demo agents
        CreateDemoAgent("demo_technician_A", new Vector3(-6, 1, 0), Color.green);
        CreateDemoAgent("demo_technician_B", new Vector3(6, 1, 0), Color.cyan);
    }
    
    void CreateDemoTool(string name, Vector3 position, Color color)
    {
        GameObject tool = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tool.name = name;
        tool.transform.position = position;
        tool.GetComponent<Renderer>().material.color = color;
        
        // Add highlight
        GameObject highlight = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        highlight.transform.SetParent(tool.transform);
        highlight.transform.localPosition = Vector3.up * 2f;
        highlight.transform.localScale = Vector3.one * 0.3f;
        highlight.GetComponent<Renderer>().material.color = Color.yellow;
        
        Light light = highlight.AddComponent<Light>();
        light.color = Color.yellow;
        light.intensity = 2f;
        light.range = 3f;
    }
    
    void CreateDemoAgent(string name, Vector3 position, Color color)
    {
        GameObject agent = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        agent.name = name;
        agent.transform.position = position;
        agent.GetComponent<Renderer>().material.color = color;
        
        // Add highlight
        GameObject highlight = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        highlight.transform.SetParent(agent.transform);
        highlight.transform.localPosition = Vector3.up * 2f;
        highlight.transform.localScale = Vector3.one * 0.3f;
        highlight.GetComponent<Renderer>().material.color = Color.cyan;
        
        Light light = highlight.AddComponent<Light>();
        light.color = Color.cyan;
        light.intensity = 2f;
        light.range = 3f;
    }
    
    void InitializeProximityIntegration()
    {
        Debug.Log("Initializing proximity system integration...");
        
        // Find or create proximity integration component
        ProximityIntegration proximityIntegration = FindObjectOfType<ProximityIntegration>();
        if (proximityIntegration == null)
        {
            GameObject integrationObj = new GameObject("ProximityIntegration");
            proximityIntegration = integrationObj.AddComponent<ProximityIntegration>();
            Debug.Log("Created ProximityIntegration component");
        }
        
        // Trigger integration
        proximityIntegration.OnEntitiesCreated();
        
        Debug.Log("Proximity integration initialized");
    }
}
