using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Zone 0 cognitive/physical step stats HUD for multiplayer RAG embed (toggle via RagTrainingHudSwitch).
/// </summary>
public static class RagMultiplayerStatsHud
{
    public static void EnsureInScene()
    {
        if (!BsgIntegrationSettings.UsePhotonPlayerAsPhysicalAgent)
            return;
        if (!RagTrainingHudSwitchController.IsAllowedScene(SceneManager.GetActiveScene().name))
            return;

        RagTrainingHudVisibility.Initialize(defaultVisible: false);

        AgentSkillGaugeUI ui = Object.FindObjectOfType<AgentSkillGaugeUI>(true);
        if (ui == null)
        {
            GameObject go = new GameObject("AgentSkillGaugeUI");
            ui = go.AddComponent<AgentSkillGaugeUI>();
        }

        ui.enabled = true;
        ui.gameObject.SetActive(true);
        ui.showOnlyPhysicalRagAgents = false;
        ui.showOnlyZoneIndex = 0;
        ui.showZone0StepIndicator = true;
        ui.stepIndicatorOnlyMode = true;
        ui.leftPadding = 20f;
        ui.gaugeWidth = 270f;
        ui.gaugeHeight = 34f;
        ui.spacing = 8f;
        ui.topPadding = 20f;

        TemporalBufferGaugeUI.GetOrCreate(0);
        DeclarativeMemoryGaugeUI.GetOrCreate(0);
        CognitiveStationDetailPanelUI.GetOrCreate(0);
        ui.RebuildGaugesNow();
        ui.ApplyUserHudVisibility();
        RagTrainingHudSwitchController.EnsureInScene();
    }
}
