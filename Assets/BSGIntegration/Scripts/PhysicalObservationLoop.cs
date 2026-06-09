using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Runs on the Physical (P) agent's GameObject.
/// When activated by MentalAgentSpawner (after first cognitive pass):
///   1. P moves to each tool in its zone.
///   2. Collects toolId, toolName, worldPosition, isAvailable.
///   3. Writes observations to Declarative Memory station (cognitive_013_zoneN).
///   4. Injects summarised spatial data to Visual/VisualLoc/Manual stations.
///   5. Freezes P and notifies MentalAgentSpawner → second cognitive pass begins.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PhysicalObservationLoop : MonoBehaviour
{
    [Header("Zone Identity")]
    public int zoneIndex = -1;

    [Header("Movement")]
    public float moveSpeed       = 4.5f;
    public float arrivalDistance = 2.2f;

    [Header("Environment scan (first pass)")]
    [Tooltip("Slower than normal play — reduces shoving into workbenches / tools.")]
    public float observationMoveSpeed = 1.6f;
    [Tooltip("Stop farther from the tool pivot so the capsule clears tables and props.")]
    public float observationArrivalDistance = 3.4f;
    [Tooltip("SphereCast radius for simple slide-around obstacles during scan.")]
    public float obstacleProbeRadius = 0.42f;
    [Tooltip("How far ahead to look for blocking colliders.")]
    public float obstacleProbeDistance = 0.95f;
    public LayerMask observationObstacleMask;

    [Header("Runtime State (read-only)")]
    public bool  isObserving    = false;
    public bool  observationDone = false;
    public string currentTarget  = "";

    // Observation data
    [System.Serializable]
    public class SceneObservationEntry
    {
        public string toolId;
        public string toolName;
        public Vector3 worldPosition;
        public bool   isAvailable;
        public float  distanceFromAgent;
        public string semanticThread;
        public string observedState;
        public string metadataSource;
        public bool   hasRealDeclarativeMetadata;
    }

    public List<SceneObservationEntry> observations = new List<SceneObservationEntry>();

    // References
    private Rigidbody              rb;
    private ZoneDeclarativeMemory  declarativeMemory;
    private AgentCognitiveMemory   cognitiveMemory;
    private MentalAgentSpawner     spawner;
    private bool                   frozen = false;
    private Transform              _scanVfxRoot;
    private LineRenderer           _scanCore;
    private LineRenderer           _scanGlow;

    // ── Tool and object scan targets ──────────────────────────────────────
    // Base IDs for tools — zone suffix appended at runtime
    private static readonly string[] ToolBaseIds =
        { "tool_001", "tool_002", "tool_003", "tool_004" };

    // Detection radius used in OverlapSphere at each scan point
    [Header("Detection")]
    public float detectionRadius = 4.0f;   // how close P must be to "see" an object

    AgentGroundMotor _groundMotor;

    void Awake()
    {
        rb             = GetComponent<Rigidbody>();
        _groundMotor   = GetComponent<AgentGroundMotor>();
        if (_groundMotor != null && zoneIndex >= 0)
            _groundMotor.clampZoneIndex = zoneIndex;
        ScenePhysicsLayers.EnsureInitialized();
        if (observationObstacleMask.value == 0)
            observationObstacleMask = ScenePhysicsLayers.EnvironmentMask;
        cognitiveMemory= GetComponent<AgentCognitiveMemory>()
                      ?? gameObject.AddComponent<AgentCognitiveMemory>();
    }

    // Called by ReplicaSceneSetup after everything is wired up
    public void Init(ZoneDeclarativeMemory mem, MentalAgentSpawner sp)
    {
        declarativeMemory = mem;
        spawner           = sp;
    }

    public void ResetForEpisode()
    {
        isObserving     = false;
        observationDone = false;
        frozen          = false;
        currentTarget   = "";
        observations.Clear();
        SetObservationScanFlag(false);
        ClearScanBeam();
    }

    // ── Called by MentalAgentSpawner.CheckFirstPassComplete() ─────────────
    public void StartObservation()
    {
        if (isObserving || observationDone) return;
        StartCoroutine(RunObservation());
    }

    private IEnumerator RunObservation()
    {
        isObserving = true;
        frozen      = false;
        observations.Clear();

        // Tell BSGMLAgent to allow movement during scan (bypasses idle gate)
        SetObservationScanFlag(true);
        EnsureScanBeamRenderer();

        Debug.Log($"[PhysObs Z{zoneIndex}] P agent starting environment scan");

        try
        {
        // ── Step 1: Move to each known tool position and physically detect it ──
        foreach (string baseId in ToolBaseIds)
        {
            string zoneId = $"{baseId}_zone{zoneIndex}";
            currentTarget = zoneId;

            // Find the target GO by name (for navigation target only)
            GameObject toolGO = GameObject.Find(zoneId);
            if (toolGO == null)
            {
                Debug.LogWarning($"[PhysObs Z{zoneIndex}] Tool not found in scene: {zoneId}");
                continue;
            }

            // Move physically to within detection range of the tool
            yield return MoveTo(toolGO.transform.position);

            // Brief dwell: pulse beam + jitter reads as an active LIDAR-style sweep
            for (int pulse = 0; pulse < 10; pulse++)
            {
                UpdateScanBeamToward(toolGO.transform.position, Time.time + pulse * 0.11f);
                yield return new WaitForSeconds(0.055f);
            }

            // ── Physics detection: OverlapSphere at current agent position ──
            // This simulates the agent's "eyes" — only detects what is close enough
            Collider[] nearby = Physics.OverlapSphere(
                transform.position, detectionRadius,
                Physics.AllLayers, QueryTriggerInteraction.Collide);

            bool detected = false;
            DeclarativeObjectMetadata rootMetadata = ResolveMetadataForObservedObject(toolGO, toolGO);
            foreach (var col in nearby)
            {
                // Match by zone-suffixed name (tool_001_zone0, workbench_zone0, etc.)
                if (!col.gameObject.name.Contains(baseId)) continue;

                // Physically detected — collect real data from the scene object
                detected = true;
                bool isAvail = IsToolAvailable(col.gameObject);
                DeclarativeObjectMetadata metadata = ResolveMetadataForObservedObject(col.gameObject, toolGO) ?? rootMetadata;

                var entry = new SceneObservationEntry
                {
                    toolId            = zoneId,
                    toolName          = col.gameObject.name,
                    worldPosition     = col.gameObject.transform.position,
                    isAvailable       = isAvail,
                    distanceFromAgent = Vector3.Distance(transform.position,
                                        col.gameObject.transform.position),
                    semanticThread    = metadata != null ? metadata.semanticThread : string.Empty,
                    observedState     = metadata != null ? metadata.currentState : string.Empty,
                    metadataSource    = metadata != null ? metadata.metadataSource : "missing",
                    hasRealDeclarativeMetadata = metadata != null && !metadata.isPlaceholder
                };
                observations.Add(entry);

                Debug.Log($"[PhysObs Z{zoneIndex}] DETECTED {zoneId} | " +
                          $"pos={entry.worldPosition} | avail={isAvail} | " +
                          $"dist={entry.distanceFromAgent:F1}m | thread={entry.semanticThread} | source={entry.metadataSource}");

                // Flash the tool white to show it was physically registered
                yield return FlashObject(col.gameObject, 0.5f);
                break;
            }

            if (!detected)
            {
                // Tool exists in scene but P-agent was not close enough — log gap
                Debug.LogWarning($"[PhysObs Z{zoneIndex}] {zoneId} NOT detected by OverlapSphere " +
                                 $"(radius={detectionRadius}). Adding with estimated data.");
                observations.Add(new SceneObservationEntry
                {
                    toolId            = zoneId,
                    toolName          = toolGO.name,
                    worldPosition     = toolGO.transform.position,
                    isAvailable       = true,
                    distanceFromAgent = Vector3.Distance(transform.position, toolGO.transform.position),
                    semanticThread    = rootMetadata != null ? rootMetadata.semanticThread : string.Empty,
                    observedState     = rootMetadata != null ? rootMetadata.currentState : string.Empty,
                    metadataSource    = rootMetadata != null ? rootMetadata.metadataSource : "missing",
                    hasRealDeclarativeMetadata = rootMetadata != null && !rootMetadata.isPlaceholder
                });
            }

            yield return new WaitForSeconds(0.15f);
        }

        // ── Step 2: Also scan the workbench in this zone ───────────────────
        yield return ScanWorkbench();

        // ── Step 3: Write all observations into Declarative Memory + buffers ─
        yield return InjectObservations();

        // ── Step 4: Resolve best physical target ──────────────────────────
        string resolvedTarget = ResolveTarget();
        declarativeMemory?.RecordCognitiveStep("p_resolved_target", "resolvedTargetId", resolvedTarget);
        cognitiveMemory.Store("resolvedTargetId", resolvedTarget, $"cognitive_013_zone{zoneIndex}");

        Debug.Log($"[PhysObs Z{zoneIndex}] Scan complete. " +
                  $"Detected {observations.Count} objects. " +
                  $"Resolved target: {resolvedTarget}");

        // Stop scan — P agent freezes until second pass completes
        SetObservationScanFlag(false);
        Freeze();
        isObserving     = false;
        observationDone = true;
        currentTarget   = "";

        // Signal spawner → second cognitive pass resumes
        spawner?.OnPhysicalObservationComplete();
        }
        finally
        {
            // Always clear scan state so BSGMLAgent can unlock after cognitive merge even if the coroutine errors mid-scan.
            isObserving = false;
            SetObservationScanFlag(false);
            ClearScanBeam();
        }
    }

    private DeclarativeObjectMetadata ResolveMetadataForObservedObject(GameObject observed, GameObject expectedRoot)
    {
        DeclarativeObjectMetadata metadata = observed != null
            ? observed.GetComponentInParent<DeclarativeObjectMetadata>()
            : null;
        if (metadata != null) return metadata;

        return expectedRoot != null ? expectedRoot.GetComponent<DeclarativeObjectMetadata>() : null;
    }

    /// <summary>
    /// Checks whether the tool is currently "available" by looking at its
    /// Renderer color (occupied tools are visually different) and whether
    /// another agent's collider is overlapping it.
    /// </summary>
    private bool IsToolAvailable(GameObject toolGO)
    {
        // Check if another agent capsule is already overlapping this tool
        Collider[] atTool = Physics.OverlapSphere(
            toolGO.transform.position, 1.5f,
            Physics.AllLayers, QueryTriggerInteraction.Ignore);

        foreach (var c in atTool)
        {
            // If any BSGMLAgent (other than self) is touching this tool — it's occupied
            BSGMLAgent other = c.GetComponent<BSGMLAgent>();
            if (other != null && c.gameObject != gameObject)
                return false;
        }
        return true;
    }

    /// <summary>Moves to the workbench and records it as an additional observation point.</summary>
    private IEnumerator ScanWorkbench()
    {
        string workbenchId = $"workbench_zone{zoneIndex}";
        GameObject wb = GameObject.Find(workbenchId);
        if (wb == null) yield break;

        currentTarget = workbenchId;
        yield return MoveTo(wb.transform.position);

        Collider[] nearby = Physics.OverlapSphere(transform.position, detectionRadius + 1f,
            Physics.AllLayers, QueryTriggerInteraction.Collide);

        foreach (var col in nearby)
        {
            if (!col.gameObject.name.Contains("workbench")) continue;
            Debug.Log($"[PhysObs Z{zoneIndex}] WORKBENCH detected @ {col.gameObject.transform.position}");
            declarativeMemory?.RecordCognitiveStep("p_workbench",
                "workbench_pos",
                $"({col.gameObject.transform.position.x:F1},{col.gameObject.transform.position.z:F1})");
            yield return FlashObject(col.gameObject, 0.4f);
            break;
        }
    }

    private IEnumerator InjectObservations()
    {
        string obsSummary     = BuildObservationSummary();
        string spatialSummary = BuildSpatialSummary();
        string cognitiveFacts = BuildCognitiveReadableFacts();
        string catalogLine    = BuildToolCatalogForDeclarative();
        string semanticThreads = BuildSemanticThreadSummary();

        // ── Zone declarative blackboard (M-agents read visualSummary / retrievalSchema / dataSlots) ──
        if (declarativeMemory != null)
        {
            // Keys match ZoneDeclarativeMemory.RecordCognitiveStep routing ("visual*" → visualSummary, "schema"/"retriev" → retrievalSchema)
            declarativeMemory.RecordCognitiveStep("p_scan_slots", "p_tool_inventory", catalogLine);
            declarativeMemory.RecordCognitiveStep("p_scan_slots", "p_semantic_threads", semanticThreads);
            declarativeMemory.RecordCognitiveStep("p_scan", "retrieval_schema",
                $"SPATIAL:{spatialSummary}  THREADS:{semanticThreads}  FACTS:{cognitiveFacts}");
            declarativeMemory.RecordCognitiveStep("p_scan", "visual_scene_aggregate",
                $"[P_SCAN z{zoneIndex}] {obsSummary}");

            Debug.Log($"[PhysObs Z{zoneIndex}] Declarative memory: visual_scene_aggregate + retrieval_schema + p_tool_inventory ({observations.Count} tools).");
        }

        // Flash each target station white to show data injection
        yield return FlashStationInject($"cognitive_013_zone{zoneIndex}", obsSummary);
        yield return FlashStationInject($"cognitive_006_zone{zoneIndex}", spatialSummary);
        yield return FlashStationInject($"cognitive_007_zone{zoneIndex}", spatialSummary);
        yield return FlashStationInject($"cognitive_016_zone{zoneIndex}", spatialSummary);

        cognitiveMemory.Store("physical_obs_summary", obsSummary,     $"cognitive_013_zone{zoneIndex}");
        cognitiveMemory.Store("visual_info",           spatialSummary, $"cognitive_006_zone{zoneIndex}");
        cognitiveMemory.Store("p_tool_inventory",      catalogLine,    $"cognitive_013_zone{zoneIndex}");
        cognitiveMemory.Store("p_semantic_threads",    semanticThreads,$"cognitive_013_zone{zoneIndex}");
        cognitiveMemory.Store("p_scan_readable",       cognitiveFacts, $"cognitive_013_zone{zoneIndex}");

        declarativeMemory?.MarkCognitiveStepComplete("p_obs_decl");
        declarativeMemory?.MarkCognitiveStepComplete("p_obs_visual");
        declarativeMemory?.MarkCognitiveStepComplete("p_obs_visloc");
        declarativeMemory?.MarkCognitiveStepComplete("p_obs_manual");
    }

    private IEnumerator FlashStationInject(string stationId, string data)
    {
        InjectToStation(stationId, data);
        GameObject go = GameObject.Find(stationId);
        if (go != null) yield return FlashObject(go, 0.5f);
        else yield return new WaitForSeconds(0.1f);
    }

    private IEnumerator FlashObject(GameObject go, float duration)
    {
        if (go == null) { yield return new WaitForSeconds(duration); yield break; }
        Renderer[] renderers  = go.GetComponentsInChildren<Renderer>(true);
        Color[]    origColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            origColors[i] = renderers[i].material != null ? renderers[i].material.color : Color.white;

        foreach (var r in renderers)
            if (r.material != null) r.material.color = Color.white;

        yield return new WaitForSeconds(duration);

        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null && renderers[i].material != null)
                renderers[i].material.color = origColors[i];
    }

    private void InjectToStation(string stationId, string data)
    {
        GameObject go = GameObject.Find(stationId);
        if (go == null) return;

        var interactable = go.GetComponent<CognitiveStationInteractable>();
        if (interactable != null) interactable.storedData = data;

        declarativeMemory?.RecordCognitiveStep($"p_inject_{stationId}", "injected_data", data);
        Debug.Log($"[PhysObs Z{zoneIndex}] Injected → {stationId}: {data.Substring(0, Mathf.Min(80, data.Length))}");
    }

    /// <summary>
    /// Selects the best physical execution target from observations.
    /// Priority 1: the P-agent's own actionSequence step 0 target — if confirmed visible in scene.
    /// Priority 2: available tools ordered by distance to agent.
    /// Falls back to nearest tool regardless of availability.
    /// Falls back to tool_001_zoneN if no observations at all.
    /// </summary>
    private string ResolveTarget()
    {
        if (observations.Count == 0)
            return $"tool_001_zone{zoneIndex}";

        // Priority 1: confirm the agent's intended first-step target from its actionSequence
        string pAgentId = GetZonePAgentId();
        if (!string.IsNullOrEmpty(pAgentId) && AgentSequenceManager.Instance != null)
        {
            AgentSequenceData seq = AgentSequenceManager.Instance.GetSequence(pAgentId);
            if (seq?.actionSequence != null && seq.actionSequence.Count > 0)
            {
                string intended = $"{seq.actionSequence[0].targetObjectId}_zone{zoneIndex}";
                foreach (var e in observations)
                {
                    if (e.toolId == intended)
                    {
                        Debug.Log($"[PhysObs Z{zoneIndex}] ResolveTarget → {intended} (confirmed from actionSequence)");
                        return intended;
                    }
                }
                Debug.Log($"[PhysObs Z{zoneIndex}] Intended target {intended} not in observations — falling back to nearest");
            }
        }

        // Priority 2: prefer available tools, nearest first
        SceneObservationEntry best = null;
        foreach (var e in observations)
        {
            if (!e.isAvailable) continue;
            if (best == null || e.distanceFromAgent < best.distanceFromAgent)
                best = e;
        }

        // Fallback: if nothing available, take nearest regardless
        if (best == null)
        {
            foreach (var e in observations)
            {
                if (best == null || e.distanceFromAgent < best.distanceFromAgent)
                    best = e;
            }
        }

        string target = best?.toolId ?? $"tool_001_zone{zoneIndex}";
        Debug.Log($"[PhysObs Z{zoneIndex}] ResolveTarget → {target} " +
                  $"(avail={best?.isAvailable}, dist={best?.distanceFromAgent:F1}m)");
        return target;
    }

    /// <summary>Returns the P-agent agentId for this zone (mirrors MentalAgentController).</summary>
    private string GetZonePAgentId()
    {
        switch (zoneIndex)
        {
            case 0: return "SIMPLE_Technician_01";
            case 1: return "SIMPLE_Technician_02";
            case 2: return "SIMPLE_Supervisor_01";
            case 3: return "SIMPLE_Supervisor_02";
            default: return null;
        }
    }

    private string BuildObservationSummary()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var e in observations)
            sb.Append($"{e.toolId}@({e.worldPosition.x:F1},{e.worldPosition.z:F1})|avail={e.isAvailable}|thread={e.semanticThread};");
        return sb.ToString();
    }

    private string BuildSpatialSummary()
    {
        var sb = new StringBuilder();
        foreach (var e in observations)
            sb.Append($"{e.toolId}:pos=({e.worldPosition.x:F1},{e.worldPosition.z:F1}),dist={e.distanceFromAgent:F1},thread={e.semanticThread};");
        return sb.ToString();
    }

    private string BuildSemanticThreadSummary()
    {
        var sb = new StringBuilder();
        foreach (var e in observations)
        {
            string source = string.IsNullOrWhiteSpace(e.metadataSource) ? "missing" : e.metadataSource;
            string thread = string.IsNullOrWhiteSpace(e.semanticThread) ? "<missing>" : e.semanticThread;
            sb.Append($"{e.toolId}:thread={thread},state={e.observedState},source={source};");
        }
        return sb.Length > 0 ? sb.ToString() : "no semantic threads observed";
    }

    /// <summary>Compact row for blackboard dataSlots — every tool P registered.</summary>
    string BuildToolCatalogForDeclarative()
    {
        var sb = new StringBuilder();
        foreach (var e in observations)
        {
            sb.Append(e.toolId);
            sb.Append(e.isAvailable ? "[free]" : "[busy]");
            if (!string.IsNullOrWhiteSpace(e.semanticThread))
                sb.Append($" thread={e.semanticThread}");
            sb.Append($"@({e.worldPosition.x:F1},{e.worldPosition.z:F1})");
            sb.Append($" d={e.distanceFromAgent:F1}m | ");
        }

        return sb.Length > 0 ? sb.ToString().TrimEnd(' ', '|') : $"tool_001_zone{zoneIndex} (no rows)";
    }

    /// <summary>Natural-language style facts for second-pass / merge reasoning.</summary>
    string BuildCognitiveReadableFacts()
    {
        var sb = new StringBuilder();
        sb.Append($"Zone {zoneIndex} physical sweep: ");
        foreach (var e in observations)
        {
            sb.Append($"{e.toolId} is {(e.isAvailable ? "available" : "occupied")} at ");
            sb.Append($"({e.worldPosition.x:F1},{e.worldPosition.z:F1}), ");
            sb.Append($"{e.distanceFromAgent:F1}m from P");
            if (!string.IsNullOrWhiteSpace(e.semanticThread))
                sb.Append($", semantic thread {e.semanticThread}");
            if (!string.IsNullOrWhiteSpace(e.observedState))
                sb.Append($", state {e.observedState}");
            sb.Append("; ");
        }

        return sb.ToString().TrimEnd(' ', ';');
    }

    // ── Movement + scan VFX ─────────────────────────────────────────────

    private IEnumerator MoveTo(Vector3 target)
    {
        target.y = transform.position.y;
        float timeout = 32f;
        float elapsed = 0f;
        float speed   = observationMoveSpeed > 0.05f ? observationMoveSpeed : moveSpeed * 0.35f;
        float arrive  = Mathf.Max(arrivalDistance, observationArrivalDistance);

        while (Vector3.Distance(transform.position, target) > arrive && elapsed < timeout)
        {
            if (!frozen)
            {
                Vector3 to = target - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude < 1e-6f)
                    break;
                Vector3 dir = to.normalized;
                float stepLen = speed * Time.fixedDeltaTime;
                Vector3 delta = ComputeObstacleAwareStep(dir, stepLen);
                if (_groundMotor != null)
                    _groundMotor.TryMoveGround(delta);
                else
                    rb.MovePosition(transform.position + delta);
                UpdateScanBeamToward(target, Time.time + elapsed);
            }

            elapsed += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }
    }

    Vector3 ComputeObstacleAwareStep(Vector3 dir, float stepLen)
    {
        Vector3 origin = transform.position + Vector3.up * 0.65f;
        float probe = Mathf.Max(0.15f, obstacleProbeDistance);

        if (!SphereCastBlocked(origin, dir, stepLen + probe))
            return dir * stepLen;

        Vector3 left = Vector3.Cross(Vector3.up, dir).normalized;
        if (!SphereCastBlocked(origin, left, stepLen + probe * 0.85f))
            return left * stepLen;
        if (!SphereCastBlocked(origin, -left, stepLen + probe * 0.85f))
            return -left * stepLen;

        // Tight squeeze: creep forward slowly instead of ramming
        return dir * (stepLen * 0.25f);
    }

    bool SphereCastBlocked(Vector3 origin, Vector3 direction, float distance)
    {
        if (direction.sqrMagnitude < 1e-6f) return true;
        RaycastHit hit;
        if (!Physics.SphereCast(origin, obstacleProbeRadius, direction.normalized, out hit, distance,
                observationObstacleMask, QueryTriggerInteraction.Ignore))
            return false;
        if (hit.rigidbody != null && hit.rigidbody == rb) return false;
        return true;
    }

    void EnsureScanBeamRenderer()
    {
        if (RagInferenceSceneController.IsInferenceSceneActive())
            return;
        if (_scanCore != null) return;

        GameObject root = new GameObject("ObservationScanVfx");
        root.transform.SetParent(transform, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        _scanVfxRoot = root.transform;

        // Wide soft halo (drawn first = behind core if sorting works)
        _scanGlow = CreateBeamLayer(root.transform, "ScanGlow",
            w0: 0.2f, w1: 0.38f,
            c0: new Color(0.15f, 0.95f, 1f, 0.14f),
            c1: new Color(0.1f, 0.55f, 0.95f, 0.02f),
            sortingOrder: 0);

        // Bright narrow core
        _scanCore = CreateBeamLayer(root.transform, "ScanCore",
            w0: 0.022f, w1: 0.085f,
            c0: new Color(0.92f, 1f, 1f, 1f),
            c1: new Color(0.35f, 1f, 0.88f, 0.55f),
            sortingOrder: 1);
    }

    static LineRenderer CreateBeamLayer(Transform parent, string name,
        float w0, float w1, Color c0, Color c1, int sortingOrder)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(parent, false);
        LineRenderer lr = g.AddComponent<LineRenderer>();
        lr.useWorldSpace      = true;
        lr.positionCount      = 2;
        lr.numCapVertices     = 8;
        lr.numCornerVertices  = 4;
        lr.startWidth         = w0;
        lr.endWidth           = w1;
        lr.startColor         = c0;
        lr.endColor           = c1;
        lr.textureMode        = LineTextureMode.Stretch;
        lr.alignment          = LineAlignment.View;
        lr.shadowCastingMode  = ShadowCastingMode.Off;
        lr.receiveShadows     = false;
        lr.sortingOrder       = sortingOrder;
        lr.material           = CreateScanBeamMaterial();
        lr.enabled            = false;
        return lr;
    }

    static Material CreateScanBeamMaterial()
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        Material m = new Material(sh);
        if (sh.name.Contains("Universal") && m.HasProperty("_BaseColor"))
            m.SetColor("_BaseColor", Color.white);
        if (m.HasProperty("_Surface"))
            m.SetFloat("_Surface", 1f);
        m.renderQueue = 3100;
        return m;
    }

    void UpdateScanBeamToward(Vector3 worldTarget, float beamPhase)
    {
        if (RagInferenceSceneController.IsInferenceSceneActive())
        {
            ClearScanBeam();
            return;
        }
        if (_scanCore == null || _scanGlow == null) return;

        Vector3 eye = transform.position + Vector3.up * 1.08f;
        Vector3 toTarget = worldTarget + Vector3.up * 0.4f - eye;
        float dist = toTarget.magnitude;
        float len = Mathf.Clamp(dist, 1.15f, 9.5f);
        Vector3 dir = dist > 0.05f ? toTarget.normalized : transform.forward;

        float j = beamPhase * 22f;
        Vector3 jitter = new Vector3(
            Mathf.Sin(j * 1.7f) * 0.045f,
            Mathf.Sin(j * 2.3f) * 0.028f,
            Mathf.Cos(j * 1.9f) * 0.045f);
        Vector3 end = eye + dir * len + jitter;

        if (_scanVfxRoot != null && !_scanVfxRoot.gameObject.activeSelf)
            _scanVfxRoot.gameObject.SetActive(true);

        if (!LineRendererSafe.CanDraw(_scanCore) || !LineRendererSafe.CanDraw(_scanGlow))
            return;

        LineRendererSafe.TrySetPosition(_scanCore, 0, eye);
        LineRendererSafe.TrySetPosition(_scanCore, 1, end);
        LineRendererSafe.TrySetPosition(_scanGlow, 0, eye);
        LineRendererSafe.TrySetPosition(_scanGlow, 1, end);

        float pulse = 0.78f + 0.22f * Mathf.Sin(beamPhase * 10.5f);
        float pulseSlow = 0.88f + 0.12f * Mathf.Sin(beamPhase * 6.2f);

        _scanCore.startWidth = 0.02f * pulse;
        _scanCore.endWidth   = 0.072f * (0.92f + 0.08f * pulseSlow);

        _scanGlow.startWidth = 0.18f * pulseSlow;
        _scanGlow.endWidth   = 0.36f * (0.9f + 0.1f * pulse);
    }

    void ClearScanBeam()
    {
        if (_scanCore != null) _scanCore.enabled = false;
        if (_scanGlow != null) _scanGlow.enabled = false;
        if (_scanVfxRoot != null)
            _scanVfxRoot.gameObject.SetActive(false);
    }

    void OnGUI()
    {
        if (!isObserving) return;
        const int w = 380, h = 62;
        GUI.Box(new Rect(12f, Screen.height - h - 14f, w, h), GUIContent.none);
        GUI.Label(new Rect(22f, Screen.height - h - 8f, w - 16f, 22f),
            "<b>Environment scan</b> — slow sweep, laser = look direction", RichStyle());
        GUI.Label(new Rect(22f, Screen.height - h + 18f, w - 16f, 22f),
            string.IsNullOrEmpty(currentTarget) ? "…" : $"Target: {currentTarget}", RichStyle());
    }

    static GUIStyle s_obsGuiLabel;

    static GUIStyle RichStyle()
    {
        if (s_obsGuiLabel == null)
            s_obsGuiLabel = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 13 };
        return s_obsGuiLabel;
    }

    public void Freeze()
    {
        frozen = true;
        if (rb != null) rb.linearVelocity = Vector3.zero;
        SetObservationScanFlag(false);
    }

    public void Unfreeze()
    {
        frozen = false;
    }

    /// <summary>Tells BSGMLAgent to bypass its idle gate while P is scanning.</summary>
    private void SetObservationScanFlag(bool active)
    {
        BSGMLAgent bsg = GetComponent<BSGMLAgent>();
        if (bsg != null) bsg.isDoingObservationScan = active;
    }
}
