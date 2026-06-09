using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Policies;

/// <summary>
/// Attaches BSGMLAgent + ONNX InferenceOnly for <see cref="RagInferenceSceneController"/> scenes.
/// </summary>
public static class RagInferenceMLBootstrap
{
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

    public static RagMlBrainDeployer EnsureDeployerInScene()
    {
        RagMlBrainDeployer deployer = Object.FindObjectOfType<RagMlBrainDeployer>();
        if (deployer != null)
            return deployer;

        GameObject manager = GameObject.Find("REPLICA_SceneManager");
        if (manager == null)
        {
            Debug.LogError("[RagInferenceMLBootstrap] REPLICA_SceneManager not found — run BSG → Inference → 2. Wire Inference Scene.");
            return null;
        }

        deployer = manager.GetComponent<RagMlBrainDeployer>();
        if (deployer == null)
            deployer = manager.AddComponent<RagMlBrainDeployer>();

        deployer.applyOnStart = false;
        deployer.deployPhysicalBrains = true;
        deployer.deployCognitiveBrains = true;
        deployer.autoLoadModelsFromInferenceFolder = true;

        var ctrl = manager.GetComponent<RagInferenceSceneController>();
        if (ctrl != null)
            ctrl.brainDeployer = deployer;

        return deployer;
    }

    public static void AttachInferenceAgents(RagMlBrainDeployer deployer, bool useScriptedPhysicalLocomotion = true)
    {
        deployer = deployer != null ? deployer : EnsureDeployerInScene();
        if (deployer == null)
            return;

        if (!deployer.EnsureModelsReadyForInference())
            Debug.LogError("[RagInferenceMLBootstrap] ONNX models not ready — physical/cognitive brains will not attach.");

        var movers = Object.FindObjectsOfType<RagSequenceAgentMover>(true);
        if (movers == null || movers.Length == 0)
        {
            Debug.LogWarning("[RagInferenceMLBootstrap] No RagSequenceAgentMover — wait until scene generation finishes.");
            return;
        }

        var loader = Object.FindObjectOfType<SceneUILoader>();
        var profiles = loader != null && loader.sceneData != null ? loader.sceneData.agentProfiles : null;

        int physical = 0;
        int cognitive = 0;

        foreach (var mover in movers)
        {
            if (mover == null) continue;

            int zi = Mathf.Clamp(mover.zoneIndex, 0, 3);
            GameObject go = mover.gameObject;
            Vector3 spawnWorld = go.transform.position;
            bool scriptedCognitiveWalk = RagInferenceSceneController.UseInvisibleCognitiveScriptedWalk();
            bool scriptedPhysicalWalk = useScriptedPhysicalLocomotion;

            if (mover.isMentalAgent)
            {
                mover.inferenceSuppressScriptedCognitive = !scriptedCognitiveWalk;
                mover.enabled = scriptedCognitiveWalk;

                string mentalId = ZoneAgentIds.NormalizeProfileAgentId(
                    string.IsNullOrWhiteSpace(mover.agentId) ? go.name : mover.agentId.Trim(), zi);
                AgentProfile profile = null;
                if (profiles != null)
                    profiles.TryGetValue(mentalId, out profile);

                string behaviorName = MLAgentAttacher.ResolveMlBehaviorNameForRagCognitive(profile, zi);
                string cognitiveAgentId = $"CognitiveBrain_Zone{zi}";

                if (go.GetComponent<BSGMLAgent>() == null)
                    MLAgentAttacher.AttachMLAgentComponents(go, behaviorName, cognitiveAgentId, spawnWorld);

                var ml = go.GetComponent<BSGMLAgent>();
                if (ml == null) continue;

                ml.ConfigureAsCognitiveBrain(zi, cognitiveAgentId, behaviorName, mover.moveSpeed);
                ml.agentConnectionWaitSeconds = 0f;
                // Scripted invisible walk: RagMover MoveTowards + dwell. ONNX locomotion only when mover is off.
                ml.inferenceOnnxControlsLocomotion = !scriptedCognitiveWalk;
                if (scriptedCognitiveWalk)
                    ml.YieldCognitiveSequenceToRagMover();
                ApplyZonePlayAreaBounds(ml, zi);

                var dr = go.GetComponent<DecisionRequester>();
                if (dr != null)
                {
                    dr.enabled = true;
                    dr.DecisionPeriod = 4;
                }

                if (deployer.ApplyCognitiveBrainToAgent(ml))
                    cognitive++;

                continue;
            }

            // Match training scene: mover handles walk/approach/proximity; ONNX drives interaction only.
            mover.enabled = true;

            string agentId = ZoneAgentIds.NormalizeProfileAgentId(
                string.IsNullOrWhiteSpace(mover.agentId) ? go.name : mover.agentId.Trim(), zi);
            AgentProfile pProfile = null;
            if (profiles != null)
                profiles.TryGetValue(agentId, out pProfile);

            string pBehavior = MLAgentAttacher.ResolveMlBehaviorNameForRagPhysical(pProfile, zi, agentId);

            HumanBodyBuilder.EnsureBareCapsuleHidden(go);

            if (go.GetComponent<BSGMLAgent>() == null)
                MLAgentAttacher.AttachMLAgentComponents(go, pBehavior, agentId, spawnWorld);

            var pMl = go.GetComponent<BSGMLAgent>();
            if (pMl == null) continue;

            pMl.zoneIndex = zi;
            pMl.agentId = agentId;
            pMl.agentRole = BSGMLAgent.AgentRole.Physical;
            pMl.waitForCognitiveReady = scriptedCognitiveWalk;
            pMl.physicalUnlocked = false;
            pMl.inferenceOnnxControlsLocomotion = !scriptedPhysicalWalk;
            pMl.ragSpawnPreserveActive = true;
            pMl.agentConnectionWaitSeconds = 0f;
            pMl.moveSpeed = 2f;
            pMl.physicalMovementSpeed = 2.5f;
            pMl.rotationSpeed = 90f;
            if (pProfile != null)
            {
                pMl.skillLevel = pProfile.skillLevel;
                pMl.desireLevel = pProfile.desireLevel > 0f ? pProfile.desireLevel : pProfile.skillLevel;
            }

            ApplyZonePlayAreaBounds(pMl, zi);
            pMl.ConfigureRagPhysicalPresentation();

            HumanWalkAnimation walk = go.GetComponent<HumanWalkAnimation>();
            if (walk != null)
            {
                walk.enabled = true;
                walk.Unfreeze();
            }
            HandRotationManager.EnsureOnAgent(go);

            var pDr = go.GetComponent<DecisionRequester>();
            if (pDr != null)
            {
                pDr.enabled = true;
                pDr.DecisionPeriod = 4;
            }

            if (deployer.ApplyPhysicalBrainToAgent(pMl))
                physical++;
        }

        deployer.ApplyBrains();

        RagMenuController.EnsureInScene()?.RefreshMenuOptions();

        string physLoco = useScriptedPhysicalLocomotion ? "scripted mover + ONNX interaction" : "ONNX locomotion";
        Debug.Log($"[RagInferenceMLBootstrap] {physical} physical + {cognitive} cognitive agents with ONNX attached ({physLoco}).");
    }

    public static void ReassertOnnxBrains(RagMlBrainDeployer deployer)
    {
        deployer = deployer != null ? deployer : EnsureDeployerInScene();
        if (deployer == null) return;
        deployer.EnsureModelsReadyForInference();
        deployer.ApplyBrains();
    }
}
