using UnityEngine;

public class SimpleNoUIScene : MonoBehaviour
{
    [Header("Scene Generation")]
    public bool generateOnStart = true;
    
    void Start()
    {
        if (generateOnStart)
        {
            Debug.Log("🚀 Creating Simple Plan Scene (No UI)...");
            CreatePlanScene();
        }
    }
    
    void CreatePlanScene()
    {
        // Clear any existing objects first
        ClearScene();
        
        // Create the main plan platform (like the orange outlined area in your image)
        CreateMainPlan();
        
        // Create 3 sphere technicians on the plan
        CreateSphereTechnicians();
        
        // Setup camera to view the scene properly
        SetupCamera();
        
        Debug.Log("✅ Plan Scene Created Successfully!");
    }
    
    void ClearScene()
    {
        // Remove any existing generated objects
        GameObject[] existingObjects = GameObject.FindObjectsOfType<GameObject>();
        foreach (GameObject obj in existingObjects)
        {
            if (obj.name.StartsWith("Plan_") || obj.name.StartsWith("Technician_") || 
                obj.name.StartsWith("Environment_"))
            {
                DestroyImmediate(obj);
            }
        }
    }
    
    void CreateMainPlan()
    {
        Debug.Log("Creating main plan platform...");
        
        // Create the main plan platform (flat, like the orange area in your image)
        GameObject mainPlan = GameObject.CreatePrimitive(PrimitiveType.Cube);
        mainPlan.name = "Plan_MainPlatform";
        mainPlan.transform.position = new Vector3(0, 0, 0);
        mainPlan.transform.localScale = new Vector3(20f, 0.2f, 15f); // Large flat platform
        
        // Make it orange like in your image
        Renderer planRenderer = mainPlan.GetComponent<Renderer>();
        planRenderer.material.color = new Color(1f, 0.6f, 0.2f); // Orange color
        
        // Add plan outline/border
        CreatePlanBorder();
        
        // Add plan text
        CreatePlanText();
        
        Debug.Log("✅ Main plan platform created");
    }
    
    void CreatePlanBorder()
    {
        // Create border lines around the plan (like the orange outline in your image)
        Color borderColor = new Color(1f, 0.4f, 0f); // Darker orange for border
        
        // North border
        GameObject northBorder = GameObject.CreatePrimitive(PrimitiveType.Cube);
        northBorder.name = "Plan_Border_North";
        northBorder.transform.position = new Vector3(0, 0.2f, 7.5f);
        northBorder.transform.localScale = new Vector3(20f, 0.1f, 0.2f);
        northBorder.GetComponent<Renderer>().material.color = borderColor;
        
        // South border
        GameObject southBorder = GameObject.CreatePrimitive(PrimitiveType.Cube);
        southBorder.name = "Plan_Border_South";
        southBorder.transform.position = new Vector3(0, 0.2f, -7.5f);
        southBorder.transform.localScale = new Vector3(20f, 0.1f, 0.2f);
        southBorder.GetComponent<Renderer>().material.color = borderColor;
        
        // East border
        GameObject eastBorder = GameObject.CreatePrimitive(PrimitiveType.Cube);
        eastBorder.name = "Plan_Border_East";
        eastBorder.transform.position = new Vector3(10f, 0.2f, 0);
        eastBorder.transform.localScale = new Vector3(0.2f, 0.1f, 15f);
        eastBorder.GetComponent<Renderer>().material.color = borderColor;
        
        // West border
        GameObject westBorder = GameObject.CreatePrimitive(PrimitiveType.Cube);
        westBorder.name = "Plan_Border_West";
        westBorder.transform.position = new Vector3(-10f, 0.2f, 0);
        westBorder.transform.localScale = new Vector3(0.2f, 0.1f, 15f);
        westBorder.GetComponent<Renderer>().material.color = borderColor;
    }
    
    void CreatePlanText()
    {
        // Add text on the plan platform
        GameObject planTextGO = new GameObject("Plan_Text");
        planTextGO.transform.position = new Vector3(0, 0.5f, 0);
        
        TextMesh planText = planTextGO.AddComponent<TextMesh>();
        planText.text = "WAREHOUSE MAINTENANCE PLAN\nDaily Equipment Protocol";
        planText.fontSize = 20;
        planText.color = Color.white;
        planText.anchor = TextAnchor.MiddleCenter;
        planText.alignment = TextAlignment.Center;
        
        // Make text face up
        planTextGO.transform.rotation = Quaternion.Euler(90, 0, 0);
    }
    
    void CreateSphereTechnicians()
    {
        Debug.Log("Creating sphere technicians...");
        
        // Create 3 sphere technicians positioned on the plan
        CreateSphereTechnician("Alex Rodriguez", "Senior Technician", new Vector3(-5, 1, 2), Color.green);
        CreateSphereTechnician("Maria Santos", "Operations Supervisor", new Vector3(0, 1, 2), Color.blue);
        CreateSphereTechnician("David Kim", "Safety Inspector", new Vector3(5, 1, 2), Color.magenta);
        
        Debug.Log("✅ All sphere technicians created");
    }
    
    void CreateSphereTechnician(string name, string role, Vector3 position, Color color)
    {
        Debug.Log($"Creating sphere technician: {name}");
        
        // Create main sphere for technician
        GameObject technician = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        technician.name = $"Technician_{name.Replace(" ", "_")}";
        technician.transform.position = position;
        technician.transform.localScale = Vector3.one * 1.5f; // Make sphere bigger
        
        // Set technician color
        Renderer techRenderer = technician.GetComponent<Renderer>();
        techRenderer.material.color = color;
        
        // Add a smaller sphere on top as "head"
        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        head.transform.SetParent(technician.transform);
        head.transform.localPosition = new Vector3(0, 1.2f, 0);
        head.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
        head.GetComponent<Renderer>().material.color = Color.Lerp(color, Color.white, 0.3f);
        
        // Add name label above technician
        GameObject nameLabel = new GameObject("NameLabel");
        nameLabel.transform.SetParent(technician.transform);
        nameLabel.transform.localPosition = new Vector3(0, 2.5f, 0);
        
        TextMesh nameText = nameLabel.AddComponent<TextMesh>();
        nameText.text = $"{name}\n{role}";
        nameText.fontSize = 8;
        nameText.color = Color.white;
        nameText.anchor = TextAnchor.MiddleCenter;
        nameText.alignment = TextAlignment.Center;
        
        // Add status indicator
        GameObject statusIndicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        statusIndicator.name = "StatusIndicator";
        statusIndicator.transform.SetParent(technician.transform);
        statusIndicator.transform.localPosition = new Vector3(0, 2f, 0);
        statusIndicator.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
        statusIndicator.GetComponent<Renderer>().material.color = Color.green;
        
        // Add simple movement component
        SimpleTechnicianMover mover = technician.AddComponent<SimpleTechnicianMover>();
        mover.technicianName = name;
        mover.originalPosition = position;
        
        Debug.Log($"✅ Created sphere technician: {name} at {position}");
    }
    
    void SetupCamera()
    {
        Debug.Log("Setting up camera...");
        
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            // Position camera to view the plan from above at an angle
            mainCamera.transform.position = new Vector3(0, 12, -8);
            mainCamera.transform.rotation = Quaternion.Euler(45, 0, 0);
            mainCamera.fieldOfView = 60;
        }
        
        Debug.Log("✅ Camera positioned");
    }
    
    void Update()
    {
        // F5 to regenerate scene
        if (Input.GetKeyDown(KeyCode.F5))
        {
            Debug.Log("🔄 F5 pressed - Regenerating scene...");
            CreatePlanScene();
        }
        
        // Display instructions in console
        if (Input.GetKeyDown(KeyCode.H))
        {
            Debug.Log("=== CONTROLS ===");
            Debug.Log("Left Click: Select sphere technician");
            Debug.Log("WASD: Move selected technician");
            Debug.Log("R: Return to original position");
            Debug.Log("F5: Regenerate scene");
            Debug.Log("H: Show this help");
        }
    }
}
