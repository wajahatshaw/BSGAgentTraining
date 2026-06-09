using System;
using System.Collections.Generic;
using UnityEngine;

public class CognitiveRewardIntegrator : MonoBehaviour
{
    [Header("Reward Weights")]
    public float validTransferReward = 2f;
    public float duplicateTransferPenalty = -0.5f;
    public float gatewayViolationPenalty = -3f;

    private readonly Dictionary<string, HashSet<string>> visitedStationsByAgent = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> rewardByAgent = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, float> RewardByAgent => rewardByAgent;

    void OnEnable()
    {
        CognitiveStationInteractable.StationEvent += OnStationEvent;
    }

    void OnDisable()
    {
        CognitiveStationInteractable.StationEvent -= OnStationEvent;
    }

    void OnStationEvent(CognitiveStationEvent evt)
    {
        if (evt.phase != "enter") return;

        if (!visitedStationsByAgent.TryGetValue(evt.agentId, out var visited))
        {
            visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            visitedStationsByAgent[evt.agentId] = visited;
        }

        float delta = visited.Add(evt.stationId) ? validTransferReward : duplicateTransferPenalty;

        // Declarative module gateway station (updated cognitive station map).
        if (evt.stationId.Equals("cognitive_003", StringComparison.OrdinalIgnoreCase))
        {
            AgentCognitiveMemory memory = FindAgentMemory(evt.agentId);
            if (memory == null || !memory.TryGet("retrieved_schema", out _))
            {
                delta += gatewayViolationPenalty;
            }
        }

        rewardByAgent.TryGetValue(evt.agentId, out float current);
        rewardByAgent[evt.agentId] = current + delta;

        int zone = ResolveZoneIndexForAgent(evt.agentId);
        if (zone >= 0)
            RagMlExtrinsicRewardHub.ApplyVariableRewardForZone(zone, delta);
    }

    static int ResolveZoneIndexForAgent(string agentId)
    {
        if (string.IsNullOrEmpty(agentId)) return -1;

        foreach (var mover in UnityEngine.Object.FindObjectsOfType<RagSequenceAgentMover>())
        {
            if (mover == null) continue;
            string id = string.IsNullOrWhiteSpace(mover.agentId) ? mover.gameObject.name : mover.agentId;
            if (!string.Equals(id, agentId, StringComparison.OrdinalIgnoreCase)) continue;
            return mover.zoneIndex;
        }

        foreach (var mental in UnityEngine.Object.FindObjectsOfType<MentalAgentController>())
        {
            if (mental == null) continue;
            if (!string.Equals(mental.gameObject.name, agentId, StringComparison.OrdinalIgnoreCase))
                continue;
            return mental.zoneIndex;
        }

        return -1;
    }

    static AgentCognitiveMemory FindAgentMemory(string agentName)
    {
        foreach (var memory in UnityEngine.Object.FindObjectsOfType<AgentCognitiveMemory>())
        {
            if (memory.gameObject.name == agentName) return memory;
        }
        return null;
    }
}
