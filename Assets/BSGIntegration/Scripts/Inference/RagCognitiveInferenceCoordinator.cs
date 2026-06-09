using System.Collections;
using UnityEngine;
using Unity.MLAgents;

/// <summary>
/// Inference scene: runs silent cognitive ONNX decisions per zone, then unlocks physical agents.
/// No visible RagSequenceAgentMover cognitive walks.
/// </summary>
public class RagCognitiveInferenceCoordinator : MonoBehaviour
{
    [Tooltip("ML decisions per cognitive step in the zone sequence (approximates training dwell).")]
    [Min(1)]
    public int decisionsPerCognitiveStep = 6;

    [Tooltip("Extra decisions after the last step before marking cognitive complete.")]
    [Min(0)]
    public int tailDecisions = 4;

    [Tooltip("Seconds to wait for scene agents after Play before starting cognitive ONNX.")]
    [Min(0.5f)]
    public float sceneReadyDelaySeconds = 3f;

    bool _started;

    void Start()
    {
        StartCoroutine(RunWhenSceneReady());
    }

    IEnumerator RunWhenSceneReady()
    {
        yield return new WaitForSeconds(sceneReadyDelaySeconds);

        float deadline = Time.unscaledTime + 45f;
        while (Time.unscaledTime < deadline)
        {
            var movers = FindObjectsOfType<RagSequenceAgentMover>(true);
            if (movers != null && movers.Length > 0)
                break;
            yield return null;
        }

        if (_started) yield break;
        _started = true;

        for (int z = 0; z < 4; z++)
            yield return RunZoneCognitiveInference(z);

        Debug.Log("[RagCognitiveInferenceCoordinator] All zones cognitive inference complete — physical ONNX may run.");
    }

    IEnumerator RunZoneCognitiveInference(int zoneIndex)
    {
        RagSequenceAgentMover leader = FindMentalLeader(zoneIndex);
        BSGMLAgent cogMl = FindCognitiveMl(zoneIndex);
        if (leader == null || cogMl == null)
        {
            Debug.LogWarning($"[RagCognitiveInferenceCoordinator] Zone {zoneIndex}: missing leader or cognitive BSGMLAgent.");
            if (leader != null)
                leader.SetCognitivePhaseCompleteForInference(true);
            yield break;
        }

        int stepCount = CountCognitiveSteps(leader);
        int totalDecisions = Mathf.Max(decisionsPerCognitiveStep, 1) * Mathf.Max(stepCount, 1) + tailDecisions;

        Debug.Log($"[RagCognitiveInferenceCoordinator] Zone {zoneIndex}: {stepCount} cognitive steps → {totalDecisions} ONNX decisions (silent).");

        for (int i = 0; i < totalDecisions; i++)
        {
            cogMl.RequestDecision();
            yield return new WaitForSeconds(0.05f);
        }

        leader.SetCognitivePhaseCompleteForInference(true);

        ZoneDeclarativeMemory mem = ZoneDeclarativeMemory.ForZone(zoneIndex);
        if (mem != null)
            mem.cognitiveReady = true;
    }

    static RagSequenceAgentMover FindMentalLeader(int zoneIndex)
    {
        RagSequenceAgentMover[] movers = FindObjectsOfType<RagSequenceAgentMover>(true);
        RagSequenceAgentMover leader = null;
        foreach (var m in movers)
        {
            if (m == null || !m.isMentalAgent || m.zoneIndex != zoneIndex) continue;
            if (leader == null)
                leader = m;
        }
        return leader;
    }

    static BSGMLAgent FindCognitiveMl(int zoneIndex)
    {
        BSGMLAgent[] agents = FindObjectsOfType<BSGMLAgent>(true);
        foreach (var a in agents)
        {
            if (a != null && a.agentRole == BSGMLAgent.AgentRole.Mental && a.zoneIndex == zoneIndex)
                return a;
        }
        return null;
    }

    static int CountCognitiveSteps(RagSequenceAgentMover leader)
    {
        if (leader == null) return 1;
        string id = ZoneAgentIds.NormalizeProfileAgentId(leader.agentId, leader.zoneIndex);
        if (AgentSequenceManager.Instance == null)
            return 4;
        var seq = AgentSequenceManager.Instance.GetSequence(id);
        if (seq?.actionSequence == null || seq.actionSequence.Count == 0)
            return 4;
        int n = 0;
        foreach (var step in seq.actionSequence)
        {
            if (step == null) continue;
            if (string.Equals(step.currentCognitiveState, "cognitive", System.StringComparison.OrdinalIgnoreCase))
                n++;
        }
        return Mathf.Max(n, 1);
    }
}
