using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>
/// Master-authoritative sync of RAG orchestrator step completion across Photon clients.
/// Mental steps: Master Client (M1 sim owner). Physical steps: designated P1 worker client.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class RagOrchestratorNetworkSync : MonoBehaviourPunCallbacks, IRagOrchestratorStepReporter
{
    public static RagOrchestratorNetworkSync Instance { get; private set; }

    public bool IsActive => ShouldUseNetworkSync();

    public static RagOrchestratorNetworkSync EnsureInScene()
    {
        if (Instance != null)
            return Instance;

        GameObject go = new GameObject(nameof(RagOrchestratorNetworkSync));
        go.AddComponent<PhotonView>();
        return go.AddComponent<RagOrchestratorNetworkSync>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        PhotonView pv = GetComponent<PhotonView>();
        if (pv != null && pv.ViewID == 0 && PhotonNetwork.IsMasterClient)
            pv.ViewID = PhotonNetwork.AllocateViewID(false);

        RagOrchestratorStepReporterRegistry.Register(this);
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            RagOrchestratorStepReporterRegistry.Register(null);
        }
    }

    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged)
    {
        if (propertiesThatChanged == null
            || !propertiesThatChanged.ContainsKey(RagPhysicalAgentAssignment.RoomPropertyKey))
        {
            return;
        }

        RagPhysicalAgentAssignment.RefreshFromRoom();
        DesignatedPhysicalPlayerAppearance.SyncAllPhysicalPlayerAppearances();
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (PhotonNetwork.IsMasterClient)
        {
            RagPhysicalAgentAssignment.EnsureAssignedInRoom();
            BroadcastMentalStepSnapshot(0);
        }

        DesignatedPhysicalPlayerAppearance.SyncAllPhysicalPlayerAppearances();
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        RagMentalAgentNetworkAuthority.ApplyAfterZoneSpawn(0);

        if (PhotonNetwork.IsMasterClient)
            BroadcastMentalStepSnapshot(0);
    }

    public void ReportMentalStepActivated(int zoneIndex, string stepId)
    {
        RequestMentalStepActivated(zoneIndex, stepId);
    }

    public void ReportStepCompleted(int zoneIndex, string stepId, bool isMentalStep)
    {
        RequestStepCompletion(zoneIndex, stepId, isMentalStep);
    }

    public void ReportCognitivePhaseComplete(int zoneIndex)
    {
        RequestCognitivePhaseComplete(zoneIndex);
    }

    public void ReportExternalStepCompleted(int zoneIndex, string stepId)
    {
        RequestExternalStepCompletion(zoneIndex, stepId);
    }

    public static void CompleteOrchestratorStep(int zoneIndex, string stepId, bool isMentalStep)
    {
        if (RagOrchestratorStepReporterRegistry.HasReporter)
            RagOrchestratorStepReporterRegistry.ReportStepCompleted(zoneIndex, stepId, isMentalStep);
        else
            ApplyStepCompletedLocally(zoneIndex, stepId);
    }

    public static void CompleteExternalStep(int zoneIndex, string stepId)
    {
        if (RagOrchestratorStepReporterRegistry.HasReporter)
            RagOrchestratorStepReporterRegistry.ReportExternalStepCompleted(zoneIndex, stepId);
        else
            ApplyStepCompletedLocally(zoneIndex, stepId);
    }

    public static void NotifyCognitivePhaseComplete(int zoneIndex)
    {
        if (RagOrchestratorStepReporterRegistry.HasReporter)
            RagOrchestratorStepReporterRegistry.ReportCognitivePhaseComplete(zoneIndex);
        else
            ApplyCognitivePhaseCompleteLocally(zoneIndex);
    }

    void RequestStepCompletion(int zoneIndex, string stepId, bool isMentalStep)
    {
        if (string.IsNullOrEmpty(stepId))
            return;

        if (!ShouldUseNetworkSync())
        {
            ApplyStepCompletedLocally(zoneIndex, stepId);
            return;
        }

        if (isMentalStep && !PhotonNetwork.IsMasterClient)
            return;

        if (!isMentalStep && !RagPhysicalAgentAssignment.IsLocalPlayerRagPhysicalAgent())
            return;

        RagOrchestratorNetworkSync sync = Instance ?? EnsureInScene();
        if (sync == null)
        {
            ApplyStepCompletedLocally(zoneIndex, stepId);
            return;
        }

        if (PhotonNetwork.IsMasterClient)
            sync.photonView.RPC(nameof(RpcApplyStepCompleted), RpcTarget.All, zoneIndex, stepId);
        else
            sync.photonView.RPC(nameof(RpcRequestStepCompleted), RpcTarget.MasterClient, zoneIndex, stepId, isMentalStep);
    }

    void RequestExternalStepCompletion(int zoneIndex, string stepId)
    {
        if (string.IsNullOrEmpty(stepId))
            return;

        if (!ShouldUseNetworkSync())
        {
            ApplyStepCompletedLocally(zoneIndex, stepId);
            return;
        }

        if (!PhotonNetwork.IsMasterClient)
            return;

        RagOrchestratorNetworkSync sync = Instance ?? EnsureInScene();
        sync?.photonView.RPC(nameof(RpcApplyStepCompleted), RpcTarget.All, zoneIndex, stepId);
    }

    void RequestCognitivePhaseComplete(int zoneIndex)
    {
        if (!ShouldUseNetworkSync())
        {
            ApplyCognitivePhaseCompleteLocally(zoneIndex);
            return;
        }

        if (!PhotonNetwork.IsMasterClient)
            return;

        RagOrchestratorNetworkSync sync = Instance ?? EnsureInScene();
        sync?.photonView.RPC(nameof(RpcCognitivePhaseComplete), RpcTarget.All, zoneIndex);
    }

    void RequestMentalStepActivated(int zoneIndex, string stepId)
    {
        if (string.IsNullOrEmpty(stepId))
            return;

        if (!ShouldUseNetworkSync())
        {
            ApplyStepActivatedLocally(zoneIndex, stepId);
            return;
        }

        if (!PhotonNetwork.IsMasterClient)
            return;

        RagOrchestratorNetworkSync sync = Instance ?? EnsureInScene();
        sync?.photonView.RPC(nameof(RpcApplyStepActivated), RpcTarget.All, zoneIndex, stepId);
    }

    void BroadcastMentalStepSnapshot(int zoneIndex)
    {
        if (!ShouldUseNetworkSync() || !PhotonNetwork.IsMasterClient)
            return;

        CognitivePhaseOrchestrator orch = CognitivePhaseOrchestrator.GetOrCreateForZone(zoneIndex);
        if (orch == null)
            return;

        if (orch.TryGetFirstActiveCognitiveStepId(out string activeId))
        {
            ActionSequenceStep step = orch.GetStep(activeId);
            if (step != null && step.isActivated)
                RequestMentalStepActivated(zoneIndex, activeId);
        }

        RagOrchestratorNetworkSync sync = Instance ?? EnsureInScene();
        if (sync == null)
            return;

        foreach (string stepId in orch.GetCompletedStepIdsSnapshot())
            sync.photonView.RPC(nameof(RpcApplyStepCompleted), RpcTarget.All, zoneIndex, stepId);
    }

    static bool ShouldUseNetworkSync()
    {
        return BsgIntegrationSettings.UsePhotonPlayerAsPhysicalAgent && PhotonNetwork.InRoom;
    }

    [PunRPC]
    void RpcRequestStepCompleted(int zoneIndex, string stepId, bool isMentalStep, PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        if (isMentalStep)
        {
            if (!info.Sender.IsMasterClient)
                return;
        }
        else if (!RagPhysicalAgentAssignment.IsActorRagPhysicalAgent(info.Sender.ActorNumber))
        {
            return;
        }

        photonView.RPC(nameof(RpcApplyStepCompleted), RpcTarget.All, zoneIndex, stepId);
    }

    [PunRPC]
    void RpcApplyStepActivated(int zoneIndex, string stepId)
    {
        ApplyStepActivatedLocally(zoneIndex, stepId);
    }

    [PunRPC]
    void RpcApplyStepCompleted(int zoneIndex, string stepId)
    {
        ApplyStepCompletedLocally(zoneIndex, stepId);
    }

    [PunRPC]
    void RpcCognitivePhaseComplete(int zoneIndex)
    {
        ApplyCognitivePhaseCompleteLocally(zoneIndex);
    }

    static void ApplyStepActivatedLocally(int zoneIndex, string stepId)
    {
        CognitivePhaseOrchestrator orch = CognitivePhaseOrchestrator.GetOrCreateForZone(zoneIndex);
        if (orch == null)
            return;

        ActionSequenceStep step = orch.GetStep(stepId);
        if (step == null)
            return;

        step.isActivated = true;
        RagCognitiveStepFx.ApplyStepActivatedFx(step, zoneIndex);
    }

    static void ApplyStepCompletedLocally(int zoneIndex, string stepId)
    {
        CognitivePhaseOrchestrator orch = CognitivePhaseOrchestrator.GetOrCreateForZone(zoneIndex);
        if (orch == null)
            return;

        if (orch.IsStepCompleted(stepId))
            return;

        ActionSequenceStep step = orch.GetStep(stepId);
        if (step != null)
        {
            step.isActivated = false;
            step.isStepCompleted = true;
        }

        orch.NotifyStepCompleted(stepId);

        if (step != null)
        {
            if (RagStepRoleClassifier.IsMentalAgentStep(step))
                RagCognitiveStepFx.ApplyStepCompletedFx(step, zoneIndex);
            else
                RagPhysicalStepFx.ApplyStepCompletedFx(step, zoneIndex);
        }
    }

    static void ApplyCognitivePhaseCompleteLocally(int zoneIndex)
    {
        CognitivePhaseOrchestrator orch = CognitivePhaseOrchestrator.GetOrCreateForZone(zoneIndex);
        orch?.NotifyCognitivePhaseCompleteForPhysicalUnlock();

        ZoneDeclarativeMemory mem = ZoneDeclarativeMemory.ForZone(zoneIndex);
        if (mem == null)
        {
            ZoneDeclarativeMemory.RebuildRegistryFromScene();
            mem = ZoneDeclarativeMemory.ForZone(zoneIndex);
        }

        if (mem != null)
            mem.cognitiveReady = true;

        foreach (RagSequenceAgentMover mover in FindObjectsOfType<RagSequenceAgentMover>())
        {
            if (mover == null || mover.zoneIndex != zoneIndex || !mover.isMentalAgent)
                continue;
            mover.SetCognitivePhaseCompleteForInference(true);
        }
    }
}
