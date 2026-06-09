using UnityEngine;
using Photon.Pun;

public class ForceMachine : MonoBehaviourPun
{
    [Header("Target")]
    [SerializeField] private Rigidbody targetRb;

    [Header("Force Settings")]
    [SerializeField] private float forceAmount = 10f;
    [SerializeField] private Vector3 forceDirection = Vector3.forward;

    private bool hasFired;

    // Called by interaction system
    [NaughtyAttributes.Button]
    public void ActivateMachine()
    {
        // if (hasFired && targetRb.linearVelocity.magnitude > 0.1f)
        // {
             
        //     return;
        // }

        // Request force application
        photonView.RPC(
            nameof(RPC_RequestApplyForce),
            RpcTarget.MasterClient
        );
    }

    // ============================
    // MASTER AUTHORITY
    // ============================

    [PunRPC]
    void RPC_RequestApplyForce()
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        // if (hasFired)
        //     return;

        hasFired = true;

        photonView.RPC(
            nameof(RPC_ApplyForce),
            RpcTarget.All
        );
    }

    // ============================
    // APPLY FORCE (SYNCED)
    // ============================

    [PunRPC]
    void RPC_ApplyForce()
    {
        if (targetRb == null)
            return;

        targetRb.isKinematic = false;
        targetRb.AddForce(
            forceDirection.normalized * forceAmount,
            ForceMode.Impulse
        );
    }
}
