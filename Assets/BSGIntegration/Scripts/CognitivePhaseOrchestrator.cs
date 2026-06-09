using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central DAG executor that drives the three-phase cognitive execution flow for a single zone:
///
///   PHASE 1 — IMAGINE (st_0, all cognitive, no physical)
///     The agent plans and imagines the task before any physical action. Ends at the
///     barrier step (t01_cog_s06) that closes st_0.
///
///   PHASE 2 — EXECUTE + VALIDATE LOOP (st_1+, cognitive and physical interleaved)
///     Production Memory issues commands; physical actions fire; Visual/Manual modules perceive
///     results; Imaginal Buffer decodes and validates; Production Memory issues next command.
///     The dependsOn field on every step enforces correct ordering.
///
///   PHASE 3 — SUBTASK TRANSITION
///     A physical barrier step closes a subtask. Imaginal Buffer closes the old subtask, writes
///     a new plan, and Production Memory issues the first command for the next subtask.
///     Phase 2 loop resumes for the new subtask.
///
/// Usage:
///   1. Attach to a manager GameObject in the scene (one per zone, or use the singleton for a
///      single-zone setup).
///   2. Call <see cref="Initialize"/> from a setup script after AgentSequenceManager has loaded.
///   3. Mental movers subscribe to <see cref="OnCognitiveStepDispatched"/>; physical movers
///      subscribe to <see cref="OnPhysicalStepDispatched"/>.
///   4. When a step is done, call <see cref="NotifyStepCompleted"/> with the stepId.
///
/// This class does NOT modify the existing move/dwell/Goal-Buffer/Imaginal-bubble logic inside
/// RagSequenceAgentMover — it only provides dispatch signals and completion gates.
/// </summary>
public class CognitivePhaseOrchestrator : MonoBehaviour
{
    // ── Per-zone registry (one orchestrator per zone) ─────────────────────────
    // Auto-creates when GetOrCreateForZone is called — no scene placement needed.
    private static readonly Dictionary<int, CognitivePhaseOrchestrator> _zoneInstances
        = new Dictionary<int, CognitivePhaseOrchestrator>();

    /// <summary>
    /// Returns the orchestrator for <paramref name="zoneIndex"/>, creating it automatically
    /// if it does not exist yet.  Each zone (0-3) gets its own independent orchestrator.
    /// </summary>
    public static CognitivePhaseOrchestrator GetOrCreateForZone(int zoneIndex)
    {
        if (_zoneInstances.TryGetValue(zoneIndex, out CognitivePhaseOrchestrator existing)
            && existing != null)
            return existing;

        // None yet for this zone — create one
        if (PlayModeQuitGuard.IsQuitting) return null;

        GameObject go = new GameObject($"CognitivePhaseOrchestrator_Zone{zoneIndex}");
        CognitivePhaseOrchestrator orch = go.AddComponent<CognitivePhaseOrchestrator>();
        orch.zoneIndex = zoneIndex;
        _zoneInstances[zoneIndex] = orch;
        Debug.Log($"[CognitivePhaseOrchestrator] Auto-created for zone {zoneIndex}.");
        return orch;
    }

    /// <summary>Backward-compat convenience — returns zone-0 orchestrator.</summary>
    public static CognitivePhaseOrchestrator Instance => GetOrCreateForZone(0);

    public static void ResetStaticRegistry()
    {
        _zoneInstances.Clear();
    }

    // ── Phase constants ───────────────────────────────────────────────────────
    public const int PHASE_IMAGINE    = 1;
    public const int PHASE_EXECUTE    = 2;
    public const int PHASE_TRANSITION = 3;

    // ── Public state ──────────────────────────────────────────────────────────
    /// <summary>Which zone (0-3) this orchestrator manages. -1 = all zones / single zone setup.</summary>
    public int zoneIndex = -1;

    /// <summary>Current execution phase (1=IMAGINE, 2=EXECUTE, 3=TRANSITION).</summary>
    public int CurrentPhase { get; private set; } = PHASE_IMAGINE;

    /// <summary>Current active subtask id (e.g. "st_0", "st_1").</summary>
    public string CurrentSubTaskId { get; private set; } = "st_0";

    /// <summary>True after Phase 1 barrier fires — matches legacy IsCognitivePhaseComplete semantics.</summary>
    public bool IsCognitivePhaseComplete { get; private set; }

    /// <summary>True once all steps across all subtasks are done.</summary>
    public bool IsAllComplete { get; private set; }

    // ── Events ────────────────────────────────────────────────────────────────
    /// <summary>Fired when a cognitive step (agentRole=="M") is ready to execute. Arg = stepId.</summary>
    public event Action<string> OnCognitiveStepDispatched;
    /// <summary>Fired when a physical step (agentRole=="P") is ready to execute. Arg = stepId.</summary>
    public event Action<string> OnPhysicalStepDispatched;
    /// <summary>Fired when a barrier step completes. Args = (barrierStepId, closesSubTaskId, opensSubTaskId).</summary>
    public event Action<string, string, string> OnBarrierReached;
    /// <summary>Fired when the current phase number changes (1, 2, or 3).</summary>
    public event Action<int> OnPhaseChanged;
    /// <summary>Fired every time any step completes. Arg = stepId.</summary>
    public event Action<string> OnStepCompleted;
    /// <summary>Fired once when all cognitive and physical steps in this zone are complete.</summary>
    public event Action<int> OnAllStepsCompleted;

    // ── Internal state ────────────────────────────────────────────────────────
    private bool _initialized;

    /// <summary>All steps (cognitive + physical) keyed by stepId.</summary>
    private Dictionary<string, ActionSequenceStep> _allSteps = new Dictionary<string, ActionSequenceStep>(256);

    /// <summary>Steps that have completed — the gate registry for dependsOn checks.</summary>
    private HashSet<string> _completedSteps = new HashSet<string>(64);

    /// <summary>Steps currently executing (dispatched but not yet completed).</summary>
    private HashSet<string> _activeSteps = new HashSet<string>(16);

    /// <summary>Ordered list of all step ids — preserves order for deterministic evaluation.</summary>
    private List<string> _stepOrder = new List<string>(256);

    /// <summary>Barriers from parallelSchedule, keyed by barrierStepId.</summary>
    private Dictionary<string, BarrierEntry> _barriers = new Dictionary<string, BarrierEntry>();

    /// <summary>SubTask entries keyed by subTaskId, used for transition detection.</summary>
    private Dictionary<string, SubTaskEntry> _subTasks = new Dictionary<string, SubTaskEntry>();

    /// <summary>For each step in subTasks[].stepIds, stores the immediately previous narrative step.</summary>
    private Dictionary<string, string> _narrativePreviousStepById = new Dictionary<string, string>();

    /// <summary>For each step in subTasks[].stepIds, stores the subtask it appears under.</summary>
    private Dictionary<string, string> _subTaskByNarrativeStepId = new Dictionary<string, string>();
    private ZoneDeclarativeMemory _zoneMemory;

    // Throttle: only evaluate ready steps every ~1 frame
    private float _evalThrottle;

    // Cached step counts for ML observation / HUD — invalidated when completion set or step registry changes.
    private bool _totalStepCountsDirty = true;
    private bool _completedStepCountsDirty = true;
    private int _cachedMentalTotal;
    private int _cachedPhysicalTotal;
    private int _cachedMentalCompleted;
    private int _cachedPhysicalCompleted;

    void InvalidateCompletedStepCounts()
    {
        _completedStepCountsDirty = true;
    }

    void InvalidateAllStepCountCaches()
    {
        _totalStepCountsDirty = true;
        _completedStepCountsDirty = true;
    }

    void RebuildTotalStepCountsIfNeeded()
    {
        if (!_totalStepCountsDirty) return;
        _cachedMentalTotal = 0;
        _cachedPhysicalTotal = 0;
        foreach (var kvp in _allSteps)
        {
            ActionSequenceStep step = kvp.Value;
            if (step == null) continue;
            bool mental = string.Equals(step.agentRole, "M", StringComparison.OrdinalIgnoreCase)
                       || IsCognitiveStepByTarget(step.targetObjectId);
            if (mental) _cachedMentalTotal++;
            else _cachedPhysicalTotal++;
        }
        _totalStepCountsDirty = false;
    }

    void RebuildCompletedStepCountsIfNeeded()
    {
        if (!_completedStepCountsDirty) return;
        _cachedMentalCompleted = 0;
        _cachedPhysicalCompleted = 0;
        foreach (string stepId in _completedSteps)
        {
            if (!_allSteps.TryGetValue(stepId, out ActionSequenceStep step)) continue;
            bool mental = string.Equals(step.agentRole, "M", StringComparison.OrdinalIgnoreCase)
                       || IsCognitiveStepByTarget(step.targetObjectId);
            if (mental) _cachedMentalCompleted++;
            else _cachedPhysicalCompleted++;
        }
        _completedStepCountsDirty = false;
    }

    // ── Nested data structs ───────────────────────────────────────────────────
    private struct BarrierEntry
    {
        public string barrierStepId;
        public string closesSubTaskId;
        public string opensSubTaskId;
        public float atTimeSec;
    }

    private struct SubTaskEntry
    {
        public string subTaskId;
        public string barrierStepId;
        public string dependsOnSubTask;
    }

    // ── Unity lifecycle ───────────────────────────────────────────────────────
    void Awake()
    {
        // Register in zone registry (zoneIndex may be set before Awake by GetOrCreateForZone)
        if (!_zoneInstances.ContainsKey(zoneIndex) || _zoneInstances[zoneIndex] == null)
            _zoneInstances[zoneIndex] = this;
    }

    void OnDestroy()
    {
        if (_zoneInstances.TryGetValue(zoneIndex, out CognitivePhaseOrchestrator stored) && stored == this)
            _zoneInstances.Remove(zoneIndex);
    }

    void Update()
    {
        if (!_initialized) return;
        if (IsAllComplete) return;
        if (Time.time < _evalThrottle) return;
        _evalThrottle = Time.time + 0.05f; // ~20 Hz evaluation

        EvaluateReadySteps();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Initializes the orchestrator with step data from <see cref="AgentSequenceManager"/>.
    /// Call this once after AgentSequenceManager has fully loaded its sequences.
    /// <paramref name="cognitiveAgentId"/> is the mental agent (e.g. "M1").
    /// <paramref name="physicalAgentId"/> is the physical agent (e.g. "P1").
    /// Pass the JSON text for barrier/subtask metadata parsing.
    /// </summary>
    public bool IsInitialized => _initialized;

    /// <summary>
    /// Inference / editor: init DAG when mental <see cref="RagSequenceAgentMover"/> is disabled and cannot call <see cref="RagSequenceAgentMover.TryInitializeOrchestrator"/>.
    /// </summary>
    public bool EnsureInitializedFromSceneAgents()
    {
        if (_initialized) return true;

        string mentalId = null;
        string physicalId = null;
        RagSequenceAgentMover[] movers = UnityEngine.Object.FindObjectsOfType<RagSequenceAgentMover>(true);
        foreach (RagSequenceAgentMover m in movers)
        {
            if (m == null || m.zoneIndex != zoneIndex) continue;
            if (m.isMentalAgent && string.IsNullOrEmpty(mentalId))
                mentalId = m.agentId;
            else if (!m.isMentalAgent && string.IsNullOrEmpty(physicalId))
                physicalId = m.agentId;
        }

        if (string.IsNullOrEmpty(mentalId))
            mentalId = $"M{Mathf.Clamp(zoneIndex, 0, 3) + 1}";
        if (string.IsNullOrEmpty(physicalId))
            physicalId = $"P{Mathf.Clamp(zoneIndex, 0, 3) + 1}";

        string json = "";
        SceneUILoader loader = UnityEngine.Object.FindObjectOfType<SceneUILoader>();
        if (loader != null)
        {
            if (!string.IsNullOrEmpty(loader.MergedRawRagJson))
                json = loader.MergedRawRagJson;
            else if (!string.IsNullOrEmpty(loader.RawJsonText))
                json = loader.RawJsonText;
            else if (!string.IsNullOrEmpty(loader.EffectivePipelineJson))
                json = loader.EffectivePipelineJson;
        }

        Initialize(mentalId, physicalId, json);
        return _initialized;
    }

    public void Initialize(string cognitiveAgentId, string physicalAgentId, string fullJson)
    {
        _allSteps.Clear();
        _completedSteps.Clear();
        _activeSteps.Clear();
        _stepOrder.Clear();
        _barriers.Clear();
        _subTasks.Clear();
        _narrativePreviousStepById.Clear();
        _subTaskByNarrativeStepId.Clear();
        IsCognitivePhaseComplete = false;
        IsAllComplete = false;
        CurrentPhase = PHASE_IMAGINE;
        CurrentSubTaskId = "st_0";

        var mgr = AgentSequenceManager.Instance;
        if (mgr == null)
        {
            Debug.LogError("[CognitivePhaseOrchestrator] AgentSequenceManager not found.");
            return;
        }

        // Cognitive steps
        AgentSequenceData cogSeq = mgr.GetCognitiveSequence(cognitiveAgentId);
        if (cogSeq != null)
        {
            foreach (var step in cogSeq.actionSequence)
            {
                if (string.IsNullOrEmpty(step.stepId)) continue;
                if (string.IsNullOrEmpty(step.agentRole)) step.agentRole = "M";
                _allSteps[step.stepId] = step;
                _stepOrder.Add(step.stepId);
            }
        }

        // Physical steps (from physicalAgents[].steps which have full dependency data)
        AgentSequenceData physSeq = mgr.GetPhysicalSequence(physicalAgentId);
        if (physSeq == null)
        {
            // Fallback: try agentProfiles actionSequence
            physSeq = mgr.GetSequence(physicalAgentId);
            Debug.LogWarning($"[CognitivePhaseOrchestrator] physicalAgents steps not found for '{physicalAgentId}'; using actionSequence fallback.");
        }
        if (physSeq != null)
        {
            foreach (var step in physSeq.actionSequence)
            {
                if (string.IsNullOrEmpty(step.stepId)) continue;
                if (string.IsNullOrEmpty(step.agentRole)) step.agentRole = "P";
                if (!_allSteps.ContainsKey(step.stepId))
                {
                    _allSteps[step.stepId] = step;
                    _stepOrder.Add(step.stepId);
                }
            }
        }

        SceneUILoader loader = UnityEngine.Object.FindObjectOfType<SceneUILoader>();
        if (loader != null && loader.PerZoneRawRagJson != null && loader.PerZoneRawRagJson.Length > 0)
        {
            foreach (string zoneRaw in loader.PerZoneRawRagJson)
            {
                if (!string.IsNullOrEmpty(zoneRaw))
                    ParseBarriersAndSubTasks(zoneRaw);
            }
        }
        else if (!string.IsNullOrEmpty(fullJson))
        {
            ParseBarriersAndSubTasks(fullJson);
        }

        if (_allSteps.Count > 0)
            NormalizeMalformedDependenciesFromNarrativeOrder();

        _zoneMemory = ResolveZoneMemory();

        _initialized = true;
        InvalidateAllStepCountCaches();
        RebuildTotalStepCountsIfNeeded();
        RebuildCompletedStepCountsIfNeeded();
        Debug.Log($"[CognitivePhaseOrchestrator] Initialized zone={zoneIndex}: {_allSteps.Count} total steps " +
                  $"({cogSeq?.actionSequence?.Count ?? 0} cognitive, {physSeq?.actionSequence?.Count ?? 0} physical), " +
                  $"{_barriers.Count} barriers, {_subTasks.Count} subtasks.");
        EvaluateReadySteps();
    }

    /// <summary>
    /// Clears DAG completion state so the zone can run again after ML-Agents <see cref="Unity.MLAgents.Agent.EndEpisode"/>.
    /// Does not reload JSON — reuse definitions from the prior <see cref="Initialize"/> call.
    /// </summary>
    static void SetZoneDeclarativeCognitiveReady(int zoneIndex, string reason)
    {
        if (RagInferenceSceneController.DeferPhysicalUntilLeaderCognitiveDone()
            && reason != null
            && reason.IndexOf("leader", StringComparison.OrdinalIgnoreCase) < 0
            && reason.IndexOf("fast-forward", StringComparison.OrdinalIgnoreCase) < 0
            && reason.IndexOf("all steps", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return;
        }

        ZoneDeclarativeMemory mem = ZoneDeclarativeMemory.ForZone(zoneIndex);
        if (mem == null)
        {
            ZoneDeclarativeMemory.RebuildRegistryFromScene();
            mem = ZoneDeclarativeMemory.ForZone(zoneIndex);
        }
        if (mem != null && !mem.cognitiveReady)
        {
            mem.cognitiveReady = true;
            Debug.Log($"[CognitivePhaseOrchestrator] Zone {zoneIndex} cognitiveReady=true ({reason}).");
        }
    }

    /// <summary>After zone leader finishes cognitive walk — unlock physical without marking every cognitive step done.</summary>
    public void NotifyCognitivePhaseCompleteForPhysicalUnlock()
    {
        if (!_initialized) return;

        IsCognitivePhaseComplete = true;
        if (CurrentPhase == PHASE_IMAGINE)
        {
            SetPhase(PHASE_EXECUTE);
            CurrentSubTaskId = "st_1";
        }

        SetZoneDeclarativeCognitiveReady(zoneIndex, "leader cognitive complete");
        EvaluateReadySteps();
        Debug.Log($"[CognitivePhaseOrchestrator] Zone {zoneIndex}: execute phase — evaluating physical DAG steps.");
    }

    /// <summary>
    /// Inference scene (Phase 5): skip visible/scripted cognitive execution; unlock physical ONNX steps.
    /// Marks all cognitive DAG steps complete and advances to execute phase without dispatching mental locomotion.
    /// </summary>
    public void CompleteCognitivePhaseForInference()
    {
        if (!_initialized || _allSteps.Count == 0)
            return;

        foreach (var kvp in _allSteps)
        {
            ActionSequenceStep step = kvp.Value;
            if (step == null || string.IsNullOrEmpty(step.stepId)) continue;

            bool isCognitive = string.Equals(step.agentRole, "M", StringComparison.OrdinalIgnoreCase)
                               || IsCognitiveStepByTarget(step.targetObjectId);
            if (!isCognitive) continue;

            if (!_completedSteps.Contains(step.stepId))
            {
                _completedSteps.Add(step.stepId);
                step.isStepCompleted = true;
                step.isActivated = false;
            }
            _activeSteps.Remove(step.stepId);
        }

        IsCognitivePhaseComplete = true;
        SetPhase(PHASE_EXECUTE);
        CurrentSubTaskId = "st_1";
        _completedStepCountsDirty = true;
        RebuildCompletedStepCountsIfNeeded();

        SetZoneDeclarativeCognitiveReady(zoneIndex, "MarkCognitivePhaseCompleteForInference");

        SeedPayloadsForInferenceFastForward();

        Debug.Log($"[CognitivePhaseOrchestrator] Zone {zoneIndex}: cognitive phase fast-forwarded for inference ({_cachedMentalCompleted}/{_cachedMentalTotal} mental steps marked done).");

        EvaluateReadySteps();
    }

    /// <summary>
    /// Inference skips scripted cognitive visits; replay payload + imaginal side effects so physical steps with consumesPayload can dispatch.
    /// </summary>
    void SeedPayloadsForInferenceFastForward()
    {
        ZoneDeclarativeMemory mem = ResolveZoneMemory();
        if (mem == null)
        {
            Debug.LogWarning($"[CognitivePhaseOrchestrator] Zone {zoneIndex}: no ZoneDeclarativeMemory — payload gates may block physical steps after the first.");
            return;
        }

        int seeded = 0;
        foreach (string stepId in _completedSteps)
        {
            if (!_allSteps.TryGetValue(stepId, out ActionSequenceStep step) || step == null)
                continue;

            if (ProductionMemoryRuleEngine.IsProductionMemoryStep(step))
                ProductionMemoryRuleEngine.TryEvaluateAndRecord(step, mem, out _);

            if (ImaginalBufferStepHelper.IsImaginalBufferStep(step))
                mem.RecordImaginalTransition(step);

            if (string.IsNullOrWhiteSpace(step.producesPayload))
                continue;

            string value = BuildInferencePayloadValue(step);
            mem.RecordProducedPayload(step.stepId, step.producesPayload, value);
            seeded++;
        }

        if (seeded > 0)
            Debug.Log($"[CognitivePhaseOrchestrator] Zone {zoneIndex}: seeded {seeded} payload slot(s) for inference DAG.");
    }

    static string BuildInferencePayloadValue(ActionSequenceStep step)
    {
        if (step == null) return "";
        if (!string.IsNullOrWhiteSpace(step.description))
            return step.description;
        if (!string.IsNullOrWhiteSpace(step.currentCognitiveState))
        {
            string target = !string.IsNullOrWhiteSpace(step.targetObjectName)
                ? step.targetObjectName
                : step.targetObjectId;
            return string.IsNullOrWhiteSpace(target)
                ? $"{step.currentCognitiveState} complete"
                : $"{step.currentCognitiveState} complete at {target}";
        }
        return string.IsNullOrWhiteSpace(step.stepId) ? "step complete" : $"{step.stepId} complete";
    }

    public void ResetProgressForMlAgentsEpisodeRestart()
    {
        if (!_initialized || _allSteps.Count == 0)
            return;

        _completedSteps.Clear();
        _activeSteps.Clear();
        IsAllComplete = false;
        IsCognitivePhaseComplete = false;
        CurrentPhase = PHASE_IMAGINE;
        CurrentSubTaskId = "st_0";
        _evalThrottle = 0f;

        foreach (ActionSequenceStep step in _allSteps.Values)
        {
            if (step == null) continue;
            step.isStepCompleted = false;
            step.isActivated = false;
        }

        InvalidateCompletedStepCounts();
        RebuildCompletedStepCountsIfNeeded();

        Debug.Log($"[CognitivePhaseOrchestrator] Zone {zoneIndex}: reset DAG progress for new ML episode.");
    }

    /// <summary>
    /// Notify that a step has completed. Call this from the agent mover when dwell finishes.
    /// Thread-safe: queues completion to be processed on the next Update tick.
    /// </summary>
    public void NotifyStepCompleted(string stepId)
    {
        if (string.IsNullOrEmpty(stepId)) return;

        if (_completedSteps.Contains(stepId))
        {
            Debug.LogWarning($"[CognitivePhaseOrchestrator] Step '{stepId}' already marked completed — ignoring duplicate.");
            return;
        }

        _completedSteps.Add(stepId);
        _activeSteps.Remove(stepId);

        ActionSequenceStep step = null;
        if (_allSteps.TryGetValue(stepId, out ActionSequenceStep foundStep))
        {
            step = foundStep;
            step.isStepCompleted = true;
        }

        Debug.Log($"[CognitivePhaseOrchestrator] ✅ Step completed: {stepId} | completed={_completedSteps.Count} active={_activeSteps.Count}");

        _completedStepCountsDirty = true;

        OnStepCompleted?.Invoke(stepId);

        HandleBarrierIfNeeded(stepId, step);
        CheckAllComplete();

        // Dispatch interleaved physical/cognitive successors immediately (e.g. t01_phy_s14 after t01_cog_s13).
        if (_initialized && !IsAllComplete)
            EvaluateReadySteps();
    }

    /// <summary>
    /// Returns the step data for a given stepId, or null if not in the registry.
    /// </summary>
    public ActionSequenceStep GetStep(string stepId)
    {
        _allSteps.TryGetValue(stepId, out ActionSequenceStep s);
        return s;
    }

    /// <summary>Returns true if stepId has been completed.</summary>
    public bool IsStepCompleted(string stepId) => _completedSteps.Contains(stepId);

    /// <summary>Returns true if stepId is currently executing.</summary>
    public bool IsStepActive(string stepId) => _activeSteps.Contains(stepId);

    /// <summary>First active physical DAG step (for inference recovery when dispatch event was missed).</summary>
    public bool TryGetFirstActivePhysicalStepId(out string stepId)
    {
        stepId = null;
        foreach (string id in _activeSteps)
        {
            if (!_allSteps.TryGetValue(id, out ActionSequenceStep step) || step == null)
                continue;
            if (!IsPhysicalStep(step))
                continue;
            stepId = id;
            return true;
        }
        return false;
    }

    /// <summary>First active cognitive DAG step (for late-bound mental movers).</summary>
    public bool TryGetFirstActiveCognitiveStepId(out string stepId)
    {
        stepId = null;
        foreach (string id in _activeSteps)
        {
            if (!_allSteps.TryGetValue(id, out ActionSequenceStep step) || step == null)
                continue;
            if (!IsCognitiveStep(step))
                continue;
            stepId = id;
            return true;
        }
        return false;
    }

    static bool IsPhysicalStep(ActionSequenceStep step)
    {
        return string.Equals(step.agentRole, "P", StringComparison.OrdinalIgnoreCase)
               || (!IsCognitiveStepByTarget(step.targetObjectId)
                   && !string.Equals(step.agentRole, "M", StringComparison.OrdinalIgnoreCase));
    }

    static bool IsCognitiveStep(ActionSequenceStep step)
    {
        return string.Equals(step.agentRole, "M", StringComparison.OrdinalIgnoreCase)
               || IsCognitiveStepByTarget(step.targetObjectId);
    }

    /// <summary>
    /// Counts completed mental and physical steps for this orchestrator's zone.
    /// Used by lightweight HUDs/debug indicators.
    /// </summary>
    public void GetCompletedStepCounts(out int mentalCompleted, out int physicalCompleted)
    {
        RebuildCompletedStepCountsIfNeeded();
        mentalCompleted = _cachedMentalCompleted;
        physicalCompleted = _cachedPhysicalCompleted;
    }

    /// <summary>
    /// Counts total known mental and physical steps for this orchestrator's zone.
    /// </summary>
    public void GetTotalStepCounts(out int mentalTotal, out int physicalTotal)
    {
        RebuildTotalStepCountsIfNeeded();
        mentalTotal = _cachedMentalTotal;
        physicalTotal = _cachedPhysicalTotal;
    }

    // ── Internal evaluation ───────────────────────────────────────────────────

    void EvaluateReadySteps()
    {
        foreach (string stepId in _stepOrder)
        {
            if (_completedSteps.Contains(stepId)) continue;
            if (_activeSteps.Contains(stepId)) continue;

            if (!_allSteps.TryGetValue(stepId, out ActionSequenceStep step)) continue;

            if (!AreAllDependenciesMet(step)) continue;

            // Only allow Phase 1 cognitive steps until the Phase 1 barrier fires
            if (CurrentPhase == PHASE_IMAGINE && !IsPhase1Step(step)) continue;

            if (!AreAllPayloadsAvailable(step)) continue;
            if (!IsImaginalStateReady(step)) continue;
            if (!PrepareProductionMemoryStep(step)) continue;

            DispatchStep(step);
        }
    }

    bool AreAllDependenciesMet(ActionSequenceStep step)
    {
        if (step.dependsOn == null || step.dependsOn.Length == 0) return true;

        // Safety net: if a malformed JSON edge still points to a future step, repair it
        // lazily before deciding readiness. This prevents runtime stalls even if the
        // initialization normalization was skipped by hot reload / stale scene state.
        if (TryRepairFutureDependencyForStep(step)
            && (step.dependsOn == null || step.dependsOn.Length == 0))
        {
            return true;
        }

        foreach (string dep in step.dependsOn)
        {
            if (!_completedSteps.Contains(dep)) return false;
        }
        return true;
    }

    bool AreAllPayloadsAvailable(ActionSequenceStep step)
    {
        if (step == null || step.consumesPayload == null || step.consumesPayload.Length == 0) return true;

        ZoneDeclarativeMemory mem = ResolveZoneMemory();
        if (mem == null)
        {
            Debug.LogWarning($"[CognitivePhaseOrchestrator] No ZoneDeclarativeMemory for zone={zoneIndex}; allowing payload gate for {step.stepId}.");
            return true;
        }

        return mem.HasPayloads(step.consumesPayload);
    }

    bool IsImaginalStateReady(ActionSequenceStep step)
    {
        if (!ImaginalBufferStepHelper.IsImaginalBufferStep(step)) return true;
        if (string.IsNullOrWhiteSpace(step.imaginalStateBefore)) return true;

        ZoneDeclarativeMemory mem = ResolveZoneMemory();
        if (mem == null)
        {
            Debug.LogWarning($"[CognitivePhaseOrchestrator] No ZoneDeclarativeMemory for Imaginal state gate on {step.stepId}; allowing.");
            return true;
        }

        bool ready = mem.CanEnterImaginalState(step.imaginalStateBefore);
        if (!ready)
        {
            Debug.Log($"[CognitivePhaseOrchestrator] Imaginal state gate waiting for {step.stepId}: need={step.imaginalStateBefore}, current={mem.imaginalBufferState}");
        }
        return ready;
    }

    bool PrepareProductionMemoryStep(ActionSequenceStep step)
    {
        if (!ProductionMemoryRuleEngine.IsProductionMemoryStep(step)) return true;

        ZoneDeclarativeMemory mem = ResolveZoneMemory();
        if (mem == null)
        {
            Debug.LogWarning($"[CognitivePhaseOrchestrator] Production Memory step {step.stepId} has no zone memory; dispatching without command record.");
            return true;
        }

        bool ok = ProductionMemoryRuleEngine.TryEvaluateAndRecord(step, mem, out ProductionMemoryCommandRecord command);
        if (ok && command != null)
            Debug.Log($"[CognitivePhaseOrchestrator] Production Memory command: {step.stepId} → {command.commandType} ({command.payloadKey})");
        return ok;
    }

    ZoneDeclarativeMemory ResolveZoneMemory()
    {
        if (_zoneMemory != null) return _zoneMemory;

        if (zoneIndex >= 0)
        {
            _zoneMemory = ZoneDeclarativeMemory.ForZone(zoneIndex);
            if (_zoneMemory == null)
            {
                ZoneDeclarativeMemory.RebuildRegistryFromScene();
                _zoneMemory = ZoneDeclarativeMemory.ForZone(zoneIndex);
            }
        }

        return _zoneMemory;
    }

    bool IsPhase1Step(ActionSequenceStep step)
    {
        return string.Equals(step.subTaskId, "st_0", StringComparison.Ordinal);
    }

    void DispatchStep(ActionSequenceStep step)
    {
        _activeSteps.Add(step.stepId);
        step.isActivated = true;

        bool isCognitive = string.Equals(step.agentRole, "M", StringComparison.OrdinalIgnoreCase)
                        || IsCognitiveStepByTarget(step.targetObjectId);

        if (isCognitive)
        {
            Debug.Log($"[CognitivePhaseOrchestrator] 🧠 Dispatching COGNITIVE step: {step.stepId} → {step.targetObjectId} ({step.currentCognitiveState})");
            OnCognitiveStepDispatched?.Invoke(step.stepId);
        }
        else
        {
            Debug.Log($"[CognitivePhaseOrchestrator] ⚙️ Dispatching PHYSICAL step: {step.stepId} → {step.targetObjectId} ({step.actionVerb})");
            OnPhysicalStepDispatched?.Invoke(step.stepId);
        }
    }

    static bool IsCognitiveStepByTarget(string targetId)
    {
        if (string.IsNullOrEmpty(targetId)) return false;
        // cognitive_ prefix or strip zone suffix
        if (targetId.StartsWith("cognitive_", StringComparison.OrdinalIgnoreCase)) return true;
        int zIdx = targetId.IndexOf("_zone", StringComparison.OrdinalIgnoreCase);
        if (zIdx > 0)
        {
            string baseId = targetId.Substring(0, zIdx);
            return baseId.StartsWith("cognitive_", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    void HandleBarrierIfNeeded(string stepId, ActionSequenceStep completedStep)
    {
        if (!_barriers.TryGetValue(stepId, out BarrierEntry barrier))
        {
            if (completedStep == null || !completedStep.isBarrier) return;

            barrier = new BarrierEntry
            {
                barrierStepId = stepId,
                closesSubTaskId = completedStep.subTaskId,
                opensSubTaskId = FindNextSubTaskId(completedStep.subTaskId)
            };
        }

        string closes = barrier.closesSubTaskId;
        string opens  = barrier.opensSubTaskId;

        Debug.Log($"[CognitivePhaseOrchestrator] 🚧 BARRIER reached: {stepId} | closes={closes} opens={opens}");

        // Phase 1 barrier: the st_0 barrier marks imagination complete
        if (string.Equals(closes, "st_0", StringComparison.Ordinal))
        {
            IsCognitivePhaseComplete = true;
            SetPhase(PHASE_EXECUTE);
            if (!RagInferenceSceneController.DeferPhysicalUntilLeaderCognitiveDone())
                SetZoneDeclarativeCognitiveReady(zoneIndex, $"barrier {stepId}");
            else
                Debug.Log($"[CognitivePhaseOrchestrator] Zone {zoneIndex}: st_0 barrier → execute phase (physical unlock deferred until leader cognitive done).");
        }
        else if (!string.IsNullOrEmpty(opens))
        {
            // Phase 3 → back to Phase 2 for the next subtask
            SetPhase(PHASE_TRANSITION);
        }

        CurrentSubTaskId = opens ?? CurrentSubTaskId;

        OnBarrierReached?.Invoke(stepId, closes, opens ?? "");

        // After Phase 3 notification, transition immediately back to Phase 2
        if (CurrentPhase == PHASE_TRANSITION)
            SetPhase(PHASE_EXECUTE);
    }

    string FindNextSubTaskId(string currentSubTaskId)
    {
        if (string.IsNullOrEmpty(currentSubTaskId)) return "";

        if (string.Equals(currentSubTaskId, "st_0", StringComparison.Ordinal)) return "st_1";

        if (currentSubTaskId.StartsWith("st_", StringComparison.Ordinal)
            && int.TryParse(currentSubTaskId.Substring(3), out int n))
        {
            string next = $"st_{n + 1}";
            return _subTasks.Count == 0 || _subTasks.ContainsKey(next) ? next : "";
        }

        return "";
    }

    void SetPhase(int newPhase)
    {
        if (CurrentPhase == newPhase) return;
        CurrentPhase = newPhase;
        Debug.Log($"[CognitivePhaseOrchestrator] 🔄 Phase → {PhaseName(newPhase)}");
        OnPhaseChanged?.Invoke(newPhase);
    }

    void CheckAllComplete()
    {
        if (IsAllComplete) return;
        if (_allSteps.Count == 0) return;
        if (_completedSteps.Count >= _allSteps.Count)
        {
            IsAllComplete = true;
            Debug.Log("[CognitivePhaseOrchestrator] 🎉 All steps completed.");
            OnAllStepsCompleted?.Invoke(zoneIndex);
        }
    }

    // ── JSON barrier + subtask parsing ────────────────────────────────────────

    void ParseBarriersAndSubTasks(string json)
    {
        ParseBarriersFromJson(json);
        ParseSubTasksFromJson(json);
    }

    void ParseBarriersFromJson(string json)
    {
        // Look for parallelSchedule.barriers array
        int schedStart = json.IndexOf("\"parallelSchedule\":");
        if (schedStart == -1) schedStart = 0;

        int barriersKey = json.IndexOf("\"barriers\":", schedStart);
        if (barriersKey == -1) return;

        int arrayStart = json.IndexOf("[", barriersKey);
        if (arrayStart == -1) return;

        int arrayEnd = FindMatchingBracket(json, arrayStart);
        if (arrayEnd == -1) return;

        string arrJson = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
        int pos = 0;
        while (pos < arrJson.Length)
        {
            while (pos < arrJson.Length && (arrJson[pos] == ' ' || arrJson[pos] == '\n' || arrJson[pos] == '\r' || arrJson[pos] == '\t' || arrJson[pos] == ',')) pos++;
            if (pos >= arrJson.Length) break;
            if (arrJson[pos] != '{') { pos++; continue; }

            int end = FindMatchingBrace(arrJson, pos);
            if (end == -1) break;

            string obj = arrJson.Substring(pos, end - pos + 1);
            string bId    = ExtractString(obj, "barrierStepId");
            string closes = ExtractString(obj, "closesSubTaskId");
            string opens  = ExtractString(obj, "opensSubTaskId");
            float  atTime = ExtractFloat(obj, "atTimeSec");

            if (!string.IsNullOrEmpty(bId))
            {
                _barriers[bId] = new BarrierEntry { barrierStepId = bId, closesSubTaskId = closes, opensSubTaskId = opens, atTimeSec = atTime };
            }

            pos = end + 1;
        }
    }

    void ParseSubTasksFromJson(string json)
    {
        int subTasksKey = json.IndexOf("\"subTasks\":");
        if (subTasksKey == -1) return;

        int arrayStart = json.IndexOf("[", subTasksKey);
        if (arrayStart == -1) return;

        int arrayEnd = FindMatchingBracket(json, arrayStart);
        if (arrayEnd == -1) return;

        string arrJson = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
        int pos = 0;
        while (pos < arrJson.Length)
        {
            while (pos < arrJson.Length && (arrJson[pos] == ' ' || arrJson[pos] == '\n' || arrJson[pos] == '\r' || arrJson[pos] == '\t' || arrJson[pos] == ',')) pos++;
            if (pos >= arrJson.Length) break;
            if (arrJson[pos] != '{') { pos++; continue; }

            int end = FindMatchingBrace(arrJson, pos);
            if (end == -1) break;

            string obj = arrJson.Substring(pos, end - pos + 1);
            string stId     = ExtractString(obj, "subTaskId");
            string bStep    = ExtractString(obj, "barrierStepId");
            string depends  = ExtractString(obj, "dependsOnSubTask");
            string[] stepIds = ExtractStringArray(obj, "stepIds");

            if (!string.IsNullOrEmpty(stId))
            {
                _subTasks[stId] = new SubTaskEntry { subTaskId = stId, barrierStepId = bStep, dependsOnSubTask = depends };

                for (int i = 0; i < stepIds.Length; i++)
                {
                    string sid = stepIds[i];
                    if (string.IsNullOrEmpty(sid)) continue;
                    _subTaskByNarrativeStepId[sid] = stId;
                    if (i > 0 && !string.IsNullOrEmpty(stepIds[i - 1]))
                        _narrativePreviousStepById[sid] = stepIds[i - 1];
                }

                // Also register barrier in _barriers if a barrier step exists for this subtask
                // and it isn't already in _barriers (some JSONs only declare in subTasks)
                if (!string.IsNullOrEmpty(bStep) && !_barriers.ContainsKey(bStep))
                {
                    // Find the next subtask to determine opensSubTaskId
                    // (populated later once all subTasks are parsed)
                    _barriers[bStep] = new BarrierEntry { barrierStepId = bStep, closesSubTaskId = stId };
                }
            }

            pos = end + 1;
        }

        // Second pass: fill opensSubTaskId for barriers derived from subTasks
        string[] stIds = new string[_subTasks.Count];
        _subTasks.Keys.CopyTo(stIds, 0);
        System.Array.Sort(stIds, StringComparer.Ordinal);
        for (int i = 0; i < stIds.Length; i++)
        {
            SubTaskEntry st = _subTasks[stIds[i]];
            if (!string.IsNullOrEmpty(st.barrierStepId) && _barriers.ContainsKey(st.barrierStepId))
            {
                BarrierEntry b = _barriers[st.barrierStepId];
                if (string.IsNullOrEmpty(b.opensSubTaskId) && i + 1 < stIds.Length)
                {
                    b.opensSubTaskId = stIds[i + 1];
                    _barriers[st.barrierStepId] = b;
                }
            }
        }
    }

    /// <summary>
    /// The current RAG file has a few physical steps whose dependency points into the future
    /// even though subTasks[].stepIds places the physical step earlier in the narrative.
    /// Example: t01_phy_s45 depends on t01_cog_s54, while t01_cog_s46 depends on t01_phy_s45.
    /// That creates a hard deadlock. Repair only future-pointing dependencies using the
    /// canonical narrative predecessor from subTasks[].stepIds.
    /// </summary>
    void NormalizeMalformedDependenciesFromNarrativeOrder()
    {
        int subTaskRepairs = 0;
        int barrierFlagRepairs = 0;
        int repairs = 0;

        foreach (var kvp in _subTaskByNarrativeStepId)
        {
            if (!_allSteps.TryGetValue(kvp.Key, out ActionSequenceStep narrativeStep) || narrativeStep == null)
                continue;

            if (!string.Equals(narrativeStep.subTaskId, kvp.Value, StringComparison.Ordinal))
            {
                string oldSubTask = narrativeStep.subTaskId;
                narrativeStep.subTaskId = kvp.Value;
                subTaskRepairs++;
                Debug.LogWarning($"[CognitivePhaseOrchestrator] Repaired subTaskId for {narrativeStep.stepId}: {oldSubTask} → {kvp.Value}");
            }

            if (_barriers.ContainsKey(narrativeStep.stepId) && !narrativeStep.isBarrier)
            {
                narrativeStep.isBarrier = true;
                barrierFlagRepairs++;
                Debug.LogWarning($"[CognitivePhaseOrchestrator] Marked scheduled barrier step as isBarrier=true: {narrativeStep.stepId}");
            }
        }

        foreach (var kvp in _allSteps)
        {
            ActionSequenceStep step = kvp.Value;
            if (step == null || step.dependsOn == null || step.dependsOn.Length == 0) continue;
            if (!_narrativePreviousStepById.TryGetValue(step.stepId, out string narrativePrev)) continue;
            if (string.IsNullOrEmpty(narrativePrev)) continue;

            bool hasFutureDependency = false;
            foreach (string dep in step.dependsOn)
            {
                if (string.IsNullOrEmpty(dep)) continue;
                if (_allSteps.TryGetValue(dep, out ActionSequenceStep depStep)
                    && depStep != null
                    && depStep.stepOrder > step.stepOrder)
                {
                    hasFutureDependency = true;
                    break;
                }
            }

            if (!hasFutureDependency) continue;

            string oldDeps = string.Join(",", step.dependsOn);
            ApplyNarrativeDependencyRepair(step, narrativePrev, oldDeps);
            repairs++;
        }

        if (repairs > 0)
            Debug.LogWarning($"[CognitivePhaseOrchestrator] Applied {repairs} narrative-order dependency repair(s).");
        if (subTaskRepairs > 0)
            Debug.LogWarning($"[CognitivePhaseOrchestrator] Applied {subTaskRepairs} narrative subTaskId repair(s).");
        if (barrierFlagRepairs > 0)
            Debug.LogWarning($"[CognitivePhaseOrchestrator] Applied {barrierFlagRepairs} barrier flag repair(s).");
    }

    bool TryRepairFutureDependencyForStep(ActionSequenceStep step)
    {
        if (step == null || step.dependsOn == null || step.dependsOn.Length == 0) return false;

        string narrativePrev = "";
        if (!_narrativePreviousStepById.TryGetValue(step.stepId, out narrativePrev)
            || string.IsNullOrEmpty(narrativePrev))
        {
            // Known malformed edge in basicUI_ml2.json. Keep this as a last-resort
            // compatibility fallback in case subTasks[].stepIds was not parsed.
            if (string.Equals(step.stepId, "t01_phy_s45", StringComparison.Ordinal))
                narrativePrev = "t01_cog_s44";
        }

        if (string.IsNullOrEmpty(narrativePrev)) return false;

        bool hasFutureDependency = false;
        foreach (string dep in step.dependsOn)
        {
            if (string.IsNullOrEmpty(dep)) continue;
            if (_allSteps.TryGetValue(dep, out ActionSequenceStep depStep)
                && depStep != null
                && depStep.stepOrder > step.stepOrder)
            {
                hasFutureDependency = true;
                break;
            }
        }

        if (!hasFutureDependency) return false;

        string oldDeps = string.Join(",", step.dependsOn);
        ApplyNarrativeDependencyRepair(step, narrativePrev, oldDeps);
        return true;
    }

    void ApplyNarrativeDependencyRepair(ActionSequenceStep step, string narrativePrev, string oldDeps)
    {
        step.dependsOn = new[] { narrativePrev };

        if (_subTaskByNarrativeStepId.TryGetValue(step.stepId, out string narrativeSubTask)
            && !string.IsNullOrEmpty(narrativeSubTask))
        {
            step.subTaskId = narrativeSubTask;
        }

        Debug.LogWarning($"[CognitivePhaseOrchestrator] Repaired future dependency for {step.stepId}: [{oldDeps}] → [{narrativePrev}] (subTask={step.subTaskId})");
    }

    // ── JSON parsing helpers (local, lightweight) ─────────────────────────────

    static string ExtractString(string json, string key)
    {
        string k = $"\"{key}\":";
        int idx = json.IndexOf(k);
        if (idx == -1) return "";
        idx += k.Length;
        while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;
        if (idx >= json.Length || json[idx] == 'n') return ""; // null
        if (json[idx] != '"') return "";
        idx++;
        int end = json.IndexOf('"', idx);
        if (end == -1) return "";
        return json.Substring(idx, end - idx);
    }

    static string[] ExtractStringArray(string json, string key)
    {
        string k = $"\"{key}\":";
        int idx = json.IndexOf(k);
        if (idx == -1) return new string[0];
        idx += k.Length;
        while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;
        if (idx >= json.Length || json[idx] != '[') return new string[0];

        int end = FindMatchingBracket(json, idx);
        if (end == -1) return new string[0];

        string content = json.Substring(idx + 1, end - idx - 1);
        var values = new List<string>();
        int pos = 0;
        while (pos < content.Length)
        {
            while (pos < content.Length && (char.IsWhiteSpace(content[pos]) || content[pos] == ',')) pos++;
            if (pos >= content.Length) break;
            if (content[pos] != '"') break;

            pos++;
            int valueEnd = content.IndexOf('"', pos);
            if (valueEnd == -1) break;
            values.Add(content.Substring(pos, valueEnd - pos));
            pos = valueEnd + 1;
        }

        return values.ToArray();
    }

    static float ExtractFloat(string json, string key)
    {
        string k = $"\"{key}\":";
        int idx = json.IndexOf(k);
        if (idx == -1) return 0f;
        idx += k.Length;
        while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;
        int end = idx;
        if (end < json.Length && json[end] == '-') end++;
        while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.')) end++;
        if (end == idx) return 0f;
        return float.TryParse(json.Substring(idx, end - idx),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float r) ? r : 0f;
    }

    static int FindMatchingBrace(string s, int start)
    {
        int depth = 0; bool inStr = false;
        for (int i = start; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"' && (i == 0 || s[i - 1] != '\\')) inStr = !inStr;
            if (!inStr) { if (c == '{') depth++; else if (c == '}') { depth--; if (depth == 0) return i; } }
        }
        return -1;
    }

    static int FindMatchingBracket(string s, int start)
    {
        int depth = 0; bool inStr = false;
        for (int i = start; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"' && (i == 0 || s[i - 1] != '\\')) inStr = !inStr;
            if (!inStr) { if (c == '[') depth++; else if (c == ']') { depth--; if (depth == 0) return i; } }
        }
        return -1;
    }

    // ── Debug helpers ─────────────────────────────────────────────────────────

    static string PhaseName(int phase)
    {
        switch (phase)
        {
            case PHASE_IMAGINE:    return "IMAGINE (Phase 1)";
            case PHASE_EXECUTE:    return "EXECUTE (Phase 2)";
            case PHASE_TRANSITION: return "TRANSITION (Phase 3)";
            default:               return $"Unknown({phase})";
        }
    }
}
