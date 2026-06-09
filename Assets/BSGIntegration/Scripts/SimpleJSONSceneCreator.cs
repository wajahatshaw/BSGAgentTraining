using UnityEngine;
using UnityEngine.UI;

public class SimpleJSONSceneCreator : MonoBehaviour
{
    [Header("Scene Setup")]
    public bool createOnStart = true;
    public Color backgroundColor = new Color(0.2f, 0.2f, 0.3f);
    
    void Start()
    {
        if (createOnStart)
        {
            CreateJSONScene();
        }
    }
    
    public void CreateJSONScene()
    {
        Debug.Log("=== CREATING SIMPLE JSON SCENE ===");
        
        // Clear existing content
        ClearExistingContent();
        
        // Create basic environment
        CreateEnvironment();
        
        // Create demo objects (since JSON might not be loaded)
        CreateDemoObjects();
        
        // Create UI panels
        CreateUIPanels();
        
        // Setup camera
        SetupCamera();
        
        Debug.Log("Simple JSON scene created successfully!");
    }
    
    void ClearExistingContent()
    {
        Debug.Log("Clearing existing content...");
        
        // Find and destroy existing objects (except camera and light)
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        foreach (GameObject obj in allObjects)
        {
            if (obj != gameObject && 
                obj.name != "Main Camera" && 
                obj.name != "Directional Light" &&
                !obj.name.StartsWith("JSON_"))
            {
                DestroyImmediate(obj);
            }
        }
    }
    
    void CreateEnvironment()
    {
        Debug.Log("Creating environment...");
        
        // Create floor
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "JSON_Floor";
        floor.transform.position = Vector3.zero;
        floor.transform.localScale = Vector3.one * 3f;
        floor.GetComponent<Renderer>().material.color = new Color(0.3f, 0.3f, 0.3f);
        
        // Create walls
        CreateWall("JSON_North_Wall", new Vector3(0, 4, 15), new Vector3(30, 8, 1));
        CreateWall("JSON_South_Wall", new Vector3(0, 4, -15), new Vector3(30, 8, 1));
        CreateWall("JSON_East_Wall", new Vector3(15, 4, 0), new Vector3(1, 8, 30));
        CreateWall("JSON_West_Wall", new Vector3(-15, 4, 0), new Vector3(1, 8, 30));
    }
    
    void CreateWall(string name, Vector3 position, Vector3 scale)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.position = position;
        wall.transform.localScale = scale;
        wall.GetComponent<Renderer>().material.color = new Color(0.4f, 0.6f, 0.8f);
    }
    
    void CreateDemoObjects()
    {
        Debug.Log("Creating demo objects...");
        
        // Create demo tools
        CreateDemoTool("JSON_Repair_Tool_001", new Vector3(-5, 0.5f, -5), Color.red);
        CreateDemoTool("JSON_Toolbox_002", new Vector3(5, 0.5f, -5), Color.yellow);
        CreateDemoTool("JSON_Machine_003", new Vector3(0, 0.5f, 5), Color.blue);
        
        // Create demo agents
        CreateDemoAgent("JSON_Technician_A", new Vector3(-8, 1, 0), Color.green);
        CreateDemoAgent("JSON_Technician_B", new Vector3(8, 1, 0), Color.cyan);
    }
    
    void CreateDemoTool(string name, Vector3 position, Color color)
    {
        GameObject tool = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tool.name = name;
        tool.transform.position = position;
        tool.GetComponent<Renderer>().material.color = color;
        
        // Add highlight
        AddHighlight(tool, "TOOL");
    }
    
    void CreateDemoAgent(string name, Vector3 position, Color color)
    {
        GameObject agent = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        agent.name = name;
        agent.transform.position = position;
        agent.GetComponent<Renderer>().material.color = color;
        
        // Add highlight
        AddHighlight(agent, "AGENT");
    }
    
    void AddHighlight(GameObject obj, string type)
    {
        GameObject highlight = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        highlight.name = $"{type}_Highlight";
        highlight.transform.SetParent(obj.transform);
        highlight.transform.localPosition = Vector3.up * 2f;
        highlight.transform.localScale = Vector3.one * 0.3f;
        
        highlight.GetComponent<Renderer>().material.color = Color.yellow;
        
        Light light = highlight.AddComponent<Light>();
        light.color = Color.yellow;
        light.intensity = 2f;
        light.range = 3f;
    }
    
    void CreateUIPanels()
    {
        Debug.Log("Creating UI panels...");
        
        // Create Canvas
        GameObject canvasGO = new GameObject("JSON_UI_Canvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        
        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        
        canvasGO.AddComponent<GraphicRaycaster>();
        
        // Create panels
        CreatePanel(canvas, "JSON_Worker_Panel", new Vector2(-400, 200), "WORKER INFO");
        CreatePanel(canvas, "JSON_Task_Panel", new Vector2(400, 200), "CURRENT TASK");
        CreatePanel(canvas, "JSON_Details_Panel", new Vector2(0, -200), "TASK DETAILS");
        CreatePanel(canvas, "JSON_Progress_Panel", new Vector2(0, 100), "PROGRESS");
    }
    
    void CreatePanel(Canvas canvas, string name, Vector2 position, string title)
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
        
        // Content placeholder
        CreateText(panel, "Content", "JSON Content Here", new Vector2(0, 0), 14, new Color(0.7f, 0.7f, 0.7f));
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
    
    void SetupCamera()
    {
        Debug.Log("Setting up camera...");
        
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            mainCamera.transform.position = new Vector3(0, 15, -20);
            mainCamera.transform.LookAt(Vector3.zero);
            mainCamera.backgroundColor = backgroundColor;
        }
    }
    
    // Public methods
    public void RegenerateScene()
    {
        CreateJSONScene();
    }
    
    public void ClearScene()
    {
        ClearExistingContent();
    }
}
