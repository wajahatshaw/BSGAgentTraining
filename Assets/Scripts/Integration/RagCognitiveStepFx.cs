using UnityEngine;

/// <summary>
/// Shared cognitive-step completion visuals for orchestrator network sync.
/// </summary>
public static class RagCognitiveStepFx
{
    public static void ApplyStepActivatedFx(ActionSequenceStep step, int zoneIndex)
    {
        if (step == null)
            return;

        if (!string.IsNullOrWhiteSpace(step.targetObjectId))
            FlashTarget(step.targetObjectId, zoneIndex, VerbToColor(step.actionVerb), 0.35f);

        RagSequenceAgentMover mentalMover = FindZoneMentalMover(zoneIndex);
        mentalMover?.PlayNetworkStepCompletionFlash();
    }

    public static void ApplyStepCompletedFx(ActionSequenceStep step, int zoneIndex)
    {
        if (step == null)
            return;

        if (RagMenuController.IsMenuStep(step))
        {
            RagMenuController menu = RagMenuController.EnsureInScene();
            menu?.MarkOptionSelected(RagMenuController.GetSelectedOptionTargetId(step), zoneIndex);
            menu?.HideAllMenus();
        }

        if (!string.IsNullOrWhiteSpace(step.targetObjectId))
            FlashTarget(step.targetObjectId, zoneIndex, VerbToColor(step.actionVerb), 0.4f);

        RagSequenceAgentMover mentalMover = FindZoneMentalMover(zoneIndex);
        mentalMover?.PlayNetworkStepCompletionFlash();
    }

    static RagSequenceAgentMover FindZoneMentalMover(int zoneIndex)
    {
        foreach (RagSequenceAgentMover mover in Object.FindObjectsOfType<RagSequenceAgentMover>())
        {
            if (mover != null && mover.isMentalAgent && mover.zoneIndex == zoneIndex)
                return mover;
        }

        return null;
    }

    static void FlashTarget(string targetObjectId, int zoneIndex, Color flashColor, float duration)
    {
        GameObject target = FindTargetObject(targetObjectId, zoneIndex);
        if (target == null)
            return;

        Renderer rend = target.GetComponentInChildren<Renderer>();
        if (rend == null)
            return;

        RagStepFxRunner runner = RagStepFxRunner.EnsureInScene();
        runner?.RunFlash(rend, flashColor, duration);
    }

    internal static GameObject FindTargetObject(string targetObjectId, int zoneIndex)
    {
        if (string.IsNullOrWhiteSpace(targetObjectId))
            return null;

        if (RagMenuController.IsMenuOptionId(targetObjectId))
        {
            string runtimeName = RagMenuController.GetRuntimeOptionObjectName(targetObjectId, zoneIndex);
            GameObject option = GameObject.Find(runtimeName);
            if (option != null)
                return option;
        }

        if (SceneGenerator.Instance != null)
        {
            GameObject tool = GameObject.Find($"Tool_{targetObjectId}_zone{zoneIndex}");
            if (tool != null)
                return tool;
        }

        GameObject found = GameObject.Find($"Tool_{targetObjectId}_zone{zoneIndex}");
        if (found != null)
            return found;
        found = GameObject.Find("Tool_" + targetObjectId);
        if (found != null)
            return found;
        return GameObject.Find(targetObjectId);
    }

    static Color VerbToColor(string verb)
    {
        if (string.IsNullOrWhiteSpace(verb))
            return Color.yellow;

        switch (verb.ToLowerInvariant())
        {
            case "examine": return new Color(0.16f, 0.50f, 0.73f);
            case "type":
            case "click": return new Color(0.90f, 0.49f, 0.13f);
            case "verify":
            case "navigate": return new Color(0.15f, 0.68f, 0.38f);
            default: return Color.yellow;
        }
    }
}
