using UnityEngine;
using Photon.Pun;

public class RoomUIScreenBinder : MonoBehaviourPunCallbacks
{
    [SerializeField] private GameObject roomUIScreen;

    public override void OnJoinedRoom()
    {
        roomUIScreen.SetActive(true);
    }

    public override void OnLeftRoom()
    {
        roomUIScreen.SetActive(false);
    }
}
