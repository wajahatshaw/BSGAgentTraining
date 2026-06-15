using Photon.Pun;
using UnityEngine;

/// <summary>
/// Scale and body metrics for the designated zone 0 physical Photon player (blue worker).
/// Applied on every client so remote viewers see the same larger avatar.
/// </summary>
public static class DesignatedPhysicalPlayerAppearance
{
    public const float DefaultBodyHeight = 1.75f;

    const string PlayerCapsuleName = "PlayerCapsule";
    const string YBotVisualName = "Y Bot";

    public static float GetScale()
    {
        MultiplayerRagZone0Anchor anchor = MultiplayerRagZone0Anchor.Instance;
        return anchor != null ? anchor.designatedPhysicalPlayerScale : 1.55f;
    }

    public static void ApplyScale(Transform playerRoot)
    {
        if (playerRoot == null)
            return;

        float scale = GetScale();
        Vector3 desired = Vector3.one * scale;
        if ((playerRoot.localScale - desired).sqrMagnitude > 0.0004f)
            playerRoot.localScale = desired;

        ApplyYBotVisual(playerRoot);
    }

    /// <summary>Default multiplayer worker look — procedural half-body, no Y Bot, unit scale.</summary>
    public static void ApplyDefaultWorkerVisual(Transform playerRoot)
    {
        if (playerRoot == null)
            return;

        if ((playerRoot.localScale - Vector3.one).sqrMagnitude > 0.0004f)
            playerRoot.localScale = Vector3.one;

        Transform capsule = playerRoot.Find(PlayerCapsuleName);
        if (capsule == null)
            return;

        for (int i = 0; i < capsule.childCount; i++)
        {
            Transform child = capsule.GetChild(i);
            if (child == null)
                continue;

            string childName = child.name;
            if (childName == YBotVisualName)
            {
                child.gameObject.SetActive(false);
                continue;
            }

            if (IsLegacyProceduralVisual(childName))
                child.gameObject.SetActive(true);
        }

        // PlayerCapsule mesh is collision-only; prefab ships with MeshRenderer disabled.
        MeshRenderer capsuleRenderer = capsule.GetComponent<MeshRenderer>();
        if (capsuleRenderer != null)
            capsuleRenderer.enabled = false;
    }

    /// <summary>
    /// Keeps exactly one designated avatar on Y Bot (all clients) and restores worker visuals for everyone else.
    /// </summary>
    public static void SyncAllPhysicalPlayerAppearances()
    {
        if (!BsgIntegrationSettings.UsePhotonPlayerAsPhysicalAgent)
            return;

        RagPhysicalAgentAssignment.EnsureAssignedInRoom();
        RagPhysicalAgentAssignment.RefreshFromRoom();
        int designatedActor = RagPhysicalAgentAssignment.DesignatedActorNumber;
        if (designatedActor < 0)
            return;

        foreach (PlayerMovement pm in Object.FindObjectsOfType<PlayerMovement>())
        {
            if (pm == null)
                continue;

            PhotonView pv = pm.GetComponent<PhotonView>();
            if (pv == null || pv.Owner == null)
                continue;

            if (!IsPhysicalPlayerTransform(pm.transform))
                continue;

            if (pv.Owner.ActorNumber == designatedActor)
                ApplyScale(pm.transform);
            else
                ApplyDefaultWorkerVisual(pm.transform);
        }
    }

    /// <summary>
    /// Swaps the designated player from procedural HumanBodyBuilder parts to the Y Bot rig
    /// already nested under PlayerCapsule in Resources/Player.prefab (Mixamo humanoid).
    /// Visual-only — does not touch movement, RAG, or cognitive components.
    /// </summary>
    public static void ApplyYBotVisual(Transform playerRoot)
    {
        if (playerRoot == null)
            return;

        Transform capsule = playerRoot.Find(PlayerCapsuleName);
        if (capsule == null)
            return;

        Transform yBot = null;
        for (int i = 0; i < capsule.childCount; i++)
        {
            Transform child = capsule.GetChild(i);
            if (child == null)
                continue;

            string childName = child.name;
            if (childName == YBotVisualName)
            {
                yBot = child;
                continue;
            }

            if (IsLegacyProceduralVisual(childName))
                child.gameObject.SetActive(false);
        }

        if (yBot != null)
            yBot.gameObject.SetActive(true);

        MeshRenderer capsuleRenderer = capsule.GetComponent<MeshRenderer>();
        if (capsuleRenderer != null)
            capsuleRenderer.enabled = false;

        HandRotationManager.EnsureOnAgent(playerRoot.gameObject);
        HandRotationManager handMgr = playerRoot.GetComponent<HandRotationManager>();
        if (handMgr != null && !handMgr.ManualPoseActive)
            handMgr.RefreshRigWire();
    }

    static bool IsLegacyProceduralVisual(string childName)
    {
        return childName == "Head"
            || childName == "Body"
            || childName.StartsWith("handmesh", System.StringComparison.OrdinalIgnoreCase);
    }

    public static float ResolveBodyHeight(Transform target)
    {
        if (target == null)
            return DefaultBodyHeight * GetScale();

        CapsuleCollider cap = target.GetComponent<CapsuleCollider>();
        if (cap == null)
            cap = target.GetComponentInChildren<CapsuleCollider>();

        float scaleY = Mathf.Max(target.lossyScale.y, 0.01f);
        if (cap != null)
            return cap.height * scaleY;

        return DefaultBodyHeight * scaleY;
    }

    public static Transform TryFindDesignatedPlayerTransform()
    {
        if (!BsgIntegrationSettings.UsePhotonPlayerAsPhysicalAgent)
            return null;

        RagPhysicalAgentAssignment.EnsureAssignedInRoom();
        RagPhysicalAgentAssignment.RefreshFromRoom();
        int actor = RagPhysicalAgentAssignment.DesignatedActorNumber;
        if (actor < 0)
            return null;

        if (PlayerRagPhysicalBridge.IsBound && PlayerRagPhysicalBridge.BoundMover != null)
        {
            Transform bound = PlayerRagPhysicalBridge.BoundMover.transform;
            if (IsPhysicalPlayerTransform(bound))
                return bound;
        }

        foreach (PlayerMovement pm in Object.FindObjectsOfType<PlayerMovement>())
        {
            if (pm == null)
                continue;

            PhotonView pv = pm.GetComponent<PhotonView>();
            if (pv == null || pv.Owner == null)
                continue;

            if (actor >= 0 && pv.Owner.ActorNumber != actor)
                continue;

            if (IsPhysicalPlayerTransform(pm.transform))
                return pm.transform;
        }

        return null;
    }

    static bool IsPhysicalPlayerTransform(Transform t)
    {
        if (t == null)
            return false;

        if (t.name.StartsWith("Agent_M", System.StringComparison.OrdinalIgnoreCase))
            return false;

        RagSequenceAgentMover mover = t.GetComponent<RagSequenceAgentMover>();
        if (mover != null && mover.isMentalAgent)
            return false;

        return t.GetComponent<PlayerMovement>() != null;
    }
}
