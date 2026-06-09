using UnityEngine;

public class SimpleCleanSceneGenerator : MonoBehaviour
{
    [Header("Clean Scene Generation")]
    public bool generateOnStart = true;
    
    void Start()
    {
        if (generateOnStart)
        {
            Debug.Log("🚀 Clean Scene Generator Starting...");
            CreateCleanScene();
        }
    }
    
    void CreateCleanScene()
    {
        // Clear existing objects
        ClearExistingObjects();
        
        // Create clean plan surface
        CreateCleanPlanSurface();
        
        // Create clean sphere technicians
        CreateCleanTechnicians();
        
        // Create clean tools
        CreateCleanTools();
        
        // Setup optimal camera
        SetupCleanCamera();
        
        Debug.Log("✅ Clean Scene Created Successfully!");
    }
    
    void ClearExistingObjects()
    {
        string[] prefixes = {"Plan_", "Technician_", "Tool_", "JSON_"};
        
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
    
    void CreateCleanPlanSurface()
    {
        Debug.Log("Creating clean plan surface...");
        
        // Create main plan platform with clean professional color
        GameObject planSurface = GameObject.CreatePrimitive(PrimitiveType.Cube);
        planSurface.name = "Plan_Surface_Clean";
        planSurface.transform.position = new Vector3(0, 0, 0);
        planSurface.transform.localScale = new Vector3(20f, 0.3f, 15f);
        
        // Clean professional gray-blue color (no metallic properties)
        planSurface.GetComponent<Renderer>().material.color = new Color(0.4f, 0.5f, 0.6f, 1f);
        
        Debug.Log("✅ Created clean plan surface");
        
        // Create clean borders
        CreateCleanBorder("Plan_Border_North", new Vector3(0, 0.4f, 7.5f), new Vector3(20f, 0.2f, 0.3f));
        CreateCleanBorder("Plan_Border_South", new Vector3(0, 0.4f, -7.5f), new Vector3(20f, 0.2f, 0.3f));
        CreateCleanBorder("Plan_Border_East", new Vector3(10f, 0.4f, 0), new Vector3(0.3f, 0.2f, 15f));
        CreateCleanBorder("Plan_Border_West", new Vector3(-10f, 0.4f, 0), new Vector3(0.3f, 0.2f, 15f));
        
        // Create clean plan text
        CreateCleanPlanText();
    }
    
    void CreateCleanBorder(string name, Vector3 position, Vector3 scale)
    {
        GameObject border = GameObject.CreatePrimitive(PrimitiveType.Cube);
        border.name = name;
        border.transform.position = position;
        border.transform.localScale = scale;
        
        // Clean border color (no metallic properties)
        border.GetComponent<Renderer>().material.color = new Color(0.3f, 0.4f, 0.5f, 1f);
        
        Debug.Log($"✅ Created {name}");
    }
    
    void CreateCleanPlanText()
    {
        GameObject planText = new GameObject("Plan_Text_Clean");
        planText.transform.position = new Vector3(0, 0.8f, -5);
        
        TextMesh textMesh = planText.AddComponent<TextMesh>();
        textMesh.text = "WAREHOUSE MAINTENANCE PLAN\nDaily Equipment Protocol";
        textMesh.fontSize = 14;
        textMesh.color = Color.white;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.transform.rotation = Quaternion.Euler(90, 0, 0);
        
        Debug.Log("✅ Created clean plan text");
    }
    
    void CreateCleanTechnicians()
    {
        Debug.Log("Creating clean technicians...");
        
        // Alex Rodriguez - Clean Green
        CreateCleanTechnician(
            "Technician_Alex_Rodriguez",
            "Alex Rodriguez\nSenior Technician",
            new Vector3(-6, 1.5f, 0),
            new Color(0.2f, 0.7f, 0.3f, 1f) // Clean green
        );
        
        // Maria Santos - Clean Blue
        CreateCleanTechnician(
            "Technician_Maria_Santos",
            "Maria Santos\nOperations Supervisor",
            new Vector3(0, 1.5f, 6),
            new Color(0.2f, 0.4f, 0.8f, 1f) // Clean blue
        );
        
        // David Kim - Clean Purple
        CreateCleanTechnician(
            "Technician_David_Kim",
            "David Kim\nSafety Inspector",
            new Vector3(6, 1.5f, 0),
            new Color(0.6f, 0.3f, 0.7f, 1f) // Clean purple
        );
        
        Debug.Log("✅ Created 3 clean technicians");
    }
    
    void CreateCleanTechnician(string name, string labelText, Vector3 position, Color color)
    {
        Debug.Log($"Creating {name}...");
        
        // Create main sphere
        GameObject technician = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        technician.name = name;
        technician.transform.position = position;
        technician.transform.localScale = Vector3.one * 1.8f;
        
        // Simple clean color (no metallic properties)
        technician.GetComponent<Renderer>().material.color = color;
        
        // Create clean head
        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        head.transform.SetParent(technician.transform);
        head.transform.localPosition = new Vector3(0, 1.1f, 0);
        head.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
        
        // Clean head color
        head.GetComponent<Renderer>().material.color = Color.Lerp(color, Color.white, 0.3f);
        
        // Create clean name label
        CreateCleanLabel(technician, labelText);
        
        // Create clean status indicator
        CreateCleanStatusIndicator(technician);
        
        // Add clean movement component
        CleanTechnicianMover mover = technician.AddComponent<CleanTechnicianMover>();
        mover.technicianName = labelText.Split('\n')[0];
        mover.originalPosition = position;
        
        Debug.Log($"✅ Created {name}");
    }
    
    void CreateCleanLabel(GameObject parent, string text)
    {
        GameObject nameLabel = new GameObject("NameLabel");
        nameLabel.transform.SetParent(parent.transform);
        nameLabel.transform.localPosition = new Vector3(0, 2.5f, 0);
        
        TextMesh nameText = nameLabel.AddComponent<TextMesh>();
        nameText.text = text;
        nameText.fontSize = 8;
        nameText.color = Color.white;
        nameText.anchor = TextAnchor.MiddleCenter;
        nameText.alignment = TextAlignment.Center;
        nameText.fontStyle = FontStyle.Bold;
    }
    
    void CreateCleanStatusIndicator(GameObject parent)
    {
        GameObject statusIndicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        statusIndicator.name = "StatusIndicator";
        statusIndicator.transform.SetParent(parent.transform);
        statusIndicator.transform.localPosition = new Vector3(0, 2f, 0);
        statusIndicator.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);
        
        // Clean status color
        statusIndicator.GetComponent<Renderer>().material.color = new Color(0.2f, 0.8f, 0.2f, 1f);
    }
    
    void CreateCleanTools()
    {
        Debug.Log("Creating clean tools...");
        
        // Industrial Motor Unit - Clean Red
        CreateCleanTool("Tool_Industrial_Motor", "Industrial Motor Unit", 
                       new Vector3(-4, 0.5f, -5), new Color(0.8f, 0.2f, 0.2f, 1f), PrimitiveType.Cylinder);
        
        // Secure Toolbox - Clean Orange
        CreateCleanTool("Tool_Secure_Toolbox", "Secure Toolbox Alpha", 
                       new Vector3(0, 0.5f, -5), new Color(0.9f, 0.6f, 0.1f, 1f), PrimitiveType.Cube);
        
        // Hydraulic Lift - Clean Blue
        CreateCleanTool("Tool_Hydraulic_Lift", "Hydraulic Lift Station", 
                       new Vector3(4, 0.5f, -5), new Color(0.2f, 0.5f, 0.9f, 1f), PrimitiveType.Cube, new Vector3(1.5f, 1.2f, 1.5f));
        
        // Safety Station - Clean Green
        CreateCleanTool("Tool_Safety_Station", "Safety Inspection Station", 
                       new Vector3(-2, 0.5f, 3), new Color(0.3f, 0.7f, 0.3f, 1f), PrimitiveType.Cube);
        
        // Assembly Workbench - Clean Gray
        CreateCleanTool("Tool_Assembly_Workbench", "Main Assembly Workbench", 
                       new Vector3(2, 0.8f, 3), new Color(0.6f, 0.6f, 0.7f, 1f), PrimitiveType.Cube, new Vector3(2f, 0.6f, 1.2f));
        
        Debug.Log("✅ Created 5 clean tools");
    }
    
    void CreateCleanTool(string name, string labelText, Vector3 position, Color color, PrimitiveType type, Vector3? scale = null)
    {
        Debug.Log($"Creating {name}...");
        
        GameObject tool = GameObject.CreatePrimitive(type);
        tool.name = name;
        tool.transform.position = position;
        
        if (scale.HasValue)
        {
            tool.transform.localScale = scale.Value;
        }
        
        // Simple clean color (no metallic properties)
        tool.GetComponent<Renderer>().material.color = color;
        
        // Create clean tool label
        CreateCleanToolLabel(tool, labelText);
        
        Debug.Log($"✅ Created {name}");
    }
    
    void CreateCleanToolLabel(GameObject tool, string text)
    {
        GameObject toolLabel = new GameObject("ToolLabel");
        toolLabel.transform.SetParent(tool.transform);
        toolLabel.transform.localPosition = new Vector3(0, 2f, 0);
        
        TextMesh labelMesh = toolLabel.AddComponent<TextMesh>();
        labelMesh.text = text;
        labelMesh.fontSize = 6;
        labelMesh.color = Color.white;
        labelMesh.anchor = TextAnchor.MiddleCenter;
        labelMesh.alignment = TextAlignment.Center;
        labelMesh.fontStyle = FontStyle.Bold;
    }
    
    void SetupCleanCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            mainCamera.transform.position = new Vector3(0, 15, -10);
            mainCamera.transform.rotation = Quaternion.Euler(35, 0, 0);
            mainCamera.fieldOfView = 50;
            
            // Clean camera background
            mainCamera.backgroundColor = new Color(0.1f, 0.1f, 0.2f, 1f);
            
            Debug.Log("✅ Camera positioned cleanly");
        }
    }
    
    void Update()
    {
        // Simple key handling
        if (Input.GetKeyDown("f5"))
        {
            Debug.Log("🔄 F5 pressed - Regenerating clean scene...");
            CreateCleanScene();
        }
        
        if (Input.GetKeyDown("g"))
        {
            Debug.Log("🔄 G pressed - Manual clean generation...");
            CreateCleanScene();
        }
        
        if (Input.GetKeyDown("h"))
        {
            ShowCleanHelp();
        }
    }
    
    void ShowCleanHelp()
    {
        Debug.Log("=== CLEAN SCENE GENERATOR ===");
        Debug.Log("F5: Regenerate scene");
        Debug.Log("G: Manual generation");
        Debug.Log("H: Show help");
        Debug.Log("Clean colors: Gray-blue plan, professional technician colors");
    }
}
