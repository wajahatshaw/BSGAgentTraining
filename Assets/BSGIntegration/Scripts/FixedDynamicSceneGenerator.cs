using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Collections.Generic;
using System.Collections;

public class FixedDynamicSceneGenerator : MonoBehaviour
{
    [Header("Scene Generation")]
    public bool generateOnStart = true;
    public string jsonFileName = "basicUi.json";
    
    [Header("Debug")]
    public bool showDebugLogs = true;
    
    private SceneData sceneData;
    private List<GameObject> generatedObjects = new List<GameObject>();
    
    void Start()
    {
        if (generateOnStart)
        {
            DebugLog("🚀 FIXED DYNAMIC SCENE GENERATOR STARTING...");
            GenerateFixedScene();
        }
    }
    
    void GenerateFixedScene()
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
        
        // Step 3: Create environment with proper materials
        CreateEnvironmentWithProperMaterials();
        
        // Step 4: Create tools with working materials
        CreateToolsWithWorkingMaterials();
        
        // Step 5: Create moving agents with proper setup
        CreateMovingAgentsWithProperSetup();
        
        // Step 6: Create UI
        CreateSimpleUI();
        
        // Step 7: Setup camera
        SetupCamera();
        
        DebugLog("✅ FIXED DYNAMIC SCENE GENERATION COMPLETE!");
        DebugLog("Check your scene - agents should be moving and materials should have proper colors!");
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
        string[] prefixes = {"FIXED_", "ENHANCED_", "GENERATED_", "JSON_", "Tool_", "Agent_", "Environment_"};
        
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
        
        generatedObjects.Clear();
        DebugLog("🧹 Cleared existing generated objects");
    }
    
    void CreateEnvironmentWithProperMaterials()
    {
        DebugLog("🏗️ Creating environment with proper materials...");
        
        // Create floor with working material
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "FIXED_Floor";
        floor.transform.position = new Vector3(0, 0, 0);
        floor.transform.localScale = new Vector3(3, 1, 3); // 30x30 units
        
        // Create working floor material
        Material floorMaterial = CreateWorkingMaterial("Floor_Material", new Color(0.5f, 0.5f, 0.5f, 1f));
        floor.GetComponent<Renderer>().material = floorMaterial;
        generatedObjects.Add(floor);
        
        // Create walls with working materials
        CreateWallWithWorkingMaterial("FIXED_Wall_North", new Vector3(0, 3, 15), new Vector3(30, 6, 1), new Color(0.7f, 0.8f, 1f, 1f));
        CreateWallWithWorkingMaterial("FIXED_Wall_South", new Vector3(0, 3, -15), new Vector3(30, 6, 1), new Color(0.7f, 0.8f, 1f, 1f));
        CreateWallWithWorkingMaterial("FIXED_Wall_East", new Vector3(15, 3, 0), new Vector3(1, 6, 30), new Color(0.7f, 0.8f, 1f, 1f));
        CreateWallWithWorkingMaterial("FIXED_Wall_West", new Vector3(-15, 3, 0), new Vector3(1, 6, 30), new Color(0.7f, 0.8f, 1f, 1f));
        
        DebugLog("✅ Environment created with working materials");
    }
    
    void CreateWallWithWorkingMaterial(string name, Vector3 position, Vector3 scale, Color color)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.position = position;
        wall.transform.localScale = scale;
        
        Material wallMaterial = CreateWorkingMaterial($"{name}_Material", color);
        wall.GetComponent<Renderer>().material = wallMaterial;
        generatedObjects.Add(wall);
    }
    
    Material CreateWorkingMaterial(string materialName, Color baseColor)
    {
        Material material = new Material(Shader.Find("Standard"));
        material.name = materialName;
        material.color = baseColor;
        
        // Set standard material properties for better appearance
        material.SetFloat("_Metallic", 0.2f);
        material.SetFloat("_Glossiness", 0.4f);
        
        DebugLog($"✅ Created working Standard material: {materialName}");
        return material;
    }
    
    void CreateToolsWithWorkingMaterials()
    {
        if (sceneData?.initialStates == null)
        {
            DebugLog("⚠️ No tools data found in JSON");
            return;
        }
        
        DebugLog($"🔧 Creating {sceneData.initialStates.Count} tools with working materials...");
        
        foreach (var toolEntry in sceneData.initialStates)
        {
            string toolId = toolEntry.Key;
            ToolState tool = toolEntry.Value;
            
            // Create tool object
            GameObject toolObj = CreateToolByType(tool.type);
            toolObj.name = $"FIXED_Tool_{toolId}";
            
            // Set position
            if (tool.position != null)
            {
                toolObj.transform.position = new Vector3(tool.position.x, tool.position.y, tool.position.z);
            }
            
            // Apply working material with proper color
            Color toolColor = ParseToolColor(tool.color);
            Material toolMaterial = CreateWorkingMaterial($"Tool_{toolId}_Material", toolColor);
            toolObj.GetComponent<Renderer>().material = toolMaterial;
            
            // Create simple highlight
            CreateSimpleHighlight(toolObj, toolColor, 2f);
            
            generatedObjects.Add(toolObj);
            DebugLog($"✅ Created tool: {tool.name} with color {toolColor} at {tool.position?.x}, {tool.position?.y}, {tool.position?.z}");
        }
    }
    
    GameObject CreateToolByType(string toolType)
    {
        switch (toolType?.ToLower())
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
    
    void CreateMovingAgentsWithProperSetup()
    {
        if (sceneData?.agentProfiles == null)
        {
            DebugLog("⚠️ No agents data found in JSON");
            return;
        }
        
        DebugLog($"👥 Creating {sceneData.agentProfiles.Count} moving agents...");
        
        foreach (var agentEntry in sceneData.agentProfiles)
        {
            string agentId = agentEntry.Key;
            AgentProfile agent = agentEntry.Value;
            
            // Create agent object
            GameObject agentObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            agentObj.name = $"FIXED_Agent_{agentId}";
            
            // Set position
            Vector3 startPosition = new Vector3(-6, 1, 0); // Default position
            if (agent.position != null)
            {
                startPosition = new Vector3(agent.position.x, agent.position.y, agent.position.z);
            }
            agentObj.transform.position = startPosition;
            
            // Apply working material with agent color
            Color agentColor = ParseAgentColor(agent.color);
            Material agentMaterial = CreateWorkingMaterial($"Agent_{agentId}_Material", agentColor);
            agentObj.GetComponent<Renderer>().material = agentMaterial;
            
            // Add WORKING movement controller
            WorkingAgentMovement movementController = agentObj.AddComponent<WorkingAgentMovement>();
            movementController.agentName = agent.name;
            movementController.agentRole = agent.role;
            movementController.startMoving = true;
            
            // Set up movement based on role
            SetupAgentMovement(movementController, agent.role);
            
            // Create simple highlight
            CreateSimpleHighlight(agentObj, agentColor, 3f);
            
            generatedObjects.Add(agentObj);
            DebugLog($"✅ Created moving agent: {agent.name} with color {agentColor}");
        }
    }
    
    void SetupAgentMovement(WorkingAgentMovement controller, string role)
    {
        if (role.Contains("Technician"))
        {
            controller.waypoints = new Vector3[]
            {
                new Vector3(-6, 1, 0),   // Start
                new Vector3(-4, 1, -5),  // Motor
                new Vector3(0, 1, -5),   // Toolbox
                new Vector3(2, 1, 3),    // Workbench
                new Vector3(-6, 1, 0)    // Back to start
            };
            controller.moveSpeed = 2.5f;
            controller.pauseTime = 3f;
        }
        else if (role.Contains("Supervisor"))
        {
            controller.waypoints = new Vector3[]
            {
                new Vector3(0, 1, 6),    // Start
                new Vector3(4, 1, -5),   // Hydraulic Lift
                new Vector3(-2, 1, 3),   // Safety Station
                new Vector3(-4, 1, -5),  // Motor
                new Vector3(0, 1, 6)     // Back to start
            };
            controller.moveSpeed = 1.8f;
            controller.pauseTime = 4f;
        }
        else if (role.Contains("Inspector"))
        {
            controller.waypoints = new Vector3[]
            {
                new Vector3(6, 1, 0),    // Start
                new Vector3(8, 1, 2),    // Observation point 1
                new Vector3(6, 1, 0),    // Back to center
                new Vector3(4, 1, -2),   // Observation point 2
                new Vector3(6, 1, 0)     // Back to start
            };
            controller.moveSpeed = 1.0f;
            controller.pauseTime = 6f;
        }
    }
    
    Color ParseToolColor(string colorString)
    {
        if (string.IsNullOrEmpty(colorString)) return Color.white;
        
        // Try hex color first
        if (colorString.StartsWith("#"))
        {
            if (ColorUtility.TryParseHtmlString(colorString, out Color hexColor))
            {
                return hexColor;
            }
        }
        
        // Fallback to named colors
        switch (colorString.ToLower())
        {
            case "red": return Color.red;
            case "yellow": return Color.yellow;
            case "blue": return Color.blue;
            case "green": return Color.green;
            case "orange": return new Color(1f, 0.5f, 0f);
            default: return Color.white;
        }
    }
    
    Color ParseAgentColor(string colorString)
    {
        if (string.IsNullOrEmpty(colorString)) return Color.cyan;
        
        // Try hex color first
        if (colorString.StartsWith("#"))
        {
            if (ColorUtility.TryParseHtmlString(colorString, out Color hexColor))
            {
                return hexColor;
            }
        }
        
        // Fallback to named colors
        switch (colorString.ToLower())
        {
            case "green": return Color.green;
            case "blue": return Color.blue;
            case "purple": return new Color(0.5f, 0f, 1f);
            default: return Color.cyan;
        }
    }
    
    void CreateSimpleHighlight(GameObject parentObject, Color baseColor, float heightOffset)
    {
        GameObject highlight = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        highlight.name = $"FIXED_Highlight_{parentObject.name}";
        highlight.transform.position = parentObject.transform.position + Vector3.up * heightOffset;
        highlight.transform.localScale = Vector3.one * 0.3f;
        
        // Create bright highlight material
        Color highlightColor = new Color(baseColor.r * 1.5f, baseColor.g * 1.5f, baseColor.b * 1.5f, 1f);
        Material highlightMaterial = CreateWorkingMaterial($"Highlight_{parentObject.name}_Material", highlightColor);
        highlight.GetComponent<Renderer>().material = highlightMaterial;
        
        // Add simple pulsing
        SimplePulse pulser = highlight.AddComponent<SimplePulse>();
        pulser.minScale = 0.3f;
        pulser.maxScale = 0.5f;
        pulser.pulseSpeed = 2f;
        
        generatedObjects.Add(highlight);
    }
    
    void CreateSimpleUI()
    {
        DebugLog("🖥️ Creating UI...");
        
        // Create Canvas
        GameObject canvasObj = new GameObject("FIXED_Canvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();
        
        // Create info panel
        GameObject panelObj = new GameObject("FIXED_InfoPanel");
        panelObj.transform.SetParent(canvasObj.transform);
        
        RectTransform panelRect = panelObj.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0, 0.8f);
        panelRect.anchorMax = new Vector2(1, 1);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        
        Image panelImage = panelObj.AddComponent<Image>();
        panelImage.color = new Color(0, 0, 0, 0.7f);
        
        // Create text
        GameObject textObj = new GameObject("FIXED_InfoText");
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
            string infoContent = $"🏭 Dynamic Scene: {sceneData.scene_id}\n";
            if (sceneData.plan != null)
            {
                infoContent += $"📋 Plan: {sceneData.plan.planName}\n";
            }
            infoContent += $"🔧 Tools: {sceneData.initialStates?.Count ?? 0} | 👥 Moving Agents: {sceneData.agentProfiles?.Count ?? 0}\n";
            infoContent += "✨ Features: Moving Agents, Proper Materials, Working Colors";
            infoText.text = infoContent;
        }
        
        generatedObjects.Add(canvasObj);
        DebugLog("✅ UI created");
    }
    
    void SetupCamera()
    {
        DebugLog("📷 Setting up camera...");
        
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            GameObject cameraObj = new GameObject("FIXED_Main Camera");
            mainCamera = cameraObj.AddComponent<Camera>();
            cameraObj.tag = "MainCamera";
        }
        
        // Position camera to see the moving agents
        mainCamera.transform.position = new Vector3(5, 15, -8);
        mainCamera.transform.rotation = Quaternion.Euler(45, 25, 0);
        
        DebugLog("✅ Camera positioned");
    }
    
    void CreateFallbackScene()
    {
        DebugLog("🔄 Creating fallback scene...");
        CreateEnvironmentWithProperMaterials();
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
    
    [ContextMenu("Regenerate Fixed Scene")]
    public void RegenerateScene()
    {
        GenerateFixedScene();
    }
}

// Simple working movement controller
public class WorkingAgentMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    public Vector3[] waypoints;
    public float moveSpeed = 2f;
    public float pauseTime = 2f;
    public bool startMoving = true;
    
    [Header("Agent Info")]
    public string agentName;
    public string agentRole;
    
    private int currentWaypointIndex = 0;
    private bool isMoving = false;
    private bool isPaused = false;
    
    void Start()
    {
        if (startMoving && waypoints != null && waypoints.Length > 1)
        {
            Debug.Log($"🚀 Starting movement for {agentName}");
            StartCoroutine(MovementLoop());
        }
    }
    
    IEnumerator MovementLoop()
    {
        isMoving = true;
        
        while (isMoving && waypoints.Length > 1)
        {
            // Get target waypoint
            Vector3 targetWaypoint = waypoints[currentWaypointIndex];
            
            // Move to waypoint
            yield return StartCoroutine(MoveToPosition(targetWaypoint));
            
            // Pause at waypoint
            isPaused = true;
            Debug.Log($"🛑 {agentName} pausing at waypoint {currentWaypointIndex}");
            yield return new WaitForSeconds(pauseTime);
            isPaused = false;
            
            // Move to next waypoint
            currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;
        }
    }
    
    IEnumerator MoveToPosition(Vector3 targetPosition)
    {
        Vector3 startPosition = transform.position;
        float journeyLength = Vector3.Distance(startPosition, targetPosition);
        float journeyTime = journeyLength / moveSpeed;
        float elapsedTime = 0;
        
        Debug.Log($"🏃 {agentName} moving to {targetPosition}");
        
        // Rotate towards target
        Vector3 direction = (targetPosition - startPosition).normalized;
        if (direction != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = targetRotation;
        }
        
        // Move towards target
        while (elapsedTime < journeyTime)
        {
            elapsedTime += Time.deltaTime;
            float fractionOfJourney = elapsedTime / journeyTime;
            
            transform.position = Vector3.Lerp(startPosition, targetPosition, fractionOfJourney);
            yield return null;
        }
        
        transform.position = targetPosition;
        Debug.Log($"✅ {agentName} reached waypoint");
    }
    
    public void StopMovement()
    {
        isMoving = false;
        StopAllCoroutines();
        Debug.Log($"🛑 Movement stopped for {agentName}");
    }
    
    public void ResumeMovement()
    {
        if (!isMoving && waypoints != null && waypoints.Length > 1)
        {
            StartCoroutine(MovementLoop());
            Debug.Log($"▶️ Movement resumed for {agentName}");
        }
    }
    
    void OnDrawGizmos()
    {
        if (waypoints != null && waypoints.Length > 1)
        {
            // Draw waypoint path
            Gizmos.color = Color.yellow;
            
            for (int i = 0; i < waypoints.Length; i++)
            {
                // Draw waypoint
                Gizmos.DrawWireSphere(waypoints[i], 0.5f);
                
                // Draw path
                if (i < waypoints.Length - 1)
                {
                    Gizmos.DrawLine(waypoints[i], waypoints[i + 1]);
                }
                else
                {
                    // Connect last to first
                    Gizmos.DrawLine(waypoints[i], waypoints[0]);
                }
            }
        }
    }
}

// Simple pulsing effect
public class SimplePulse : MonoBehaviour
{
    public float minScale = 0.3f;
    public float maxScale = 0.5f;
    public float pulseSpeed = 2f;
    
    void Update()
    {
        float pulse = Mathf.Sin(Time.time * pulseSpeed) * 0.5f + 0.5f;
        float currentScale = Mathf.Lerp(minScale, maxScale, pulse);
        transform.localScale = Vector3.one * currentScale;
    }
}
