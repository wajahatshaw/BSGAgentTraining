using UnityEngine;
using TMPro;
using Photon.Pun;

public class PhotonPingDebugger : MonoBehaviour
{
    [SerializeField] private TMP_Text pingText;
    [SerializeField] private float updateInterval = 1f;

    private float timer;

    private void Update()
    {
        if (!PhotonNetwork.IsConnected || pingText == null)
            return;

        timer += Time.deltaTime;

        if (timer >= updateInterval)
        {
            timer = 0f;
            UpdatePingUI();
        }
    }

    private void UpdatePingUI()
    {
        string region = PhotonNetwork.CloudRegion;
        int ping = PhotonNetwork.GetPing();

        pingText.text = $"Server: {region}\nPing: {ping} ms";
    }
}
