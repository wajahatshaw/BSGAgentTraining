using System.Collections;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using Jy_Util;

public class PlayerSpawner : MonoBehaviourPun
{
    [Header("Player Prefab")]
    [SerializeField] private GameObject playerPrefab;
    

    [Header("Spawn Points")]
    [SerializeField] private Transform supervisorSpawnPoint;
    [SerializeField] private Transform[] workerSpawnPoints;

    private void Start()
    { 
        StartCoroutine(SpawnLocalPlayerWhenReady());
    }

    private IEnumerator SpawnLocalPlayerWhenReady()
    {
        PlayerRole role = GetLocalPlayerRole();
        Transform spawnPoint = null;
        bool wantsPhysicalSpawn = BsgIntegrationSettings.UsePhotonPlayerAsPhysicalAgent;

        for (int i = 0; i < 80; i++)
        {
            spawnPoint = GetSpawnPoint(role);
            if (spawnPoint == null)
            {
                yield return null;
                continue;
            }

            if (wantsPhysicalSpawn && !CanResolveDesignatedPhysicalSpawn())
            {
                yield return null;
                continue;
            }

            break;
        }

        if (spawnPoint == null)
        {
            Debug.LogError("Spawn point not found for role: " + role);
            yield break;
        }

        Vector3 spawnPos = spawnPoint.position;
        Quaternion spawnRot = spawnPoint.rotation;

        if (BsgIntegrationSettings.UsePhotonPlayerAsPhysicalAgent && ShouldSpawnAsDesignatedPhysicalAgent())
        {
            MultiplayerRagZone0Anchor anchor = MultiplayerRagZone0Anchor.Instance;
            if (anchor == null)
                anchor = FindObjectOfType<MultiplayerRagZone0Anchor>();

            if (anchor != null && anchor.TryGetDesignatedPhysicalAgentSpawnWorld(out Vector3 physicalSpawn, spawnPos.y))
            {
                spawnPos = physicalSpawn;
                spawnRot = anchor.GetDesignatedPhysicalAgentSpawnWorldRotation();
            }
        }

        PhotonNetwork.Instantiate(
            playerPrefab.name,
            spawnPos,
            spawnRot
        );
    }

    static bool CanResolveDesignatedPhysicalSpawn()
    {
        if (!ShouldSpawnAsDesignatedPhysicalAgent())
            return true;

        MultiplayerRagZone0Anchor anchor = MultiplayerRagZone0Anchor.Instance;
        if (anchor == null)
            anchor = Object.FindObjectOfType<MultiplayerRagZone0Anchor>();

        return anchor != null;
    }

    static bool ShouldSpawnAsDesignatedPhysicalAgent()
    {
        if (!PhotonNetwork.InRoom)
            return false;

        RagPhysicalAgentAssignment.EnsureAssignedInRoom();
        if (RagPhysicalAgentAssignment.WaitForDesignationReady())
            return RagPhysicalAgentAssignment.IsLocalPlayerRagPhysicalAgent();

        int predicted = RagPhysicalAgentAssignment.ResolveDesignatedActorNumber();
        return predicted >= 0 && predicted == PhotonNetwork.LocalPlayer.ActorNumber;
    }

    private PlayerRole GetLocalPlayerRole()
    {
        if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue("Designation", out object value))
        {
            if (System.Enum.TryParse(value.ToString(), out PlayerRole role))
            {
                return role;
            }
        }

        // Default fallback
        return PlayerRole.Worker;
    }

    private Transform GetSpawnPoint(PlayerRole role)
    {
        if (role == PlayerRole.Supervisor)
        {
            return supervisorSpawnPoint;
        }

        if (workerSpawnPoints == null || workerSpawnPoints.Length == 0)
            return null;

        // Worker spawn (spread workers)
        int index = PhotonNetwork.LocalPlayer.ActorNumber % workerSpawnPoints.Length;
        return workerSpawnPoints[index];
    }
}
