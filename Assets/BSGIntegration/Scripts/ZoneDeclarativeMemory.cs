using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-zone shared blackboard.
/// Mental agents write cognitive outputs here; Physical agents read when all
/// required cognitive steps are complete (cognitiveReady == true).
/// </summary>
public class ZoneDeclarativeMemory : MonoBehaviour
{
    [Header("Zone Identity")]
    public int zoneIndex = -1;

    // ── Cognitive outputs written by M-agents ─────────────────────────────
    [Header("Cognitive Outputs (written by M-agents)")]
    public string intention        = string.Empty;
    public string goal             = string.Empty;
    public string visualSummary    = string.Empty;
    public string retrievalSchema  = string.Empty;
    public string productionRule   = string.Empty;
    public string motorCommand     = string.Empty;
    public string resolvedTargetId = string.Empty;   // actual object P-agent should move to
    public string imaginalBufferState = string.Empty;

    /// <summary>Goal Buffer stack extractions (keys smart_key_result / artifact_reference / sub_goal).</summary>
    public string goalBufferSmartKeyResult   = string.Empty;
    public string goalBufferArtifactReference = string.Empty;
    public string goalBufferSubGoal          = string.Empty;
    public float goalBufferResolvedDesireLevel = 0f;
    public string goalBufferResolvedDesireSource = string.Empty;

    // ── Readiness flag ────────────────────────────────────────────────────
    [Header("Readiness")]
    [Tooltip("Set to true by last M-agent when cognitive process is complete.")]
    public bool cognitiveReady = false;

    [Tooltip("Micro-completion budget (VisitStation + P-scan marks). RAG JSON has 12 macro steps — see Persona HUD second line.")]
    public int totalCognitiveSteps = 22;
    private int completedCognitiveSteps = 0;

    // ── Step log (for HUD / debug) ────────────────────────────────────────
    private readonly List<string> stepLog = new List<string>();
    public IReadOnlyList<string> StepLog => stepLog;

    // Static registry so any code can get a zone's memory without a direct ref
    private static readonly Dictionary<int, ZoneDeclarativeMemory> registry
        = new Dictionary<int, ZoneDeclarativeMemory>();

    void Awake()
    {
        RegisterInRegistry();
    }

    void OnEnable()
    {
        RegisterInRegistry();
    }

    void RegisterInRegistry()
    {
        if (zoneIndex >= 0)
            registry[zoneIndex] = this;
    }

    /// <summary>
    /// Re-scan the loaded scene for blackboards (handles script execution order before Awake).
    /// </summary>
    public static void RebuildRegistryFromScene()
    {
        var all = Object.FindObjectsOfType<ZoneDeclarativeMemory>(true);
        foreach (var z in all)
        {
            if (z != null && z.zoneIndex >= 0)
                registry[z.zoneIndex] = z;
        }
    }

    void OnDestroy()
    {
        if (zoneIndex >= 0 && registry.TryGetValue(zoneIndex, out var stored) && stored == this)
            registry.Remove(zoneIndex);
    }

    public static ZoneDeclarativeMemory ForZone(int idx)
    {
        registry.TryGetValue(idx, out var mem);
        return mem;
    }

    // ── Write API (M-agents call this) ────────────────────────────────────

    public void RecordCognitiveStep(string stepId, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        // Every step output is keyed here so agents can read it later via TryGet / dataSlots
        dataSlots[key] = value ?? string.Empty;

        // Apply to well-known slots (ACT-R–style aggregates for HUD / P-agent)
        string k = key.ToLowerInvariant();
        if (k.Contains("intent"))    intention       = value;
        else if (k.Contains("goal")) goal            = value;
        else if (k.Contains("visual") && !k.Contains("loc")) visualSummary = value;
        else if (k.Contains("retriev") || k.Contains("schema")) retrievalSchema = value;
        else if (k.Contains("product") || k.Contains("rule"))  productionRule  = value;
        else if (k.Contains("motor")  || k.Contains("command")) motorCommand   = value;
        else if (k.Contains("target") || k.Contains("resolve")) resolvedTargetId = value;

        if (string.Equals(key, "smart_key_result", System.StringComparison.OrdinalIgnoreCase))
            goalBufferSmartKeyResult = value ?? string.Empty;
        else if (string.Equals(key, "artifact_reference", System.StringComparison.OrdinalIgnoreCase))
            goalBufferArtifactReference = value ?? string.Empty;
        else if (string.Equals(key, "sub_goal", System.StringComparison.OrdinalIgnoreCase))
            goalBufferSubGoal = value ?? string.Empty;

        stepLog.Add($"[Z{zoneIndex}] {stepId} → {key}={value}");
        if (stepLog.Count > 128) stepLog.RemoveAt(0);
    }

    /// <summary>Call once per completed cognitive step from any M-agent in the zone.</summary>
    public void MarkCognitiveStepComplete(string stepId)
    {
        completedCognitiveSteps++;
        stepLog.Add($"[Z{zoneIndex}] STEP DONE ({completedCognitiveSteps}/{totalCognitiveSteps}): {stepId}");
        // Note: cognitiveReady is set by MentalAgentSpawner.OnMergeDone — NOT by step count.
        // This ensures P-agent only unlocks after the full two-pass protocol finishes.
    }

    /// <summary>Reset for a new episode.</summary>
    public void ResetForEpisode()
    {
        cognitiveReady        = false;
        completedCognitiveSteps = 0;
        totalCognitiveReward  = 0f;
        intention      = string.Empty;
        goal           = string.Empty;
        visualSummary  = string.Empty;
        retrievalSchema = string.Empty;
        productionRule  = string.Empty;
        motorCommand    = string.Empty;
        resolvedTargetId = string.Empty;
        imaginalBufferState = string.Empty;
        goalBufferSmartKeyResult   = string.Empty;
        goalBufferArtifactReference = string.Empty;
        goalBufferSubGoal          = string.Empty;
        goalBufferResolvedDesireLevel = 0f;
        goalBufferResolvedDesireSource = string.Empty;
        stepLog.Clear();
        dataSlots.Clear();
        payloadSlots.Clear();
        productionCommandSlots.Clear();
    }

    // ── Reward tracking ────────────────────────────────────────────────────

    [Header("Reward Tracking")]
    public float totalCognitiveReward = 0f;
    public float rewardPerStep        = 0.05f;

    public void AddStepReward(float amount, string stepId)
    {
        totalCognitiveReward += amount;
        stepLog.Add($"[Z{zoneIndex}] +REWARD {amount:F3} at {stepId}  (total={totalCognitiveReward:F3})");
        Debug.Log($"✅ [Z{zoneIndex}] Cognitive reward +{amount:F3} → {stepId}  cumulative={totalCognitiveReward:F3}");
    }

    public int CompletedSteps  => completedCognitiveSteps;
    public int TotalSteps      => totalCognitiveSteps;

    // ── Generic key-value store (for MentalAgentController / PhysicalObservationLoop) ─────

    private readonly Dictionary<string, string> dataSlots
        = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> payloadSlots
        = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> productionCommandSlots
        = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

    public void SetData(string key, string value)
    {
        // RecordCognitiveStep also writes dataSlots; use it so public fields + stepLog stay in sync
        RecordCognitiveStep($"kv_{key}", key, value ?? string.Empty);
    }

    public void RecordProducedPayload(string stepId, string payloadKey, string payloadValue)
    {
        if (string.IsNullOrWhiteSpace(payloadKey)) return;

        string value = payloadValue ?? string.Empty;
        payloadSlots[payloadKey] = value;
        RecordCognitiveStep(string.IsNullOrWhiteSpace(stepId) ? $"payload_{payloadKey}" : stepId, payloadKey, value);
    }

    public bool TryGetPayload(string payloadKey, out string value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(payloadKey)) return false;
        if (payloadSlots.TryGetValue(payloadKey, out value)) return true;
        return dataSlots.TryGetValue(payloadKey, out value);
    }

    public bool HasPayload(string payloadKey)
    {
        if (string.IsNullOrWhiteSpace(payloadKey)) return true;
        return TryGetPayload(payloadKey, out _);
    }

    public bool HasPayloads(string[] payloadKeys)
    {
        if (payloadKeys == null || payloadKeys.Length == 0) return true;
        for (int i = 0; i < payloadKeys.Length; i++)
        {
            if (!HasPayload(payloadKeys[i])) return false;
        }
        return true;
    }

    public string BuildPayloadSummary(string[] payloadKeys)
    {
        if (payloadKeys == null || payloadKeys.Length == 0) return "";

        List<string> parts = new List<string>();
        for (int i = 0; i < payloadKeys.Length; i++)
        {
            string key = payloadKeys[i];
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (TryGetPayload(key, out string value))
                parts.Add($"{key}={value}");
            else
                parts.Add($"{key}=<missing>");
        }
        return string.Join("; ", parts);
    }

    public void RecordProductionMemoryCommand(string stepId, ProductionMemoryCommandRecord command)
    {
        if (command == null || string.IsNullOrWhiteSpace(command.payloadKey)) return;

        string value = command.ToBlackboardValue();
        productionCommandSlots[command.payloadKey] = value;
        payloadSlots[command.payloadKey] = value;
        RecordCognitiveStep(string.IsNullOrWhiteSpace(stepId) ? command.stepId : stepId, command.payloadKey, value);

        if (!string.IsNullOrWhiteSpace(command.commandType))
            dataSlots["last_production_command_type"] = command.commandType;
        if (!string.IsNullOrWhiteSpace(command.connectionsSummary))
            dataSlots["last_production_connections"] = command.connectionsSummary;
        if (!string.IsNullOrWhiteSpace(command.knowledgeSummary))
            dataSlots["last_retrieval_context"] = command.knowledgeSummary;
    }

    public bool TryGetProductionCommand(string payloadKey, out string value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(payloadKey)) return false;
        return productionCommandSlots.TryGetValue(payloadKey, out value);
    }

    public bool CanEnterImaginalState(string requiredState)
    {
        if (string.IsNullOrWhiteSpace(requiredState)) return true;
        if (string.IsNullOrWhiteSpace(imaginalBufferState)) return true;
        return string.Equals(imaginalBufferState, requiredState, System.StringComparison.OrdinalIgnoreCase);
    }

    public void SetImaginalBufferState(string stepId, string state, string reason = "")
    {
        if (string.IsNullOrWhiteSpace(state)) return;

        imaginalBufferState = state;
        dataSlots["imaginal_state"] = state;
        payloadSlots["imaginal_state"] = state;

        string suffix = string.IsNullOrWhiteSpace(reason) ? "" : $" ({reason})";
        stepLog.Add($"[Z{zoneIndex}] {stepId} → imaginal_state={state}{suffix}");
        if (stepLog.Count > 128) stepLog.RemoveAt(0);
    }

    public void RecordImaginalTransition(ActionSequenceStep step)
    {
        if (step == null) return;

        string before = step.imaginalStateBefore ?? string.Empty;
        string after = step.imaginalStateAfter ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(before))
            dataSlots[$"{step.stepId}_imaginal_before"] = before;
        if (!string.IsNullOrWhiteSpace(after))
            dataSlots[$"{step.stepId}_imaginal_after"] = after;

        if (!string.IsNullOrWhiteSpace(after))
            SetImaginalBufferState(step.stepId, after, $"from {before}");

        if (step.imaginalBufferConnections != null && step.imaginalBufferConnections.Length > 0)
        {
            string connections = string.Join(",", step.imaginalBufferConnections);
            dataSlots[$"{step.stepId}_imaginal_connections"] = connections;
            if (!string.IsNullOrWhiteSpace(step.producesPayload))
            {
                for (int i = 0; i < step.imaginalBufferConnections.Length; i++)
                {
                    string c = step.imaginalBufferConnections[i];
                    if (string.IsNullOrWhiteSpace(c)) continue;
                    payloadSlots[$"{c}:{step.producesPayload}"] = TryGetPayload(step.producesPayload, out string v) ? v : after;
                }
            }
        }
    }

    /// <summary>Clears Goal Buffer triple-slot HUD fields before a new Goal Buffer visit.</summary>
    public void ClearGoalBufferTripleExtractions()
    {
        goalBufferSmartKeyResult = string.Empty;
        goalBufferArtifactReference = string.Empty;
        goalBufferSubGoal = string.Empty;
        dataSlots["smart_key_result"] = string.Empty;
        dataSlots["artifact_reference"] = string.Empty;
        dataSlots["sub_goal"] = string.Empty;
    }

    public void SetGoalBufferResolvedDesire(string stepId, float desireLevel, string source)
    {
        goalBufferResolvedDesireLevel = desireLevel > 0f ? desireLevel : 0f;
        goalBufferResolvedDesireSource = source ?? string.Empty;
        dataSlots[GoalBufferDesireUtility.ResolvedDesireKey] = goalBufferResolvedDesireLevel.ToString("F1");
        dataSlots[GoalBufferDesireUtility.ResolvedDesireSourceKey] = goalBufferResolvedDesireSource;

        string sid = string.IsNullOrWhiteSpace(stepId) ? "goal_buffer" : stepId;
        stepLog.Add(
            $"[Z{zoneIndex}] {sid} → resolved_desire_reference={goalBufferResolvedDesireLevel:F1} ({goalBufferResolvedDesireSource})");
        if (stepLog.Count > 128) stepLog.RemoveAt(0);
    }

    public bool TryGet(string key, out string value)
        => dataSlots.TryGetValue(key, out value);

    /// <summary>All key/value entries written via RecordCognitiveStep / SetData (for HUD).</summary>
    public List<KeyValuePair<string, string>> GetDataSlotsSnapshot()
    {
        var list = new List<KeyValuePair<string, string>>(dataSlots.Count);
        foreach (var kv in dataSlots)
            list.Add(kv);
        list.Sort((a, b) => string.Compare(a.Key, b.Key, System.StringComparison.OrdinalIgnoreCase));
        return list;
    }

    public List<KeyValuePair<string, string>> GetPayloadSnapshot()
    {
        var list = new List<KeyValuePair<string, string>>(payloadSlots.Count);
        foreach (var kv in payloadSlots)
            list.Add(kv);
        list.Sort((a, b) => string.Compare(a.Key, b.Key, System.StringComparison.OrdinalIgnoreCase));
        return list;
    }
}
