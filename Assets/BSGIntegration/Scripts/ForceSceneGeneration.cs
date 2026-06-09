using UnityEngine;
using UnityEngine.UI;
using System.IO;

public class ForceSceneGeneration : MonoBehaviour
{
    [Header("Force Generation")]
    public bool generateOnStart = true;
    
    private SceneData sceneData;
    
    void Start()
    {
        if (generateOnStart)
        {
            Debug.Log("🚀 FORCE SCENE GENERATION STARTING...");
            ForceGenerateEverything();
        }
    }
    
    void ForceGenerateEverything()
    {
        // Step 1: Load JSON
        LoadJSON();
        
        // Step 2: Create Environment
        CreateEnvironment();
        
        // Step 3: Create Technicians with Plans
        CreateTechniciansWithPlans();
        
        // Step 4: Create Tools
        CreateTools();
        
        // Step 5: Create UI
        CreateUI();
        
        // Step 6: Setup Camera
        SetupCamera();
        
        Debug.Log("✅ FORCE GENERATION COMPLETE!");
    }
    
    void LoadJSON()
    {
        try
        {
            string jsonPath = Path.Combine(Application.dataPath, "JsonFile", "basicUi.json");
            if (File.Exists(jsonPath))
            {
                string json = File.ReadAllText(jsonPath);
                sceneData = JsonUtility.FromJson<SceneData>(json);
                Debug.Log($"✅ JSON loaded: {sceneData.scene_id}");
            }
            else
            {
                Debug.LogError($"❌ JSON file not found at: {jsonPath}");
                CreateFallbackData();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Error loading JSON: {e.Message}");
            CreateFallbackData();
        }
    }
    
    void CreateFallbackData()
    {
        Debug.Log("Creating fallback data...");
        sceneData = new SceneData();
        sceneData.scene_id = "fallback_scene";
    }
    
    void CreateEnvironment()
    {
        Debug.Log("Creating environment...");
        
        // Floor
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Environment_Floor";
        floor.transform.position = new Vector3(0, -0.5f, 0);
        floor.transform.localScale = new Vector3(30, 1, 30);
        floor.GetComponent<Renderer>().material.color = Color.gray;
        
        // Walls
        CreateWall("North", new Vector3(0, 3, 15), new Vector3(30, 6, 1));
        CreateWall("South", new Vector3(0, 3, -15), new Vector3(30, 6, 1));
        CreateWall("East", new Vector3(15, 3, 0), new Vector3(1, 6, 30));
        CreateWall("West", new Vector3(-15, 3, 0), new Vector3(1, 6, 30));
    }
    
    void CreateWall(string name, Vector3 position, Vector3 scale)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = $"Environment_Wall_{name}";
        wall.transform.position = position;
        wall.transform.localScale = scale;
        wall.GetComponent<Renderer>().material.color = new Color(0.8f, 0.8f, 0.9f);
    }
    
    void CreateTechniciansWithPlans()
    {
        Debug.Log("Creating technicians with plans...");
        
        // Technician 1: Alex Rodriguez
        CreateTechnicianWithPlan(
            "Alex Rodriguez",
            "Senior Technician", 
            new Vector3(-6, 1, 0),
            Color.green,
            "MAINTENANCE PLAN\n• Motor Repair\n• Toolbox Access\n• Final Assembly"
        );
        
        // Technician 2: Maria Santos
        CreateTechnicianWithPlan(
            "Maria Santos",
            "Operations Supervisor",
            new Vector3(0, 1, 6),
            Color.blue,
            "SUPERVISION PLAN\n• Hydraulic Check\n• Quality Control\n• Team Coordination"
        );
        
        // Technician 3: David Kim
        CreateTechnicianWithPlan(
            "David Kim",
            "Safety Inspector",
            new Vector3(6, 1, 0),
            Color.magenta,
            "INSPECTION PLAN\n• Safety Compliance\n• Risk Assessment\n• Documentation"
        );
    }
    
    void CreateTechnicianWithPlan(string name, string role, Vector3 position, Color color, string planText)
    {
        Debug.Log($"Creating technician: {name}");
        
        // Create white plan platform
        GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
        platform.name = $"Plan_Platform_{name.Replace(" ", "_")}";
        platform.transform.position = position;
        platform.transform.localScale = new Vector3(4f, 0.2f, 4f);
        platform.GetComponent<Renderer>().material.color = Color.white;
        
        // Create plan text
        GameObject planTextGO = new GameObject("PlanText");
        planTextGO.transform.SetParent(platform.transform);
        planTextGO.transform.localPosition = new Vector3(0f, 2f, 0f);
        
        TextMesh planTextMesh = planTextGO.AddComponent<TextMesh>();
        planTextMesh.text = planText;
        planTextMesh.fontSize = 8;
        planTextMesh.color = Color.black;
        planTextMesh.anchor = TextAnchor.MiddleCenter;
        planTextMesh.alignment = TextAlignment.Center;
        
        // Create detailed technician model
        GameObject technician = CreateDetailedTechnician(name, role, color);
        technician.transform.position = new Vector3(position.x, position.y + 0.6f, position.z);
        
        // Add moving functionality
        TechnicianMover mover = technician.AddComponent<TechnicianMover>();
        mover.originalPosition = technician.transform.position;
        mover.technicianName = name;
        
        Debug.Log($"✅ Created technician {name} at {position}");
    }
    
    GameObject CreateDetailedTechnician(string name, string role, Color color)
    {
        GameObject technician = new GameObject($"Technician_{name.Replace(" ", "_")}");
        
        // Body (main capsule)
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(technician.transform);
        body.transform.localPosition = Vector3.zero;
        body.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
        body.GetComponent<Renderer>().material.color = color;
        
        // Head (sphere)
        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        head.transform.SetParent(technician.transform);
        head.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        head.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
        head.GetComponent<Renderer>().material.color = Color.Lerp(color, Color.white, 0.3f);
        
        // Arms
        CreateLimb(technician, "LeftArm", new Vector3(-0.7f, 0.3f, 0f), new Vector3(0, 0, 90), Color.Lerp(color, Color.gray, 0.4f));
        CreateLimb(technician, "RightArm", new Vector3(0.7f, 0.3f, 0f), new Vector3(0, 0, 90), Color.Lerp(color, Color.gray, 0.4f));
        
        // Legs
        CreateLimb(technician, "LeftLeg", new Vector3(-0.3f, -1.2f, 0f), Vector3.zero, Color.Lerp(color, Color.gray, 0.4f));
        CreateLimb(technician, "RightLeg", new Vector3(0.3f, -1.2f, 0f), Vector3.zero, Color.Lerp(color, Color.gray, 0.4f));
        
        // Name label
        GameObject nameLabel = new GameObject("NameLabel");
        nameLabel.transform.SetParent(technician.transform);
        nameLabel.transform.localPosition = new Vector3(0f, 2.5f, 0f);
        
        TextMesh nameText = nameLabel.AddComponent<TextMesh>();
        nameText.text = $"{name}\n{role}";
        nameText.fontSize = 10;
        nameText.color = Color.white;
        nameText.anchor = TextAnchor.MiddleCenter;
        nameText.alignment = TextAlignment.Center;
        
        // Status indicator
        GameObject statusIndicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        statusIndicator.name = "StatusIndicator";
        statusIndicator.transform.SetParent(technician.transform);
        statusIndicator.transform.localPosition = Vector3.up * 2f;
        statusIndicator.transform.localScale = Vector3.one * 0.3f;
        statusIndicator.GetComponent<Renderer>().material.color = Color.green;
        
        // Add collider for interaction
        BoxCollider collider = technician.AddComponent<BoxCollider>();
        collider.size = new Vector3(2f, 3f, 2f);
        
        return technician;
    }
    
    void CreateLimb(GameObject parent, string name, Vector3 position, Vector3 rotation, Color color)
    {
        GameObject limb = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        limb.name = name;
        limb.transform.SetParent(parent.transform);
        limb.transform.localPosition = position;
        limb.transform.localRotation = Quaternion.Euler(rotation);
        limb.transform.localScale = new Vector3(0.2f, 0.4f, 0.2f);
        limb.GetComponent<Renderer>().material.color = color;
    }
    
    void CreateTools()
    {
        Debug.Log("Creating tools...");
        
        // Tool 1: Motor (Red Cylinder)
        CreateTool("Industrial Motor Unit", PrimitiveType.Cylinder, new Vector3(-4, 0.5f, -5), Color.red, new Vector3(1.5f, 1f, 1.5f));
        
        // Tool 2: Toolbox (Yellow Cube)
        CreateTool("Secure Toolbox Alpha", PrimitiveType.Cube, new Vector3(0, 0.5f, -5), Color.yellow, Vector3.one);
        
        // Tool 3: Hydraulic Lift (Blue Large Cube)
        CreateTool("Hydraulic Lift Station", PrimitiveType.Cube, new Vector3(4, 0.5f, -5), Color.blue, new Vector3(1.5f, 1f, 1.5f));
        
        // Tool 4: Safety Station (Green Cube)
        CreateTool("Safety Inspection Station", PrimitiveType.Cube, new Vector3(-2, 0.5f, 3), Color.green, Vector3.one);
        
        // Tool 5: Workbench (Orange Flat Cube)
        CreateTool("Main Assembly Workbench", PrimitiveType.Cube, new Vector3(2, 0.8f, 3), new Color(1f, 0.5f, 0f), new Vector3(2f, 0.5f, 1f));
    }
    
    void CreateTool(string name, PrimitiveType type, Vector3 position, Color color, Vector3 scale)
    {
        GameObject tool = GameObject.CreatePrimitive(type);
        tool.name = $"Tool_{name.Replace(" ", "_")}";
        tool.transform.position = position;
        tool.transform.localScale = scale;
        tool.GetComponent<Renderer>().material.color = color;
        
        // Add highlight
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
        
        Debug.Log($"✅ Created tool: {name}");
    }
    
    void CreateUI()
    {
        Debug.Log("Creating UI...");
        
        // Create Canvas
        GameObject canvasGO = new GameObject("Canvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();
        
        // Create UI Panel
        GameObject panelGO = new GameObject("InfoPanel");
        panelGO.transform.SetParent(canvasGO.transform, false);
        
        Image panel = panelGO.AddComponent<Image>();
        panel.color = new Color(0, 0, 0, 0.7f);
        
        RectTransform panelRect = panelGO.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0, 0.8f);
        panelRect.anchorMax = new Vector2(1, 1);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        
        // Create Info Text
        GameObject textGO = new GameObject("InfoText");
        textGO.transform.SetParent(panelGO.transform, false);
        
        Text infoText = textGO.AddComponent<Text>();
        infoText.text = "JSON Warehouse Scene - 3 Technicians, 5 Tools, Interactive Plans\nClick on technicians to interact • WASD to move selected technician";
        infoText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        infoText.fontSize = 16;
        infoText.color = Color.white;
        infoText.alignment = TextAnchor.MiddleCenter;
        
        RectTransform textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10, 5);
        textRect.offsetMax = new Vector2(-10, -5);
    }
    
    void SetupCamera()
    {
        Debug.Log("Setting up camera...");
        
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            mainCamera.transform.position = new Vector3(0, 8, -10);
            mainCamera.transform.rotation = Quaternion.Euler(30, 0, 0);
            mainCamera.fieldOfView = 60;
        }
    }
    
    void Update()
    {
        // Quick regeneration key
        if (Input.GetKeyDown(KeyCode.F5))
        {
            Debug.Log("🔄 F5 pressed - Regenerating scene...");
            
            // Clear existing generated objects
            ClearGeneratedObjects();
            
            // Regenerate everything
            ForceGenerateEverything();
        }
    }
    
    void ClearGeneratedObjects()
    {
        // Find and destroy generated objects
        string[] objectPrefixes = {"Environment_", "Plan_Platform_", "Technician_", "Tool_", "Canvas"};
        
        foreach (string prefix in objectPrefixes)
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
    }
}
