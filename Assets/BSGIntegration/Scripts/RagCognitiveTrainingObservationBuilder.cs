using UnityEngine;
using Unity.MLAgents.Sensors;

/// <summary>
/// ML observations for RAG mental (M_A) agents — same <see cref="RagTrainingObservationBuilder.ObservationCount"/>
/// as physical P-agents so one YAML/network shape works; emphasizes cognitive navigation target when set.
/// </summary>
public static class RagCognitiveTrainingObservationBuilder
{
    public const int ObservationCount = RagTrainingObservationBuilder.ObservationCount;

    public static void AppendObservations(VectorSensor sensor, BSGMLAgent agent)
    {
        int zi = Mathf.Clamp(agent.zoneIndex, 0, 3);
        ZoneDeclarativeMemory mem = ZoneDeclarativeMemory.ForZone(zi);

        sensor.AddObservation(agent.skillLevel / 100f);
        float desire = Mathf.Max(0.01f, agent.desireLevel);
        sensor.AddObservation(desire / 100f);
        sensor.AddObservation(Mathf.Clamp01(agent.skillLevel / desire));
        sensor.AddObservation(zi / 3f);

        CognitivePhaseOrchestrator orch = CognitivePhaseOrchestrator.GetOrCreateForZone(zi);
        float phaseNorm = orch != null ? Mathf.Clamp01(orch.CurrentPhase / 3f) : 0f;
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
        sensor.AddObservation((mentalDone + physDone) / (float)(mt + pt));

        sensor.AddObservation(mem != null && mem.cognitiveReady ? 1f : 0f);
        sensor.AddObservation(mem != null ? Mathf.Clamp01(mem.totalCognitiveReward / 10f) : 0f);
        sensor.AddObservation(agent.HasMlNavigationTarget ? 1f : 0f);
        sensor.AddObservation(string.IsNullOrEmpty(agent.MlNavigationStationId) ? 0f : 1f);
        sensor.AddObservation(mem != null && !string.IsNullOrEmpty(mem.goalBufferSubGoal) ? 1f : 0f);

        Vector3 pos = agent.transform.position;
        Vector3 targetPos = Vector3.zero;
        bool hasTarget = false;
        if (agent.TryGetMlNavigationTarget(out Vector3 nav))
        {
            targetPos = nav;
            hasTarget = true;
        }
        else if (!string.IsNullOrEmpty(agent.MlNavigationStationId))
        {
            GameObject st = GameObject.Find(agent.MlNavigationStationId);
            if (st != null)
            {
                targetPos = st.transform.position;
                hasTarget = true;
            }
        }

        Vector3 to = hasTarget ? (targetPos - pos) : Vector3.forward;
        to.y = 0f;
        float dm = to.magnitude;
        Vector3 dir = dm > 0.01f ? to / dm : Vector3.zero;
        sensor.AddObservation(dir.x);
        sensor.AddObservation(dir.z);
        sensor.AddObservation(Mathf.Clamp01(dm / 40f));

        sensor.AddObservation(0f);

        sensor.AddObservation(pos.x / 25f);
        sensor.AddObservation(pos.z / 25f);
        sensor.AddObservation(agent.transform.eulerAngles.y / 360f);
        sensor.AddObservation(Vector3.Distance(pos, agent.StartPositionForObservation) / 40f);

        float near = hasTarget && dm < 2.5f ? 1f : 0f;
        sensor.AddObservation(near);
        sensor.AddObservation(1f);

        sensor.AddObservation(agent.ObservationSkillBit("repair"));
        sensor.AddObservation(agent.ObservationSkillBit("inspect"));
        sensor.AddObservation(agent.IsMlAgentActive ? 1f : 0f);

        float branchNorm = 0f;
        MentalAgentController mac = agent.GetComponent<MentalAgentController>();
        if (mac != null)
        {
            switch (mac.role)
            {
                case MentalAgentController.MRole.M_A: branchNorm = 0.33f; break;
                case MentalAgentController.MRole.M_B: branchNorm = 0.66f; break;
                case MentalAgentController.MRole.M_C: branchNorm = 1f; break;
            }
        }
        sensor.AddObservation(branchNorm);

        sensor.AddObservation(orch != null && orch.IsCognitivePhaseComplete ? 1f : 0f);
        sensor.AddObservation(mem != null ? Mathf.Clamp01(mem.goalBufferResolvedDesireLevel / 100f) : 0f);
        sensor.AddObservation(0f);
    }
}
