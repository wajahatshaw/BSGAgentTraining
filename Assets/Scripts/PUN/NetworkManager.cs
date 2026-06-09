using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using Jy_Util;
using System.Collections.Generic;


public class NetworkManager : MonoBehaviourPunCallbacks
{
    [Header("Photon")]
    [SerializeField] private string gameVersion = "1.0";


    [Header("Room Settings")]
    [SerializeField] private bool createRoom = true;
    [SerializeField] private string playerName = "Player";
    [SerializeField] private string roomSuffix = "_SimulationRoom";
    [SerializeField] private byte maxPlayers = 8;
    [SerializeField] private string nextLevelName;

    [Header("UI")]
    [SerializeField] private GameObject startButton;

    private string roomName;

    private void Start()
    {
        if (startButton != null)
            startButton.SetActive(false);

        #if UNITY_EDITOR
        playerName = playerName + "Editor";
        #endif
        
        PhotonNetwork.GameVersion = gameVersion;
        PhotonNetwork.NickName = playerName;

        PhotonNetwork.ConnectUsingSettings();
        
    }

    public override void OnConnectedToMaster()
    {
        Debug.Log("Connected to Photon Master Server");

        if (!PhotonNetwork.InLobby && PhotonNetwork.NetworkClientState != ClientState.JoiningLobby)
            PhotonNetwork.JoinLobby();
    }

    public override void OnJoinedLobby()
    {
        Debug.Log("Joined Lobby");

        RoomOptions roomOptions = new RoomOptions
        {
            MaxPlayers = maxPlayers
        };

        if (createRoom)
        {
            roomName = roomSuffix;
            PhotonNetwork.CreateRoom(roomName, roomOptions);
        }
        else
        {
            // Ensure roomName is valid before joining
            if (string.IsNullOrEmpty(roomName))
            {
                roomName = roomSuffix;
            } 

            PhotonNetwork.JoinOrCreateRoom(
                roomName,
                roomOptions,
                TypedLobby.Default
            );
        }
    }


    public override void OnCreatedRoom()
    {
        //Debug.Log("Room Created: " + PhotonNetwork.CurrentRoom.Name);
    }

    public override void OnJoinedRoom()
    {
        //Debug.Log("Joined Room: " + PhotonNetwork.CurrentRoom.Name);
        SetPlayerDesignation();
        SetPlayerColor();
        RagPhysicalAgentAssignment.EnsureAssignedInRoom();
        // Master client gets start button
        if (PhotonNetwork.IsMasterClient && startButton != null)
        {
            startButton.SetActive(true);
        }
        PhotonNetwork.AutomaticallySyncScene = true;

    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        Debug.LogError("Room creation failed: " + message);
    }

    void SetPlayerDesignation()
    {
        Hashtable props = new Hashtable();

        props[Jy_Utility._Designation] = PhotonNetwork.IsMasterClient
            ? "Supervisor"
            : "Worker";

        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    }

    void SetPlayerColor()
    {
        HashSet<int> usedColors = new HashSet<int>();

        foreach (Player player in PhotonNetwork.PlayerList)
        {
            if (player.CustomProperties.TryGetValue(Jy_Utility._PlayerColor, out object colorIndex))
            {
                usedColors.Add((int)colorIndex);
            }
        }

        int assignedColor = 0;
        while (usedColors.Contains(assignedColor))
        {
            assignedColor++;
        }

        Hashtable props = new Hashtable();
        props[Jy_Utility._PlayerColor] = assignedColor;
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    }

    public void ExitApp()
    {
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif

    }

    public void ListenToLoadMainGameScene(Component sender,object data)
    {
        PhotonNetwork.LoadLevel(nextLevelName);
    }


    
}
