using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using Unity.MLAgents;
using System.IO;
using System;

/// <summary>
/// Replica Scene Setup - EXACT COPY of SimpleFourEntitySystem
/// Creates a complete replica of JSONWorkflowScene with same logic
/// </summary>
[DefaultExecutionOrder(-50)]
public class ReplicaSceneSetup : MonoBehaviour
{
    [Header("Replica Scene Setup")]
    public bool setupOnStart = true;
    public string jsonFileName = "basicUI_ml2.json"; // RAG envelope source used by SceneUILoader normalization
    [Tooltip("When true and jsonFileName is an ml2 RAG file, skip legacy 4-zone hardcoded replica spawn.")]
    public bool ragOnlyMode = true;

    [Header("Ronald-Johnson — single motor zone")]
    [Tooltip("When true, SceneGenerator builds only zone 0 at zoneWorldOrigin (no 4-quadrant grid).")]
    public bool singleZoneMode = false;
    [Tooltip("World origin for the single training zone ground plane.")]
    public Vector3 zoneWorldOrigin = Vector3.zero;

    [Header("Ronald-Johnson — multiplayer embed")]
    [Tooltip("When true (ProtoypeSceneMultiplayer): spawn RAG zone 0 only; keep multiplayer camera and UI.")]
    public bool multiplayerEmbedMode = false;
    [Tooltip("Bitmask of zones to spawn (bit 0 = zone 0). 0 = all zones. Multiplayer embed uses 1.")]
    public int spawnZonesMask = 0;
    [Tooltip("When true, SceneGenerator skips BSG floor/walls/4-grid.")]
    public bool skipEnvironmentGeneration = false;
    [Tooltip("Parent RAG content under MultiplayerRagZone0Anchor and remap JSON coords.")]
    public bool useSceneAnchorLayout = false;
    [Tooltip("Legacy world offset when not using scene anchor layout.")]
    public Vector3 ragWorldOrigin = Vector3.zero;

    [Header("ML-Agents — RAG runtime")]
    [Tooltip("When true, after RAG scene generation attach ML-Agents to physical agents (P*) while keeping RagSequenceAgentMover enabled for RAG locomotion; YAML behaviors PhysicalAgentZone0–3. Turn off for scripted RAG-only demos.")]
    public bool enableMlTrainingInRagMode = true;

    [Tooltip("When true (and ML training on), attach CognitiveAgentZone0–3 for observations/rewards on RAG M1–M4 (RagSequenceAgentMover still runs cognitive steps). Replica M_A defers locomotion to ML only when Python is connected. Turn off to restore pre-ML cognitive/physical timing.")]
    public bool enableMlTrainingForCognitiveAgents = true;

    [Tooltip("Bitmask of zones (0–3) where ML bootstrap attaches (bit 0 = zone 0 …). Default 15 = all zones. Use 0 = train all zones (same as 15).")]
    public int trainZonesMask = 15;

    [Tooltip("When true, TemporalCognitionRuntime applies a one-shot ML penalty when a step exceeds ragDelayPenaltyThreshold × expected duration.")]
    public bool useRagSparseStepTimeoutPenalty = false;

    [Header("RAG ML — additive extrinsic rewards")]
    [Tooltip("Max ML bonus when a step completes faster than expected (efficiency).")]
    [Min(0f)]
    public float ragEfficiencyBonusMax = 0.5f;
    [Tooltip("Multiply expected step duration; penalty fires once when elapsed exceeds this.")]
    [Min(1f)]
    public float ragDelayPenaltyThreshold = 1.5f;
    [Tooltip("ML extrinsic penalty applied on temporal buffer timeout (negative value).")]
    public float ragDelayPenaltyMagnitude = -1f;

    [Header("Per-zone RAG JSON (optional)")]
    [Tooltip("Four filenames under Assets/JsonFile or StreamingAssets (zone 0–3). Empty = use jsonFileName only.")]
    public string[] ragJsonByZone = new string[0];
    [Tooltip("When ML-Agents Python connects, it can set Unity Time.timeScale via engine_settings (e.g. 20 for fast training). When true, MlAgentsRealtimeTimeScaleEnforcer clamps after Academy applies settings (very late script order).")]
    public bool clampUnityTimeScaleForMlAgents = true;
    [Tooltip("Upper limit for Time.timeScale when clamp is on. Default 20 matches common mlagents engine_settings; use 1 for strict real-time. If this is lower than Python's time_scale, simulation (including cognitive dwell) runs slower in real wall-clock time.")]
    [Min(0.01f)]
    public float maxUnityTimeScaleForMlAgents = 20f;

    [Header("RAG ML — step cadence")]
    [Tooltip("DecisionRequester.DecisionPeriod on each physical P-agent after ML attach. Higher = fewer Python round-trips per real second = smoother mental/cognitive RagSequenceAgentMover updates (same process as Academy). Lower = denser RL samples. Typical 8–20.")]
    [Min(1)]
    public int ragPhysicalMlDecisionPeriod = 12;

    [Tooltip("DecisionRequester period on M_A cognitive brains after ML attach.")]
    [Min(1)]
    public int ragCognitiveMlDecisionPeriod = 8;

    [Header("Startup / loader")]
    [Tooltip("Loader stays visible at least this long (unscaled time) so it never flashes. Actual work (JSON + build) always runs to completion and may take longer.")]
    public float minimumLoaderDisplaySeconds = 2f;

    // ─── 4-Zone Configuration ────────────────────────────────────────────────
    private struct ZoneConfig
    {
        public string  agentId;
        public int     zoneIndex;
        public Vector3 worldOffset;
        public Color   groundColor;
    }

    // Each zone is 40×40 (ground scale 4.0).
    // Zones are placed so their boundaries TOUCH — centres at ±20 on X and Z.
    // The whole grid is shifted +20 in Z so the empty lower-screen space is used.
    // Zone 0 = Technician_01  (SW, -X/-Z)  |  Zone 1 = Technician_02  (SE, +X/-Z)
    // Zone 2 = Supervisor_01  (NW, -X/+Z)  |  Zone 3 = Supervisor_02  (NE, +X/+Z)
    private const float ZoneHalf   = 20f;   // half-size of each zone (40/2)
    private const float ZoneInset  = 18f;   // play-area half-size (inner, excluding wall thickness)
    private const float WallH      = 4f;    // wall height
    private const float WallT      = 1f;    // wall thickness
    private const float WallLen    = 41f;   // wall length (zone side + a bit to fill corners)
    private const float GridShiftZ = 18f;   // shift entire grid forward so it fills more screen

    private static readonly ZoneConfig[] ZoneConfigs =
    {
        new ZoneConfig { agentId = "SIMPLE_Technician_01", zoneIndex = 0, worldOffset = new Vector3(-ZoneHalf, 0f, -ZoneHalf + GridShiftZ), groundColor = new Color(0.05f, 0.55f, 0.05f) },
        new ZoneConfig { agentId = "SIMPLE_Technician_02", zoneIndex = 1, worldOffset = new Vector3( ZoneHalf, 0f, -ZoneHalf + GridShiftZ), groundColor = new Color(0.05f, 0.35f, 0.55f) },
        new ZoneConfig { agentId = "SIMPLE_Supervisor_01", zoneIndex = 2, worldOffset = new Vector3(-ZoneHalf, 0f,  ZoneHalf + GridShiftZ), groundColor = new Color(0.45f, 0.15f, 0.05f) },
        new ZoneConfig { agentId = "SIMPLE_Supervisor_02", zoneIndex = 3, worldOffset = new Vector3( ZoneHalf, 0f,  ZoneHalf + GridShiftZ), groundColor = new Color(0.35f, 0.05f, 0.45f) },
    };

    private struct ZoneToolTemplate
    {
        public string      id;
        public string      displayName;
        public Vector3     localPos;   // relative to zone worldOffset
        public Color       color;
        public PrimitiveType ptype;
        public Vector3?    scale;
    }

    private static readonly ZoneToolTemplate[] ToolTemplates =
    {
        // Workbench — near south wall, centre X
        new ZoneToolTemplate { id="workbench_001", displayName="Main Assembly Workbench",   localPos=new Vector3( 0f,  0.8f, -11f), color=new Color(1f,0.50f,0f),        ptype=PrimitiveType.Cube,     scale=new Vector3(3.5f,0.9f,2.0f) },
        // 4 tools spread across the south section, same Z row
        new ZoneToolTemplate { id="tool_001",      displayName="Industrial Motor Unit",     localPos=new Vector3(-9f,  0.8f, -15f), color=new Color(0.86f,0.08f,0.24f),  ptype=PrimitiveType.Cylinder, scale=new Vector3(1.4f,1.6f,1.4f) },
        new ZoneToolTemplate { id="tool_002",      displayName="Secure Toolbox Alpha",      localPos=new Vector3(-3f,  0.8f, -15f), color=new Color(1f,0.84f,0f),        ptype=PrimitiveType.Cube,     scale=new Vector3(1.6f,1.6f,1.6f) },
        new ZoneToolTemplate { id="tool_003",      displayName="Hydraulic Lift Station",    localPos=new Vector3( 3f,  0.8f, -15f), color=new Color(0.12f,0.56f,1f),     ptype=PrimitiveType.Cube,     scale=new Vector3(2.0f,1.8f,2.0f) },
        new ZoneToolTemplate { id="tool_004",      displayName="Safety Inspection Station", localPos=new Vector3( 9f,  0.8f, -15f), color=new Color(0.12f,0.56f,1f),     ptype=PrimitiveType.Cube,     scale=new Vector3(1.6f,1.6f,1.6f) },
    };
    // ─── End Zone Configuration ───────────────────────────────────────────────

    void Start()
    {
        if (setupOnStart)
            StartCoroutine(DelayedStart());
    }

    private IEnumerator DelayedStart()
    {
        float loadT0 = Time.unscaledTime;
        var loadingOverlay = multiplayerEmbedMode
            ? default
            : CreateLoadingOverlay();

        // Let other components run Start() (e.g. SceneUILoader → LoadSceneData from JsonFile).
        yield return null;
        yield return new WaitForEndOfFrame();

        // ── Phase 1: wait for real JSON init (SceneUILoader — e.g. basicUI_ml2.json + sidecar merge) ─────────
        UpdateLoadingOverlay(
            loadingOverlay,
            0.12f,
            "Loading scene",
            $"Reading {jsonFileName}…");

        var sceneLoader = FindObjectOfType<SceneUILoader>();
        const float jsonWaitTimeout = 60f;
        float jsonWaited = 0f;
        if (sceneLoader != null)
        {
            while (sceneLoader.sceneData == null && jsonWaited < jsonWaitTimeout)
            {
                jsonWaited += Time.unscaledDeltaTime;
                UpdateLoadingOverlay(
                    loadingOverlay,
                    0.12f,
                    title: null,
                    subtitle: $"Reading {jsonFileName}…");
                yield return null;
            }

            if (sceneLoader.sceneData == null)
            {
                Debug.LogWarning(
                    $"⚠️ REPLICA: SceneUILoader did not load '{jsonFileName}' within {jsonWaitTimeout:0}s — proceeding anyway.");
            }
            else
            {
                int steps = sceneLoader.sceneData.sequence != null ? sceneLoader.sceneData.sequence.Length : 0;
                string sid = string.IsNullOrEmpty(sceneLoader.sceneData.scene_id)
                    ? "(no scene_id)"
                    : sceneLoader.sceneData.scene_id;
                UpdateLoadingOverlay(
                    loadingOverlay,
                    0.28f,
                    "Loading scene",
                    $"{jsonFileName} — {sid}, {steps} workflow steps");
                Debug.Log($"✅ REPLICA: SceneUILoader ready '{jsonFileName}' (scene_id={sid}, steps={steps})");
            }
        }
        else
        {
            Debug.LogWarning("⚠️ REPLICA: No SceneUILoader found — JSON-driven UI data may be missing.");
        }

        bool ragOnly = ragOnlyMode && (
            jsonFileName.IndexOf("ml2", StringComparison.OrdinalIgnoreCase) >= 0 ||
            jsonFileName.IndexOf("motor_single_zone", StringComparison.OrdinalIgnoreCase) >= 0);
        if (ragOnly)
        {
            BsgIntegrationSettings.ApplyFromSetup(this);
            UpdateLoadingOverlay(
                loadingOverlay,
                0.75f,
                "Building scene",
                "RAG-only mode: skipping legacy replica zones/tools/extra agents");
            Debug.Log("ℹ️ REPLICA: RAG-only mode active; legacy 4-zone CreateReplicaSystem is skipped.");

            // Keep component wiring but do not spawn legacy hardcoded world objects.
            EnsureJSONFileName();
            EnsureCoreSystemsRagOnly();
            EnsureRagOnlySceneGeneration();

            UpdateLoadingOverlay(
                loadingOverlay,
                0.95f,
                "Starting",
                "Ready — RAG-generated scene only");

            float elapsedRag = Time.unscaledTime - loadT0;
            float minRemainRag = multiplayerEmbedMode ? 0f : minimumLoaderDisplaySeconds - elapsedRag;
            if (minRemainRag > 0f)
                yield return new WaitForSecondsRealtime(minRemainRag);

            DestroyLoadingOverlay(loadingOverlay);
            if (!multiplayerEmbedMode)
                EnsureRagPhysicalSkillGauge();
            else
            {
                RagTrainingHudSuppressor.Apply();
                EnsureRagPhysicalSkillGauge();
            }
            Debug.Log(multiplayerEmbedMode
                ? "✅ REPLICA SCENE SETUP: Complete (RAG embedded in multiplayer)."
                : "✅ REPLICA SCENE SETUP: Complete (RAG-only mode).");
            yield break;
        }

        // ── Phase 2: build replica geometry & entities (sync) ───────────────────────────
        UpdateLoadingOverlay(
            loadingOverlay,
            0.35f,
            "Building scene",
            "Creating zones, agents, tools, and cognitive layout…");
        Debug.Log("🔄 REPLICA SCENE SETUP: Starting...");
        ClearAllObjects();
        CreateReplicaSystem(startPostBuildCoroutines: false);
        UpdateLoadingOverlay(
            loadingOverlay,
            0.52f,
            "Building scene",
            "World layout created — finalising systems…");

        // ── Phase 3: ML / status board / proximity (async, progress from real completion) ─
        yield return StartCoroutine(RunPostBuildReplicaCoroutinesParallel(loadingOverlay));

        // ── Phase 4: minimum visible time (no countdown shown) ─────────────────────────
        UpdateLoadingOverlay(
            loadingOverlay,
            0.95f,
            "Starting",
            "Ready — handoff to simulation");
        float elapsedLoad = Time.unscaledTime - loadT0;
        float minRemain = minimumLoaderDisplaySeconds - elapsedLoad;
        if (minRemain > 0f)
            yield return new WaitForSecondsRealtime(minRemain);

        DestroyLoadingOverlay(loadingOverlay);
        Debug.Log("✅ REPLICA SCENE SETUP: Complete!");
    }

    /// <summary>
    /// Runs the same post-build coroutines as <see cref="CreateReplicaSystem(bool)"/> would start in parallel,
    /// but as a single yieldable sequence (used while the loading overlay is visible).
    /// </summary>
    IEnumerator RunPostBuildReplicaCoroutinesParallel(
        (GameObject canvas, UnityEngine.UI.Text title, UnityEngine.UI.Text subtitle, UnityEngine.UI.Image bar) overlay)
    {
        int pending = 3;
        int finished = 0;

        void Done()
        {
            pending--;
            finished++;
            float p = 0.52f + (finished / 3f) * 0.38f; // 0.52 → 0.90 as each real task completes
            UpdateLoadingOverlay(
                overlay,
                p,
                "Finishing setup",
                $"Systems ready ({finished}/3): ML-Agents, status UI, proximity");
        }

        IEnumerator Wrap(IEnumerator inner, System.Action onDone)
        {
            yield return StartCoroutine(inner);
            onDone?.Invoke();
        }

        UpdateLoadingOverlay(
            overlay,
            0.55f,
            "Finishing setup",
            "Connecting ML-Agents, status board, and proximity overlays…");

        StartCoroutine(Wrap(SetupReplicaComponentsDelayed(), Done));
        StartCoroutine(Wrap(EnsureStatusBoardVisible(), Done));
        StartCoroutine(Wrap(ShowProximityZonesDelayed(), Done));

        while (pending > 0)
            yield return null;
    }

    private AgentProfile ResolveRuntimeAgentProfile(string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId))
            return null;

        SceneUILoader loader = FindObjectOfType<SceneUILoader>();
        if (loader != null && loader.sceneData != null && loader.sceneData.agentProfiles != null
            && loader.sceneData.agentProfiles.TryGetValue(agentId, out AgentProfile loaderProfile) && loaderProfile != null)
        {
            return loaderProfile;
        }

        SkillBasedActionSystem skillSystem = FindObjectOfType<SkillBasedActionSystem>();
        if (skillSystem != null && skillSystem.IsDataReady())
        {
            AgentProfile skillProfile = skillSystem.GetAgentProfile(agentId);
            if (skillProfile != null)
                return skillProfile;
        }

        return null;
    }

    private void ApplyRuntimeAgentDefaults(BSGMLAgent mlAgent, string agentId, float fallbackSkillLevel, float fallbackDesireLevel)
    {
        if (mlAgent == null)
            return;

        AgentProfile runtimeProfile = ResolveRuntimeAgentProfile(agentId);
        mlAgent.skillLevel = runtimeProfile != null ? runtimeProfile.skillLevel : fallbackSkillLevel;
        mlAgent.desireLevel = runtimeProfile != null ? runtimeProfile.desireLevel : fallbackDesireLevel;
        mlAgent.moveSpeed = 2f;
        mlAgent.rotationSpeed = 90f;
        mlAgent.maxDistance = 15f;
    }

    // ── Loading Overlay helpers ───────────────────────────────────────────────

    private (GameObject canvas, UnityEngine.UI.Text title, UnityEngine.UI.Text subtitle, UnityEngine.UI.Image bar)
        CreateLoadingOverlay()
    {
        // Root canvas
        var canvasGO = new GameObject("_LoadingOverlay");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;
        canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
        canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        // Dark background panel
        var bgGO = new GameObject("Background");
        bgGO.transform.SetParent(canvasGO.transform, false);
        var bgImg = bgGO.AddComponent<UnityEngine.UI.Image>();
        bgImg.color = new Color(0.05f, 0.05f, 0.1f, 0.92f);
        var bgRect = bgGO.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        // Title
        var titleGO = new GameObject("Title");
        titleGO.transform.SetParent(canvasGO.transform, false);
        var title = titleGO.AddComponent<UnityEngine.UI.Text>();
        title.text = "Loading scene";
        title.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        title.fontSize = 28;
        title.fontStyle = FontStyle.Bold;
        title.color = Color.white;
        title.alignment = TextAnchor.MiddleCenter;
        var titleRect = titleGO.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.2f, 0.52f);
        titleRect.anchorMax = new Vector2(0.8f, 0.62f);
        titleRect.offsetMin = Vector2.zero;
        titleRect.offsetMax = Vector2.zero;

        // Progress bar background
        var barBgGO = new GameObject("BarBg");
        barBgGO.transform.SetParent(canvasGO.transform, false);
        var barBgImg = barBgGO.AddComponent<UnityEngine.UI.Image>();
        barBgImg.color = new Color(0.2f, 0.2f, 0.3f, 1f);
        var barBgRect = barBgGO.GetComponent<RectTransform>();
        barBgRect.anchorMin = new Vector2(0.2f, 0.46f);
        barBgRect.anchorMax = new Vector2(0.8f, 0.50f);
        barBgRect.offsetMin = Vector2.zero;
        barBgRect.offsetMax = Vector2.zero;

        // Progress bar fill (width driven by UpdateLoadingOverlay — phase-based, not a fake countdown)
        var barFillGO = new GameObject("BarFill");
        barFillGO.transform.SetParent(canvasGO.transform, false);
        var barFillImg = barFillGO.AddComponent<UnityEngine.UI.Image>();
        barFillImg.color = new Color(0.2f, 0.6f, 1f, 1f);
        var barFillRect = barFillGO.GetComponent<RectTransform>();
        barFillRect.anchorMin = new Vector2(0.2f, 0.46f);
        barFillRect.anchorMax = new Vector2(0.2f, 0.50f);
        barFillRect.offsetMin = Vector2.zero;
        barFillRect.offsetMax = Vector2.zero;

        // Subtitle (status line — no fixed second counts)
        var subGO = new GameObject("Subtitle");
        subGO.transform.SetParent(canvasGO.transform, false);
        var subtitle = subGO.AddComponent<UnityEngine.UI.Text>();
        subtitle.text = $"Initialising from {jsonFileName}…";
        subtitle.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        subtitle.fontSize = 18;
        subtitle.color = new Color(0.7f, 0.8f, 1f, 1f);
        subtitle.alignment = TextAnchor.MiddleCenter;
        var subRect = subGO.GetComponent<RectTransform>();
        subRect.anchorMin = new Vector2(0.15f, 0.38f);
        subRect.anchorMax = new Vector2(0.85f, 0.46f);
        subRect.offsetMin = Vector2.zero;
        subRect.offsetMax = Vector2.zero;

        return (canvasGO, title, subtitle, barFillImg);
    }

    private void UpdateLoadingOverlay(
        (GameObject canvas, UnityEngine.UI.Text title, UnityEngine.UI.Text subtitle, UnityEngine.UI.Image bar) overlay,
        float progress,
        string title = null,
        string subtitle = null)
    {
        if (overlay.title != null && title != null)
            overlay.title.text = title;
        if (overlay.subtitle != null && subtitle != null)
            overlay.subtitle.text = subtitle;

        if (overlay.bar != null)
        {
            var rect = overlay.bar.GetComponent<RectTransform>();
            rect.anchorMax = new Vector2(0.2f + 0.6f * Mathf.Clamp01(progress), rect.anchorMax.y);
        }
    }

    private void DestroyLoadingOverlay(
        (GameObject canvas, UnityEngine.UI.Text title, UnityEngine.UI.Text subtitle, UnityEngine.UI.Image bar) overlay)
    {
        if (overlay.canvas != null)
            Destroy(overlay.canvas);
    }
    
    void ClearAllObjects()
    {
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            if (obj != this.gameObject && 
                !obj.name.Contains("Camera") &&
                !obj.name.Contains("Directional Light"))
            {
                bool shouldDestroy =
                    obj.name.Contains("REPLICA_") ||
                    obj.name.Contains("SIMPLE_") ||
                    obj.name.StartsWith("M_Agent_Zone") ||
                    obj.name.StartsWith("M_M_A_Zone") ||
                    obj.name.StartsWith("M_M_B_Zone") ||
                    obj.name.StartsWith("M_M_C_Zone") ||
                    obj.name.StartsWith("M_M_merge_Zone") ||
                    obj.name.StartsWith("MentalAgentSpawner_Zone") ||
                    obj.name.StartsWith("ZoneDeclarativeMemory") ||
                    obj.name.Contains("workbench_") ||
                    obj.name.Contains("tool_") ||
                    obj.name.Contains("cognitive_") ||
                    obj.name.StartsWith("Ground_Zone") ||
                    obj.name.StartsWith("Wall_Zone") ||
                    obj.name.StartsWith("Wall_Divider");

                if (shouldDestroy)
                {
                    Debug.Log($"🗑️ Clearing: {obj.name}");
                    DestroyImmediate(obj);
                }
            }
        }
    }
    
    /// <param name="startPostBuildCoroutines">
    /// When true (default), starts ML wiring, status board, and proximity UI on the next frames.
    /// When false, caller must yield <see cref="RunPostBuildReplicaCoroutinesParallel"/> (e.g. from <see cref="DelayedStart"/> while the loader is up).
    /// </param>
    void CreateReplicaSystem(bool startPostBuildCoroutines = true)
    {
        Debug.Log("🏗️ Creating 4-zone cognitive system...");

        EnsureCoreSystems();

        // Phase 2: build each quadrant zone independently
        foreach (var zone in ZoneConfigs)
        {
            CreateZoneGround(zone);
            CreateZoneWalls(zone);
        }
        // Shared dividers at world origin (the cross separating the 4 zones)
        CreateSharedDividerWalls();

        // Cognitive stations must exist before agents start navigating to them
        CreateAllZoneCognitiveStations();

        // One P-agent per zone (the existing technician/supervisor)
        CreateReplicaEntities();

        // 3 M-agents per zone for parallel cognitive processing
        CreateAllMentalAgents();

        // Zone-scoped tools
        CreateAllZoneTools();

        EnsurePersonaCognitiveBootstrap();

        SetupReplicaCamera();

        if (startPostBuildCoroutines)
        {
            StartCoroutine(SetupReplicaComponentsDelayed());
            StartCoroutine(EnsureStatusBoardVisible());
            StartCoroutine(ShowProximityZonesDelayed());
        }

        Debug.Log($"✅ 4-zone system created — {ZoneConfigs.Length} zones active.");
    }

    // ─── Phase 2: Zone Ground & Walls ────────────────────────────────────────

    void CreateZoneGround(ZoneConfig zone)
    {
        GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.name = $"Ground_Zone{zone.zoneIndex}";
        plane.transform.position = zone.worldOffset;
        // Scale 4.0 → 40×40 units matching ZoneHalf*2
        plane.transform.localScale = new Vector3(4.0f, 1f, 4.0f);

        Renderer r = plane.GetComponent<Renderer>();
        if (r != null) ApplyDirectColor(r, zone.groundColor);

        Debug.Log($"✅ Zone{zone.zoneIndex} ground (40×40) at {zone.worldOffset}");
    }

    void CreateZoneWalls(ZoneConfig zone)
    {
        // Per-zone outer walls only — shared/inner edges are handled by CreateSharedDividerWalls()
        Color wallColor = new Color(0.55f, 0.55f, 0.55f);
        Vector3 o = zone.worldOffset;
        int     i = zone.zoneIndex;

        // Outer edges of THIS zone — only the edges that face the outside world
        // Zone 0 (SW): South wall + West wall
        // Zone 1 (SE): South wall + East wall
        // Zone 2 (NW): North wall + West wall
        // Zone 3 (NE): North wall + East wall
        float outerZ_south = o.z - ZoneHalf;  // southernmost face
        float outerZ_north = o.z + ZoneHalf;  // northernmost face
        float outerX_west  = o.x - ZoneHalf;  // westernmost face
        float outerX_east  = o.x + ZoneHalf;  // easternmost face

        bool isSouth = (i == 0 || i == 1);
        bool isNorth = (i == 2 || i == 3);
        bool isWest  = (i == 0 || i == 2);
        bool isEast  = (i == 1 || i == 3);

        if (isSouth)
            CreateReplicaWall($"Wall_Zone{i}_S", new Vector3(o.x, WallH * 0.5f, outerZ_south), new Vector3(WallLen, WallH, WallT), wallColor);
        if (isNorth)
            CreateReplicaWall($"Wall_Zone{i}_N", new Vector3(o.x, WallH * 0.5f, outerZ_north), new Vector3(WallLen, WallH, WallT), wallColor);
        if (isWest)
            CreateReplicaWall($"Wall_Zone{i}_W", new Vector3(outerX_west,  WallH * 0.5f, o.z), new Vector3(WallT, WallH, WallLen), wallColor);
        if (isEast)
            CreateReplicaWall($"Wall_Zone{i}_E", new Vector3(outerX_east,  WallH * 0.5f, o.z), new Vector3(WallT, WallH, WallLen), wallColor);
    }

    // Creates the 2 shared divider walls (the cross at the grid centre separating all 4 zones)
    void CreateSharedDividerWalls()
    {
        Color divColor = new Color(0.4f, 0.4f, 0.4f);
        float totalLen = ZoneHalf * 4f + WallT; // span both rows/columns
        float centreZ  = GridShiftZ;            // grid centre Z (shifted)
        // Horizontal divider (separates N/S pairs — runs along X axis)
        CreateReplicaWall("Wall_Divider_H", new Vector3(0f, WallH * 0.5f, centreZ), new Vector3(totalLen, WallH, WallT), divColor);
        // Vertical divider (separates E/W pairs — runs along Z axis)
        CreateReplicaWall("Wall_Divider_V", new Vector3(0f, WallH * 0.5f, centreZ), new Vector3(WallT, WallH, totalLen), divColor);
    }

    // ─── Phase 2: Zone Cognitive Stations ────────────────────────────────────

    void CreateAllZoneCognitiveStations()
    {
        List<ToolState> stations = LoadCognitiveStations();
        if (stations.Count == 0)
        {
            Debug.LogWarning("⚠️ No cognitive stations found in JSON.");
            return;
        }

        foreach (var zone in ZoneConfigs)
            CreateZoneCognitiveStations(zone, stations);

        EnsureCognitiveRuntimeSystems();
        Debug.Log($"✅ Spawned {stations.Count * ZoneConfigs.Length} cognitive stations across {ZoneConfigs.Length} zones.");
    }

    void CreateZoneCognitiveStations(ZoneConfig zone, List<ToolState> stations)
    {
        foreach (ToolState station in stations)
        {
            string zoneId = $"{station.objectId}_zone{zone.zoneIndex}";

            PrimitiveType primitive = ResolveCognitivePrimitiveType(station);
            GameObject go = GameObject.CreatePrimitive(primitive);
            go.name = zoneId;

            Vector3 localPos = station.position != null
                ? new Vector3(station.position.x, station.position.y <= 0f ? 0.5f : station.position.y, station.position.z)
                : Vector3.zero;
            go.transform.position = zone.worldOffset + localPos;

            ApplyCognitiveScaleProfile(go, station);

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null && !string.IsNullOrWhiteSpace(station.color) &&
                ColorUtility.TryParseHtmlString(station.color, out Color parsed))
            {
                renderer.material.color = parsed;
            }

            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity  = false;
            AttachDeclarativeMetadata(go, zoneId, station.name, station.type, station.initialState, station.declarativeMetadata);

            CognitiveStationInteractable interactable = go.AddComponent<CognitiveStationInteractable>();
            interactable.stationId = zoneId;
            interactable.stationType = !string.IsNullOrWhiteSpace(station.moduleType) ? station.moduleType : station.bufferType;
            if (string.IsNullOrWhiteSpace(interactable.stationType)) interactable.stationType = station.type;
            interactable.stationAction = !string.IsNullOrWhiteSpace(station.stationAction) ? station.stationAction : "learn";
            interactable.interactionRadius = 2.5f;

            CognitiveStationVisualStyler styler = go.GetComponent<CognitiveStationVisualStyler>()
                ?? go.AddComponent<CognitiveStationVisualStyler>();
            styler.stationId    = zoneId;
            styler.displayName  = (string.IsNullOrWhiteSpace(station.name) ? station.objectId : station.name)
                                  + $" [Z{zone.zoneIndex}]";
            styler.stationType  = interactable.stationType;
            styler.stationAction = interactable.stationAction;
            styler.ApplyStyle();

            ProximityZoneVisualizer viz = go.GetComponent<ProximityZoneVisualizer>()
                ?? go.AddComponent<ProximityZoneVisualizer>();
            viz.radius    = 2.5f;
            viz.zoneColor = new Color(0.65f, 0.4f, 0.9f, 0.25f);

            if (!string.IsNullOrEmpty(station.objectId)
                && string.Equals(station.objectId, "cognitive_008", StringComparison.OrdinalIgnoreCase))
            {
                GoalBufferStationPresenter gbp = go.GetComponent<GoalBufferStationPresenter>()
                    ?? go.AddComponent<GoalBufferStationPresenter>();
                gbp.zoneIndex = zone.zoneIndex;
            }
        }
    }

    // ─── Phase 2: Zone Tools ─────────────────────────────────────────────────

    void CreateAllZoneTools()
    {
        foreach (var zone in ZoneConfigs)
            foreach (var t in ToolTemplates)
                CreateZoneTool(zone, t);

        Debug.Log($"✅ Spawned {ToolTemplates.Length * ZoneConfigs.Length} tools/workbenches across {ZoneConfigs.Length} zones.");
    }

    void CreateZoneTool(ZoneConfig zone, ZoneToolTemplate tool)
    {
        string  zoneId   = $"{tool.id}_zone{zone.zoneIndex}";
        Vector3 worldPos = zone.worldOffset + tool.localPos;

        if (tool.id == "workbench_001")
            CreateReplicaWorkbench(zoneId, tool.displayName, worldPos, tool.color, tool.scale ?? new Vector3(2f, 0.6f, 1.2f));
        else
            CreateReplicaTool(zoneId, tool.displayName, worldPos, tool.color, tool.ptype, tool.scale);
    }

    // ─── Legacy single-zone methods kept as no-ops to avoid call-site errors ──

    void ApplyCognitiveScaleProfile(GameObject stationObject, ToolState station)
    {
        string stationType = (station?.properties?.stationType ?? station?.type ?? string.Empty).ToLowerInvariant();
        if (stationType.Contains("module"))
        {
            stationObject.transform.localScale = new Vector3(2.0f, 3.0f, 2.0f);
            return;
        }

        if (stationType.Contains("buffer"))
        {
            stationObject.transform.localScale = new Vector3(2.0f, 2.0f, 2.0f);
            return;
        }

        if (stationType.Contains("hub"))
        {
            stationObject.transform.localScale = new Vector3(2.2f, 1.8f, 2.2f);
        }
    }

    PrimitiveType ResolveCognitivePrimitiveType(ToolState station)
    {
        string stationType = (station?.properties?.stationType ?? station?.type ?? string.Empty).ToLowerInvariant();
        if (stationType.Contains("module")) return PrimitiveType.Capsule;
        if (stationType.Contains("buffer")) return PrimitiveType.Sphere;
        if (stationType.Contains("hub")) return PrimitiveType.Cylinder;

        string shape = station?.shape?.ToLowerInvariant() ?? "cube";
        if (shape.Contains("sphere")) return PrimitiveType.Sphere;
        if (shape.Contains("cylinder")) return PrimitiveType.Cylinder;
        if (shape.Contains("capsule")) return PrimitiveType.Capsule;
        return PrimitiveType.Cube;
    }

    List<ToolState> LoadCognitiveStations()
    {
        var results = new List<ToolState>();
        string path = Path.Combine(Application.dataPath, "JsonFile", jsonFileName);
        if (!File.Exists(path)) return results;

        string json = File.ReadAllText(path);
        Dictionary<string, string> stationActions = ParseCognitiveStationActions(json);
        string section = ExtractNamedObjectSection(json, "initialStates");
        if (string.IsNullOrEmpty(section)) return results;

        foreach (var kvp in ParseTopLevelObjectEntries(section))
        {
            if (!kvp.Key.StartsWith("cognitive_", StringComparison.OrdinalIgnoreCase)) continue;
            ToolState station = JsonUtility.FromJson<ToolState>(kvp.Value);
            if (station == null) continue;
            if (string.IsNullOrWhiteSpace(station.objectId)) station.objectId = kvp.Key;
            if (string.IsNullOrWhiteSpace(station.stationAction) && !string.IsNullOrWhiteSpace(station.properties?.stationAction))
                station.stationAction = station.properties.stationAction;
            if ((string.IsNullOrWhiteSpace(station.stationAction) || station.stationAction == "learn") &&
                stationActions.TryGetValue(kvp.Key, out string mappedAction) &&
                !string.IsNullOrWhiteSpace(mappedAction))
            {
                station.stationAction = mappedAction;
            }
            if (string.IsNullOrWhiteSpace(station.moduleType) && !string.IsNullOrWhiteSpace(station.properties?.bufferOrModuleType))
                station.moduleType = station.properties.bufferOrModuleType;
            results.Add(station);
        }

        return results;
    }

    Dictionary<string, string> ParseCognitiveStationActions(string fullJson)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string cognitiveSection = ExtractNamedObjectSection(fullJson, "cognitiveInteraction");
        if (string.IsNullOrEmpty(cognitiveSection)) return result;

        string wrapped = "{ " + cognitiveSection + " }";
        string actionSection = ExtractNamedObjectSection(wrapped, "stationActions");
        if (string.IsNullOrEmpty(actionSection)) return result;

        int i = 0;
        while (i < actionSection.Length)
        {
            while (i < actionSection.Length && (char.IsWhiteSpace(actionSection[i]) || actionSection[i] == ',')) i++;
            if (i >= actionSection.Length || actionSection[i] != '"') break;

            int keyStart = i + 1;
            int keyEnd = actionSection.IndexOf('"', keyStart);
            if (keyEnd < 0) break;
            string key = actionSection.Substring(keyStart, keyEnd - keyStart);

            int colon = actionSection.IndexOf(':', keyEnd);
            if (colon < 0) break;
            int valueStart = colon + 1;
            while (valueStart < actionSection.Length && char.IsWhiteSpace(actionSection[valueStart])) valueStart++;
            if (valueStart >= actionSection.Length || actionSection[valueStart] != '"')
            {
                i = valueStart + 1;
                continue;
            }

            int valueEnd = actionSection.IndexOf('"', valueStart + 1);
            if (valueEnd < 0) break;
            string value = actionSection.Substring(valueStart + 1, valueEnd - valueStart - 1);
            result[key] = value;
            i = valueEnd + 1;
        }

        return result;
    }

    static string ExtractNamedObjectSection(string json, string sectionName)
    {
        string token = $"\"{sectionName}\"";
        int keyIndex = json.IndexOf(token, StringComparison.Ordinal);
        if (keyIndex < 0) return null;
        int colonIndex = json.IndexOf(":", keyIndex + token.Length, StringComparison.Ordinal);
        if (colonIndex < 0) return null;
        int openIndex = json.IndexOf("{", colonIndex + 1, StringComparison.Ordinal);
        if (openIndex < 0) return null;
        int closeIndex = FindMatchingBrace(json, openIndex);
        if (closeIndex < 0) return null;
        return json.Substring(openIndex + 1, closeIndex - openIndex - 1);
    }

    static Dictionary<string, string> ParseTopLevelObjectEntries(string objectBody)
    {
        var result = new Dictionary<string, string>();
        int i = 0;
        while (i < objectBody.Length)
        {
            while (i < objectBody.Length && (char.IsWhiteSpace(objectBody[i]) || objectBody[i] == ',')) i++;
            if (i >= objectBody.Length || objectBody[i] != '"') break;
            int keyStart = i + 1;
            int keyEnd = objectBody.IndexOf('"', keyStart);
            if (keyEnd < 0) break;
            string key = objectBody.Substring(keyStart, keyEnd - keyStart);
            int colon = objectBody.IndexOf(":", keyEnd, StringComparison.Ordinal);
            if (colon < 0) break;
            int valueStart = colon + 1;
            while (valueStart < objectBody.Length && char.IsWhiteSpace(objectBody[valueStart])) valueStart++;
            if (valueStart >= objectBody.Length || objectBody[valueStart] != '{')
            {
                i = valueStart + 1;
                continue;
            }
            int valueEnd = FindMatchingBrace(objectBody, valueStart);
            if (valueEnd < 0) break;
            result[key] = objectBody.Substring(valueStart, valueEnd - valueStart + 1);
            i = valueEnd + 1;
        }
        return result;
    }

    static int FindMatchingBrace(string text, int openIndex)
    {
        int depth = 0;
        bool inString = false;
        for (int i = openIndex; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"' && (i == 0 || text[i - 1] != '\\')) inString = !inString;
            if (inString) continue;
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    void EnsureCognitiveRuntimeSystems()
    {
        // Phase 1 scope: keep only physical interaction + light cognitive memory transfer.
        // Contract validation and reward integration are deferred to later phases.
        CognitiveTransferContractEngine contractEngine = FindObjectOfType<CognitiveTransferContractEngine>();
        if (contractEngine != null) Destroy(contractEngine.gameObject);

        CognitiveRewardIntegrator rewardIntegrator = FindObjectOfType<CognitiveRewardIntegrator>();
        if (rewardIntegrator == null)
        {
            var go = new GameObject("CognitiveRewardIntegrator");
            go.AddComponent<CognitiveRewardIntegrator>();
        }

        CognitiveInteractionHUD existingHud = FindObjectOfType<CognitiveInteractionHUD>();
        if (existingHud != null)
        {
            Destroy(existingHud.gameObject);
        }
    }
    
    IEnumerator EnsureStatusBoardVisible()
    {
        // Wait for SimpleStatusBoard to initialize and create its objects
        yield return new WaitForSeconds(1f);
        
        SimpleStatusBoard statusBoard = FindObjectOfType<SimpleStatusBoard>();
        if (statusBoard != null)
        {
            // Update position to top-center - move it upward a bit
            statusBoard.boardPosition = new Vector3(0, 10, 0); // Moved upward from 8 to 10
            statusBoard.fontSize = 1.5f; // Larger font for visibility
            statusBoard.textColor = Color.white;
            
            // Force update by accessing the status board GameObject directly
            GameObject boardObj = GameObject.Find("SimpleStatusBoard");
            if (boardObj != null)
            {
                boardObj.transform.position = new Vector3(0, 8, 0);
                Debug.Log($"✅ REPLICA: Status board positioned at {boardObj.transform.position}");
            }
            
            Debug.Log("✅ REPLICA: Status board position ensured at top-center");
        }
        else
        {
            Debug.LogWarning("⚠️ REPLICA: SimpleStatusBoard not found after delay!");
        }
        
        // Also ensure AgentSkillGaugeUI is working (shows percentage gauges)
        AgentSkillGaugeUI gaugeUI = FindObjectOfType<AgentSkillGaugeUI>();
        if (gaugeUI != null)
        {
            Debug.Log("✅ REPLICA: AgentSkillGaugeUI found - percentage gauges should appear");
        }
        else
        {
            Debug.LogWarning("⚠️ REPLICA: AgentSkillGaugeUI not found - skill gauges may not appear!");
        }
    }
    
    IEnumerator ShowProximityZonesDelayed()
    {
        yield return new WaitForSeconds(1.5f);

        Debug.Log("🟢 Adding proximity visualizers to zone tools and cognitive stations...");

        // Remove stale zone-less visualizer spheres
        foreach (GameObject obj in GameObject.FindGameObjectsWithTag("Untagged"))
        {
            if (obj != null && obj.name.Contains("ProximityZone_"))
                DestroyImmediate(obj);
        }

        // Scan all zones and all object types
        for (int zi = 0; zi < ZoneConfigs.Length; zi++)
        {
            // Tools
            foreach (var t in ToolTemplates)
            {
                string zoneName = $"{t.id}_zone{zi}";
                GameObject obj = GameObject.Find(zoneName);
                if (obj == null) continue;

                ProximityZoneVisualizer viz = obj.GetComponent<ProximityZoneVisualizer>();
                if (viz == null)
                {
                    viz = obj.AddComponent<ProximityZoneVisualizer>();
                    viz.radius    = 3f;
                    viz.zoneColor = new Color(0.6f, 1f, 0.6f, 0.3f);
                }
            }

            // Cognitive stations
            for (int ci = 1; ci <= 17; ci++)
            {
                string cogName = $"cognitive_{ci:000}_zone{zi}";
                GameObject obj = GameObject.Find(cogName);
                if (obj == null) continue;

                ProximityZoneVisualizer viz = obj.GetComponent<ProximityZoneVisualizer>();
                if (viz == null)
                {
                    viz = obj.AddComponent<ProximityZoneVisualizer>();
                    viz.radius    = 2.0f;
                    viz.zoneColor = new Color(0.75f, 0.45f, 1f, 0.3f);
                }
            }
        }

        Debug.Log("✅ REPLICA: Proximity visualizers applied to all zone objects.");
    }
    
    void SetupReplicaCamera()
    {
        Debug.Log("📷 Setting up camera for 4-zone connected grid view...");

        Camera cam = Camera.main;
        if (cam == null)
        {
            GameObject cameraObj = new GameObject("Main Camera");
            cameraObj.tag = "MainCamera";
            cam = cameraObj.AddComponent<Camera>();
        }

        // Zoom in: camera lower + closer + wider FOV so the grid fills most of the screen.
        // Grid centre is at (0, 0, GridShiftZ=18). Total footprint 80×80.
        float gridCentreZ = GridShiftZ;
        cam.transform.position = new Vector3(0f, 52f, gridCentreZ - 42f);
        cam.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
        cam.backgroundColor    = new Color(0.06f, 0.06f, 0.12f, 1f);
        cam.fieldOfView        = 85f;
        cam.farClipPlane       = 400f;
        cam.targetDisplay      = 0; // holistic view → Game "Display 1"

        Debug.Log($"✅ REPLICA: Camera at {cam.transform.position}, looking at grid centre Z={gridCentreZ}, FOV {cam.fieldOfView}");

        SetupAuxiliaryFollowCamerasZone0();
    }

    /// <summary>
    /// Extra Game window targets: P + MA/MB/MC for zone 0. Unity's Display dropdown stays "Display N"; each view shows a corner tag (P, MA, …).
    /// Mental agent GO names match <see cref="MentalAgentSpawner.SpawnAgent"/> (spawned shortly after play — follow script resolves them when they exist).
    /// </summary>
    void SetupAuxiliaryFollowCamerasZone0()
    {
        MultiDisplayGameViewBootstrap.Touch();

        foreach (string n in new[]
                 {
                     "Display2_Zone0TechnicianFollowCam",
                     "CamFollow_P_Zone0", "CamFollow_PPhysical_Zone0",
                     "GameView_Display2_MentalPhysical_Camera", "GameView_Display3_Physical_Camera",
                     "CamFollow_MA_Zone0", "CamFollow_MB_Zone0", "CamFollow_MC_Zone0",
                     "GameView_Display4_MA_Camera", "GameView_Display5_MB_Camera", "GameView_Display6_MC_Camera",
                 })
        {
            GameObject old = GameObject.Find(n);
            if (old != null)
                DestroyImmediate(old);
        }

        const float droneLift = 5.5f;
        const float droneTallM = 2.75f;

        // Display 2 — canonical rig: cognitive context until P moves; if M and P move together, P wins.
        // Display 3 is a duplicate of this rig, like adding another Game view camera the same way.
        CreateZoneFollowCamera("GameView_Display2_MentalPhysical_Camera", "SIMPLE_Technician_01", 1, "P",
            distanceBehind: 7f, height: 2.7f, lookAhead: 17f, lookBias: 1.15f, fov: 62f,
            mentalThenPhysicalFocus: true, zoneIdx: 0,
            useDrone: true, droneTallBoost: droneTallM, droneLift: droneLift);

        GameObject disp2 = GameObject.Find("GameView_Display2_MentalPhysical_Camera");
        if (disp2 != null)
        {
            GameObject disp3 = UnityEngine.Object.Instantiate(disp2);
            disp3.name = "GameView_Display3_Physical_Camera";
            ZoneAgentFollowCamera z3 = disp3.GetComponent<ZoneAgentFollowCamera>();
            if (z3 != null)
            {
                z3.useMentalThenPhysicalFocus = false;
                z3.targetDisplay             = 2;
                z3.agentObjectName           = "SIMPLE_Technician_01";
                z3.cornerLabel               = "Phys";
                z3.useFrontView              = true;
                z3.frontViewDistance         = 11.0f;
                z3.frontViewHeight           = 5.0f;
                z3.frontViewLookAtHeight     = 1.8f;
                z3.fieldOfView               = 58f;
                z3.ApplyRoutingAndUrp();
                z3.SyncCornerLabelUi();
            }
        }

        // MA / MB / MC — Displays 4–6
        CreateZoneFollowCamera("GameView_Display4_MA_Camera", "M_M_A_Zone0", 3, "MA",
            distanceBehind: 11f, height: 4.3f, lookAhead: 20f, lookBias: 2.1f, fov: 58f,
            useDrone: true, droneTallBoost: droneTallM, droneLift: droneLift);
        CreateZoneFollowCamera("GameView_Display5_MB_Camera", "M_M_B_Zone0", 4, "MB",
            distanceBehind: 11f, height: 4.3f, lookAhead: 20f, lookBias: 2.1f, fov: 58f,
            useDrone: true, droneTallBoost: droneTallM, droneLift: droneLift);
        CreateZoneFollowCamera("GameView_Display6_MC_Camera", "M_M_C_Zone0", 5, "MC",
            distanceBehind: 11f, height: 4.3f, lookAhead: 20f, lookBias: 2.1f, fov: 58f,
            useDrone: true, droneTallBoost: droneTallM, droneLift: droneLift);

        Debug.Log("✅ REPLICA: GameView_Display2 (P-priority M/P) + duplicate Display3 (Phys P) + D4–D6 MA–MC. Holistic = Display 1.");
    }

    static void CreateZoneFollowCamera(
        string goName,
        string agentObjectName,
        int targetDisplay,
        string cornerLabel,
        float distanceBehind,
        float height,
        float lookAhead,
        float lookBias,
        float fov,
        bool mentalThenPhysicalFocus = false,
        int zoneIdx = 0,
        bool useDrone = false,
        float droneTallBoost = 0f,
        float droneLift = 5.5f,
        bool useRagM1P1FocusMode = false)
    {
        GameObject go = new GameObject(goName);
        ZoneAgentFollowCamera f = go.AddComponent<ZoneAgentFollowCamera>();
        f.agentObjectName     = agentObjectName;
        f.targetDisplay       = targetDisplay;
        f.cornerLabel         = cornerLabel;
        f.distanceBehind      = distanceBehind;
        f.heightAboveAgentFeet = height;
        f.lookAheadDistance   = lookAhead;
        f.lookHeightBias      = lookBias;
        f.fieldOfView         = fov;
        f.useMentalThenPhysicalFocus = mentalThenPhysicalFocus;
        f.cognitiveZoneIndex  = zoneIdx;
        f.useRagM1P1Focus     = useRagM1P1FocusMode;
        if (mentalThenPhysicalFocus)
        {
            f.mentalHeightAboveFeet  = 4.3f;
            f.mentalDistanceBehind   = 11f;
            f.mentalLookAhead        = 20f;
            f.mentalLookHeightBias   = 2.1f;
        }

        if (useDrone)
        {
            f.useDroneTracking       = true;
            f.droneExtraHeight       = droneLift;
            f.droneTallSubjectBoost  = droneTallBoost;
            f.droneDistanceFactor    = 1.14f;
            f.droneLookAheadFactor   = 0.5f;
            f.droneLookBiasFactor    = 0.88f;
        }
    }

    /// <summary>
    /// Secondary Game view cameras for RAG ml2: Display 2 follows M1 for context but switches to P1 while P1 moves; Display 3 face-on P1.
    /// </summary>
    void SetupRagAuxiliaryFollowCameras()
    {
        MultiDisplayGameViewBootstrap.Touch();

        foreach (string n in new[]
                 {
                     "GameView_Display2_Rag_MentalPhysical_Camera",
                     "GameView_Display3_Rag_Physical_Camera",
                 })
        {
            GameObject old = GameObject.Find(n);
            if (old != null)
                DestroyImmediate(old);
        }

        const float droneLift = 2.65f;
        const float droneTallM = 1.35f;

        CreateZoneFollowCamera(
            "GameView_Display2_Rag_MentalPhysical_Camera",
            "Agent_P1",
            1,
            "P",
            distanceBehind: 3.5f,
            height: 1.55f,
            lookAhead: 7.5f,
            lookBias: 0.92f,
            fov: 48f,
            mentalThenPhysicalFocus: true,
            zoneIdx: 0,
            useDrone: true,
            droneTallBoost: droneTallM,
            droneLift: droneLift,
            useRagM1P1FocusMode: true);

        GameObject disp2 = GameObject.Find("GameView_Display2_Rag_MentalPhysical_Camera");
        if (disp2 != null)
        {
            ZoneAgentFollowCamera f2 = disp2.GetComponent<ZoneAgentFollowCamera>();
            if (f2 != null)
            {
                f2.mentalHeightAboveFeet = 2.45f;
                f2.mentalDistanceBehind = 5.2f;
                f2.mentalLookAhead = 8.5f;
                f2.mentalLookHeightBias = 1.75f;
                f2.droneDistanceFactor = 1.02f;
                f2.droneLookAheadFactor = 0.42f;
                f2.positionSmoothTime = 0.06f;
                f2.fieldOfView = 46f;
                f2.ApplyRoutingAndUrp();
            }

            GameObject disp3 = UnityEngine.Object.Instantiate(disp2);
            disp3.name = "GameView_Display3_Rag_Physical_Camera";
            ZoneAgentFollowCamera z3 = disp3.GetComponent<ZoneAgentFollowCamera>();
            if (z3 != null)
            {
                z3.useMentalThenPhysicalFocus = false;
                z3.useRagM1P1Focus            = false;
                z3.targetDisplay              = 2;
                z3.agentObjectName            = "Agent_P1";
                z3.cornerLabel                = "Phys";
                z3.useFrontView               = false;
                z3.useDroneTracking           = true;
                z3.heightAboveAgentFeet       = 2.4f;
                z3.distanceBehind             = 4.8f;
                z3.lookAheadDistance          = 4.2f;
                z3.lookHeightBias             = 1.45f;
                z3.droneExtraHeight           = 3.8f;
                z3.droneDistanceFactor        = 1.05f;
                z3.droneLookAheadFactor       = 0.55f;
                z3.droneLookBiasFactor        = 0.95f;
                z3.fieldOfView                = 46f;
                z3.positionSmoothTime         = 0.05f;
                z3.rotationSlerp              = 18f;
                z3.ApplyRoutingAndUrp();
                z3.SyncCornerLabelUi();
            }
        }

        Debug.Log("✅ RAG: Display 2 (P1-priority M1/P1 follow) + Display 3 (P1 rear drone follow). Set Game window to Display 2/3 to preview.");
    }
    
    void Awake()
    {
        BsgIntegrationSettings.ApplyFromSetup(this);
        if (!multiplayerEmbedMode)
            MultiDisplayGameViewBootstrap.Touch();
        MlAgentsRealtimeTimeScaleEnforcer.Configure(maxUnityTimeScaleForMlAgents, clampUnityTimeScaleForMlAgents);
        // CRITICAL: Set JSON file name BEFORE any Start() methods run
        // This ensures SceneUILoader uses the correct file
        EnsureJSONFileName();
    }
    
    void EnsureJSONFileName()
    {
        // The SceneUILoader lives on this same GameObject (REPLICA_SceneManager).
        // Its jsonFileName is already set to basicUI_ml2.json in the scene file,
        // so this is just a safety override to guarantee correctness.
        SceneUILoader sceneLoader = GetComponent<SceneUILoader>();
        if (sceneLoader != null)
        {
            sceneLoader.jsonFileName = jsonFileName;
            if (ragJsonByZone != null && ragJsonByZone.Length == 4)
                sceneLoader.ragJsonFileNamesByZone = ragJsonByZone;
            Debug.Log($"✅ REPLICA: Confirmed SceneUILoader.jsonFileName = {jsonFileName} in Awake()");
        }
        
        // Also set ProximityConfigLoader if it exists
        ProximityConfigLoader proximityLoader = FindObjectOfType<ProximityConfigLoader>();
        if (proximityLoader != null)
        {
            string proximityPath = $"Assets/JsonFile/{jsonFileName}";
            proximityLoader.jsonFilePath = proximityPath;
            Debug.Log($"✅ REPLICA: Set ProximityConfigLoader.jsonFilePath to {proximityPath} in Awake()");
        }
    }
    
    void EnsureCoreSystems()
    {
        Debug.Log("🔄 Ensuring core systems exist...");
        
        // Find or create JSONSceneController (like working scene)
        GameObject jsonController = GameObject.Find("JSONSceneController");
        if (jsonController == null)
        {
            jsonController = new GameObject("JSONSceneController");
            Debug.Log("✅ Created JSONSceneController");
        }
        
        // Ensure SceneUILoader exists with replica JSON file
        SceneUILoader sceneLoader = jsonController.GetComponent<SceneUILoader>();
        if (sceneLoader == null)
        {
            sceneLoader = jsonController.AddComponent<SceneUILoader>();
            sceneLoader.jsonFileName = jsonFileName; // Use replica JSON file
            Debug.Log($"✅ Added SceneUILoader with JSON file: {jsonFileName}");
        }
        else
        {
            // CRITICAL: Always update to replica JSON file (even if it was already attached)
            // This ensures the replica scene NEVER uses basicUi.json
            sceneLoader.jsonFileName = jsonFileName;
            Debug.Log($"✅ REPLICA: SceneUILoader found, FORCED JSON file to: {jsonFileName} (not basicUi.json)");
        }

        // Ensure SceneGenerator exists so JSON-driven world (floor/walls/tools/agents) is created.
        SceneGenerator sceneGenerator = jsonController.GetComponent<SceneGenerator>();
        if (sceneGenerator == null)
        {
            sceneGenerator = jsonController.AddComponent<SceneGenerator>();
            sceneGenerator.generateOnStart = true;
            sceneGenerator.clearExistingScene = false;
            Debug.Log("✅ Added SceneGenerator to JSONSceneController");
        }
        
        // Ensure SkillBasedActionSystem exists
        SkillBasedActionSystem skillSystem = FindObjectOfType<SkillBasedActionSystem>();
        if (skillSystem == null)
        {
            GameObject skillObj = new GameObject("SkillBasedActionSystem");
            skillSystem = skillObj.AddComponent<SkillBasedActionSystem>();
            // Note: skillPenaltyAmount removed - no penalties applied
            // skillSystem.skillPenaltyAmount = 10.0f;
            Debug.Log("✅ Created SkillBasedActionSystem");
        }
        
        // CRITICAL: Ensure ProximityConfigLoader uses replica JSON file (not basicUi.json)
        ProximityConfigLoader proximityLoader = FindObjectOfType<ProximityConfigLoader>();
        if (proximityLoader != null)
        {
            string replicaJsonPath = $"Assets/JsonFile/{jsonFileName}";
            proximityLoader.jsonFilePath = replicaJsonPath;
            Debug.Log($"✅ REPLICA: ProximityConfigLoader forced to use: {replicaJsonPath} (not basicUi.json)");
        }
        
        // CRITICAL: Update ProximityDetectionSystem if it has hardcoded path
        // (ProximityDetectionSystem loads config through ProximityConfigLoader, so this should be handled)
        
        // Ensure SimpleStatusBoard exists and is properly configured (TOP-CENTER)
        SimpleStatusBoard statusBoard = FindObjectOfType<SimpleStatusBoard>();
        if (statusBoard == null)
        {
            GameObject statusObj = new GameObject("SimpleStatusBoard");
            statusBoard = statusObj.AddComponent<SimpleStatusBoard>();
            // Configure status board for top-center position - moved upward
            statusBoard.boardPosition = new Vector3(0, 14, 0); // Moved upward from 8 to 10
            statusBoard.fontSize = 1.5f; // Larger font
            statusBoard.updateInterval = 0.3f;
            statusBoard.textColor = Color.white;
            Debug.Log("✅ Created SimpleStatusBoard at top-center position");
        }
        else
        {
            // Ensure position is correct - moved upward
            statusBoard.boardPosition = new Vector3(0, 14, 0); // Moved upward from 8 to 10
            statusBoard.fontSize = 1.5f;
            Debug.Log("✅ SimpleStatusBoard already exists - position updated to top-center");
        }
        
        // CRITICAL: Ensure StepEfficiencyIndicator exists (shows step efficiency metrics)
        StepEfficiencyIndicator efficiencyIndicator = FindObjectOfType<StepEfficiencyIndicator>();
        if (efficiencyIndicator == null)
        {
            GameObject efficiencyObj = new GameObject("StepEfficiencyIndicator");
            efficiencyIndicator = efficiencyObj.AddComponent<StepEfficiencyIndicator>();
            Debug.Log("✅ Created StepEfficiencyIndicator");
        }
        else
        {
            Debug.Log("✅ StepEfficiencyIndicator already exists");
        }
        
        // CRITICAL: Ensure DistanceToTargetMeter exists (shows distance progress to target)
        DistanceToTargetMeter distanceMeter = FindObjectOfType<DistanceToTargetMeter>();
        if (distanceMeter == null)
        {
            GameObject distanceObj = new GameObject("DistanceToTargetMeter");
            distanceMeter = distanceObj.AddComponent<DistanceToTargetMeter>();
            Debug.Log("✅ Created DistanceToTargetMeter");
        }
        else
        {
            Debug.Log("✅ DistanceToTargetMeter already exists");
        }
        
        // CRITICAL: Ensure EpisodeCounterTimer exists (shows episode count, training time, and total steps)
        EpisodeCounterTimer episodeTimer = FindObjectOfType<EpisodeCounterTimer>();
        if (episodeTimer == null)
        {
            GameObject episodeObj = new GameObject("EpisodeCounterTimer");
            episodeTimer = episodeObj.AddComponent<EpisodeCounterTimer>();
            Debug.Log("✅ Created EpisodeCounterTimer");
        }
        else
        {
            Debug.Log("✅ EpisodeCounterTimer already exists");
        }
        
        // CRITICAL: Ensure AgentSkillGaugeUI exists (shows percentage gauges)
        AgentSkillGaugeUI skillGaugeUI = FindObjectOfType<AgentSkillGaugeUI>();
        if (skillGaugeUI == null)
        {
            GameObject gaugeObj = new GameObject("AgentSkillGaugeUI");
            skillGaugeUI = gaugeObj.AddComponent<AgentSkillGaugeUI>();
            skillGaugeUI.gaugeWidth = 270f;
            skillGaugeUI.gaugeHeight = 34f;
            skillGaugeUI.spacing = 8f;
            skillGaugeUI.topPadding = 20f;
            skillGaugeUI.leftPadding = 20f;
            skillGaugeUI.enableConsoleLogs = true; // Enable console logs for debugging
            Debug.Log("✅ Created AgentSkillGaugeUI for skill percentage gauges");
        }
        else
        {
            // Ensure it's enabled and configured
            skillGaugeUI.enabled = true;
            skillGaugeUI.gaugeWidth = 270f;
            skillGaugeUI.gaugeHeight = 34f;
            skillGaugeUI.spacing = 8f;
            skillGaugeUI.topPadding = 20f;
            skillGaugeUI.leftPadding = 20f;
            skillGaugeUI.enableConsoleLogs = true;
            Debug.Log("✅ AgentSkillGaugeUI already exists - ensuring it's enabled");
        }
        
        Debug.Log("✅ Core systems ensured");
    }

    void EnsureCoreSystemsRagOnly()
    {
        Debug.Log("🔄 Ensuring RAG-only core systems...");

        // Use the already-existing SceneUILoader (on REPLICA_SceneManager or wherever it lives).
        // NEVER create a new one — AddComponent triggers Awake() immediately with the wrong default
        // jsonFileName before we can set the correct one, loading basicUi.json instead of ml2.
        SceneUILoader sceneLoader = FindObjectOfType<SceneUILoader>();
        if (sceneLoader == null)
        {
            Debug.LogError("❌ RAG-only: No SceneUILoader found in scene. Cannot build scene.");
            return;
        }

        if (string.IsNullOrEmpty(sceneLoader.EffectivePipelineJson))
        {
            // Loader is present but didn't normalise yet (should not happen — Awake already ran).
            Debug.LogWarning("⚠️ RAG-only: SceneUILoader has no EffectivePipelineJson; forcing reload.");
            sceneLoader.jsonFileName = jsonFileName;
            sceneLoader.ForceReloadSceneData();
        }

        Debug.Log($"✅ RAG-only: Using SceneUILoader on '{sceneLoader.gameObject.name}' " +
                  $"(agents={sceneLoader.sceneData?.agentProfiles?.Count ?? 0})");

        // Add SceneGenerator to the SAME object as SceneUILoader so GetComponent<SceneUILoader>() works.
        SceneGenerator sceneGenerator = sceneLoader.GetComponent<SceneGenerator>();
        if (sceneGenerator == null)
            sceneGenerator = sceneLoader.gameObject.AddComponent<SceneGenerator>();
        sceneGenerator.generateOnStart = false;   // we call GenerateScene() explicitly below
        sceneGenerator.clearExistingScene = true;
        sceneGenerator.spawnCognitiveStationsBeforeAgents = true;

        ProximityConfigLoader proximityLoader = FindObjectOfType<ProximityConfigLoader>();
        if (proximityLoader != null)
            proximityLoader.jsonFilePath = $"Assets/JsonFile/{jsonFileName}";

        ClearLegacyMentalArtifacts();

        // Remove legacy fixed-agent dashboard components in RAG mode.
        DisableLegacyHudComponent<SimpleStatusBoard>();
        DisableLegacyHudComponent<StepEfficiencyIndicator>();
        DisableLegacyHudComponent<DistanceToTargetMeter>();
        DisableLegacyHudComponent<EpisodeCounterTimer>();
        // AgentSkillGaugeUI: keep enabled for per-zone physical skill gauges (RAG P1–P4).

        string[] legacyCanvasNames =
        {
            "StepEfficiencyCanvas",
            "DistanceMeterCanvas",
            "EpisodeCounterCanvas"
        };
        foreach (string n in legacyCanvasNames)
        {
            GameObject go = GameObject.Find(n);
            if (go != null)
                go.SetActive(false);
        }

        Debug.Log("✅ RAG-only core systems ensured");
    }

    void ClearLegacyMentalArtifacts()
    {
        // Remove any leftover mental spawners/agents from previous non-RAG runs.
        MentalAgentSpawner[] spawners = FindObjectsOfType<MentalAgentSpawner>(true);
        for (int i = 0; i < spawners.Length; i++)
        {
            if (spawners[i] != null) Destroy(spawners[i].gameObject);
        }

        ZoneDeclarativeMemory[] memories = FindObjectsOfType<ZoneDeclarativeMemory>(true);
        for (int i = 0; i < memories.Length; i++)
        {
            if (memories[i] != null) Destroy(memories[i].gameObject);
        }

        GameObject[] all = FindObjectsOfType<GameObject>(true);
        for (int i = 0; i < all.Length; i++)
        {
            GameObject go = all[i];
            if (go == null) continue;
            string n = go.name;
            // Remove legacy agents, tools, and any pre-existing generated folders
            if (n.StartsWith("M_M_A_Zone", StringComparison.Ordinal) ||
                n.StartsWith("M_M_B_Zone", StringComparison.Ordinal) ||
                n.StartsWith("M_M_C_Zone", StringComparison.Ordinal) ||
                n.StartsWith("MentalAgentSpawner_Zone", StringComparison.Ordinal) ||
                n.StartsWith("SIMPLE_Technician_", StringComparison.Ordinal) ||
                n.StartsWith("SIMPLE_Supervisor_", StringComparison.Ordinal) ||
                n == "JSON_Generated_Tools" ||
                n == "JSON_Generated_Agents" ||
                n == "JSON_Generated_Environment" ||
                n.StartsWith("Agent_SIMPLE_", StringComparison.Ordinal) ||
                n.StartsWith("Tool_tool_0", StringComparison.Ordinal) ||
                n.StartsWith("Tool_workbench_", StringComparison.Ordinal))
            {
                Destroy(go);
            }
        }
    }

    void DisableLegacyHudComponent<T>() where T : Behaviour
    {
        T c = FindObjectOfType<T>();
        if (c != null)
            c.enabled = false;
    }

    void EnsureRagOnlySceneGeneration()
    {
        SceneUILoader loader = FindObjectOfType<SceneUILoader>();
        if (loader == null)
        {
            Debug.LogError("❌ RAG-only scene generation: no SceneUILoader found.");
            return;
        }

        SceneGenerator generator = loader.GetComponent<SceneGenerator>();
        if (generator == null)
        {
            Debug.LogError("❌ RAG-only scene generation: no SceneGenerator on SceneUILoader's object.");
            return;
        }

        generator.singleZoneMode = singleZoneMode;
        generator.zoneWorldOrigin = zoneWorldOrigin;
        generator.multiplayerEmbedMode = multiplayerEmbedMode;
        generator.spawnZonesMask = spawnZonesMask;
        generator.skipEnvironmentGeneration = skipEnvironmentGeneration;
        generator.useSceneAnchorLayout = useSceneAnchorLayout;
        generator.ragWorldOrigin = ragWorldOrigin;
        loader.spawnZonesMask = spawnZonesMask;
        if (spawnZonesMask != 0)
            loader.FilterSceneDataBySpawnMask();

        if (loader.sceneData == null)
        {
            Debug.LogWarning("⚠️ RAG-only: SceneUILoader has no sceneData — forcing reload.");
            loader.ForceReloadSceneData();
        }

        int agentCount = loader.sceneData?.agentProfiles?.Count ?? 0;
        int stateCount = loader.sceneData?.initialStates?.Count ?? 0;
        Debug.Log($"✅ RAG-only: Generating scene — agents={agentCount}, stations={stateCount}.");
        generator.GenerateScene();

        if (!multiplayerEmbedMode)
        {
            SetupRagAuxiliaryFollowCameras();
            EnsureRagPhysicalSkillGauge();
        }
        else
        {
            RagTrainingHudSuppressor.Apply();
            BsgIntegrationSettings.EnsureDisplay2OverviewCamera?.Invoke();
            EnsureRagPhysicalSkillGauge();
        }

        if (enableMlTrainingInRagMode)
            RagRuntimeMLBootstrap.TryBootstrap(this);
    }

    /// <summary>
    /// Left-panel skill gauges for dynamic physical agents (P*) — required because RAG-only mode skips <see cref="EnsureCoreSystems"/>.
    /// </summary>
    void EnsureRagPhysicalSkillGauge()
    {
        if (BsgIntegrationSettings.UsePhotonPlayerAsPhysicalAgent)
        {
            RagMultiplayerStatsHud.EnsureInScene();
            Debug.Log("✅ RAG multiplayer: step stats HUD ensured for Photon physical agent (toggle via HUD switch).");
            return;
        }

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
        ui.gaugeWidth = 270f;
        ui.gaugeHeight = 34f;
        ui.spacing = 8f;
        ui.topPadding = 20f;
        ui.leftPadding = 20f;

        RagTrainingHudVisibility.Initialize(defaultVisible: false);
        TemporalBufferGaugeUI.GetOrCreate(0);
        DeclarativeMemoryGaugeUI.GetOrCreate(0);
        ui.RebuildGaugesNow();
        ui.ApplyUserHudVisibility();
        if (RagTrainingHudSwitchController.IsAllowedScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name))
            RagTrainingHudSwitchController.EnsureInScene();
        StartCoroutine(RetryRagZone0SkillGaugeWhenProfilesReady(ui));
        Debug.Log("✅ RAG-only: Zone 0 skill gauge + step indicator + temporal buffer ensured (HUD toggle default off).");
    }

    static void DisablePhysicalAgentSkillGaugeUi()
    {
        foreach (AgentSkillGaugeUI gauge in FindObjectsOfType<AgentSkillGaugeUI>(true))
        {
            if (gauge != null)
                gauge.enabled = false;
        }

        GameObject gaugeRoot = GameObject.Find("AgentSkillGaugeUI");
        if (gaugeRoot != null)
            gaugeRoot.SetActive(false);
    }

    System.Collections.IEnumerator RetryRagZone0SkillGaugeWhenProfilesReady(AgentSkillGaugeUI ui)
    {
        if (ui == null) yield break;

        for (int attempt = 0; attempt < 12; attempt++)
        {
            yield return new WaitForSeconds(0.5f);
            if (ui == null) yield break;

            SkillBasedActionSystem skillSystem = FindObjectOfType<SkillBasedActionSystem>();
            if (skillSystem == null) continue;

            var profiles = skillSystem.GetAllAgentProfiles();
            if (profiles == null || profiles.Count == 0) continue;

            bool hasZone0Physical = false;
            foreach (var kvp in profiles)
            {
                AgentProfile ap = kvp.Value;
                if (ap == null || ap.zoneIndex != 0) continue;
                string id = !string.IsNullOrEmpty(ap.agentId) ? ap.agentId : kvp.Key;
                if (id.Length >= 2 && id.StartsWith("P", System.StringComparison.OrdinalIgnoreCase) && char.IsDigit(id[1]))
                {
                    hasZone0Physical = true;
                    break;
                }
            }

            if (!hasZone0Physical) continue;

            ui.showOnlyPhysicalRagAgents = true;
            ui.showOnlyZoneIndex = 0;
            ui.showZone0StepIndicator = true;
            TemporalBufferGaugeUI.GetOrCreate(0);
            DeclarativeMemoryGaugeUI.GetOrCreate(0);
            ui.RebuildGaugesNow();
            ui.ApplyUserHudVisibility();
            yield break;
        }
    }

    void CreateReplicaPlane()
    {
        // Phase 2: replaced by CreateZoneGround() per-zone. No-op kept for safety.
        Debug.Log("ℹ️ CreateReplicaPlane() skipped — zones use CreateZoneGround().");
    }
    
    void CreateReplicaWalls()
    {
        // Phase 2: replaced by CreateZoneWalls() per-zone. No-op kept for safety.
        Debug.Log("ℹ️ CreateReplicaWalls() skipped — zones use CreateZoneWalls().");
    }
    
    void CreateReplicaWall(string name, Vector3 position, Vector3 scale, Color color)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.position = position;
        wall.transform.localScale = scale;
        
        Renderer wallRenderer = wall.GetComponent<Renderer>();
        Material wallMat = null;
        
        Shader unlitShader = Shader.Find("Unlit/Color");
        if (unlitShader != null)
        {
            wallMat = new Material(unlitShader);
            wallMat.color = color;
        }
        else
        {
            Shader legacyShader = Shader.Find("Legacy Shaders/Diffuse");
            if (legacyShader != null)
            {
                wallMat = new Material(legacyShader);
                wallMat.color = color;
            }
        }
        
        if (wallMat != null)
        {
            wallRenderer.material = wallMat;
        }
        
        Rigidbody rb = wall.AddComponent<Rigidbody>();
        rb.isKinematic = true;
    }
    
    void CreateReplicaEntities()
    {
        Debug.Log("👥 Creating 1 P-agent per zone (4 zones)...");

        foreach (var zone in ZoneConfigs)
        {
            bool isTech = zone.agentId.Contains("Technician");
            Color color  = isTech
                ? new Color(0.2f, 1f, 0.2f, 1f)   // green for technicians
                : new Color(0.4f, 0.7f, 1f, 1f);  // blue for supervisors
            CreateReplicaEntity(zone.agentId, isTech, zone.zoneIndex, color);
        }

        Debug.Log("✅ REPLICA: All 4 zone agents created!");
    }
    
    void CreateReplicaEntity(string entityName, bool isTechnician, int index, Color prominentColor)
    {
        GameObject entity = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        entity.name = entityName;

        // Phase 2: Spawn each P-agent near the south edge of its own zone
        Vector3 spawnPos = new Vector3(0f, 1f, 0f);
        for (int z = 0; z < ZoneConfigs.Length; z++)
        {
            if (ZoneConfigs[z].agentId == entityName)
            {
                // ZoneHalf = 20, spawn 5 units from south wall
                spawnPos = ZoneConfigs[z].worldOffset + new Vector3(0f, 1f, -(ZoneHalf - 5f));
                break;
            }
        }

        entity.transform.position = spawnPos;
        Debug.Log($"📍 REPLICA: Spawning {entityName} at zone position: {spawnPos}");

        // Scale agent up — large enough to be clearly visible from camera height
        entity.transform.localScale = new Vector3(2.2f, 2.8f, 2.2f);
        
        // Add rigidbody - CRITICAL: Disable gravity to prevent jumping
        Rigidbody rb = entity.AddComponent<Rigidbody>();
        rb.useGravity = false; // DISABLED to prevent jumping/falling
        rb.freezeRotation = true;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ | RigidbodyConstraints.FreezePositionY; // Lock Y position
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.mass = 1.8f;
        rb.linearDamping = 0.3f;
        rb.angularDamping = 1.5f;

        if (entity.GetComponent<AgentCognitiveMemory>() == null)
        {
            entity.AddComponent<AgentCognitiveMemory>();
        }
        
        // Apply color (EXACT COPY)
        Renderer entityRenderer = entity.GetComponent<Renderer>();
        Material entityMat = null;
        
        Shader unlitShader = Shader.Find("Unlit/Color");
        if (unlitShader != null)
        {
            entityMat = new Material(unlitShader);
            entityMat.color = prominentColor;
        }
        else
        {
            Shader legacyShader = Shader.Find("Legacy Shaders/Diffuse");
            if (legacyShader != null)
            {
                entityMat = new Material(legacyShader);
                entityMat.color = prominentColor;
            }
        }
        
        if (entityMat != null)
        {
            entityRenderer.material = entityMat;
        }

        // Floating "P" label above physical agent
        AttachAgentLabel(entity, "P", new Color(1f, 0.9f, 0.2f));

        Debug.Log($"✅ REPLICA: Created {entityName} at {spawnPos}");
    }
    
    void CreateReplicaWorkbenchAndTools()
    {
        // Phase 2: replaced by CreateAllZoneTools() which creates zone-scoped copies.
        // No-op kept for safety.
        Debug.Log("ℹ️ CreateReplicaWorkbenchAndTools() skipped — zones use CreateAllZoneTools().");
    }

    // ─── Mental Agents (M-agents) ─────────────────────────────────────────
    private const int MentalAgentsPerZone = 3;

    void CreateAllMentalAgents()
    {
        foreach (var zone in ZoneConfigs)
        {
            EnsureZoneDeclarativeMemory(zone);
            EnsureMentalAgentSpawner(zone);
        }
        Debug.Log($"✅ MentalAgentSpawner created for {ZoneConfigs.Length} zones.");
    }

    void EnsureZoneDeclarativeMemory(ZoneConfig zone)
    {
        if (ZoneDeclarativeMemory.ForZone(zone.zoneIndex) != null) return;
        GameObject memGO = new GameObject($"ZoneDeclarativeMemory_Zone{zone.zoneIndex}");
        memGO.transform.position = zone.worldOffset;   // keep near zone centre
        ZoneDeclarativeMemory mem = memGO.AddComponent<ZoneDeclarativeMemory>();
        mem.zoneIndex            = zone.zoneIndex;
        // ~1 mark per VisitStation MemDone + P-scan injections (see MentalAgentController / PhysicalObservationLoop)
        mem.totalCognitiveSteps  = 22;
        // Awake() ran with zoneIndex=-1 (before the line above executed), so the static
        // registry was never populated with the correct index.  Force a re-scan now.
        ZoneDeclarativeMemory.RebuildRegistryFromScene();
        Debug.Log($"✅ ZoneDeclarativeMemory created for Zone{zone.zoneIndex}");
    }

    void EnsureMentalAgentSpawner(ZoneConfig zone)
    {
        // --- ZoneDeclarativeMemory (already created above) ---
        ZoneDeclarativeMemory mem = ZoneDeclarativeMemory.ForZone(zone.zoneIndex);

        // --- PhysicalObservationLoop: attach to P-agent in this zone ---
        PhysicalObservationLoop obsLoop = null;
        // P-agent is named directly by agentId (e.g. "SIMPLE_Technician_01")
        GameObject pAgentGO = GameObject.Find(zone.agentId);
        if (pAgentGO != null)
        {
            obsLoop = pAgentGO.GetComponent<PhysicalObservationLoop>();
            if (obsLoop == null) obsLoop = pAgentGO.AddComponent<PhysicalObservationLoop>();
            obsLoop.zoneIndex = zone.zoneIndex;
        }
        else
        {
            Debug.LogWarning($"[EnsureMentalAgentSpawner] P-agent GO not found for zone {zone.zoneIndex} ({zone.agentId})");
        }

        // --- MentalAgentSpawner ---
        string spawnerName = $"MentalAgentSpawner_Zone{zone.zoneIndex}";
        GameObject spawnerGO = GameObject.Find(spawnerName)
                            ?? new GameObject(spawnerName);

        MentalAgentSpawner spawner = spawnerGO.GetComponent<MentalAgentSpawner>()
                                  ?? spawnerGO.AddComponent<MentalAgentSpawner>();
        spawner.zoneIndex  = zone.zoneIndex;
        spawner.agentCount = MentalAgentsPerZone;
        // Position GO at zone centre so Init() can read transform.position as fallback
        spawnerGO.transform.position = zone.worldOffset;

        // Wire up ObsLoop ↔ Spawner
        if (obsLoop != null) obsLoop.Init(mem, spawner);
        spawner.Init(mem, obsLoop, MentalAgentsPerZone);

        // Kick off cognitive process after a short delay (deferred so scene is fully settled)
        StartCoroutine(StartSpawnerNextFrame(spawner));

        Debug.Log($"✅ MentalAgentSpawner wired for Zone{zone.zoneIndex} | obsLoop={obsLoop != null}");
    }

    private System.Collections.IEnumerator StartSpawnerNextFrame(MentalAgentSpawner spawner)
    {
        yield return new WaitForSeconds(1.5f);  // let all agents and tools fully settle after scene build
        spawner.StartCognitiveProcess();
    }

    void CreateMentalAgent(ZoneConfig zone, int agentIdx)
    {
        // Legacy stub — spawning is now handled by MentalAgentSpawner/MentalAgentController
    }

    /// <summary>
    /// Distributes the 12 cognitive steps across MentalAgentsPerZone agents.
    /// Agent 0 handles steps 1-4 (first-pass hypothetical reasoning).
    /// Agent 1 handles steps 5-8 (branch + P-scan + second-pass start).
    /// Agent 2 handles steps 9-12 (merge + decision + motor command).
    /// Steps are mapped to the zone-suffixed station IDs.
    /// </summary>
    private static readonly string[][] CognitiveStepStations =
    {
        // Agent 0: steps 1-4
        new[] { "cognitive_001", "cognitive_012", "cognitive_008", "cognitive_012" },
        // Agent 1: steps 5-8
        new[] { "cognitive_002", "cognitive_005", "cognitive_013", "cognitive_003" },
        // Agent 2: steps 9-12
        new[] { "cognitive_010", "cognitive_004", "cognitive_005", "cognitive_016" },
    };

    private static readonly string[][] CognitiveStepOutputKeys =
    {
        new[] { "intention",      "goal_image",   "goal",      "updated_image" },
        new[] { "tool_name",      "env_query",    "obs_data",  "decl_match"    },
        new[] { "prod_decision",  "motor_command","target",    "action_ready"  },
    };

    private static readonly string[][] CognitiveStepOutputValues =
    {
        new[] { "derive_intention",    "build_image",  "bind_goal",  "update_image_with_goal" },
        new[] { "retrieve_tool_name",  "prep_env_slots","scan_scene","match_schema"           },
        new[] { "select_action_rule",  "command_motor", "resolve_target", "ready_to_execute"  },
    };

    List<MentalAgent.CognitiveStepEntry> BuildMentalAgentSteps(int zoneIdx, int agentIdx)
    {
        var list = new List<MentalAgent.CognitiveStepEntry>();
        if (agentIdx >= MentalAgentsPerZone) return list;

        string[] stations    = CognitiveStepStations[agentIdx];
        string[] outputKeys  = CognitiveStepOutputKeys[agentIdx];
        string[] outputVals  = CognitiveStepOutputValues[agentIdx];
        int baseStep = agentIdx * 4 + 1;

        for (int s = 0; s < stations.Length; s++)
        {
            list.Add(new MentalAgent.CognitiveStepEntry
            {
                stepId        = $"cog_step_{baseStep + s}_z{zoneIdx}_m{agentIdx}",
                stepOrder     = baseStep + s,
                cognitiveState = $"CognitiveStep{baseStep + s}",
                targetObjectId = $"{stations[s]}_zone{zoneIdx}",
                outputKey      = outputKeys[s],
                outputValue    = outputVals[s],
            });
        }
        return list;
    }
    
    void CreateReplicaWorkbench(string objectName, string displayName, Vector3 position, Color color, Vector3 scale)
    {
        Debug.Log($"🔧 Creating detailed workbench: {displayName}");
        
        // Create main workbench body (EXACT COPY from SimpleFourEntitySystem)
        GameObject workbench = GameObject.CreatePrimitive(PrimitiveType.Cube);
        workbench.name = objectName;
        workbench.transform.position = position;
        workbench.transform.localScale = scale;
        
        Rigidbody rb = workbench.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        
        Renderer renderer = workbench.GetComponent<Renderer>();
        ApplyDirectColor(renderer, color);
        AttachDeclarativeMetadata(workbench, objectName, displayName, "workbench", "available", null);
        
        // Add professional details (EXACT COPY from SimpleFourEntitySystem)
        CreateWorkbenchLegs(workbench, position, scale);
        CreateWorkbenchSurface(workbench, position, scale);
        CreateControlPanel(workbench, position, scale);
        EnvironmentSolidCollider.EnsureOnObject(workbench, addBoxIfMissing: false);

        Debug.Log($"✅ REPLICA: Created detailed workbench {objectName} at {position}");
    }
    
    void CreateWorkbenchLegs(GameObject workbench, Vector3 position, Vector3 scale)
    {
        Vector3 legSize = new Vector3(0.1f, scale.y * 0.8f, 0.1f);
        Vector3[] legPositions = {
            new Vector3(position.x - scale.x * 0.4f, position.y - scale.y * 0.4f, position.z - scale.z * 0.4f),
            new Vector3(position.x + scale.x * 0.4f, position.y - scale.y * 0.4f, position.z - scale.z * 0.4f),
            new Vector3(position.x - scale.x * 0.4f, position.y - scale.y * 0.4f, position.z + scale.z * 0.4f),
            new Vector3(position.x + scale.x * 0.4f, position.y - scale.y * 0.4f, position.z + scale.z * 0.4f)
        };
        
        for (int i = 0; i < 4; i++)
        {
            GameObject leg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leg.name = $"{workbench.name}_Leg_{i + 1}";
            leg.transform.position = legPositions[i];
            leg.transform.localScale = legSize;
            
            Renderer legRenderer = leg.GetComponent<Renderer>();
            ApplyDirectColor(legRenderer, new Color(0.3f, 0.3f, 0.3f));
            
            Rigidbody legRb = leg.AddComponent<Rigidbody>();
            legRb.isKinematic = true;
        }
    }
    
    void CreateWorkbenchSurface(GameObject workbench, Vector3 position, Vector3 scale)
    {
        GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
        surface.name = $"{workbench.name}_Surface";
        surface.transform.position = new Vector3(position.x, position.y + scale.y * 0.1f, position.z);
        surface.transform.localScale = new Vector3(scale.x * 1.1f, scale.y * 0.1f, scale.z * 1.1f);
        
        Renderer surfaceRenderer = surface.GetComponent<Renderer>();
        ApplyDirectColor(surfaceRenderer, new Color(0.4f, 0.4f, 0.4f));
        
        Rigidbody surfaceRb = surface.AddComponent<Rigidbody>();
        surfaceRb.isKinematic = true;
    }
    
    void CreateControlPanel(GameObject workbench, Vector3 position, Vector3 scale)
    {
        GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = $"{workbench.name}_ControlPanel";
        panel.transform.position = new Vector3(position.x, position.y + scale.y * 0.3f, position.z - scale.z * 0.6f);
        panel.transform.localScale = new Vector3(scale.x * 0.6f, scale.y * 0.4f, 0.05f);
        
        Renderer panelRenderer = panel.GetComponent<Renderer>();
        ApplyDirectColor(panelRenderer, new Color(0.1f, 0.1f, 0.1f));
        
        Rigidbody panelRb = panel.AddComponent<Rigidbody>();
        panelRb.isKinematic = true;
        
        // Add control buttons
        for (int i = 0; i < 3; i++)
        {
            GameObject button = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            button.name = $"{panel.name}_Button_{i + 1}";
            button.transform.position = new Vector3(
                position.x - scale.x * 0.2f + i * scale.x * 0.2f,
                position.y + scale.y * 0.3f,
                position.z - scale.z * 0.55f
            );
            button.transform.localScale = new Vector3(0.1f, 0.02f, 0.1f);
            button.transform.rotation = Quaternion.Euler(90, 0, 0);
            
            Renderer buttonRenderer = button.GetComponent<Renderer>();
            Color[] buttonColors = { Color.red, Color.yellow, Color.green };
            ApplyDirectColor(buttonRenderer, buttonColors[i]);
            
            Rigidbody buttonRb = button.AddComponent<Rigidbody>();
            buttonRb.isKinematic = true;
        }
    }
    
    void CreateReplicaTool(string objectName, string displayName, Vector3 position, Color color, PrimitiveType type, Vector3? scale = null)
    {
        Debug.Log($"🔧 Creating detailed machine: {displayName}");
        
        GameObject tool = GameObject.CreatePrimitive(type);
        tool.name = objectName;
        tool.transform.position = position;
        
        if (scale.HasValue)
        {
            tool.transform.localScale = scale.Value;
        }
        
        Rigidbody rb = tool.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        
        Renderer renderer = tool.GetComponent<Renderer>();
        ApplyDirectColor(renderer, color);
        AttachDeclarativeMetadata(tool, objectName, displayName, "tool", "available", null);
        
        // Add professional details based on tool type
        if (objectName.Contains("tool_001")) // Industrial Motor Unit
        {
            CreateMotorDetails(tool, position, scale ?? Vector3.one);
        }
        else if (objectName.Contains("tool_002")) // Secure Toolbox Alpha
        {
            CreateToolboxDetails(tool, position, scale ?? Vector3.one);
        }
        else if (objectName.Contains("tool_003")) // Hydraulic Lift Station
        {
            CreateHydraulicDetails(tool, position, scale ?? Vector3.one);
        }
        else if (objectName.Contains("tool_004")) // Safety Inspection Station
        {
            CreateInspectionStationDetails(tool, position, scale ?? Vector3.one);
        }

        EnvironmentSolidCollider.EnsureOnObject(tool, addBoxIfMissing: false);

        Debug.Log($"✅ REPLICA: Created detailed tool {objectName} at {position}");
    }
    
    void CreateMotorDetails(GameObject motor, Vector3 position, Vector3 scale)
    {
        // Create motor housing
        GameObject housing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        housing.name = $"{motor.name}_Housing";
        housing.transform.position = new Vector3(position.x, position.y + scale.y * 0.3f, position.z);
        housing.transform.localScale = new Vector3(scale.x * 0.8f, scale.y * 0.6f, scale.z * 0.8f);
        
        Renderer housingRenderer = housing.GetComponent<Renderer>();
        ApplyDirectColor(housingRenderer, new Color(0.2f, 0.2f, 0.2f));
        
        Rigidbody housingRb = housing.AddComponent<Rigidbody>();
        housingRb.isKinematic = true;
        
        // Create cooling fins
        for (int i = 0; i < 8; i++)
        {
            GameObject fin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fin.name = $"{motor.name}_Fin_{i + 1}";
            float angle = i * 45f;
            Vector3 finPos = new Vector3(
                position.x + Mathf.Cos(angle * Mathf.Deg2Rad) * scale.x * 0.5f,
                position.y + scale.y * 0.3f,
                position.z + Mathf.Sin(angle * Mathf.Deg2Rad) * scale.z * 0.5f
            );
            fin.transform.position = finPos;
            fin.transform.localScale = new Vector3(0.05f, scale.y * 0.4f, 0.2f);
            fin.transform.rotation = Quaternion.Euler(0, angle, 0);
            
            Renderer finRenderer = fin.GetComponent<Renderer>();
            ApplyDirectColor(finRenderer, new Color(0.3f, 0.3f, 0.3f));
            
            Rigidbody finRb = fin.AddComponent<Rigidbody>();
            finRb.isKinematic = true;
        }
    }
    
    void CreateToolboxDetails(GameObject toolbox, Vector3 position, Vector3 scale)
    {
        // Create toolbox lid
        GameObject lid = GameObject.CreatePrimitive(PrimitiveType.Cube);
        lid.name = $"{toolbox.name}_Lid";
        lid.transform.position = new Vector3(position.x, position.y + scale.y * 0.6f, position.z);
        lid.transform.localScale = new Vector3(scale.x * 0.9f, scale.y * 0.2f, scale.z * 0.9f);
        
        Renderer lidRenderer = lid.GetComponent<Renderer>();
        ApplyDirectColor(lidRenderer, new Color(0.8f, 0.6f, 0.1f));
        
        Rigidbody lidRb = lid.AddComponent<Rigidbody>();
        lidRb.isKinematic = true;
        
        // Create lock mechanism
        GameObject lockMechanism = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        lockMechanism.name = $"{toolbox.name}_Lock";
        lockMechanism.transform.position = new Vector3(position.x, position.y + scale.y * 0.4f, position.z + scale.z * 0.4f);
        lockMechanism.transform.localScale = new Vector3(0.1f, 0.05f, 0.1f);
        lockMechanism.transform.rotation = Quaternion.Euler(90, 0, 0);
        
        Renderer lockRenderer = lockMechanism.GetComponent<Renderer>();
        ApplyDirectColor(lockRenderer, new Color(0.1f, 0.1f, 0.1f));
        
        Rigidbody lockRb = lockMechanism.AddComponent<Rigidbody>();
        lockRb.isKinematic = true;
    }
    
    void CreateHydraulicDetails(GameObject hydraulic, Vector3 position, Vector3 scale)
    {
        // Create hydraulic cylinder
        GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cylinder.name = $"{hydraulic.name}_Cylinder";
        cylinder.transform.position = new Vector3(position.x, position.y + scale.y * 0.5f, position.z);
        cylinder.transform.localScale = new Vector3(scale.x * 0.6f, scale.y * 0.8f, scale.z * 0.6f);
        
        Renderer cylinderRenderer = cylinder.GetComponent<Renderer>();
        ApplyDirectColor(cylinderRenderer, new Color(0.4f, 0.4f, 0.4f));
        
        Rigidbody cylinderRb = cylinder.AddComponent<Rigidbody>();
        cylinderRb.isKinematic = true;
        
        // Create piston
        GameObject piston = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        piston.name = $"{hydraulic.name}_Piston";
        piston.transform.position = new Vector3(position.x, position.y + scale.y * 0.9f, position.z);
        piston.transform.localScale = new Vector3(scale.x * 0.4f, scale.y * 0.3f, scale.z * 0.4f);
        
        Renderer pistonRenderer = piston.GetComponent<Renderer>();
        ApplyDirectColor(pistonRenderer, new Color(0.6f, 0.6f, 0.6f));
        
        Rigidbody pistonRb = piston.AddComponent<Rigidbody>();
        pistonRb.isKinematic = true;
    }
    
    void CreateInspectionStationDetails(GameObject station, Vector3 position, Vector3 scale)
    {
        // Create inspection table
        GameObject table = GameObject.CreatePrimitive(PrimitiveType.Cube);
        table.name = $"{station.name}_Table";
        table.transform.position = new Vector3(position.x, position.y + scale.y * 0.3f, position.z);
        table.transform.localScale = new Vector3(scale.x * 0.8f, scale.y * 0.2f, scale.z * 0.8f);
        
        Renderer tableRenderer = table.GetComponent<Renderer>();
        ApplyDirectColor(tableRenderer, new Color(0.5f, 0.5f, 0.5f));
        
        Rigidbody tableRb = table.AddComponent<Rigidbody>();
        tableRb.isKinematic = true;
        
        // Create inspection light
        GameObject light = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        light.name = $"{station.name}_Light";
        light.transform.position = new Vector3(position.x, position.y + scale.y * 0.7f, position.z);
        light.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
        
        Renderer lightRenderer = light.GetComponent<Renderer>();
        ApplyDirectColor(lightRenderer, Color.yellow);
        
        Rigidbody lightRb = light.AddComponent<Rigidbody>();
        lightRb.isKinematic = true;
    }
    
    // ─── Agent floating label helper ─────────────────────────────────────
    void AttachAgentLabel(GameObject agent, string labelChar, Color labelColor)
    {
        const string labelName = "AgentRoleLabel";
        // Remove stale label if any
        Transform old = agent.transform.Find(labelName);
        if (old != null) Destroy(old.gameObject);

        GameObject labelGO = new GameObject(labelName);
        labelGO.transform.SetParent(agent.transform, false);
        // Float above the capsule top
        labelGO.transform.localPosition = new Vector3(0f, 2.0f, 0f);
        // Tilt slightly toward camera (camera looks down at ~55°)
        labelGO.transform.localRotation = Quaternion.Euler(55f, 0f, 0f);

        TextMesh tm = labelGO.AddComponent<TextMesh>();
        tm.text          = labelChar;
        tm.anchor        = TextAnchor.MiddleCenter;
        tm.alignment     = TextAlignment.Center;
        tm.characterSize = 0.14f;
        tm.fontSize      = 64;
        tm.fontStyle     = FontStyle.Bold;
        tm.color         = labelColor;
    }

    void ApplyDirectColor(Renderer renderer, Color color)
    {
        if (renderer == null) return;
        
        Material mat = null;
        
        Shader unlitShader = Shader.Find("Unlit/Color");
        if (unlitShader != null)
        {
            mat = new Material(unlitShader);
            mat.color = color;
        }
        else
        {
            Shader legacyShader = Shader.Find("Legacy Shaders/Diffuse");
            if (legacyShader != null)
            {
                mat = new Material(legacyShader);
                mat.color = color;
            }
        }
        
        if (mat != null)
        {
            renderer.material = mat;
        }
    }

    void AttachDeclarativeMetadata(GameObject go, string objectId, string displayName, string type, string state, DeclarativeObjectData data)
    {
        if (go == null) return;

        DeclarativeObjectMetadata metadata = go.GetComponent<DeclarativeObjectMetadata>()
            ?? go.AddComponent<DeclarativeObjectMetadata>();
        metadata.Apply(data, objectId, displayName, type, state);
    }
    
    IEnumerator SetupReplicaComponentsDelayed()
    {
        // Wait for entities to be created
        yield return new WaitForSeconds(0.2f);
        
        // Add AgentProximity to all agents (EXACT COPY)
        GameObject tech01 = GameObject.Find("SIMPLE_Technician_01");
        GameObject tech02 = GameObject.Find("SIMPLE_Technician_02");
        GameObject sup01 = GameObject.Find("SIMPLE_Supervisor_01");
        GameObject sup02 = GameObject.Find("SIMPLE_Supervisor_02");
        
        if (tech01 != null)
        {
            AgentProximity prox01 = tech01.GetComponent<AgentProximity>();
            if (prox01 == null) prox01 = tech01.AddComponent<AgentProximity>();
            prox01.agentID = "SIMPLE_Technician_01";
            prox01.detectionRadius = 3f;
            prox01.targetLayer = LayerMask.GetMask("Default");
            Debug.Log("✅ REPLICA: Added AgentProximity to SIMPLE_Technician_01");
        }
        
        if (tech02 != null)
        {
            AgentProximity prox02 = tech02.GetComponent<AgentProximity>();
            if (prox02 == null) prox02 = tech02.AddComponent<AgentProximity>();
            prox02.agentID = "SIMPLE_Technician_02";
            prox02.detectionRadius = 3f;
            prox02.targetLayer = LayerMask.GetMask("Default");
            Debug.Log("✅ REPLICA: Added AgentProximity to SIMPLE_Technician_02");
        }
        
        if (sup01 != null)
        {
            AgentProximity prox03 = sup01.GetComponent<AgentProximity>();
            if (prox03 == null) prox03 = sup01.AddComponent<AgentProximity>();
            prox03.agentID = "SIMPLE_Supervisor_01";
            prox03.detectionRadius = 3f;
            prox03.targetLayer = LayerMask.GetMask("Default");
            Debug.Log("✅ REPLICA: Added AgentProximity to SIMPLE_Supervisor_01");
        }
        
        if (sup02 != null)
        {
            AgentProximity prox04 = sup02.GetComponent<AgentProximity>();
            if (prox04 == null) prox04 = sup02.AddComponent<AgentProximity>();
            prox04.agentID = "SIMPLE_Supervisor_02";
            prox04.detectionRadius = 3f;
            prox04.targetLayer = LayerMask.GetMask("Default");
            Debug.Log("✅ REPLICA: Added AgentProximity to SIMPLE_Supervisor_02");
        }
        
        // CRITICAL: Use MLAgentAttacher to properly set up ML-Agents with correct behavior names
        // This ensures automatic connection to ML-Agents training server
        // Behavior names must match YAML config: PhysicalAgentZone0–PhysicalAgentZone3
        
        if (tech01 != null)
        {
            // Use MLAgentAttacher which properly configures Behavior Parameters
            string behaviorName = MLAgentAttacher.GetBehaviorNameForAgent("SIMPLE_Technician_01");
            MLAgentAttacher.AttachMLAgentComponents(tech01, behaviorName, "SIMPLE_Technician_01");
            
            // CRITICAL: Double-check action space is correct
            var bp = tech01.GetComponent<Unity.MLAgents.Policies.BehaviorParameters>();
            if (bp != null)
            {
                Debug.Log($"🔍 TECH01 FINAL CHECK: ActionSpec={bp.BrainParameters.ActionSpec}, VectorObs={bp.BrainParameters.VectorObservationSize}");
            }
            
            // Update BSGMLAgent properties
            BSGMLAgent mlAgent = tech01.GetComponent<BSGMLAgent>();
            if (mlAgent != null)
                ApplyRuntimeAgentDefaults(mlAgent, "SIMPLE_Technician_01", 0f, 100f);
            Debug.Log($"✅ REPLICA: ML-Agent attached to SIMPLE_Technician_01 with behavior: {behaviorName}");
            SetDecisionRequesterEnabled(tech01, !ShouldEnablePersonaCognitiveControl());
        }
        
        if (tech02 != null)
        {
            string behaviorName = MLAgentAttacher.GetBehaviorNameForAgent("SIMPLE_Technician_02");
            MLAgentAttacher.AttachMLAgentComponents(tech02, behaviorName, "SIMPLE_Technician_02");
            
            BSGMLAgent mlAgent = tech02.GetComponent<BSGMLAgent>();
            if (mlAgent != null)
                ApplyRuntimeAgentDefaults(mlAgent, "SIMPLE_Technician_02", 0f, 80f);
            Debug.Log($"✅ REPLICA: ML-Agent attached to SIMPLE_Technician_02 with behavior: {behaviorName}");
            SetDecisionRequesterEnabled(tech02, !ShouldEnablePersonaCognitiveControl());
        }
        
        if (sup01 != null)
        {
            string behaviorName = MLAgentAttacher.GetBehaviorNameForAgent("SIMPLE_Supervisor_01");
            MLAgentAttacher.AttachMLAgentComponents(sup01, behaviorName, "SIMPLE_Supervisor_01");
            
            BSGMLAgent mlAgent = sup01.GetComponent<BSGMLAgent>();
            if (mlAgent != null)
                ApplyRuntimeAgentDefaults(mlAgent, "SIMPLE_Supervisor_01", 0f, 90f);
            Debug.Log($"✅ REPLICA: ML-Agent attached to SIMPLE_Supervisor_01 with behavior: {behaviorName}");
            SetDecisionRequesterEnabled(sup01, !ShouldEnablePersonaCognitiveControl());
        }
        
        if (sup02 != null)
        {
            string behaviorName = MLAgentAttacher.GetBehaviorNameForAgent("SIMPLE_Supervisor_02");
            MLAgentAttacher.AttachMLAgentComponents(sup02, behaviorName, "SIMPLE_Supervisor_02");
            
            BSGMLAgent mlAgent = sup02.GetComponent<BSGMLAgent>();
            if (mlAgent != null)
                ApplyRuntimeAgentDefaults(mlAgent, "SIMPLE_Supervisor_02", 0f, 100f);
            Debug.Log($"✅ REPLICA: ML-Agent attached to SIMPLE_Supervisor_02 with behavior: {behaviorName}");
            SetDecisionRequesterEnabled(sup02, !ShouldEnablePersonaCognitiveControl());
        }
        
        Debug.Log("✅ REPLICA: All ML-Agents configured with correct behavior names for automatic connection");
        
        // CRITICAL: Add MLAgentsDebugger to help diagnose issues
        GameObject debuggerGO = new GameObject("MLAgentsDebugger");
        MLAgentsDebugger debugger = debuggerGO.AddComponent<MLAgentsDebugger>();
        debugger.enableDebugLogs = true;
        debugger.debugInterval = 5f; // Check every 5 seconds
        Debug.Log("✅ REPLICA: Added MLAgentsDebugger for diagnostics");
        
        // CRITICAL: Add MLAgentsActionSpaceFixer to fix action space issues
        MLAgentsActionSpaceFixer actionFixer = debuggerGO.AddComponent<MLAgentsActionSpaceFixer>();
        actionFixer.autoFixOnStart = true;
        actionFixer.enableDebugLogs = true;
        Debug.Log("✅ REPLICA: Added MLAgentsActionSpaceFixer to fix action space configuration");
        
        // EMERGENCY: Add ForceMLAgentsActionSpaceFix for immediate fix
        GameObject emergencyFixGO = new GameObject("EmergencyMLAgentsFix");
        ForceMLAgentsActionSpaceFix emergencyFix = emergencyFixGO.AddComponent<ForceMLAgentsActionSpaceFix>();
        Debug.Log("🚨 REPLICA: Added EMERGENCY ML-Agents action space fix");
        
        // CRITICAL: Wait a moment then force fix all agents again
        StartCoroutine(FinalActionSpaceCheck());
        
        // DISABLED: Movement components removed - agents stay at initial positions
        // Agents should not move randomly in the scene
        if (tech01 != null)
        {
            // Disable any existing movement
            SimpleEntityMovement movement = tech01.GetComponent<SimpleEntityMovement>();
            if (movement != null)
            {
                movement.enabled = false;
                Debug.Log("🚫 Disabled SimpleEntityMovement on tech01");
            }
            // Stop any velocity
            Rigidbody rb = tech01.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
        
        if (tech02 != null)
        {
            SimpleEntityMovement movement = tech02.GetComponent<SimpleEntityMovement>();
            if (movement != null)
            {
                movement.enabled = false;
                Debug.Log("🚫 Disabled SimpleEntityMovement on tech02");
            }
            Rigidbody rb = tech02.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
        
        if (sup01 != null)
        {
            SimpleEntityMovement movement = sup01.GetComponent<SimpleEntityMovement>();
            if (movement != null)
            {
                movement.enabled = false;
                Debug.Log("🚫 Disabled SimpleEntityMovement on sup01");
            }
            Rigidbody rb = sup01.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
        
        if (sup02 != null)
        {
            SimpleEntityMovement movement = sup02.GetComponent<SimpleEntityMovement>();
            if (movement != null)
            {
                movement.enabled = false;
                Debug.Log("🚫 Disabled SimpleEntityMovement on sup02");
            }
            Rigidbody rb = sup02.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        TrySetupCognitiveControl(new List<GameObject> { tech01, tech02, sup01, sup02 });

        // Phase 2: stamp each agent with its zone index + zone-scoped play-area bounds
        ApplyZoneConfigToAgents();
        
        Debug.Log("✅ REPLICA: All components added to agents!");
    }

    // ─── Phase 2: stamp zone identity + zone-local play-area bounds on each P-agent ──
    void ApplyZoneConfigToAgents()
    {
        foreach (var zone in ZoneConfigs)
        {
            GameObject agentGO = GameObject.Find(zone.agentId);
            if (agentGO == null) continue;

            BSGMLAgent mlAgent = agentGO.GetComponent<BSGMLAgent>();
            if (mlAgent == null) continue;

            mlAgent.zoneIndex             = zone.zoneIndex;
            mlAgent.agentRole             = BSGMLAgent.AgentRole.Physical;
            mlAgent.waitForCognitiveReady = true;
            mlAgent.constrainToPlayArea   = true;
            // Keep agent inside its own zone — inset from both divider and outer walls
            mlAgent.playAreaMinX = zone.worldOffset.x - ZoneInset;
            mlAgent.playAreaMaxX = zone.worldOffset.x + ZoneInset;
            mlAgent.playAreaMinZ = zone.worldOffset.z - ZoneInset;
            mlAgent.playAreaMaxZ = zone.worldOffset.z + ZoneInset;

            Debug.Log($"✅ Zone{zone.zoneIndex} bounds → {zone.agentId}: " +
                      $"X[{mlAgent.playAreaMinX:F0}..{mlAgent.playAreaMaxX:F0}] " +
                      $"Z[{mlAgent.playAreaMinZ:F0}..{mlAgent.playAreaMaxZ:F0}]");
        }
    }

    void EnsurePersonaCognitiveBootstrap()
    {
        if (!ShouldEnablePersonaCognitiveControl())
        {
            return;
        }

        if (FindObjectOfType<PersonaCognitiveControlSystem>() == null)
        {
            GameObject controlObj = new GameObject("PersonaCognitiveControlSystem");
            controlObj.AddComponent<PersonaCognitiveControlSystem>();
        }

        if (FindObjectOfType<PersonaPreActionHudOverlay>() == null)
        {
            GameObject hudObj = new GameObject("PersonaPreActionHudOverlay");
            hudObj.AddComponent<PersonaPreActionHudOverlay>();
        }

        // Phase 1 update: cognitive modules/buffers now come from JSON stations.
        // Remove any legacy ACTR graph object generation to avoid duplicate scene objects.
        RemoveLegacyActrSceneObjects();
    }

    void TrySetupCognitiveControl(List<GameObject> agents)
    {
        if (!ShouldEnablePersonaCognitiveControl())
        {
            return;
        }

        PersonaCognitiveControlSystem control = FindObjectOfType<PersonaCognitiveControlSystem>();
        if (control == null)
        {
            GameObject controlObj = new GameObject("PersonaCognitiveControlSystem");
            control = controlObj.AddComponent<PersonaCognitiveControlSystem>();
        }

        control.InitializeForAgents(agents);
    }

    bool ShouldEnablePersonaCognitiveControl()
    {
        return SceneManager.GetActiveScene().name == "JSONWorkflowScenePersona";
    }

    void SetDecisionRequesterEnabled(GameObject agentObject, bool enabled)
    {
        if (agentObject == null)
        {
            return;
        }

        DecisionRequester decisionRequester = agentObject.GetComponent<DecisionRequester>();
        if (decisionRequester != null)
        {
            decisionRequester.enabled = enabled;
        }
    }

    void RemoveLegacyActrSceneObjects()
    {
        GameObject graphRoot = GameObject.Find("ACTR_SceneGraphRoot");
        if (graphRoot != null)
        {
            Destroy(graphRoot);
        }

        ActrSceneModulesVisualizer visualizer = FindObjectOfType<ActrSceneModulesVisualizer>();
        if (visualizer != null)
        {
            Destroy(visualizer.gameObject);
        }

        GameObject[] all = FindObjectsOfType<GameObject>(true);
        foreach (GameObject go in all)
        {
            if (go != null && go.name.StartsWith("ACTR_"))
            {
                Destroy(go);
            }
        }
    }
    
    [ContextMenu("Force Setup Scene")]
    public void ForceSetupScene()
    {
        ClearAllObjects();
        CreateReplicaSystem();
    }

    [ContextMenu("Refresh Cognitive Stations Only")]
    public void RefreshCognitiveStationsOnly()
    {
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        foreach (GameObject obj in allObjects)
        {
            if (obj != null && obj.name.StartsWith("cognitive_", StringComparison.OrdinalIgnoreCase))
                DestroyImmediate(obj);
        }

        CreateAllZoneCognitiveStations();
        Debug.Log("✅ REPLICA: Zone cognitive stations refreshed.");
    }
    
    /// <summary>
    /// Final check to ensure all agents have correct action space
    /// </summary>
    System.Collections.IEnumerator FinalActionSpaceCheck()
    {
        yield return new WaitForSeconds(3f);
        
        Debug.Log("🔍 FINAL ACTION SPACE CHECK:");
        
        // Check all agents one more time
        var allBehaviorParams = FindObjectsOfType<Unity.MLAgents.Policies.BehaviorParameters>();
        foreach (var bp in allBehaviorParams)
        {
            Debug.Log($"🔍 {bp.gameObject.name}:");
            Debug.Log($"  BehaviorName: {bp.BehaviorName}");
            Debug.Log($"  BehaviorType: {bp.BehaviorType}");
            Debug.Log($"  ActionSpec: {bp.BrainParameters.ActionSpec}");
            Debug.Log($"  VectorObservationSize: {bp.BrainParameters.VectorObservationSize}");
            
            if (bp.BrainParameters.ActionSpec.BranchSizes == null || bp.BrainParameters.ActionSpec.BranchSizes.Length != 3)
            {
                Debug.LogError($"❌ {bp.gameObject.name} STILL HAS WRONG ACTION SPACE!");
            }
            else
            {
                Debug.Log($"✅ {bp.gameObject.name} Action space is correct: [{string.Join(", ", bp.BrainParameters.ActionSpec.BranchSizes)}]");
            }
        }
    }
}
