using System;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[System.Serializable]
public class ProximityZone
{
    public string zoneId;
    public string centerObject;
    public float radius;
    public string actionType;
    public ActionData[] availableActions;
    public ProximityEffects effects;
    public string[] affectedAgents;
}

[System.Serializable]
public class ActionData
{
    public string actionId;
    public string actionName;
    public string actionType;
    public string color;
    public MaterialColor materialColor;
}

[System.Serializable]
public class MaterialColor
{
    public float r;
    public float g;
    public float b;
    public float a;
}

[System.Serializable]
public class ProximityEffects
{
    public ColorChangeEffect colorChange;
    public DirectionChangeEffect directionChange;
    public SoundEffect soundEffect;
}

[System.Serializable]
public class ColorChangeEffect
{
    public bool enabled;
    public Color targetColor;
    public float fadeSpeed;
}

[System.Serializable]
public class DirectionChangeEffect
{
    public bool enabled;
    public float avoidanceForce;
    public float rotationSpeed;
}

[System.Serializable]
public class SoundEffect
{
    public bool enabled;
    public string soundClip;
    public float volume;
}

[System.Serializable]
public class ProximityDetectionConfig
{
    public bool enabled;
    public float detectionRadius;
    public float updateFrequency;
    public ProximityZone[] proximityZones;
}

public class ProximityDetectionSystem : MonoBehaviour
{
    [Header("Proximity Detection Settings")]
    public ProximityDetectionConfig config;
    
    [Header("Debug Settings")]
    public bool showDebugGizmos = true;
    public Color gizmoColor = Color.yellow;
    [Tooltip("When off, suppresses per-frame 'PROXIMITY STATUS UPDATE' spam (RAG scenes).")]
    public bool logProximityFrameStatus = false;
    [Tooltip("When off, suppresses boundary/enter/exit logs in UpdateProximityDetection (major hitch fix).")]
    public bool verboseProximityEventLogs = false;
    
    [Header("Performance Settings")]
    public bool enableDirectionChanges = false; // DISABLED: No direction changes when hitting proximity
    
    [Header("Visual Zone Settings")]
    [Tooltip("When on, creates semi-transparent cylinder meshes at proximity zone centers (can look like a disc on/around agents if a zone is centered on an agent). Off by default for clean Game view; enable from Inspector when debugging zones.")]
    public bool showVisualZones = false;
    public Material zoneMaterial;
    private Dictionary<string, GameObject> visualZoneObjects = new Dictionary<string, GameObject>();
    
    private Dictionary<string, GameObject> objectRegistry = new Dictionary<string, GameObject>();
    private Dictionary<string, GameObject> agentRegistry = new Dictionary<string, GameObject>();
    private Dictionary<string, Color> originalColors = new Dictionary<string, Color>();
    private Dictionary<string, Renderer> objectRenderers = new Dictionary<string, Renderer>();
    private Dictionary<string, AudioSource> audioSources = new Dictionary<string, AudioSource>();

    /// <summary>Built from <see cref="ProximityDetectionConfig.proximityZones"/>; avoids O(n×m) scans and Debug spam in <see cref="OnDrawGizmos"/>.</summary>
    Dictionary<string, float> _radiusByCenterObject;
    
    // Track agents currently in proximity zones
    private Dictionary<string, HashSet<string>> agentsInProximity = new Dictionary<string, HashSet<string>>();
    private Dictionary<string, bool> agentInAnyProximity = new Dictionary<string, bool>();
    private Dictionary<string, float> lastDirectionChangeTime = new Dictionary<string, float>();
    private const float DIRECTION_CHANGE_COOLDOWN = 1.0f; // 1 second between direction changes
    
    private float lastUpdateTime;

    static void DestroyObjectSafe(UnityEngine.Object obj)
    {
        if (obj == null)
            return;

        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }

    /// <summary>Live cognitive stations for steering when JSON zones are missing, disabled, or too small vs. mesh colliders.</summary>
    readonly List<CognitiveStationInteractable> _cognitiveStationsAvoidanceCache = new List<CognitiveStationInteractable>();
    float _cognitiveStationsAvoidanceCacheTime = -999f;
    const float CognitiveStationsAvoidanceCacheTtl = 1.25f;

    readonly List<EnvironmentSolidCollider> _environmentAvoidanceCache = new List<EnvironmentSolidCollider>();
    float _environmentAvoidanceCacheTime = -999f;
    const float EnvironmentAvoidanceCacheTtl = 1.25f;

    void EnsureCognitiveStationAvoidanceCache()
    {
        if (Time.time - _cognitiveStationsAvoidanceCacheTime < CognitiveStationsAvoidanceCacheTtl)
            return;

        _cognitiveStationsAvoidanceCacheTime = Time.time;
        _cognitiveStationsAvoidanceCache.Clear();
        _cognitiveStationsAvoidanceCache.AddRange(FindObjectsOfType<CognitiveStationInteractable>());
    }

    /// <summary>Disc center (XZ) and radius from solid colliders when possible, else transform + interaction radius.</summary>
    static bool TryGetCognitiveStationAvoidanceDisc(CognitiveStationInteractable st, out Vector3 centerXz, out float discRadius)
    {
        centerXz = default;
        discRadius = 2.5f;
        if (st == null) return false;

        bool hasSolid = false;
        Bounds b = default;
        foreach (Collider col in st.GetComponentsInChildren<Collider>(true))
        {
            if (col == null || col.isTrigger) continue;
            if (!hasSolid)
            {
                b = col.bounds;
                hasSolid = true;
            }
            else
                b.Encapsulate(col.bounds);
        }

        float interactionR = Mathf.Max(0.5f, st.interactionRadius);

        if (hasSolid)
        {
            Vector3 c = b.center;
            centerXz = c;
            centerXz.y = 0f;
            float xzHalf = Mathf.Max(b.extents.x, b.extents.z);
            discRadius = Mathf.Max(interactionR * 1.08f, xzHalf * 1.45f, 1.85f);
            return true;
        }

        Vector3 p = st.transform.position;
        centerXz = p;
        centerXz.y = 0f;
        discRadius = Mathf.Max(2.1f, interactionR * 1.15f);
        return true;
    }

    /// <summary>XZ radial “no inward” + outer tangential blend (shared by JSON zones and live cognitive stations).</summary>
    static void ApplyRadialDiscRuleXZ(
        Vector3 agentXz,
        Vector3 centerXz,
        float zoneRadius,
        ref Vector3 vNorm,
        Vector3 goalDirNorm,
        float hullPadding,
        float approachBand,
        float aggression)
    {
        Vector3 delta = agentXz - centerXz;
        float dist = delta.magnitude;
        float R = Mathf.Max(0.1f, zoneRadius);
        float pad = Mathf.Max(0.05f, hullPadding);
        float inner = R + pad;
        float band = Mathf.Max(0.25f, approachBand);
        float outer = inner + band;

        if (dist > outer)
            return;

        Vector3 radialOut = dist > 0.06f
            ? delta / dist
            : Vector3.Cross(Vector3.up, goalDirNorm).normalized;

        if (dist <= inner)
        {
            float inward = Vector3.Dot(vNorm, -radialOut);
            if (inward > 0f)
            {
                vNorm = vNorm + radialOut * inward;
                vNorm.y = 0f;
                if (vNorm.sqrMagnitude < 1e-10f)
                    vNorm = TangentTowardGoal(radialOut, goalDirNorm);
                else
                    vNorm.Normalize();
            }
        }
        else
        {
            float u = 1f - Mathf.Clamp01((dist - inner) / Mathf.Max(0.001f, band));
            u *= Mathf.Clamp01(aggression / 1.15f);
            Vector3 tan = TangentTowardGoal(radialOut, goalDirNorm);
            vNorm = Vector3.Normalize(Vector3.Lerp(vNorm, tan, Mathf.Clamp01(u * 0.92f)));
            vNorm.y = 0f;
        }
    }
    
    void Start()
    {
        Debug.Log("ProximityDetectionSystem starting...");

        if (ShouldDeferFullSetupUntilEmbedSpawn())
        {
            InitializeDefaultConfig();
            Debug.Log("[ProximityDetectionSystem] Deferring registry/visual setup until post-spawn refresh (multiplayer embed).");
            return;
        }

        InitializeDefaultConfig();
        
        // Initialize registries
        InitializeObjectRegistry();
        InitializeAgentRegistry();
        
        // Initialize audio sources (will be safe now)
        InitializeAudioSources();
        
        // Load proximity config (will override defaults)
        LoadProximityConfig();
        
        // Create visual proximity zones
        CreateVisualProximityZones();
        
        Debug.Log("ProximityDetectionSystem initialization complete");
    }

    static bool ShouldDeferFullSetupUntilEmbedSpawn()
    {
        return BsgIntegrationSettings.MultiplayerEmbedMode
               || BsgIntegrationSettings.UseSceneAnchorLayout
               || BsgIntegrationSettings.UsePhotonPlayerAsPhysicalAgent;
    }

    void OnDestroy()
    {
        ClearVisualZoneObjects();
    }

    void ClearVisualZoneObjects()
    {
        foreach (var kv in visualZoneObjects)
        {
            if (kv.Value != null)
                DestroyObjectSafe(kv.Value);
        }
        visualZoneObjects.Clear();
    }

    /// <summary>
    /// Call after <see cref="SceneGenerator"/> spawns tools/agents so registry keys match
    /// <c>Tool_{initialStateKey}</c> and <c>Agent_{agentId}</c> (and proximity zones can resolve center objects).
    /// </summary>
    public void RefreshProximityRegistries()
    {
        InvalidateRadiusLookupCache();

        _cognitiveStationsAvoidanceCacheTime = -999f;
        _cognitiveStationsAvoidanceCache.Clear();
        _environmentAvoidanceCacheTime = -999f;
        _environmentAvoidanceCache.Clear();

        InitializeDefaultConfig();

        objectRegistry = new Dictionary<string, GameObject>();
        agentRegistry = new Dictionary<string, GameObject>();
        objectRenderers = new Dictionary<string, Renderer>();
        foreach (var kv in visualZoneObjects)
        {
            if (kv.Value != null)
                DestroyObjectSafe(kv.Value);
        }
        visualZoneObjects.Clear();

        InitializeObjectRegistry();
        InitializeAgentRegistry();
        InitializeAudioSources();
        CreateVisualProximityZones();
        Debug.Log($"[ProximityDetectionSystem] RefreshProximityRegistries: tools={objectRegistry.Count}, agents={agentRegistry.Count}, zones={config?.proximityZones?.Length ?? 0}");
    }

    void InvalidateRadiusLookupCache()
    {
        _radiusByCenterObject = null;
    }

    void EnsureRadiusLookupCache()
    {
        if (_radiusByCenterObject != null)
            return;
        _radiusByCenterObject = new Dictionary<string, float>(StringComparer.Ordinal);
        if (config?.proximityZones != null)
        {
            foreach (ProximityZone z in config.proximityZones)
            {
                if (z != null && !string.IsNullOrEmpty(z.centerObject))
                    _radiusByCenterObject[z.centerObject] = z.radius;
            }
        }
    }
    
    void InitializeDefaultConfig()
    {
        // Always initialize config if it's null
        if (config == null)
        {
            config = new ProximityDetectionConfig();
            Debug.Log("Created new ProximityDetectionConfig");
        }
        
        // Set default values
        config.enabled = true;
        config.detectionRadius = 3.0f;
        config.updateFrequency = 0.35f;
        
        // Initialize proximity zones array if null
        if (config.proximityZones == null)
        {
            config.proximityZones = new ProximityZone[0]; // Empty array initially
        }
        
        Debug.Log("Initialized default proximity config");
    }
    
    void Update()
    {
        if (config == null || !config.enabled) return;
        
        if (Time.time - lastUpdateTime >= config.updateFrequency)
        {
            UpdateProximityDetection();
            lastUpdateTime = Time.time;
        }
    }
    
    void InitializeObjectRegistry()
    {
        Debug.Log("*** INITIALIZING OBJECT REGISTRY ***");
        
        // Find all objects with specific tags or names
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        Debug.Log($"*** FOUND {allObjects.Length} TOTAL OBJECTS ***");
        
        int registeredCount = 0;
        
        foreach (GameObject obj in allObjects)
        {
            // Check if object has a specific component or tag that identifies it as a tool/workbench
            // Handle different naming patterns: tool_001, JSON_Tool_tool_001, etc.
            if (IsToolOrWorkbench(obj.name))
            {
                Debug.Log($"*** FOUND TOOL/WORKBENCH: {obj.name} ***");
                
                string objectId = ExtractObjectId(obj.name);
                Debug.Log($"*** EXTRACTED OBJECT ID: {objectId} from {obj.name} ***");
                
                if (!string.IsNullOrEmpty(objectId))
                {
                    objectRegistry[objectId] = obj;
                    registeredCount++;
                    
                    // Store original color and renderer
                    Renderer renderer = obj.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        objectRenderers[objectId] = renderer;
                        originalColors[objectId] = renderer.material.color;
                        Debug.Log($"*** REGISTERED WITH RENDERER: {objectId} -> {obj.name} at {obj.transform.position} ***");
                    }
                    else
                    {
                        Debug.LogWarning($"*** NO RENDERER FOUND: {obj.name} ***");
                    }
                    
                    Debug.Log($"*** REGISTERED TOOL/WORKBENCH: {objectId} -> {obj.name} ***");
                }
                else
                {
                    Debug.LogWarning($"*** COULD NOT EXTRACT OBJECT ID from: {obj.name} ***");
                }
            }
        }
        
        Debug.Log($"*** OBJECT REGISTRY COMPLETE: {registeredCount} tools/workbenches registered ***");
        Debug.Log($"*** REGISTERED OBJECTS: {string.Join(", ", objectRegistry.Keys)} ***");
    }

    /// <summary>
    /// Proximity zones may be centered on tools/stations (<see cref="objectRegistry"/>) or on agents (<see cref="agentRegistry"/>).
    /// </summary>
    bool TryResolveCenterGameObject(string centerObjectId, out GameObject centerGo)
    {
        centerGo = null;
        if (string.IsNullOrEmpty(centerObjectId))
            return false;
        if (objectRegistry.TryGetValue(centerObjectId, out GameObject og) && og != null)
        {
            centerGo = og;
            return true;
        }
        if (agentRegistry.TryGetValue(centerObjectId, out GameObject ag) && ag != null)
        {
            centerGo = ag;
            return true;
        }
        return false;
    }

    /// <summary>JSON proximity radius for a step target when a zone is configured (e.g. tool_001 / scene_007).</summary>
    public bool TryGetProximityRadiusForTarget(string targetObjectId, out float radius)
    {
        radius = 0f;
        if (string.IsNullOrWhiteSpace(targetObjectId) || config?.proximityZones == null)
            return false;

        for (int i = 0; i < config.proximityZones.Length; i++)
        {
            ProximityZone zone = config.proximityZones[i];
            if (zone == null || string.IsNullOrWhiteSpace(zone.centerObject))
                continue;
            if (!RagSequenceAgentMover.StationIdsMatch(zone.centerObject, targetObjectId))
                continue;

            radius = Mathf.Max(0.1f, zone.radius);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Non-target cognitive stations: <b>no inward radial motion</b> inside (radius + hullPadding) on XZ,
    /// plus an outer approach band that blends toward a tangential go-around toward the goal.
    /// Sources: (1) JSON <see cref="ProximityDetectionConfig.proximityZones"/> when enabled;
    /// (2) every <see cref="CognitiveStationInteractable"/> in the scene using solid collider bounds (or interaction radius),
    /// so desks still steer even when JSON zones are missing, disabled, or smaller than the mesh.
    /// Current step target is skipped so the agent can enter that station's proximity for dwell.
    /// </summary>
    public Vector3 BlendMovementAwayFromNonTargetCognitiveZones(
        Vector3 agentWorldPos,
        string stepTargetObjectId,
        Vector3 flatDesired,
        Vector3 flatTowardGoal,
        float aggression,
        float hullPadding,
        float approachBand,
        int agentZoneIndex = -1)
    {
        if (aggression < 0.0001f)
            return flatDesired;

        Vector3 v = flatDesired;
        v.y = 0f;
        if (v.sqrMagnitude < 1e-8f) return flatDesired;
        v.Normalize();

        Vector3 g = flatTowardGoal;
        g.y = 0f;
        if (g.sqrMagnitude < 1e-8f) g = v;
        else g.Normalize();

        Vector3 axz = agentWorldPos;
        axz.y = 0f;

        float pad = Mathf.Max(0.05f, hullPadding);
        float band = Mathf.Max(0.25f, approachBand);

        if (config != null && config.enabled && config.proximityZones != null)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < config.proximityZones.Length; i++)
                {
                    ProximityZone zone = config.proximityZones[i];
                    if (zone == null || string.IsNullOrEmpty(zone.centerObject)) continue;

                    if (RagSequenceAgentMover.StationIdsMatch(zone.centerObject, stepTargetObjectId))
                        continue;

                    if (agentZoneIndex >= 0 && !ZonePlayAreaBounds.BelongsToZone(zone.centerObject, agentZoneIndex))
                        continue;

                    if (!TryResolveCenterGameObject(zone.centerObject, out GameObject centerGo) || centerGo == null)
                        continue;

                    if (agentZoneIndex >= 0 && !ZonePlayAreaBounds.WorldPositionInZone(agentZoneIndex, centerGo.transform.position))
                        continue;

                    bool cognitiveCenter = centerGo.GetComponentInParent<CognitiveStationInteractable>() != null
                        || zone.centerObject.StartsWith("cognitive_", StringComparison.OrdinalIgnoreCase);
                    if (!cognitiveCenter)
                        continue;

                    Vector3 cxz = centerGo.transform.position;
                    cxz.y = 0f;

                    float R = Mathf.Max(0.1f, zone.radius);
                    ApplyRadialDiscRuleXZ(axz, cxz, R, ref v, g, pad, band, aggression);
                }
            }
        }

        // Scene stations: works when JSON is off, zones omit a desk, or JSON radius is smaller than the mesh.
        EnsureCognitiveStationAvoidanceCache();
        for (int pass = 0; pass < 2; pass++)
        {
            for (int si = 0; si < _cognitiveStationsAvoidanceCache.Count; si++)
            {
                CognitiveStationInteractable st = _cognitiveStationsAvoidanceCache[si];
                if (st == null) continue;

                string sid = string.IsNullOrEmpty(st.stationId) ? st.gameObject.name : st.stationId;
                if (RagSequenceAgentMover.StationIdsMatch(sid, stepTargetObjectId))
                    continue;

                if (agentZoneIndex >= 0)
                {
                    if (!ZonePlayAreaBounds.BelongsToZone(sid, agentZoneIndex))
                        continue;
                    if (!ZonePlayAreaBounds.WorldPositionInZone(agentZoneIndex, st.transform.position))
                        continue;
                }

                if (!TryGetCognitiveStationAvoidanceDisc(st, out Vector3 cxz, out float discR))
                    continue;

                ApplyRadialDiscRuleXZ(axz, cxz, discR, ref v, g, pad, band, aggression);
            }
        }

        return v.sqrMagnitude > 1e-8f ? v.normalized : flatDesired;
    }

    void EnsureEnvironmentAvoidanceCache()
    {
        if (Time.time - _environmentAvoidanceCacheTime < EnvironmentAvoidanceCacheTtl)
            return;

        _environmentAvoidanceCacheTime = Time.time;
        _environmentAvoidanceCache.Clear();

        foreach (EnvironmentSolidCollider solid in FindObjectsOfType<EnvironmentSolidCollider>(true))
        {
            if (solid == null)
                continue;
            if (solid.GetComponentInParent<CognitiveStationInteractable>() != null)
                continue;
            if (string.Equals(solid.gameObject.name, "CognitiveNavObstacle", System.StringComparison.Ordinal))
                continue;
            _environmentAvoidanceCache.Add(solid);
        }
    }

    static bool TryGetEnvironmentAvoidanceDisc(EnvironmentSolidCollider solid, out Vector3 centerXz, out float discRadius)
    {
        centerXz = default;
        discRadius = 1.5f;
        if (solid == null)
            return false;

        Collider col = solid.GetComponent<Collider>();
        if (col == null || col.isTrigger)
            return false;

        Bounds b = col.bounds;
        centerXz = b.center;
        centerXz.y = 0f;
        float xzHalf = Mathf.Max(b.extents.x, b.extents.z);
        discRadius = Mathf.Max(1.1f, xzHalf * 1.62f);
        return true;
    }

    static string ResolveEnvironmentObjectId(GameObject root)
    {
        if (root == null)
            return string.Empty;

        DeclarativeObjectMetadata meta = root.GetComponent<DeclarativeObjectMetadata>();
        if (meta != null && !string.IsNullOrWhiteSpace(meta.objectId))
            return meta.objectId;

        string name = root.name;
        if (name.StartsWith("Tool_", System.StringComparison.Ordinal))
        {
            string id = name.Substring("Tool_".Length);
            int zoneIdx = id.IndexOf("_zone", System.StringComparison.OrdinalIgnoreCase);
            if (zoneIdx > 0)
                id = id.Substring(0, zoneIdx);
            return id;
        }

        return name;
    }

    /// <summary>
    /// Steers around solid physical-environment props (tables, HubSpot, mouse keys, etc.) that are not the current step target.
    /// Uses the same radial proximity rules as cognitive stations but with wider discs for large props.
    /// </summary>
    public Vector3 BlendMovementAwayFromNonTargetEnvironmentObstacles(
        Vector3 agentWorldPos,
        string stepTargetObjectId,
        Vector3 flatDesired,
        Vector3 flatTowardGoal,
        float aggression,
        float hullPadding,
        float approachBand,
        int agentZoneIndex = -1)
    {
        if (aggression < 0.0001f)
            return flatDesired;

        Vector3 v = flatDesired;
        v.y = 0f;
        if (v.sqrMagnitude < 1e-8f)
            return flatDesired;
        v.Normalize();

        Vector3 g = flatTowardGoal;
        g.y = 0f;
        if (g.sqrMagnitude < 1e-8f)
            g = v;
        else
            g.Normalize();

        Vector3 axz = agentWorldPos;
        axz.y = 0f;

        float pad = Mathf.Max(0.05f, hullPadding);
        float band = Mathf.Max(0.35f, approachBand);

        EnsureEnvironmentAvoidanceCache();
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < _environmentAvoidanceCache.Count; i++)
            {
                EnvironmentSolidCollider solid = _environmentAvoidanceCache[i];
                if (solid == null)
                    continue;

                GameObject root = solid.transform.root.gameObject;
                string objectId = ResolveEnvironmentObjectId(root);
                if (RagSequenceAgentMover.StationIdsMatch(objectId, stepTargetObjectId))
                    continue;

                if (agentZoneIndex >= 0)
                {
                    if (!ZonePlayAreaBounds.WorldPositionInZone(agentZoneIndex, root.transform.position))
                        continue;
                }

                if (!TryGetEnvironmentAvoidanceDisc(solid, out Vector3 cxz, out float discR))
                    continue;

                ApplyRadialDiscRuleXZ(axz, cxz, discR, ref v, g, pad, band, aggression);
            }
        }

        return v.sqrMagnitude > 1e-8f ? v.normalized : flatDesired;
    }

    static Vector3 TangentTowardGoal(Vector3 radialOut, Vector3 flatGoalDir)
    {
        Vector3 t = Vector3.Cross(Vector3.up, radialOut);
        t.y = 0f;
        if (t.sqrMagnitude < 1e-10f) return flatGoalDir.sqrMagnitude > 1e-8f ? flatGoalDir.normalized : Vector3.forward;
        t.Normalize();
        if (Vector3.Dot(t, flatGoalDir) < Vector3.Dot(-t, flatGoalDir))
            t = -t;
        return t;
    }

    bool IsToolOrWorkbench(string objectName)
    {
        bool isTool = objectName.Contains("tool_") || 
                     objectName.Contains("workbench_") ||
                     objectName.Contains("cognitive_") ||
                     objectName.Contains("JSON_Tool_") ||
                     objectName.Contains("Tool_") ||
                     objectName.Contains("Workbench_") ||
                     objectName.Contains("Tool_tool_") ||
                     objectName.Contains("Tool_workbench_") ||
                     objectName.Contains("Tool_cognitive_");
        
        if (isTool)
        {
            Debug.Log($"*** IDENTIFIED AS TOOL/WORKBENCH: {objectName} ***");
        }
        
        return isTool;
    }
    
    string ExtractObjectId(string objectName)
    {
        // SceneGenerator names tools Tool_{initialStateKey} — keep full key (e.g. cognitive_001_zone0).
        int toolPrefix = objectName.IndexOf("Tool_", StringComparison.Ordinal);
        if (toolPrefix >= 0)
        {
            string after = objectName.Substring(toolPrefix + "Tool_".Length);
            int paren = after.IndexOf('(');
            if (paren > 0)
                after = after.Substring(0, paren).TrimEnd();
            if (!string.IsNullOrEmpty(after))
                return after;
        }

        Debug.Log($"*** EXTRACTING OBJECT ID from: {objectName} ***");
        
        // Extract the actual object ID from various naming patterns
        if (objectName.Contains("cognitive_"))
        {
            int startIndex = objectName.IndexOf("cognitive_");
            if (startIndex >= 0)
            {
                string remaining = objectName.Substring(startIndex);
                int endIndex = remaining.IndexOf("_", 10);
                if (endIndex > 0)
                {
                    string result = remaining.Substring(0, endIndex);
                    Debug.Log($"*** EXTRACTED COGNITIVE ID: {result} ***");
                    return result;
                }
                Debug.Log($"*** EXTRACTED COGNITIVE ID (full): {remaining} ***");
                return remaining;
            }
        }
        else if (objectName.Contains("tool_"))
        {
            // Find tool_001, tool_002, etc.
            int startIndex = objectName.IndexOf("tool_");
            if (startIndex >= 0)
            {
                string remaining = objectName.Substring(startIndex);
                int endIndex = remaining.IndexOf("_", 5); // Skip "tool_"
                if (endIndex > 0)
                {
                    string result = remaining.Substring(0, endIndex);
                    Debug.Log($"*** EXTRACTED TOOL ID: {result} ***");
                    return result;
                }
                else
                {
                    Debug.Log($"*** EXTRACTED TOOL ID (full): {remaining} ***");
                    return remaining; // tool_001, tool_002, etc.
                }
            }
        }
        else if (objectName.Contains("workbench_"))
        {
            // Find workbench_001, etc.
            int startIndex = objectName.IndexOf("workbench_");
            if (startIndex >= 0)
            {
                string remaining = objectName.Substring(startIndex);
                int endIndex = remaining.IndexOf("_", 9); // Skip "workbench_"
                if (endIndex > 0)
                {
                    string result = remaining.Substring(0, endIndex);
                    Debug.Log($"*** EXTRACTED WORKBENCH ID: {result} ***");
                    return result;
                }
                else
                {
                    Debug.Log($"*** EXTRACTED WORKBENCH ID (full): {remaining} ***");
                    return remaining; // workbench_001, etc.
                }
            }
        }
        
        Debug.LogWarning($"*** COULD NOT EXTRACT OBJECT ID from: {objectName} ***");
        return null;
    }
    
    void InitializeAgentRegistry()
    {
        // Find all agent objects
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            // Handle different agent naming patterns: agent_technician_A, Technician_agent_technician_A, etc.
            if (IsAgent(obj.name))
            {
                string agentId = ExtractAgentId(obj.name);
                if (!string.IsNullOrEmpty(agentId))
                {
                    agentRegistry[agentId] = obj;
                    
                    // Store original color for this agent
                    Renderer renderer = obj.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        originalColors[agentId] = renderer.material.color;
                        Debug.Log($"Registered agent: {agentId} -> {obj.name} with original color: {renderer.material.color}");
                    }
                    else
                    {
                        Debug.LogWarning($"No renderer found on agent: {obj.name}");
                    }
                }
                else
                {
                    Debug.LogWarning($"Could not extract agent ID from: {obj.name}");
                }
            }
        }
        
        Debug.Log($"Agent registry initialized with {agentRegistry.Count} agents");
        Debug.Log($"Original colors stored for {originalColors.Count} agents");
    }
    
    bool IsAgent(string objectName)
    {
        return objectName.Contains("agent_") ||
               objectName.Contains("Technician_") ||
               objectName.Contains("Supervisor_") ||
               objectName.Contains("Inspector_") ||
               objectName.Contains("Agent_") ||
               objectName.StartsWith("Agent_", StringComparison.Ordinal) ||
               objectName.Contains("SIMPLE_Technician") ||
               objectName.Contains("SIMPLE_Supervisor") ||
               objectName.Contains("SIMPLE_Inspector");
    }
    
    string ExtractAgentId(string objectName)
    {
        // RAG / SceneGenerator: Agent_M1, Agent_P2 → M1, P2 (matches agentProfiles keys & cognitive JSON)
        if (objectName.StartsWith("Agent_", StringComparison.Ordinal))
        {
            string id = objectName.Substring("Agent_".Length);
            int paren = id.IndexOf('(');
            if (paren > 0) id = id.Substring(0, paren).TrimEnd();
            if (!string.IsNullOrWhiteSpace(id))
                return id.Trim();
        }

        Debug.Log($"*** EXTRACTING AGENT ID from: {objectName} ***");
        
        // Extract the actual agent ID from various naming patterns
        if (objectName.Contains("agent_"))
        {
            // Find agent_technician_A, agent_supervisor_B, etc.
            int startIndex = objectName.IndexOf("agent_");
            if (startIndex >= 0)
            {
                string remaining = objectName.Substring(startIndex);
                Debug.Log($"*** REMAINING STRING: {remaining} ***");
                
                // Extract the full agent ID: agent_technician_A, agent_supervisor_B, agent_inspector_C
                if (remaining.Contains("technician_A"))
                {
                    Debug.Log($"*** EXTRACTED: agent_technician_A ***");
                    return "agent_technician_A";
                }
                else if (remaining.Contains("supervisor_B"))
                {
                    Debug.Log($"*** EXTRACTED: agent_supervisor_B ***");
                    return "agent_supervisor_B";
                }
                else if (remaining.Contains("inspector_C"))
                {
                    Debug.Log($"*** EXTRACTED: agent_inspector_C ***");
                    return "agent_inspector_C";
                }
                else if (remaining.Contains("technician"))
                {
                    Debug.Log($"*** EXTRACTED: agent_technician_A (fallback) ***");
                    return "agent_technician_A"; // Default technician
                }
                else if (remaining.Contains("supervisor"))
                {
                    Debug.Log($"*** EXTRACTED: agent_supervisor_B (fallback) ***");
                    return "agent_supervisor_B"; // Default supervisor
                }
                else if (remaining.Contains("inspector"))
                {
                    Debug.Log($"*** EXTRACTED: agent_inspector_C (fallback) ***");
                    return "agent_inspector_C"; // Default inspector
                }
                else
                {
                    Debug.Log($"*** EXTRACTED: {remaining} (full remaining) ***");
                    return remaining; // Fallback to full remaining string
                }
            }
        }
        else if (objectName.Contains("SIMPLE_Technician"))
        {
            // Handle SIMPLE_Technician_01, SIMPLE_Technician_02 - Use JSON-compatible IDs
            if (objectName.Contains("01"))
            {
                Debug.Log($"*** EXTRACTED: SIMPLE_Technician_01 ***");
                return "SIMPLE_Technician_01";
            }
            else if (objectName.Contains("02"))
            {
                Debug.Log($"*** EXTRACTED: SIMPLE_Technician_02 ***");
                return "SIMPLE_Technician_02";
            }
            else
            {
                Debug.Log($"*** EXTRACTED: SIMPLE_Technician_01 (SIMPLE default) ***");
                return "SIMPLE_Technician_01"; // Default
            }
        }
        else if (objectName.Contains("SIMPLE_Supervisor"))
        {
            // Handle SIMPLE_Supervisor_01, SIMPLE_Supervisor_02 - Use JSON-compatible IDs
            if (objectName.Contains("01"))
            {
                Debug.Log($"*** EXTRACTED: SIMPLE_Supervisor_01 ***");
                return "SIMPLE_Supervisor_01";
            }
            else if (objectName.Contains("02"))
            {
                Debug.Log($"*** EXTRACTED: SIMPLE_Supervisor_02 ***");
                return "SIMPLE_Supervisor_02";
            }
            else
            {
                Debug.Log($"*** EXTRACTED: SIMPLE_Supervisor_01 (SIMPLE default) ***");
                return "SIMPLE_Supervisor_01"; // Default
            }
        }
        else if (objectName.Contains("SIMPLE_Inspector"))
        {
            Debug.Log($"*** EXTRACTED: SIMPLE_Supervisor_02 (SIMPLE Inspector fallback) ***");
            return "SIMPLE_Supervisor_02"; // Map to supervisor 02 for proximity system
        }
        
        Debug.LogWarning($"*** NO AGENT ID EXTRACTED from: {objectName} ***");
        return null;
    }

    /// <summary>
    /// Loader lists profile IDs (M1); spawned agents are named Agent_M1.
    /// </summary>
    bool AgentMatchesZoneAffected(ProximityZone zone, string agentName, string extractedAgentId)
    {
        if (zone?.affectedAgents == null || zone.affectedAgents.Length == 0)
            return true;

        foreach (string entry in zone.affectedAgents)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            if (string.Equals(agentName, entry, StringComparison.Ordinal))
                return true;
            if (!string.IsNullOrEmpty(extractedAgentId) &&
                string.Equals(entry, extractedAgentId, StringComparison.Ordinal))
                return true;
            if (string.Equals(agentName, "Agent_" + entry, StringComparison.Ordinal))
                return true;
        }

        return IsSIMPLEAgentAffected(agentName, zone.affectedAgents);
    }
    
    bool IsSIMPLEAgentAffected(string agentName, string[] affectedAgents)
    {
        // Check if SIMPLE agent should be affected based on its role
        if (agentName.Contains("SIMPLE_"))
        {
            // Map SIMPLE agents to their corresponding role-based agent IDs for proximity detection
            if (agentName.Contains("Technician"))
            {
                bool isAffected = affectedAgents.Contains("agent_technician_A");
                Debug.Log($"*** SIMPLE TECHNICIAN CHECK: {agentName} -> agent_technician_A -> {isAffected} ***");
                return isAffected;
            }
            else if (agentName.Contains("Supervisor"))
            {
                bool isAffected = affectedAgents.Contains("agent_supervisor_B");
                Debug.Log($"*** SIMPLE SUPERVISOR CHECK: {agentName} -> agent_supervisor_B -> {isAffected} ***");
                return isAffected;
            }
            else if (agentName.Contains("Inspector"))
            {
                bool isAffected = affectedAgents.Contains("agent_inspector_C");
                Debug.Log($"*** SIMPLE INSPECTOR CHECK: {agentName} -> agent_inspector_C -> {isAffected} ***");
                return isAffected;
            }
        }
        return false;
    }
    
    void InitializeAudioSources()
    {
        // Initialize audio sources for each proximity zone
        if (config.proximityZones == null) return;
        
        foreach (var zone in config.proximityZones)
        {
                if (zone.effects.soundEffect.enabled)
                {
                    if (TryResolveCenterGameObject(zone.centerObject, out GameObject centerObj))
                    {
                        AudioSource audioSource = centerObj.GetComponent<AudioSource>();
                        if (audioSource == null)
                            audioSource = centerObj.AddComponent<AudioSource>();
                        audioSources[zone.zoneId] = audioSource;
                    }
                    else
                    {
                        Debug.LogWarning($"Center object '{zone.centerObject}' not found in object/agent registry for zone '{zone.zoneId}'");
                    }
                }
        }
    }
    
    void LoadProximityConfig()
    {
        // Load configuration from JSON file
        string jsonPath = "Assets/JsonFile/basicUi.json";
        if (System.IO.File.Exists(jsonPath))
        {
            string jsonContent = System.IO.File.ReadAllText(jsonPath);
            // Parse JSON and extract proximity detection config
            // This would need proper JSON parsing - simplified for now
        }
    }
    
    void UpdateProximityDetection()
    {
        if (config.proximityZones == null) 
        {
            Debug.LogWarning("Proximity zones are null!");
            return;
        }
        
        // Track current frame's proximity status
        Dictionary<string, HashSet<string>> currentFrameProximity = new Dictionary<string, HashSet<string>>();
        Dictionary<string, bool> currentFrameAgentInAnyProximity = new Dictionary<string, bool>();
        
        // Initialize tracking for this frame
        foreach (var zone in config.proximityZones)
        {
            if (zone != null)
            {
                currentFrameProximity[zone.zoneId] = new HashSet<string>();
            }
        }
        
        // Check all proximity zones for boundary detection
        foreach (var zone in config.proximityZones)
        {
            if (zone == null) continue;
            
            if (!TryResolveCenterGameObject(zone.centerObject, out GameObject centerObject))
                continue;
            
            Vector3 centerPosition = centerObject.transform.position;
            
            // Use a larger detection radius to catch agents approaching the boundary
            float detectionRadius = zone.radius + 1.0f; // Add 1 unit buffer for boundary detection
            
            // Detect agents approaching the proximity zone boundary
            Collider[] hitColliders = Physics.OverlapSphere(centerPosition, detectionRadius);
            
            foreach (var hit in hitColliders)
            {
                if (hit == null || hit.gameObject == centerObject) continue;
                
                string agentName = hit.gameObject.name;
                string agentId = ExtractAgentId(agentName);
                
                // Check if this agent is affected by this proximity zone
                bool isAffected = AgentMatchesZoneAffected(zone, agentName, agentId);
                
                if (isAffected)
                {
                    // Calculate distance to center
                    float distanceToCenter = Vector3.Distance(hit.transform.position, centerPosition);
                    
                    // Check if agent is at the boundary (within detection radius but outside actual zone)
                    bool isAtBoundary = distanceToCenter > zone.radius && distanceToCenter <= detectionRadius;
                    bool isInsideZone = distanceToCenter <= zone.radius;
                    
                    if (isAtBoundary)
                    {
                        // Agent is at the boundary - trigger effects and redirect
                        bool wasInProximity = agentsInProximity.ContainsKey(zone.zoneId) && 
                                            agentsInProximity[zone.zoneId].Contains(agentId);
                        
                        if (!wasInProximity)
                        {
                            // Agent just hit the boundary
                            ApplyBoundaryEffects(hit.gameObject, zone, centerPosition);
                            if (verboseProximityEventLogs)
                                Debug.Log($"*** AGENT HIT BOUNDARY: {agentName} (ID: {agentId}) hit boundary of {zone.zoneId} ***");
                            
                            // Mark as in proximity to prevent re-triggering
                            currentFrameProximity[zone.zoneId].Add(agentId);
                            currentFrameAgentInAnyProximity[agentId] = true;
                        }
                    }
                    else if (isInsideZone)
                    {
                        // Agent is inside the zone - DISABLED: No redirection
                        // Agents should not be redirected when in proximity
                        // RedirectAgentOutOfZone(hit.gameObject, zone, centerPosition);
                        if (verboseProximityEventLogs)
                            Debug.Log($"*** AGENT INSIDE ZONE: {agentName} (ID: {agentId}) inside {zone.zoneId} (no redirection) ***");
                        
                        currentFrameProximity[zone.zoneId].Add(agentId);
                        currentFrameAgentInAnyProximity[agentId] = true;
                    }
                }
            }
        }
        
        // Check for agents that left proximity zones
        foreach (var zoneId in agentsInProximity.Keys)
        {
            if (currentFrameProximity.ContainsKey(zoneId))
            {
                foreach (var agentId in agentsInProximity[zoneId])
                {
                    if (!currentFrameProximity[zoneId].Contains(agentId))
                    {
                        // Agent left this proximity zone
                        if (verboseProximityEventLogs)
                            Debug.Log($"*** AGENT LEFT ZONE: {agentId} left {zoneId} ***");
                        HandleAgentExitProximity(agentId, zoneId);
                    }
                }
            }
        }
        
        // Check for agents that left all proximity zones
        foreach (var agentId in agentInAnyProximity.Keys)
        {
            if (!currentFrameAgentInAnyProximity.ContainsKey(agentId))
            {
                // Agent left all proximity zones
                if (verboseProximityEventLogs)
                    Debug.Log($"*** AGENT LEFT ALL ZONES: {agentId} left all proximity zones ***");
                HandleAgentExitAllProximity(agentId);
            }
        }
        
        // Debug: optional — default off for RAG (hundreds of zones × every frame)
        if (logProximityFrameStatus)
        {
            Debug.Log($"=== PROXIMITY STATUS UPDATE ===");
            Debug.Log($"Previous frame agents in proximity: {agentInAnyProximity.Count}");
            Debug.Log($"Current frame agents in proximity: {currentFrameAgentInAnyProximity.Count}");
            foreach (var zone in currentFrameProximity)
            {
                Debug.Log($"Zone {zone.Key}: {zone.Value.Count} agents");
                foreach (var aid in zone.Value)
                    Debug.Log($"  - {aid}");
            }
        }
        
        // Update tracking for next frame
        agentsInProximity = currentFrameProximity;
        agentInAnyProximity = currentFrameAgentInAnyProximity;
    }
    
    void ApplyProximityEffects(GameObject agent, ProximityZone zone, Vector3 centerPosition)
    {
        if (agent == null || zone == null || zone.effects == null) return;
        
        Debug.Log($"*** APPLYING EFFECTS to {agent.name} (ENTERED {zone.zoneId}) ***");
        
        // Apply color change effect (only when entering)
        if (zone.effects.colorChange != null && zone.effects.colorChange.enabled)
        {
            ApplyColorChange(agent, zone.effects.colorChange);
            Debug.Log($"Applied color change to {agent.name}");
        }
        
        // Apply direction change effect (only when entering) - DISABLED
        // Direction changes removed - agents should not reflect when hitting proximity
        // if (zone.effects.directionChange != null && zone.effects.directionChange.enabled)
        // {
        //     ApplyDirectionChange(agent, zone.effects.directionChange, centerPosition);
        //     Debug.Log($"Applied direction change to {agent.name}");
        // }
        
        // Random jumps break scripted RAG paths — RagSequenceAgentMover handles navigation + proximity steering.
        if (agent.GetComponent<RagSequenceAgentMover>() == null)
            ApplyRandomPositionChange(agent);
        
        // Apply sound effect (only when entering)
        if (zone.effects.soundEffect != null && zone.effects.soundEffect.enabled)
        {
            PlayProximitySound(zone.zoneId, zone.effects.soundEffect);
        }
    }
    
    void ApplyBoundaryEffects(GameObject agent, ProximityZone zone, Vector3 centerPosition)
    {
        if (agent == null || zone == null || zone.effects == null) 
        {
            Debug.LogWarning($"*** BOUNDARY EFFECTS: Agent={agent != null}, Zone={zone != null}, Effects={zone?.effects != null} ***");
            return;
        }
        
        Debug.Log($"*** APPLYING BOUNDARY EFFECTS to {agent.name} (HIT BOUNDARY of {zone.zoneId}) ***");
        
        // SIMPLIFIED: Only use ApplyColorChange, remove HandleAgentBoundaryHit to avoid conflicts
        // HandleAgentBoundaryHit(agent, zone);
        
        // Apply color change effect immediately when hitting boundary
        if (zone.effects.colorChange != null && zone.effects.colorChange.enabled)
        {
            Debug.Log($"*** CALLING APPLY COLOR CHANGE for {agent.name} ***");
            ApplyColorChange(agent, zone.effects.colorChange);
            Debug.Log($"*** APPLY COLOR CHANGE COMPLETED for {agent.name} ***");
        }
        else
        {
            Debug.LogWarning($"*** NO COLOR CHANGE EFFECT: zone.effects.colorChange={zone.effects.colorChange != null}, enabled={zone.effects.colorChange?.enabled} ***");
        }
        
        // Apply strong direction change to redirect agent away from zone - DISABLED
        // Direction changes removed - agents should not reflect when hitting proximity
        // if (zone.effects.directionChange != null && zone.effects.directionChange.enabled)
        // {
        //     ApplyStrongDirectionChange(agent, zone.effects.directionChange, centerPosition);
        //     Debug.Log($"Applied strong direction change to {agent.name}");
        // }
        
        // Apply sound effect
        if (zone.effects.soundEffect != null && zone.effects.soundEffect.enabled)
        {
            PlayProximitySound(zone.zoneId, zone.effects.soundEffect);
        }
    }
    
    void RedirectAgentOutOfZone(GameObject agent, ProximityZone zone, Vector3 centerPosition)
    {
        // DISABLED: Agent redirection removed - agents should not be moved when in proximity
        Debug.Log($"🚫 Agent redirection disabled for {agent.name} - proximity redirection removed");
        return;
        
        // Original code commented out:
        // if (agent == null || zone == null) return;
        // Debug.Log($"*** REDIRECTING AGENT OUT OF ZONE: {agent.name} ***");
        // Vector3 awayDirection = (agent.transform.position - centerPosition).normalized;
        // float redirectDistance = zone.radius + 2.0f;
        // Vector3 newPosition = centerPosition + awayDirection * redirectDistance;
        // agent.transform.position = newPosition;
        // if (zone.effects != null && zone.effects.colorChange != null && zone.effects.colorChange.enabled)
        // {
        //     ApplyColorChange(agent, zone.effects.colorChange);
        // }
    }
    
    void ApplyRandomPositionChange(GameObject agent)
    {
        // Apply a random position change when agent enters proximity zone
        Vector3 randomDirection = new Vector3(
            UnityEngine.Random.Range(-1f, 1f),
            0,
            UnityEngine.Random.Range(-1f, 1f)
        ).normalized;
        
        float moveDistance = UnityEngine.Random.Range(1f, 3f);
        agent.transform.position += randomDirection * moveDistance;
        
        Debug.Log($"*** MOVED {agent.name} in random direction {randomDirection} by {moveDistance} units ***");
    }
    
    void HandleAgentExitProximity(string agentId, string zoneId)
    {
        Debug.Log($"*** AGENT LEFT PROXIMITY: {agentId} left {zoneId} ***");
        
        // Find the agent GameObject
        GameObject agent = FindAgentById(agentId);
        if (agent != null)
        {
            // Check if agent is still in any other proximity zone
            bool stillInAnyProximity = false;
            foreach (var zone in config.proximityZones)
            {
                if (zone != null && zone.zoneId != zoneId)
                {
                    if (agentsInProximity.ContainsKey(zone.zoneId) && 
                        agentsInProximity[zone.zoneId].Contains(agentId))
                    {
                        stillInAnyProximity = true;
                        break;
                    }
                }
            }
            
            // Only reset color if agent is not in any proximity zone
            if (!stillInAnyProximity)
            {
                // Check if agent has ActionSelectionSystem component
                ActionSelectionSystem actionSystem = agent.GetComponent<ActionSelectionSystem>();
                if (actionSystem != null)
                {
                    // Clear available actions and reset to default
                    actionSystem.ClearAvailableActions();
                    Debug.Log($"*** ACTION SYSTEM: Cleared actions for {agentId} (left all proximity zones) ***");
                }
                else
                {
                    // Fallback to old color reset
                    ResetAgentColor(agent);
                    Debug.Log($"*** FALLBACK RESET COLOR: {agentId} color reset to default (left all proximity zones) ***");
                }
            }
        }
    }
    
    void HandleAgentExitAllProximity(string agentId)
    {
        Debug.Log($"*** AGENT LEFT ALL PROXIMITY: {agentId} left all proximity zones ***");
        
        // Find the agent GameObject
        GameObject agent = FindAgentById(agentId);
        if (agent != null)
        {
            Debug.Log($"*** FOUND AGENT: {agent.name} for ID {agentId} ***");
            
            // Check if agent has ActionSelectionSystem component
            ActionSelectionSystem actionSystem = agent.GetComponent<ActionSelectionSystem>();
            if (actionSystem != null)
            {
                // Clear available actions and reset to default
                actionSystem.ClearAvailableActions();
                Debug.Log($"*** ACTION SYSTEM: Cleared actions for {agentId} (left all proximity zones) ***");
            }
            else
            {
                // Fallback to old color reset
                ResetAgentColor(agent);
                Debug.Log($"*** FALLBACK RESET COLOR: {agentId} color reset to default ***");
            }
        }
        else
        {
            Debug.LogWarning($"*** AGENT NOT FOUND: Could not find agent for ID {agentId} ***");
        }
    }
    
    GameObject FindAgentById(string agentId)
    {
        Debug.Log($"*** FINDING AGENT BY ID: {agentId} ***");
        
        // Try to find agent by ID in registry
        if (agentRegistry.ContainsKey(agentId))
        {
            Debug.Log($"*** FOUND IN REGISTRY: {agentId} -> {agentRegistry[agentId].name} ***");
            return agentRegistry[agentId];
        }
        
        // Fallback: search all GameObjects
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        foreach (GameObject obj in allObjects)
        {
            if (IsAgent(obj.name))
            {
                string objAgentId = ExtractAgentId(obj.name);
                Debug.Log($"*** CHECKING AGENT: {obj.name} -> ID: {objAgentId} ***");
                if (objAgentId == agentId)
                {
                    Debug.Log($"*** FOUND MATCHING AGENT: {obj.name} for ID {agentId} ***");
                    return obj;
                }
            }
        }
        
        Debug.LogWarning($"*** NO AGENT FOUND for ID: {agentId} ***");
        return null;
    }
    
    void ApplyColorChange(GameObject agent, ColorChangeEffect effect)
    {
        Debug.Log($"*** APPLY COLOR CHANGE CALLED for {agent.name} ***");
        
        // SIMPLIFIED: Direct color change based on zone action type
        string currentZoneId = GetCurrentProximityZone(agent);
        Debug.Log($"*** CURRENT ZONE ID: {currentZoneId} for {agent.name} ***");
        
        if (!string.IsNullOrEmpty(currentZoneId))
        {
            ProximityZone zone = GetProximityZoneById(currentZoneId);
            Debug.Log($"*** ZONE FOUND: {zone != null} for zone {currentZoneId} ***");
            
            if (zone != null)
            {
                Debug.Log($"*** ZONE AVAILABLE ACTIONS: {zone.availableActions?.Length ?? 0} actions ***");
                
                // Select random action from zone
                ActionData selectedAction = SelectRandomActionFromZone(zone);
                Debug.Log($"*** SELECTED ACTION: {selectedAction?.actionName ?? "NULL"} ({selectedAction?.actionType}) ***");
                
                if (selectedAction != null)
                {
                    // Apply color based on action type
                    Color actionColor = GetColorFromAction(selectedAction);
                    Debug.Log($"*** ACTION COLOR: {actionColor} for action {selectedAction.actionName} ({selectedAction.actionType}) ***");
                    
                    ApplyFallbackColor(agent, actionColor);
                    
                    // Log the action
                    string agentId = ExtractAgentId(agent.name);
                    LogAgentAction(agentId, zone.zoneId, selectedAction);
                    
                    Debug.Log($"*** ACTION COLOR APPLIED: {agent.name} -> {actionColor} for action {selectedAction.actionName} ({selectedAction.actionType}) ***");
                }
                else
                {
                    // Fallback to zone-based color
                    Color zoneColor = GetFallbackColorForZone(zone);
                    Debug.Log($"*** FALLBACK ZONE COLOR: {zoneColor} for zone {currentZoneId} ***");
                    ApplyFallbackColor(agent, zoneColor);
                    Debug.Log($"*** ZONE COLOR APPLIED: {agent.name} -> {zoneColor} for zone {currentZoneId} ***");
                }
            }
            else
            {
                // Ultimate fallback to green
                Debug.LogWarning($"*** ZONE NOT FOUND: {currentZoneId} ***");
                ApplyFallbackColor(agent, Color.green);
                Debug.Log($"*** ULTIMATE FALLBACK: {agent.name} to green (zone not found) ***");
            }
        }
        else
        {
            // Ultimate fallback to green
            Debug.LogWarning($"*** NO ZONE ID for {agent.name} ***");
            ApplyFallbackColor(agent, Color.green);
            Debug.Log($"*** ULTIMATE FALLBACK: {agent.name} to green (no zone) ***");
        }
    }
    
    string GetCurrentProximityZone(GameObject agent)
    {
        string agentId = ExtractAgentId(agent.name);
        Debug.Log($"*** GET CURRENT PROXIMITY ZONE: Agent ID = {agentId} for {agent.name} ***");
        
        if (string.IsNullOrEmpty(agentId)) 
        {
            Debug.LogWarning($"*** NO AGENT ID FOUND for {agent.name} ***");
            return "";
        }
        
        // Find which proximity zone this agent is currently in
        foreach (var zone in config.proximityZones)
        {
            if (zone != null)
            {
                bool hasZone = agentsInProximity.ContainsKey(zone.zoneId);
                bool agentInZone = hasZone && agentsInProximity[zone.zoneId].Contains(agentId);
                
                Debug.Log($"*** CHECKING ZONE {zone.zoneId}: HasZone={hasZone}, AgentInZone={agentInZone} ***");
                
                if (agentInZone)
                {
                    Debug.Log($"*** FOUND AGENT {agentId} IN ZONE {zone.zoneId} ***");
                    return zone.zoneId;
                }
            }
        }
        
        Debug.LogWarning($"*** NO PROXIMITY ZONE FOUND for agent {agentId} ***");
        return "";
    }
    
    void ApplyDirectionChange(GameObject agent, DirectionChangeEffect effect, Vector3 centerPosition)
    {
        // DISABLED: Direction changes removed - agents should not reflect when hitting proximity
        Debug.Log($"🚫 Direction changes disabled for {agent.name} - proximity reflection removed");
        return;
        
        // Check cooldown to prevent constant direction changes
        string agentKey = agent.name;
        if (lastDirectionChangeTime.ContainsKey(agentKey) && 
            Time.time - lastDirectionChangeTime[agentKey] < DIRECTION_CHANGE_COOLDOWN)
        {
            Debug.Log($"⏰ Direction change cooldown active for {agent.name}");
            return;
        }
        
        // Skip direction change if agent is moving too slowly (avoid disrupting normal movement)
        Rigidbody rb = agent.GetComponent<Rigidbody>();
        if (rb != null && rb.linearVelocity.magnitude < 8f) // Higher threshold for fast agents
        {
            Debug.Log($"⚠️ Skipping direction change for {agent.name} - too slow ({rb.linearVelocity.magnitude})");
            return;
        }
        
        // Update cooldown timer
        lastDirectionChangeTime[agentKey] = Time.time;
        
        // Calculate avoidance direction
        Vector3 avoidanceDirection = (agent.transform.position - centerPosition).normalized;
        
        // Apply avoidance force - make it much gentler to avoid disrupting normal movement
        if (rb != null)
        {
            // Use extremely small force to avoid disrupting normal movement
            Vector3 gentleForce = avoidanceDirection * effect.avoidanceForce * 0.0001f; // Further reduced by 99.99%
            rb.AddForce(gentleForce, ForceMode.Force);
            
            // Ensure the agent maintains its original speed by restoring velocity if it gets too slow
            if (rb.linearVelocity.magnitude < 12f) // Increased threshold for high-speed agents
            {
                // Restore movement speed by applying a gentle push in the current direction
                Vector3 currentDirection = rb.linearVelocity.normalized;
                if (currentDirection.magnitude < 0.1f)
                {
                    // If velocity is too small, use the avoidance direction as fallback
                    currentDirection = avoidanceDirection;
                }
                Vector3 speedRestore = currentDirection * 20f; // Much higher restore speed for fast agents
                speedRestore.y = rb.linearVelocity.y; // Preserve gravity
                rb.linearVelocity = speedRestore;
                Debug.Log($"🚀 Speed restored for {agent.name}: {rb.linearVelocity.magnitude}");
            }
        }
        else
        {
            // If no rigidbody, move transform directly - also gentler
            Vector3 gentleMovement = avoidanceDirection * effect.avoidanceForce * 0.01f * Time.deltaTime;
            agent.transform.position += gentleMovement;
        }
        
        // Apply rotation - also gentler
        float gentleRotation = effect.rotationSpeed * 0.01f * Time.deltaTime; // Reduced by 99%
        agent.transform.Rotate(0, gentleRotation, 0);
    }
    
    void ApplyStrongDirectionChange(GameObject agent, DirectionChangeEffect effect, Vector3 centerPosition)
    {
        // DISABLED: Strong direction changes removed - agents should not reflect when hitting proximity
        Debug.Log($"🚫 Strong direction changes disabled for {agent.name} - proximity reflection removed");
        return;
        
        // All code below is disabled - commented out to prevent compilation errors
        // Original code that used avoidanceDirection:
        // Vector3 avoidanceDirection = (agent.transform.position - centerPosition).normalized;
        // Rigidbody rb = agent.GetComponent<Rigidbody>();
        // if (rb != null)
        // {
        //     Vector3 gentleForce = avoidanceDirection * effect.avoidanceForce * 0.001f;
        //     rb.AddForce(gentleForce, ForceMode.Force);
        //     if (rb.linearVelocity.magnitude < 15f)
        //     {
        //         Vector3 currentDirection = rb.linearVelocity.normalized;
        //         if (currentDirection.magnitude < 0.1f)
        //         {
        //             currentDirection = avoidanceDirection;
        //         }
        //         Vector3 speedRestore = currentDirection * 25f;
        //         speedRestore.y = rb.linearVelocity.y;
        //         rb.linearVelocity = speedRestore;
        //         Debug.Log($"🚀 Strong speed restored for {agent.name}: {rb.linearVelocity.magnitude}");
        //     }
        // }
        // else
        // {
        //     Vector3 moderateMovement = avoidanceDirection * effect.avoidanceForce * 0.5f * Time.deltaTime;
        //     agent.transform.position += moderateMovement;
        // }
        // float moderateRotation = effect.rotationSpeed * 0.5f * Time.deltaTime;
        // agent.transform.Rotate(0, moderateRotation, 0);
        // Debug.Log($"*** APPLIED MODERATE DIRECTION CHANGE to {agent.name}: direction={avoidanceDirection}, force={effect.avoidanceForce * 0.5f} ***");
    }
    
    void PlayProximitySound(string zoneId, SoundEffect effect)
    {
        if (audioSources.ContainsKey(zoneId))
        {
            AudioSource audioSource = audioSources[zoneId];
            if (!audioSource.isPlaying)
            {
                // Load and play sound clip
                AudioClip clip = Resources.Load<AudioClip>(effect.soundClip);
                if (clip != null)
                {
                    audioSource.clip = clip;
                    audioSource.volume = effect.volume;
                    audioSource.Play();
                }
            }
        }
    }
    
    void ResetAgentColor(GameObject agent)
    {
        Renderer renderer = agent.GetComponent<Renderer>();
        if (renderer != null)
        {
            // Try to find original color by agent ID
            string agentId = ExtractAgentId(agent.name);
            Debug.Log($"*** RESETTING COLOR for {agent.name} (ID: {agentId}) ***");
            
            if (!string.IsNullOrEmpty(agentId) && originalColors.ContainsKey(agentId))
            {
                Material newMaterial = new Material(renderer.material);
                newMaterial.color = originalColors[agentId];
                renderer.material = newMaterial;
                Debug.Log($"*** RESET SUCCESS: {agent.name} color reset to original: {originalColors[agentId]} ***");
            }
            else
            {
                // Fallback to default colors based on agent type
                Color defaultColor = GetDefaultAgentColor(agent.name);
                Material newMaterial = new Material(renderer.material);
                newMaterial.color = defaultColor;
                renderer.material = newMaterial;
                Debug.Log($"*** RESET FALLBACK: {agent.name} color reset to default: {defaultColor} ***");
                
                // Store this as the original color for future reference
                if (!string.IsNullOrEmpty(agentId))
                {
                    originalColors[agentId] = defaultColor;
                    Debug.Log($"*** STORED DEFAULT COLOR: {agentId} -> {defaultColor} ***");
                }
            }
        }
        else
        {
            Debug.LogWarning($"*** NO RENDERER: Cannot reset color for {agent.name} ***");
        }
    }
    
    Color GetDefaultAgentColor(string agentName)
    {
        Debug.Log($"*** GETTING DEFAULT COLOR for: {agentName} ***");
        
        if (agentName.Contains("technician") || agentName.Contains("Technician"))
        {
            Debug.Log($"*** TECHNICIAN DETECTED: {agentName} -> Spring Green ***");
            return new Color(0f, 1f, 0.5f, 1f); // #00FF7F - Spring Green
        }
        else if (agentName.Contains("supervisor") || agentName.Contains("Supervisor"))
        {
            Debug.Log($"*** SUPERVISOR DETECTED: {agentName} -> Royal Blue ***");
            return new Color(0.25f, 0.41f, 0.88f, 1f); // #4169E1 - Royal Blue
        }
        else if (agentName.Contains("inspector") || agentName.Contains("Inspector"))
        {
            Debug.Log($"*** INSPECTOR DETECTED: {agentName} -> Peach ***");
            return new Color(1f, 0.76f, 0.61f, 1f); // #FFC29B - Peach/Salmon color
        }
        else
        {
            Debug.Log($"*** UNKNOWN AGENT TYPE: {agentName} -> White ***");
            return Color.white;
        }
    }
    
    Color GetProximityZoneColor(string zoneId)
    {
        // Return consistent red color for all proximity zones (fallback)
        return new Color(1f, 0.2f, 0.2f); // Consistent Red for all tools and workbench
    }
    
    ProximityZone GetProximityZoneById(string zoneId)
    {
        if (config?.proximityZones != null)
        {
            foreach (var zone in config.proximityZones)
            {
                if (zone != null && zone.zoneId == zoneId)
                {
                    return zone;
                }
            }
        }
        return null;
    }
    
    List<ActionSelectionSystem.ActionData> ConvertToActionSelectionData(ActionData[] jsonActions)
    {
        List<ActionSelectionSystem.ActionData> actions = new List<ActionSelectionSystem.ActionData>();
        
        Debug.Log($"*** CONVERTING {jsonActions.Length} JSON actions to ActionSelectionSystem actions ***");
        
        foreach (var jsonAction in jsonActions)
        {
            if (jsonAction != null)
            {
                ActionSelectionSystem.ActionData action = new ActionSelectionSystem.ActionData();
                action.actionId = jsonAction.actionId;
                action.actionName = jsonAction.actionName;
                action.zoneId = ""; // Will be set by the calling method
                
                // Convert action type string to enum
                switch (jsonAction.actionType.ToLower())
                {
                    case "positive":
                        action.actionType = ActionSelectionSystem.ActionType.Positive;
                        break;
                    case "neutral":
                        action.actionType = ActionSelectionSystem.ActionType.Neutral;
                        break;
                    case "negative":
                        action.actionType = ActionSelectionSystem.ActionType.Negative;
                        break;
                    default:
                        action.actionType = ActionSelectionSystem.ActionType.Neutral;
                        break;
                }
                
                // Convert color from JSON
                if (jsonAction.materialColor != null)
                {
                    action.actionColor = new Color(
                        jsonAction.materialColor.r,
                        jsonAction.materialColor.g,
                        jsonAction.materialColor.b,
                        jsonAction.materialColor.a
                    );
                    Debug.Log($"*** CONVERTED ACTION: {action.actionName} ({action.actionType}) with color {action.actionColor} ***");
                }
                else
                {
                    // Fallback colors based on action type
                    switch (action.actionType)
                    {
                        case ActionSelectionSystem.ActionType.Positive:
                            action.actionColor = Color.white;
                            break;
                        case ActionSelectionSystem.ActionType.Neutral:
                            action.actionColor = Color.gray;
                            break;
                        case ActionSelectionSystem.ActionType.Negative:
                            action.actionColor = Color.black;
                            break;
                    }
                    Debug.Log($"*** CONVERTED ACTION: {action.actionName} ({action.actionType}) with fallback color {action.actionColor} ***");
                }
                
                actions.Add(action);
            }
        }
        
        Debug.Log($"*** CONVERSION COMPLETE: {actions.Count} actions converted ***");
        return actions;
    }
    
    Color GetFallbackColorForZone(ProximityZone zone)
    {
        if (zone.actionType != null)
        {
            switch (zone.actionType.ToLower())
            {
                case "positive":
                    return Color.white;
                case "neutral":
                    return Color.gray;
                case "negative":
                    return Color.black;
                default:
                    return Color.green; // Default fallback to green
            }
        }
        return Color.green; // Default fallback to green
    }
    
    void ApplyFallbackColor(GameObject agent, Color color)
    {
        Debug.Log($"*** APPLY FALLBACK COLOR: {agent.name} -> {color} ***");
        
        Renderer renderer = agent.GetComponent<Renderer>();
        if (renderer != null)
        {
            Debug.Log($"*** RENDERER FOUND: {agent.name} has renderer ***");
            
            // Force create a new material to ensure color change
            Material newMaterial = new Material(Shader.Find("Unlit/Color"));
            if (newMaterial.shader == null)
            {
                newMaterial = new Material(Shader.Find("Legacy Shaders/Diffuse"));
            }
            if (newMaterial.shader == null)
            {
                newMaterial = new Material(Shader.Find("Sprites/Default"));
            }
            
            newMaterial.color = color;
            renderer.material = newMaterial;
            
            Debug.Log($"*** FALLBACK COLOR APPLIED: {agent.name} -> {color} (Material: {newMaterial.shader.name}) ***");
        }
        else
        {
            Debug.LogWarning($"*** NO RENDERER: {agent.name} has no renderer component ***");
        }
    }
    
    // NEW: Handle agent boundary hit with action assignment
    void HandleAgentBoundaryHit(GameObject agent, ProximityZone zone)
    {
        if (agent == null || zone == null) 
        {
            Debug.LogWarning($"*** BOUNDARY HIT: Agent or zone is null ***");
            return;
        }
        
        Debug.Log($"*** BOUNDARY HIT: Processing {agent.name} in zone {zone.zoneId} ***");
        
        // Get agent ID
        string agentId = ExtractAgentId(agent.name);
        if (string.IsNullOrEmpty(agentId))
        {
            Debug.LogWarning($"*** BOUNDARY HIT: Could not extract agent ID from {agent.name} ***");
            return;
        }
        
        Debug.Log($"*** BOUNDARY HIT: Agent {agentId} hit boundary of {zone.zoneId} ***");
        
        // Check if zone has available actions
        if (zone.availableActions == null || zone.availableActions.Length == 0)
        {
            Debug.LogWarning($"*** BOUNDARY HIT: Zone {zone.zoneId} has no available actions ***");
            // Apply fallback color immediately
            Color zoneColor = GetFallbackColorForZone(zone);
            ApplyActionColorToAgent(agent, zoneColor);
            Debug.Log($"*** BOUNDARY HIT: Applied fallback color {zoneColor} to {agent.name} ***");
            return;
        }
        
        // Select random action from available actions
        ActionData selectedAction = SelectRandomActionFromZone(zone);
        if (selectedAction != null)
        {
            // Apply color based on action
            Color actionColor = GetColorFromAction(selectedAction);
            ApplyActionColorToAgent(agent, actionColor);
            
            // Store agent's current tool/workbench
            StoreAgentCurrentTool(agentId, zone.zoneId, selectedAction);
            
            // Log the action
            LogAgentAction(agentId, zone.zoneId, selectedAction);
        }
        else
        {
            // Fallback to zone-based color
            Color zoneColor = GetFallbackColorForZone(zone);
            ApplyActionColorToAgent(agent, zoneColor);
            
            Debug.Log($"*** BOUNDARY HIT: Agent {agentId} using fallback color {zoneColor} for zone {zone.zoneId} ***");
        }
    }
    
    // Select random action from zone's available actions
    ActionData SelectRandomActionFromZone(ProximityZone zone)
    {
        if (zone.availableActions == null || zone.availableActions.Length == 0)
        {
            Debug.LogWarning($"*** NO ACTIONS AVAILABLE for zone {zone.zoneId} ***");
            return null;
        }
        
        // Select random action
        int randomIndex = UnityEngine.Random.Range(0, zone.availableActions.Length);
        ActionData selectedAction = zone.availableActions[randomIndex];
        
        Debug.Log($"*** SELECTED ACTION: {selectedAction.actionName} ({selectedAction.actionType}) from zone {zone.zoneId} ***");
        return selectedAction;
    }
    
    // Get color from action
    Color GetColorFromAction(ActionData action)
    {
        if (action.materialColor != null)
        {
            return new Color(action.materialColor.r, action.materialColor.g, action.materialColor.b, action.materialColor.a);
        }
        
        // Fallback colors based on action type
        switch (action.actionType.ToLower())
        {
            case "positive":
                return Color.white;
            case "neutral":
                return Color.gray;
            case "negative":
                return Color.black;
            default:
                return Color.green;
        }
    }
    
    // Apply action color to agent
    void ApplyActionColorToAgent(GameObject agent, Color color)
    {
        Renderer renderer = agent.GetComponent<Renderer>();
        if (renderer != null)
        {
            // Force create a new material to ensure color change
            Material newMaterial = new Material(Shader.Find("Unlit/Color"));
            if (newMaterial.shader == null)
            {
                newMaterial = new Material(Shader.Find("Legacy Shaders/Diffuse"));
            }
            if (newMaterial.shader == null)
            {
                newMaterial = new Material(Shader.Find("Sprites/Default"));
            }
            
            newMaterial.color = color;
            renderer.material = newMaterial;
            
            Debug.Log($"*** ACTION COLOR APPLIED: {agent.name} -> {color} (Material: {newMaterial.shader.name}) ***");
        }
        else
        {
            Debug.LogWarning($"*** NO RENDERER FOUND on {agent.name} ***");
        }
    }
    
    // Store agent's current tool/workbench
    void StoreAgentCurrentTool(string agentId, string zoneId, ActionData action)
    {
        // Create a simple storage system
        if (!agentCurrentTools.ContainsKey(agentId))
        {
            agentCurrentTools[agentId] = new AgentToolMemory();
        }
        
        agentCurrentTools[agentId].currentZoneId = zoneId;
        agentCurrentTools[agentId].currentAction = action;
        agentCurrentTools[agentId].lastUpdateTime = Time.time;
        
        Debug.Log($"*** STORED: Agent {agentId} is now working with {zoneId} doing {action.actionName} ***");
    }
    
    // Log agent action
    void LogAgentAction(string agentId, string zoneId, ActionData action)
    {
        string toolName = GetToolNameFromZoneId(zoneId);
        Debug.Log($"🎯 AGENT ACTION: {agentId} is {action.actionName} ({action.actionType}) on {toolName} - Color: {GetColorFromAction(action)}");
    }
    
    // Get tool name from zone ID
    string GetToolNameFromZoneId(string zoneId)
    {
        switch (zoneId)
        {
            case "tool_001_proximity":
                return "Industrial Motor Unit";
            case "tool_002_proximity":
                return "Secure Toolbox Alpha";
            case "tool_003_proximity":
                return "Hydraulic Lift Station";
            case "tool_004_proximity":
                return "Safety Inspection Station";
            case "workbench_001_proximity":
                return "Main Assembly Workbench";
            default:
                return zoneId;
        }
    }
    
    // Agent tool memory class
    [System.Serializable]
    public class AgentToolMemory
    {
        public string currentZoneId;
        public ActionData currentAction;
        public float lastUpdateTime;
    }
    
    // Dictionary to store agent's current tool
    private Dictionary<string, AgentToolMemory> agentCurrentTools = new Dictionary<string, AgentToolMemory>();
    
    // TEST FUNCTION: Force color change for testing
    [ContextMenu("Test Color Change")]
    public void TestColorChange()
    {
        Debug.Log("*** TESTING COLOR CHANGE ***");
        
        // Find all agents
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        foreach (GameObject obj in allObjects)
        {
            if (obj.name.Contains("SIMPLE_Technician") || obj.name.Contains("SIMPLE_Supervisor"))
            {
                Debug.Log($"*** TESTING: Found agent {obj.name} ***");
                
                // Test with different colors
                Color[] testColors = { Color.white, Color.gray, Color.black, Color.red, Color.blue };
                Color testColor = testColors[UnityEngine.Random.Range(0, testColors.Length)];
                
                ApplyActionColorToAgent(obj, testColor);
                Debug.Log($"*** TEST: Applied {testColor} to {obj.name} ***");
            }
        }
    }
    
    // TEST FUNCTION: Force boundary hit for testing
    [ContextMenu("Test Boundary Hit")]
    public void TestBoundaryHit()
    {
        Debug.Log("*** TESTING BOUNDARY HIT ***");
        
        // Find all agents
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        foreach (GameObject obj in allObjects)
        {
            if (obj.name.Contains("SIMPLE_Technician") || obj.name.Contains("SIMPLE_Supervisor"))
            {
                Debug.Log($"*** TESTING: Found agent {obj.name} ***");
                
                // Find first available zone
                if (config?.proximityZones != null && config.proximityZones.Length > 0)
                {
                    ProximityZone testZone = config.proximityZones[0];
                    Debug.Log($"*** TESTING: Using zone {testZone.zoneId} ***");
                    
                    HandleAgentBoundaryHit(obj, testZone);
                }
            }
        }
    }
    
    // SIMPLE TEST: Force color change without boundary detection
    [ContextMenu("Force Color Change")]
    public void ForceColorChange()
    {
        Debug.Log("*** FORCING COLOR CHANGE ***");
        
        // Find all agents
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        foreach (GameObject obj in allObjects)
        {
            if (obj.name.Contains("SIMPLE_Technician") || obj.name.Contains("SIMPLE_Supervisor"))
            {
                Debug.Log($"*** FORCING: Found agent {obj.name} ***");
                
                // Force apply white color
                ApplyActionColorToAgent(obj, Color.white);
                Debug.Log($"*** FORCED: Applied WHITE to {obj.name} ***");
            }
        }
    }
    
    // SIMPLE TEST: Check if agents exist
    [ContextMenu("Check Agents")]
    public void CheckAgents()
    {
        Debug.Log("*** CHECKING AGENTS ***");
        
        // Find all objects
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        Debug.Log($"*** TOTAL OBJECTS: {allObjects.Length} ***");
        
        int agentCount = 0;
        foreach (GameObject obj in allObjects)
        {
            if (obj.name.Contains("SIMPLE_Technician") || obj.name.Contains("SIMPLE_Supervisor"))
            {
                agentCount++;
                Debug.Log($"*** AGENT {agentCount}: {obj.name} at {obj.transform.position} ***");
                
                // Check renderer
                Renderer renderer = obj.GetComponent<Renderer>();
                if (renderer != null)
                {
                    Debug.Log($"*** RENDERER: {obj.name} has renderer with material {renderer.material.name} color {renderer.material.color} ***");
                }
                else
                {
                    Debug.LogWarning($"*** NO RENDERER: {obj.name} has no renderer ***");
                }
            }
        }
        
        Debug.Log($"*** TOTAL AGENTS FOUND: {agentCount} ***");
    }
    
    // ULTRA SIMPLE TEST: Direct color change
    [ContextMenu("Ultra Simple Color Test")]
    public void UltraSimpleColorTest()
    {
        Debug.Log("*** ULTRA SIMPLE COLOR TEST ***");
        
        // Find all objects
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            if (obj.name.Contains("SIMPLE_Technician") || obj.name.Contains("SIMPLE_Supervisor"))
            {
                Debug.Log($"*** TESTING: {obj.name} ***");
                
                // Get renderer
                Renderer renderer = obj.GetComponent<Renderer>();
                if (renderer != null)
                {
                    Debug.Log($"*** BEFORE: {obj.name} color = {renderer.material.color} ***");
                    
                    // Create new material
                    Material newMaterial = new Material(Shader.Find("Unlit/Color"));
                    if (newMaterial.shader == null)
                    {
                        newMaterial = new Material(Shader.Find("Legacy Shaders/Diffuse"));
                    }
                    if (newMaterial.shader == null)
                    {
                        newMaterial = new Material(Shader.Find("Sprites/Default"));
                    }
                    
                    // Set color to red
                    newMaterial.color = Color.red;
                    renderer.material = newMaterial;
                    
                    Debug.Log($"*** AFTER: {obj.name} color = {renderer.material.color} (Material: {newMaterial.shader.name}) ***");
                }
                else
                {
                    Debug.LogWarning($"*** NO RENDERER: {obj.name} ***");
                }
            }
        }
    }
    
    // TEST: Check JSON configuration
    [ContextMenu("Check JSON Config")]
    public void CheckJSONConfig()
    {
        Debug.Log("*** CHECKING JSON CONFIG ***");
        
        if (config == null)
        {
            Debug.LogWarning("*** CONFIG IS NULL ***");
            return;
        }
        
        Debug.Log($"*** CONFIG LOADED: {config != null} ***");
        Debug.Log($"*** PROXIMITY ZONES: {config.proximityZones?.Length ?? 0} ***");
        
        if (config.proximityZones != null)
        {
            for (int i = 0; i < config.proximityZones.Length; i++)
            {
                var zone = config.proximityZones[i];
                Debug.Log($"*** ZONE {i}: {zone.zoneId} - Actions: {zone.availableActions?.Length ?? 0} ***");
                
                if (zone.availableActions != null)
                {
                    for (int j = 0; j < zone.availableActions.Length; j++)
                    {
                        var action = zone.availableActions[j];
                        Debug.Log($"***   ACTION {j}: {action.actionName} ({action.actionType}) - Color: {action.materialColor} ***");
                    }
                }
            }
        }
    }
    
    // Debug visualization
    void OnDrawGizmosSelected()
    {
        if (!showDebugGizmos || config.proximityZones == null) return;
        
        foreach (var zone in config.proximityZones)
        {
            if (TryResolveCenterGameObject(zone.centerObject, out GameObject centerGo))
            {
                Vector3 centerPosition = centerGo.transform.position;
                centerPosition.y = 0.1f; // Slightly above ground
                
                // Set different colors for different zones
                Color zoneColor = GetZoneColorForObject(zone.centerObject);
                Gizmos.color = zoneColor;
                
                // Draw wireframe circle
                Gizmos.DrawWireSphere(centerPosition, zone.radius);
                
                // Draw filled circle
                Gizmos.color = new Color(zoneColor.r, zoneColor.g, zoneColor.b, 0.2f);
                Gizmos.DrawSphere(centerPosition, zone.radius);
            }
        }
    }
    
    void OnDrawGizmos()
    {
        if (!showDebugGizmos || objectRegistry == null) return;
        
        // Draw zones for all registered objects
        foreach (var kvp in objectRegistry)
        {
            string objectId = kvp.Key;
            GameObject obj = kvp.Value;
            
            Vector3 centerPosition = obj.transform.position;
            centerPosition.y = 0.1f; // Slightly above ground
            
            // Get actual radius
            float actualRadius = GetProximityRadiusForObject(objectId);
            
            // Set different colors for different objects
            Color zoneColor = GetZoneColorForObject(objectId);
            Gizmos.color = zoneColor;
            
            // Draw wireframe circle with actual radius
            Gizmos.DrawWireSphere(centerPosition, actualRadius);
            
            // Draw filled circle with actual radius
            Gizmos.color = new Color(zoneColor.r, zoneColor.g, zoneColor.b, 0.2f);
            Gizmos.DrawSphere(centerPosition, actualRadius);
        }
    }
    
    // Public methods for external control
    public void EnableProximityDetection()
    {
        if (config != null)
        {
            config.enabled = true;
            Debug.Log("Proximity detection enabled");
        }
    }
    
    public void DisableProximityDetection()
    {
        if (config != null)
        {
            config.enabled = false;
            Debug.Log("Proximity detection disabled");
        }
    }
    
    public void DisableDirectionEffects()
    {
        if (config != null && config.proximityZones != null)
        {
            foreach (var zone in config.proximityZones)
            {
                if (zone != null && zone.effects != null && zone.effects.directionChange != null)
                {
                    zone.effects.directionChange.enabled = false;
                }
            }
            Debug.Log("Direction effects disabled - agents can move normally");
        }
    }
    
    public void EnableDirectionEffects()
    {
        if (config != null && config.proximityZones != null)
        {
            foreach (var zone in config.proximityZones)
            {
                if (zone != null && zone.effects != null && zone.effects.directionChange != null)
                {
                    zone.effects.directionChange.enabled = true;
                }
            }
            Debug.Log("Direction effects enabled");
        }
    }
    
    public void SetProximityRadius(string zoneId, float newRadius)
    {
        var zone = config.proximityZones.FirstOrDefault(z => z.zoneId == zoneId);
        if (zone != null)
        {
            zone.radius = newRadius;
        }
    }
    
    public void ResetAllAgentColors()
    {
        foreach (var agent in agentRegistry.Values)
        {
            ResetAgentColor(agent);
        }
    }
    
    void CreateVisualProximityZones()
    {
        Debug.Log($"*** CREATING VISUAL ZONES: showVisualZones={showVisualZones}, zones count={config.proximityZones?.Length ?? 0} ***");
        
        if (!showVisualZones || config.proximityZones == null) 
        {
            Debug.LogWarning("*** VISUAL ZONES DISABLED OR NO CONFIG ***");
            return;
        }
        
        Debug.Log("*** Creating visual proximity zones... ***");
        
        foreach (var zone in config.proximityZones)
        {
            if (zone == null) 
            {
                Debug.LogWarning("*** NULL ZONE FOUND ***");
                continue;
            }
            
            Debug.Log($"*** Processing zone: {zone.zoneId}, centerObject: {zone.centerObject} ***");
            
            if (!TryResolveCenterGameObject(zone.centerObject, out GameObject centerObject))
            {
                Debug.LogWarning($"*** CENTER OBJECT NOT FOUND (tools/stations or Agent_*): {zone.centerObject} ***");
                Debug.Log($"*** Objects: {string.Join(", ", objectRegistry.Keys)} | Agents: {string.Join(", ", agentRegistry.Keys)} ***");
                continue;
            }
            
            Debug.Log($"*** Creating visual zone for {zone.zoneId} at {centerObject.transform.position} ***");
            
            // Create visual zone GameObject
            GameObject visualZone = new GameObject($"VisualZone_{zone.zoneId}");
            visualZone.transform.position = centerObject.transform.position;
            visualZone.transform.position = new Vector3(visualZone.transform.position.x, 0.1f, visualZone.transform.position.z); // Higher above ground
            
            // Create cylinder mesh for the zone
            GameObject zoneCylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            zoneCylinder.transform.SetParent(visualZone.transform);
            zoneCylinder.transform.localPosition = Vector3.zero;
            zoneCylinder.transform.localScale = new Vector3(zone.radius * 2, 0.2f, zone.radius * 2); // Slightly thicker cylinder
            
            // Remove collider to avoid interference
            Collider collider = zoneCylinder.GetComponent<Collider>();
            if (collider != null)
            {
                DestroyObjectSafe(collider);
            }
            
            // Apply material and color
            Renderer renderer = zoneCylinder.GetComponent<Renderer>();
            if (renderer != null)
            {
                // Create a semi-transparent material
                Material zoneMat = new Material(Shader.Find("Standard"));
                zoneMat.SetFloat("_Mode", 3); // Transparent mode
                zoneMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                zoneMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                zoneMat.SetInt("_ZWrite", 0);
                zoneMat.DisableKeyword("_ALPHATEST_ON");
                zoneMat.EnableKeyword("_ALPHABLEND_ON");
                zoneMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                zoneMat.renderQueue = 3000;
                
                // Set light blue color with transparency (like in the reference image)
                Color zoneColor = new Color(0.5f, 0.8f, 1.0f, 0.4f); // Slightly more opaque
                zoneMat.color = zoneColor;
                
                renderer.material = zoneMat;
                
                Debug.Log($"*** APPLIED MATERIAL: {zoneColor} to {zone.zoneId} ***");
            }
            else
            {
                Debug.LogWarning($"*** NO RENDERER FOUND for {zone.zoneId} ***");
            }
            
            visualZoneObjects[zone.zoneId] = visualZone;
            Debug.Log($"*** SUCCESS: Created visual zone for {zone.zoneId} ***");
        }
        
        Debug.Log($"*** COMPLETE: Created {visualZoneObjects.Count} visual proximity zones ***");
        
        // Force refresh the scene view
        #if UNITY_EDITOR
        UnityEditor.SceneView.RepaintAll();
        #endif
    }
    
    /// <summary>Enable or disable proximity disc meshes without toggling (unlike <see cref="ToggleVisualZones"/>).</summary>
    public void SetShowVisualZones(bool enabled)
    {
        showVisualZones = enabled;
        foreach (var visualZone in visualZoneObjects.Values)
        {
            if (visualZone != null)
                visualZone.SetActive(enabled);
        }
    }
    
    public void ToggleVisualZones()
    {
        showVisualZones = !showVisualZones;
        
        foreach (var visualZone in visualZoneObjects.Values)
        {
            if (visualZone != null)
            {
                visualZone.SetActive(showVisualZones);
            }
        }
        
        Debug.Log($"Visual zones {(showVisualZones ? "enabled" : "disabled")}");
    }
    
    [ContextMenu("Force Create Visual Zones")]
    public void ForceCreateVisualZones()
    {
        Debug.Log("*** FORCE CREATING VISUAL ZONES ***");
        
        // Clear existing visual zones
        foreach (var visualZone in visualZoneObjects.Values)
        {
            if (visualZone != null)
            {
                DestroyObjectSafe(visualZone);
            }
        }
        visualZoneObjects.Clear();
        
        // Force enable visual zones
        showVisualZones = true;
        
        // Recreate visual zones
        CreateVisualProximityZones();
    }
    
    [ContextMenu("Stop All Color Tests")]
    public void StopAllColorTests()
    {
        Debug.Log("*** STOPPING ALL COLOR TESTS ***");
        
        // Find and disable ForceColorTest components
        ForceColorTest[] forceColorTests = FindObjectsOfType<ForceColorTest>();
        foreach (var test in forceColorTests)
        {
            test.enableForceTest = false;
            Debug.Log($"Disabled ForceColorTest on {test.gameObject.name}");
        }
        
        // Reset all agent colors to original
        ResetAllAgentColors();
        
        Debug.Log("*** ALL COLOR TESTS STOPPED ***");
    }
    
    [ContextMenu("Force Reset All Agents to Default Colors")]
    public void ForceResetAllAgentsToDefaultColors()
    {
        Debug.Log("*** FORCE RESETTING ALL AGENTS TO DEFAULT COLORS ***");
        
        // Find all agent objects in the scene
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            if (IsAgent(obj.name))
            {
                Debug.Log($"*** RESETTING AGENT: {obj.name} ***");
                
                Renderer renderer = obj.GetComponent<Renderer>();
                if (renderer != null)
                {
                    // Get the correct default color for this agent
                    Color defaultColor = GetDefaultAgentColor(obj.name);
                    
                    // Apply the default color
                    Material newMaterial = new Material(renderer.material);
                    newMaterial.color = defaultColor;
                    renderer.material = newMaterial;
                    
                    // Store this as the original color
                    string agentId = ExtractAgentId(obj.name);
                    if (!string.IsNullOrEmpty(agentId))
                    {
                        originalColors[agentId] = defaultColor;
                    }
                    
                    Debug.Log($"*** RESET SUCCESS: {obj.name} -> {defaultColor} ***");
                }
                else
                {
                    Debug.LogWarning($"*** NO RENDERER: {obj.name} ***");
                }
            }
        }
        
        Debug.Log("*** ALL AGENTS RESET TO DEFAULT COLORS ***");
    }
    
    [ContextMenu("Disable All Color Test Scripts")]
    public void DisableAllColorTestScripts()
    {
        Debug.Log("*** DISABLING ALL COLOR TEST SCRIPTS ***");
        
        // Disable ForceColorTest components
        ForceColorTest[] forceColorTests = FindObjectsOfType<ForceColorTest>();
        foreach (var test in forceColorTests)
        {
            test.enableForceTest = false;
            test.enabled = false; // Also disable the component entirely
            Debug.Log($"Disabled ForceColorTest on {test.gameObject.name}");
        }
        
        // Disable ProximityColorTest components
        ProximityColorTest[] proximityColorTests = FindObjectsOfType<ProximityColorTest>();
        foreach (var test in proximityColorTests)
        {
            if (test != null)
            {
                test.enabled = false;
                Debug.Log($"Disabled ProximityColorTest on {test.gameObject.name}");
            }
        }
        
        // Disable AgentColorResetTest components
        AgentColorResetTest[] agentColorResetTests = FindObjectsOfType<AgentColorResetTest>();
        foreach (var test in agentColorResetTests)
        {
            if (test != null)
            {
                test.enabled = false;
                Debug.Log($"Disabled AgentColorResetTest on {test.gameObject.name}");
            }
        }
        
        Debug.Log("*** ALL COLOR TEST SCRIPTS DISABLED ***");
    }
    
    [ContextMenu("Fix SIMPLE Agents")]
    public void FixSIMPLEAgents()
    {
        Debug.Log("*** FIXING SIMPLE AGENTS ***");
        
        // Find all SIMPLE agents
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            if (obj.name.Contains("SIMPLE_"))
            {
                Debug.Log($"*** FOUND SIMPLE AGENT: {obj.name} ***");
                
                // Extract agent ID (now unique for each SIMPLE agent)
                string agentId = ExtractAgentId(obj.name);
                Debug.Log($"*** EXTRACTED ID: {agentId} for {obj.name} ***");
                
                // Get correct default color
                Color defaultColor = GetDefaultAgentColor(obj.name);
                Debug.Log($"*** DEFAULT COLOR: {defaultColor} for {obj.name} ***");
                
                // Apply the color
                Renderer renderer = obj.GetComponent<Renderer>();
                if (renderer != null)
                {
                    Material newMaterial = new Material(renderer.material);
                    newMaterial.color = defaultColor;
                    renderer.material = newMaterial;
                    
                    // Store in registry with unique ID
                    agentRegistry[agentId] = obj;
                    originalColors[agentId] = defaultColor;
                    
                    Debug.Log($"*** APPLIED COLOR: {defaultColor} to {obj.name} with ID {agentId} ***");
                }
                else
                {
                    Debug.LogWarning($"*** NO RENDERER: {obj.name} ***");
                }
            }
        }
        
        Debug.Log($"*** SIMPLE AGENTS FIXED - Registry now has {agentRegistry.Count} agents ***");
    }
    
    [ContextMenu("Clear Agent Registry")]
    public void ClearAgentRegistry()
    {
        Debug.Log("*** CLEARING AGENT REGISTRY ***");
        
        agentRegistry.Clear();
        originalColors.Clear();
        
        Debug.Log("*** AGENT REGISTRY CLEARED ***");
    }
    
    [ContextMenu("Reinitialize All Agents")]
    public void ReinitializeAllAgents()
    {
        Debug.Log("*** REINITIALIZING ALL AGENTS ***");
        
        // Clear existing registry
        agentRegistry.Clear();
        originalColors.Clear();
        
        // Reinitialize
        InitializeAgentRegistry();
        
        Debug.Log($"*** REINITIALIZED - Registry now has {agentRegistry.Count} agents ***");
    }
    
    [ContextMenu("Fix SIMPLE Agent Color Issues")]
    public void FixSIMPLEAgentColorIssues()
    {
        Debug.Log("*** FIXING SIMPLE AGENT COLOR ISSUES ***");
        
        // Step 1: Clear existing registry to avoid conflicts
        agentRegistry.Clear();
        originalColors.Clear();
        
        // Step 2: Find all SIMPLE agents and fix them individually
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            if (obj.name.Contains("SIMPLE_"))
            {
                Debug.Log($"*** PROCESSING SIMPLE AGENT: {obj.name} ***");
                
                // Get unique agent ID
                string agentId = ExtractAgentId(obj.name);
                Debug.Log($"*** UNIQUE ID: {agentId} ***");
                
                // Get correct default color based on role
                Color defaultColor = GetDefaultAgentColor(obj.name);
                Debug.Log($"*** DEFAULT COLOR: {defaultColor} ***");
                
                // Apply the default color immediately
                Renderer renderer = obj.GetComponent<Renderer>();
                if (renderer != null)
                {
                    Material newMaterial = new Material(renderer.material);
                    newMaterial.color = defaultColor;
                    renderer.material = newMaterial;
                    
                    // Store in registry with unique ID
                    agentRegistry[agentId] = obj;
                    originalColors[agentId] = defaultColor;
                    
                    Debug.Log($"*** FIXED: {obj.name} -> {agentId} -> {defaultColor} ***");
                }
                else
                {
                    Debug.LogWarning($"*** NO RENDERER FOUND: {obj.name} ***");
                }
            }
        }
        
        // Step 3: Disable any conflicting scripts
        DisableAllColorTestScripts();
        
        Debug.Log($"*** SIMPLE AGENT COLOR ISSUES FIXED - {agentRegistry.Count} agents registered ***");
    }
    
    [ContextMenu("Force Correct SIMPLE Agent Colors")]
    public void ForceCorrectSIMPLEAgentColors()
    {
        Debug.Log("*** FORCING CORRECT SIMPLE AGENT COLORS ***");
        
        // Step 1: Disable all conflicting scripts first
        DisableAllColorTestScripts();
        
        // Step 2: Find all SIMPLE agents and force correct colors
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            if (obj.name.Contains("SIMPLE_"))
            {
                Debug.Log($"*** FORCING COLOR FOR: {obj.name} ***");
                
                // Get correct default color
                Color correctColor = GetDefaultAgentColor(obj.name);
                Debug.Log($"*** CORRECT COLOR: {correctColor} ***");
                
                // Apply the color directly
                Renderer renderer = obj.GetComponent<Renderer>();
                if (renderer != null)
                {
                    // Create new material with correct color
                    Material newMaterial = new Material(Shader.Find("Unlit/Color"));
                    newMaterial.color = correctColor;
                    renderer.material = newMaterial;
                    
                    Debug.Log($"*** APPLIED CORRECT COLOR: {correctColor} to {obj.name} ***");
                }
                else
                {
                    Debug.LogWarning($"*** NO RENDERER: {obj.name} ***");
                }
            }
        }
        
        Debug.Log("*** CORRECT SIMPLE AGENT COLORS FORCED ***");
    }
    
    [ContextMenu("Debug Object Registry")]
    public void DebugObjectRegistry()
    {
        Debug.Log("*** DEBUGGING OBJECT REGISTRY ***");
        Debug.Log($"Registered objects count: {objectRegistry.Count}");
        
        foreach (var kvp in objectRegistry)
        {
            Debug.Log($"  {kvp.Key} -> {kvp.Value.name} at {kvp.Value.transform.position}");
        }
        
        Debug.Log("*** OBJECT REGISTRY DEBUG COMPLETE ***");
    }
    
    [ContextMenu("Create Zones for All Objects")]
    public void CreateZonesForAllObjects()
    {
        Debug.Log("*** CREATING ZONES FOR ALL OBJECTS ***");
        
        // Clear existing visual zones
        foreach (var visualZone in visualZoneObjects.Values)
        {
            if (visualZone != null)
            {
                DestroyObjectSafe(visualZone);
            }
        }
        visualZoneObjects.Clear();
        
        // Create zones for each registered object
        foreach (var kvp in objectRegistry)
        {
            string objectId = kvp.Key;
            GameObject obj = kvp.Value;
            
            Debug.Log($"*** Creating zone for {objectId} at {obj.transform.position} ***");
            
            CreateVisualZoneForObject(objectId, obj);
        }
        
        Debug.Log($"*** CREATED {visualZoneObjects.Count} VISUAL ZONES ***");
        
        // Force refresh the scene view
        #if UNITY_EDITOR
        UnityEditor.SceneView.RepaintAll();
        #endif
    }
    
    void CreateVisualZoneForObject(string objectId, GameObject centerObject)
    {
        Debug.Log($"*** CREATING VISUAL ZONE for {objectId} ***");
        
        // Get the actual radius from proximity config
        float actualRadius = GetProximityRadiusForObject(objectId);
        Debug.Log($"*** Using radius {actualRadius} for {objectId} ***");
        
        // Create visual zone GameObject
        GameObject visualZone = new GameObject($"VisualZone_{objectId}");
        visualZone.transform.position = centerObject.transform.position;
        visualZone.transform.position = new Vector3(visualZone.transform.position.x, 0.02f, visualZone.transform.position.z); // Very close to ground
        
        // Create cylinder mesh for the zone
        GameObject zoneCylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        zoneCylinder.transform.SetParent(visualZone.transform);
        zoneCylinder.transform.localPosition = Vector3.zero;
        zoneCylinder.transform.localScale = new Vector3(actualRadius * 2, 0.05f, actualRadius * 2); // Match actual radius
        
        // Remove collider
        Collider collider = zoneCylinder.GetComponent<Collider>();
        if (collider != null)
        {
            DestroyObjectSafe(collider);
        }
        
        // Apply material and color
        Renderer renderer = zoneCylinder.GetComponent<Renderer>();
        if (renderer != null)
        {
            // Create a transparent material that works in Game view
            Material zoneMat = new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse"));
            
            // Different colors for different objects
            Color zoneColor = GetZoneColorForObject(objectId);
            zoneMat.color = zoneColor;
            
            renderer.material = zoneMat;
            
            Debug.Log($"*** SUCCESS: Created zone for {objectId} with color {zoneColor} and radius {actualRadius} at {visualZone.transform.position} ***");
        }
        else
        {
            Debug.LogError($"*** ERROR: No renderer found for {objectId} ***");
        }
        
        visualZoneObjects[objectId] = visualZone;
    }
    
    float GetProximityRadiusForObject(string objectId)
    {
        EnsureRadiusLookupCache();
        if (_radiusByCenterObject != null && objectId != null &&
            _radiusByCenterObject.TryGetValue(objectId, out float r))
            return r;
        return 2.5f;
    }
    
    Color GetZoneColorForObject(string objectId)
    {
        // Return different colors for different objects - more opaque for Game view visibility
        switch (objectId)
        {
            case "tool_001": return new Color(1f, 0.2f, 0.2f, 0.8f); // Red - very opaque
            case "tool_002": return new Color(1f, 0.5f, 0f, 0.8f); // Orange - very opaque
            case "tool_003": return new Color(0.2f, 0.5f, 1f, 0.8f); // Blue - very opaque
            case "tool_004": return new Color(0.2f, 1f, 0.2f, 0.8f); // Green - very opaque
            case "workbench_001": return new Color(1f, 0.8f, 0.2f, 0.8f); // Yellow - very opaque
            default: return new Color(0.5f, 0.8f, 1f, 0.8f); // Light blue default - very opaque
        }
    }
    
    [ContextMenu("Create Game View Zones")]
    public void CreateGameViewZones()
    {
        Debug.Log("*** CREATING GAME VIEW ZONES ***");
        
        // Clear existing visual zones
        foreach (var visualZone in visualZoneObjects.Values)
        {
            if (visualZone != null)
            {
                DestroyObjectSafe(visualZone);
            }
        }
        visualZoneObjects.Clear();
        
        // Create zones optimized for Game view visibility
        foreach (var kvp in objectRegistry)
        {
            string objectId = kvp.Key;
            GameObject obj = kvp.Value;
            
            Debug.Log($"*** Creating Game view zone for {objectId} ***");
            
            // Get actual radius
            float actualRadius = GetProximityRadiusForObject(objectId);
            
            // Create visual zone GameObject
            GameObject visualZone = new GameObject($"GameViewZone_{objectId}");
            visualZone.transform.position = obj.transform.position;
            visualZone.transform.position = new Vector3(visualZone.transform.position.x, 0.05f, visualZone.transform.position.z);
            
            // Create cylinder mesh for the zone
            GameObject zoneCylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            zoneCylinder.transform.SetParent(visualZone.transform);
            zoneCylinder.transform.localPosition = Vector3.zero;
            zoneCylinder.transform.localScale = new Vector3(actualRadius * 2, 0.1f, actualRadius * 2); // Match actual radius
            
            // Remove collider
            Collider collider = zoneCylinder.GetComponent<Collider>();
            if (collider != null)
            {
                DestroyObjectSafe(collider);
            }
            
            // Apply material optimized for Game view
            Renderer renderer = zoneCylinder.GetComponent<Renderer>();
            if (renderer != null)
            {
                // Use Standard shader with transparency
                Material zoneMat = new Material(Shader.Find("Standard"));
                zoneMat.SetFloat("_Mode", 3); // Transparent mode
                zoneMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                zoneMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                zoneMat.SetInt("_ZWrite", 0);
                zoneMat.DisableKeyword("_ALPHATEST_ON");
                zoneMat.EnableKeyword("_ALPHABLEND_ON");
                zoneMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                zoneMat.renderQueue = 3000;
                
                // Different colors for different objects
                Color zoneColor = GetZoneColorForObject(objectId);
                zoneMat.color = zoneColor;
                
                renderer.material = zoneMat;
                
                Debug.Log($"*** Created Game view zone for {objectId} with color {zoneColor} and radius {actualRadius} ***");
            }
            
            visualZoneObjects[objectId] = visualZone;
        }
        
        Debug.Log($"*** CREATED {visualZoneObjects.Count} GAME VIEW ZONES ***");
        
        // Force refresh the scene view
        #if UNITY_EDITOR
        UnityEditor.SceneView.RepaintAll();
        #endif
    }
    
    [ContextMenu("Create Simple Visual Zones")]
    public void CreateSimpleVisualZones()
    {
        Debug.Log("*** CREATING SIMPLE VISUAL ZONES ***");
        
        // Clear existing visual zones
        foreach (var visualZone in visualZoneObjects.Values)
        {
            if (visualZone != null)
            {
                DestroyObjectSafe(visualZone);
            }
        }
        visualZoneObjects.Clear();
        
        // Create simple zones using basic shapes
        foreach (var kvp in objectRegistry)
        {
            string objectId = kvp.Key;
            GameObject obj = kvp.Value;
            
            Debug.Log($"*** Creating simple zone for {objectId} ***");
            
            // Get actual radius
            float actualRadius = GetProximityRadiusForObject(objectId);
            
            // Create a simple plane for the zone
            GameObject zonePlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            zonePlane.name = $"Zone_{objectId}";
            zonePlane.transform.position = obj.transform.position;
            zonePlane.transform.position = new Vector3(zonePlane.transform.position.x, 0.01f, zonePlane.transform.position.z);
            zonePlane.transform.localScale = new Vector3(actualRadius * 0.2f, 1f, actualRadius * 0.2f); // Scale based on actual radius
            
            // Remove collider
            Collider collider = zonePlane.GetComponent<Collider>();
            if (collider != null)
            {
                DestroyObjectSafe(collider);
            }
            
            // Apply material
            Renderer renderer = zonePlane.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material zoneMat = new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse"));
                Color zoneColor = GetZoneColorForObject(objectId);
                zoneMat.color = zoneColor;
                renderer.material = zoneMat;
                
                Debug.Log($"*** Created simple zone for {objectId} with color {zoneColor} and radius {actualRadius} ***");
            }
            
            visualZoneObjects[objectId] = zonePlane;
        }
        
        Debug.Log($"*** CREATED {visualZoneObjects.Count} SIMPLE VISUAL ZONES ***");
        
        // Force refresh the scene view
        #if UNITY_EDITOR
        UnityEditor.SceneView.RepaintAll();
        #endif
    }
    
    [ContextMenu("Create Wireframe Zones")]
    public void CreateWireframeZones()
    {
        Debug.Log("*** CREATING WIREFRAME ZONES ***");
        
        // Clear existing visual zones
        foreach (var visualZone in visualZoneObjects.Values)
        {
            if (visualZone != null)
            {
                DestroyObjectSafe(visualZone);
            }
        }
        visualZoneObjects.Clear();
        
        // Create wireframe zones using spheres
        foreach (var kvp in objectRegistry)
        {
            string objectId = kvp.Key;
            GameObject obj = kvp.Value;
            
            Debug.Log($"*** Creating wireframe zone for {objectId} ***");
            
            // Get actual radius
            float actualRadius = GetProximityRadiusForObject(objectId);
            
            // Create a sphere for the zone
            GameObject zoneSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            zoneSphere.name = $"WireframeZone_{objectId}";
            zoneSphere.transform.position = obj.transform.position;
            zoneSphere.transform.position = new Vector3(zoneSphere.transform.position.x, 0.5f, zoneSphere.transform.position.z);
            zoneSphere.transform.localScale = new Vector3(actualRadius * 2, 0.1f, actualRadius * 2); // Match actual radius
            
            // Remove collider
            Collider collider = zoneSphere.GetComponent<Collider>();
            if (collider != null)
            {
                DestroyObjectSafe(collider);
            }
            
            // Apply wireframe material
            Renderer renderer = zoneSphere.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material zoneMat = new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse"));
                Color zoneColor = GetZoneColorForObject(objectId);
                zoneMat.color = zoneColor;
                renderer.material = zoneMat;
                
                Debug.Log($"*** Created wireframe zone for {objectId} with color {zoneColor} and radius {actualRadius} ***");
            }
            
            visualZoneObjects[objectId] = zoneSphere;
        }
        
        Debug.Log($"*** CREATED {visualZoneObjects.Count} WIREFRAME ZONES ***");
        
        // Force refresh the scene view
        #if UNITY_EDITOR
        UnityEditor.SceneView.RepaintAll();
        #endif
    }
}
