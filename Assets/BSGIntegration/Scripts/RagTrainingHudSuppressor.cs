using UnityEngine;

/// <summary>
/// Hides BSG ML training HUD when RAG is embedded inside ProtoypeSceneMultiplayer.
/// Keeps user-toggleable step stats (AgentSkillGaugeUI step indicator + temporal buffer).
/// </summary>
public static class RagTrainingHudSuppressor
{
    public static void Apply()
    {
        foreach (TemporalBufferGaugeUI temporal in Object.FindObjectsOfType<TemporalBufferGaugeUI>(true))
        {
            if (temporal != null && temporal.zoneIndex != 0)
                temporal.enabled = false;
        }
        foreach (DeclarativeMemoryGaugeUI declarative in Object.FindObjectsOfType<DeclarativeMemoryGaugeUI>(true))
        {
            if (declarative != null && declarative.zoneIndex != 0)
                declarative.enabled = false;
        }
        foreach (CognitiveStationDetailPanelUI stationDetail in Object.FindObjectsOfType<CognitiveStationDetailPanelUI>(true))
        {
            if (stationDetail != null && stationDetail.zoneIndex != 0)
                stationDetail.enabled = false;
        }
        DisableBehaviour<TrainingSpeedIndicator>();
        DisableBehaviour<StepEfficiencyIndicator>();
        DisableBehaviour<DistanceToTargetMeter>();
        DisableBehaviour<EpisodeCounterTimer>();
        DisableBehaviour<SimpleStatusBoard>();
        DisableBehaviour<CognitiveInteractionHUD>();

        foreach (string canvasName in new[]
        {
            "TrainingSpeedCanvas",
            "StepEfficiencyCanvas",
            "DistanceMeterCanvas",
            "EpisodeCounterCanvas",
            "_LoadingOverlay",
        })
        {
            GameObject canvas = GameObject.Find(canvasName);
            if (canvas != null)
                canvas.SetActive(false);
        }

        foreach (string panelName in new[]
        {
            "Zone1TemporalBufferPanel",
            "Zone2TemporalBufferPanel",
            "Zone3TemporalBufferPanel",
            "Zone1DeclarativeMemoryPanel",
            "Zone2DeclarativeMemoryPanel",
            "Zone3DeclarativeMemoryPanel",
        })
        {
            GameObject panel = GameObject.Find(panelName);
            if (panel != null)
                panel.SetActive(false);
        }

        DestroyStrayBsgFollowCamerasOnSecondaryDisplays();
        SuppressStaleMultiplayerTaskLabel();

        // Do not disable AgentSkillGaugeUI — RagMultiplayerStatsHud uses it for M/P step toggle.
    }

    static void DestroyStrayBsgFollowCamerasOnSecondaryDisplays()
    {
        foreach (string name in new[]
        {
            "GameView_Display2_Rag_MentalPhysical_Camera",
            "GameView_Display3_Rag_Physical_Camera",
            "GameView_Display2_MentalPhysical_Camera",
            "GameView_Display3_Physical_Camera",
            "GameView_Display4_MA_Camera",
            "GameView_Display5_MB_Camera",
            "GameView_Display6_MC_Camera",
        })
        {
            GameObject go = GameObject.Find(name);
            if (go != null)
                Object.Destroy(go);
        }

        foreach (ZoneAgentFollowCamera cam in Object.FindObjectsOfType<ZoneAgentFollowCamera>(true))
        {
            if (cam == null)
                continue;
            if (cam.targetDisplay == 1 || cam.targetDisplay == 2)
                Object.Destroy(cam.gameObject);
        }
    }

    static void SuppressStaleMultiplayerTaskLabel()
    {
        if (!BsgIntegrationSettings.UsePhotonPlayerAsPhysicalAgent)
            return;

        GameObject taskLabel = GameObject.Find("Task_TMP(Text)");
        if (taskLabel != null)
            taskLabel.SetActive(false);

        GameObject taskUi = GameObject.Find("TaskUI");
        if (taskUi != null)
            taskUi.SetActive(false);

        foreach (SceneUILoader loader in Object.FindObjectsOfType<SceneUILoader>(true))
        {
            if (loader == null)
                continue;
            if (loader.currentTaskText != null)
                loader.currentTaskText.gameObject.SetActive(false);
            if (loader.workerTitleText != null)
                loader.workerTitleText.gameObject.SetActive(false);
            if (loader.taskDetailsText != null)
                loader.taskDetailsText.gameObject.SetActive(false);
            if (loader.stepProgressText != null)
                loader.stepProgressText.gameObject.SetActive(false);
        }
    }

    static void DisableBehaviour<T>() where T : Behaviour
    {
        foreach (T c in Object.FindObjectsOfType<T>(true))
        {
            if (c != null)
                c.enabled = false;
        }
    }
}
