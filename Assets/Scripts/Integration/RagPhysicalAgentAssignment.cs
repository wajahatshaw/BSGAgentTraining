using System;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>
/// Session-wide designation of which player acts as zone 0 physical agent P1 (RAG physical steps).
/// Master Client sets room property <see cref="RoomPropertyKey"/>.
/// Only the Hamid editor test client is designated; all other players keep fixed-joystick movement.
/// </summary>
public static class RagPhysicalAgentAssignment
{
    public const string RoomPropertyKey = "RagPhysicalActorNumber";

    /// <summary>
    /// NetworkManager sets playerName to "Hamid" and appends "Editor" in UNITY_EDITOR → HamidEditor.
    /// Aliases cover underscore variants used in older scenes.
    /// </summary>
    static readonly string[] DesignatedPhysicalPlayerNicks =
    {
        "HamidEditor",
        "Hamid_editor",
        "Hamid_Editor",
    };

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
            && TryParseActorNumber(value, out int actorNumber))
        {
            DesignatedActorNumber = actorNumber;
        }
    }

    public static bool TryParseActorNumber(object value, out int actorNumber)
    {
        actorNumber = -1;
        if (value == null)
            return false;

        switch (value)
        {
            case int i:
                actorNumber = i;
                return true;
            case byte b:
                actorNumber = b;
                return true;
            case short s:
                actorNumber = s;
                return true;
            default:
                try
                {
                    actorNumber = Convert.ToInt32(value);
                    return true;
                }
                catch
                {
                    return false;
                }
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
            && TryParseActorNumber(existing, out int current)
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
        return FindDesignatedPhysicalPlayerActorNumber();
    }

    static int FindDesignatedPhysicalPlayerActorNumber()
    {
        int lowest = int.MaxValue;
        bool found = false;

        foreach (Player player in PhotonNetwork.PlayerList)
        {
            if (player == null || string.IsNullOrEmpty(player.NickName))
                continue;

            if (!IsDesignatedPhysicalPlayerNick(player.NickName))
                continue;

            if (player.ActorNumber < lowest)
            {
                lowest = player.ActorNumber;
                found = true;
            }
        }

        return found ? lowest : -1;
    }

    static bool IsDesignatedPhysicalPlayerNick(string nick)
    {
        if (string.IsNullOrEmpty(nick))
            return false;

        foreach (string designatedNick in DesignatedPhysicalPlayerNicks)
        {
            if (string.Equals(nick, designatedNick, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
