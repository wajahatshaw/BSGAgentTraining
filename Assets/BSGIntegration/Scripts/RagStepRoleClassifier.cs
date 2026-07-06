using System;

/// <summary>
/// Single source of truth for mental (M) vs physical (P) RAG step classification.
/// <see cref="ActionSequenceStep.agentRole"/> from JSON is authoritative when present.
/// Legacy steps without <c>agentRole</c> infer from <c>actionType</c> and target id.
/// </summary>
public static class RagStepRoleClassifier
{
    /// <summary>Physical-agent step: executes on P, interleaved after opening barrier via DAG.</summary>
    public static bool IsPhysicalAgentStep(ActionSequenceStep step)
    {
        if (step == null) return false;

        if (!string.IsNullOrWhiteSpace(step.agentRole))
            return string.Equals(step.agentRole, "P", StringComparison.OrdinalIgnoreCase);

        // Legacy RAG without agentRole: manual act steps are physical.
        return string.Equals(step.actionType, "act", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Mental/cognitive step: dispatches to M agent. P-role steps are never mental even when
    /// <see cref="ActionSequenceStep.targetObjectId"/> references a cognitive module (e.g. cognitive_005).
    /// </summary>
    public static bool IsMentalAgentStep(ActionSequenceStep step)
    {
        if (step == null || IsPhysicalAgentStep(step)) return false;

        if (!string.IsNullOrWhiteSpace(step.agentRole))
            return string.Equals(step.agentRole, "M", StringComparison.OrdinalIgnoreCase);

        // Legacy: move/visit cognitive stations without explicit agentRole.
        if (string.Equals(step.actionType, "move", StringComparison.OrdinalIgnoreCase)
            || string.Equals(step.actionType, "compare", StringComparison.OrdinalIgnoreCase))
        {
            return IsCognitiveStationTargetId(step.targetObjectId);
        }

        return false;
    }

    public static bool IsCognitiveStationTargetId(string targetObjectId)
    {
        if (string.IsNullOrWhiteSpace(targetObjectId)) return false;
        if (SceneGenerator.Instance != null)
            return SceneGenerator.Instance.IsKnownCognitiveStation(targetObjectId);

        string baseId = StripZoneSuffix(targetObjectId);
        return baseId.StartsWith("cognitive_", StringComparison.OrdinalIgnoreCase);
    }

    static string StripZoneSuffix(string objectId)
    {
        if (string.IsNullOrWhiteSpace(objectId)) return objectId;
        int zIdx = objectId.IndexOf("_zone", StringComparison.OrdinalIgnoreCase);
        return zIdx > 0 ? objectId.Substring(0, zIdx) : objectId;
    }
}
