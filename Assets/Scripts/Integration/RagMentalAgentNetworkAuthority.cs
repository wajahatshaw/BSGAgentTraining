using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Master Client owns M1 cognitive simulation; other clients observe via PhotonTransformView.
/// </summary>
public static class RagMentalAgentNetworkAuthority
{
    public static RagSequenceAgentMover MasterMentalMover { get; private set; }

    public static void ApplyAfterZoneSpawn(int zoneIndex = 0)
    {
        if (!BsgIntegrationSettings.UsePhotonPlayerAsPhysicalAgent || !PhotonNetwork.InRoom)
            return;

        RagSequenceAgentMover mentalMover = FindZoneMentalMover(zoneIndex);
        if (mentalMover == null)
            return;

        MasterMentalMover = mentalMover;

        if (PhotonNetwork.IsMasterClient)
            EnableMasterSimulation(mentalMover);
        else
            DisableLocalSimulation(mentalMover);
    }

    public static void DisableLocalSimulation(RagSequenceAgentMover mentalMover)
    {
        if (mentalMover == null)
            return;

        mentalMover.enabled = false;

        Rigidbody rb = mentalMover.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        BSGMLAgent mlAgent = mentalMover.GetComponent<BSGMLAgent>();
        if (mlAgent != null)
            mlAgent.enabled = false;
    }

    static void EnableMasterSimulation(RagSequenceAgentMover mentalMover)
    {
        GameObject go = mentalMover.gameObject;
        mentalMover.enabled = true;

        PhotonView pv = go.GetComponent<PhotonView>();
        if (pv == null)
            pv = go.AddComponent<PhotonView>();

        if (pv.ViewID == 0)
            pv.ViewID = PhotonNetwork.AllocateViewID(false);

        PhotonTransformView ptv = go.GetComponent<PhotonTransformView>();
        if (ptv == null)
            ptv = go.AddComponent<PhotonTransformView>();

        var observed = new List<Component>();
        if (ptv != null)
            observed.Add(ptv);
        pv.ObservedComponents = observed;

        Rigidbody rb = go.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        AgentGroundMotor motor = go.GetComponent<AgentGroundMotor>();
        if (motor == null)
            motor = go.AddComponent<AgentGroundMotor>();
        motor.clampZoneIndex = mentalMover.zoneIndex;
        motor.SnapFeetToGround();

        mentalMover.RecoverOrchestratorDispatchIfNeeded();
    }

    static RagSequenceAgentMover FindZoneMentalMover(int zoneIndex)
    {
        foreach (RagSequenceAgentMover mover in Object.FindObjectsOfType<RagSequenceAgentMover>())
        {
            if (mover != null && mover.isMentalAgent && mover.zoneIndex == zoneIndex)
                return mover;
        }

        return null;
    }
}
