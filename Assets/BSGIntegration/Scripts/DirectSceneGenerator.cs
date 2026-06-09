using UnityEngine;

public class DirectSceneGenerator : MonoBehaviour
{
    [Header("Direct Scene Generation")]
    public bool generateOnStart = true;
    
    void Start()
    {
        if (generateOnStart)
        {
            Debug.Log("🚀 DIRECT Scene Generator Starting...");
            CreateSceneDirectly();
        }
    }
    
    void CreateSceneDirectly()
    {
        // Clear existing objects first
        ClearExistingObjects();
        
        // Create orange plan surface
        CreateOrangePlan();
        
        // Create 3 sphere technicians
        CreateSphereTechnicians();
        
        // Create tools
        CreateTools();
        
        // Create status board
        CreateStatusBoard();
        
        // Setup camera
        SetupCamera();
        
        Debug.Log("✅ DIRECT Scene Created Successfully!");
        Debug.Log("Check your hierarchy - you should see all objects now!");
    }
    
    void ClearExistingObjects()
    {
        // Clear any existing generated objects
        string[] prefixes = {"Plan_", "Technician_", "Tool_", "JSON_", "SimpleStatusBoard", "AgentStatusBoard", "StatusBoard"};
        
        foreach (string prefix in prefixes)
        {
            GameObject[] allObjects = FindObjectsOfType<GameObject>();
            foreach (GameObject obj in allObjects)
            {
                if (obj.name.StartsWith(prefix))
                {
                    DestroyImmediate(obj);
                }
            }
        }
        
        Debug.Log("🧹 Cleared existing objects");
    }
    
    void CreateOrangePlan()
    {
        Debug.Log("Creating orange plan surface...");
        
        // Create main orange platform
        GameObject planSurface = GameObject.CreatePrimitive(PrimitiveType.Cube);
        planSurface.name = "Plan_Surface_Orange";
        planSurface.transform.position = new Vector3(0, 0, 0);
        planSurface.transform.localScale = new Vector3(20f, 0.3f, 15f);
        
        // Make it orange
        Renderer planRenderer = planSurface.GetComponent<Renderer>();
        Material orangeMaterial = new Material(Shader.Find("Standard"));
        orangeMaterial.color = new Color(1f, 0.6f, 0.2f); // Orange
        planRenderer.material = orangeMaterial;
        
        Debug.Log("✅ Created Plan_Surface_Orange");
        
        // Create orange borders
        CreateBorder("Plan_Border_North", new Vector3(0, 0.4f, 7.5f), new Vector3(20f, 0.2f, 0.3f));
        CreateBorder("Plan_Border_South", new Vector3(0, 0.4f, -7.5f), new Vector3(20f, 0.2f, 0.3f));
        CreateBorder("Plan_Border_East", new Vector3(10f, 0.4f, 0), new Vector3(0.3f, 0.2f, 15f));
        CreateBorder("Plan_Border_West", new Vector3(-10f, 0.4f, 0), new Vector3(0.3f, 0.2f, 15f));
        
        // Create plan text
        GameObject planText = new GameObject("Plan_Text");
        planText.transform.position = new Vector3(0, 0.6f, -9);
        
        TextMesh textMesh = planText.AddComponent<TextMesh>();
        textMesh.text = "WAREHOUSE MAINTENANCE PLAN";
        textMesh.fontSize = 16;
        textMesh.color = Color.white;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.transform.rotation = Quaternion.Euler(90, 0, 0);
        
        Debug.Log("✅ Created Plan_Text");
    }
    
    void CreateBorder(string name, Vector3 position, Vector3 scale)
    {
        GameObject border = GameObject.CreatePrimitive(PrimitiveType.Cube);
        border.name = name;
        border.transform.position = position;
        border.transform.localScale = scale;
        
        Renderer borderRenderer = border.GetComponent<Renderer>();
        Material borderMaterial = new Material(Shader.Find("Standard"));
        borderMaterial.color = new Color(1f, 0.4f, 0f); // Darker orange
        borderRenderer.material = borderMaterial;
        
        Debug.Log($"✅ Created {name}");
    }
    
    void CreateSphereTechnicians()
    {
        Debug.Log("Creating sphere technicians...");
        
        // Create technician001 (Green)
        CreateSphereTechnician(
            "Technician_Sphere_SIMPLE_Technician_01",
            "technician001",
            new Vector3(-6, 1.5f, 0),
            Color.green
        );
        
        // Create supervisor001 (Blue)
        CreateSphereTechnician(
            "Technician_Sphere_SIMPLE_Supervisor_01",
            "supervisor001",
            new Vector3(0, 1.5f, 6),
            Color.blue
        );
        
        // Create technician002 (Purple)
        CreateSphereTechnician(
            "Technician_Sphere_SIMPLE_Technician_02",
            "technician002",
            new Vector3(6, 1.5f, 0),
            Color.magenta
        );
        
        Debug.Log("✅ Created 3 sphere technicians");
    }
    
    void CreateSphereTechnician(string name, string labelText, Vector3 position, Color color)
    {
        Debug.Log($"Creating {name}...");
        
        // Create main sphere
        GameObject technician = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        technician.name = name;
        technician.transform.position = position;
        technician.transform.localScale = Vector3.one * 1.8f;
        
        // Set color
        Renderer techRenderer = technician.GetComponent<Renderer>();
        Material techMaterial = new Material(Shader.Find("Standard"));
        techMaterial.color = color;
        techRenderer.material = techMaterial;
        
        // Create head sphere
        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        head.transform.SetParent(technician.transform);
        head.transform.localPosition = new Vector3(0, 1.1f, 0);
        head.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
        
        Renderer headRenderer = head.GetComponent<Renderer>();
        Material headMaterial = new Material(Shader.Find("Standard"));
        headMaterial.color = Color.Lerp(color, Color.white, 0.4f);
        headRenderer.material = headMaterial;
        
        // Create name label (only showing agent ID)
        GameObject nameLabel = new GameObject("NameLabel");
        nameLabel.transform.SetParent(technician.transform);
        nameLabel.transform.localPosition = new Vector3(0, 2.5f, 0);
        
        TextMesh nameText = nameLabel.AddComponent<TextMesh>();
        nameText.text = labelText; // This will be technician001, supervisor001, etc.
        nameText.fontSize = 12;
        nameText.color = Color.white;
        nameText.anchor = TextAnchor.MiddleCenter;
        nameText.alignment = TextAlignment.Center;
        
        // Create status indicator
        GameObject statusIndicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        statusIndicator.name = "StatusIndicator";
        statusIndicator.transform.SetParent(technician.transform);
        statusIndicator.transform.localPosition = new Vector3(0, 2f, 0);
        statusIndicator.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);
        
        Renderer statusRenderer = statusIndicator.GetComponent<Renderer>();
        Material statusMaterial = new Material(Shader.Find("Standard"));
        statusMaterial.color = Color.green;
        statusRenderer.material = statusMaterial;
        
        // Add movement component
        SimpleTechnicianMover mover = technician.AddComponent<SimpleTechnicianMover>();
        mover.technicianName = labelText; // Use the agent ID directly
        mover.originalPosition = position;
        
        // Add AgentProximity component for skill-based interactions
        AgentProximity proximity = technician.AddComponent<AgentProximity>();
        proximity.agentID = name; // Use the full name for agent ID
        proximity.detectionRadius = 3f;
        
        Debug.Log($"✅ Created {name}");
    }
    
    void CreateTools()
    {
        Debug.Log("Creating tools...");
        
        // Tool 1: Industrial Motor Unit (Red Cylinder)
        CreateTool("Tool_tool_001", "Industrial Motor Unit", 
                  new Vector3(-4, 0.5f, -5), Color.red, PrimitiveType.Cylinder);
        
        // Tool 2: Secure Toolbox Alpha (Yellow Cube)
        CreateTool("Tool_tool_002", "Secure Toolbox Alpha", 
                  new Vector3(0, 0.5f, -5), Color.yellow, PrimitiveType.Cube);
        
        // Tool 3: Hydraulic Lift Station (Blue Large Cube)
        CreateTool("Tool_tool_003", "Hydraulic Lift Station", 
                  new Vector3(4, 0.5f, -5), Color.blue, PrimitiveType.Cube, new Vector3(1.5f, 1.2f, 1.5f));
        
        // Tool 4: Safety Inspection Station (Green Cube)
        CreateTool("Tool_tool_004", "Safety Inspection Station", 
                  new Vector3(-2, 0.5f, 3), Color.green, PrimitiveType.Cube);
        
        // Tool 5: Main Assembly Workbench (Orange Flat Cube)
        CreateTool("Tool_workbench_001", "Main Assembly Workbench", 
                  new Vector3(2, 0.8f, 3), new Color(1f, 0.5f, 0f), PrimitiveType.Cube, new Vector3(2f, 0.6f, 1.2f));
        
        Debug.Log("✅ Created 5 tools");
    }
    
    void CreateTool(string name, string labelText, Vector3 position, Color color, PrimitiveType type, Vector3? scale = null)
    {
        Debug.Log($"Creating {name}...");
        
        // Create tool
        GameObject tool = GameObject.CreatePrimitive(type);
        tool.name = name;
        tool.transform.position = position;
        
        if (scale.HasValue)
        {
            tool.transform.localScale = scale.Value;
        }
        
        // Set color
        Renderer toolRenderer = tool.GetComponent<Renderer>();
        Material toolMaterial = new Material(Shader.Find("Standard"));
        toolMaterial.color = color;
        toolRenderer.material = toolMaterial;
        
        // Create tool label
        GameObject toolLabel = new GameObject("ToolLabel");
        toolLabel.transform.SetParent(tool.transform);
        toolLabel.transform.localPosition = new Vector3(0, 2f, 0);
        
        TextMesh labelMesh = toolLabel.AddComponent<TextMesh>();
        labelMesh.text = labelText;
        labelMesh.fontSize = 6;
        labelMesh.color = Color.white;
        labelMesh.anchor = TextAnchor.MiddleCenter;
        labelMesh.alignment = TextAlignment.Center;
        
        Debug.Log($"✅ Created {name}");
    }
    
    void SetupCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            mainCamera.transform.position = new Vector3(0, 15, -10);
            mainCamera.transform.rotation = Quaternion.Euler(35, 0, 0);
            mainCamera.fieldOfView = 50;
            Debug.Log("✅ Camera positioned for optimal view");
        }
    }
    
    void Update()
    {
        // Note: Input disabled due to Input System conflict
        // Use context menu or inspector button instead
    }
    
    [ContextMenu("Regenerate Scene")]
    void RegenerateScene()
    {
        Debug.Log("🔄 Regenerating scene...");
        CreateSceneDirectly();
    }
    
    [ContextMenu("Show Help")]
    void ShowHelpMenu()
    {
        ShowHelp();
    }
    
    void ShowHelp()
    {
        Debug.Log("=== DIRECT SCENE GENERATOR CONTROLS ===");
        Debug.Log("F5: Regenerate entire scene");
        Debug.Log("G: Manual generation");
        Debug.Log("H: Show this help");
        Debug.Log("Left Click: Select sphere technician");
        Debug.Log("WASD: Move selected technician");
        Debug.Log("R: Return to original position");
    }
    
    void CreateStatusBoard()
    {
        Debug.Log("Creating Simple Status Board...");
        
        // Create SkillBasedActionSystem first (required for status board)
        GameObject skillSystemGO = new GameObject("SkillBasedActionSystem");
        SkillBasedActionSystem skillSystem = skillSystemGO.AddComponent<SkillBasedActionSystem>();
        
        // Create a GameObject to hold the simple status board
        GameObject statusBoardGO = new GameObject("SimpleStatusBoard");
        
        // Add the SimpleStatusBoard component
        SimpleStatusBoard statusBoard = statusBoardGO.AddComponent<SimpleStatusBoard>();
        
        // Configure the status board
        statusBoard.boardPosition = new Vector3(0, 11, 0); // Lower position to utilize empty space
        statusBoard.fontSize = 1.2f; // Larger font size for readability 
        statusBoard.updateInterval = 0.3f; // Update every 0.3 seconds for continuous updates
        statusBoard.textColor = Color.white;
        statusBoard.backgroundColor = new Color(0, 0, 0, 0.0f); // Fully transparent background
        statusBoard.showBackground = false; // Disable background to avoid pink
        
        Debug.Log("✅ Created Simple Status Board and Skill System");
    }
}
