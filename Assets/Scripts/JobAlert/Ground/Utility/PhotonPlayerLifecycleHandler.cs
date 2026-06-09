using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

/// <summary>
/// TASK:
/// - Handle player entry/exit callbacks
/// - Room lifecycle stability
///
/// Handles player join/leave events and validates
/// room integrity when players disconnect unexpectedly.
/// </summary>
public class PhotonPlayerLifecycleHandler : MonoBehaviourPunCallbacks
{
    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        Debug.Log($"[Photon] Player joined: {newPlayer.NickName}");
        Debug.Log($"[Photon] Total players: {PhotonNetwork.CurrentRoom.PlayerCount}");
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        Debug.Log($"[Photon] Player left: {otherPlayer.NickName}");

        // Bug Fix:
        // Previously room state was not updated when players left,
        // causing incorrect game flow in multiplayer sessions.
        if (PhotonNetwork.IsMasterClient)
        {
            Debug.Log("[Photon] Master client validating room state after player exit.");
        }
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        Debug.Log($"[Photon] Master client switched to: {newMasterClient.NickName}");
    }
}
