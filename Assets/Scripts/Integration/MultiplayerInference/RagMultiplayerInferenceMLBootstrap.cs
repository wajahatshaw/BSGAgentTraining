using Unity.MLAgents;
using UnityEngine;

/// <summary>
/// Attaches ONNX InferenceOnly brains for zone 0 in the multiplayer inference scene:
/// M1 cognitive agent + designated Photon player as P1 physical agent.
/// </summary>
public static class RagMultiplayerInferenceMLBootstrap
{
    const int ZoneIndex = 0;

    public static void AttachZone0CognitiveInference(
        RagMlBrainDeployer deployer,
        bool useScriptedCognitiveWalk = true)
    {
        deployer = deployer != null ? deployer : RagInferenceMLBootstrap.EnsureDeployerInScene();
        if (deployer == null)
            return;

        if (!deployer.EnsureModelsReadyForInference())
        {
            Debug.LogError("[RagMultiplayerInferenceMLBootstrap] ONNX models not ready — assign models under Assets/ML-Agents/Models/Inference.");
            return;
        }

        var loader = Object.FindObjectOfType<SceneUILoader>();
        var profiles = loader != null && loader.sceneData != null ? loader.sceneData.agentProfiles : null;

        int attached = 0;
        foreach (RagSequenceAgentMover mover in Object.FindObjectsOfType<RagSequenceAgentMover>(true))
        {
            if (mover == null || !mover.isMentalAgent || mover.zoneIndex != ZoneIndex)
                continue;

            if (AttachCognitiveInferenceToMover(mover, deployer, profiles, useScriptedCognitiveWalk))
                attached++;
        }

        deployer.ApplyBrains();
        Debug.Log($"[RagMultiplayerInferenceMLBootstrap] Zone 0 cognitive inference attached to {attached} mental agent(s).");
    }

    /// <summary>
    /// Multiplayer embed: SceneGenerator skips P1; Photon player binds via <see cref="PlayerRagInferenceBridge"/>.
    /// </summary>
    public static bool TryBootstrapPhotonPlayerPhysical(
        RagSequenceAgentMover mover,
        RagMlBrainDeployer deployer,
        bool useScriptedPhysicalLocomotion = true)
    {
        if (mover == null || mover.isMentalAgent || !mover.hostPlayerMovement)
            return false;
        if (mover.zoneIndex != ZoneIndex)
            return false;

        deployer = deployer != null ? deployer : RagInferenceMLBootstrap.EnsureDeployerInScene();
        if (deployer == null || !deployer.EnsureModelsReadyForInference())
            return false;

        GameObject go = mover.gameObject;
        if (go.GetComponent<BSGMLAgent>() != null)
        {
            deployer.ApplyPhysicalBrainToAgent(go.GetComponent<BSGMLAgent>());
            deployer.ApplyBrains();
            return true;
        }

        var loader = Object.FindObjectOfType<SceneUILoader>();
        var profiles = loader != null && loader.sceneData != null ? loader.sceneData.agentProfiles : null;

        string agentId = ZoneAgentIds.NormalizeProfileAgentId(
            string.IsNullOrWhiteSpace(mover.agentId) ? go.name : mover.agentId.Trim(),
            ZoneIndex);

        AgentProfile profile = null;
        if (profiles != null)
            profiles.TryGetValue(agentId, out profile);

        string behaviorName = MLAgentAttacher.ResolveMlBehaviorNameForRagPhysical(profile, ZoneIndex, agentId);
        Vector3 spawnWorld = go.transform.position;

        if (BsgIntegrationSettings.TryResolveDesignatedPhysicalSpawnWorld != null)
        {
            Vector3? designated = BsgIntegrationSettings.TryResolveDesignatedPhysicalSpawnWorld(spawnWorld.y);
            if (designated.HasValue)
                spawnWorld = designated.Value;
        }

        mover.enabled = true;
        HumanBodyBuilder.EnsureBareCapsuleHidden(go);

        MLAgentAttacher.AttachMLAgentComponents(go, behaviorName, agentId, spawnWorld);

        BSGMLAgent ml = go.GetComponent<BSGMLAgent>();
        if (ml == null)
            return false;

        bool scriptedWalk = useScriptedPhysicalLocomotion;
        ml.zoneIndex = ZoneIndex;
        ml.agentId = agentId;
        ml.agentRole = BSGMLAgent.AgentRole.Physical;
        ml.waitForCognitiveReady = RagInferenceSceneController.UseInvisibleCognitiveScriptedWalk();
        ml.physicalUnlocked = false;
        ml.inferenceOnnxControlsLocomotion = !scriptedWalk;
        ml.ragSpawnPreserveActive = true;
        ml.agentConnectionWaitSeconds = 0f;
        ml.moveSpeed = 2f;
        ml.physicalMovementSpeed = 2.5f;
        ml.rotationSpeed = 90f;

        if (profile != null)
        {
            ml.skillLevel = profile.skillLevel;
            ml.desireLevel = profile.desireLevel > 0f ? profile.desireLevel : profile.skillLevel;
        }

        MultiplayerRagZone0Anchor anchor = MultiplayerRagZone0Anchor.Instance;
        anchor?.ApplyPlayAreaToZoneAgents();
        ml.ConfigureRagPhysicalPresentation();

        HumanWalkAnimation walk = go.GetComponent<HumanWalkAnimation>();
        if (walk != null)
        {
            walk.enabled = true;
            walk.Unfreeze();
        }

        HandRotationManager.EnsureOnAgent(go);

        DecisionRequester dr = go.GetComponent<DecisionRequester>();
        if (dr != null)
        {
            dr.enabled = true;
            dr.DecisionPeriod = 4;
        }

        if (!deployer.ApplyPhysicalBrainToAgent(ml))
            return false;

        deployer.ApplyBrains();
        RagMenuController.EnsureInScene()?.RefreshMenuOptions();

        string loco = scriptedWalk ? "scripted mover + ONNX interaction" : "ONNX locomotion";
        Debug.Log($"[RagMultiplayerInferenceMLBootstrap] PhysicalAgentZone0 ONNX attached to designated Photon player ({agentId}, {loco}).");
        return true;
    }

    static bool AttachCognitiveInferenceToMover(
        RagSequenceAgentMover mover,
        RagMlBrainDeployer deployer,
        System.Collections.Generic.Dictionary<string, AgentProfile> profiles,
        bool scriptedCognitiveWalk)
    {
        GameObject go = mover.gameObject;
        Vector3 spawnWorld = go.transform.position;

        mover.inferenceSuppressScriptedCognitive = !scriptedCognitiveWalk;
        mover.enabled = scriptedCognitiveWalk;

        string mentalId = ZoneAgentIds.NormalizeProfileAgentId(
            string.IsNullOrWhiteSpace(mover.agentId) ? go.name : mover.agentId.Trim(),
            ZoneIndex);

        AgentProfile profile = null;
        if (profiles != null)
            profiles.TryGetValue(mentalId, out profile);

        string behaviorName = MLAgentAttacher.ResolveMlBehaviorNameForRagCognitive(profile, ZoneIndex);
        string cognitiveAgentId = $"CognitiveBrain_Zone{ZoneIndex}";

        if (go.GetComponent<BSGMLAgent>() == null)
            MLAgentAttacher.AttachMLAgentComponents(go, behaviorName, cognitiveAgentId, spawnWorld);

        BSGMLAgent ml = go.GetComponent<BSGMLAgent>();
        if (ml == null)
            return false;

        ml.ConfigureAsCognitiveBrain(ZoneIndex, cognitiveAgentId, behaviorName, mover.moveSpeed);
        ml.agentConnectionWaitSeconds = 0f;
        ml.inferenceOnnxControlsLocomotion = !scriptedCognitiveWalk;
        if (scriptedCognitiveWalk)
            ml.YieldCognitiveSequenceToRagMover();

        MultiplayerRagZone0Anchor anchor = MultiplayerRagZone0Anchor.Instance;
        anchor?.ApplyPlayAreaToZoneAgents();

        DecisionRequester dr = go.GetComponent<DecisionRequester>();
        if (dr != null)
        {
            dr.enabled = true;
            dr.DecisionPeriod = 4;
        }

        return deployer.ApplyCognitiveBrainToAgent(ml);
    }
}
