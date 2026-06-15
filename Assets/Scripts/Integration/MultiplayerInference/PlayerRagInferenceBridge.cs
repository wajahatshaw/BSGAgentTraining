using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Binds the local Photon player as zone 0 physical agent P1 for ONNX inference testing.
/// Mirrors <see cref="PlayerRagPhysicalBridge"/> but attaches InferenceOnly brains instead of Python training.
/// </summary>
public static class PlayerRagInferenceBridge
{
    const string DefaultPhysicalAgentId = "P1";
    const int ZoneIndex = 0;

    public static bool IsBound { get; private set; }
    public static RagSequenceAgentMover BoundMover { get; private set; }

    public static void BeginBinding(MonoBehaviour host)
    {
        if (!BsgIntegrationSettings.UsePhotonPlayerAsPhysicalAgent)
            return;
        if (host == null)
            return;
        host.StartCoroutine(BindLocalPlayerWhenReady(host));
    }

    static IEnumerator BindLocalPlayerWhenReady(MonoBehaviour host)
    {
        if (IsBound)
            yield break;

        for (int i = 0; i < 120; i++)
        {
            if (!BsgIntegrationSettings.UsePhotonPlayerAsPhysicalAgent)
                yield break;

            RagPhysicalAgentAssignment.EnsureAssignedInRoom();
            if (!RagPhysicalAgentAssignment.WaitForDesignationReady())
            {
                yield return new WaitForSeconds(0.25f);
                continue;
            }

            if (!RagPhysicalAgentAssignment.IsLocalPlayerRagPhysicalAgent())
            {
                Debug.Log("[PlayerRagInferenceBridge] Local player is not the designated RAG physical agent — joystick only.");
                yield break;
            }

            PlayerMovement localPlayer = FindLocalPlayerMovement();
            if (localPlayer != null && AgentSequenceManager.Instance != null)
            {
                if (TryBindPlayer(localPlayer, host))
                {
                    host.StartCoroutine(EnsurePhysicalSpawnPlacement(localPlayer));
                    yield break;
                }
            }

            yield return new WaitForSeconds(0.25f);
        }

        Debug.LogWarning("[PlayerRagInferenceBridge] Timed out waiting for local player + RAG runtime.");
    }

    static PlayerMovement FindLocalPlayerMovement()
    {
        foreach (PlayerMovement pm in Object.FindObjectsOfType<PlayerMovement>())
        {
            if (pm == null)
                continue;
            PhotonView pv = pm.GetComponent<PhotonView>();
            if (pv != null && pv.IsMine)
                return pm;
        }

        return null;
    }

    static bool TryBindPlayer(PlayerMovement playerMovement, MonoBehaviour host)
    {
        if (playerMovement == null || IsBound)
            return false;

        GameObject playerGo = playerMovement.gameObject;
        AgentGroundMotor motor = PlayerRagPhysicalAgentSetup.Configure(playerGo, ZoneIndex);
        playerMovement.EnableRagGroundMotorMovement(motor);

        RagSequenceAgentMover existingMover = playerGo.GetComponent<RagSequenceAgentMover>();
        if (existingMover != null)
        {
            IsBound = true;
            BoundMover = existingMover;
            ApplyDesignatedPlayerNavTuning(existingMover);
            existingMover.RefreshHostPlayerGroundMotor();
            RagPhysicalAgentLocalMode.ApplyForLocalClient(playerMovement);
            host.StartCoroutine(FinalizeBindAfterMoverStart(existingMover));
            return true;
        }

        string physicalAgentId = ResolveZone0PhysicalAgentId();
        float moveSpeed = ResolvePhysicalMoveSpeed(physicalAgentId);

        RagSequenceAgentMover mover = playerGo.AddComponent<RagSequenceAgentMover>();
        mover.agentId = physicalAgentId;
        mover.zoneIndex = ZoneIndex;
        mover.isMentalAgent = false;
        mover.hostPlayerMovement = true;
        mover.gateOperationalOnLeaderCognitive = true;
        ApplyDesignatedPlayerNavTuning(mover);
        mover.moveSpeed = moveSpeed;
        mover.mentalLeaderAgentId = ResolveZone0MentalLeaderId();

        HandRotationManager.EnsureOnAgent(playerGo);

        IsBound = true;
        BoundMover = mover;
        mover.RefreshHostPlayerGroundMotor();
        RagPhysicalAgentLocalMode.ApplyForLocalClient(playerMovement);
        Debug.Log($"[PlayerRagInferenceBridge] Local Photon player bound as {physicalAgentId} (zone {ZoneIndex}) — ONNX inference drives interaction.");
        host.StartCoroutine(FinalizeBindAfterMoverStart(mover));
        return true;
    }

    static IEnumerator FinalizeBindAfterMoverStart(RagSequenceAgentMover mover)
    {
        for (int i = 0; i < 40; i++)
        {
            if (IsPhysicalEmbedContentReady())
                break;
            yield return new WaitForSeconds(0.25f);
        }

        yield return null;

        CognitivePhaseOrchestrator orch = CognitivePhaseOrchestrator.GetOrCreateForZone(ZoneIndex);
        orch?.EnsureInitializedFromSceneAgents();

        if (mover != null)
        {
            mover.RefreshHostPlayerGroundMotor();
            mover.RecoverOrchestratorDispatchIfNeeded();
        }

        if (mover != null)
            RelocateDesignatedPlayerToPhysicalSpawn(mover.transform, force: true);

        TryAttachInferenceForDesignatedPlayer(mover);
    }

    static bool IsPhysicalEmbedContentReady()
    {
        MultiplayerRagZone0Anchor anchor = MultiplayerRagZone0Anchor.Instance;
        if (anchor == null)
            return false;

        foreach (DeclarativeObjectMetadata meta in Object.FindObjectsOfType<DeclarativeObjectMetadata>())
        {
            if (meta == null || meta.gameObject.name.IndexOf("cognitive_", System.StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            if (meta.gameObject.name.StartsWith("Tool_", System.StringComparison.OrdinalIgnoreCase)
                || meta.GetComponent<EnvironmentSolidCollider>() != null)
                return true;
        }

        return Object.FindObjectOfType<CognitiveStationInteractable>() != null;
    }

    static void TryAttachInferenceForDesignatedPlayer(RagSequenceAgentMover mover)
    {
        if (mover == null || !RagPhysicalAgentAssignment.IsLocalPlayerRagPhysicalAgent())
            return;

        RagMlBrainDeployer deployer = RagInferenceMLBootstrap.EnsureDeployerInScene();
        bool useScriptedLoco = RagInferenceSceneController.UseScriptedPhysicalLocomotion();

        if (!RagMultiplayerInferenceMLBootstrap.TryBootstrapPhotonPlayerPhysical(mover, deployer, useScriptedLoco))
            return;

        MultiplayerRagZone0Anchor anchor = MultiplayerRagZone0Anchor.Instance;
        anchor?.ApplyPlayAreaToZoneAgents();
        RagInferenceMLBootstrap.ReassertOnnxBrains(deployer);
        Debug.Log("[PlayerRagInferenceBridge] PhysicalAgentZone0 ONNX attached to designated Photon player (P1, zone 0).");
    }

    static IEnumerator EnsurePhysicalSpawnPlacement(PlayerMovement playerMovement)
    {
        for (int i = 0; i < 24; i++)
        {
            if (playerMovement == null)
                yield break;

            MultiplayerRagZone0Anchor anchor = MultiplayerRagZone0Anchor.Instance;
            if (anchor != null)
            {
                RelocateDesignatedPlayerToPhysicalSpawn(playerMovement.transform);
                yield break;
            }

            yield return new WaitForSeconds(0.25f);
        }
    }

    static void RelocateDesignatedPlayerToPhysicalSpawn(Transform playerTransform, bool force = false)
    {
        if (playerTransform == null || !RagPhysicalAgentAssignment.IsLocalPlayerRagPhysicalAgent())
            return;

        MultiplayerRagZone0Anchor anchor = MultiplayerRagZone0Anchor.Instance;
        if (anchor == null || !anchor.TryGetDesignatedPhysicalAgentSpawnWorld(out Vector3 target, playerTransform.position.y))
            return;

        Vector3 delta = target - playerTransform.position;
        delta.y = 0f;
        if (!force && delta.sqrMagnitude < 0.35f)
            return;

        Rigidbody rb = playerTransform.GetComponent<Rigidbody>();
        Quaternion rot = anchor.GetDesignatedPhysicalAgentSpawnWorldRotation();
        if (rb != null)
        {
            bool wasKinematic = rb.isKinematic;
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = target;
            rb.rotation = rot;
            rb.isKinematic = wasKinematic;
        }
        else
        {
            playerTransform.SetPositionAndRotation(target, rot);
        }

        AgentGroundMotor motor = playerTransform.GetComponent<AgentGroundMotor>();
        motor?.SnapFeetToGround();

        BSGMLAgent ml = playerTransform.GetComponent<BSGMLAgent>();
        if (ml != null)
            ml.SetRagSpawnPreserve(target);

        Debug.Log($"[PlayerRagInferenceBridge] Designated physical agent moved to spawn near physical targets at {target}.");
    }

    static string ResolveZone0PhysicalAgentId()
    {
        SkillBasedActionSystem skill = Object.FindObjectOfType<SkillBasedActionSystem>();
        if (skill != null)
        {
            string id = skill.GetPhysicalAgentIdForZone(ZoneIndex);
            if (!string.IsNullOrWhiteSpace(id))
                return id.Trim();
        }

        return ZoneAgentIds.TryResolvePhysicalAgentId(ZoneIndex) ?? DefaultPhysicalAgentId;
    }

    static string ResolveZone0MentalLeaderId()
    {
        foreach (RagSequenceAgentMover m in Object.FindObjectsOfType<RagSequenceAgentMover>())
        {
            if (m != null && m.isMentalAgent && m.zoneIndex == ZoneIndex && !string.IsNullOrWhiteSpace(m.agentId))
                return m.agentId;
        }

        string leader = RagSceneJsonBridge.LastParsedLeaderAgentId;
        return string.IsNullOrWhiteSpace(leader) ? "M1" : leader;
    }

    static float ResolvePhysicalMoveSpeed(string physicalAgentId)
    {
        SkillBasedActionSystem skill = Object.FindObjectOfType<SkillBasedActionSystem>();
        if (skill != null)
        {
            var profiles = skill.GetAllAgentProfiles();
            if (profiles != null && profiles.TryGetValue(physicalAgentId, out AgentProfile profile) && profile != null)
            {
                float spd = profile.skillLevel > 0f ? profile.skillLevel / 40f : 2.5f;
                return Mathf.Clamp(spd, 1.5f, 4f);
            }
        }

        return 2.5f;
    }

    static void ApplyDesignatedPlayerNavTuning(RagSequenceAgentMover mover)
    {
        if (mover == null)
            return;

        float playerScale = Mathf.Max(mover.transform.lossyScale.x, mover.transform.lossyScale.z);
        if (playerScale < 1.01f)
            playerScale = DesignatedPhysicalPlayerAppearance.GetScale();

        float capsuleRadius = 0.28f * playerScale;
        CapsuleCollider cap = mover.GetComponent<CapsuleCollider>();
        if (cap != null)
            capsuleRadius = cap.radius * Mathf.Max(mover.transform.lossyScale.x, mover.transform.lossyScale.z);

        mover.reachThreshold = Mathf.Max(0.42f, capsuleRadius + 0.12f);
        mover.cognitiveInteractionStandDistance = Mathf.Max(0.38f, 0.36f * playerScale);
        mover.avoidCognitiveObstacles = true;
        mover.useProximityCognitiveSteering = true;
        mover.useProximityEnvironmentSteering = true;
        mover.proximitySteerAggression = 1.55f;
        mover.proximityStationHullPadding = 0.88f;
        mover.proximityApproachBand = 3.45f;
        mover.environmentSteerHullPadding = 1.05f;
        mover.environmentSteerApproachBand = 4.1f;
        mover.obstacleLookAhead = 4.25f;
        mover.obstacleProbeRadius = 0.56f;
        mover.obstacleFanMaxDegrees = 115f;
        mover.obstacleFanMinPickAngleDegrees = 32f;
        mover.obstacleFanWideSweepBonus = 0.032f;
        mover.navStuckTimeoutSeconds = 0.62f;
        mover.navEscapeDurationSeconds = 1.85f;
        mover.navEscapeSpeedMultiplier = 1.22f;
    }

    public static void ResetForDomainReload()
    {
        IsBound = false;
        BoundMover = null;
    }
}
