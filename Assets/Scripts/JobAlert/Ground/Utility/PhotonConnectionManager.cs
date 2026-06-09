using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using System.Collections;

public class PhotonConnectionManager : MonoBehaviourPunCallbacks
{
    [Header("Photon Config")]
    [SerializeField] private string gameVersion = "1.0";
    [SerializeField] private int maxRetryAttempts = 3;

    private int currentRetryCount = 0;
    private bool isConnecting;

    private void Awake()
    {
        PhotonNetwork.AutomaticallySyncScene = true;
        PhotonNetwork.GameVersion = gameVersion;
    }

    private void Start()
    {
        InitializePhotonConnection();
    }

    private void InitializePhotonConnection()
    {
        if (PhotonNetwork.IsConnected)
        {
            Debug.Log("[Photon] Already connected to Photon Cloud.");
            return;
        }

        Debug.Log("[Photon] Initializing connection to Photon Cloud...");
        isConnecting = true;

        PhotonNetwork.ConnectUsingSettings();
    }

    public override void OnConnectedToMaster()
    {
        Debug.Log("[Photon] Successfully connected to Master Server.");
        Debug.Log($"[Photon] Region: {PhotonNetwork.CloudRegion}");

        isConnecting = false;
        currentRetryCount = 0;

        JoinLobby();
    }

    private void JoinLobby()
    {
        if (PhotonNetwork.InLobby || PhotonNetwork.NetworkClientState == ClientState.JoiningLobby)
            return;

        Debug.Log("[Photon] Attempting to join default lobby...");
        PhotonNetwork.JoinLobby();
    }

    public override void OnJoinedLobby()
    {
        Debug.Log("[Photon] Joined lobby successfully.");
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogWarning($"[Photon] Disconnected from Photon Cloud. Cause: {cause}");

        if (currentRetryCount < maxRetryAttempts)
        {
            currentRetryCount++;
            Debug.Log($"[Photon] Retry attempt {currentRetryCount}/{maxRetryAttempts}");
            StartCoroutine(RetryConnection());
        }
        else
        {
            Debug.LogError("[Photon] Max retry attempts reached. Connection failed.");
        }
    }

    private IEnumerator RetryConnection()
    {
        yield return new WaitForSeconds(2f);

        Debug.Log("[Photon] Retrying connection...");
        PhotonNetwork.ConnectUsingSettings();
    }
}
