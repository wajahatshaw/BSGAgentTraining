using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

public class PhotonNetworkDiagnostics : MonoBehaviourPunCallbacks
{
    private float pingCheckTimer;
    private const float pingCheckInterval = 3f;

    private void Update()
    {
        if (!PhotonNetwork.IsConnected)
            return;

        pingCheckTimer += Time.deltaTime;

        if (pingCheckTimer >= pingCheckInterval)
        {
            pingCheckTimer = 0f;
            LogNetworkStats();
        }
    }

    private void LogNetworkStats()
    {
        Debug.Log($"[Photon Diagnostics] Ping: {PhotonNetwork.GetPing()} ms");
        Debug.Log($"[Photon Diagnostics] Player Count: {PhotonNetwork.CountOfPlayers}");
        Debug.Log($"[Photon Diagnostics] Room Count: {PhotonNetwork.CountOfRooms}");
    }

    public override void OnRegionListReceived(RegionHandler regionHandler)
    {
        Debug.Log("[Photon Diagnostics] Available regions received.");

        foreach (Region region in regionHandler.EnabledRegions)
        {
            Debug.Log($"[Photon Diagnostics] Region: {region.Code}, Ping: {region.Ping}");
        }
    }

    public override void OnCustomAuthenticationFailed(string debugMessage)
    {
        Debug.LogError($"[Photon Diagnostics] Authentication failed: {debugMessage}");
    }

    public override void OnCustomAuthenticationResponse(System.Collections.Generic.Dictionary<string, object> data)
    {
        Debug.Log("[Photon Diagnostics] Authentication response received.");

        foreach (var item in data)
        {
            Debug.Log($"[Photon Diagnostics] Auth Data: {item.Key} = {item.Value}");
        }
    }
}
