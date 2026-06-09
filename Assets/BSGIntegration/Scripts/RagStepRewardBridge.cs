using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Single path for RAG training rewards: +1 HUD (<see cref="SkillBasedActionSystem.ApplyCorrectStepReward"/>)
/// and +1 ML-Agents extrinsic (<see cref="BSGMLAgent.ApplyMlTrainingStepReward"/>) per completed cognitive or physical step.
/// Mental completions credit the zone physical agent (P1–P4) who owns the <see cref="BSGMLAgent"/>.
/// </summary>
public static class RagStepRewardBridge
{
    const float StandardStepReward = 1f;

    static readonly Dictionary<string, BSGMLAgent> s_physicalMlByAgentId =
        new Dictionary<string, BSGMLAgent>(8, System.StringComparer.OrdinalIgnoreCase);

    static readonly Dictionary<int, BSGMLAgent> s_cognitiveMlByZone =
        new Dictionary<int, BSGMLAgent>(4);

    static SkillBasedActionSystem s_cachedSkillSystem;

    /// <summary>Call from <see cref="BSGMLAgent.Initialize"/> / RAG bootstrap so rewards never scan all agents.</summary>
    public static void RegisterPhysicalMlAgentForRewards(BSGMLAgent agent)
    {
        if (agent == null || string.IsNullOrEmpty(agent.agentId)) return;
        if (agent.agentRole != BSGMLAgent.AgentRole.Physical) return;
        s_physicalMlByAgentId[agent.agentId] = agent;
    }

    public static void UnregisterPhysicalMlAgentForRewards(BSGMLAgent agent)
    {
        if (agent == null || string.IsNullOrEmpty(agent.agentId)) return;
        if (s_physicalMlByAgentId.TryGetValue(agent.agentId, out BSGMLAgent reg) && reg == agent)
            s_physicalMlByAgentId.Remove(agent.agentId);
    }

    /// <summary>Zone M_A cognitive brain (CognitiveAgentZoneN) for direct ML-Agents extrinsic on mental steps.</summary>
    public static void RegisterCognitiveMlAgentForRewards(BSGMLAgent agent)
    {
        if (agent == null || agent.agentRole != BSGMLAgent.AgentRole.Mental) return;
        if (agent.zoneIndex < 0 || agent.zoneIndex > 3) return;
        s_cognitiveMlByZone[agent.zoneIndex] = agent;
    }

    public static void UnregisterCognitiveMlAgentForRewards(BSGMLAgent agent)
    {
        if (agent == null || agent.zoneIndex < 0 || agent.zoneIndex > 3) return;
        if (s_cognitiveMlByZone.TryGetValue(agent.zoneIndex, out BSGMLAgent reg) && reg == agent)
            s_cognitiveMlByZone.Remove(agent.zoneIndex);
    }

    public static BSGMLAgent TryGetRegisteredCognitiveAgent(int zoneIndex)
    {
        if (zoneIndex < 0 || zoneIndex > 3) return null;
        s_cognitiveMlByZone.TryGetValue(zoneIndex, out BSGMLAgent agent);
        return agent;
    }

    /// <summary>Extrinsic +1 on zone cognitive brain when RAG mental (M1–M4) completes a step.</summary>
    public static void ApplyCognitiveZoneMlReward(int zoneIndex, string stepId)
    {
        BSGMLAgent cog = TryGetRegisteredCognitiveAgent(zoneIndex);
        if (cog == null) return;

        cog.AddCognitiveStepReward(StandardStepReward);
        RagMlExtrinsicRewardHub.ApplyMlStepSuccess(cog.agentId, stepId ?? "", StandardStepReward);
    }

    public static void PrimeSkillSystemCache(SkillBasedActionSystem skillSystem)
    {
        if (skillSystem != null)
            s_cachedSkillSystem = skillSystem;
    }

    /// <summary>Cognitive / physical step finished — reward zone P-agent for HUD + ML.</summary>
    public static void ApplyStandardStepReward(
        string physicalRewardAgentId,
        string completingAgentId,
        ActionSequenceStep step,
        SkillBasedActionSystem skillSystem)
    {
        if (string.IsNullOrEmpty(physicalRewardAgentId) || skillSystem == null)
            return;

        PrimeSkillSystemCache(skillSystem);

        skillSystem.ApplyCorrectStepReward(physicalRewardAgentId, StandardStepReward);

        RagMlExtrinsicRewardHub.ApplyMlStepSuccess(physicalRewardAgentId, step?.stepId ?? "", StandardStepReward);

        MLTrainingResultsWriter.Instance?.OnStepCompleted(physicalRewardAgentId, step?.stepId, -1f);
        // -1f completionTimeSec → writer uses elapsed since step start when available
        MLTrainingLogger.Instance?.LogStepCompleted(physicalRewardAgentId, step?.stepId ?? "", StandardStepReward);
    }

    /// <summary>
    /// MentalAgentController path: fixed +1 for HUD+ML on zone P-agent (memory narrative amount stays caller-owned).
    /// </summary>
    public static void ApplyStandardStepRewardFromMentalController(string physicalRewardAgentId, string mentalAgentObjectName, int zoneIndex = -1)
    {
        if (s_cachedSkillSystem == null)
            s_cachedSkillSystem = Object.FindObjectOfType<SkillBasedActionSystem>();
        if (s_cachedSkillSystem == null)
        {
            Debug.LogWarning("[RagStepRewardBridge] SkillBasedActionSystem missing — mental reward skipped.");
            return;
        }

        ApplyStandardStepReward(physicalRewardAgentId, mentalAgentObjectName, step: null, s_cachedSkillSystem);

        if (zoneIndex >= 0 && zoneIndex <= 3)
        {
            BSGMLAgent cogMl = TryGetRegisteredCognitiveAgent(zoneIndex);
            if (cogMl != null)
            {
                cogMl.AddCognitiveStepReward(StandardStepReward);
                RagMlExtrinsicRewardHub.ApplyMlStepSuccess(cogMl.agentId, mentalAgentObjectName, StandardStepReward);
            }
        }
    }

    public static BSGMLAgent TryGetRegisteredPhysicalAgent(string agentId) =>
        FindBsgMlAgentByAgentId(agentId);

    static BSGMLAgent FindBsgMlAgentByAgentId(string agentId)
    {
        if (string.IsNullOrEmpty(agentId)) return null;

        if (s_physicalMlByAgentId.TryGetValue(agentId, out BSGMLAgent registered) && registered != null)
            return registered;

        string normalized = ZoneAgentIds.NormalizeProfileAgentId(agentId);
        if (!string.Equals(normalized, agentId, System.StringComparison.OrdinalIgnoreCase)
            && s_physicalMlByAgentId.TryGetValue(normalized, out registered) && registered != null)
            return registered;

        GameObject byName = GameObject.Find(agentId);
        if (byName == null && !string.Equals(normalized, agentId, System.StringComparison.OrdinalIgnoreCase))
            byName = GameObject.Find("Agent_" + normalized);
        if (byName != null)
        {
            BSGMLAgent ml = byName.GetComponent<BSGMLAgent>();
            if (ml != null && string.Equals(ml.agentId, agentId, System.StringComparison.OrdinalIgnoreCase))
            {
                RegisterPhysicalMlAgentForRewards(ml);
                return ml;
            }
            ml = byName.GetComponentInChildren<BSGMLAgent>(true);
            if (ml != null && string.Equals(ml.agentId, agentId, System.StringComparison.OrdinalIgnoreCase))
            {
                RegisterPhysicalMlAgentForRewards(ml);
                return ml;
            }
        }

        return null;
    }
}
