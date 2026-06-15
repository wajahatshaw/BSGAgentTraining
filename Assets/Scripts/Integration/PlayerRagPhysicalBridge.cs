using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Binds the local Photon player (blue worker) as zone 0 physical agent P1.
/// Reuses <see cref="RagSequenceAgentMover"/> for RAG physical steps; movement goes through
/// <see cref="PlayerMovement"/> + <see cref="AgentGroundMotor"/> for BSG collision parity.
/// </summary>
public static class PlayerRagPhysicalBridge
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
                Debug.Log("[PlayerRagPhysicalBridge] Local player is not the designated RAG physical agent — joystick only.");
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

        Debug.LogWarning("[PlayerRagPhysicalBridge] Timed out waiting for local player + RAG runtime.");
    }

    static PlayerMovement FindLocalPlayerMovement()
    {
        foreach (PlayerMovement pm in Object.FindObjectsOfType<PlayerMovement>())
        {
            if (pm == null) continue;
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
        HandRotationManager.EnsureOnAgent(playerGo)?.RefreshRigWire();

        RagSequenceAgentMover existingMover = playerGo.GetComponent<RagSequenceAgentMover>();
        if (existingMover != null)
        {
            existingMover.hostPlayerMovement = true;
            KleinFrameExecutor kleinExisting = KleinFrameExecutor.EnsureOnAgent(playerGo, ZoneIndex);
            string ragTextExisting = ResolveActiveRagJsonText();
            if (kleinExisting != null)
            {
                kleinExisting.BootstrapFromRagText(ragTextExisting);
                existingMover.BindKleinFrameExecutor(kleinExisting, ragTextExisting);
            }

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

        KleinFrameExecutor kleinExec = KleinFrameExecutor.EnsureOnAgent(playerGo, ZoneIndex);
        string ragText = ResolveActiveRagJsonText();
        if (kleinExec != null)
        {
            kleinExec.BootstrapFromRagText(ragText);
            mover.BindKleinFrameExecutor(kleinExec, ragText);
        }

        IsBound = true;
        BoundMover = mover;
        mover.RefreshHostPlayerGroundMotor();
        RagPhysicalAgentLocalMode.ApplyForLocalClient(playerMovement);
        Debug.Log($"[PlayerRagPhysicalBridge] Local Photon player bound as {physicalAgentId} (zone {ZoneIndex}) — RAG drives movement.");
        Debug.Log("[PlayerRagPhysicalBridge] You are the physical agent for this session.");
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

        TryAttachMlTrainingForDesignatedPlayer(mover);
        if (mover != null)
        {
            DesignatedPhysicalPlayerAppearance.ApplyScale(mover.transform);
            HandRotationManager.EnsureOnAgent(mover.gameObject)?.RefreshRigWire();
        }
        TryBootstrapKleinMotorResting(mover);
    }

    static void TryBootstrapKleinMotorResting(RagSequenceAgentMover mover)
    {
        if (mover == null || !RagPhysicalAgentAssignment.IsLocalPlayerRagPhysicalAgent())
            return;

        KleinFrameExecutor exec = mover.GetComponent<KleinFrameExecutor>();
        if (exec == null)
            return;

        exec.TryExecuteRestingFrame();
    }

    static string ResolveActiveRagJsonText()
    {
        SceneUILoader loader = Object.FindObjectOfType<SceneUILoader>();
        if (loader == null)
            return string.Empty;

        if (!string.IsNullOrEmpty(loader.MergedRawRagJson))
            return loader.MergedRawRagJson;
        if (!string.IsNullOrEmpty(loader.RawJsonText))
            return loader.RawJsonText;
        if (!string.IsNullOrEmpty(loader.EffectivePipelineJson))
            return loader.EffectivePipelineJson;
        return string.Empty;
    }

    static bool IsPhysicalEmbedContentReady()
    {
        MultiplayerRagZone0Anchor anchor = MultiplayerRagZone0Anchor.Instance;
        if (anchor == null)
            return false;

        foreach (DeclarativeObjectMetadata meta in UnityEngine.Object.FindObjectsOfType<DeclarativeObjectMetadata>())
        {
            if (meta == null || meta.gameObject.name.IndexOf("cognitive_", System.StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            if (meta.gameObject.name.StartsWith("Tool_", System.StringComparison.OrdinalIgnoreCase)
                || meta.GetComponent<EnvironmentSolidCollider>() != null)
                return true;
        }

        return UnityEngine.Object.FindObjectOfType<CognitiveStationInteractable>() != null;
    }

    /// <summary>
    /// SceneGenerator skips P1 in multiplayer — attach PhysicalAgentZone0 after the Photon player binds.
    /// </summary>
    static void TryAttachMlTrainingForDesignatedPlayer(RagSequenceAgentMover mover)
    {
        if (mover == null || !RagPhysicalAgentAssignment.IsLocalPlayerRagPhysicalAgent())
            return;

        if (!RagRuntimeMLBootstrap.TryBootstrapPhotonPlayerPhysical(mover))
            return;

        MultiplayerRagZone0Anchor anchor = MultiplayerRagZone0Anchor.Instance;
        anchor?.ApplyPlayAreaToZoneAgents();
        Debug.Log("[PlayerRagPhysicalBridge] PhysicalAgentZone0 ML attached to designated Photon player (P1, zone 0).");
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

        Debug.Log($"[PlayerRagPhysicalBridge] Designated physical agent moved to spawn near physical targets at {target}.");
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

        mover.reachThreshold = Mathf.Max(1.35f, 0.92f * playerScale);
        mover.cognitiveInteractionStandDistance = Mathf.Max(0.92f, 0.58f * playerScale);
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
