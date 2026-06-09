using UnityEngine;
using System.IO;

public class SimpleJSONPlanGenerator : MonoBehaviour
{
    [Header("JSON Scene Generation")]
    public bool generateOnStart = true;
    public string jsonFileName = "basicUi.json";
    
    private SceneData sceneData;
    
    void Start()
    {
        if (generateOnStart)
        {
            Debug.Log("🚀 Simple JSON Plan Generator Starting...");
            GenerateSceneFromJSON();
        }
    }
    
    void GenerateSceneFromJSON()
    {
        // Load JSON data
        LoadJSON();
        
        // Clear existing generated objects
        ClearGeneratedObjects();
        
        // Create orange plan surface
        CreateOrangePlanSurface();
        
        // Create sphere technicians from JSON
        CreateSphereTechniciansFromJSON();
        
        // Create tools from JSON (if available)
        CreateToolsFromJSON();
        
        // Setup camera
        SetupCamera();
        
        Debug.Log("✅ JSON Plan Scene Generated Successfully!");
    }
    
    void LoadJSON()
    {
        try
        {
            string jsonPath = Path.Combine(Application.dataPath, "JsonFile", jsonFileName);
            if (File.Exists(jsonPath))
            {
                string json = File.ReadAllText(jsonPath);
                sceneData = JsonUtility.FromJson<SceneData>(json);
                Debug.Log($"✅ JSON loaded: {sceneData.scene_id}");
                
                if (sceneData.plan != null)
                {
                    Debug.Log($"Plan: {sceneData.plan.planName}");
                }
            }
            else
            {
                Debug.LogError($"❌ JSON file not found at: {jsonPath}");
                sceneData = null;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Error loading JSON: {e.Message}");
            sceneData = null;
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
    
    void CreateOrangePlanSurface()
    {
        Debug.Log("Creating orange plan surface...");
        
        // Get plan name from JSON or use default
        string planName = "Warehouse Plan";
        if (sceneData?.plan != null)
        {
            planName = sceneData.plan.planName ?? "Warehouse Plan";
        }
        
        // Create main orange platform
        GameObject planSurface = GameObject.CreatePrimitive(PrimitiveType.Cube);
        planSurface.name = "Plan_Surface_Orange";
        planSurface.transform.position = new Vector3(0, 0, 0);
        planSurface.transform.localScale = new Vector3(20f, 0.3f, 15f);
        
        // Make it orange
        Renderer planRenderer = planSurface.GetComponent<Renderer>();
        planRenderer.material.color = new Color(1f, 0.6f, 0.2f); // Orange color
        
        // Create orange border
        CreateOrangeBorder();
        
        // Add plan text
        CreatePlanText(planName);
        
        Debug.Log($"✅ Created orange plan surface: {planName}");
    }
    
    void CreateOrangeBorder()
    {
        Color borderColor = new Color(1f, 0.4f, 0f); // Darker orange
        
        // Create 4 border segments
        CreateBorderSegment("Plan_Border_North", new Vector3(0, 0.4f, 7.5f), new Vector3(20f, 0.2f, 0.3f), borderColor);
        CreateBorderSegment("Plan_Border_South", new Vector3(0, 0.4f, -7.5f), new Vector3(20f, 0.2f, 0.3f), borderColor);
        CreateBorderSegment("Plan_Border_East", new Vector3(10f, 0.4f, 0), new Vector3(0.3f, 0.2f, 15f), borderColor);
        CreateBorderSegment("Plan_Border_West", new Vector3(-10f, 0.4f, 0), new Vector3(0.3f, 0.2f, 15f), borderColor);
    }
    
    void CreateBorderSegment(string name, Vector3 position, Vector3 scale, Color color)
    {
        GameObject border = GameObject.CreatePrimitive(PrimitiveType.Cube);
        border.name = name;
        border.transform.position = position;
        border.transform.localScale = scale;
        border.GetComponent<Renderer>().material.color = color;
    }
    
    void CreatePlanText(string planName)
    {
        GameObject planTextGO = new GameObject("Plan_Text");
        planTextGO.transform.position = new Vector3(0, 0.8f, -5);
        
        TextMesh planText = planTextGO.AddComponent<TextMesh>();
        planText.text = planName.ToUpper();
        planText.fontSize = 16;
        planText.color = Color.white;
        planText.anchor = TextAnchor.MiddleCenter;
        planText.alignment = TextAlignment.Center;
        
        // Make text face up
        planTextGO.transform.rotation = Quaternion.Euler(90, 0, 0);
    }
    
    void CreateSphereTechniciansFromJSON()
    {
        Debug.Log("Creating sphere technicians...");
        
        if (sceneData?.agentProfiles == null)
        {
            Debug.LogWarning("No agent profiles in JSON, creating default technicians");
            CreateDefaultTechnicians();
            return;
        }
        
        int index = 0;
        foreach (var agentKV in sceneData.agentProfiles)
        {
            string agentId = agentKV.Key;
            var agentProfile = agentKV.Value;
            
            CreateSphereTechnician(agentId, agentProfile, index);
            index++;
        }
        
        Debug.Log($"✅ Created {index} sphere technicians from JSON");
    }
    
    void CreateSphereTechnician(string agentId, AgentProfile agentProfile, int index)
    {
        // Get position from JSON or use default
        Vector3 position = GetTechnicianPosition(agentProfile, index);
        
        // Get color from JSON or use default
        Color technicianColor = GetTechnicianColor(agentId, agentProfile);
        
        // Get name from JSON or use agent ID
        string technicianName = agentProfile?.name ?? agentId;
        string technicianRole = agentProfile?.role ?? "Technician";
        
        Debug.Log($"Creating sphere technician: {technicianName} at {position}");
        
        // Create main sphere
        GameObject technician = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        technician.name = $"Technician_Sphere_{agentId}";
        technician.transform.position = position;
        technician.transform.localScale = Vector3.one * 1.8f;
        
        // Set color
        Renderer techRenderer = technician.GetComponent<Renderer>();
        techRenderer.material.color = technicianColor;
        
        // Add smaller head sphere
        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        head.transform.SetParent(technician.transform);
        head.transform.localPosition = new Vector3(0, 1.1f, 0);
        head.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
        head.GetComponent<Renderer>().material.color = Color.Lerp(technicianColor, Color.white, 0.4f);
        
        // Add name label
        CreateTechnicianLabel(technician, technicianName, technicianRole);
        
        // Add status indicator
        CreateStatusIndicator(technician);
        
        // Add movement component
        SimpleTechnicianMover mover = technician.AddComponent<SimpleTechnicianMover>();
        mover.technicianName = technicianName;
        mover.originalPosition = position;
        
        Debug.Log($"✅ Created sphere technician: {technicianName}");
    }
    
    Vector3 GetTechnicianPosition(AgentProfile agentProfile, int index)
    {
        // Try to get position from JSON
        if (agentProfile?.position != null)
        {
            return new Vector3(agentProfile.position.x, agentProfile.position.y + 0.5f, agentProfile.position.z);
        }
        
        // Default positions spread out
        float spacing = 6f;
        return new Vector3(-6f + (index * spacing), 1.5f, 3f);
    }
    
    Color GetTechnicianColor(string agentId, AgentProfile agentProfile)
    {
        // Try to get color from JSON
        if (!string.IsNullOrEmpty(agentProfile?.color))
        {
            switch (agentProfile.color.ToLower())
            {
                case "green": return Color.green;
                case "blue": return Color.blue;
                case "purple": return Color.magenta;
                case "red": return Color.red;
                case "yellow": return Color.yellow;
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
    
    void CreateTechnicianLabel(GameObject parent, string name, string role)
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
    
    void CreateStatusIndicator(GameObject parent)
    {
        GameObject statusIndicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        statusIndicator.name = "StatusIndicator";
        statusIndicator.transform.SetParent(parent.transform);
        statusIndicator.transform.localPosition = new Vector3(0, 2f, 0);
        statusIndicator.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);
        statusIndicator.GetComponent<Renderer>().material.color = Color.green;
    }
    
    void CreateDefaultTechnicians()
    {
        // Create 3 default sphere technicians
        CreateDefaultTechnician("Alex Rodriguez", "Senior Technician", new Vector3(-5, 1.5f, 3), Color.green);
        CreateDefaultTechnician("Maria Santos", "Operations Supervisor", new Vector3(0, 1.5f, 3), Color.blue);
        CreateDefaultTechnician("David Kim", "Safety Inspector", new Vector3(5, 1.5f, 3), Color.magenta);
    }
    
    void CreateDefaultTechnician(string name, string role, Vector3 position, Color color)
    {
        GameObject technician = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        technician.name = $"Technician_Sphere_{name.Replace(" ", "_")}";
        technician.transform.position = position;
        technician.transform.localScale = Vector3.one * 1.8f;
        technician.GetComponent<Renderer>().material.color = color;
        
        CreateTechnicianLabel(technician, name, role);
        CreateStatusIndicator(technician);
        
        SimpleTechnicianMover mover = technician.AddComponent<SimpleTechnicianMover>();
        mover.technicianName = name;
        mover.originalPosition = position;
    }
    
    void CreateToolsFromJSON()
    {
        if (sceneData?.initialStates == null)
        {
            Debug.Log("No tools found in JSON");
            return;
        }
        
        Debug.Log("Creating tools from JSON...");
        
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
        // Get position from JSON or use default
        Vector3 position = Vector3.zero;
        if (toolState.position != null)
        {
            position = new Vector3(toolState.position.x, toolState.position.y, toolState.position.z);
        }
        else
        {
            // Default position
            position = new Vector3(0, 0.5f, -8f);
        }
        
        // Create tool based on type
        GameObject tool = CreateToolByType(toolState.type);
        tool.name = $"Tool_{toolId}";
        tool.transform.position = position;
        
        // Set color from JSON
        Color toolColor = GetToolColor(toolState.color);
        tool.GetComponent<Renderer>().material.color = toolColor;
        
        // Add tool label
        CreateToolLabel(tool, toolState.name ?? toolId);
        
        Debug.Log($"✅ Created tool: {toolState.name ?? toolId} at {position}");
    }
    
    GameObject CreateToolByType(string toolType)
    {
        if (string.IsNullOrEmpty(toolType))
        {
            return GameObject.CreatePrimitive(PrimitiveType.Cube);
        }
        
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
    
    Color GetToolColor(string colorName)
    {
        if (string.IsNullOrEmpty(colorName))
        {
            return Color.gray;
        }
        
        switch (colorName.ToLower())
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
    
    void SetupCamera()
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
        // F5 to regenerate
        if (Input.GetKeyDown(KeyCode.F5))
        {
            Debug.Log("🔄 F5 pressed - Regenerating scene from JSON...");
            GenerateSceneFromJSON();
        }
        
        // H for help
        if (Input.GetKeyDown(KeyCode.H))
        {
            ShowHelp();
        }
    }
    
    void ShowHelp()
    {
        Debug.Log("=== SIMPLE JSON PLAN SCENE CONTROLS ===");
        Debug.Log("Left Click: Select sphere technician");
        Debug.Log("WASD: Move selected technician");
        Debug.Log("R: Return to original position");
        Debug.Log("F5: Regenerate scene from JSON");
        Debug.Log("H: Show this help");
        
        if (sceneData != null)
        {
            Debug.Log($"Scene ID: {sceneData.scene_id}");
            Debug.Log($"Plan: {sceneData.plan?.planName ?? "No plan data"}");
        }
    }
}
