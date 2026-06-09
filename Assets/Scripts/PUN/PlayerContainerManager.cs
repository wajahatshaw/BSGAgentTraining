using System.Collections;
using System.Collections.Generic;
using Jy_Util;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerContainerManager : MonoBehaviourPunCallbacks
{
    [SerializeField] private Transform containerParent;
    [SerializeField] private LobbyPlayerContainer playerContainerPrefab;
    List<LobbyPlayerContainer> lobbyPlayerContainers = new List<LobbyPlayerContainer>();
    [SerializeField] TMP_Text numberOfPlayerJoined;
    [SerializeField]GameEvent Event_loadNextLevel;

    [Header("Ready System")]
    [SerializeField] private float startDelay = 5f;
    [SerializeField] private TMP_Text simulationStartTimerText;
    [SerializeField] private Button readyOrStartButton;
    [SerializeField] private TMP_Text readyButtonText;



    public override void OnJoinedRoom()
    {
        SpawnExistingPlayers();
        UpdatePlayerCount();
        UpdateReadyUI();
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        SpawnPlayerContainer(newPlayer);
        UpdatePlayerCount();
        UpdateReadyUI();
    }
    
    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        UpdatePlayerCount();
    }

    private void SpawnExistingPlayers()
    {
        foreach (Player player in PhotonNetwork.PlayerList)
        {
            SpawnPlayerContainer(player);
        }
    }

    private void SpawnPlayerContainer(Player player)
    {
        LobbyPlayerContainer container = Instantiate(playerContainerPrefab, containerParent);
        container.Setup(player);
        lobbyPlayerContainers.Add(container);
    }

    public override void OnPlayerPropertiesUpdate(Player targetPlayer,ExitGames.Client.Photon.Hashtable changedProps)
    {
        
        foreach(LobbyPlayerContainer item in lobbyPlayerContainers)
        {
            if(targetPlayer == item.myPlayer)
            {
                item.RefreshProp(changedProps);
            }
        }
        
        
        if (changedProps.ContainsKey(Jy_Utility._Ready))
        {
            UpdateReadyUI();
        }
        
    }

    private void UpdatePlayerCount()
    {
        if (numberOfPlayerJoined == null || PhotonNetwork.CurrentRoom == null)
            return;

        int currentPlayers = PhotonNetwork.CurrentRoom.PlayerCount;
        int max = PhotonNetwork.CurrentRoom.MaxPlayers > 0
            ? PhotonNetwork.CurrentRoom.MaxPlayers
            : 8;

        numberOfPlayerJoined.text = $" {currentPlayers} / {max} Players joined";
    }

    #region Ready Start
    public void OnReadyButtonPressed()
    {
        if (PhotonNetwork.IsMasterClient)
            return;

        bool isReady = false;

        if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue(Jy_Utility._Ready, out object value))
            isReady = (bool)value;

        ExitGames.Client.Photon.Hashtable props =
            new ExitGames.Client.Photon.Hashtable
            {
                { Jy_Utility._Ready, !isReady }
            };

        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    }





    public void OnStartButtonPressed()
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        readyOrStartButton.interactable = false;

        photonView.RPC(
            nameof(RpcLockReadyButtons),
            RpcTarget.Others
        );

        StartCoroutine(StartCountdown());
    }

    [PunRPC]
    void RpcLockReadyButtons()
    {
        readyOrStartButton.interactable = false;
        StartCoroutine(StartCountdown());
    }

    private IEnumerator StartCountdown()
    {
        float timeLeft = startDelay;

        simulationStartTimerText.gameObject.SetActive(true);

        while (timeLeft > 0f)
        {
            simulationStartTimerText.text =
                $"Simulation starts in {Mathf.CeilToInt(timeLeft)}";


            yield return new WaitForSeconds(1f);
            timeLeft--;
        }

        simulationStartTimerText.text = "Starting...";


        // ⛔ DO NOT start simulation here
        // 👉 You will hook this later
        Debug.Log("is master: "+PhotonNetwork.IsMasterClient);
        if(PhotonNetwork.IsMasterClient) Event_loadNextLevel.Raise(this,true);
        
    }

    
    private void UpdateReadyUI()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            bool allReady = AreAllPlayersReady();
            Debug.Log("<color=yellow>all ready:" +allReady+"</color>");
            readyButtonText.text = "Start";
            readyOrStartButton.interactable = allReady;
        }
        else
        {
            bool isReady = false;

            if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue(Jy_Utility._Ready, out object value))
                isReady = (bool)value;

            readyButtonText.text = isReady ? "Not Ready" : "Ready";
        }
    }

    private bool AreAllPlayersReady()
    {
        foreach (var player in PhotonNetwork.PlayerList)
        {
           // Debug.Log("players ready status for player:"+player.NickName);
            if(player.IsMasterClient) continue;
            
            if (!player.CustomProperties.TryGetValue("Ready", out object value))
            return false;

            if (!(bool)value)
                return false;
        }
        return true;
    }


    #endregion

    
}
