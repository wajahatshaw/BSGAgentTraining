using UnityEngine;
using Unity.MLAgents.Sensors;

/// <summary>
/// RAG-specific ML observations for replica physical agents (orchestrator, payloads, timing, target).
/// Exactly <see cref="ObservationCount"/> floats — must stay aligned with BehaviorParameters and YAML.
/// </summary>
public static class RagTrainingObservationBuilder
{
    public const int ObservationCount = 30;

    static readonly string[] s_payloadWatchKeys =
    {
        "manual_command",
        "physical_result",
        "task_plan",
        "intention",
        "campaign_objective_focus_visible"
    };

    /// <summary>Adds <see cref="ObservationCount"/> normalized floats.</summary>
    public static void AppendObservations(VectorSensor sensor, BSGMLAgent agent)
    {
        float desireLevel = Mathf.Max(0.01f, agent.desireLevel);
        sensor.AddObservation(agent.skillLevel / 100f);
        sensor.AddObservation(desireLevel / 100f);
        sensor.AddObservation(Mathf.Clamp01(agent.skillLevel / desireLevel));

        int zi = Mathf.Clamp(agent.zoneIndex, 0, 3);
        sensor.AddObservation(zi / 3f);

        CognitivePhaseOrchestrator orch = CognitivePhaseOrchestrator.GetOrCreateForZone(zi);
        float phaseNorm = orch != null ? Mathf.Clamp01(orch.CurrentPhase / 3f) : 0.33f;
        sensor.AddObservation(phaseNorm);

        int mentalDone = 0, physDone = 0, mentalTotal = 1, physTotal = 1;
        if (orch != null)
        {
            orch.GetCompletedStepCounts(out mentalDone, out physDone);
            orch.GetTotalStepCounts(out mentalTotal, out physTotal);
        }

        int mt = Mathf.Max(1, mentalTotal);
        int pt = Mathf.Max(1, physTotal);
        sensor.AddObservation(mentalDone / (float)mt);
        sensor.AddObservation(physDone / (float)pt);
        int sumTot = Mathf.Max(1, mentalTotal + physTotal);
        sensor.AddObservation((mentalDone + physDone) / (float)sumTot);

        ZoneDeclarativeMemory mem = ZoneDeclarativeMemory.ForZone(zi);
        for (int i = 0; i < 5; i++)
        {
            string key = s_payloadWatchKeys[i];
            float bit = mem != null && mem.TryGetPayload(key, out _) ? 1f : 0f;
            sensor.AddObservation(bit);
        }

        Vector3 pos = agent.transform.position;

        Vector3 targetPos = Vector3.zero;
        bool hasTarget = false;
        string targetId = "";

        if (agent.TryGetRagPhysicalObservationTarget(out Vector3 ragTarget, out string ragTargetId))
        {
            targetPos = ragTarget;
            targetId = ragTargetId;
            hasTarget = true;
        }

        TemporalCognitionRuntime rt = null;
        TemporalCognitionRuntime.TryGetForZone(zi, out rt);
        TemporalStepTimingRecord active = rt != null ? rt.GetPrimaryActiveRecord() : null;

        if (!hasTarget && active != null && !string.IsNullOrEmpty(active.targetObjectId))
        {
            targetId = active.targetObjectId;
            if (SceneGenerator.Instance != null)
            {
                Vector3 p = SceneGenerator.Instance.GetTargetPositionById(targetId, zi);
                if (p != Vector3.zero)
                {
                    targetPos = p;
                    hasTarget = true;
                }
            }
        }

        Vector3 to = hasTarget ? (targetPos - pos) : Vector3.forward;
        to.y = 0f;
        float dm = to.magnitude;
        Vector3 dir = dm > 0.01f ? to / dm : Vector3.zero;
        sensor.AddObservation(dir.x);
        sensor.AddObservation(dir.z);
        sensor.AddObservation(Mathf.Clamp01(dm / 40f));

        float timing = 0f;
        if (active != null && active.expectedDurationSec > 0.01f)
        {
            float nowSec = rt != null ? rt.ElapsedSceneSeconds : Time.time;
            timing = Mathf.Clamp01(active.ActualElapsedSec(nowSec) / (active.expectedDurationSec * 2f));
        }
        sensor.AddObservation(timing);

        sensor.AddObservation(pos.x / 25f);
        sensor.AddObservation(pos.z / 25f);
        sensor.AddObservation(agent.transform.eulerAngles.y / 360f);
        sensor.AddObservation(Vector3.Distance(pos, agent.StartPositionForObservation) / 40f);

        float near = hasTarget && dm < 4f ? 1f : 0f;
        sensor.AddObservation(near);

        float activeMental = 0f;
        if (active != null && string.Equals(active.agentRole, "M", System.StringComparison.OrdinalIgnoreCase))
            activeMental = 1f;
        else if (active != null && !string.IsNullOrEmpty(active.targetObjectId)
                 && active.targetObjectId.StartsWith("cognitive_", System.StringComparison.OrdinalIgnoreCase))
            activeMental = 1f;
        sensor.AddObservation(activeMental);

        sensor.AddObservation(agent.ObservationSkillBit("repair"));
        sensor.AddObservation(agent.ObservationSkillBit("inspect"));
        sensor.AddObservation(agent.IsMlAgentActive ? 1f : 0f);

        float subNorm = 0f;
        if (orch != null && !string.IsNullOrEmpty(orch.CurrentSubTaskId))
        {
            string sid = orch.CurrentSubTaskId;
            if (sid.StartsWith("st_", System.StringComparison.OrdinalIgnoreCase)
                && int.TryParse(sid.Substring(3), out int sn))
                subNorm = Mathf.Clamp01(sn / 10f);
        }
        sensor.AddObservation(subNorm);

        sensor.AddObservation(orch != null && orch.IsCognitivePhaseComplete ? 1f : 0f);
        sensor.AddObservation(agent.ObservationInteractionSubtype());
        sensor.AddObservation(agent.ObservationFingerTargetDistance());
    }
}
