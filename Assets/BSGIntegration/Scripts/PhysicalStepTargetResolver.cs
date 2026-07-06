using System;

/// <summary>
/// Resolves world-navigation targets for physical-agent (P) RAG steps.
/// Cognitive steps use <see cref="ActionSequenceStep.targetObjectId"/> / <see cref="ActionSequenceStep.targetObjectName"/>.
/// Physical steps use <see cref="ActionSequenceStep.physicalTarget"/> (meronym name, e.g. enter_key)
/// and <see cref="ActionSequenceStep.physicalTargetId"/> (root <c>sceneEntities[]</c> id that owns that
/// meronym — e.g. scene_006 for keyboard keys, not a sub-part id).
/// </summary>
public static class PhysicalStepTargetResolver
{
    public static bool IsPhysicalStep(ActionSequenceStep step)
    {
        return step != null
               && string.Equals(step.agentRole, "P", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Object id used for scene lookup / navigation for this step.
    /// Physical P-steps prefer <c>target_id</c> then <c>target</c> name; never cognitive station ids.
    /// </summary>
    public static string ResolveObjectId(ActionSequenceStep step, int zoneIndex = -1)
    {
        if (step == null)
            return string.Empty;

        if (!IsPhysicalStep(step))
            return step.targetObjectId ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(step.physicalTargetId))
            return step.physicalTargetId.Trim();

        if (!string.IsNullOrWhiteSpace(step.physicalTarget))
        {
            string fromName = ResolveObjectIdByTargetName(step.physicalTarget, zoneIndex);
            if (!string.IsNullOrWhiteSpace(fromName))
                return fromName;
            return step.physicalTarget.Trim();
        }

        // Legacy RAG without target / target_id — avoid navigating to ManualModule cognitive id.
        if (!string.IsNullOrWhiteSpace(step.targetObjectId)
            && !IsCognitiveStationId(step.targetObjectId))
            return step.targetObjectId.Trim();

        return string.Empty;
    }

    public static string ResolveObjectIdByTargetName(string targetName, int zoneIndex = -1)
    {
        if (string.IsNullOrWhiteSpace(targetName))
            return null;

        string normalized = targetName.Trim();

        SceneUILoader loader = UnityEngine.Object.FindObjectOfType<SceneUILoader>();
        if (loader?.sceneData?.initialStates != null)
        {
            if (zoneIndex >= 0)
            {
                foreach (var kvp in loader.sceneData.initialStates)
                {
                    if (!kvp.Key.EndsWith($"_zone{zoneIndex}", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (NameMatchesToolState(kvp.Value, normalized))
                        return kvp.Key;
                }
            }

            foreach (var kvp in loader.sceneData.initialStates)
            {
                if (NameMatchesToolState(kvp.Value, normalized))
                    return kvp.Key;
            }
        }

        if (SceneGenerator.Instance != null)
        {
            string fromScene = SceneGenerator.Instance.ResolveObjectIdByName(normalized, zoneIndex);
            if (!string.IsNullOrWhiteSpace(fromScene))
                return fromScene;
        }

        return null;
    }

    static bool NameMatchesToolState(ToolState state, string normalizedName)
    {
        if (state == null || string.IsNullOrWhiteSpace(normalizedName))
            return false;

        if (!string.IsNullOrWhiteSpace(state.name)
            && string.Equals(state.name.Trim(), normalizedName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(state.objectId))
        {
            string baseId = StripZoneSuffix(state.objectId);
            if (string.Equals(baseId, normalizedName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    static bool IsCognitiveStationId(string objectId)
    {
        return !string.IsNullOrWhiteSpace(objectId)
               && objectId.StartsWith("cognitive_", StringComparison.OrdinalIgnoreCase);
    }

    static string StripZoneSuffix(string objectId)
    {
        if (string.IsNullOrWhiteSpace(objectId))
            return objectId;
        int zIdx = objectId.IndexOf("_zone", StringComparison.OrdinalIgnoreCase);
        return zIdx > 0 ? objectId.Substring(0, zIdx) : objectId;
    }
}
