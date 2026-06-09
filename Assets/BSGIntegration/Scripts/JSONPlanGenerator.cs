using UnityEngine;
using System.IO;
using System.Collections.Generic;

public class JSONPlanGenerator : MonoBehaviour
{
    [Header("JSON Scene Generation")]
    public bool generateOnStart = true;
    public string jsonFileName = "basicUi.json";
    
    private SceneData sceneData;
    
    void Start()
    {
        if (generateOnStart)
        {
            Debug.Log("🚀 JSON Plan Generator Starting...");
            LoadJSONAndCreateScene();
        }
    }
    
    void LoadJSONAndCreateScene()
    {
        // Load JSON data
        LoadJSONData();
        
        // Clear existing generated objects
        ClearGeneratedObjects();
        
        // Create plan surface from JSON
        CreatePlanSurface();
        
        // Create sphere technicians from JSON
        CreateSphereTechniciansFromJSON();
        
        // Create tools from JSON
        CreateToolsFromJSON();
        
        // Setup camera for optimal view
        SetupOptimalCamera();
        
        Debug.Log("✅ JSON Plan Scene Generated Successfully!");
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
                Debug.Log($"✅ JSON loaded: {sceneData.scene_id}");
                Debug.Log($"Plan: {sceneData.plan?.planName}");
                Debug.Log($"Agents: {sceneData.agentProfiles?.Count ?? 0}");
            }
            else
            {
                Debug.LogError($"❌ JSON file not found at: {jsonPath}");
                CreateFallbackScene();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Error loading JSON: {e.Message}");
            CreateFallbackScene();
        }
    }
    
    void ClearGeneratedObjects()
    {
        // Clear existing generated objects
        string[] prefixes = {"Plan_", "Technician_", "Tool_", "JSON_"};
        
        foreach (string prefix in prefixes)
        {
            GameObject[] objects = GameObject.FindObjectsOfType<GameObject>();
            foreach (GameObject obj in objects)
            {
                if (obj.name.StartsWith(prefix))
                {
                    DestroyImmediate(obj);
                }
            }
        }
        
        Debug.Log("🧹 Cleared existing generated objects");
    }
    
    void CreatePlanSurface()
    {
        Debug.Log("Creating plan surface from JSON...");
        
        // Get plan info from JSON
        string planName = sceneData?.plan?.planName ?? "Warehouse Maintenance Plan";
        string planDescription = sceneData?.plan?.description ?? "Daily Equipment Protocol";
        
        // Create main orange plan platform (like in your image)
        GameObject planSurface = GameObject.CreatePrimitive(PrimitiveType.Cube);
        planSurface.name = "Plan_Surface_Main";
        planSurface.transform.position = new Vector3(0, 0, 0);
        planSurface.transform.localScale = new Vector3(20f, 0.3f, 15f);
        
        // Make it orange like your image
        Renderer planRenderer = planSurface.GetComponent<Renderer>();
        planRenderer.material.color = new Color(1f, 0.6f, 0.2f); // Orange
        
        // Create plan border (orange outline)
        CreatePlanBorder();
        
        // Add plan text from JSON
        CreatePlanTextFromJSON(planName, planDescription);
        
        Debug.Log($"✅ Created plan surface: {planName}");
    }
    
    void CreatePlanBorder()
    {
        Color borderColor = new Color(1f, 0.4f, 0f); // Darker orange
        float borderHeight = 0.4f;
        
        // North border
        CreateBorderSegment("Plan_Border_North", new Vector3(0, borderHeight, 7.5f), 
                           new Vector3(20f, 0.2f, 0.3f), borderColor);
        
        // South border  
        CreateBorderSegment("Plan_Border_South", new Vector3(0, borderHeight, -7.5f), 
                           new Vector3(20f, 0.2f, 0.3f), borderColor);
        
        // East border
        CreateBorderSegment("Plan_Border_East", new Vector3(10f, borderHeight, 0), 
                           new Vector3(0.3f, 0.2f, 15f), borderColor);
        
        // West border
        CreateBorderSegment("Plan_Border_West", new Vector3(-10f, borderHeight, 0), 
                           new Vector3(0.3f, 0.2f, 15f), borderColor);
    }
    
    void CreateBorderSegment(string name, Vector3 position, Vector3 scale, Color color)
    {
        GameObject border = GameObject.CreatePrimitive(PrimitiveType.Cube);
        border.name = name;
        border.transform.position = position;
        border.transform.localScale = scale;
        border.GetComponent<Renderer>().material.color = color;
    }
    
    void CreatePlanTextFromJSON(string planName, string description)
    {
        GameObject planTextGO = new GameObject("Plan_Text_Main");
        planTextGO.transform.position = new Vector3(0, 0.8f, -5);
        
        TextMesh planText = planTextGO.AddComponent<TextMesh>();
        planText.text = $"{planName.ToUpper()}\n{description}";
        planText.fontSize = 16;
        planText.color = Color.white;
        planText.anchor = TextAnchor.MiddleCenter;
        planText.alignment = TextAlignment.Center;
        
        // Make text face up
        planTextGO.transform.rotation = Quaternion.Euler(90, 0, 0);
    }
    
    void CreateSphereTechniciansFromJSON()
    {
        Debug.Log("Creating sphere technicians from JSON...");
        
        if (sceneData?.agentProfiles == null)
        {
            Debug.LogWarning("No agent profiles found in JSON, creating fallback technicians");
            CreateFallbackTechnicians();
            return;
        }
        
        int index = 0;
        foreach (var agentKV in sceneData.agentProfiles)
        {
            string agentId = agentKV.Key;
            var agentProfile = agentKV.Value;
            
            CreateSphereTechnicianFromJSON(agentId, agentProfile, index);
            index++;
        }
        
        Debug.Log($"✅ Created {index} sphere technicians from JSON");
    }
    
    void CreateSphereTechnicianFromJSON(string agentId, AgentProfile agentProfile, int index)
    {
        // Get position from JSON or use default
        Vector3 position = GetAgentPositionFromJSON(agentId, index);
        
        // Get color from JSON or use default
        Color agentColor = GetAgentColorFromJSON(agentId);
        
        // Get name and role from JSON
        string agentName = agentProfile.name ?? agentId;
        string agentRole = agentProfile.role ?? "Technician";
        
        Debug.Log($"Creating sphere technician: {agentName} at {position}");
        
        // Create main sphere technician
        GameObject technician = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        technician.name = $"Technician_Sphere_{agentId}";
        technician.transform.position = position;
        technician.transform.localScale = Vector3.one * 1.8f; // Bigger sphere
        
        // Set color from JSON
        Renderer techRenderer = technician.GetComponent<Renderer>();
        techRenderer.material.color = agentColor;
        
        // Add smaller head sphere
        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        head.transform.SetParent(technician.transform);
        head.transform.localPosition = new Vector3(0, 1.1f, 0);
        head.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
        head.GetComponent<Renderer>().material.color = Color.Lerp(agentColor, Color.white, 0.4f);
        
        // Add name label from JSON
        CreateAgentNameLabel(technician, agentName, agentRole);
        
        // Add status indicator
        CreateStatusIndicator(technician, agentColor);
        
        // Add movement functionality
        JSONTechnicianMover mover = technician.AddComponent<JSONTechnicianMover>();
        mover.agentId = agentId;
        mover.agentName = agentName;
        mover.originalPosition = position;
        
        Debug.Log($"✅ Created sphere technician: {agentName} ({agentRole})");
    }
    
    Vector3 GetAgentPositionFromJSON(string agentId, int index)
    {
        // Try to get position from JSON
        if (sceneData?.agentProfiles != null && 
            sceneData.agentProfiles.ContainsKey(agentId) &&
            sceneData.agentProfiles[agentId].position != null)
        {
            var pos = sceneData.agentProfiles[agentId].position;
            return new Vector3(pos.x, pos.y + 0.5f, pos.z); // Slightly above ground
        }
        
        // Fallback to spread positions
        float spacing = 6f;
        return new Vector3(-6f + (index * spacing), 1.5f, 3f);
    }
    
    Color GetAgentColorFromJSON(string agentId)
    {
        // Get color from JSON or use defaults
        if (sceneData?.agentProfiles != null && 
            sceneData.agentProfiles.ContainsKey(agentId))
        {
            string colorName = sceneData.agentProfiles[agentId].color;
            
            switch (colorName?.ToLower())
            {
                case "green": return Color.green;
                case "blue": return Color.blue;
                case "purple": return Color.magenta;
                case "red": return Color.red;
                case "yellow": return Color.yellow;
                default: break;
            }
        }
        
        // Default colors based on agent ID
        switch (agentId)
        {
            case "agent_technician_A": return Color.green;
            case "agent_supervisor_B": return Color.blue;
            case "agent_inspector_C": return Color.magenta;
            default: return Color.cyan;
        }
    }
    
    void CreateAgentNameLabel(GameObject parent, string name, string role)
    {
        GameObject nameLabel = new GameObject("NameLabel");
        nameLabel.transform.SetParent(parent.transform);
        nameLabel.transform.localPosition = new Vector3(0, 2.5f, 0);
        
        TextMesh nameText = nameLabel.AddComponent<TextMesh>();
        nameText.text = $"{name}\n{role}";
        nameText.fontSize = 8;
        nameText.color = Color.white;
        nameText.anchor = TextAnchor.MiddleCenter;
        nameText.alignment = TextAlignment.Center;
    }
    
    void CreateStatusIndicator(GameObject parent, Color baseColor)
    {
        GameObject statusIndicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        statusIndicator.name = "StatusIndicator";
        statusIndicator.transform.SetParent(parent.transform);
        statusIndicator.transform.localPosition = new Vector3(0, 2f, 0);
        statusIndicator.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);
        statusIndicator.GetComponent<Renderer>().material.color = Color.green; // Start green (ready)
    }
    
    void CreateToolsFromJSON()
    {
        Debug.Log("Creating tools from JSON...");
        
        if (sceneData?.initialStates == null)
        {
            Debug.LogWarning("No tools found in JSON");
            return;
        }
        
        int toolCount = 0;
        foreach (var toolKV in sceneData.initialStates)
        {
            string toolId = toolKV.Key;
            var toolState = toolKV.Value;
            
            CreateToolFromJSON(toolId, toolState);
            toolCount++;
        }
        
        Debug.Log($"✅ Created {toolCount} tools from JSON");
    }
    
    void CreateToolFromJSON(string toolId, ToolState toolState)
    {
        // Get position from JSON
        Vector3 position = Vector3.zero;
        if (toolState.position != null)
        {
            position = new Vector3(toolState.position.x, toolState.position.y, toolState.position.z);
        }
        
        // Get color from JSON
        Color toolColor = GetToolColorFromJSON(toolState);
        
        // Get tool type and create appropriate shape
        GameObject tool = CreateToolByType(toolState);
        tool.name = $"Tool_{toolId}_{toolState.name?.Replace(" ", "_")}";
        tool.transform.position = position;
        
        // Set color
        tool.GetComponent<Renderer>().material.color = toolColor;
        
        // Add tool label
        CreateToolLabel(tool, toolState.name ?? toolId);
        
        Debug.Log($"✅ Created tool: {toolState.name} at {position}");
    }
    
    GameObject CreateToolByType(ToolState toolState)
    {
        string toolType = toolState.type ?? "default";
        
        switch (toolType.ToLower())
        {
            case "motor":
                GameObject motor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                motor.transform.localScale = new Vector3(1.2f, 0.8f, 1.2f);
                return motor;
                
            case "storage":
                return GameObject.CreatePrimitive(PrimitiveType.Cube);
                
            case "machinery":
                GameObject machinery = GameObject.CreatePrimitive(PrimitiveType.Cube);
                machinery.transform.localScale = new Vector3(1.5f, 1.2f, 1.5f);
                return machinery;
                
            case "workstation":
                GameObject workstation = GameObject.CreatePrimitive(PrimitiveType.Cube);
                workstation.transform.localScale = new Vector3(2f, 0.6f, 1.2f);
                return workstation;
                
            default:
                return GameObject.CreatePrimitive(PrimitiveType.Cube);
        }
    }
    
    Color GetToolColorFromJSON(ToolState toolState)
    {
        string colorName = toolState.color;
        
        switch (colorName?.ToLower())
        {
            case "red": return Color.red;
            case "yellow": return Color.yellow;
            case "blue": return Color.blue;
            case "green": return Color.green;
            case "orange": return new Color(1f, 0.5f, 0f);
            default: return Color.gray;
        }
    }
    
    void CreateToolLabel(GameObject tool, string toolName)
    {
        GameObject label = new GameObject("ToolLabel");
        label.transform.SetParent(tool.transform);
        label.transform.localPosition = new Vector3(0, 2f, 0);
        
        TextMesh labelText = label.AddComponent<TextMesh>();
        labelText.text = toolName;
        labelText.fontSize = 6;
        labelText.color = Color.white;
        labelText.anchor = TextAnchor.MiddleCenter;
        labelText.alignment = TextAlignment.Center;
    }
    
    void CreateFallbackScene()
    {
        Debug.Log("Creating fallback scene...");
        CreatePlanSurface();
        CreateFallbackTechnicians();
    }
    
    void CreateFallbackTechnicians()
    {
        CreateSphereTechnician("Alex Rodriguez", "Senior Technician", new Vector3(-5, 1.5f, 3), Color.green);
        CreateSphereTechnician("Maria Santos", "Operations Supervisor", new Vector3(0, 1.5f, 3), Color.blue);
        CreateSphereTechnician("David Kim", "Safety Inspector", new Vector3(5, 1.5f, 3), Color.magenta);
    }
    
    void CreateSphereTechnician(string name, string role, Vector3 position, Color color)
    {
        GameObject technician = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        technician.name = $"Technician_Sphere_{name.Replace(" ", "_")}";
        technician.transform.position = position;
        technician.transform.localScale = Vector3.one * 1.8f;
        technician.GetComponent<Renderer>().material.color = color;
        
        CreateAgentNameLabel(technician, name, role);
        CreateStatusIndicator(technician, color);
        
        JSONTechnicianMover mover = technician.AddComponent<JSONTechnicianMover>();
        mover.agentName = name;
        mover.originalPosition = position;
    }
    
    void SetupOptimalCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            mainCamera.transform.position = new Vector3(0, 15, -10);
            mainCamera.transform.rotation = Quaternion.Euler(35, 0, 0);
            mainCamera.fieldOfView = 50;
        }
        
        Debug.Log("✅ Camera positioned for optimal view");
    }
    
    void Update()
    {
        // F5 to regenerate from JSON
        if (Input.GetKeyDown(KeyCode.F5))
        {
            Debug.Log("🔄 F5 pressed - Regenerating from JSON...");
            LoadJSONAndCreateScene();
        }
        
        // H for help
        if (Input.GetKeyDown(KeyCode.H))
        {
            ShowControls();
        }
    }
    
    void ShowControls()
    {
        Debug.Log("=== JSON PLAN SCENE CONTROLS ===");
        Debug.Log("Left Click: Select sphere technician");
        Debug.Log("WASD: Move selected technician");
        Debug.Log("R: Return to original position");
        Debug.Log("F5: Regenerate scene from JSON");
        Debug.Log("H: Show this help");
        Debug.Log($"Scene ID: {sceneData?.scene_id}");
        Debug.Log($"Plan: {sceneData?.plan?.planName}");
    }
}
