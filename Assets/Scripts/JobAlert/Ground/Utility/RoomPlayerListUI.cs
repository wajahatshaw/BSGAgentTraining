using UnityEngine;
using TMPro;
using Photon.Pun;
using Photon.Realtime;

public class RoomPlayerListUI : MonoBehaviourPunCallbacks
{
    [SerializeField] private Transform contentParent;
    [SerializeField] private TMP_Text playerNamePrefab;

    private void Start()
    {
        RefreshPlayerList();
    }

    public override void OnJoinedRoom()
    {
        RefreshPlayerList();
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        RefreshPlayerList();
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        RefreshPlayerList();
    }

    private void RefreshPlayerList()
    {
        foreach (Transform child in contentParent)
            Destroy(child.gameObject);

        foreach (Player player in PhotonNetwork.PlayerList)
        {
            TMP_Text nameText = Instantiate(playerNamePrefab, contentParent);
            nameText.text = player.NickName;
        }
    }
}
