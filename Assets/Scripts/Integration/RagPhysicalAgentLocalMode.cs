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

    /// <summary>
    /// Restores normal fixed-joystick worker control for non-designated multiplayer clients.
    /// </summary>
    public static void EnsureWorkerControlForLocalClient(PlayerMovement playerMovement)
    {
        if (playerMovement == null || IsActive)
            return;

        PhotonView pv = playerMovement.GetComponent<PhotonView>();
        if (pv != null && !pv.IsMine)
            return;

        if (RagPhysicalAgentAssignment.IsLocalPlayerRagPhysicalAgent())
            return;

        playerMovement.EnableRagGroundMotorMovement(null);

        Rigidbody rb = playerMovement.rb;
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
        }

        playerMovement.DisableAll(move: true, crouch: true, interact: true, jump: true, look: true);

        PlayerMovementInputProcessor inputProcessor = playerMovement.InputProcessor;
        if (inputProcessor != null)
        {
            inputProcessor.SetRagAutopilot(false);
            FixedJoystick joy = playerMovement.fixedJoystick != null
                ? playerMovement.fixedJoystick
                : Object.FindAnyObjectByType<FixedJoystick>();
            inputProcessor.EnableManualJoystick(joy);
        }

        if (UIManager.Instance != null)
            UIManager.Instance.ToggleHudInput(true);

        foreach (FixedJoystick joy in Object.FindObjectsOfType<FixedJoystick>(true))
        {
            if (joy != null && joy.gameObject != null)
                joy.gameObject.SetActive(true);
        }

        foreach (FixedTouchField touch in Object.FindObjectsOfType<FixedTouchField>(true))
        {
            if (touch != null && touch.gameObject != null)
                touch.gameObject.SetActive(true);
        }
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
