using System.Collections;
using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Embeds BSG zone 0 RAG (P1, M1, cognitive stations) into ProtoypeSceneMultiplayer.
/// Multiplayer scene stays dominant: joystick, task UI, motor environment, player camera.
/// No 4-zone grid, no BSG training HUD, no environment hide.
/// </summary>
[DefaultExecutionOrder(-100)]
public class RagMultiplayerSceneBootstrap : MonoBehaviour
{
    public const string MultiplayerSceneName = "ProtoypeSceneMultiplayer";

    [Header("RAG embed — zone 0 only")]
    [SerializeField] private string jsonFileName = RagSceneFactory.DefaultJsonFileName;

    [Header("ML (off during normal multiplayer play)")]
    [SerializeField] private bool enableMlTrainingInRagMode;
    [SerializeField] private bool enableMlTrainingForCognitiveAgents;

    [Header("TaskManager bridge")]
    [SerializeField] private bool enableTaskRagBridge = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void UnregisterBootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void RegisterBootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != MultiplayerSceneName)
            return;
        if (Object.FindObjectOfType<RagMultiplayerSceneBootstrap>() != null)
            return;

        var go = new GameObject(nameof(RagMultiplayerSceneBootstrap));
        go.AddComponent<RagMultiplayerSceneBootstrap>();
    }

    void Awake()
    {
        MultiplayerRagZone0Anchor anchor = FindObjectOfType<MultiplayerRagZone0Anchor>();
        if (anchor == null)
        {
            Debug.LogError("[RagMultiplayerSceneBootstrap] MultiplayerRagZone0Anchor not found in ProtoypeSceneMultiplayer. Add it under =Environment/Base.");
            return;
        }

        var options = RagSceneFactory.DefaultMultiplayerOptions;
        options.jsonFileName = jsonFileName;
        options.spawnZonesMask = 1;
        options.skipEnvironmentGeneration = true;
        options.useSceneAnchorLayout = true;
        options.enableMlTrainingInRagMode = anchor.enableMlTrainingInRagMode || enableMlTrainingInRagMode;
        options.enableMlTrainingForCognitiveAgents =
            anchor.enableMlTrainingForCognitiveAgents || enableMlTrainingForCognitiveAgents;
        options.trainZonesMask = anchor.trainZonesMask != 0 ? anchor.trainZonesMask : 1;

        RagSceneFactory.EnsureReplicaSceneManager(options);

        if (enableTaskRagBridge && FindObjectOfType<TaskRagBridge>() == null)
        {
            var bridgeGo = new GameObject("TaskRagBridge");
            bridgeGo.AddComponent<TaskRagBridge>();
        }

        RagPhysicalAgentAssignment.EnsureAssignedInRoom();
        RagOrchestratorNetworkSync.EnsureInScene();
        StartCoroutine(ApplyRagPhysicalAgentLocalModeWhenReady());
        StartCoroutine(SetupMentalAgentNetworkAuthorityWhenReady());
        StartCoroutine(SuppressBsgHudAfterGeneration());
        StartCoroutine(EnsureHudSwitchWhenHudReady());
        StartCoroutine(EnsureDisplay2OverviewWhenReady());
        StartCoroutine(EnsureDesignatedPlayerAppearanceWhenReady());
        StartCoroutine(EnsureWorkerPlayerControlWhenReady());
        PlayerRagPhysicalBridge.BeginBinding(this);

        Debug.Log($"[RagMultiplayerSceneBootstrap] Zone 0 RAG embed started — mlTraining={options.enableMlTrainingInRagMode}, " +
                  $"cognitiveMl={options.enableMlTrainingForCognitiveAgents}, trainZonesMask={options.trainZonesMask}. " +
                  "PhysicalAgentZone0 attaches when the designated Photon player binds as P1.");
    }

    IEnumerator ApplyRagPhysicalAgentLocalModeWhenReady()
    {
        for (int i = 0; i < 120; i++)
        {
            if (SceneManager.GetActiveScene().name != MultiplayerSceneName)
                yield break;

            RagPhysicalAgentAssignment.EnsureAssignedInRoom();
            if (RagPhysicalAgentAssignment.WaitForDesignationReady()
                && RagPhysicalAgentAssignment.IsLocalPlayerRagPhysicalAgent())
            {
                RagPhysicalAgentLocalMode.ApplyForLocalClient();
                yield break;
            }

            yield return new WaitForSeconds(0.25f);
        }
    }

    IEnumerator SetupMentalAgentNetworkAuthorityWhenReady()
    {
        for (int i = 0; i < 120; i++)
        {
            if (SceneManager.GetActiveScene().name != MultiplayerSceneName)
                yield break;

            RagSequenceAgentMover mentalMover = null;
            foreach (RagSequenceAgentMover mover in FindObjectsOfType<RagSequenceAgentMover>())
            {
                if (mover != null && mover.isMentalAgent && mover.zoneIndex == 0)
                {
                    mentalMover = mover;
                    break;
                }
            }

            if (mentalMover != null)
            {
                RagMentalAgentNetworkAuthority.ApplyAfterZoneSpawn(0);
                yield break;
            }

            yield return new WaitForSeconds(0.25f);
        }
    }

    IEnumerator EnsureDesignatedPlayerAppearanceWhenReady()
    {
        for (int i = 0; i < 120; i++)
        {
            if (SceneManager.GetActiveScene().name != MultiplayerSceneName)
                yield break;

            RagPhysicalAgentAssignment.EnsureAssignedInRoom();
            if (!RagPhysicalAgentAssignment.WaitForDesignationReady())
            {
                yield return new WaitForSeconds(0.25f);
                continue;
            }

            DesignatedPhysicalPlayerAppearance.SyncAllPhysicalPlayerAppearances();
            yield break;
        }
    }

    IEnumerator EnsureWorkerPlayerControlWhenReady()
    {
        for (int i = 0; i < 120; i++)
        {
            if (SceneManager.GetActiveScene().name != MultiplayerSceneName)
                yield break;

            RagPhysicalAgentAssignment.EnsureAssignedInRoom();
            if (!RagPhysicalAgentAssignment.WaitForDesignationReady())
            {
                yield return new WaitForSeconds(0.25f);
                continue;
            }

            if (RagPhysicalAgentAssignment.IsLocalPlayerRagPhysicalAgent())
                yield break;

            foreach (PlayerMovement pm in FindObjectsOfType<PlayerMovement>())
            {
                if (pm == null)
                    continue;

                PhotonView pv = pm.GetComponent<PhotonView>();
                if (pv == null || !pv.IsMine)
                    continue;

                RagPhysicalAgentLocalMode.EnsureWorkerControlForLocalClient(pm);
                yield break;
            }

            yield return new WaitForSeconds(0.25f);
        }
    }

    IEnumerator EnsureDisplay2OverviewWhenReady()
    {
        for (int i = 0; i < 80; i++)
        {
            if (SceneManager.GetActiveScene().name != MultiplayerSceneName)
                yield break;

            MultiplayerRagZone0Anchor anchor = FindObjectOfType<MultiplayerRagZone0Anchor>();
            if (anchor != null)
            {
                anchor.EnsureSecondaryDisplayCameras();
                yield break;
            }

            yield return new WaitForSeconds(0.25f);
        }
    }

    IEnumerator EnsureHudSwitchWhenHudReady()
    {
        for (int i = 0; i < 80; i++)
        {
            if (SceneManager.GetActiveScene().name != MultiplayerSceneName)
                yield break;

            if (GameObject.Find("SettingsButton") != null)
            {
                RagMultiplayerStatsHud.EnsureInScene();
                RagTrainingHudSwitchController.EnsureInScene();
                yield break;
            }

            yield return new WaitForSeconds(0.25f);
        }
    }

    IEnumerator SuppressBsgHudAfterGeneration()
    {
        bool embedSetupDone = false;

        for (int i = 0; i < 120; i++)
        {
            RagTrainingHudSuppressor.Apply();
            RagMultiplayerStatsHud.EnsureInScene();

            MultiplayerRagZone0Anchor anchor = FindObjectOfType<MultiplayerRagZone0Anchor>();
            anchor?.EnsureSecondaryDisplayCameras();
            MultiplayerDesignatedPlayerFollowCamera.Active?.EnforceExclusiveDisplay();

            if (!embedSetupDone && IsZone0EmbedContentReady())
            {
                anchor?.EnsureEmbedCollisionAndProximity();
                anchor?.RelocatePhysicalBandBlockingProps();
                embedSetupDone = true;
            }

            yield return new WaitForSeconds(0.25f);
        }
    }

    static bool IsZone0EmbedContentReady()
    {
        return FindObjectOfType<CognitiveStationInteractable>() != null
               || FindObjectOfType<DeclarativeObjectMetadata>() != null;
    }
}
