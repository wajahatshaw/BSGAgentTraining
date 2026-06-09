using System;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Jy_Util;

/// <summary>
/// Session-wide designation of which player acts as zone 0 physical agent P1 (RAG physical steps).
/// Master Client sets room property <see cref="RoomPropertyKey"/>.
/// Priority: editor test nick (*Editor, e.g. HamidEditor) → lowest Worker → solo room fallback.
/// </summary>
public static class RagPhysicalAgentAssignment
{
    public const string RoomPropertyKey = "RagPhysicalActorNumber";

    /// <summary>NetworkManager appends this in UNITY_EDITOR (Hamid → HamidEditor).</summary>
    public const string EditorNickSuffix = "Editor";

    public static int DesignatedActorNumber { get; private set; } = -1;

    public static void EnsureAssignedInRoom()
    {
        if (!PhotonNetwork.InRoom || !BsgIntegrationSettings.UsePhotonPlayerAsPhysicalAgent)
            return;

        RefreshFromRoom();

        if (PhotonNetwork.IsMasterClient)
            TryPublishDesignatedPlayer();
    }

    public static bool WaitForDesignationReady()
    {
        if (!PhotonNetwork.InRoom || !BsgIntegrationSettings.UsePhotonPlayerAsPhysicalAgent)
            return false;

        if (DesignatedActorNumber >= 0)
            return true;

        RefreshFromRoom();
        return DesignatedActorNumber >= 0;
    }

    public static void RefreshFromRoom()
    {
        DesignatedActorNumber = -1;
        if (!PhotonNetwork.InRoom)
            return;

        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(RoomPropertyKey, out object value)
            && value is int actorNumber)
        {
            DesignatedActorNumber = actorNumber;
        }
    }

    public static bool IsLocalPlayerRagPhysicalAgent()
    {
        if (!BsgIntegrationSettings.UsePhotonPlayerAsPhysicalAgent || !PhotonNetwork.InRoom)
            return false;

        if (DesignatedActorNumber < 0)
            RefreshFromRoom();

        if (DesignatedActorNumber < 0)
            return false;

        return PhotonNetwork.LocalPlayer.ActorNumber == DesignatedActorNumber;
    }

    public static bool IsActorRagPhysicalAgent(int actorNumber)
    {
        if (DesignatedActorNumber < 0)
            RefreshFromRoom();
        return DesignatedActorNumber >= 0 && actorNumber == DesignatedActorNumber;
    }

    static void TryPublishDesignatedPlayer()
    {
        int designated = ResolveDesignatedActorNumber();
        if (designated < 0)
            return;

        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(RoomPropertyKey, out object existing)
            && existing is int current
            && current == designated)
        {
            DesignatedActorNumber = designated;
            return;
        }

        Hashtable props = new Hashtable { { RoomPropertyKey, designated } };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
        DesignatedActorNumber = designated;

        Player designatedPlayer = PhotonNetwork.CurrentRoom.GetPlayer(designated);
        string nick = designatedPlayer != null ? designatedPlayer.NickName : "?";
        Debug.Log($"[RagPhysicalAgentAssignment] RAG physical agent (P1) = ActorNumber {designated} ({nick})");
    }

    public static int ResolveDesignatedActorNumber()
    {
        int editorPlayer = FindPlayerByNickSuffix(EditorNickSuffix);
        if (editorPlayer >= 0)
            return editorPlayer;

        int lowestWorker = FindLowestWorkerActorNumber();
        if (lowestWorker >= 0)
            return lowestWorker;

        if (PhotonNetwork.PlayerList.Length == 1 && PhotonNetwork.PlayerList[0] != null)
            return PhotonNetwork.PlayerList[0].ActorNumber;

        return -1;
    }

    static int FindPlayerByNickSuffix(string suffix)
    {
        if (string.IsNullOrEmpty(suffix))
            return -1;

        int lowest = int.MaxValue;
        bool found = false;

        foreach (Player player in PhotonNetwork.PlayerList)
        {
            if (player == null || string.IsNullOrEmpty(player.NickName))
                continue;
            if (!player.NickName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;

            if (player.ActorNumber < lowest)
            {
                lowest = player.ActorNumber;
                found = true;
            }
        }

        return found ? lowest : -1;
    }

    static int FindLowestWorkerActorNumber()
    {
        int lowest = int.MaxValue;
        bool found = false;

        foreach (Player player in PhotonNetwork.PlayerList)
        {
            if (player == null || player.IsMasterClient)
                continue;

            if (player.CustomProperties.TryGetValue(Jy_Utility._Designation, out object designation)
                && designation != null
                && designation.ToString() == PlayerRole.Supervisor.ToString())
            {
                continue;
            }

            if (player.ActorNumber < lowest)
            {
                lowest = player.ActorNumber;
                found = true;
            }
        }

        return found ? lowest : -1;
    }
}
