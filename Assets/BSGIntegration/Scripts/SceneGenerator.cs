using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;

/// <summary>
/// Runs before <see cref="RagSequenceAgentMover"/> so <see cref="GenerateScene"/> creates tools
/// before agents query positions in <c>Start</c>/<c>Update</c>.
/// </summary>
[DefaultExecutionOrder(-100)]
public class SceneGenerator : MonoBehaviour
{
    [Header("Loading Overlay")]
    [Tooltip("Optional CanvasGroup on a 'Loading' UI panel. If assigned it is shown during RAG parse + scene build and hidden once the scene is ready.")]
    public CanvasGroup loadingOverlay;
    [Tooltip("Optional Text on the loading overlay to show current status.")]
    public UnityEngine.UI.Text loadingStatusText;

    [Header("Scene Generation")]
    public bool generateOnStart = true;
    public bool clearExistingScene = false; // Don't clear existing scene objects
    public bool spawnCognitiveStationsBeforeAgents = true;
    
    [Header("Prefab References")]
    public GameObject toolPrefab;
    public GameObject agentPrefab;
    public GameObject wallPrefab;
    public GameObject floorPrefab;
    
    [Header("Scene Layout")]
    public Vector3 sceneCenter = Vector3.zero;
    public float toolSpacing = 4f;
    public float wallHeight = 5f;
    public float floorSize = 56f;

    [Header("RAG — agents & labels")]
    [Tooltip("Uniform scale for mental (M) agents.")]
    [Range(0.8f, 2.5f)] public float ragMentalAgentScale = 1.28f;
    [Tooltip("Uniform scale for physical (P) agents — increased so hand/finger reach is more visible during menu interactions.")]
    [Range(0.8f, 2.5f)] public float ragPhysicalAgentScale = 1.68f;
    [Obsolete("Use ragMentalAgentScale / ragPhysicalAgentScale.")]
    [Range(0.8f, 3f)] public float ragAgentUniformScale = 1.15f;

    [Tooltip("Movement speed (units/sec) for mental agents while walking cognitive steps between stations. Operational physical agents keep base speed from profile.")]
    [Range(2f, 14f)] public float ragMentalCognitiveMoveSpeed = 6.5f;

    [Tooltip("Floating TextMesh names on dynamic tools/targets (ToolState.name / id). Cognitive stations use CognitiveStationVisualStyler.")]
    public bool showDynamicObjectNameLabels = true;

    [Tooltip("Alternating ± vertical offset (world units) after sorting objects by world position (Z, then X). Tweak if labels still collide.")]
    [Min(0f)]
    public float nameLabelHeightStagger = 0.36f;
    
    [Header("Ronald-Johnson — single motor zone")]
    public bool singleZoneMode = false;
    public Vector3 zoneWorldOrigin = Vector3.zero;

    [Header("Ronald-Johnson — multiplayer embed")]
    public bool multiplayerEmbedMode = false;
    public int spawnZonesMask = 0;
    public bool skipEnvironmentGeneration = false;
    public bool useSceneAnchorLayout = false;
    public Vector3 ragWorldOrigin = Vector3.zero;
    
    [Header("Generated Objects")]
    public Transform toolsParent;
    public Transform agentsParent;
    public Transform environmentParent;
    
    private SceneData sceneData;
    private Dictionary<string, GameObject> generatedTools = new Dictionary<string, GameObject>();
    private Dictionary<string, GameObject> generatedAgents = new Dictionary<string, GameObject>();

    /// <summary>Avoid logging the same missing target every frame (ML obs / temporal probes).</summary>
    readonly HashSet<string> _reportedMissingTargetKeys = new HashSet<string>();

    public static SceneGenerator Instance { get; private set; }

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
    
    void Start()
    {
        Debug.Log("=== SceneGenerator Start() called ===");

        if (generateOnStart)
        {
            Debug.Log("Auto-generating scene...");
            ShowLoadingOverlay("Parsing RAG pipeline…");
            GenerateScene();
            HideLoadingOverlay();
        }
        else
        {
            Debug.Log("Auto-generation disabled. Press F to force generate.");
        }
    }

    void ShowLoadingOverlay(string status = "Loading…")
    {
        if (loadingOverlay == null) return;
        loadingOverlay.alpha = 1f;
        loadingOverlay.blocksRaycasts = true;
        if (loadingStatusText != null) loadingStatusText.text = status;
    }

    void SetLoadingStatus(string status)
    {
        if (loadingStatusText != null) loadingStatusText.text = status;
    }

    void HideLoadingOverlay()
    {
        if (loadingOverlay == null) return;
        loadingOverlay.alpha = 0f;
        loadingOverlay.blocksRaycasts = false;
        if (loadingStatusText != null) loadingStatusText.text = "";
    }
    
    public void GenerateScene()
    {
        Debug.Log("=== SCENE GENERATION STARTED ===");
        ShowLoadingOverlay("Fetching RAG data…");

        // Load JSON data first
        SceneUILoader sceneLoader = GetComponent<SceneUILoader>();
        if (sceneLoader == null)
        {
            Debug.LogError("SceneUILoader not found! Cannot generate scene without JSON data.");
            HideLoadingOverlay();
            return;
        }

        SetLoadingStatus("Loading scene data…");
        sceneData = sceneLoader.GetSceneData();
        if (sceneData == null)
        {
            Debug.LogError("No scene data available! Make sure JSON is loaded first.");
            HideLoadingOverlay();
            return;
        }

        Debug.Log($"Scene data loaded: {sceneData.scene_id}");
        Debug.Log($"Tools to generate: {sceneData.initialStates?.Count ?? 0}");
        Debug.Log($"Agents to generate: {sceneData.agentProfiles?.Count ?? 0}");
        
        if (clearExistingScene)
        {
            ClearExistingScene();
        }

        SetLoadingStatus("Building scene structure…");
        CreateSceneStructure();

        if (UsesSceneAnchorLayout() && BsgIntegrationSettings.PrepareLayoutFromSceneData != null)
        {
            SetLoadingStatus("Fitting RAG layout to zone 0…");
            BsgIntegrationSettings.PrepareLayoutFromSceneData(sceneData);
        }

        SetLoadingStatus("Spawning cognitive stations…");
        GenerateTools();
        RagMenuController.EnsureInScene()?.RefreshMenuOptions();
        SetLoadingStatus("Spawning agents…");
        GenerateAgents();
        SetLoadingStatus("Initialising cognitive runtime…");
        EnsureCognitiveRuntimeSystems();
        if (!ShouldSkipEnvironmentGeneration())
        {
            SetLoadingStatus("Generating environment…");
            GenerateEnvironment();
        }
        else
        {
            Debug.Log("[SceneGenerator] Skipping BSG environment generation (multiplayer embed / skip flag).");
        }
        SetupSceneLighting();

        Debug.Log($"Scene '{sceneData.scene_id}' generated successfully!");
        Debug.Log($"Generated tools: {generatedTools.Count}");
        Debug.Log($"Generated agents: {generatedAgents.Count}");

        ProximityDetectionSystem proximity = FindObjectOfType<ProximityDetectionSystem>();
        if (proximity != null && !BsgIntegrationSettings.MultiplayerEmbedMode && !BsgIntegrationSettings.UseSceneAnchorLayout)
            proximity.RefreshProximityRegistries();

        SetLoadingStatus("Scene ready — starting cognitive execution…");
        HideLoadingOverlay();

        RagInferenceSceneController inference = FindObjectOfType<RagInferenceSceneController>();
        if (inference != null)
            inference.OnSceneGenerated();

        FinalizeMultiplayerEmbedLayout();
        
        // Show summary of where everything was created
        ShowGenerationSummary();
    }

    void FinalizeMultiplayerEmbedLayout()
    {
        if (!UsesSceneAnchorLayout())
            return;

        if (!BsgIntegrationSettings.HasSceneAnchorLayout)
        {
            Debug.LogWarning("[SceneGenerator] useSceneAnchorLayout set but no scene anchor registered.");
            return;
        }

        BsgIntegrationSettings.ApplyPlayAreaToAgents?.Invoke();

        var spawned = new List<GameObject>(generatedTools.Count + generatedAgents.Count);
        foreach (var go in generatedTools.Values)
            if (go != null) spawned.Add(go);
        foreach (var go in generatedAgents.Values)
            if (go != null) spawned.Add(go);
        BsgIntegrationSettings.ValidateOverlapAndNudge?.Invoke(spawned);
        LogMultiplayerRagSpawnCounts();
    }

    void LogMultiplayerRagSpawnCounts()
    {
        int modules = 0;
        int buffers = 0;
        int sceneEntities = 0;

        foreach (var kvp in generatedTools)
        {
            string baseId = StripZoneSuffix(kvp.Key);
            ToolState state = sceneData?.initialStates != null && sceneData.initialStates.TryGetValue(kvp.Key, out ToolState ts)
                ? ts
                : null;
            string type = (state?.type ?? string.Empty).ToLowerInvariant();

            if (baseId.StartsWith("cognitive_", System.StringComparison.OrdinalIgnoreCase))
            {
                if (type.Contains("buffer"))
                    buffers++;
                else
                    modules++;
            }
            else if (baseId.StartsWith("scene_", System.StringComparison.OrdinalIgnoreCase))
            {
                sceneEntities++;
            }
        }

        Debug.Log($"[SceneGenerator] Zone 0 RAG spawn counts — modules={modules}/6, buffers={buffers}/10, physical env={sceneEntities}, total tools={generatedTools.Count}");
        if (modules != 6 || buffers != 10)
            Debug.LogWarning("[SceneGenerator] RAG cognitive spawn counts do not match basicUI_ml2.json — check layout overlap or zone filter.");
    }

    bool ShouldSkipEnvironmentGeneration()
    {
        return skipEnvironmentGeneration || BsgIntegrationSettings.SkipEnvironmentGeneration;
    }

    bool UsesSceneAnchorLayout()
    {
        return useSceneAnchorLayout || BsgIntegrationSettings.UseSceneAnchorLayout;
    }

    bool ShouldSpawnZone(int zoneIndex)
    {
        int mask = spawnZonesMask != 0 ? spawnZonesMask : BsgIntegrationSettings.SpawnZonesMask;
        if (mask == 0)
            return true;
        if (zoneIndex < 0)
            zoneIndex = 0;
        return (mask & (1 << zoneIndex)) != 0;
    }

    Vector3 RemapJsonPosition(string objectId, Vector3 jsonLocal)
    {
        if (UsesSceneAnchorLayout() && BsgIntegrationSettings.MapJsonToWorld != null)
            return BsgIntegrationSettings.MapJsonToWorld(objectId ?? string.Empty, jsonLocal);

        if (multiplayerEmbedMode || BsgIntegrationSettings.MultiplayerEmbedMode)
        {
            Vector3 origin = ragWorldOrigin != Vector3.zero
                ? ragWorldOrigin
                : BsgIntegrationSettings.RagWorldOrigin;
            if (origin != Vector3.zero)
                return origin + jsonLocal;
        }

        if (singleZoneMode || BsgIntegrationSettings.SingleZoneMode)
        {
            Vector3 origin = zoneWorldOrigin != Vector3.zero
                ? zoneWorldOrigin
                : BsgIntegrationSettings.ZoneWorldOrigin;
            return origin + jsonLocal;
        }

        return jsonLocal;
    }
    
    void ShowGenerationSummary()
    {
        Debug.Log("=== GENERATION SUMMARY ===");
        Debug.Log($"Scene Center: {sceneCenter}");
        
        if (generatedTools.Count > 0)
        {
            Debug.Log("Generated Tools:");
            foreach (var tool in generatedTools)
            {
                Debug.Log($"  - {tool.Key}: {tool.Value.name} at {tool.Value.transform.position}");
            }
        }
        
        if (generatedAgents.Count > 0)
        {
            Debug.Log("Generated Agents:");
            foreach (var agent in generatedAgents)
            {
                Debug.Log($"  - {agent.Key}: {agent.Value.name} at {agent.Value.transform.position}");
            }
        }
        
        Debug.Log("=== LOOK FOR THESE OBJECTS ===");
        Debug.Log("1. Check Hierarchy for 'JSON_Generated_*' folders");
        Debug.Log("2. Tool labels / agents are listed under JSON_Generated_* parents");
        Debug.Log("3. Objects are positioned 5 units to the right of scene center");
        Debug.Log("4. Camera may have been repositioned to show new objects");
    }
    
    public void ClearExistingScene()
    {
        Debug.Log("=== CLEARING EXISTING GENERATED SCENE ===");
        
        if (toolsParent != null)
        {
            foreach (Transform child in toolsParent)
                DestroyGeneratedObject(child.gameObject);
        }
        
        if (agentsParent != null)
        {
            foreach (Transform child in agentsParent)
                DestroyGeneratedObject(child.gameObject);
        }
        
        if (environmentParent != null)
        {
            foreach (Transform child in environmentParent)
                DestroyGeneratedObject(child.gameObject);
        }

        // Also remove any old zone ground/walls from legacy replica system
        GameObject[] all = FindObjectsOfType<GameObject>();
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null) continue;
            string n = all[i].name;
            if (n.StartsWith("Ground_Zone", System.StringComparison.Ordinal) ||
                n.StartsWith("Wall_Zone", System.StringComparison.Ordinal) ||
                n.StartsWith("Wall_Divider", System.StringComparison.Ordinal) ||
                n.StartsWith("JSON_Generated_Wall", System.StringComparison.Ordinal) ||
                n == "JSON_Generated_Floor" ||
                n == "JSON_BaseGreyPlane")
            {
                DestroyGeneratedObject(all[i]);
            }
        }

        generatedTools.Clear();
        generatedAgents.Clear();
        
        Debug.Log("Generated scene cleared successfully");
    }

    static void DestroyGeneratedObject(UnityEngine.Object obj)
    {
        if (obj == null)
            return;

        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }
    
    void CreateSceneStructure()
    {
        Transform contentRoot = transform;

        if (UsesSceneAnchorLayout())
        {
            Transform anchorRoot = BsgIntegrationSettings.GetRagWorldRoot?.Invoke();
            if (anchorRoot != null)
                contentRoot = anchorRoot;
            else
                Debug.LogError("[SceneGenerator] useSceneAnchorLayout=true but no scene anchor registered.");
        }
        else if (multiplayerEmbedMode || BsgIntegrationSettings.MultiplayerEmbedMode)
        {
            Vector3 origin = ragWorldOrigin != Vector3.zero
                ? ragWorldOrigin
                : BsgIntegrationSettings.RagWorldOrigin;
            GameObject worldRoot = GameObject.Find("RAG_WorldRoot");
            if (worldRoot == null)
            {
                worldRoot = new GameObject("RAG_WorldRoot");
                worldRoot.transform.position = origin;
            }
            contentRoot = worldRoot.transform;
        }

        if (toolsParent == null)
        {
            GameObject toolsGO = new GameObject("JSON_Generated_Tools");
            toolsParent = toolsGO.transform;
            toolsParent.SetParent(contentRoot);
        }
        
        if (agentsParent == null)
        {
            GameObject agentsGO = new GameObject("JSON_Generated_Agents");
            agentsParent = agentsGO.transform;
            agentsParent.SetParent(contentRoot);
        }
        
        if (environmentParent == null)
        {
            GameObject envGO = new GameObject("JSON_Generated_Environment");
            environmentParent = envGO.transform;
            environmentParent.SetParent(contentRoot);
        }
    }
    
    void GenerateTools()
    {
        Debug.Log("=== GENERATING TOOLS ===");
        
        if (sceneData.initialStates == null)
        {
            Debug.LogError("No initial states found in scene data!");
            return;
        }
        
        Debug.Log($"Found {sceneData.initialStates.Count} tools to generate");
        
        int toolIndex = 0;
        var orderedTools = sceneData.initialStates.OrderBy(kvp => kvp.Key).ToList();

        if (spawnCognitiveStationsBeforeAgents)
        {
            orderedTools = orderedTools
                .OrderByDescending(kvp => IsCognitiveStation(kvp.Key, kvp.Value))
                .ThenBy(kvp => kvp.Key)
                .ToList();
        }

        foreach (var toolEntry in orderedTools)
        {
            string toolId = toolEntry.Key;
            ToolState toolState = toolEntry.Value;

            int toolZone = ExtractZoneIndex(toolId, 0);
            if (!ShouldSpawnZone(toolZone))
                continue;
            
            Debug.Log($"Creating tool: {toolId} at index {toolIndex}");

            if (generatedTools.TryGetValue(toolId, out GameObject prevTool) && prevTool != null)
            {
                Destroy(prevTool);
                generatedTools.Remove(toolId);
            }
            
            // Create tool GameObject
            GameObject toolGO = CreateToolObject(toolId, toolState, toolIndex);
            if (toolGO != null)
            {
                generatedTools[toolId] = toolGO;
                Debug.Log($"Successfully created tool: {toolId} at position {toolGO.transform.position}");
                toolIndex++;
            }
            else
            {
                Debug.LogError($"Failed to create tool: {toolId}");
            }
        }

        ApplySpatialStaggerToGeneratedToolLabels();

        int cognitiveCount = 0;
        int moduleCount = 0;
        int bufferCount = 0;
        foreach (var kvp in generatedTools)
        {
            if (kvp.Value == null)
                continue;
            ToolState st = null;
            if (sceneData.initialStates != null)
                sceneData.initialStates.TryGetValue(kvp.Key, out st);
            if (!IsCognitiveStation(kvp.Key, st))
                continue;

            cognitiveCount++;
            string typeLower = (st?.type ?? string.Empty).ToLowerInvariant();
            if (typeLower.Contains("buffer"))
                bufferCount++;
            else
                moduleCount++;
        }
        Debug.Log($"Tool generation complete. Created {generatedTools.Count} tools " +
                  $"(cognitive stations={cognitiveCount}: modules={moduleCount}, buffers={bufferCount}).");
        if (cognitiveCount < 16)
            Debug.LogWarning($"[SceneGenerator] Expected 16 cognitive stations but only spawned {cognitiveCount}. " +
                             "Check zone mask / initialStates.");
    }
    
    GameObject CreateToolObject(string toolId, ToolState toolState, int index)
    {
        GameObject toolGO;
        
        // Use prefab if available, otherwise create primitive
        if (toolPrefab != null)
        {
            toolGO = Instantiate(toolPrefab, toolsParent);
        }
        else
        {
            // Create a default tool representation
            toolGO = GameObject.CreatePrimitive(ResolvePrimitiveType(toolId, toolState));
            toolGO.transform.SetParent(toolsParent);
            
            // Add a tool component for interaction
            ToolComponent toolComponent = toolGO.AddComponent<ToolComponent>();
            toolComponent.Initialize(toolId, toolState);
        }
        
        Vector3 position = ResolveToolPosition(toolId, toolState, index);
        
        Debug.Log($"Positioning tool {toolId} at: {position}");
        
        toolGO.transform.position = position;
        toolGO.name = $"Tool_{toolId}";
        ApplyCognitiveScaleProfile(toolGO, toolId, toolState);
        
        // Set tool properties based on JSON data
        UpdateToolVisuals(toolGO, toolState);
        AttachDeclarativeMetadata(toolGO, toolId, toolState);
        
        bool isCognitive = IsCognitiveStation(toolId, toolState);
        bool isMenuOption = IsMenuOptionTool(toolId, toolState);
        if (!isCognitive && !isMenuOption)
        {
            // Keep legacy highlight for regular tools only.
            AddObjectHighlight(toolGO, "TOOL");
        }

        if (isMenuOption)
        {
            ConfigureMenuOptionObject(toolGO, toolState, toolId);
        }
        else if (isCognitive)
        {
            var station = toolGO.GetComponent<CognitiveStationInteractable>();
            if (station == null)
            {
                station = toolGO.AddComponent<CognitiveStationInteractable>();
            }
            station.stationId = toolId;
            string typeLower = (toolState.type ?? string.Empty).ToLowerInvariant();
            station.stationType = typeLower.Contains("buffer")
                ? "buffer"
                : (typeLower.Contains("module") ? "module" : (!string.IsNullOrWhiteSpace(toolState.moduleType) ? toolState.moduleType : toolState.bufferType));
            station.stationAction = string.IsNullOrWhiteSpace(toolState.stationAction) ? "learn" : toolState.stationAction;
            station.interactionRadius = 2.0f;

            var visualStyler = toolGO.GetComponent<CognitiveStationVisualStyler>();
            if (visualStyler == null)
            {
                visualStyler = toolGO.AddComponent<CognitiveStationVisualStyler>();
            }
            visualStyler.stationId     = toolId;
            visualStyler.displayName   = string.IsNullOrWhiteSpace(toolState.name) ? toolId : toolState.name;
            visualStyler.stationType   = typeLower.Contains("buffer") ? "buffer" : "module";
            visualStyler.stationAction = station.stationAction;
            visualStyler.showFloatingLabel    = true;
            visualStyler.useMushroomStyle     = true;
            visualStyler.billboardLabelToCamera = true;
            visualStyler.useFullNameInLabel   = true;
            // Modules are the primary "brain" stations — give them a base plate and halo;
            // buffers stay clean without those extra markers.
            bool isModuleType = !typeLower.Contains("buffer");
            visualStyler.addBasePlate = isModuleType;
            visualStyler.addHalo      = false; // ring already built inside composite
            visualStyler.labelHeightStaggerOffset = 0f;
            visualStyler.ApplyStyle();

            // Modules slightly above buffers; multiplayer embed applies anchor scale separately.
            // Trimmed a bit (was 0.92 / 0.82) so stations take less floor space and leave wider lanes for the
            // agent to weave through the staggered cognitive field. The CognitiveNavObstacle box is a child, so
            // it scales down with this too, widening the navigable gaps.
            toolGO.transform.localScale = isModuleType
                ? Vector3.one * 0.80f
                : Vector3.one * 0.72f;

            if (UsesSceneAnchorLayout())
            {
                float m = BsgIntegrationSettings.MultiplayerCognitiveScaleMultiplier;
                if (m > 0.01f)
                    toolGO.transform.localScale *= m;
            }

            // Visual primitives have colliders stripped
            // synthetic box for obstacle queries so RagSequenceAgentMover can steer around stations.
            // CognitiveStationInteractable keeps its trigger for enter/stay events.
            StripSolidCollidersKeepingTriggers(toolGO);
            EnsureCognitiveNavigationCollider(toolGO, typeLower);

            ConfigureSpecialCognitiveStation(toolGO, toolId);
        }
        else
        {
            if (UsesSceneAnchorLayout() || multiplayerEmbedMode || BsgIntegrationSettings.MultiplayerEmbedMode)
            {
                Collider col = toolGO.GetComponent<Collider>();
                if (col != null)
                    col.isTrigger = true;
                ApplyMultiplayerEnvironmentToolPresentation(toolGO, toolState);
                // Enlarge ONLY left_mouse_button AFTER the presentation sets its scale (so the boost
                // is not clobbered) and BEFORE the nav collider is fitted (so it matches the new size).
                ApplyPhysicalPressTargetScale(toolGO, toolId, toolState);
                EnvironmentNavigationColliderBuilder.EnsureOnTool(toolGO);
            }
            else
            {
                ApplyPhysicalPressTargetScale(toolGO, toolId, toolState);
                EnvironmentSolidCollider.EnsureOnObject(toolGO, addBoxIfMissing: true);
            }
        }

        if (showDynamicObjectNameLabels && !isCognitive && !isMenuOption)
        {
            AttachToolObjectNameLabel(toolGO, toolState, toolId);
        }

        return toolGO;
    }

    // left_mouse_button (scene_011) is a small, low cube the tall designated physical agent cannot
    // comfortably reach. Enlarge ONLY this press target so its top rises toward hand height while its
    // base stays on the ground. Tune here if the finger still lands short / overshoots.
    static readonly Vector3 PhysicalPressTargetScale = new Vector3(1.2f, 1.45f, 1.2f);

    void ApplyPhysicalPressTargetScale(GameObject toolGO, string toolId, ToolState toolState)
    {
        if (toolGO == null)
            return;

        bool isPressTarget =
            string.Equals(toolId, "scene_007", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolId, "scene_011", System.StringComparison.OrdinalIgnoreCase)
            || (toolState != null && !string.IsNullOrWhiteSpace(toolState.name)
                && string.Equals(toolState.name, "left_mouse_button", System.StringComparison.OrdinalIgnoreCase));
        if (!isPressTarget)
            return;

        Vector3 scaleBoost = string.Equals(toolId, "scene_007", System.StringComparison.OrdinalIgnoreCase)
            ? new Vector3(1.15f, 1.42f, 1.15f)
            : PhysicalPressTargetScale;

        // Measure the MAIN cube renderer (on the root), not the decorative base-pad child, so the
        // re-seat keeps the cube's base on the ground after scaling.
        Renderer before = toolGO.GetComponent<Renderer>() ?? toolGO.GetComponentInChildren<Renderer>();
        float baseY = before != null ? before.bounds.min.y : toolGO.transform.position.y;

        toolGO.transform.localScale = Vector3.Scale(toolGO.transform.localScale, scaleBoost);

        // Re-seat the base on the ground (scaling about the pivot would otherwise sink/raise it).
        Renderer after = toolGO.GetComponent<Renderer>() ?? toolGO.GetComponentInChildren<Renderer>();
        if (after != null)
            toolGO.transform.position += new Vector3(0f, baseY - after.bounds.min.y, 0f);

        Debug.Log($"[SceneGenerator] Enlarged press target {toolId} (left_mouse_button) to scale " +
                  $"{toolGO.transform.localScale}; visible top now at y={(after != null ? after.bounds.max.y : 0f):F2}.");
    }

    static bool IsMenuOptionTool(string toolId, ToolState toolState)
    {
        return RagMenuController.IsMenuOptionId(toolId)
               || RagMenuController.IsMenuOptionId(toolState?.objectId)
               || (!string.IsNullOrWhiteSpace(toolState?.name)
                   && toolState.name.IndexOf("menu_option", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    void ConfigureMenuOptionObject(GameObject toolGO, ToolState toolState, string toolId)
    {
        if (toolGO == null) return;

        toolGO.transform.localScale = new Vector3(1.45f, 0.14f, 0.46f);
        Renderer r = toolGO.GetComponent<Renderer>();
        if (r != null)
        {
            Shader s = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            r.material = new Material(s) { color = new Color(0.18f, 0.38f, 0.95f, 1f) };
        }

        EnvironmentSolidCollider solid = toolGO.GetComponent<EnvironmentSolidCollider>();
        if (solid != null) Destroy(solid);

        Collider[] colliders = toolGO.GetComponents<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].isTrigger = true;
        }

        AttachMenuOptionLabel(toolGO, toolState, toolId);
    }

    void AttachMenuOptionLabel(GameObject toolGO, ToolState toolState, string toolId)
    {
        const string labelName = "MenuOptionLabel";
        Transform existing = toolGO.transform.Find(labelName);
        if (existing != null) Destroy(existing.gameObject);

        string title = toolState != null && !string.IsNullOrWhiteSpace(toolState.name)
            ? toolState.name.Replace("menu_option_", "").Replace("_", " ")
            : toolId;

        GameObject labelGo = new GameObject(labelName);
        labelGo.transform.SetParent(toolGO.transform, false);
        labelGo.transform.localPosition = new Vector3(0f, 0.62f, 0f);
        labelGo.transform.localRotation = Quaternion.Euler(65f, 0f, 0f);

        TextMesh tm = labelGo.AddComponent<TextMesh>();
        tm.text = title;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.characterSize = 0.13f;
        tm.fontSize = 64;
        tm.fontStyle = FontStyle.Bold;
        tm.color = Color.white;
    }

    void AttachDeclarativeMetadata(GameObject toolGO, string toolId, ToolState toolState)
    {
        if (toolGO == null) return;

        DeclarativeObjectMetadata metadata = toolGO.GetComponent<DeclarativeObjectMetadata>()
            ?? toolGO.AddComponent<DeclarativeObjectMetadata>();

        string objectId = !string.IsNullOrWhiteSpace(toolState?.objectId) ? toolState.objectId : toolId;
        string displayName = !string.IsNullOrWhiteSpace(toolState?.name) ? toolState.name : objectId;
        string type = toolState?.type ?? string.Empty;
        string state = toolState?.initialState ?? string.Empty;
        metadata.Apply(toolState?.declarativeMetadata, objectId, displayName, type, state);

        if (metadata.isPlaceholder)
        {
            Debug.Log($"[DeclarativeMetadata] Placeholder metadata attached to {objectId}: {metadata.semanticThread}");
        }
    }

    /// <summary>Remove root primitive mesh colliders; composite visuals strip their own. Triggers remain.</summary>
    static void StripSolidCollidersKeepingTriggers(GameObject root)
    {
        if (root == null) return;
        Collider[] cols = root.GetComponents<Collider>();
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null && !cols[i].isTrigger)
                UnityEngine.Object.Destroy(cols[i]);
        }
    }

    /// <summary>
    /// Single axis-aligned box used only for obstacle avoidance (not triggers). Matches typical
    /// module / buffer / hub footprint after <see cref="CognitiveStationVisualStyler"/> composite scale.
    /// </summary>
    static void EnsureCognitiveNavigationCollider(GameObject root, string typeLower)
    {
        if (root == null) return;
        const string childName = "CognitiveNavObstacle";
        if (root.transform.Find(childName) != null) return;

        bool isModule = typeLower.Contains("module");
        bool isBuffer = typeLower.Contains("buffer");
        bool isHub = typeLower.Contains("hub");

        GameObject go = new GameObject(childName);
        go.transform.SetParent(root.transform, false);
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.isTrigger = false;

        EnvironmentSolidCollider solid = go.AddComponent<EnvironmentSolidCollider>();
        solid.ConfigureSolidCollider();
        ScenePhysicsLayers.ApplyEnvironmentLayer(go);

        // Fit the collision box to the station's ACTUAL VISIBLE geometry (composite mesh), like physical props
        // do (EnvironmentVisibleSolid). This is what removes the "invisible gap": the agent now collides with
        // the real station body instead of an oversized constant box that stopped it short of what it sees.
        // A tiny pad keeps the box just outside the mesh surface; the NavMesh bake radius provides path
        // clearance, so the box no longer needs to be inflated. Called after the composite style is applied.
        if (EnvironmentNavigationColliderBuilder.TryComputeLocalVisibleBox(root, out Vector3 fitCenter, out Vector3 fitSize))
        {
            const float contactPad = 1.04f;
            box.center = fitCenter;
            box.size = new Vector3(
                Mathf.Max(0.12f, fitSize.x * contactPad),
                Mathf.Max(0.12f, fitSize.y),
                Mathf.Max(0.12f, fitSize.z * contactPad));
            return;
        }

        // Fallback (no visible renderer found): conservative type constants, no longer inflated.
        if (isHub)
        {
            box.center = new Vector3(0f, 1.05f, 0f);
            box.size = new Vector3(2.35f, 2.45f, 2.35f);
        }
        else if (isModule)
        {
            box.center = new Vector3(0f, 1.02f, 0f);
            box.size = new Vector3(2.05f, 2.30f, 2.05f);
        }
        else if (isBuffer)
        {
            box.center = new Vector3(0f, 0.62f, 0f);
            box.size = new Vector3(2.85f, 1.65f, 2.15f);
        }
        else
        {
            box.center = new Vector3(0f, 0.85f, 0f);
            box.size = new Vector3(2.1f, 2.1f, 2.1f);
        }
    }

    static bool IsWorkstationSceneEntity(string objectId)
    {
        if (string.IsNullOrWhiteSpace(objectId))
            return false;

        string id = objectId.Trim();
        int zoneIdx = id.IndexOf("_zone", System.StringComparison.OrdinalIgnoreCase);
        if (zoneIdx > 0)
            id = id.Substring(0, zoneIdx);

        return id.StartsWith("scene_00", System.StringComparison.OrdinalIgnoreCase);
    }

    void ApplyMultiplayerEnvironmentToolPresentation(GameObject toolGO, ToolState toolState)
    {
        if (toolGO == null)
            return;

        // Workstation scene entities get real sizing + desk rig from MeronymPartSpawner — skip the tall env cube.
        if (IsWorkstationSceneEntity(toolState?.objectId))
        {
            toolGO.transform.localScale = Vector3.one;
            return;
        }

        float scale = BsgIntegrationSettings.MultiplayerEnvironmentScaleMultiplier;
        if (scale < 0.01f)
            scale = 1f;

        float heightMul = BsgIntegrationSettings.MultiplayerEnvironmentHeightMultiplier;
        if (heightMul < 0.5f)
            heightMul = 1f;

        float xz = scale * 1.05f;
        float y = Mathf.Max(scale * heightMul, EnvironmentNavigationColliderBuilder.MinAgentBlockingHeight * 0.72f);
        toolGO.transform.localScale = new Vector3(xz, y, xz);

        EnsureEnvironmentToolBasePad(toolGO, scale);

        Renderer renderer = toolGO.GetComponent<Renderer>();
        if (renderer != null && renderer.material != null)
        {
            Color baseColor = renderer.material.color;
            renderer.material.color = Color.Lerp(baseColor, Color.white, 0.12f);
        }

        if (toolState != null && !string.IsNullOrWhiteSpace(toolState.color)
            && ColorUtility.TryParseHtmlString(toolState.color, out Color parsed))
        {
            Renderer accent = toolGO.GetComponent<Renderer>();
            if (accent != null && accent.material != null)
                accent.material.color = Color.Lerp(parsed, Color.white, 0.08f);
        }
    }

    void EnsureEnvironmentToolBasePad(GameObject toolGO, float objectScale)
    {
        const string padName = "EnvToolBasePad";
        if (toolGO.transform.Find(padName) != null)
            return;

        GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pad.name = padName;
        pad.transform.SetParent(toolGO.transform, false);
        pad.transform.localPosition = new Vector3(0f, -0.48f * objectScale, 0f);
        pad.transform.localScale = new Vector3(1.35f * objectScale, 0.06f, 1.35f * objectScale);

        Collider padCol = pad.GetComponent<Collider>();
        if (padCol != null)
            Destroy(padCol);

        Renderer padRenderer = pad.GetComponent<Renderer>();
        if (padRenderer != null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader != null)
            {
                padRenderer.material = new Material(shader);
                padRenderer.material.color = new Color(0.12f, 0.14f, 0.18f, 1f);
            }
        }
    }

    static bool UsesMultiplayerEnvironmentPresentation()
    {
        return BsgIntegrationSettings.UseSceneAnchorLayout
            || BsgIntegrationSettings.MultiplayerEmbedMode;
    }

    void ApplyCognitiveScaleProfile(GameObject stationObject, string toolId, ToolState toolState)
    {
        if (!IsCognitiveStation(toolId, toolState) || stationObject == null)
        {
            return;
        }

        string stationType = (toolState?.properties?.stationType ?? toolState?.type ?? string.Empty).ToLowerInvariant();
        if (stationType.Contains("module"))
        {
            stationObject.transform.localScale = new Vector3(0.98f, 1.12f, 0.98f);
        }
        else if (stationType.Contains("buffer"))
        {
            stationObject.transform.localScale = new Vector3(0.95f, 0.95f, 0.95f);
        }
        else if (stationType.Contains("hub"))
        {
            stationObject.transform.localScale = new Vector3(1.2f, 1.0f, 1.2f);
        }

        if (UsesSceneAnchorLayout())
        {
            float m = BsgIntegrationSettings.MultiplayerCognitiveScaleMultiplier;
            if (m > 0.01f && Mathf.Abs(m - 1f) > 0.001f)
                stationObject.transform.localScale *= m;
        }
    }

    PrimitiveType ResolvePrimitiveType(string toolId, ToolState toolState)
    {
        bool isCognitive = IsCognitiveStation(toolId, toolState);
        if (isCognitive)
        {
            string stationType = (toolState?.properties?.stationType ?? toolState?.type ?? string.Empty).ToLowerInvariant();
            if (stationType.Contains("module")) return PrimitiveType.Capsule;
            if (stationType.Contains("buffer")) return PrimitiveType.Sphere;
            if (stationType.Contains("hub")) return PrimitiveType.Cylinder;
        }

        string shape = toolState?.shape?.ToLowerInvariant() ?? "cube";
        if (shape.Contains("sphere")) return PrimitiveType.Sphere;
        if (shape.Contains("cylinder")) return PrimitiveType.Cylinder;
        if (shape.Contains("capsule")) return PrimitiveType.Capsule;
        return PrimitiveType.Cube;
    }

    Vector3 ResolveToolPosition(string toolId, ToolState toolState, int index)
    {
        if (toolState != null && toolState.position != null)
        {
            Vector3 jsonPos = new Vector3(
                toolState.position.x,
                toolState.position.y <= 0f ? 0.5f : toolState.position.y,
                toolState.position.z);
            return RemapJsonPosition(toolId, jsonPos);
        }

        int row = index / 3;
        int col = index % 3;
        Vector3 fallback = sceneCenter + new Vector3(
            col * toolSpacing - toolSpacing + 5f,
            0.5f,
            row * toolSpacing - toolSpacing
        );
        return RemapJsonPosition(toolId, fallback);
    }

    bool IsCognitiveStation(string toolId, ToolState state)
    {
        if (state != null && state.isCognitiveStation) return true;
        if (!string.IsNullOrEmpty(toolId) && toolId.StartsWith("cognitive_")) return true;
        if (!string.IsNullOrWhiteSpace(state?.actr_layer)) return true;
        return false;
    }

    void ConfigureSpecialCognitiveStation(GameObject toolGO, string toolId)
    {
        if (toolGO == null || string.IsNullOrWhiteSpace(toolId)) return;

        string baseId = StripZoneSuffix(toolId);
        int zoneIndex = ExtractZoneIndex(toolId, 0);
        string displayName = "";
        DeclarativeObjectMetadata metadata = toolGO.GetComponent<DeclarativeObjectMetadata>();
        if (metadata != null)
            displayName = metadata.displayName ?? "";

        if (string.Equals(baseId, "cognitive_008", StringComparison.OrdinalIgnoreCase))
        {
            GoalBufferStationPresenter gbp = toolGO.GetComponent<GoalBufferStationPresenter>()
                ?? toolGO.AddComponent<GoalBufferStationPresenter>();
            gbp.ConfigureZone(zoneIndex);
        }

        // Temporal UI is intentionally limited to zone 0 for now, matching the current scene requirement.
        if (zoneIndex != 0) return;

        if (IsTemporalModuleStation(baseId, displayName))
        {
            TemporalStationPresenter.GetOrCreateForStation(toolGO, TemporalStationDisplayMode.Module, zoneIndex);
        }
    }

    static bool IsTemporalModuleStation(string baseId, string displayName)
    {
        return string.Equals(baseId, "cognitive_006", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(displayName, "Temporal Module", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsTemporalBufferStation(string baseId, string displayName)
    {
        return string.Equals(baseId, "cognitive_014", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(displayName, "Temporal Buffer", StringComparison.OrdinalIgnoreCase);
    }

    static string StripZoneSuffix(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return string.Empty;
        int zoneIdx = id.LastIndexOf("_zone", StringComparison.OrdinalIgnoreCase);
        return zoneIdx > 0 ? id.Substring(0, zoneIdx) : id;
    }

    static int ExtractZoneIndex(string id, int fallback)
    {
        if (string.IsNullOrWhiteSpace(id)) return fallback;
        int zoneIdx = id.LastIndexOf("_zone", StringComparison.OrdinalIgnoreCase);
        if (zoneIdx < 0) return fallback;

        string suffix = id.Substring(zoneIdx + 5);
        return int.TryParse(suffix, out int zone) ? zone : fallback;
    }

    /// <summary>
    /// Returns true when <paramref name="toolId"/> is a cognitive station in the currently loaded
    /// scene data.  Checks the <c>isCognitiveStation</c> flag first (data-driven), then falls back
    /// to the project-wide "cognitive_" naming convention so that legacy/offline scenes still work.
    /// </summary>
    public bool IsKnownCognitiveStation(string toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId)) return false;

        if (sceneData?.initialStates != null)
        {
            // Exact key lookup (e.g. "cognitive_001_zone0")
            if (sceneData.initialStates.TryGetValue(toolId, out ToolState ts) && ts != null)
                return IsCognitiveStation(toolId, ts);

            // Strip zone suffix (e.g. "cognitive_001_zone0" → "cognitive_001") and try again
            int zoneSep = toolId.LastIndexOf("_zone", System.StringComparison.OrdinalIgnoreCase);
            if (zoneSep > 0)
            {
                string baseId = toolId.Substring(0, zoneSep);
                if (sceneData.initialStates.TryGetValue(baseId, out ToolState tsBase) && tsBase != null)
                    return IsCognitiveStation(baseId, tsBase);
            }
        }

        // Convention fallback — covers scenes where data is not yet loaded
        return toolId.StartsWith("cognitive_", System.StringComparison.OrdinalIgnoreCase);
    }
    
    void AddObjectHighlight(GameObject obj, string type)
    {
        // Yellow sphere + point light removed — they cluttered the scene and read as "bulb" markers.
        // Proximity / selection feedback can be added here later if needed (non-lit mesh only).
    }

    /// <summary>Rank in spatially sorted list → alternating band below / above baseline.</summary>
    static float SpatialStaggerDeltaY(int spatialRank, float amplitude)
    {
        if (amplitude <= 0f) return 0f;
        return (spatialRank & 1) == 0 ? -amplitude : amplitude;
    }

    const string ToolNameLabelChild = "ToolNameLabel";
    const string CognitiveLabelChild = "CognitiveLabel";
    const string AgentIdLabelChild = "AgentIdLabel";

    /// <summary>
    /// Spawn order does not match layout on the ground — neighbors can share the same band and still overlap.
    /// After all tools exist, sort by world (Z, X) and push alternate labels up/down.
    /// </summary>
    void ApplySpatialStaggerToGeneratedToolLabels()
    {
        if (nameLabelHeightStagger <= 0f || generatedTools == null || generatedTools.Count == 0)
            return;

        var withLabels = new List<GameObject>(generatedTools.Count);
        foreach (var kvp in generatedTools)
        {
            GameObject go = kvp.Value;
            if (go == null) continue;
            if (go.transform.Find(ToolNameLabelChild) != null || go.transform.Find(CognitiveLabelChild) != null)
                withLabels.Add(go);
        }

        if (withLabels.Count <= 1)
            return;

        withLabels.Sort(CompareToolWorldPositionZX);

        for (int i = 0; i < withLabels.Count; i++)
        {
            Transform label = withLabels[i].transform.Find(ToolNameLabelChild);
            if (label == null)
                label = withLabels[i].transform.Find(CognitiveLabelChild);
            if (label == null) continue;

            float dy = SpatialStaggerDeltaY(i, nameLabelHeightStagger);
            Vector3 lp = label.localPosition;
            label.localPosition = new Vector3(lp.x, lp.y + dy, lp.z);
        }
    }

    void ApplySpatialStaggerToGeneratedAgentLabels()
    {
        if (nameLabelHeightStagger <= 0f || generatedAgents == null || generatedAgents.Count == 0)
            return;

        var withLabels = new List<GameObject>(generatedAgents.Count);
        foreach (var kvp in generatedAgents)
        {
            GameObject go = kvp.Value;
            if (go == null) continue;
            if (go.transform.Find(AgentIdLabelChild) != null)
                withLabels.Add(go);
        }

        if (withLabels.Count <= 1)
            return;

        withLabels.Sort(CompareToolWorldPositionZX);

        for (int i = 0; i < withLabels.Count; i++)
        {
            Transform label = withLabels[i].transform.Find(AgentIdLabelChild);
            if (label == null) continue;

            float dy = SpatialStaggerDeltaY(i, nameLabelHeightStagger);
            Vector3 lp = label.localPosition;
            label.localPosition = new Vector3(lp.x, lp.y + dy, lp.z);
        }
    }

    static int CompareToolWorldPositionZX(GameObject a, GameObject b)
    {
        Vector3 pa = a.transform.position;
        Vector3 pb = b.transform.position;
        const float tie = 0.001f;
        float dz = pa.z - pb.z;
        if (Mathf.Abs(dz) > tie) return dz < 0f ? -1 : 1;
        float dx = pa.x - pb.x;
        if (Mathf.Abs(dx) > tie) return dx < 0f ? -1 : 1;
        return string.Compare(a.name, b.name, StringComparison.Ordinal);
    }

    
    void UpdateToolVisuals(GameObject toolGO, ToolState toolState)
    {
        Renderer renderer = toolGO.GetComponent<Renderer>();
        if (renderer == null) return;
        
        // Set color based on tool state
        Color toolColor = Color.white;
        if (!string.IsNullOrWhiteSpace(toolState.color) && ColorUtility.TryParseHtmlString(toolState.color, out Color parsedColor))
        {
            toolColor = parsedColor;
        }
        
        if (toolState.properties != null)
        {
            if (toolState.properties.power == "on")
            {
                toolColor = Color.green; // Powered on
            }
            else if (toolState.properties.power == "off")
            {
                toolColor = Color.red; // Powered off
            }
            
            if (toolState.properties.locked)
            {
                toolColor = Color.yellow; // Locked
            }
        }
        
        if (!toolState.isAvailable)
        {
            toolColor = Color.gray; // Unavailable
        }
        
        renderer.material.color = toolColor;
        
        // Add visual indicators for tool state
        AddToolStateIndicators(toolGO, toolState);
    }
    
    void AddToolStateIndicators(GameObject toolGO, ToolState toolState)
    {
        // Add power indicator light
        if (toolState.properties?.power == "on")
        {
            GameObject lightGO = new GameObject("PowerLight");
            lightGO.transform.SetParent(toolGO.transform);
            lightGO.transform.localPosition = Vector3.up * 1.5f;
            
            Light light = lightGO.AddComponent<Light>();
            light.color = Color.green;
            light.intensity = 2f;
            light.range = 3f;
        }
        
        // Add lock indicator
        if (toolState.properties?.locked == true)
        {
            GameObject lockGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lockGO.transform.SetParent(toolGO.transform);
            lockGO.transform.localPosition = Vector3.up * 0.8f;
            lockGO.transform.localScale = Vector3.one * 0.3f;
            
            Renderer lockRenderer = lockGO.GetComponent<Renderer>();
            lockRenderer.material.color = Color.yellow;
        }
    }
    
    void GenerateAgents()
    {
        Debug.Log("=== GENERATING AGENTS ===");
        
        if (sceneData.agentProfiles == null)
        {
            Debug.LogError("No agent profiles found in scene data!");
            return;
        }
        
        Debug.Log($"Found {sceneData.agentProfiles.Count} agents to generate");
        
        int agentIndex = 0;
        foreach (var agentEntry in sceneData.agentProfiles)
        {
            string agentId = agentEntry.Key;
            AgentProfile agentProfile = agentEntry.Value;

            int agentZone = agentProfile?.zoneIndex ?? ExtractZoneIndex(agentId, 0);
            if (!ShouldSpawnZone(agentZone))
                continue;

            if (BsgIntegrationSettings.UsePhotonPlayerAsPhysicalAgent && IsPhysicalAgentProfile(agentId, agentProfile))
            {
                Debug.Log($"[SceneGenerator] Skipping physical agent '{agentId}' — Photon player will perform RAG physical steps.");
                continue;
            }
            
            Debug.Log($"Creating agent: {agentId} at index {agentIndex}");
            
            GameObject agentGO = CreateAgentObject(agentId, agentProfile, agentIndex);
            if (agentGO != null)
            {
                generatedAgents[agentId] = agentGO;
                Debug.Log($"Successfully created agent: {agentId} at position {agentGO.transform.position}");
                agentIndex++;
            }
            else
            {
                Debug.LogError($"Failed to create agent: {agentId}");
            }
        }

        ApplySpatialStaggerToGeneratedAgentLabels();
        
        Debug.Log($"Agent generation complete. Created {generatedAgents.Count} agents.");
    }
    
    GameObject CreateAgentObject(string agentId, AgentProfile agentProfile, int index)
    {
        // Build on an empty root (HumanBodyBuilder hides the capsule renderer)
        GameObject agentGO = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        agentGO.transform.SetParent(agentsParent);

        // Resolve agent tint from profile color field; fallback to white
        Color agentColor = Color.white;
        if (!string.IsNullOrWhiteSpace(agentProfile?.color) &&
            !ColorUtility.TryParseHtmlString(agentProfile.color, out agentColor))
            agentColor = Color.white;

        // Build the realistic humanoid body (hides bare capsule, adds full limb hierarchy)
        HumanBodyBuilder.BuildBody(agentGO, agentColor);

        bool isMentalScale = agentProfile != null && agentProfile.role != null
            && agentProfile.role.IndexOf("mental", System.StringComparison.OrdinalIgnoreCase) >= 0;
        if (!isMentalScale && agentId.StartsWith("M", System.StringComparison.OrdinalIgnoreCase))
            isMentalScale = true;
        if (isMentalScale && agentId.StartsWith("P", System.StringComparison.OrdinalIgnoreCase))
            isMentalScale = false;

        float agentScale = isMentalScale ? ragMentalAgentScale : ragPhysicalAgentScale;
        if (UsesSceneAnchorLayout())
            agentScale *= BsgIntegrationSettings.MultiplayerAgentScaleMultiplier;
        if (agentScale > 0.001f && Mathf.Abs(agentScale - 1f) > 0.001f)
            agentGO.transform.localScale = Vector3.one * agentScale;

        // Procedural walking animation driven by move speed
        float spd = agentProfile != null ? Mathf.Clamp(agentProfile.skillLevel > 0 ? agentProfile.skillLevel / 40f : 2.5f, 1f, 4f) : 2.5f;
        HumanWalkAnimation walkAnim = agentGO.AddComponent<HumanWalkAnimation>();
        walkAnim.walkCycleSpeed = spd * 0.42f;

        // Use profile position if set; otherwise fall back to a default spread south of the cognitive grid
        Vector3 position;
        if (agentProfile != null && agentProfile.position != null &&
            (agentProfile.position.x != 0f || agentProfile.position.z != 0f))
        {
            position = RemapJsonPosition(agentId, new Vector3(agentProfile.position.x, 0f, agentProfile.position.z));
        }
        else
        {
            position = RemapJsonPosition(agentId, sceneCenter + new Vector3(-2f + index * 4f, 0f, -9f));
        }

        agentGO.transform.position = position;
        agentGO.name = $"Agent_{agentId}";

        if (agentGO.GetComponent<AgentCognitiveMemory>() == null)
            agentGO.AddComponent<AgentCognitiveMemory>();

        AttachAgentIdLabel(agentGO, agentId, agentColor);
        EnsureRagMover(agentGO, agentId, spd, agentProfile);

        AgentGroundMotor motor = agentGO.GetComponent<AgentGroundMotor>();
        if (motor != null)
            motor.SnapFeetToGround();

        return agentGO;
    }

    static bool IsPhysicalAgentProfile(string agentId, AgentProfile agentProfile)
    {
        if (agentProfile != null && !string.IsNullOrWhiteSpace(agentProfile.role))
        {
            if (agentProfile.role.IndexOf("mental", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            if (agentProfile.role.IndexOf("physical", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return !string.IsNullOrEmpty(agentId)
               && agentId.StartsWith("P", System.StringComparison.OrdinalIgnoreCase);
    }

    void EnsureRagMover(GameObject agentGO, string agentId, float moveSpeed = 2.5f, AgentProfile agentProfile = null)
    {
        if (string.IsNullOrWhiteSpace(agentId)) return;

        bool isMental = false;
        bool isPhysical = false;

        if (agentProfile != null && !string.IsNullOrWhiteSpace(agentProfile.role))
        {
            isMental  = agentProfile.role.IndexOf("mental",  System.StringComparison.OrdinalIgnoreCase) >= 0;
            isPhysical = agentProfile.role.IndexOf("physical", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        if (!isMental && !isPhysical)
        {
            if      (agentId.StartsWith("P", System.StringComparison.OrdinalIgnoreCase)) isPhysical = true;
            else if (agentId.StartsWith("M", System.StringComparison.OrdinalIgnoreCase)) isMental   = true;
            else return;
        }

        // Kinematic RB: movement is driven by RagSequenceAgentMover (transform), not physics forces.
        // Dynamic + station solid colliders caused agents to bounce and never finish dwell at stations.
        Rigidbody rb = agentGO.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = agentGO.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.freezeRotation = true;
            rb.constraints = RigidbodyConstraints.FreezeRotationX
                           | RigidbodyConstraints.FreezeRotationZ
                           | RigidbodyConstraints.FreezePositionY;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }
        rb.isKinematic = true;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        CapsuleCollider agentCap = agentGO.GetComponent<CapsuleCollider>();
        if (agentCap == null)
        {
            agentCap = agentGO.AddComponent<CapsuleCollider>();
            agentCap.height = 1.75f;
            agentCap.radius = 0.28f;
            agentCap.center = new Vector3(0f, 0.875f, 0f);
        }
        agentCap.isTrigger = false;
        ScenePhysicsLayers.ApplyCharacterLayer(agentGO);

        AgentGroundMotor groundMotor = agentGO.GetComponent<AgentGroundMotor>();
        if (groundMotor == null)
            groundMotor = agentGO.AddComponent<AgentGroundMotor>();
        groundMotor.clampZoneIndex = agentProfile?.zoneIndex ?? 0;

        RagSequenceAgentMover mover = agentGO.GetComponent<RagSequenceAgentMover>();
        if (mover == null) mover = agentGO.AddComponent<RagSequenceAgentMover>();
        mover.agentId = agentId;
        mover.moveSpeed = isMental ? Mathf.Clamp(ragMentalCognitiveMoveSpeed, 2f, 14f) : moveSpeed;
        mover.avoidCognitiveObstacles = true;
        mover.useProximityCognitiveSteering = true;
        // Looser threshold when agents are scaled up (uniform scale on root affects footprint vs station center).
        float scale = Mathf.Max(agentGO.transform.localScale.x, 0.85f);
        mover.reachThreshold = Mathf.Max(1.05f, 0.88f * scale);
        mover.cognitiveInteractionStandDistance = Mathf.Max(0.82f, agentCap.radius * scale + 0.24f);
        mover.destinationNavRelaxDistance = 0.22f;
        // Gate ALL non-mental agents on their zone's mental leader cognitive completion.
        // Do NOT use agentId prefix ("P") — isMental is the authoritative role flag set from AgentProfile.role.
        mover.gateOperationalOnLeaderCognitive = !isMental;
        mover.isMentalAgent = isMental;

        // Zone-aware leader: prefer per-agent leaderAgentId from profile; fall back to global
        mover.zoneIndex = agentProfile?.zoneIndex ?? 0;
        if (isPhysical && agentProfile != null && !string.IsNullOrEmpty(agentProfile.leaderAgentId))
            mover.mentalLeaderAgentId = agentProfile.leaderAgentId;
        else
        {
            string leader = RagSceneJsonBridge.LastParsedLeaderAgentId;
            mover.mentalLeaderAgentId = string.IsNullOrWhiteSpace(leader) ? "M1" : leader;
        }

        // Keep the walk animation synced to RagSequenceAgentMover movement speed
        HumanWalkAnimation wa = agentGO.GetComponent<HumanWalkAnimation>();
        if (wa != null) wa.walkCycleSpeed = mover.moveSpeed * 0.42f;
    }

    void AttachAgentIdLabel(GameObject agentGO, string agentId, Color labelColor = default)
    {
        if (agentGO == null || string.IsNullOrWhiteSpace(agentId)) return;

        const string labelName = "AgentIdLabel";
        Transform existing = agentGO.transform.Find(labelName);
        if (existing != null) Destroy(existing.gameObject);

        GameObject label = new GameObject(labelName);
        label.transform.SetParent(agentGO.transform, false);
        label.transform.localPosition = new Vector3(0f, 2.55f, 0f);
        label.transform.localRotation = Quaternion.Euler(55f, 0f, 0f);

        Color col = labelColor == default ? Color.white : labelColor;

        TextMesh tm = label.AddComponent<TextMesh>();
        tm.text = agentId.Trim();
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.characterSize = 0.056f;
        tm.fontSize = 56;
        tm.fontStyle = FontStyle.Bold;
        tm.color = col;

        if (label.GetComponent<IndicatorBillboard>() == null)
            label.AddComponent<IndicatorBillboard>();
    }

    void AttachToolObjectNameLabel(GameObject toolGO, ToolState toolState, string toolId)
    {
        if (toolGO == null || string.IsNullOrWhiteSpace(toolId)) return;

        const string labelName = "ToolNameLabel";
        Transform existing = toolGO.transform.Find(labelName);
        if (existing != null) Destroy(existing.gameObject);

        string title = toolState != null && !string.IsNullOrWhiteSpace(toolState.name)
            ? toolState.name.Trim()
            : toolId;

        GameObject labelGo = new GameObject(labelName);
        labelGo.transform.SetParent(toolGO.transform, false);

        float yLift = 1.05f;
        Renderer rr = toolGO.GetComponent<Renderer>();
        if (rr == null) rr = toolGO.GetComponentInChildren<Renderer>();
        if (rr != null)
            yLift = Mathf.Clamp(rr.bounds.extents.y + 0.42f, 0.75f, 6f);

        labelGo.transform.localPosition = new Vector3(0f, yLift, 0f);

        TextMesh tm = labelGo.AddComponent<TextMesh>();
        tm.text = title;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.characterSize = 0.068f;
        tm.fontSize = 44;
        tm.fontStyle = FontStyle.Bold;
        tm.color = new Color(0.97f, 0.97f, 1f, 1f);

        labelGo.AddComponent<IndicatorBillboard>();
    }
    
    void UpdateAgentVisuals(GameObject agentGO, AgentProfile agentProfile)
    {
        Renderer renderer = agentGO.GetComponent<Renderer>();
        if (renderer == null) return;
        
        // Calculate average skill level for color coding
        float avgSkill = 0f;
        int skillCount = 0;
        
        if (agentProfile.onetSkillLevels != null)
        {
            foreach (var skill in agentProfile.onetSkillLevels.Values)
            {
                avgSkill += skill;
                skillCount++;
            }
        }
        
        if (skillCount > 0)
        {
            avgSkill /= skillCount;
            
            // Color code based on skill level (1-5 scale)
            Color agentColor = Color.Lerp(Color.black, Color.green, avgSkill / 5f);
            renderer.material.color = agentColor;
        }
    }
    
    void GenerateEnvironment()
    {
        if (ShouldSkipEnvironmentGeneration())
            return;

        if (singleZoneMode || BsgIntegrationSettings.SingleZoneMode)
        {
            GenerateSingleZoneEnvironment();
            return;
        }

        // Mirrors ReplicaSceneSetup / feat/implement-rag-cognitive-system-2.1 — same constants and placement
        // so RAG-generated worlds match the legacy 4-zone layout (40×40 cells, GridShiftZ screen framing).
        const float ZoneHalf   = 20f;   // each ground = 40×40 (plane scale 4)
        const float GridShiftZ = 18f;   // shifts grid +Z so Display 1 leaves lower area for stats UI
        const float WallH      = 4f;
        const float WallT      = 1f;    // match ReplicaSceneSetup (not thin 0.6)
        const float WallLen    = 41f;   // zone edge + corner overlap

        Vector3[] zoneOffsets =
        {
            new Vector3(-ZoneHalf, 0f, -ZoneHalf + GridShiftZ),
            new Vector3( ZoneHalf, 0f, -ZoneHalf + GridShiftZ),
            new Vector3(-ZoneHalf, 0f,  ZoneHalf + GridShiftZ),
            new Vector3( ZoneHalf, 0f,  ZoneHalf + GridShiftZ),
        };

        Color[] zoneColors =
        {
            new Color(0.05f, 0.55f, 0.05f),
            new Color(0.05f, 0.35f, 0.55f),
            new Color(0.45f, 0.15f, 0.05f),
            new Color(0.35f, 0.05f, 0.45f),
        };

        SceneUILoader loader = GetComponent<SceneUILoader>();
        if (loader != null)
        {
            string rawJson = loader.RawJsonText;
            if (!string.IsNullOrEmpty(rawJson) && RagSceneJsonBridge.IsRagEnvelope(rawJson))
                TryReadZoneColors(rawJson, zoneColors);
        }

        // Grey apron under the grid — visible margin around the 80×80 colour floor (space reads as UI gutter like branch builds).
        GameObject apron = GameObject.CreatePrimitive(PrimitiveType.Plane);
        apron.transform.SetParent(environmentParent);
        apron.name = "JSON_BaseGreyPlane";
        apron.transform.position = new Vector3(0f, -0.06f, GridShiftZ);
        apron.transform.localScale = new Vector3(18f, 1f, 18f);
        ApplyUnlitColor(apron.GetComponent<Renderer>(), new Color(0.42f, 0.42f, 0.44f));
        Destroy(apron.GetComponent<Collider>());

        Color outerWallColor = new Color(0.55f, 0.55f, 0.55f);
        Color divColor       = new Color(0.40f, 0.40f, 0.40f);

        for (int i = 0; i < 4; i++)
        {
            Vector3 o = zoneOffsets[i];
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.SetParent(environmentParent);
            ground.transform.position = o;
            ground.transform.localScale = new Vector3(4f, 1f, 4f);
            ground.name = $"Ground_Zone{i}";
            ApplyUnlitColor(ground.GetComponent<Renderer>(), zoneColors[i]);
            Destroy(ground.GetComponent<Collider>());

            float outerZ_south = o.z - ZoneHalf;
            float outerZ_north = o.z + ZoneHalf;
            float outerX_west  = o.x - ZoneHalf;
            float outerX_east  = o.x + ZoneHalf;

            bool isSouth = (i == 0 || i == 1);
            bool isNorth = (i == 2 || i == 3);
            bool isWest  = (i == 0 || i == 2);
            bool isEast  = (i == 1 || i == 3);

            if (isSouth)
                CreateWallPrim($"Wall_Zone{i}_S", new Vector3(o.x, WallH * 0.5f, outerZ_south), new Vector3(WallLen, WallH, WallT), outerWallColor);
            if (isNorth)
                CreateWallPrim($"Wall_Zone{i}_N", new Vector3(o.x, WallH * 0.5f, outerZ_north), new Vector3(WallLen, WallH, WallT), outerWallColor);
            if (isWest)
                CreateWallPrim($"Wall_Zone{i}_W", new Vector3(outerX_west, WallH * 0.5f, o.z), new Vector3(WallT, WallH, WallLen), outerWallColor);
            if (isEast)
                CreateWallPrim($"Wall_Zone{i}_E", new Vector3(outerX_east, WallH * 0.5f, o.z), new Vector3(WallT, WallH, WallLen), outerWallColor);
        }

        float totalLen = ZoneHalf * 4f + WallT;
        float centreZ  = GridShiftZ;
        CreateWallPrim("Wall_Divider_H", new Vector3(0f, WallH * 0.5f, centreZ), new Vector3(totalLen, WallH, WallT), divColor);
        CreateWallPrim("Wall_Divider_V", new Vector3(0f, WallH * 0.5f, centreZ), new Vector3(WallT, WallH, totalLen), divColor);
    }

    void GenerateSingleZoneEnvironment()
    {
        const float ZoneHalf = 20f;
        const float WallH = 4f;
        const float WallT = 1f;
        const float WallLen = 41f;
        Vector3 origin = singleZoneMode ? zoneWorldOrigin : BsgIntegrationSettings.ZoneWorldOrigin;

        GameObject apron = GameObject.CreatePrimitive(PrimitiveType.Plane);
        apron.transform.SetParent(environmentParent);
        apron.name = "JSON_BaseGreyPlane";
        apron.transform.position = origin + new Vector3(0f, -0.06f, 0f);
        apron.transform.localScale = new Vector3(6f, 1f, 6f);
        ApplyUnlitColor(apron.GetComponent<Renderer>(), new Color(0.42f, 0.42f, 0.44f));
        Destroy(apron.GetComponent<Collider>());

        Color zoneColor = new Color(0.05f, 0.55f, 0.05f);
        Color[] zoneColors = { zoneColor, zoneColor, zoneColor, zoneColor };
        SceneUILoader loader = GetComponent<SceneUILoader>();
        if (loader != null && !string.IsNullOrEmpty(loader.RawJsonText))
            TryReadZoneColors(loader.RawJsonText, zoneColors);
        zoneColor = zoneColors[0];

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.SetParent(environmentParent);
        ground.transform.position = origin;
        ground.transform.localScale = new Vector3(4f, 1f, 4f);
        ground.name = "Ground_Zone0";
        ApplyUnlitColor(ground.GetComponent<Renderer>(), zoneColor);
        Destroy(ground.GetComponent<Collider>());

        Color outerWallColor = new Color(0.55f, 0.55f, 0.55f);
        float outerZ_south = origin.z - ZoneHalf;
        float outerZ_north = origin.z + ZoneHalf;
        float outerX_west = origin.x - ZoneHalf;
        float outerX_east = origin.x + ZoneHalf;

        CreateWallPrim("Wall_Zone0_S", new Vector3(origin.x, WallH * 0.5f, outerZ_south), new Vector3(WallLen, WallH, WallT), outerWallColor);
        CreateWallPrim("Wall_Zone0_N", new Vector3(origin.x, WallH * 0.5f, outerZ_north), new Vector3(WallLen, WallH, WallT), outerWallColor);
        CreateWallPrim("Wall_Zone0_W", new Vector3(outerX_west, WallH * 0.5f, origin.z), new Vector3(WallT, WallH, WallLen), outerWallColor);
        CreateWallPrim("Wall_Zone0_E", new Vector3(outerX_east, WallH * 0.5f, origin.z), new Vector3(WallT, WallH, WallLen), outerWallColor);

        Debug.Log($"[SceneGenerator] Single-zone environment at {origin}");
    }

    void CreateWallPrim(string name, Vector3 pos, Vector3 scale, Color color)
    {
        GameObject w = GameObject.CreatePrimitive(PrimitiveType.Cube);
        w.transform.SetParent(environmentParent);
        w.transform.position = pos;
        w.transform.localScale = scale;
        w.name = name;
        ApplyUnlitColor(w.GetComponent<Renderer>(), color);
        EnvironmentSolidCollider.EnsureOnObject(w, addBoxIfMissing: false);
    }

    void TryReadZoneColors(string rawJson, Color[] target)
    {
        // Parse "zones": [ { "zoneIndex": 0, "groundColor": "#..." }, ... ]
        int zonesKey = rawJson.IndexOf("\"zones\"", StringComparison.Ordinal);
        if (zonesKey < 0) return;
        int arrOpen = rawJson.IndexOf('[', zonesKey);
        if (arrOpen < 0) return;

        int pos = arrOpen + 1;
        while (pos < rawJson.Length)
        {
            int objStart = rawJson.IndexOf('{', pos);
            if (objStart < 0) break;
            int depth = 0;
            int objEnd = objStart;
            for (int k = objStart; k < rawJson.Length; k++)
            {
                if (rawJson[k] == '{') depth++;
                else if (rawJson[k] == '}') { depth--; if (depth == 0) { objEnd = k; break; } }
            }
            string obj = rawJson.Substring(objStart, objEnd - objStart + 1);

            int zi = ExtractJsonInt(obj, "zoneIndex");
            string gc = ExtractJsonString(obj, "groundColor");
            if (zi >= 0 && zi < target.Length && !string.IsNullOrEmpty(gc))
                ColorUtility.TryParseHtmlString(gc, out target[zi]);

            pos = objEnd + 1;
            if (rawJson[pos >= rawJson.Length ? rawJson.Length - 1 : pos] == ']') break;
        }
    }

    static int ExtractJsonInt(string json, string key)
    {
        string token = "\"" + key + "\"";
        int idx = json.IndexOf(token, StringComparison.Ordinal);
        if (idx < 0) return -1;
        int colon = json.IndexOf(':', idx + token.Length);
        if (colon < 0) return -1;
        int start = colon + 1;
        while (start < json.Length && (json[start] == ' ' || json[start] == '\n' || json[start] == '\r')) start++;
        int end = start;
        while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-')) end++;
        if (end == start) return -1;
        return int.TryParse(json.Substring(start, end - start), out int v) ? v : -1;
    }

    static string ExtractJsonString(string json, string key)
    {
        string token = "\"" + key + "\"";
        int idx = json.IndexOf(token, StringComparison.Ordinal);
        if (idx < 0) return null;
        int colon = json.IndexOf(':', idx + token.Length);
        if (colon < 0) return null;
        int q1 = json.IndexOf('"', colon + 1);
        if (q1 < 0) return null;
        int q2 = json.IndexOf('"', q1 + 1);
        if (q2 < 0) return null;
        return json.Substring(q1 + 1, q2 - q1 - 1);
    }

    static void ApplyUnlitColor(Renderer r, Color color)
    {
        if (r == null) return;
        Shader sh = Shader.Find("Unlit/Color") ?? Shader.Find("Legacy Shaders/Diffuse");
        if (sh != null)
        {
            Material mat = new Material(sh);
            mat.color = color;
            r.material = mat;
        }
        else
        {
            r.material.color = color;
        }
    }

    void CreateWallsWithPrefab()
    {
        Vector3[] wallPositions = {
            new Vector3(-floorSize/2, wallHeight/2, 0),
            new Vector3(floorSize/2, wallHeight/2, 0),
            new Vector3(0, wallHeight/2, -floorSize/2),
            new Vector3(0, wallHeight/2, floorSize/2)
        };

        Vector3[] wallRotations = {
            Vector3.zero, Vector3.zero,
            new Vector3(0, 90, 0), new Vector3(0, 90, 0)
        };

        for (int i = 0; i < wallPositions.Length; i++)
        {
            GameObject wall = Instantiate(wallPrefab, environmentParent);
            wall.transform.position = wallPositions[i];
            wall.transform.eulerAngles = wallRotations[i];
            wall.name = $"Generated_Wall_{i}";
        }
    }

    void CreateWallsWithPrimitives()
    {
        // Teal/cyan walls matching the original BSG scene walls
        // Grey perimeter walls (match legacy replica / reference screenshots)
        Color wallColor = new Color(0.55f, 0.55f, 0.55f);
        float h = floorSize / 2f;

        Vector3[] wallPositions = {
            new Vector3(-h, wallHeight/2, 0),
            new Vector3( h, wallHeight/2, 0),
            new Vector3(0, wallHeight/2, -h),
            new Vector3(0, wallHeight/2,  h)
        };

        Vector3[] wallScales = {
            new Vector3(1, wallHeight, floorSize),
            new Vector3(1, wallHeight, floorSize),
            new Vector3(floorSize, wallHeight, 1),
            new Vector3(floorSize, wallHeight, 1)
        };
        
        for (int i = 0; i < wallPositions.Length; i++)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.SetParent(environmentParent);
            wall.transform.position = wallPositions[i];
            wall.transform.localScale = wallScales[i];
            wall.name = $"JSON_Generated_Wall_{i}";

            ApplyUnlitColor(wall.GetComponent<Renderer>(), wallColor);
        }
    }
    
    void SetupSceneLighting()
    {
        // Create directional light if none exists
        if (FindObjectOfType<Light>() == null)
        {
            GameObject lightGO = new GameObject("Generated_DirectionalLight");
            lightGO.transform.SetParent(environmentParent);
            
            Light light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            light.color = Color.white;
            light.transform.rotation = Quaternion.Euler(45f, 45f, 0f);
        }
        
        // Add ambient lighting
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Color.white;
        RenderSettings.ambientEquatorColor = Color.gray;
        RenderSettings.ambientGroundColor = new Color(0.3f, 0.3f, 0.3f);
        
        // Ensure camera can see new objects
        SetupCameraVisibility();
    }
    
    void SetupCameraVisibility()
    {
        if (multiplayerEmbedMode || BsgIntegrationSettings.SuppressRagCameraOverride)
        {
            Debug.Log("[SceneGenerator] Multiplayer embed — keeping existing player camera.");
            return;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera == null) return;

        // Tighter on the 80×80 grid than the ultra-wide safety shot — same centre line as Replica (Z = GridShiftZ − 42).
        const float GridShiftZ = 18f;
        mainCamera.transform.position = new Vector3(0f, 54f, GridShiftZ - 42f);
        mainCamera.transform.rotation = Quaternion.Euler(54f, 0f, 0f);
        mainCamera.fieldOfView = 80f;
        mainCamera.farClipPlane = Mathf.Max(mainCamera.farClipPlane, 500f);
        mainCamera.targetDisplay = 0;
        mainCamera.backgroundColor = new Color(0.06f, 0.06f, 0.12f, 1f);

        Debug.Log($"[SceneGenerator] Display 1 overview (zoomed on 4 zones): pos={mainCamera.transform.position} FOV=80");
    }

    void EnsureCognitiveRuntimeSystems()
    {
        // Phase 1 scope: only station spawn + light interaction.
        // Advanced transfer contracts/reward integration are deferred.
        CognitiveTransferContractEngine contractEngine = FindObjectOfType<CognitiveTransferContractEngine>();
        if (contractEngine != null)
        {
            Destroy(contractEngine.gameObject);
        }

        CognitiveRewardIntegrator rewardIntegrator = FindObjectOfType<CognitiveRewardIntegrator>();
        if (rewardIntegrator == null)
        {
            var rewardGo = new GameObject("CognitiveRewardIntegrator");
            rewardGo.AddComponent<CognitiveRewardIntegrator>();
        }

        CognitiveInteractionHUD existingHud = FindObjectOfType<CognitiveInteractionHUD>();
        if (existingHud != null)
        {
            Destroy(existingHud.gameObject);
        }

        EnsureTemporalRuntimeForAllZones();
        EnsureTemporalUiForZoneZero();
    }

    void EnsureTemporalRuntimeForAllZones()
    {
        HashSet<int> zones = new HashSet<int>();
        zones.Add(0);

        if (sceneData?.agentProfiles != null)
        {
            foreach (var kvp in sceneData.agentProfiles)
            {
                AgentProfile profile = kvp.Value;
                if (profile != null && profile.zoneIndex >= 0)
                    zones.Add(profile.zoneIndex);
            }
        }

        foreach (var kvp in generatedTools)
        {
            string key = kvp.Key;
            int zone = ExtractZoneIndex(key, -1);
            if (zone >= 0)
                zones.Add(zone);
        }

        foreach (int zone in zones)
            TemporalCognitionRuntime.GetOrCreate(zone);
    }

    void EnsureTemporalUiForZoneZero()
    {
        if (multiplayerEmbedMode || BsgIntegrationSettings.SuppressRagTrainingHud)
            return;

        TemporalCognitionRuntime.GetOrCreate(0);
        TemporalBufferGaugeUI.GetOrCreate(0);
        DeclarativeMemoryGaugeUI.GetOrCreate(0);
        CognitiveStationDetailPanelUI.GetOrCreate(0);
        EnsureZone0SkillGaugeHud();

        bool moduleAttached = false;

        foreach (var kvp in generatedTools)
        {
            GameObject go = kvp.Value;
            if (go == null) continue;

            string baseId = StripZoneSuffix(kvp.Key);
            int zoneIndex = ExtractZoneIndex(kvp.Key, 0);
            if (zoneIndex != 0) continue;

            DeclarativeObjectMetadata metadata = go.GetComponent<DeclarativeObjectMetadata>();
            string displayName = metadata != null ? metadata.displayName : "";

            if (IsTemporalModuleStation(baseId, displayName))
            {
                TemporalStationPresenter.GetOrCreateForStation(go, TemporalStationDisplayMode.Module, 0);
                moduleAttached = true;
            }
        }

        if (!moduleAttached)
            AttachTemporalPresenterBySceneScan("cognitive_006", "Temporal Module", TemporalStationDisplayMode.Module);
    }

    /// <summary>Zone 0 physical skill gauge stack (matches MultiplayerSetup / JSONWorkflowSceneML HUD).</summary>
    static void EnsureZone0SkillGaugeHud()
    {
        if (BsgIntegrationSettings.UsePhotonPlayerAsPhysicalAgent)
            return;

        AgentSkillGaugeUI ui = FindObjectOfType<AgentSkillGaugeUI>();
        if (ui == null)
        {
            GameObject go = new GameObject("AgentSkillGaugeUI");
            ui = go.AddComponent<AgentSkillGaugeUI>();
        }

        ui.enabled = true;
        ui.showOnlyPhysicalRagAgents = true;
        ui.showOnlyZoneIndex = 0;
        ui.showZone0StepIndicator = true;
        ui.leftPadding = 20f;
        RagTrainingHudVisibility.Initialize(defaultVisible: false);
        ui.RebuildGaugesNow();
        ui.ApplyUserHudVisibility();
        if (RagTrainingHudSwitchController.IsAllowedScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name))
            RagTrainingHudSwitchController.EnsureInScene();
    }

    void AttachTemporalPresenterBySceneScan(string baseId, string displayName, TemporalStationDisplayMode mode)
    {
        GameObject[] all = FindObjectsOfType<GameObject>();
        for (int i = 0; i < all.Length; i++)
        {
            GameObject go = all[i];
            if (go == null) continue;

            string name = go.name ?? "";
            bool idMatch = name.IndexOf(baseId, StringComparison.OrdinalIgnoreCase) >= 0;

            DeclarativeObjectMetadata metadata = go.GetComponent<DeclarativeObjectMetadata>();
            bool nameMatch = metadata != null &&
                             string.Equals(metadata.displayName, displayName, StringComparison.OrdinalIgnoreCase);

            if (!idMatch && !nameMatch) continue;

            TemporalStationPresenter.GetOrCreateForStation(go, mode, 0);
            Debug.Log($"[SceneGenerator] Attached fallback Temporal UI to {go.name} ({displayName}).");
            return;
        }

        Debug.LogWarning($"[SceneGenerator] Temporal UI target not found: {displayName} ({baseId}).");
    }
    
    // Public methods for external control
    public void RegenerateScene()
    {
        Debug.Log("Manual scene regeneration requested");
        GenerateScene();
    }
    
    // Force generation even if there are errors
    public void ForceGenerateScene()
    {
        Debug.Log("=== FORCE GENERATING SCENE ===");
        
        // Try to get scene data
        SceneUILoader sceneLoader = GetComponent<SceneUILoader>();
        if (sceneLoader == null)
        {
            Debug.LogError("SceneUILoader not found! Creating basic scene anyway...");
            CreateBasicScene();
            return;
        }
        
        sceneData = sceneLoader.GetSceneData();
        if (sceneData == null)
        {
            Debug.LogError("No scene data available! Creating basic scene anyway...");
            CreateBasicScene();
            return;
        }
        
        GenerateScene();
    }
    
    // Create a basic scene for testing
    public void CreateBasicScene()
    {
        Debug.Log("Creating basic test scene...");
        
        CreateSceneStructure();
        
        // Create test tools with different names
        GameObject testTool1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        testTool1.transform.SetParent(toolsParent);
        testTool1.transform.position = new Vector3(8f, 0.5f, -2f);
        testTool1.name = "JSON_Repair_Tool_001";
        testTool1.GetComponent<Renderer>().material.color = Color.red;
        AddObjectHighlight(testTool1, "TEST_TOOL");
        generatedTools["test_tool_1"] = testTool1;
        
        GameObject testTool2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        testTool2.transform.SetParent(toolsParent);
        testTool2.transform.position = new Vector3(8f, 0.5f, 2f);
        testTool2.name = "JSON_Toolbox_002";
        testTool2.GetComponent<Renderer>().material.color = Color.yellow;
        AddObjectHighlight(testTool2, "TEST_TOOL");
        generatedTools["test_tool_2"] = testTool2;
        
        // Create test agents with different names
        GameObject testAgent1 = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        testAgent1.transform.SetParent(agentsParent);
        testAgent1.transform.position = new Vector3(10f, 1f, 0f);
        testAgent1.name = "JSON_Technician_Agent_A";
        testAgent1.GetComponent<Renderer>().material.color = Color.green;
        AddObjectHighlight(testAgent1, "TEST_AGENT");
        generatedAgents["test_agent_1"] = testAgent1;
        
        GameObject testAgent2 = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        testAgent2.transform.SetParent(agentsParent);
        testAgent2.transform.position = new Vector3(12f, 1f, 3f);
        testAgent2.name = "JSON_Technician_Agent_B";
        testAgent2.GetComponent<Renderer>().material.color = Color.blue;
        AddObjectHighlight(testAgent2, "TEST_AGENT");
        generatedAgents["test_agent_2"] = testAgent2;
        
        Debug.Log("Basic test scene created successfully!");
        Debug.Log($"Created {generatedTools.Count} test tools and {generatedAgents.Count} test agents");
        
        // Show where they were created
        ShowGenerationSummary();
    }
    
    public GameObject GetTool(string toolId)
    {
        return generatedTools.ContainsKey(toolId) ? generatedTools[toolId] : null;
    }
    
    public GameObject GetAgent(string agentId)
    {
        return generatedAgents.ContainsKey(agentId) ? generatedAgents[agentId] : null;
    }

    /// <summary>
    /// Returns the world position of a cognitive station or tool for a specific zone.
    /// Looks for "{objectId}_zone{zoneIndex}" first; falls back to the base objectId.
    /// Used by cognitive sequence steps to resolve zone-local navigation targets.
    /// </summary>
    public Vector3 GetTargetPositionById(string objectId, int zoneIndex = -1)
    {
        if (zoneIndex >= 0)
        {
            string zoneKey = $"{objectId}_zone{zoneIndex}";
            if (generatedTools.ContainsKey(zoneKey))
                return generatedTools[zoneKey].transform.position;

            // Also search by GameObject name directly (CreateToolObject uses Tool_{toolId})
            GameObject byName = GameObject.Find($"Tool_{zoneKey}");
            if (byName != null) return byName.transform.position;
            byName = GameObject.Find(zoneKey);
            if (byName != null) return byName.transform.position;
        }

        // Fallback — base name lookup
        if (generatedTools.ContainsKey(objectId))
            return generatedTools[objectId].transform.position;

        GameObject fallback = GameObject.Find($"Tool_{objectId}");
        if (fallback != null) return fallback.transform.position;
        fallback = GameObject.Find(objectId);
        if (fallback != null) return fallback.transform.position;

        string missKey = objectId + "|" + zoneIndex;
        if (_reportedMissingTargetKeys.Add(missKey))
            Debug.LogWarning($"[SceneGenerator] GetTargetPositionById: '{objectId}' not found (zone={zoneIndex}) — further misses for this key are silent.");
        return Vector3.zero;
    }

    /// <summary>Resolve a physical-step target name to a spawned tool id in <see cref="sceneData"/>.</summary>
    public string ResolveObjectIdByName(string objectName, int zoneIndex = -1)
    {
        return PhysicalStepTargetResolver.ResolveObjectIdByTargetName(objectName, zoneIndex);
    }
    
    public void UpdateToolState(string toolId, ToolState newState)
    {
        if (generatedTools.ContainsKey(toolId))
        {
            UpdateToolVisuals(generatedTools[toolId], newState);
        }
    }
    
    // Public methods for the tester
    public int GetGeneratedToolsCount()
    {
        return generatedTools.Count;
    }
    
    public int GetGeneratedAgentsCount()
    {
        return generatedAgents.Count;
    }
}
