using Photon.Pun;
using UnityEngine;

/// <summary>
/// When the local client is the designated RAG physical agent (P1), disconnect from
/// ProtoypeSceneMultiplayer TaskManager/joystick flow and run zone 0 RAG physical steps only.
/// </summary>
public static class RagPhysicalAgentLocalMode
{
    static bool _applied;

    public static bool IsActive => _applied;

    public static bool ShouldUseRagPhysicalAgentMode()
    {
        return BsgIntegrationSettings.UsePhotonPlayerAsPhysicalAgent
               && RagPhysicalAgentAssignment.IsLocalPlayerRagPhysicalAgent();
    }

    public static void ApplyForLocalClient(PlayerMovement playerMovement = null)
    {
        if (!ShouldUseRagPhysicalAgentMode())
            return;

        _applied = true;
        SuppressTaskManager();
        SuppressTaskRagBridge();
        HideMovementHud();
        HideDesignatedPlayerTaskUi();
        CloseTaskPopupIfOpen();

        if (playerMovement != null)
            ConfigurePlayer(playerMovement);
        else
            TryConfigureSpawnedLocalPlayer();
    }

    public static void TryApplyEarly()
    {
        if (!BsgIntegrationSettings.UsePhotonPlayerAsPhysicalAgent)
            return;

        RagPhysicalAgentAssignment.EnsureAssignedInRoom();
        if (!RagPhysicalAgentAssignment.WaitForDesignationReady())
            return;

        ApplyForLocalClient();
    }

    static void ConfigurePlayer(PlayerMovement playerMovement)
    {
        if (playerMovement == null)
            return;

        PlayerMovementInputProcessor inputProcessor = playerMovement.InputProcessor;
        if (inputProcessor != null)
            inputProcessor.DisableManualJoystick();
        else
            playerMovement.fixedJoystick = null;

        playerMovement.fixedTouchField = null;
        playerMovement.DisableAll(move: false, crouch: false, interact: false, jump: false, look: true);
        HideWorldMapDotOnPlayer(playerMovement.gameObject);
    }

    static void TryConfigureSpawnedLocalPlayer()
    {
        foreach (PlayerMovement pm in Object.FindObjectsOfType<PlayerMovement>())
        {
            if (pm == null)
                continue;
            PhotonView pv = pm.GetComponent<PhotonView>();
            if (pv != null && pv.IsMine)
            {
                ConfigurePlayer(pm);
                return;
            }
        }
    }

    static void SuppressTaskManager()
    {
        foreach (TaskManager tm in Object.FindObjectsOfType<TaskManager>(true))
        {
            if (tm == null)
                continue;
            tm.CancelInvoke(nameof(TaskManagerBase.TaskManagerInit));
            tm.enabled = false;
        }

        foreach (TaskManagerBase tm in Object.FindObjectsOfType<TaskManagerBase>(true))
        {
            if (tm == null || tm is TaskManager)
                continue;
            tm.CancelInvoke(nameof(TaskManagerBase.TaskManagerInit));
            tm.enabled = false;
        }
    }

    static void SuppressTaskRagBridge()
    {
        foreach (TaskRagBridge bridge in Object.FindObjectsOfType<TaskRagBridge>(true))
        {
            if (bridge != null)
                bridge.enabled = false;
        }
    }

    static void HideMovementHud()
    {
        if (UIManager.Instance != null)
            UIManager.Instance.ToggleHudInput(false);

        foreach (FixedJoystick joy in Object.FindObjectsOfType<FixedJoystick>(true))
        {
            if (joy != null && joy.gameObject != null)
                joy.gameObject.SetActive(false);
        }

        foreach (FixedTouchField touch in Object.FindObjectsOfType<FixedTouchField>(true))
        {
            if (touch != null && touch.gameObject != null)
                touch.gameObject.SetActive(false);
        }
    }

    static void CloseTaskPopupIfOpen()
    {
        if (UIManager.Instance != null)
            UIManager.Instance.PopupTaskPopup(false, string.Empty, string.Empty, string.Empty, 0, -1);
    }

    static void HideDesignatedPlayerTaskUi()
    {
        GameObject taskUi = GameObject.Find("TaskUI");
        if (taskUi != null)
            taskUi.SetActive(false);

        foreach (TaskManagerBase tm in Object.FindObjectsOfType<TaskManagerBase>(true))
        {
            if (tm == null)
                continue;
            tm.enabled = false;
        }
    }

    static void HideWorldMapDotOnPlayer(GameObject playerGo)
    {
        if (playerGo == null)
            return;

        Transform mapDot = playerGo.transform.Find("PlayerMapDot");
        if (mapDot != null)
            mapDot.gameObject.SetActive(false);
    }

    public static void ResetForDomainReload()
    {
        _applied = false;
    }
}
