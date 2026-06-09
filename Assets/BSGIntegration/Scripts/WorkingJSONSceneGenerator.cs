using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Collections.Generic;

public class WorkingJSONSceneGenerator : MonoBehaviour
{
    [Header("Scene Generation")]
    public bool generateOnStart = true;
    public string jsonFileName = "basicUi.json";
    
    [Header("Debug")]
    public bool showDebugLogs = true;
    
    private SceneData sceneData;
    
    void Start()
    {
        if (generateOnStart)
        {
            DebugLog("🚀 WORKING JSON SCENE GENERATOR STARTING...");
            GenerateCompleteScene();
        }
    }
    
    void GenerateCompleteScene()
    {
        // Step 1: Load JSON
        if (!LoadJSONData())
        {
            DebugLog("❌ Failed to load JSON, creating fallback scene");
            CreateFallbackScene();
            return;
        }
        
        // Step 2: Clear existing objects
        ClearExistingObjects();
        
        // Step 3: Create environment
        CreateEnvironment();
        
        // Step 4: Create tools from JSON
        CreateToolsFromJSON();
        
        // Step 5: Create agents from JSON
        CreateAgentsFromJSON();
        
        // Step 6: Create UI
        CreateSimpleUI();
        
        // Step 7: Setup camera
        SetupCamera();
        
        DebugLog("✅ SCENE GENERATION COMPLETE!");
        DebugLog("Check your hierarchy - you should see all objects now!");
    }
    
    bool LoadJSONData()
    {
        try
        {
            string jsonPath = Path.Combine(Application.dataPath, "JsonFile", jsonFileName);
            DebugLog($"Looking for JSON at: {jsonPath}");
            
            if (File.Exists(jsonPath))
            {
                string json = File.ReadAllText(jsonPath);
                sceneData = JsonUtility.FromJson<SceneData>(json);
                DebugLog($"✅ JSON loaded successfully: {sceneData.scene_id}");
                DebugLog($"Tools found: {sceneData.initialStates?.Count ?? 0}");
                DebugLog($"Agents found: {sceneData.agentProfiles?.Count ?? 0}");
                return true;
            }
            else
            {
                DebugLog($"❌ JSON file not found at: {jsonPath}");
                return false;
            }
        }
        catch (System.Exception e)
        {
            DebugLog($"❌ Error loading JSON: {e.Message}");
            return false;
        }
    }
    
    void ClearExistingObjects()
    {
        // Clear any existing generated objects
        string[] prefixes = {"GENERATED_", "JSON_", "Tool_", "Agent_", "Environment_"};
        
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
        
        DebugLog("🧹 Cleared existing generated objects");
    }
    
    void CreateEnvironment()
    {
        DebugLog("🏗️ Creating environment...");
        
        // Create floor
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "GENERATED_Floor";
        floor.transform.position = new Vector3(0, 0, 0);
        floor.transform.localScale = new Vector3(3, 1, 3); // 30x30 units
        
        // Set floor color to gray
        Renderer floorRenderer = floor.GetComponent<Renderer>();
        Material floorMaterial = new Material(Shader.Find("Standard"));
        floorMaterial.color = Color.gray;
        floorRenderer.material = floorMaterial;
        
        // Create walls
        CreateWall("GENERATED_Wall_North", new Vector3(0, 3, 15), new Vector3(30, 6, 1));
        CreateWall("GENERATED_Wall_South", new Vector3(0, 3, -15), new Vector3(30, 6, 1));
        CreateWall("GENERATED_Wall_East", new Vector3(15, 3, 0), new Vector3(1, 6, 30));
        CreateWall("GENERATED_Wall_West", new Vector3(-15, 3, 0), new Vector3(1, 6, 30));
        
        DebugLog("✅ Environment created");
    }
    
    void CreateWall(string name, Vector3 position, Vector3 scale)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.position = position;
        wall.transform.localScale = scale;
        
        // Set wall color to light blue
        Renderer wallRenderer = wall.GetComponent<Renderer>();
        Material wallMaterial = new Material(Shader.Find("Standard"));
        wallMaterial.color = new Color(0.7f, 0.8f, 1f, 1f);
        wallRenderer.material = wallMaterial;
    }
    
    void CreateToolsFromJSON()
    {
        if (sceneData?.initialStates == null)
        {
            DebugLog("⚠️ No tools data found in JSON");
            return;
        }
        
        DebugLog($"🔧 Creating {sceneData.initialStates.Count} tools from JSON...");
        
        foreach (var toolEntry in sceneData.initialStates)
        {
            string toolId = toolEntry.Key;
            ToolState tool = toolEntry.Value;
            
            // Create tool object
            GameObject toolObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            toolObj.name = $"GENERATED_Tool_{toolId}";
            
            // Set position
            if (tool.position != null)
            {
                toolObj.transform.position = new Vector3(tool.position.x, tool.position.y, tool.position.z);
            }
            
            // Set color
            Renderer toolRenderer = toolObj.GetComponent<Renderer>();
            Material toolMaterial = new Material(Shader.Find("Standard"));
            
            switch (tool.color?.ToLower())
            {
                case "red":
                    toolMaterial.color = Color.red;
                    break;
                case "yellow":
                    toolMaterial.color = Color.yellow;
                    break;
                case "blue":
                    toolMaterial.color = Color.blue;
                    break;
                case "green":
                    toolMaterial.color = Color.green;
                    break;
                case "orange":
                    toolMaterial.color = new Color(1f, 0.5f, 0f);
                    break;
                default:
                    toolMaterial.color = Color.white;
                    break;
            }
            
            toolRenderer.material = toolMaterial;

            DebugLog($"✅ Created tool: {tool.name} at {tool.position?.x}, {tool.position?.y}, {tool.position?.z}");
        }
    }
    
    void CreateAgentsFromJSON()
    {
        if (sceneData?.agentProfiles == null)
        {
            DebugLog("⚠️ No agents data found in JSON");
            return;
        }
        
        DebugLog($"👥 Creating {sceneData.agentProfiles.Count} agents from JSON...");
        
        foreach (var agentEntry in sceneData.agentProfiles)
        {
            string agentId = agentEntry.Key;
            AgentProfile agent = agentEntry.Value;
            
            // Create agent object
            GameObject agentObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            agentObj.name = $"GENERATED_Agent_{agentId}";
            
            // Set position
            if (agent.position != null)
            {
                agentObj.transform.position = new Vector3(agent.position.x, agent.position.y, agent.position.z);
            }
            
            // Set color
            Renderer agentRenderer = agentObj.GetComponent<Renderer>();
            Material agentMaterial = new Material(Shader.Find("Standard"));
            
            switch (agent.color?.ToLower())
            {
                case "green":
                    agentMaterial.color = Color.green;
                    break;
                case "blue":
                    agentMaterial.color = Color.blue;
                    break;
                case "purple":
                    agentMaterial.color = new Color(0.5f, 0f, 1f);
                    break;
                default:
                    agentMaterial.color = Color.cyan;
                    break;
            }
            
            agentRenderer.material = agentMaterial;

            DebugLog($"✅ Created agent: {agent.name} at {agent.position?.x}, {agent.position?.y}, {agent.position?.z}");
        }
    }
    
    void CreateSimpleUI()
    {
        DebugLog("🖥️ Creating UI...");
        
        // Create Canvas
        GameObject canvasObj = new GameObject("GENERATED_Canvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();
        
        // Create info panel
        GameObject panelObj = new GameObject("GENERATED_InfoPanel");
        panelObj.transform.SetParent(canvasObj.transform);
        
        RectTransform panelRect = panelObj.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0, 0.8f);
        panelRect.anchorMax = new Vector2(1, 1);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        
        Image panelImage = panelObj.AddComponent<Image>();
        panelImage.color = new Color(0, 0, 0, 0.7f);
        
        // Create text
        GameObject textObj = new GameObject("GENERATED_InfoText");
        textObj.transform.SetParent(panelObj.transform);
        
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10, 10);
        textRect.offsetMax = new Vector2(-10, -10);
        
        Text infoText = textObj.AddComponent<Text>();
        infoText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        infoText.fontSize = 16;
        infoText.color = Color.white;
        infoText.alignment = TextAnchor.MiddleLeft;
        
        // Set info text content
        if (sceneData != null)
        {
            string infoContent = $"Scene: {sceneData.scene_id}\n";
            if (sceneData.plan != null)
            {
                infoContent += $"Plan: {sceneData.plan.planName}\n";
                infoContent += $"Duration: {sceneData.plan.estimatedTotalDuration} minutes\n";
            }
            infoContent += $"Tools: {sceneData.initialStates?.Count ?? 0} | Agents: {sceneData.agentProfiles?.Count ?? 0}";
            infoText.text = infoContent;
        }
        else
        {
            infoText.text = "Fallback Scene - No JSON Data Loaded";
        }
        
        DebugLog("✅ UI created");
    }
    
    void SetupCamera()
    {
        DebugLog("📷 Setting up camera...");
        
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            GameObject cameraObj = new GameObject("GENERATED_Main Camera");
            mainCamera = cameraObj.AddComponent<Camera>();
            cameraObj.tag = "MainCamera";
        }
        
        // Position camera to see the entire scene
        mainCamera.transform.position = new Vector3(0, 15, -10);
        mainCamera.transform.rotation = Quaternion.Euler(45, 0, 0);
        
        DebugLog("✅ Camera positioned");
    }
    
    void CreateFallbackScene()
    {
        DebugLog("🔄 Creating fallback scene...");
        
        // Create a simple fallback scene
        CreateEnvironment();
        
        // Create some basic objects
        GameObject fallbackTool = GameObject.CreatePrimitive(PrimitiveType.Cube);
        fallbackTool.name = "GENERATED_FallbackTool";
        fallbackTool.transform.position = new Vector3(0, 1, 0);
        
        Renderer fallbackRenderer = fallbackTool.GetComponent<Renderer>();
        Material fallbackMaterial = new Material(Shader.Find("Standard"));
        fallbackMaterial.color = Color.magenta;
        fallbackRenderer.material = fallbackMaterial;
        
        CreateSimpleUI();
        SetupCamera();
        
        DebugLog("✅ Fallback scene created");
    }
    
    void DebugLog(string message)
    {
        if (showDebugLogs)
        {
            Debug.Log(message);
        }
    }
    
    // Public method to regenerate scene manually
    [ContextMenu("Regenerate Scene")]
    public void RegenerateScene()
    {
        GenerateCompleteScene();
    }
}
