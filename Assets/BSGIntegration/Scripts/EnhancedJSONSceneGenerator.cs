using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Collections.Generic;

public class EnhancedJSONSceneGenerator : MonoBehaviour
{
    [Header("Scene Generation")]
    public bool generateOnStart = true;
    public string jsonFileName = "basicUi.json";
    
    [Header("Visual Settings")]
    public bool useURPMaterials = true;
    public bool showMovementPaths = true;
    
    [Header("Debug")]
    public bool showDebugLogs = true;
    
    private SceneData sceneData;
    private List<GameObject> generatedObjects = new List<GameObject>();
    
    void Start()
    {
        if (generateOnStart)
        {
            DebugLog("🚀 ENHANCED JSON SCENE GENERATOR STARTING...");
            GenerateEnhancedScene();
        }
    }
    
    void GenerateEnhancedScene()
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
        
        // Step 4: Create tools with enhanced materials
        CreateEnhancedToolsFromJSON();
        
        // Step 5: Create agents with movement system
        CreateMovingAgentsFromJSON();
        
        // Step 6: Create enhanced UI
        CreateEnhancedUI();
        
        // Step 7: Setup camera
        SetupCamera();
        
        DebugLog("✅ ENHANCED SCENE GENERATION COMPLETE!");
        DebugLog($"Generated {generatedObjects.Count} objects with movement and URP materials!");
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
        string[] prefixes = {"ENHANCED_", "GENERATED_", "JSON_", "Tool_", "Agent_", "Environment_"};
        
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
    
    void CreateEnvironment()
    {
        DebugLog("🏗️ Creating enhanced environment...");
        
        // Create floor with better material
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "ENHANCED_Floor";
        floor.transform.position = new Vector3(0, 0, 0);
        floor.transform.localScale = new Vector3(3, 1, 3); // 30x30 units
        
        // Enhanced floor material
        Material floorMaterial = CreateEnhancedMaterial("Floor_Material", new Color(0.4f, 0.4f, 0.4f, 1f));
        floor.GetComponent<Renderer>().material = floorMaterial;
        generatedObjects.Add(floor);
        
        // Create walls with enhanced materials
        CreateEnhancedWall("ENHANCED_Wall_North", new Vector3(0, 3, 15), new Vector3(30, 6, 1), new Color(0.6f, 0.7f, 0.9f, 1f));
        CreateEnhancedWall("ENHANCED_Wall_South", new Vector3(0, 3, -15), new Vector3(30, 6, 1), new Color(0.6f, 0.7f, 0.9f, 1f));
        CreateEnhancedWall("ENHANCED_Wall_East", new Vector3(15, 3, 0), new Vector3(1, 6, 30), new Color(0.6f, 0.7f, 0.9f, 1f));
        CreateEnhancedWall("ENHANCED_Wall_West", new Vector3(-15, 3, 0), new Vector3(1, 6, 30), new Color(0.6f, 0.7f, 0.9f, 1f));
        
        DebugLog("✅ Enhanced environment created");
    }
    
    void CreateEnhancedWall(string name, Vector3 position, Vector3 scale, Color color)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.position = position;
        wall.transform.localScale = scale;
        
        Material wallMaterial = CreateEnhancedMaterial($"{name}_Material", color);
        wall.GetComponent<Renderer>().material = wallMaterial;
        generatedObjects.Add(wall);
    }
    
    void CreateEnhancedToolsFromJSON()
    {
        if (sceneData?.initialStates == null)
        {
            DebugLog("⚠️ No tools data found in JSON");
            return;
        }
        
        DebugLog($"🔧 Creating {sceneData.initialStates.Count} enhanced tools from JSON...");
        
        foreach (var toolEntry in sceneData.initialStates)
        {
            string toolId = toolEntry.Key;
            ToolState tool = toolEntry.Value;
            
            // Create tool object with better shape based on type
            GameObject toolObj = CreateToolByType(tool.type);
            toolObj.name = $"ENHANCED_Tool_{toolId}";
            
            // Set position
            if (tool.position != null)
            {
                toolObj.transform.position = new Vector3(tool.position.x, tool.position.y, tool.position.z);
            }
            
            // Apply enhanced material with JSON color
            Color toolColor = ParseMaterialColor(tool);
            Material toolMaterial = CreateEnhancedMaterial($"Tool_{toolId}_Material", toolColor);
            toolObj.GetComponent<Renderer>().material = toolMaterial;
            
            // Add tool info component
            ToolInfoComponent toolInfo = toolObj.AddComponent<ToolInfoComponent>();
            toolInfo.toolId = toolId;
            toolInfo.toolName = tool.name;
            toolInfo.toolType = tool.type;
            toolInfo.isAvailable = tool.isAvailable;
            
            // Create enhanced highlight with pulsing effect
            CreateEnhancedHighlight(toolObj, toolColor, "Tool");
            
            generatedObjects.Add(toolObj);
            DebugLog($"✅ Created enhanced tool: {tool.name} at {tool.position?.x}, {tool.position?.y}, {tool.position?.z}");
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
    
    void CreateMovingAgentsFromJSON()
    {
        if (sceneData?.agentProfiles == null)
        {
            DebugLog("⚠️ No agents data found in JSON");
            return;
        }
        
        DebugLog($"👥 Creating {sceneData.agentProfiles.Count} moving agents from JSON...");
        
        foreach (var agentEntry in sceneData.agentProfiles)
        {
            string agentId = agentEntry.Key;
            AgentProfile agent = agentEntry.Value;
            
            // Create agent object
            GameObject agentObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            agentObj.name = $"ENHANCED_Agent_{agentId}";
            
            // Set position
            if (agent.position != null)
            {
                agentObj.transform.position = new Vector3(agent.position.x, agent.position.y, agent.position.z);
            }
            
            // Parse movement pattern from JSON
            MovementPattern movementPattern = ParseMovementPattern(agent);
            
            // Add movement controller
            AgentMovementController movementController = agentObj.AddComponent<AgentMovementController>();
            movementController.movementPattern = movementPattern;
            movementController.materialColor = ParseAgentMaterialColor(agent);
            movementController.agentName = agent.name;
            movementController.agentRole = agent.role;
            
            // Create enhanced highlight with agent-specific color
            Color agentColor = ParseMaterialColor(agent);
            CreateEnhancedHighlight(agentObj, agentColor, "Agent");
            
            generatedObjects.Add(agentObj);
            DebugLog($"✅ Created moving agent: {agent.name} with {movementPattern.waypoints?.Count ?? 0} waypoints");
        }
    }
    
    MovementPattern ParseMovementPattern(AgentProfile agent)
    {
        MovementPattern pattern = new MovementPattern();
        
        // Try to parse movement data from JSON (this would need custom JSON parsing)
        // For now, create default patterns based on agent role
        pattern.waypoints = new List<Vector3>();
        
        if (agent.role.Contains("Technician"))
        {
            pattern.type = "patrol";
            pattern.speed = 2.5f;
            pattern.pauseDuration = 3.0f;
            pattern.rotationSpeed = 90.0f;
            
            // Create patrol waypoints
            pattern.waypoints.Add(new Vector3(-6, 1, 0));
            pattern.waypoints.Add(new Vector3(-4, 1, -5));
            pattern.waypoints.Add(new Vector3(0, 1, -5));
            pattern.waypoints.Add(new Vector3(2, 1, 3));
        }
        else if (agent.role.Contains("Supervisor"))
        {
            pattern.type = "inspection_rounds";
            pattern.speed = 1.8f;
            pattern.pauseDuration = 4.0f;
            pattern.rotationSpeed = 60.0f;
            
            // Create inspection waypoints
            pattern.waypoints.Add(new Vector3(0, 1, 6));
            pattern.waypoints.Add(new Vector3(4, 1, -5));
            pattern.waypoints.Add(new Vector3(-2, 1, 3));
            pattern.waypoints.Add(new Vector3(-4, 1, -5));
        }
        else if (agent.role.Contains("Inspector"))
        {
            pattern.type = "stationary_observation";
            pattern.speed = 1.0f;
            pattern.pauseDuration = 6.0f;
            pattern.rotationSpeed = 45.0f;
            
            // Create observation waypoints
            pattern.waypoints.Add(new Vector3(6, 1, 0));
            pattern.waypoints.Add(new Vector3(8, 1, 2));
            pattern.waypoints.Add(new Vector3(6, 1, 0));
            pattern.waypoints.Add(new Vector3(4, 1, -2));
        }
        
        return pattern;
    }
    
    MaterialColorData ParseAgentMaterialColor(AgentProfile agent)
    {
        MaterialColorData colorData = new MaterialColorData();
        
        // Default colors based on role
        if (agent.role.Contains("Technician"))
        {
            colorData.r = 0.0f; colorData.g = 1.0f; colorData.b = 0.5f; colorData.a = 1.0f;
        }
        else if (agent.role.Contains("Supervisor"))
        {
            colorData.r = 0.25f; colorData.g = 0.41f; colorData.b = 0.88f; colorData.a = 1.0f;
        }
        else if (agent.role.Contains("Inspector"))
        {
            colorData.r = 0.6f; colorData.g = 0.2f; colorData.b = 0.8f; colorData.a = 1.0f;
        }
        
        return colorData;
    }
    
    Color ParseMaterialColor(ToolState tool)
    {
        // Try to parse hex color first
        if (!string.IsNullOrEmpty(tool.color) && tool.color.StartsWith("#"))
        {
            if (ColorUtility.TryParseHtmlString(tool.color, out Color hexColor))
            {
                return hexColor;
            }
        }
        
        // Fallback to string color names
        switch (tool.color?.ToLower())
        {
            case "red": return Color.red;
            case "yellow": return Color.yellow;
            case "blue": return Color.blue;
            case "green": return Color.green;
            case "orange": return new Color(1f, 0.5f, 0f);
            default: return Color.white;
        }
    }
    
    Color ParseMaterialColor(AgentProfile agent)
    {
        // Try to parse hex color first
        if (!string.IsNullOrEmpty(agent.color) && agent.color.StartsWith("#"))
        {
            if (ColorUtility.TryParseHtmlString(agent.color, out Color hexColor))
            {
                return hexColor;
            }
        }
        
        // Fallback to string color names
        switch (agent.color?.ToLower())
        {
            case "green": return Color.green;
            case "blue": return Color.blue;
            case "purple": return new Color(0.5f, 0f, 1f);
            default: return Color.cyan;
        }
    }
    
    Material CreateEnhancedMaterial(string materialName, Color baseColor)
    {
        Material material;
        
        if (useURPMaterials)
        {
            // Try URP/Lit shader first
            Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
            if (urpShader == null) urpShader = Shader.Find("URP/Lit");
            
            if (urpShader != null)
            {
                material = new Material(urpShader);
                material.name = materialName;
                
                // Set URP properties
                if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", baseColor);
                if (material.HasProperty("_Metallic"))
                    material.SetFloat("_Metallic", 0.3f);
                if (material.HasProperty("_Smoothness"))
                    material.SetFloat("_Smoothness", 0.7f);
                
                DebugLog($"✅ Created URP material: {materialName}");
                return material;
            }
        }
        
        // Fallback to Standard shader
        material = new Material(Shader.Find("Standard"));
        material.name = materialName;
        material.color = baseColor;
        material.SetFloat("_Metallic", 0.3f);
        material.SetFloat("_Glossiness", 0.7f);
        
        DebugLog($"✅ Created Standard material: {materialName}");
        return material;
    }
    
    void CreateEnhancedHighlight(GameObject parentObject, Color baseColor, string type)
    {
        GameObject highlight = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        highlight.name = $"ENHANCED_Highlight_{type}_{parentObject.name}";
        
        float heightOffset = type == "Agent" ? 3f : 2f;
        highlight.transform.position = parentObject.transform.position + Vector3.up * heightOffset;
        highlight.transform.localScale = Vector3.one * 0.4f;
        
        // Create glowing material
        Color glowColor = new Color(baseColor.r, baseColor.g, baseColor.b, 0.8f);
        Material highlightMaterial = CreateEnhancedMaterial($"Highlight_{type}_Material", glowColor);
        
        // Add emission for glow effect
        if (highlightMaterial.HasProperty("_EmissionColor"))
        {
            highlightMaterial.EnableKeyword("_EMISSION");
            highlightMaterial.SetColor("_EmissionColor", glowColor * 0.5f);
        }
        
        highlight.GetComponent<Renderer>().material = highlightMaterial;
        
        // Add pulsing animation
        PulsingHighlight pulser = highlight.AddComponent<PulsingHighlight>();
        pulser.baseScale = 0.4f;
        pulser.pulseScale = 0.6f;
        pulser.pulseSpeed = 2f;
        
        generatedObjects.Add(highlight);
    }
    
    void CreateEnhancedUI()
    {
        DebugLog("🖥️ Creating enhanced UI...");
        
        // Create Canvas
        GameObject canvasObj = new GameObject("ENHANCED_Canvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();
        
        // Create info panel with better styling
        GameObject panelObj = new GameObject("ENHANCED_InfoPanel");
        panelObj.transform.SetParent(canvasObj.transform);
        
        RectTransform panelRect = panelObj.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0, 0.75f);
        panelRect.anchorMax = new Vector2(1, 1);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        
        Image panelImage = panelObj.AddComponent<Image>();
        panelImage.color = new Color(0, 0, 0, 0.8f);
        
        // Create enhanced text
        GameObject textObj = new GameObject("ENHANCED_InfoText");
        textObj.transform.SetParent(panelObj.transform);
        
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(20, 10);
        textRect.offsetMax = new Vector2(-20, -10);
        
        Text infoText = textObj.AddComponent<Text>();
        infoText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        infoText.fontSize = 18;
        infoText.color = Color.white;
        infoText.alignment = TextAnchor.MiddleLeft;
        
        // Set enhanced info text content
        if (sceneData != null)
        {
            string infoContent = $"🏭 Enhanced Scene: {sceneData.scene_id}\n";
            if (sceneData.plan != null)
            {
                infoContent += $"📋 Plan: {sceneData.plan.planName}\n";
                infoContent += $"⏱️ Duration: {sceneData.plan.estimatedTotalDuration} minutes\n";
            }
            infoContent += $"🔧 Tools: {sceneData.initialStates?.Count ?? 0} | 👥 Moving Agents: {sceneData.agentProfiles?.Count ?? 0}\n";
            infoContent += $"✨ Features: URP Materials, Agent Movement, Enhanced Visuals";
            infoText.text = infoContent;
        }
        
        generatedObjects.Add(canvasObj);
        DebugLog("✅ Enhanced UI created");
    }
    
    void SetupCamera()
    {
        DebugLog("📷 Setting up enhanced camera...");
        
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            GameObject cameraObj = new GameObject("ENHANCED_Main Camera");
            mainCamera = cameraObj.AddComponent<Camera>();
            cameraObj.tag = "MainCamera";
        }
        
        // Better camera positioning for enhanced scene
        mainCamera.transform.position = new Vector3(5, 20, -8);
        mainCamera.transform.rotation = Quaternion.Euler(60, 25, 0);
        
        // Enhanced camera settings
        mainCamera.fieldOfView = 60f;
        mainCamera.farClipPlane = 1000f;
        
        DebugLog("✅ Enhanced camera positioned");
    }
    
    void CreateFallbackScene()
    {
        DebugLog("🔄 Creating enhanced fallback scene...");
        CreateEnvironment();
        CreateEnhancedUI();
        SetupCamera();
        DebugLog("✅ Enhanced fallback scene created");
    }
    
    void DebugLog(string message)
    {
        if (showDebugLogs)
        {
            Debug.Log(message);
        }
    }
    
    [ContextMenu("Regenerate Enhanced Scene")]
    public void RegenerateScene()
    {
        GenerateEnhancedScene();
    }
}

// Helper component for tool information
public class ToolInfoComponent : MonoBehaviour
{
    public string toolId;
    public string toolName;
    public string toolType;
    public bool isAvailable;
}

// Helper component for pulsing highlights
public class PulsingHighlight : MonoBehaviour
{
    public float baseScale = 0.4f;
    public float pulseScale = 0.6f;
    public float pulseSpeed = 2f;
    
    void Update()
    {
        float pulse = Mathf.Sin(Time.time * pulseSpeed) * 0.5f + 0.5f;
        float currentScale = Mathf.Lerp(baseScale, pulseScale, pulse);
        transform.localScale = Vector3.one * currentScale;
    }
}

