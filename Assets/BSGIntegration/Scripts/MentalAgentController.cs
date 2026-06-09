using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Controls a single Mental (M) agent through its assigned branch of the cognitive sequence.
///
/// Key responsibilities:
///   - Move to each cognitive station, dwell for expectedDuration, simulate the cognitive function
///   - Dwell uses JSON expectedDuration (via CognitiveProcessManager); reward flash is white for configurable seconds
///   - Draw a coloured thread line from this agent to its current target station
///   - Mark steps complete in AgentSequenceManager (so StepEfficiencyIndicator updates)
///   - Pass cognitive step rewards to the zone's P-agent via RagStepRewardBridge (+1 HUD + ML).
///   - Freeze/show idle thread when waiting; resume thread when active
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(AgentCognitiveMemory))]
public class MentalAgentController : MonoBehaviour
{
    public enum MRole { M_A, M_B, M_C, M_merge }

    // ── Role thread colours ───────────────────────────────────────────────
    private static readonly Color ColMA    = new Color(0.2f, 1.0f, 1.0f, 0.95f);  // cyan
    private static readonly Color ColMB    = new Color(0.4f, 0.55f, 1.0f, 0.95f); // blue
    private static readonly Color ColMC    = new Color(0.85f, 0.4f, 1.0f, 0.95f); // purple
    private static readonly Color ColMerge = new Color(1.0f, 0.6f, 0.1f, 0.95f);  // orange

    [Header("Identity")]
    public int    zoneIndex  = -1;
    public MRole  role       = MRole.M_A;
    public string agentLabel = "M";

    [Header("Movement")]
    public float moveSpeed       = 5.5f;
    public float arrivalDistance = 1.8f;
    public float defaultDwellTime = 2.5f;   // fallback when JSON expectedDuration unavailable

    [Header("Rewards")]
    [Tooltip("Narrative amount stored on memory; HUD+ML standard reward is +1 via RagStepRewardBridge when BSGMLAgent exists.")]
    public float cognitiveStepCompletionReward = 1f;

    [Header("Goal Buffer stack")]
    [Tooltip("Per dwell roll: probability of a failed extract (retry, black flash, penalty). 0 = always succeed after first dwell.")]
    [Range(0f, 1f)]
    public float goalBufferSimulatedMissProbability = 0f;

    [Header("ML training (M_A cognitive brain)")]
    [Tooltip("When true, VisitStation locomotion is driven by attached BSGMLAgent (CognitiveAgentZoneN) instead of scripted MoveTo.")]
    public bool deferLocomotionToMl;

    [Header("Runtime State (read-only)")]
    public string currentStation  = "";
    public string currentPhase    = "Idle";
    public bool   isFrozen        = false;
    public bool   firstPassDone   = false;
    public bool   secondPassDone  = false;

    // ── Internals ─────────────────────────────────────────────────────────
    private MentalAgentSpawner    spawner;
    private ZoneDeclarativeMemory memory;
    private AgentCognitiveMemory  localMemory;
    private Rigidbody             rb;
    private Renderer              rend;
    private Color                 baseColor;   // stored at spawn time, restored after flash
    private LineRenderer          threadLine;
    private Vector3               currentTargetPos;
    private bool                  threadActive = false;
    private HumanWalkAnimation    walkAnim;
    private BSGMLAgent            mlAgent;

    public void BindMlTraining(BSGMLAgent agent)
    {
        mlAgent = agent;
        deferLocomotionToMl = agent != null && agent.ShouldDeferCognitiveLocomotionToMl();
    }

    void Awake()
    {
        CognitiveProcessManager.EnsureExists();
        rb          = GetComponent<Rigidbody>();
        rend        = GetComponent<Renderer>();
        localMemory = GetComponent<AgentCognitiveMemory>();
        walkAnim    = GetComponent<HumanWalkAnimation>();
        if (rend != null && rend.material != null)
            baseColor = rend.material.color;
    }

    // ── Dynamic context (set in Init, used to build meaningful output strings) ─
    private string pAgentName   = "agent";
    private string firstTarget  = "target";
    private string firstAction  = "inspect";

    private void CacheAgentContext()
    {
        string pid = GetZonePAgentId();
        if (string.IsNullOrEmpty(pid)) return;
        pAgentName = pid.Replace("SIMPLE_", "").Replace("_", " ");

        // Pull first operational step from P-agent's action sequence
        if (AgentSequenceManager.Instance != null)
        {
            AgentSequenceData seq = AgentSequenceManager.Instance.GetSequence(pid);
            if (seq?.actionSequence != null && seq.actionSequence.Count > 0)
            {
                var step0 = seq.actionSequence[0];
                firstTarget = step0.targetObjectId ?? "target";
                firstAction = step0.actionType ?? "inspect";
            }
        }
    }

    void LateUpdate()
    {
        UpdateThreadLine();
    }

    // ── Setup ─────────────────────────────────────────────────────────────

    public void Init(MentalAgentSpawner parentSpawner, ZoneDeclarativeMemory mem)
    {
        spawner = parentSpawner;
        memory  = mem;
        // Warm up resolved agent/target strings for use in output generation
        CacheAgentContext();
    }

    // ── Null-safe memory helpers ──────────────────────────────────────────

    private void Mem(string stepId, string key, string value)
    {
        memory?.RecordCognitiveStep(stepId, key, value);
    }

    private void MemDone(string stepId)
    {
        memory?.MarkCognitiveStepComplete(stepId);
    }

    private void Loc(string key, string value, string station)
    {
        localMemory?.Store(key, value, station);
    }

    private void NotifySpawner(System.Action action)
    {
        if (spawner != null) action();
        else Debug.LogWarning($"[MentalAgent {name}] spawner is null");
    }

    // ── Branch A (steps 1-4) ─────────────────────────────────────────────

    public IEnumerator RunBranchA()
    {
        currentPhase = "BranchA";
        CacheAgentContext();  // refresh in case sequence loaded after Init
        CognitiveProcessManager.EnsureExists();

        if (TryGetOpeningCognitiveStepsFromJson(out List<ActionSequenceStep> openingSteps) &&
            openingSteps != null && openingSteps.Count > 0)
        {
            foreach (ActionSequenceStep st in openingSteps)
            {
                if (st == null) continue;

                bool isGoalBuffer =
                    string.Equals(st.targetObjectId, "cognitive_008", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(st.currentCognitiveState) &&
                    st.currentCognitiveState.IndexOf("Goal", StringComparison.OrdinalIgnoreCase) >= 0;

                if (isGoalBuffer)
                    yield return VisitGoalBufferStack();
                else
                {
                    string sid = $"{st.targetObjectId}_zone{zoneIndex}";
                    string outKey = string.IsNullOrEmpty(st.producesPayload) ? "memory_note" : st.producesPayload;
                    string outVal = string.IsNullOrEmpty(st.description)
                        ? $"{st.currentCognitiveState} complete"
                        : st.description;
                    yield return VisitStation(
                        sid,
                        string.IsNullOrEmpty(st.stepId) ? "cog_step_adhoc" : st.stepId,
                        outKey,
                        outVal,
                        string.IsNullOrEmpty(st.description) ? outVal : st.description,
                        0f,
                        st);
                }
            }

            firstPassDone = true;
            HideThread();
            Freeze();
            currentPhase = "BranchA_Frozen";
            NotifySpawner(() => spawner.OnBranchAComplete(this));
            yield break;
        }

        // Legacy hardcoded branch when no JSON cognitive pipeline for st_0
        yield return VisitStation($"cognitive_001_zone{zoneIndex}", "step1_intention",
            "intention", $"{pAgentName} intends to {firstAction} {firstTarget}",
            "Detecting intention from IntentionalModule...");

        yield return VisitStation($"cognitive_012_zone{zoneIndex}", "step2_image",
            "visual_summary", $"Mental image: locate {firstTarget} in zone {zoneIndex}",
            "Building task image in ImaginalBuffer...");

        yield return VisitGoalBufferStack();

        yield return VisitStation($"cognitive_012_zone{zoneIndex}", "step4_image_update",
            "visual_summary", $"Updated image: path to {firstTarget} confirmed",
            "Updating image with goal context...");

        firstPassDone = true;
        HideThread();
        Freeze();
        currentPhase = "BranchA_Frozen";
        NotifySpawner(() => spawner.OnBranchAComplete(this));
    }

    /// <summary>
    /// Ordered cognitive substeps for opening_sequence (st_0). Falls back to all cognitive_* steps if subTaskId missing.
    /// </summary>
    private bool TryGetOpeningCognitiveStepsFromJson(out List<ActionSequenceStep> steps)
    {
        steps = null;
        string pAgentId = GetZonePAgentId();
        if (string.IsNullOrEmpty(pAgentId) || AgentSequenceManager.Instance == null) return false;

        AgentSequenceData cog = AgentSequenceManager.Instance.GetCognitiveSequence(pAgentId);
        if (cog?.actionSequence == null || cog.actionSequence.Count == 0) return false;

        IEnumerable<ActionSequenceStep> ordered = cog.actionSequence
            .Where(s => s != null && !string.IsNullOrEmpty(s.targetObjectId))
            .Where(s => s.targetObjectId.StartsWith("cognitive_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.stepOrder);

        List<ActionSequenceStep> filtered = ordered
            .Where(s => string.IsNullOrEmpty(s.subTaskId) || string.Equals(s.subTaskId, "st_0", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (filtered.Count == 0 && !ordered.Any(s => !string.IsNullOrEmpty(s.subTaskId)))
            filtered = ordered.ToList();

        if (filtered.Count == 0) return false;
        steps = filtered;
        return true;
    }

    // ── Branch B — First Pass ─────────────────────────────────────────────

    public IEnumerator RunBranchB_FirstPass()
    {
        currentPhase = "BranchB_FirstPass";

        // Step 5B: ProductionMemory — read production rules
        yield return VisitStation($"cognitive_002_zone{zoneIndex}", "b_prod_mem",
            "production_rule", $"IF need {firstTarget} THEN retrieve tool schema",
            "Reading ProductionMemory for tool schema...");

        // RetrievalBuffer — retrieve schema
        yield return VisitStation($"cognitive_010_zone{zoneIndex}", "b_retrieval",
            "retrieval_schema", $"Schema: {firstTarget} → type=tool, location=zone{zoneIndex}",
            "Retrieving schema from RetrievalBuffer...");

        // DeclarativeModule — form hypothesis
        yield return VisitStation($"cognitive_013_zone{zoneIndex}", "b_decl_hyp",
            "retrieval_schema", $"Hypothesis: {firstTarget} is required tool for {firstAction}",
            "Forming tool hypothesis in DeclarativeMemory...");

        HideThread();
        Freeze();
        currentPhase = "BranchB_FirstPass_Frozen";
        NotifySpawner(() => spawner.OnBranchBFirstPassComplete(this));
    }

    // ── Branch C — First Pass ─────────────────────────────────────────────

    public IEnumerator RunBranchC_FirstPass()
    {
        currentPhase = "BranchC_FirstPass";

        yield return VisitStation($"cognitive_002_zone{zoneIndex}", "c_prod_mem",
            "production_rule", $"IF perceive env THEN scan for {firstTarget}",
            "Loading production rules for perception...");

        yield return VisitStation($"cognitive_006_zone{zoneIndex}", "c_visual",
            "visual_summary", $"Visual field primed: searching for {firstTarget}",
            "Priming VisualBuffer slot...");

        yield return VisitStation($"cognitive_007_zone{zoneIndex}", "c_vis_loc",
            "visual_loc", $"Spatial map ready: zone{zoneIndex} scanned",
            "Priming VisualLocation buffer...");

        yield return VisitStation($"cognitive_016_zone{zoneIndex}", "c_manual",
            "manual_slot", $"Motor affordances ready for {firstAction}",
            "Priming ManualModule slot...");

        yield return VisitStation($"cognitive_009_zone{zoneIndex}", "c_aural",
            "aural_slot", "Audio channel open for env feedback",
            "Priming AuralBuffer slot...");

        yield return VisitStation($"cognitive_017_zone{zoneIndex}", "c_aural_mod",
            "env_query_slots", "All perceptual slots primed — P-scan ready",
            "Completing perceptual priming in AuralModule...");

        HideThread();
        Freeze();
        currentPhase = "BranchC_FirstPass_Frozen";
        NotifySpawner(() => spawner.OnBranchCFirstPassComplete(this));
    }

    // ── Branch B — Second Pass ────────────────────────────────────────────

    public IEnumerator RunBranchB_SecondPass()
    {
        Unfreeze();
        currentPhase = "BranchB_SecondPass";

        // Re-read Declarative Memory with P's real observations
        yield return VisitStation($"cognitive_013_zone{zoneIndex}", "b2_decl_real",
            "tool_name", null,
            "Re-reading DeclarativeMemory with real P observations...");

        string resolvedTool = memory != null ? memory.resolvedTargetId : null;
        if (string.IsNullOrEmpty(resolvedTool) && memory != null)
            memory.TryGet("tool_name", out resolvedTool);
        resolvedTool = resolvedTool ?? "unknown_tool";

        Mem("b2_decl_real", "tool_name",        resolvedTool);
        Mem("b2_decl_real", "retrieval_schema",  $"Confirmed: {resolvedTool} found in scene");
        Loc("tool_name", resolvedTool, $"cognitive_013_zone{zoneIndex}");
        TickStepIndicator("b2_decl_real", $"cognitive_013_zone{zoneIndex}", resolvedTool, 0.1f);

        secondPassDone = true;
        HideThread();
        Freeze();
        currentPhase = "BranchB_SecondPass_Done";
        NotifySpawner(() => spawner.OnBranchBSecondPassComplete(this));
    }

    // ── Branch C — Second Pass ────────────────────────────────────────────

    public IEnumerator RunBranchC_SecondPass()
    {
        Unfreeze();
        currentPhase = "BranchC_SecondPass";

        yield return VisitStation($"cognitive_006_zone{zoneIndex}", "c2_visual",
            "visual_info", "confirm_spatial_model: spatial model verified with P data",
            "Confirming spatial model with P observation data...");

        secondPassDone = true;
        HideThread();
        Freeze();
        currentPhase = "BranchC_SecondPass_Done";
        NotifySpawner(() => spawner.OnBranchCSecondPassComplete(this));
    }

    // ── Merge Step (M_merge) ──────────────────────────────────────────────

    public IEnumerator RunMerge()
    {
        Unfreeze();
        currentPhase = "Merge";

        // Step 9: Production Buffer — merge all branches
        yield return VisitStation($"cognitive_004_zone{zoneIndex}", "merge_action_rule",
            "action_rule", "merged_action_rule: final action rule formed",
            "Merging all branches in ProductionBuffer...");

        // Step 10: Motor Module — emit motor command + resolvedTargetId
        yield return VisitStation($"cognitive_005_zone{zoneIndex}", "merge_motor",
            "motor_command", null,
            "Converting action rule to motor command...");

        // Resolve target
        string resolvedTool = memory != null ? memory.resolvedTargetId : null;
        if (string.IsNullOrEmpty(resolvedTool) && memory != null)
        {
            memory.TryGet("tool_name", out resolvedTool);
            if (!string.IsNullOrEmpty(resolvedTool) && !resolvedTool.Contains("_zone"))
                resolvedTool = $"{resolvedTool}_zone{zoneIndex}";
        }
        if (string.IsNullOrEmpty(resolvedTool))
            resolvedTool = $"tool_001_zone{zoneIndex}";

        Mem("merge_motor", "motor_command",    $"execute_target:{resolvedTool}");
        Mem("merge_motor", "motor_command",    $"MOVE → {resolvedTool} (zone{zoneIndex})");
        Mem("merge_motor", "resolvedTargetId", resolvedTool);
        Loc("motor_command",    $"execute_target:{resolvedTool}", $"cognitive_005_zone{zoneIndex}");
        // MemDone already called at end of VisitStation for merge_motor
        TickStepIndicator("merge_motor", $"cognitive_005_zone{zoneIndex}", resolvedTool, 0.15f);

        HideThread();
        Freeze();
        currentPhase = "Merge_Done";
        NotifySpawner(() => spawner.OnMergeDone(this));
    }

    // ── Core VisitStation — the heart of each cognitive step ─────────────
    //
    // Dwell: see CognitiveProcessManager (JSON expectedDuration, minimumDwellSeconds, fallback).
    // Agent uses processing tint during dwell; white flash follows reward (rewardWhiteFlashSeconds).
    //
    // Execution order per step:
    //   1. Show thread line toward station
    //   2. Walk to station   (walk animation ON)
    //   3. Arrive → stop     (walk animation OFF, agent stands still)
    //   4. Processing tint     (agent "working" — not reward white yet)
    //   5. Optional station flash during dwell (off by default — white only on success)
    //   6. Wait full dwell (expectedDuration)
    //   7. Store cognitive outputs + MemDone
    //   8. Tick step + PassReward (correct_step_reward from JSON when set)
    //   9. WHITE flash rewardWhiteFlashSeconds (agent + station)
    //  10. Restore body + station colours
    //  11. Brief settle → next station
    //
    // stationId       : zone-suffixed cognitive station name
    // stepId          : unique key for this step
    // outputKey/Value : what gets stored in memory (value can be null → filled later)
    // description     : what the agent is "thinking" (logged to console)
    // dwellOverride   : seconds to dwell (0 = use defaultDwellTime)

    private const int GoalBufferMaxRetries = 64;

    private ActionSequenceStep ResolveGoalBufferCognitiveStep()
    {
        string pAgentId = GetZonePAgentId();
        if (string.IsNullOrEmpty(pAgentId) || AgentSequenceManager.Instance == null) return null;

        AgentSequenceData cogSeq = AgentSequenceManager.Instance.GetCognitiveSequence(pAgentId);
        if (cogSeq?.actionSequence == null) return null;

        foreach (ActionSequenceStep s in cogSeq.actionSequence)
        {
            if (s == null) continue;
            if (s.targetObjectId != "cognitive_008") continue;
            if (!string.Equals(s.currentCognitiveState, "GoalBuffer", StringComparison.OrdinalIgnoreCase)) continue;
            return s;
        }

        return null;
    }

    static void SyncGoalBufferLayerToSequenceStep(ActionSequenceStep step, int stackIndex, GoalBufferStackLayer runtime)
    {
        if (step?.goalBufferContract?.stack == null || runtime == null) return;
        if (stackIndex < 0 || stackIndex >= step.goalBufferContract.stack.Length) return;
        GoalBufferStackLayer s = step.goalBufferContract.stack[stackIndex];
        if (s == null) return;
        s.isActive = runtime.isActive;
        s.isCompleted = runtime.isCompleted;
        s.retryAttempt = runtime.retryAttempt;
        s.desireLevel = runtime.desireLevel;
        if (!string.IsNullOrEmpty(runtime.value))
            s.value = runtime.value;
    }

    /// <summary>
    /// Goal Buffer: move once, process stack bottom→middle→top, then mark cognitive step 3 complete.
    /// </summary>
    private IEnumerator VisitGoalBufferStack()
    {
        string stationId = $"cognitive_008_zone{zoneIndex}";
        currentStation = stationId;

        ActionSequenceStep jsonStep = ResolveGoalBufferCognitiveStep();
        if (jsonStep != null && jsonStep.goalBufferContract != null && jsonStep.goalBufferContract.IsTripleModeContract())
        {
            yield return VisitGoalBufferTripleInitialState(jsonStep, stationId);
            yield break;
        }

        if (jsonStep == null || jsonStep.goalBufferContract == null ||
            jsonStep.goalBufferContract.stack == null || jsonStep.goalBufferContract.stack.Length == 0)
        {
            yield return VisitStation(stationId, "step3_goal",
                "goal", $"Goal: {firstAction} {firstTarget} successfully",
                "Binding goal in GoalBuffer...");
            yield break;
        }

        // Working copy (runtime retry counts without mutating asset JSON)
        var layers = new List<GoalBufferStackLayer>();
        foreach (GoalBufferStackLayer L in jsonStep.goalBufferContract.stack)
        {
            layers.Add(new GoalBufferStackLayer
            {
                position = L.position,
                type = L.type,
                value = L.value,
                isActive = false,
                isCompleted = false,
                reward = L.reward,
                penalty = L.penalty,
                retryAttempt = L.retryAttempt,
                desireLevel = L.desireLevel,
                expectedDuration = L.expectedDuration
            });
        }

        for (int si = 0; si < jsonStep.goalBufferContract.stack.Length; si++)
        {
            GoalBufferStackLayer src = jsonStep.goalBufferContract.stack[si];
            if (src == null) continue;
            src.isActive = false;
            src.isCompleted = false;
        }

        Vector3 targetPos = FindPos(stationId);
        currentTargetPos = targetPos;
        threadActive = true;

        if (targetPos != Vector3.zero)
            yield return MoveTo(targetPos);
        else
            yield return new WaitForSeconds(0.15f);

        if (rb != null) rb.linearVelocity = Vector3.zero;

        string jsonStepId = string.IsNullOrEmpty(jsonStep.stepId) ? "step3_goal" : jsonStep.stepId;
        GoalBufferDesireUtility.TryResolveDesire(jsonStep, memory, out float resolvedDesire, out string desireSource);
        if (memory != null)
            memory.SetGoalBufferResolvedDesire(jsonStepId, resolvedDesire, desireSource);
        if (resolvedDesire > 0f)
            Debug.Log($"🧠 [{name}] {jsonStepId} Goal Buffer desire reference resolved to {resolvedDesire:F1} via {desireSource}");

        for (int li = 0; li < layers.Count; li++)
        {
            GoalBufferStackLayer layer = layers[li];
            layer.isActive = true;
            SyncGoalBufferLayerToSequenceStep(jsonStep, li, layer);

            bool skipLayerForEligibility = false;
            bool layerDone = false;
            while (!layerDone)
            {
                TryGetZonePAgentSkillLevel(out float pAgentSkillLevel, out string pAgentIdForSkill);
                if (!GoalBufferDesireUtility.IsLayerEligible(pAgentSkillLevel, layer.desireLevel))
                {
                    layer.retryAttempt++;
                    layer.isActive = false;
                    layer.isCompleted = false;
                    SyncGoalBufferLayerToSequenceStep(jsonStep, li, layer);

                    CacheBodyColors();
                    SetBodyColor(Color.red);
                    yield return new WaitForSeconds(0.35f);
                    RestoreBodyColors();

                    string targetPAgent = string.IsNullOrWhiteSpace(pAgentIdForSkill) ? "zone_p_agent" : pAgentIdForSkill;
                    Debug.LogWarning(
                        $"🧠 [{name}] Goal Buffer layer {layer.position}/{layer.type} skipped for {targetPAgent}: " +
                        $"skill {pAgentSkillLevel:F1} < desire {layer.desireLevel:F1}");
                    skipLayerForEligibility = true;
                    break;
                }

                // No white/station flash during dwell — that read as "success" before validation.
                // White + station flash only after successful extraction when layer.reward > 0.

                CognitiveProcessManager.EnsureExists();
                float dwell = CognitiveProcessManager.Instance != null
                    ? CognitiveProcessManager.Instance.ResolveLayerDwell(layer.expectedDuration)
                    : Mathf.Max(0.05f, layer.expectedDuration > 0f ? layer.expectedDuration : 2.5f);
                Debug.Log($"🧠 [{name}] {jsonStepId} layer {layer.position}/{layer.type} | dwell={dwell:F2}s (retry {layer.retryAttempt})");

                float elapsed = 0f;
                while (elapsed < dwell)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                bool miss = goalBufferSimulatedMissProbability > 0f &&
                            UnityEngine.Random.value < goalBufferSimulatedMissProbability;
                if (miss && layer.retryAttempt < GoalBufferMaxRetries)
                {
                    layer.retryAttempt++;
                    SyncGoalBufferLayerToSequenceStep(jsonStep, li, layer);
                    CacheBodyColors();
                    RestoreStationColor(stationId);
                    SetBodyColor(Color.black);
                    yield return new WaitForSeconds(1f);
                    RestoreBodyColors();
                    int penBase = layer.penalty > 0 ? layer.penalty : 2;
                    PassPenaltyToPAgent(penBase);
                    float waitRetry = Mathf.Max(0.5f, layer.expectedDuration);
                    yield return new WaitForSeconds(waitRetry);
                    continue;
                }

                string pAgentIdForDisk = GetZonePAgentId();
                string diskErr = null;
                string diskValue = null;
                bool diskOk = !string.IsNullOrEmpty(pAgentIdForDisk) &&
                    GoalBufferContractParser.TryReadGoalBufferLayerForRuntime(
                        pAgentIdForDisk, layer.position, layer.type,
                        out diskValue, out diskErr);
                if (!diskOk)
                {
                    if (layer.retryAttempt < GoalBufferMaxRetries)
                        layer.retryAttempt++;
                    SyncGoalBufferLayerToSequenceStep(jsonStep, li, layer);
                    string reason = string.IsNullOrEmpty(pAgentIdForDisk) ? "no_zone_agent_id" : diskErr;
                    Debug.LogWarning(
                        $"🧠 [{name}] Goal Buffer disk validation failed layer {layer.position}/{layer.type}: {reason} — retry after dwell");
                    CacheBodyColors();
                    RestoreStationColor(stationId);
                    SetBodyColor(Color.black);
                    yield return new WaitForSeconds(1f);
                    RestoreBodyColors();
                    int penBase2 = layer.penalty > 0 ? layer.penalty : 2;
                    PassPenaltyToPAgent(penBase2);
                    float waitDiskRetry = Mathf.Max(0.5f, layer.expectedDuration);
                    yield return new WaitForSeconds(waitDiskRetry);
                    continue;
                }

                layer.value = diskValue;
                layerDone = true;
            }

            if (skipLayerForEligibility)
                continue;

            // Persist to declarative memory before marking stack layer complete (contract: no isCompleted until stored).
            string memKey = string.IsNullOrEmpty(layer.type) ? "goal_buffer_layer" : layer.type;
            Mem(jsonStepId, memKey, layer.value ?? "");
            Loc(memKey, layer.value ?? "", stationId);

            int rw = layer.reward;
            // Reward cue: white agent + white station only when this layer actually grants reward.
            if (rw > 0)
            {
                CacheBodyColors();
                SetBodyColor(Color.white);
                StartCoroutine(FlashStation(stationId, 0f));
                yield return new WaitForSeconds(0.35f);
                PassRewardToPAgent(rw);
                RestoreBodyColors();
                RestoreStationColor(stationId);
            }

            layer.isActive = false;
            layer.isCompleted = true;
            SyncGoalBufferLayerToSequenceStep(jsonStep, li, layer);

            yield return new WaitForSeconds(0.25f);
        }

        string compositeGoal = $"Goal: {firstAction} {firstTarget}";
        foreach (GoalBufferStackLayer completedLayer in layers)
        {
            if (completedLayer != null && completedLayer.isCompleted && !string.IsNullOrWhiteSpace(completedLayer.value))
            {
                compositeGoal = completedLayer.value;
                break;
            }
        }
        Mem(jsonStepId, "goal", compositeGoal);
        MemDone(jsonStepId);

        string connSummary = jsonStep.goalBufferContract.connections != null
            ? string.Join(", ", jsonStep.goalBufferContract.connections)
            : "";
        float endReward = jsonStep != null && jsonStep.correct_step_reward > 0f
            ? jsonStep.correct_step_reward
            : cognitiveStepCompletionReward;
        TickStepIndicator(jsonStepId, stationId, connSummary, endReward);
        PassRewardToPAgent(endReward);

        CognitiveProcessManager.EnsureExists();
        float flashSec = CognitiveProcessManager.Instance != null
            ? CognitiveProcessManager.Instance.rewardWhiteFlashSeconds
            : 2f;
        CacheBodyColors();
        SetBodyColor(Color.white);
        StartCoroutine(FlashStation(stationId, 0f));
        yield return new WaitForSeconds(flashSec);
        RestoreBodyColors();
        RestoreStationColor(stationId);
    }

    /// <summary>
    /// RAG <c>initial_state</c> bottom/middle/top strings — extract to declarative memory and grab matching balls.
    /// </summary>
    private IEnumerator VisitGoalBufferTripleInitialState(ActionSequenceStep jsonStep, string stationId)
    {
        Vector3 targetPos = FindPos(stationId);
        currentTargetPos = targetPos;
        threadActive = true;

        if (targetPos != Vector3.zero)
            yield return MoveTo(targetPos);
        else
            yield return new WaitForSeconds(0.15f);

        if (rb != null) rb.linearVelocity = Vector3.zero;

        string jsonStepId = string.IsNullOrEmpty(jsonStep.stepId) ? "step3_goal" : jsonStep.stepId;

        GoalBufferDesireUtility.TryResolveDesire(jsonStep, memory, out float resolvedDesire, out string desireSource);
        if (memory != null)
        {
            memory.ClearGoalBufferTripleExtractions();
            memory.SetGoalBufferResolvedDesire(jsonStepId, resolvedDesire, desireSource);
        }

        GoalBufferStationPresenter pres = GoalBufferStationPresenter.TryGetForZone(zoneIndex);
        if (pres == null)
        {
            GameObject gbGo = GameObject.Find(stationId);
            pres = gbGo != null ? gbGo.GetComponent<GoalBufferStationPresenter>() : null;
        }
        pres?.ApplyPreviewForContract(jsonStep.goalBufferContract);

        float perSlot = GoalBufferTripleOrchestrator.PerSlotDwellSeconds(jsonStep, jsonStep.goalBufferContract);

        for (int ord = 0; ord < 3; ord++)
        {
            GoalBufferSlot slot = (GoalBufferSlot)ord;
            string text = GoalBufferTripleOrchestrator.GetSlotText(jsonStep.goalBufferContract, slot);
            if (text == null) continue;

            float elapsed = 0f;
            while (elapsed < perSlot)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            GoalBufferTripleOrchestrator.WriteSlotToZoneMemory(memory, jsonStepId, slot, text);
            pres?.PlayGrab(slot);
            if (walkAnim != null)
                walkAnim.PlayGoalBufferGrabPulse();

            yield return new WaitForSeconds(0.25f);
        }

        string compositeGoal = GoalBufferTripleOrchestrator.GetSlotText(jsonStep.goalBufferContract, GoalBufferSlot.Top)
            ?? GoalBufferTripleOrchestrator.GetSlotText(jsonStep.goalBufferContract, GoalBufferSlot.Middle)
            ?? GoalBufferTripleOrchestrator.GetSlotText(jsonStep.goalBufferContract, GoalBufferSlot.Bottom)
            ?? $"Goal: {firstAction} {firstTarget}";
        Mem(jsonStepId, "goal", compositeGoal);
        MemDone(jsonStepId);

        string connSummary = jsonStep.goalBufferContract.connections != null
            ? string.Join(", ", jsonStep.goalBufferContract.connections)
            : "";
        float endReward = jsonStep != null && jsonStep.correct_step_reward > 0f
            ? jsonStep.correct_step_reward
            : cognitiveStepCompletionReward;
        TickStepIndicator(jsonStepId, stationId, connSummary, endReward);
        PassRewardToPAgent(endReward);

        CognitiveProcessManager.EnsureExists();
        float flashSec = CognitiveProcessManager.Instance != null
            ? CognitiveProcessManager.Instance.rewardWhiteFlashSeconds
            : 2f;
        CacheBodyColors();
        SetBodyColor(Color.white);
        StartCoroutine(FlashStation(stationId, 0f));
        yield return new WaitForSeconds(flashSec);
        RestoreBodyColors();
        RestoreStationColor(stationId);
    }

    private IEnumerator VisitStation(
        string stationId, string stepId,
        string outputKey, string outputValue,
        string description, float dwellOverride = 0f,
        ActionSequenceStep sequenceStep = null)
    {
        currentStation = stationId;
        CognitiveProcessManager.EnsureExists();
        CognitiveProcessManager mgr = CognitiveProcessManager.Instance;

        string pAgentId = GetZonePAgentId();
        ActionSequenceStep resolved = sequenceStep;
        if (resolved == null && mgr != null)
            resolved = mgr.FindStepForStation(pAgentId, stationId, stepId);

        float dwell = mgr != null
            ? mgr.ResolveDwellSeconds(resolved, dwellOverride, defaultDwellTime)
            : Mathf.Max(0.05f, dwellOverride > 0f ? dwellOverride : defaultDwellTime);

        // ── 1. Point thread toward station ────────────────────────────────
        Vector3 targetPos = FindPos(stationId);
        currentTargetPos = targetPos;
        threadActive = true;

        // ── 2. Walk to station ────────────────────────────────────────────
        if (deferLocomotionToMl && mlAgent != null && targetPos != Vector3.zero)
        {
            mlAgent.SetMlNavigationTarget(targetPos, stationId);
            yield return WaitForMlArrival(targetPos, stationId);
            mlAgent.ClearMlNavigationTarget();
        }
        else if (targetPos != Vector3.zero)
            yield return MoveTo(targetPos);
        else
            yield return new WaitForSeconds(0.15f);

        if (rb != null) rb.linearVelocity = Vector3.zero;

        // ── 3–6. Processing tint + optional station colour during dwell ───
        CacheBodyColors();
        if (mgr != null)
            SetBodyColor(mgr.processingBodyTint);
        else
            SetBodyColor(new Color(0.35f, 0.85f, 0.95f, 1f));

        if (mgr == null || !mgr.whiteStationOnlyOnSuccess)
            StartCoroutine(FlashStation(stationId, 0f));

        Debug.Log($"🧠 [{name}] {stepId} @ {stationId} | {description} | dwell={dwell:F2}s (json expected={(resolved != null ? resolved.expectedDuration.ToString("F2") : "—")})");

        bool imaginalVisit = ImaginalBufferStepHelper.IsImaginalBufferStep(resolved)
            || ImaginalBufferStepHelper.StationIdIsImaginalBuffer(stationId);
        ImaginalThoughtBubble imgBubble = ImaginalThoughtBubble.GetOrCreate(transform);
        if (imaginalVisit)
            imgBubble.Show(BuildImaginalBubbleText(resolved, description, outputKey));
        else
            imgBubble.Hide();

        float elapsed = 0f;
        while (elapsed < dwell)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        imgBubble.Hide();

        // ── 7. Store cognitive outputs ────────────────────────────────────
        if (!string.IsNullOrEmpty(outputKey) && !string.IsNullOrEmpty(outputValue))
        {
            Mem(stepId, outputKey, outputValue);
            Loc(outputKey, outputValue, stationId);
        }
        MemDone(stepId);

        float stepReward = resolved != null && resolved.correct_step_reward > 0f
            ? resolved.correct_step_reward
            : cognitiveStepCompletionReward;

        TickStepIndicator(stepId, stationId, outputValue ?? "", stepReward);
        PassRewardToPAgent(stepReward);

        // ── 8–9. Reward feedback: white agent + station ───────────────────
        float flashSec = mgr != null ? mgr.rewardWhiteFlashSeconds : 2f;
        SetBodyColor(Color.white);
        StartCoroutine(FlashStation(stationId, 0f));
        yield return new WaitForSeconds(flashSec);
        RestoreBodyColors();
        RestoreStationColor(stationId);

        yield return new WaitForSeconds(0.25f);
    }

    string BuildImaginalBubbleText(ActionSequenceStep step, string fallbackDescription, string outputKey)
    {
        if (step != null && !string.IsNullOrWhiteSpace(step.imaginalThoughtText))
            return step.imaginalThoughtText;

        string desc = step != null && !string.IsNullOrWhiteSpace(step.description)
            ? step.description
            : fallbackDescription;
        if (string.IsNullOrWhiteSpace(desc))
            desc = "Imagining next step";

        string transition = "";
        if (step != null && (!string.IsNullOrWhiteSpace(step.imaginalStateBefore) || !string.IsNullOrWhiteSpace(step.imaginalStateAfter)))
            transition = $"{step.imaginalStateBefore} → {step.imaginalStateAfter}";

        string payload = step != null && !string.IsNullOrWhiteSpace(step.producesPayload)
            ? $"Output: {step.producesPayload}"
            : !string.IsNullOrWhiteSpace(outputKey) ? $"Output: {outputKey}" : "";

        if (!string.IsNullOrWhiteSpace(transition) && !string.IsNullOrWhiteSpace(payload))
            return $"{desc}\n{transition}\n{payload}";
        if (!string.IsNullOrWhiteSpace(transition))
            return $"{desc}\n{transition}";
        if (!string.IsNullOrWhiteSpace(payload))
            return $"{desc}\n{payload}";
        return desc;
    }

    // ── Per-step body-colour helpers ─────────────────────────────────────

    // Stored colours so we can restore after the white "processing" phase
    private Renderer[] _bodyRenderers;
    private Color[]    _bodySavedColors;

    private void CacheBodyColors()
    {
        _bodyRenderers   = GetComponentsInChildren<Renderer>(true);
        _bodySavedColors = new Color[_bodyRenderers.Length];
        for (int i = 0; i < _bodyRenderers.Length; i++)
            _bodySavedColors[i] = _bodyRenderers[i] != null && _bodyRenderers[i].material != null
                ? _bodyRenderers[i].material.color
                : Color.white;
    }

    private void SetBodyColor(Color c)
    {
        if (_bodyRenderers == null) return;
        foreach (var r in _bodyRenderers)
            if (r != null && r.material != null) r.material.color = c;
    }

    private void RestoreBodyColors()
    {
        if (_bodyRenderers == null) return;
        for (int i = 0; i < _bodyRenderers.Length; i++)
            if (_bodyRenderers[i] != null && _bodyRenderers[i].material != null)
                _bodyRenderers[i].material.color = _bodySavedColors[i];
    }

    // ── Station colour helpers (replaces fire-and-forget FlashStation) ───

    // Stores station original colours for instant restore
    private Renderer[] _stationRenderers;
    private Color[]    _stationSavedColors;

    private IEnumerator FlashStation(string stationId, float legacyDuration)
    {
        // legacyDuration is ignored; station now turns white at step start
        // and is restored explicitly via RestoreStationColor at step end.
        GameObject go = GameObject.Find(stationId);
        if (go == null) yield break;

        _stationRenderers   = go.GetComponentsInChildren<Renderer>(true);
        _stationSavedColors = new Color[_stationRenderers.Length];
        for (int i = 0; i < _stationRenderers.Length; i++)
            _stationSavedColors[i] = _stationRenderers[i] != null && _stationRenderers[i].material != null
                ? _stationRenderers[i].material.color
                : Color.white;

        foreach (var r in _stationRenderers)
            if (r != null && r.material != null) r.material.color = Color.white;
    }

    private void RestoreStationColor(string stationId)
    {
        if (_stationRenderers == null) return;
        for (int i = 0; i < _stationRenderers.Length; i++)
            if (_stationRenderers[i] != null && _stationRenderers[i].material != null)
                _stationRenderers[i].material.color = _stationSavedColors[i];
        _stationRenderers = null;
    }

    // ── Step indicator update ─────────────────────────────────────────────

    /// <summary>
    /// Marks the cognitive step complete in AgentSequenceManager for the P-agent ID,
    /// then triggers MLTrainingResultsWriter so StepEfficiencyIndicator picks it up.
    /// </summary>
    private void TickStepIndicator(string stepId, string stationId, string value, float reward)
    {
        if (AgentSequenceManager.Instance == null) return;

        // The cognitive sequence in AgentSequenceManager is keyed by the P-agent's agentId
        string pAgentId = GetZonePAgentId();
        if (string.IsNullOrEmpty(pAgentId)) return;

        AgentSequenceData cogSeq = AgentSequenceManager.Instance.GetCognitiveSequence(pAgentId);
        if (cogSeq == null) return;

        ActionSequenceStep stepToMark = null;
        // JSON cognitive step ids (e.g. t01_cog_s03) match exactly; legacy labels (step1_intention, …) skip this and use first incomplete — same as pre–Goal Buffer stack behavior.
        if (!string.IsNullOrEmpty(stepId) && stepId.Contains("_cog_"))
        {
            foreach (ActionSequenceStep s in cogSeq.actionSequence)
            {
                if (s != null && !s.isStepCompleted &&
                    string.Equals(s.stepId, stepId, StringComparison.OrdinalIgnoreCase))
                {
                    stepToMark = s;
                    break;
                }
            }
        }

        if (stepToMark == null)
        {
            foreach (ActionSequenceStep s in cogSeq.actionSequence)
            {
                if (s != null && !s.isStepCompleted)
                {
                    stepToMark = s;
                    break;
                }
            }
        }
        if (stepToMark != null)
        {
            stepToMark.isActivated    = true;
            stepToMark.isStepCompleted = true;
        }

        // Notify PersonaCognitiveControlSystem (drives HUD overlay)
        if (PersonaCognitiveControlSystem.Instance != null)
        {
            PersonaCognitiveControlSystem.Instance.NotifyCognitiveStepCompleted(
                pAgentId, stepId, stationId);
            PersonaCognitiveControlSystem.Instance.NotifyStepTransition(pAgentId, stepId);
        }

        // In-memory stats only; RagStepRewardBridge.OnStepCompleted handles disk when ML trainer is connected.
    }

    // ── Reward passthrough to P-agent ─────────────────────────────────────

    private void PassPenaltyToPAgent(float positivePenaltyMagnitude)
    {
        float p = Mathf.Abs(positivePenaltyMagnitude);
        memory?.AddStepReward(-p, currentStation);

        string pAgentId = GetZonePAgentId();
        if (string.IsNullOrEmpty(pAgentId)) return;

        GameObject pGO = GameObject.Find(pAgentId);
        if (pGO == null) return;

        BSGMLAgent bsgAgent = pGO.GetComponent<BSGMLAgent>();
        if (bsgAgent != null)
        {
            bsgAgent.AddCognitiveStackPenalty(p);
            Debug.Log($"⚠️ [{name}] Goal Buffer penalty -{p:F2} → {pAgentId} (station={currentStation})");
        }
    }

    private void PassRewardToPAgent(float amount)
    {
        if (amount < 0f)
        {
            PassPenaltyToPAgent(-amount);
            return;
        }

        memory?.AddStepReward(amount, currentStation);

        string pAgentId = GetZonePAgentId();
        if (string.IsNullOrEmpty(pAgentId)) return;

        RagStepRewardBridge.ApplyStandardStepRewardFromMentalController(pAgentId, name, zoneIndex);
        Debug.Log($"💰 [{name}] Cognitive reward +1 (HUD+ML) → {pAgentId} (memory narrative +{amount:F2}, station={currentStation})");
    }

    // ── Thread line (drawn every LateUpdate) ─────────────────────────────

    private void UpdateThreadLine()
    {
        if (RagInferenceSceneController.IsInferenceSceneActive())
        {
            if (threadLine != null)
            {
                threadLine.enabled = false;
                threadLine.gameObject.SetActive(false);
            }
            return;
        }

        if (isFrozen || !threadActive)
        {
            if (threadLine != null) threadLine.enabled = false;
            return;
        }

        if (currentTargetPos == Vector3.zero)
        {
            if (threadLine != null) threadLine.enabled = false;
            return;
        }

        if (threadLine == null) CreateThreadLine();
        if (threadLine == null) return;

        if (!threadLine.gameObject.activeSelf)
            threadLine.gameObject.SetActive(true);
        threadLine.enabled = true;
        if (!LineRendererSafe.CanDraw(threadLine))
            return;
        LineRendererSafe.TrySetPosition(threadLine, 0, transform.position + Vector3.up * 1.85f);
        LineRendererSafe.TrySetPosition(threadLine, 1, currentTargetPos + Vector3.up * 0.5f);
    }

    private void CreateThreadLine()
    {
        GameObject lineGO = new GameObject($"{name}_Thread");
        lineGO.transform.SetParent(transform, false);
        lineGO.SetActive(true);
        threadLine = lineGO.AddComponent<LineRenderer>();
        threadLine.positionCount = 2;
        threadLine.startWidth    = 0.07f;
        threadLine.endWidth      = 0.04f;
        threadLine.useWorldSpace = true;
        threadLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        threadLine.receiveShadows    = false;

        Color tc = GetThreadColor();
        Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
        Material mat = shader != null ? new Material(shader) : new Material(Shader.Find("Standard"));
        mat.color      = tc;
        threadLine.material    = mat;
        threadLine.startColor  = tc;
        threadLine.endColor    = new Color(tc.r, tc.g, tc.b, 0.5f);
    }

    private Color GetThreadColor()
    {
        switch (role)
        {
            case MRole.M_A:     return ColMA;
            case MRole.M_B:     return ColMB;
            case MRole.M_C:     return ColMC;
            case MRole.M_merge: return ColMerge;
            default:            return Color.white;
        }
    }

    public void HideThread()
    {
        threadActive = false;
        if (threadLine != null) threadLine.enabled = false;
    }

    public void ShowThread()
    {
        threadActive = true;
    }

    // ── Movement ──────────────────────────────────────────────────────────

    private IEnumerator WaitForMlArrival(Vector3 target, string stationId)
    {
        if (rb == null) yield break;
        target.y = transform.position.y;

        if (walkAnim == null) walkAnim = GetComponent<HumanWalkAnimation>();
        if (walkAnim != null) walkAnim.StartWalking();

        float timeout = Mathf.Max(20f, Vector3.Distance(transform.position, target) / Mathf.Max(0.5f, moveSpeed) * 4f);
        float elapsed = 0f;
        float lastDist = float.MaxValue;
        while (Vector3.Distance(transform.position, target) > arrivalDistance && elapsed < timeout)
        {
            float dist = Vector3.Distance(transform.position, target);
            if (elapsed > 2.5f && dist >= lastDist - 0.08f)
            {
                Debug.LogWarning($"[MentalAgent {name}] ML locomotion stalled @ {stationId} — scripted MoveTo fallback.");
                if (walkAnim != null) walkAnim.StopWalking();
                if (mlAgent != null) mlAgent.ClearMlNavigationTarget();
                yield return MoveTo(target);
                yield break;
            }
            lastDist = dist;

            if (!isFrozen)
            {
                Vector3 dir = (target - transform.position);
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 10f * Time.fixedDeltaTime);
                }
            }
            elapsed += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        if (walkAnim != null) walkAnim.StopWalking();
        if (rb != null) rb.linearVelocity = Vector3.zero;
    }

    private IEnumerator MoveTo(Vector3 target)
    {
        if (rb == null) yield break;
        target.y = transform.position.y;

        // Start walk animation
        if (walkAnim == null) walkAnim = GetComponent<HumanWalkAnimation>();
        if (walkAnim != null) walkAnim.StartWalking();

        float timeout = 15f, elapsed = 0f;
        while (Vector3.Distance(transform.position, target) > arrivalDistance && elapsed < timeout)
        {
            if (!isFrozen)
            {
                Vector3 dir = (target - transform.position).normalized;
                rb.MovePosition(transform.position + dir * moveSpeed * Time.fixedDeltaTime);

                // Face direction of travel smoothly
                if (dir.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 10f * Time.fixedDeltaTime);
                }
            }
            elapsed += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        // Arrived — stop walk animation
        if (walkAnim != null) walkAnim.StopWalking();
    }

    private Vector3 FindPos(string id)
    {
        if (string.IsNullOrEmpty(id)) return Vector3.zero;
        GameObject go = GameObject.Find(id);
        return go != null ? go.transform.position : Vector3.zero;
    }

    public void Freeze()
    {
        isFrozen = true;
        if (rb != null) rb.linearVelocity = Vector3.zero;
        if (walkAnim == null) walkAnim = GetComponent<HumanWalkAnimation>();
        if (walkAnim != null) walkAnim.Freeze();
        HideThread();
    }

    /// <summary>
    /// Permanently freezes and hides this M-agent after the full cognitive process is done.
    /// Only call this from MentalAgentSpawner.OnMergeDone — NOT during mid-step freezes.
    /// </summary>
    public void FreezePermanently()
    {
        isFrozen = true;
        if (rb != null) rb.linearVelocity = Vector3.zero;
        if (walkAnim == null) walkAnim = GetComponent<HumanWalkAnimation>();
        if (walkAnim != null) walkAnim.Freeze();
        HideThread();
        // Visually idle: hide all child renderers so M-agents disappear during physical phase
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            r.enabled = false;
        foreach (var tm in GetComponentsInChildren<TextMesh>())
            tm.text = "";
    }

    public void Unfreeze()
    {
        isFrozen = false;
        if (walkAnim == null) walkAnim = GetComponent<HumanWalkAnimation>();
        if (walkAnim != null) walkAnim.Unfreeze();
        // Restore visibility when re-activated for second pass or merge
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            r.enabled = true;
        ShowThread();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>Resolves the zone's attached physical agent so Goal Buffer can validate skill and pass rewards.</summary>
    private bool TryGetZonePAgent(out BSGMLAgent pAgent, out string pAgentId)
    {
        pAgent = null;
        pAgentId = GetZonePAgentId();
        if (string.IsNullOrWhiteSpace(pAgentId))
            return false;

        GameObject pGO = GameObject.Find(pAgentId);
        if (pGO == null)
            return false;

        pAgent = pGO.GetComponent<BSGMLAgent>();
        return pAgent != null;
    }

    private bool TryGetZonePAgentSkillLevel(out float skillLevel, out string pAgentId)
    {
        skillLevel = 0f;
        if (!TryGetZonePAgent(out BSGMLAgent pAgent, out pAgentId) || pAgent == null)
            return false;

        skillLevel = pAgent.GetDynamicSkillLevel();
        return true;
    }

    /// <summary>Returns the P-agent agentId for this zone, used for step indicator + rewards.</summary>
    private string GetZonePAgentId() => ZoneAgentIds.TryResolvePhysicalAgentId(zoneIndex);

    public string GetZonePAgentIdForBootstrap() => GetZonePAgentId();
}
