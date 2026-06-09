using Photon.Pun;
using UnityEngine;

/// <summary>
/// Scale and body metrics for the designated zone 0 physical Photon player (blue worker).
/// Applied on every client so remote viewers see the same larger avatar.
/// </summary>
public static class DesignatedPhysicalPlayerAppearance
{
    public const float DefaultBodyHeight = 1.75f;

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
