using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

/// <summary>
/// TASK:
/// - Room state synchronization
/// - Multiplayer stability validation
///
/// Ensures that room-level state is synchronized correctly
/// across all players and prevents desync issues when players
/// join late or reconnect.
/// </summary>
public class PhotonRoomStateSync : MonoBehaviourPunCallbacks
{
    private const string GAME_STATE_KEY = "GameState";

    public override void OnJoinedRoom()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            Debug.Log("[Photon] Master client setting initial room state.");
            SetRoomState("WaitingForPlayers");
        }
    }

    private void SetRoomState(string state)
    {
        ExitGames.Client.Photon.Hashtable props =
            new ExitGames.Client.Photon.Hashtable
            {
                { GAME_STATE_KEY, state }
            };

        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    public override void OnRoomPropertiesUpdate(
        ExitGames.Client.Photon.Hashtable propertiesThatChanged)
    {
        if (propertiesThatChanged.ContainsKey(GAME_STATE_KEY))
        {
            Debug.Log($"[Photon] Room state updated: {propertiesThatChanged[GAME_STATE_KEY]}");
        }
    }
}
