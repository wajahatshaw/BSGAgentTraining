using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-zone orchestrator for the full two-pass cognitive protocol.
/// Spawns M_A, M_B, M_C (+ optional M_D/M_E), coordinates branches,
/// signals PhysicalObservationLoop when first pass is done, then
/// triggers second pass + merge after P observation completes.
/// </summary>
public class MentalAgentSpawner : MonoBehaviour
{
    [Header("Zone Identity")]
    public int zoneIndex  = -1;
    public int agentCount = 3;   // 3-5; read from cognitiveProcessConfig

    [Header("Spawning")]
    public float moveSpeed      = 5.5f;
    public float dwellTime      = 0.55f;
    public float spawnStagger   = 0.25f;  // seconds between agent activations

    [Header("Runtime State (read-only)")]
    public string phase = "Idle";

    // Zone world position — set in Init so spawn positions are correct
    private Vector3 zoneWorldOffset = Vector3.zero;

    // References
    private ZoneDeclarativeMemory  memory;
    private PhysicalObservationLoop observationLoop;

    // Spawned agents
    private MentalAgentController M_A;
    private MentalAgentController M_B;
    private MentalAgentController M_C;
    private List<MentalAgentController> allAgents = new List<MentalAgentController>();

    // Coordination flags
    private bool branchBFirstDone = false;
    private bool branchCFirstDone = false;
    private bool branchBSecondDone = false;
    private bool branchCSecondDone = false;
    private bool mergeDone         = false;

    // Static registry
    private static readonly Dictionary<int, MentalAgentSpawner> registry
        = new Dictionary<int, MentalAgentSpawner>();

    public static MentalAgentSpawner ForZone(int idx)
    {
        registry.TryGetValue(idx, out var s); return s;
    }

    void Awake()
    {
        if (zoneIndex >= 0) registry[zoneIndex] = this;
    }

    void OnEnable()
    {
        if (zoneIndex >= 0) registry[zoneIndex] = this;
    }

    void OnDestroy()
    {
        if (zoneIndex >= 0 && registry.TryGetValue(zoneIndex, out var s) && s == this)
            registry.Remove(zoneIndex);
    }

    // ── Initialization ────────────────────────────────────────────────────

    public void Init(ZoneDeclarativeMemory mem, PhysicalObservationLoop obsLoop, int count = 3)
    {
        memory          = mem;
        observationLoop = obsLoop;
        agentCount      = Mathf.Clamp(count, 3, 5);
        // Capture world offset now so SpawnAgent has correct positions
        zoneWorldOffset = mem != null ? mem.transform.position : transform.position;
        // Register in static registry (zoneIndex must be set before calling Init)
        if (zoneIndex >= 0) registry[zoneIndex] = this;

        EnsureMASpawnedForMlTraining();
    }

    /// <summary>
    /// Spawn M_A at scene load so CognitiveAgentZoneN registers with Python before the 1.5s cognitive delay.
    /// </summary>
    void EnsureMASpawnedForMlTraining()
    {
        ReplicaSceneSetup setup = FindObjectOfType<ReplicaSceneSetup>();
        if (setup == null || !setup.enableMlTrainingInRagMode || !setup.enableMlTrainingForCognitiveAgents)
            return;

        if (M_A != null && M_A.GetComponent<BSGMLAgent>() != null)
            return;

        Vector3 offset = zoneWorldOffset != Vector3.zero ? zoneWorldOffset : transform.position;
        Color ma = new Color(0.18f, 0.78f, 0.62f, 1f);
        M_A = SpawnAgent(MentalAgentController.MRole.M_A, offset, -4.0f, ma, "MA");
        M_A.Freeze();
        M_A.currentPhase = "MlBootstrap_Idle";
        Debug.Log($"[MentalAgentSpawner Z{zoneIndex}] M_A pre-spawned for CognitiveAgentZone{zoneIndex} ML registration.");
    }

    public void StartCognitiveProcess()
    {
        StopAllCoroutines();
        StartCoroutine(RunFirstPass());
    }

    // ── Spawn helper ──────────────────────────────────────────────────────

    MentalAgentController SpawnAgent(
        MentalAgentController.MRole role,
        Vector3 worldOffset,
        float xOffset,
        Color color,
        string label)
    {
        string goName = $"M_{role}_Zone{zoneIndex}";

        // Destroy stale instance if it exists (scene reload safety)
        GameObject existing = GameObject.Find(goName);
        if (existing != null) Destroy(existing);

        // Use a simple root GameObject (no visible capsule mesh — HumanBodyBuilder provides all visuals)
        GameObject go = new GameObject(goName);
        // Scale up so the human figure is clearly visible next to cognitive station objects
        const float humanScale = 3.5f;
        go.transform.position   = worldOffset + new Vector3(xOffset, 0f, 8f);
        go.transform.localScale = Vector3.one * humanScale;

        // ── Capsule collider for physics (root) — dimensions in LOCAL space ─
        CapsuleCollider col = go.AddComponent<CapsuleCollider>();
        col.height = 1.75f;   // local units (world = 1.75 * humanScale)
        col.radius = 0.25f;
        col.center = new Vector3(0f, 0.875f, 0f);

        // ── Rigidbody FIRST — before any controller Awake() fires ──────────
        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.useGravity     = false;
        rb.freezeRotation = true;
        rb.constraints    = RigidbodyConstraints.FreezeRotationX
                          | RigidbodyConstraints.FreezeRotationY
                          | RigidbodyConstraints.FreezeRotationZ
                          | RigidbodyConstraints.FreezePositionY;
        rb.interpolation  = RigidbodyInterpolation.Interpolate;
        rb.mass           = 1f;

        // ── AgentCognitiveMemory SECOND ────────────────────────────────────
        go.AddComponent<AgentCognitiveMemory>();

        // ── Build realistic human body ─────────────────────────────────────
        HumanBodyBuilder.BuildBody(go, color);

        // ── Walk animation component ───────────────────────────────────────
        HumanWalkAnimation walkAnim = go.AddComponent<HumanWalkAnimation>();
        walkAnim.walkCycleSpeed = moveSpeed * 0.42f;  // gait frequency tuned for larger body

        // ── Controller LAST (Awake reads rb and rend which now exist) ──────
        MentalAgentController ctrl = go.AddComponent<MentalAgentController>();
        ctrl.zoneIndex        = zoneIndex;
        ctrl.role             = role;
        ctrl.agentLabel       = label;
        ctrl.moveSpeed        = moveSpeed;
        ctrl.defaultDwellTime = dwellTime;
        ctrl.Init(this, memory);

        // Floating label (positioned above the head)
        AttachLabel(go, label, color);

        allAgents.Add(ctrl);

        if (role == MentalAgentController.MRole.M_A)
        {
            ReplicaSceneSetup setup = FindObjectOfType<ReplicaSceneSetup>();
            RagRuntimeMLBootstrap.TryAttachCognitiveBrain(ctrl, setup);
        }

        return ctrl;
    }

    private void AttachLabel(GameObject go, string text, Color c)
    {
        const string n = "MAgentLabel";
        Transform old = go.transform.Find(n);
        if (old != null) Destroy(old.gameObject);

        GameObject lgo = new GameObject(n);
        lgo.transform.SetParent(go.transform, false);
        lgo.transform.localPosition = new Vector3(0f, 2.3f, 0f);
        lgo.transform.localRotation = Quaternion.Euler(55f, 0f, 0f);
        TextMesh tm = lgo.AddComponent<TextMesh>();
        tm.text          = text;
        tm.anchor        = TextAnchor.MiddleCenter;
        tm.alignment     = TextAlignment.Center;
        tm.characterSize = 0.04f;   // scaled down because parent is 3.5× larger
        tm.fontSize      = 64;
        tm.fontStyle     = FontStyle.Bold;
        tm.color         = c;
    }

    private Vector3 GetZoneWorldOffset()
    {
        return zoneWorldOffset;
    }

    // ── First Pass ────────────────────────────────────────────────────────

    private IEnumerator RunFirstPass()
    {
        phase = "FirstPass";
        memory?.ResetForEpisode();

        Vector3 offset = GetZoneWorldOffset();

        // Same three shirt colours for MA / MB / MC in every zone (passed to HumanBodyBuilder as agentTint).
        Color ma = new Color(0.18f, 0.78f, 0.62f, 1f);  // MA — teal / cyan-green
        Color mb = new Color(0.22f, 0.42f, 0.95f, 1f);  // MB — royal blue
        Color mc = new Color(0.62f, 0.28f, 0.92f, 1f);  // MC — purple

        if (M_A == null)
            M_A = SpawnAgent(MentalAgentController.MRole.M_A, offset, -4.0f, ma, "MA");
        else
            M_A.Unfreeze();
        M_B = SpawnAgent(MentalAgentController.MRole.M_B, offset,  0.0f, mb, "MB");
        M_C = SpawnAgent(MentalAgentController.MRole.M_C, offset,  4.0f, mc, "MC");

        // Branch A runs first; B+C start in parallel after A finishes (triggered via event)
        yield return StartCoroutine(M_A.RunBranchA());
        // RunBranchA calls OnBranchAComplete which starts B+C
    }

    // ── Event Callbacks from controllers ─────────────────────────────────

    public void OnBranchAComplete(MentalAgentController agent)
    {
        Debug.Log($"[MentalAgentSpawner Z{zoneIndex}] Branch A done — starting B+C in parallel");
        phase = "BranchBC_Parallel";
        StartCoroutine(M_B.RunBranchB_FirstPass());
        StartCoroutine(M_C.RunBranchC_FirstPass());
    }

    public void OnBranchBFirstPassComplete(MentalAgentController agent)
    {
        branchBFirstDone = true;
        Debug.Log($"[MentalAgentSpawner Z{zoneIndex}] Branch B first pass done");
        CheckFirstPassComplete();
    }

    public void OnBranchCFirstPassComplete(MentalAgentController agent)
    {
        branchCFirstDone = true;
        Debug.Log($"[MentalAgentSpawner Z{zoneIndex}] Branch C first pass done");
        CheckFirstPassComplete();
    }

    private void CheckFirstPassComplete()
    {
        if (!branchBFirstDone || !branchCFirstDone) return;
        phase = "FirstPassComplete_AwaitingPScan";
        Debug.Log($"[MentalAgentSpawner Z{zoneIndex}] FIRST PASS COMPLETE — activating P observation scan");
        observationLoop?.StartObservation();
    }

    // Called by PhysicalObservationLoop when P scan is done
    public void OnPhysicalObservationComplete()
    {
        Debug.Log($"[MentalAgentSpawner Z{zoneIndex}] P observation complete — starting second pass");
        phase = "SecondPass";
        StartCoroutine(M_B.RunBranchB_SecondPass());
        StartCoroutine(M_C.RunBranchC_SecondPass());
    }

    public void OnBranchBSecondPassComplete(MentalAgentController agent)
    {
        branchBSecondDone = true;
        CheckSecondPassComplete();
    }

    public void OnBranchCSecondPassComplete(MentalAgentController agent)
    {
        branchCSecondDone = true;
        CheckSecondPassComplete();
    }

    private void CheckSecondPassComplete()
    {
        if (!branchBSecondDone || !branchCSecondDone) return;
        phase = "Merge";
        Debug.Log($"[MentalAgentSpawner Z{zoneIndex}] Second pass done — running merge");

        // M_A IS a MentalAgentController — no GetComponent needed
        if (M_A == null) { Debug.LogError($"[Z{zoneIndex}] M_A is null at merge step"); return; }
        M_A.role = MentalAgentController.MRole.M_merge;
        M_A.Unfreeze();
        StartCoroutine(M_A.RunMerge());
    }

    public void OnMergeDone(MentalAgentController agent)
    {
        mergeDone = true;
        phase = "CognitiveProcessComplete";
        Debug.Log($"[MentalAgentSpawner Z{zoneIndex}] Merge done — P agent unlocked for physical execution");

        // Permanently hide all M agents — they stay frozen during physical phase
        foreach (var a in allAgents) a.FreezePermanently();

        // The ZoneDeclarativeMemory.cognitiveReady is already set via MarkCognitiveStepComplete
        // but force it here as well for safety
        if (memory != null)
        {
            memory.cognitiveReady = true;
            Debug.Log($"[Z{zoneIndex}] ZoneDeclarativeMemory.cognitiveReady = true | resolvedTarget={memory.resolvedTargetId}");
        }

        // Open the PersonaCognitiveControlSystem gate so IsCognitiveGateBlocked returns false
        // and the P-agent can start RL / heuristic movement immediately
        string pAgentId = GetZonePAgentId();
        if (!string.IsNullOrEmpty(pAgentId) && PersonaCognitiveControlSystem.Instance != null)
            PersonaCognitiveControlSystem.Instance.OpenGateForAgent(pAgentId);
    }

    public void ResetForEpisode()
    {
        StopAllCoroutines();
        phase = "Idle";
        branchBFirstDone = branchCFirstDone = false;
        branchBSecondDone = branchCSecondDone = mergeDone = false;
        allAgents.Clear();

        // Destroy spawned M agents so they are re-created cleanly next episode
        DestroyAgent(ref M_A);
        DestroyAgent(ref M_B);
        DestroyAgent(ref M_C);
    }

    private void DestroyAgent(ref MentalAgentController ctrl)
    {
        if (ctrl != null)
        {
            Destroy(ctrl.gameObject);
            ctrl = null;
        }
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
}
