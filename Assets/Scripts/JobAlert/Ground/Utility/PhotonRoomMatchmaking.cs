using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

/// <summary>
/// TASK:
/// - Photon room matchmaking and lifecycle implementation
/// - Multiplayer room flow development
///
/// This script is responsible for handling room creation,
/// joining existing rooms, and managing fallback logic.
/// Focus was on making the room flow predictable and stable
/// rather than relying on default Photon behavior.
/// </summary>
public class PhotonRoomMatchmaking : MonoBehaviourPunCallbacks
{
    [SerializeField] private string defaultRoomName = "DefaultRoom";
    [SerializeField] private byte maxPlayers = 4;

    private void Start()
    {
        if (PhotonNetwork.IsConnected && PhotonNetwork.InLobby)
        {
            JoinOrCreateRoom();
        }
    }

    public void JoinOrCreateRoom()
    {
        Debug.Log("[Photon] Attempting to join random room...");
        PhotonNetwork.JoinRandomRoom();
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        Debug.LogWarning("[Photon] No available room found. Creating new room.");

        RoomOptions options = new RoomOptions
        {
            MaxPlayers = maxPlayers,
            IsVisible = true,
            IsOpen = true
        };

        PhotonNetwork.CreateRoom(defaultRoomName, options);
    }

    public override void OnCreatedRoom()
    {
        Debug.Log("[Photon] Room created successfully.");
    }

    public override void OnJoinedRoom()
    {
        Debug.Log($"[Photon] Joined room: {PhotonNetwork.CurrentRoom.Name}");
        Debug.Log($"[Photon] Player Count: {PhotonNetwork.CurrentRoom.PlayerCount}");
    }

    public override void OnLeftRoom()
    {
        Debug.Log("[Photon] Left room. Returning to lobby.");
        
    }
}
