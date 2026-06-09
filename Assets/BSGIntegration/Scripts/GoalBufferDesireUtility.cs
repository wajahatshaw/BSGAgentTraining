using System;
using UnityEngine;

/// <summary>
/// Centralizes Goal Buffer desire resolution and eligibility checks so mental, scripted,
/// HUD, and export paths all use the same precedence rules.
/// </summary>
public static class GoalBufferDesireUtility
{
    public const string ResolvedDesireKey = "goal_buffer_resolved_desire";
    public const string ResolvedDesireSourceKey = "goal_buffer_resolved_desire_source";

    private const string SmartKeyOverrideKey = "smart_key_result_desire_level";
    private const string GoalBufferOverrideKey = "goal_buffer_desire_override";

    public static bool TryResolveDesire(
        ActionSequenceStep step,
        ZoneDeclarativeMemory zoneMemory,
        out float resolvedDesire,
        out string source)
    {
        resolvedDesire = 0f;
        source = string.Empty;

        if (TryReadFutureOverride(zoneMemory, out resolvedDesire))
        {
            source = "smart_key_result_override";
            CacheOnStep(step, resolvedDesire, source);
            return true;
        }

        if (TryResolveDesire(step?.goalBufferContract, out resolvedDesire, out source))
        {
            CacheOnStep(step, resolvedDesire, source);
            return true;
        }

        CacheOnStep(step, 0f, string.Empty);
        return false;
    }

    public static bool TryResolveDesire(
        GoalBufferContract contract,
        out float resolvedDesire,
        out string source)
    {
        resolvedDesire = 0f;
        source = string.Empty;

        if (contract?.stack == null || contract.stack.Length == 0)
            return false;

        if (TryGetLayerDesire(contract, "smart_key_result", out resolvedDesire))
        {
            source = "smart_key_result_layer";
            return true;
        }

        float best = 0f;
        foreach (GoalBufferStackLayer layer in contract.stack)
        {
            if (layer == null || layer.desireLevel <= 0f)
                continue;

            if (layer.desireLevel > best)
                best = layer.desireLevel;
        }

        if (best > 0f)
        {
            resolvedDesire = best;
            source = "highest_goal_buffer_layer";
            return true;
        }

        return false;
    }

    public static bool IsLayerEligible(float agentSkillLevel, float layerDesireLevel)
    {
        if (layerDesireLevel <= 0f)
            return true;

        return agentSkillLevel >= layerDesireLevel;
    }

    public static void CacheOnStep(ActionSequenceStep step, float resolvedDesire, string source)
    {
        if (step == null)
            return;

        step.resolvedGoalBufferDesireLevel = resolvedDesire > 0f ? resolvedDesire : 0f;
        step.resolvedGoalBufferDesireSource = source ?? string.Empty;
    }

    private static bool TryReadFutureOverride(ZoneDeclarativeMemory zoneMemory, out float desire)
    {
        desire = 0f;
        if (zoneMemory == null)
            return false;

        if (TryGetPositiveFloat(zoneMemory, GoalBufferOverrideKey, out desire))
            return true;

        if (TryGetPositiveFloat(zoneMemory, SmartKeyOverrideKey, out desire))
            return true;

        return false;
    }

    private static bool TryGetPositiveFloat(ZoneDeclarativeMemory zoneMemory, string key, out float value)
    {
        value = 0f;
        if (zoneMemory == null || string.IsNullOrWhiteSpace(key))
            return false;

        if (!zoneMemory.TryGet(key, out string raw) || string.IsNullOrWhiteSpace(raw))
            return false;

        if (!float.TryParse(raw, out value))
            return false;

        return value > 0f;
    }

    private static bool TryGetLayerDesire(GoalBufferContract contract, string layerType, out float desire)
    {
        desire = 0f;
        if (contract?.stack == null || string.IsNullOrWhiteSpace(layerType))
            return false;

        foreach (GoalBufferStackLayer layer in contract.stack)
        {
            if (layer == null)
                continue;

            if (!string.Equals(layer.type, layerType, StringComparison.OrdinalIgnoreCase))
                continue;

            if (layer.desireLevel <= 0f)
                continue;

            desire = layer.desireLevel;
            return true;
        }

        return false;
    }
}
