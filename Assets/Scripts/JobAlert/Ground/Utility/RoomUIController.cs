using UnityEngine;
using TMPro;
using Photon.Pun;

public class RoomUIController : MonoBehaviourPunCallbacks
{
    [SerializeField] private TMP_Text playerCountText;

    private void Start()
    {
        UpdatePlayerCount();
    }

    public override void OnJoinedRoom()
    {
        UpdatePlayerCount();
    }

    public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
    {
        UpdatePlayerCount();
    }

    public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
    {
        UpdatePlayerCount();
    }

    private void UpdatePlayerCount()
    {
        if (PhotonNetwork.CurrentRoom == null) return;

        playerCountText.text =
            $"{PhotonNetwork.CurrentRoom.PlayerCount} / {PhotonNetwork.CurrentRoom.MaxPlayers} Players";
    }
}
