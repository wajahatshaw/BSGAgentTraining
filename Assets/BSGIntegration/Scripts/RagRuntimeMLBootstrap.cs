using UnityEngine;
using Unity.MLAgents;

/// <summary>
/// After RAG-only <see cref="SceneGenerator.GenerateScene"/> completes, wires ML-Agents onto
/// physical agents so <c>mlagents-learn</c> sees behaviors (e.g. PhysicalAgentZone0) instead of an empty policy set.
/// </summary>
public static class RagRuntimeMLBootstrap
{
    // Mirror ReplicaSceneSetup zone layout for play-area clamps on BSGMLAgent.
    const float ZoneHalf = 20f;
    const float ZoneInset = 18f;
    const float GridShiftZ = 18f;

    static Vector3 ZoneWorldOffset(int zoneIndex)
    {
        switch (Mathf.Clamp(zoneIndex, 0, 3))
        {
            case 0: return new Vector3(-ZoneHalf, 0f, -ZoneHalf + GridShiftZ);
            case 1: return new Vector3(ZoneHalf, 0f, -ZoneHalf + GridShiftZ);
            case 2: return new Vector3(-ZoneHalf, 0f, ZoneHalf + GridShiftZ);
            default: return new Vector3(ZoneHalf, 0f, ZoneHalf + GridShiftZ);
        }
    }

    static void ApplyZonePlayAreaBounds(BSGMLAgent mlAgent, int zoneIndex)
    {
        if (mlAgent == null) return;
        Vector3 zone = ZoneWorldOffset(zoneIndex);
        mlAgent.constrainToPlayArea = true;
        mlAgent.playAreaMinX = zone.x - ZoneInset;
        mlAgent.playAreaMaxX = zone.x + ZoneInset;
        mlAgent.playAreaMinZ = zone.z - ZoneInset;
        mlAgent.playAreaMaxZ = zone.z + ZoneInset;
    }

    /// <summary>
    /// Call after RAG scene objects exist. Safe to no-op when training is disabled on <paramref name="setup"/>.
    /// </summary>
    public static void TryBootstrap(ReplicaSceneSetup setup)
    {
        if (setup == null || !setup.enableMlTrainingInRagMode)
            return;

        MlAgentsRealtimeTimeScaleEnforcer.Configure(setup.maxUnityTimeScaleForMlAgents, setup.clampUnityTimeScaleForMlAgents);

        ProximityDetectionSystem proxVis = Object.FindObjectOfType<ProximityDetectionSystem>();
        if (proxVis != null)
        {
            proxVis.SetShowVisualZones(false);
            proxVis.logProximityFrameStatus = false;
            proxVis.verboseProximityEventLogs = false;
            if (proxVis.config != null)
                proxVis.config.updateFrequency = Mathf.Max(0.35f, proxVis.config.updateFrequency);
        }

        if (MLTrainingResultsWriter.Instance != null)
            MLTrainingResultsWriter.Instance.diskWritesOnlyWhenTrainerConnected = true;

        RagStepRewardBridge.PrimeSkillSystemCache(Object.FindObjectOfType<SkillBasedActionSystem>());
        RagMlExtrinsicRewardHub.PrimeReplicaSetupCache(setup);

        SceneUILoader loaderForRewards = Object.FindObjectOfType<SceneUILoader>();
        if (loaderForRewards != null)
        {
            string raw = !string.IsNullOrEmpty(loaderForRewards.MergedRawRagJson)
                ? loaderForRewards.MergedRawRagJson
                : loaderForRewards.RawJsonText;
            RagMlExtrinsicRewardHub.LoadEfficiencyPenaltiesFromJson(raw);
        }

        var movers = Object.FindObjectsOfType<RagSequenceAgentMover>(true);
        if (movers == null || movers.Length == 0)
        {
            Debug.Log("ℹ️ RAG ML bootstrap: no RagSequenceAgentMover instances — nothing to attach.");
            return;
        }

        var loader = Object.FindObjectOfType<SceneUILoader>();
        var profiles = loader != null && loader.sceneData != null ? loader.sceneData.agentProfiles : null;

        int attached = 0;
        foreach (var mover in movers)
        {
            if (mover == null || mover.isMentalAgent)
                continue;

            int zoneBit = 1 << Mathf.Clamp(mover.zoneIndex, 0, 3);
            bool zoneTrainingEnabled = setup.trainZonesMask == 0 || (setup.trainZonesMask & zoneBit) != 0;
            if (!zoneTrainingEnabled)
            {
                Debug.Log($"ℹ️ RAG ML bootstrap: skipping physical '{mover.agentId}' zone {mover.zoneIndex} (trainZonesMask={setup.trainZonesMask})");
                continue;
            }

            GameObject go = mover.gameObject;
            string agentId = ZoneAgentIds.NormalizeProfileAgentId(
                string.IsNullOrWhiteSpace(mover.agentId) ? go.name : mover.agentId.Trim(),
                mover.zoneIndex);

            AgentProfile profile = null;
            if (profiles != null)
                profiles.TryGetValue(agentId, out profile);

            string behaviorName = MLAgentAttacher.ResolveMlBehaviorNameForRagPhysical(profile, mover.zoneIndex, agentId);

            // Keep RagSequenceAgentMover enabled — it owns orchestrator physical steps, humanoid walk,
            // and SceneGenerator target resolution. BSGMLAgent is attached for ML-Agents policy IO only.
            mover.enabled = true;

            HumanBodyBuilder.EnsureBareCapsuleHidden(go);

            AgentProximity legacyProx = go.GetComponent<AgentProximity>();
            if (legacyProx != null && HumanBodyBuilder.HasHumanoidBody(go))
                Object.Destroy(legacyProx);

            Vector3 spawnWorld = go.transform.position;

            MLAgentAttacher.AttachMLAgentComponents(go, behaviorName, agentId, spawnWorld);

            var ml = go.GetComponent<BSGMLAgent>();
            if (ml != null)
            {
                ml.zoneIndex = Mathf.Clamp(mover.zoneIndex, 0, 3);
                ml.agentId = ZoneAgentIds.NormalizeProfileAgentId(agentId, ml.zoneIndex);
                ml.agentRole = BSGMLAgent.AgentRole.Physical;
                ml.waitForCognitiveReady = true;
                ml.moveSpeed = 2f;
                ml.physicalMovementSpeed = 2.5f;
                ml.physicalMovementSpeedMultiplier = 1f;
                ml.rotationSpeed = 90f;
                if (profile != null)
                {
                    ml.skillLevel = profile.skillLevel;
                    ml.desireLevel = profile.desireLevel > 0f ? profile.desireLevel : profile.skillLevel;
                }

                ApplyZonePlayAreaBounds(ml, ml.zoneIndex);
                ml.ConfigureRagPhysicalPresentation();
                RagStepRewardBridge.RegisterPhysicalMlAgentForRewards(ml);

                AgentGroundMotor motor = go.GetComponent<AgentGroundMotor>();
                if (motor != null)
                {
                    motor.clampZoneIndex = Mathf.Clamp(mover.zoneIndex, 0, 3);
                    motor.SnapFeetToGround();
                }
            }

            HumanWalkAnimation walk = go.GetComponent<HumanWalkAnimation>();
            if (walk != null)
                walk.enabled = true;

            bool enableDecisions = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "JSONWorkflowScenePersona";
            var dr = go.GetComponent<DecisionRequester>();
            if (dr != null)
            {
                dr.enabled = enableDecisions;
                int period = Mathf.Max(1, setup.ragPhysicalMlDecisionPeriod);
                dr.DecisionPeriod = period;
            }

            attached++;
            Debug.Log($"✅ RAG ML bootstrap: attached '{behaviorName}' to physical agent '{agentId}' (zone {mover.zoneIndex}).");
        }

        int cognitiveAttached = BootstrapCognitiveRagAgents(setup, movers, profiles);

        Debug.Log($"✅ RAG ML bootstrap complete: {attached} physical + {cognitiveAttached} cognitive agent(s). " +
                  "Behaviors: PhysicalAgentZone0-3 + CognitiveAgentZone0-3. " +
                  "Python trainer can now collect steps — leave Play running until checkpoints save .onnx " +
                  "(checkpoint_interval in worker_training_config_fixed.yaml). Ctrl+C exports final models if steps > 0.");
    }

    static int BootstrapCognitiveRagAgents(
        ReplicaSceneSetup setup,
        RagSequenceAgentMover[] movers,
        System.Collections.Generic.Dictionary<string, AgentProfile> profiles)
    {
        if (setup == null || !setup.enableMlTrainingForCognitiveAgents || movers == null)
            return 0;

        int attached = 0;
        foreach (var mover in movers)
        {
            if (mover == null || !mover.isMentalAgent)
                continue;

            int zi = Mathf.Clamp(mover.zoneIndex, 0, 3);
            int zoneBit = 1 << zi;
            if (setup.trainZonesMask != 0 && (setup.trainZonesMask & zoneBit) == 0)
                continue;

            if (TryAttachCognitiveBrainToRagMover(mover, setup, profiles))
                attached++;
        }

        LogCognitiveBrainManifest(attached);

        if (attached == 0)
            Debug.LogWarning("[RagRuntimeMLBootstrap] No RAG mental agents (M1-M4) received CognitiveAgentZone ML attach. " +
                             "Check enableMlTrainingForCognitiveAgents, trainZonesMask, and that SceneGenerator spawned mental movers.");

        return attached;
    }

    static void LogCognitiveBrainManifest(int attachedCount)
    {
        if (attachedCount <= 0) return;

        Debug.Log(
            "[RagMl] Cognitive brain map (one ONNX per zone):\n" +
            "  M1 zone 0 → CognitiveAgentZone0.onnx\n" +
            "  M2 zone 1 → CognitiveAgentZone1.onnx\n" +
            "  M3 zone 2 → CognitiveAgentZone2.onnx\n" +
            "  M4 zone 3 → CognitiveAgentZone3.onnx\n" +
            $"  Attached {attachedCount}/4 mental GameObjects for mlagents-learn.");
    }

    static bool TryAttachCognitiveBrainToRagMover(
        RagSequenceAgentMover mover,
        ReplicaSceneSetup setup,
        System.Collections.Generic.Dictionary<string, AgentProfile> profiles)
    {
        if (mover == null) return false;

        int zi = Mathf.Clamp(mover.zoneIndex, 0, 3);
        GameObject go = mover.gameObject;
        string mentalId = ZoneAgentIds.NormalizeProfileAgentId(
            string.IsNullOrWhiteSpace(mover.agentId) ? go.name : mover.agentId.Trim(), zi);

        AgentProfile mentalProfile = null;
        AgentProfile physicalProfile = null;
        if (profiles != null)
        {
            profiles.TryGetValue(mentalId, out mentalProfile);
            string pAgentId = ZoneAgentIds.TryResolvePhysicalAgentId(zi);
            if (!string.IsNullOrEmpty(pAgentId))
                profiles.TryGetValue(pAgentId, out physicalProfile);
        }

        string behaviorName = MLAgentAttacher.ResolveMlBehaviorNameForRagCognitive(mentalProfile ?? physicalProfile, zi);
        string cognitiveAgentId = $"CognitiveBrain_Zone{zi}";

        if (go.GetComponent<BSGMLAgent>() == null)
            MLAgentAttacher.AttachMLAgentComponents(go, behaviorName, cognitiveAgentId, go.transform.position);

        var ml = go.GetComponent<BSGMLAgent>();
        if (ml == null) return false;

        ml.ConfigureAsCognitiveBrain(zi, cognitiveAgentId, behaviorName, mover.moveSpeed);
        ApplyZonePlayAreaBounds(ml, zi);

        if (mentalProfile != null)
        {
            ml.skillLevel = mentalProfile.skillLevel;
            ml.desireLevel = mentalProfile.desireLevel > 0f ? mentalProfile.desireLevel : mentalProfile.skillLevel;
        }

        var dr = go.GetComponent<DecisionRequester>();
        if (dr != null)
        {
            dr.enabled = true;
            // Observations/rewards only — RagSequenceAgentMover keeps scripted cognitive locomotion.
            dr.DecisionPeriod = Mathf.Max(1, setup.ragCognitiveMlDecisionPeriod);
        }

        Debug.Log($"[RagMl] Mental {mentalId} (zone {zi}) → ML behavior '{behaviorName}' on '{go.name}' " +
                  $"(obs/reward only; RagSequenceAgentMover drives steps, DecisionPeriod={dr?.DecisionPeriod ?? 0})");
        return true;
    }

    /// <summary>Attach CognitiveAgentZoneN to zone M_A when MentalAgentSpawner creates the agent (replica path).</summary>
    public static bool TryAttachCognitiveBrain(MentalAgentController ctrl, ReplicaSceneSetup setup)
    {
        if (ctrl == null)
            return false;
        if (setup == null)
        {
            Debug.LogWarning("[RagRuntimeMLBootstrap] TryAttachCognitiveBrain: ReplicaSceneSetup not found — cognitive ONNX will not train.");
            return false;
        }
        if (!setup.enableMlTrainingInRagMode || !setup.enableMlTrainingForCognitiveAgents)
        {
            Debug.Log($"[RagRuntimeMLBootstrap] Cognitive ML skipped zone {ctrl.zoneIndex} (training flags off).");
            return false;
        }
        if (ctrl.role != MentalAgentController.MRole.M_A)
            return false;
        if (ctrl.GetComponent<BSGMLAgent>() != null && ctrl.deferLocomotionToMl)
            return true;

        int zi = Mathf.Clamp(ctrl.zoneIndex, 0, 3);
        int zoneBit = 1 << zi;
        if (setup.trainZonesMask != 0 && (setup.trainZonesMask & zoneBit) == 0)
            return false;

        var loader = Object.FindObjectOfType<SceneUILoader>();
        var profiles = loader != null && loader.sceneData != null ? loader.sceneData.agentProfiles : null;
        string pAgentId = ctrl.GetZonePAgentIdForBootstrap();
        AgentProfile profile = null;
        if (profiles != null && !string.IsNullOrEmpty(pAgentId))
            profiles.TryGetValue(pAgentId, out profile);

        string behaviorName = MLAgentAttacher.ResolveMlBehaviorNameForRagCognitive(profile, zi);
        string cognitiveAgentId = $"CognitiveBrain_Zone{zi}";
        GameObject go = ctrl.gameObject;

        var ml = go.GetComponent<BSGMLAgent>();
        if (ml == null)
            MLAgentAttacher.AttachMLAgentComponents(go, behaviorName, cognitiveAgentId, go.transform.position);
        ml = go.GetComponent<BSGMLAgent>();
        if (ml == null)
        {
            Debug.LogError($"[RagRuntimeMLBootstrap] Failed to add BSGMLAgent on M_A zone {zi}");
            return false;
        }

        ml.ConfigureAsCognitiveBrain(zi, cognitiveAgentId, behaviorName, ctrl.moveSpeed);
        ApplyZonePlayAreaBounds(ml, zi);
        RagStepRewardBridge.RegisterCognitiveMlAgentForRewards(ml);
        ctrl.BindMlTraining(ml);

        var dr = go.GetComponent<DecisionRequester>();
        if (dr != null)
        {
            dr.enabled = true;
            dr.DecisionPeriod = Mathf.Max(1, setup.ragCognitiveMlDecisionPeriod);
        }

        Debug.Log($"✅ RAG ML bootstrap: attached '{behaviorName}' to M_A zone {zi}.");
        return true;
    }
}
