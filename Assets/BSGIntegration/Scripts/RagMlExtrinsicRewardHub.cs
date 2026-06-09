using UnityEngine;

/// <summary>
/// Central ML-Agents extrinsic reward entry for RAG training (additive to +1/step via <see cref="RagStepRewardBridge"/>).
/// Gates on <see cref="ReplicaSceneSetup.enableMlTrainingInRagMode"/>.
/// </summary>
public static class RagMlExtrinsicRewardHub
{
    static ReplicaSceneSetup s_setup;
    public static bool VerboseLogs;

    public static void PrimeReplicaSetupCache(ReplicaSceneSetup setup)
    {
        if (setup != null)
            s_setup = setup;
    }

    static ReplicaSceneSetup Setup
    {
        get
        {
            if (s_setup == null)
                s_setup = UnityEngine.Object.FindObjectOfType<ReplicaSceneSetup>();
            return s_setup;
        }
    }

    public static bool IsMlTrainingEnabled =>
        Setup != null && Setup.enableMlTrainingInRagMode;

    public static float EfficiencyBonusMax =>
        Setup != null ? Setup.ragEfficiencyBonusMax : 0.5f;

    public static float DelayPenaltyThreshold =>
        Setup != null ? Setup.ragDelayPenaltyThreshold : 1.5f;

    public static float DelayPenaltyMagnitude =>
        Setup != null ? Setup.ragDelayPenaltyMagnitude : -1f;

    public static bool UseSparseStepTimeoutPenalty =>
        Setup != null && Setup.useRagSparseStepTimeoutPenalty;

    public static float PerStepEfficiencyPenalty { get; private set; } = -0.001f;
    public static float BoundaryViolationPenalty { get; private set; } = -0.5f;

    static void LogVerbose(string message)
    {
        if (VerboseLogs)
            Debug.Log(message);
    }

    public static void LoadEfficiencyPenaltiesFromJson(string rawJson)
    {
        if (string.IsNullOrEmpty(rawJson)) return;
        float perStep = TryParseJsonFloat(rawJson, "\"per_step\"", -0.001f);
        float boundary = TryParseJsonFloat(rawJson, "\"boundary_violation\"", -0.5f);
        PerStepEfficiencyPenalty = perStep;
        BoundaryViolationPenalty = boundary;
    }

    static float TryParseJsonFloat(string json, string keyFragment, float fallback)
    {
        int idx = json.IndexOf(keyFragment, System.StringComparison.Ordinal);
        if (idx < 0) return fallback;
        int colon = json.IndexOf(':', idx);
        if (colon < 0) return fallback;
        int start = colon + 1;
        while (start < json.Length && (json[start] == ' ' || json[start] == '\t')) start++;
        int end = start;
        while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.' || json[end] == '-' || json[end] == '+'))
            end++;
        if (end <= start) return fallback;
        return float.TryParse(json.Substring(start, end - start),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out float v) ? v : fallback;
    }

    public static BSGMLAgent ResolvePhysicalMlAgent(string physicalAgentId)
    {
        return RagStepRewardBridge.TryGetRegisteredPhysicalAgent(physicalAgentId);
    }

    public static BSGMLAgent ResolvePhysicalMlAgentForZone(int zoneIndex)
    {
        string id = ZoneAgentIds.TryResolvePhysicalAgentId(zoneIndex);
        return string.IsNullOrEmpty(id) ? null : ResolvePhysicalMlAgent(id);
    }

    /// <summary>+1 ML extrinsic for a completed step (HUD handled by bridge caller).</summary>
    public static void ApplyMlStepSuccess(string physicalAgentId, string stepId, float amount = 1f)
    {
        if (!IsMlTrainingEnabled || string.IsNullOrEmpty(physicalAgentId)) return;
        BSGMLAgent ml = ResolvePhysicalMlAgent(physicalAgentId);
        if (ml == null) return;
        ml.ApplyMlTrainingStepReward(amount);
        LogVerbose($"[RagMlExtrinsicRewardHub] step +{amount:F2} ML → {physicalAgentId} ({stepId})");
    }

    public static void ApplyEfficiencyBonus(int zoneIndex, string stepId, float expectedSec, float actualSec)
    {
        if (!IsMlTrainingEnabled || expectedSec <= 0f || actualSec > expectedSec) return;
        float ratio = 1f - Mathf.Clamp01(actualSec / expectedSec);
        float bonus = EfficiencyBonusMax * ratio;
        if (bonus <= 0.0001f) return;

        BSGMLAgent ml = ResolvePhysicalMlAgentForZone(zoneIndex);
        if (ml == null) return;
        ml.ApplyMlTrainingStepReward(bonus);
        LogVerbose($"[RagMlExtrinsicRewardHub] efficiency +{bonus:F3} zone {zoneIndex} step {stepId} ({actualSec:F2}s/{expectedSec:F2}s)");
    }

    public static void ApplyDelayPenalty(int zoneIndex, string stepId, float overdueFactor)
    {
        if (!IsMlTrainingEnabled || !UseSparseStepTimeoutPenalty) return;
        float pen = DelayPenaltyMagnitude;
        if (pen >= 0f) pen = -Mathf.Abs(DelayPenaltyMagnitude);

        BSGMLAgent ml = ResolvePhysicalMlAgentForZone(zoneIndex);
        if (ml == null) return;
        ml.ApplyMlTrainingStepReward(pen);
        LogVerbose($"[RagMlExtrinsicRewardHub] delay {pen:F2} zone {zoneIndex} step {stepId} (overdue x{overdueFactor:F2})");
    }

    public static void ApplyFailurePenalty(int zoneIndex, float magnitude, string reason)
    {
        if (!IsMlTrainingEnabled || magnitude <= 0f) return;
        BSGMLAgent ml = ResolvePhysicalMlAgentForZone(zoneIndex);
        if (ml == null) return;
        ml.AddCognitiveStackPenalty(magnitude);
        LogVerbose($"[RagMlExtrinsicRewardHub] failure -{magnitude:F2} zone {zoneIndex}: {reason}");
    }

    public static void ApplyFailurePenaltyByAgentId(string physicalAgentId, float magnitude, string reason)
    {
        if (!IsMlTrainingEnabled || magnitude <= 0f || string.IsNullOrEmpty(physicalAgentId)) return;
        BSGMLAgent ml = ResolvePhysicalMlAgent(physicalAgentId);
        if (ml == null) return;
        ml.AddCognitiveStackPenalty(magnitude);
        LogVerbose($"[RagMlExtrinsicRewardHub] failure -{magnitude:F2} {physicalAgentId}: {reason}");
    }

    public static void ApplyIncorrectActionPenalty(string physicalAgentId, float magnitude)
    {
        if (!IsMlTrainingEnabled || string.IsNullOrEmpty(physicalAgentId)) return;
        float pen = magnitude >= 0f ? -Mathf.Abs(magnitude) : magnitude;
        BSGMLAgent ml = ResolvePhysicalMlAgent(physicalAgentId);
        if (ml == null) return;
        ml.ApplyMlTrainingStepReward(pen);
        LogVerbose($"[RagMlExtrinsicRewardHub] incorrect action {pen:F2} → {physicalAgentId}");
    }

    public static void ApplyVariableReward(string physicalAgentId, float delta)
    {
        if (!IsMlTrainingEnabled || string.IsNullOrEmpty(physicalAgentId) || Mathf.Approximately(delta, 0f)) return;
        BSGMLAgent ml = ResolvePhysicalMlAgent(physicalAgentId);
        if (ml == null) return;
        if (delta < 0f)
            ml.AddCognitiveStackPenalty(-delta);
        else
            ml.ApplyMlTrainingStepReward(delta);
        LogVerbose($"[RagMlExtrinsicRewardHub] variable {(delta >= 0 ? "+" : "")}{delta:F2} → {physicalAgentId}");
    }

    public static void ApplyVariableRewardForZone(int zoneIndex, float delta)
    {
        string id = ZoneAgentIds.TryResolvePhysicalAgentId(zoneIndex);
        if (!string.IsNullOrEmpty(id))
            ApplyVariableReward(id, delta);
    }

    public static void ApplyPerStepDriftPenalty(BSGMLAgent ml)
    {
        if (!IsMlTrainingEnabled || ml == null) return;
        float p = PerStepEfficiencyPenalty;
        if (Mathf.Approximately(p, 0f)) return;
        ml.ApplyMlTrainingStepReward(p);
    }

    public static void ApplyBoundaryViolationPenalty(BSGMLAgent ml)
    {
        if (!IsMlTrainingEnabled || ml == null) return;
        float p = BoundaryViolationPenalty;
        if (Mathf.Approximately(p, 0f)) return;
        ml.ApplyMlTrainingStepReward(p);
        LogVerbose($"[RagMlExtrinsicRewardHub] boundary {p:F2} → {ml.agentId}");
    }
}
