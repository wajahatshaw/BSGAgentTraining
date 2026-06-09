using System;
using UnityEngine;

/// <summary>
/// Bottom → middle → top Goal Buffer flow without legacy stack arrays.
/// Maps slots to existing declarative keys (smart_key_result / artifact_reference / sub_goal) for HUD.
/// </summary>
public enum GoalBufferSlot
{
    Bottom = 0,
    Middle = 1,
    Top = 2
}

public static class GoalBufferTripleOrchestrator
{
    public static bool IsTripleModeStep(ActionSequenceStep step)
    {
        if (step == null) return false;
        if (!string.Equals(step.currentCognitiveState, "GoalBuffer", StringComparison.OrdinalIgnoreCase))
            return false;
        return step.goalBufferContract != null && step.goalBufferContract.IsTripleModeContract();
    }

    public static string GetSlotText(GoalBufferContract c, GoalBufferSlot slot)
    {
        if (c == null) return null;
        string raw;
        switch (slot)
        {
            case GoalBufferSlot.Bottom: raw = c.initialStateBottom; break;
            case GoalBufferSlot.Middle: raw = c.initialStateMiddle; break;
            case GoalBufferSlot.Top: raw = c.initialStateTop; break;
            default: raw = null; break;
        }
        return NormalizeTripleText(raw);
    }

    public static string NormalizeTripleText(string s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static void WriteSlotToZoneMemory(ZoneDeclarativeMemory mem, string stepId, GoalBufferSlot slot, string text)
    {
        if (mem == null || text == null) return;
        string sid = string.IsNullOrWhiteSpace(stepId) ? "goal_buffer_triple" : stepId;
        switch (slot)
        {
            case GoalBufferSlot.Bottom:
                mem.RecordCognitiveStep(sid, "smart_key_result", text);
                break;
            case GoalBufferSlot.Middle:
                mem.RecordCognitiveStep(sid, "artifact_reference", text);
                break;
            case GoalBufferSlot.Top:
                mem.RecordCognitiveStep(sid, "sub_goal", text);
                break;
        }
    }

    public static int CountNonSkippedSlots(GoalBufferContract c)
    {
        if (c == null) return 0;
        int n = 0;
        for (int i = 0; i < 3; i++)
        {
            if (GetSlotText(c, (GoalBufferSlot)i) != null) n++;
        }
        return n;
    }

    public static float PerSlotDwellSeconds(ActionSequenceStep step, GoalBufferContract c)
    {
        float total = CognitiveProcessManager.ComputeStationDwellSeconds(step);
        total = Mathf.Clamp(total, 0.05f, 120f);
        int n = CountNonSkippedSlots(c);
        if (n <= 0) return Mathf.Max(0.05f, total / 3f);
        return Mathf.Max(0.05f, total / n);
    }

    /// <summary>Advance slot index past null-only slots (still ordered bottom → top).</summary>
    public static GoalBufferSlot AdvancePastSkipped(GoalBufferContract c, ref int slotOrdinal)
    {
        while (slotOrdinal < 3)
        {
            var slot = (GoalBufferSlot)slotOrdinal;
            if (GetSlotText(c, slot) != null) return slot;
            slotOrdinal++;
        }

        return GoalBufferSlot.Top;
    }
}
