using UnityEngine;

/// <summary>
/// Shared physical-step completion visuals for orchestrator network sync.
/// </summary>
public static class RagPhysicalStepFx
{
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
        {
            if (RagMenuController.IsMenuStep(step))
                RagMenuController.EnsureInScene()?.ShowButtonPressedFeedback(step.targetObjectId, zoneIndex);

            FlashTarget(step.targetObjectId, zoneIndex, VerbToColor(step.actionVerb), 0.4f);
        }

        RagSequenceAgentMover physicalMover = FindDesignatedPhysicalMover(zoneIndex);
        physicalMover?.PlayNetworkStepCompletionFlash();
    }

    static RagSequenceAgentMover FindDesignatedPhysicalMover(int zoneIndex)
    {
        if (PlayerRagPhysicalBridge.IsBound && PlayerRagPhysicalBridge.BoundMover != null
            && PlayerRagPhysicalBridge.BoundMover.zoneIndex == zoneIndex)
        {
            return PlayerRagPhysicalBridge.BoundMover;
        }

        foreach (RagSequenceAgentMover mover in Object.FindObjectsOfType<RagSequenceAgentMover>())
        {
            if (mover != null && !mover.isMentalAgent && mover.zoneIndex == zoneIndex)
                return mover;
        }

        return null;
    }

    static void FlashTarget(string targetObjectId, int zoneIndex, Color flashColor, float duration)
    {
        GameObject target = RagCognitiveStepFx.FindTargetObject(targetObjectId, zoneIndex);
        if (target == null)
            return;

        Renderer rend = target.GetComponentInChildren<Renderer>();
        if (rend == null)
            return;

        RagStepFxRunner runner = RagStepFxRunner.EnsureInScene();
        runner?.RunFlash(rend, flashColor, duration);
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
