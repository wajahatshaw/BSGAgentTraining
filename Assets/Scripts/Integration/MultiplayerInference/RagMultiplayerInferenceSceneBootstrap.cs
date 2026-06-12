using System.Collections;
using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Embeds BSG zone 0 RAG with ONNX inference into ProtoypeSceneMultiplayerInference.
/// Separate from <see cref="RagMultiplayerSceneBootstrap"/> (training) — does not enable Python ML training.
/// </summary>
[DefaultExecutionOrder(-100)]
public class RagMultiplayerInferenceSceneBootstrap : MonoBehaviour
{
    public static bool IsActive => BsgIntegrationSettings.MultiplayerInferenceSceneActive;

    [Header("RAG embed — zone 0 only")]
    [SerializeField] private string jsonFileName = RagSceneFactory.DefaultJsonFileName;

    [Header("ONNX inference")]
    [SerializeField] private bool hideCognitiveAndMentalVisuals = true;
    [SerializeField] private bool useInvisibleCognitiveScriptedWalk = true;
    [SerializeField] private bool useScriptedPhysicalLocomotion = true;

    [Header("TaskManager bridge")]
    [SerializeField] private bool enableTaskRagBridge = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void UnregisterBootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        BsgIntegrationSettings.MultiplayerInferenceSceneActive = false;
        PlayerRagInferenceBridge.ResetForDomainReload();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void RegisterBootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != MultiplayerInferenceSceneNames.GameScene)
            return;
        if (Object.FindObjectOfType<RagMultiplayerInferenceSceneBootstrap>() != null)
            return;

        var go = new GameObject(nameof(RagMultiplayerInferenceSceneBootstrap));
        go.AddComponent<RagMultiplayerInferenceSceneBootstrap>();
    }

    void Awake()
    {
        BsgIntegrationSettings.MultiplayerInferenceSceneActive = true;
        MLTrainingLogger.SuppressPeriodicSummary = true;

        MultiplayerRagZone0Anchor anchor = FindObjectOfType<MultiplayerRagZone0Anchor>();
        if (anchor == null)
        {
            Debug.LogError("[RagMultiplayerInferenceSceneBootstrap] MultiplayerRagZone0Anchor not found. Add it under =Environment/Base in ProtoypeSceneMultiplayerInference.");
            return;
        }

        anchor.enableMlTrainingInRagMode = false;
        anchor.enableMlTrainingForCognitiveAgents = false;
        anchor.trainZonesMask = 0;

        var options = RagSceneFactory.DefaultMultiplayerOptions;
        options.jsonFileName = jsonFileName;
        options.spawnZonesMask = 1;
        options.skipEnvironmentGeneration = true;
        options.useSceneAnchorLayout = true;
        options.enableMlTrainingInRagMode = false;
        options.enableMlTrainingForCognitiveAgents = false;
        options.trainZonesMask = 0;

        GameObject replicaRoot = RagSceneFactory.EnsureReplicaSceneManager(options);
        EnsureInferenceDeployerOnReplica(replicaRoot);

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
        StartCoroutine(ApplyInferencePipelineWhenReady());
        PlayerRagInferenceBridge.BeginBinding(this);

        Debug.Log("[RagMultiplayerInferenceSceneBootstrap] Zone 0 ONNX inference embed started — no mlagents-learn. " +
                  "M1 cognitive + designated Photon P1 use Sentis models from Assets/ML-Agents/Models/Inference.");
    }

    void OnDestroy()
    {
        if (BsgIntegrationSettings.MultiplayerInferenceSceneActive)
        {
            BsgIntegrationSettings.MultiplayerInferenceSceneActive = false;
            MLTrainingLogger.SuppressPeriodicSummary = false;
        }
    }

    static void EnsureInferenceDeployerOnReplica(GameObject replicaRoot)
    {
        if (replicaRoot == null)
            return;

        RagMlBrainDeployer deployer = replicaRoot.GetComponent<RagMlBrainDeployer>();
        if (deployer == null)
            deployer = replicaRoot.AddComponent<RagMlBrainDeployer>();

        deployer.applyOnStart = false;
        deployer.deployPhysicalBrains = true;
        deployer.deployCognitiveBrains = true;
        deployer.autoLoadModelsFromInferenceFolder = true;
    }

    IEnumerator ApplyInferencePipelineWhenReady()
    {
        for (int i = 0; i < 120; i++)
        {
            if (SceneManager.GetActiveScene().name != MultiplayerInferenceSceneNames.GameScene)
                yield break;

            RagInferenceSceneController ctrl = FindObjectOfType<RagInferenceSceneController>();
            if (ctrl != null)
            {
                // Solo inference scene controller present — let it run the pipeline.
                yield break;
            }

            if (IsZone0EmbedContentReady())
            {
                if (hideCognitiveAndMentalVisuals)
                    RagInferenceVisuals.ApplyFullInferenceHide();

                RagMlBrainDeployer deployer = RagInferenceMLBootstrap.EnsureDeployerInScene();
                RagMultiplayerInferenceMLBootstrap.AttachZone0CognitiveInference(
                    deployer,
                    useInvisibleCognitiveScriptedWalk);

                CognitivePhaseOrchestrator orch = CognitivePhaseOrchestrator.GetOrCreateForZone(0);
                orch?.EnsureInitializedFromSceneAgents();

                if (hideCognitiveAndMentalVisuals)
                    RagInferenceVisuals.ApplyFullInferenceHide();

                RagInferenceMLBootstrap.ReassertOnnxBrains(deployer);

                RagMultiplayerStatsHud.EnsureInScene();
                RagTrainingHudSwitchController.EnsureInScene();
                yield break;
            }

            yield return new WaitForSeconds(0.25f);
        }
    }

    IEnumerator ApplyRagPhysicalAgentLocalModeWhenReady()
    {
        for (int i = 0; i < 120; i++)
        {
            if (SceneManager.GetActiveScene().name != MultiplayerInferenceSceneNames.GameScene)
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
            if (SceneManager.GetActiveScene().name != MultiplayerInferenceSceneNames.GameScene)
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
            if (SceneManager.GetActiveScene().name != MultiplayerInferenceSceneNames.GameScene)
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
            if (SceneManager.GetActiveScene().name != MultiplayerInferenceSceneNames.GameScene)
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
            if (SceneManager.GetActiveScene().name != MultiplayerInferenceSceneNames.GameScene)
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
            if (SceneManager.GetActiveScene().name != MultiplayerInferenceSceneNames.GameScene)
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
