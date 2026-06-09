using System;
using UnityEngine;

/// <summary>
/// User-controlled visibility for RAG training HUD panels (skill gauges, Zone 0 step indicator, temporal buffer status).
/// Default is hidden until the player turns the HUD switch ON (next to Settings).
/// </summary>
public static class RagTrainingHudVisibility
{
    public const string PlayerPrefsKey = "RagTrainingHudVisible";

    public static event Action<bool> Changed;

    static bool _initialized;
    static bool _visible;

    public static bool IsVisible => _visible;

    public static void Initialize(bool defaultVisible = false, bool loadSavedPreference = true)
    {
        if (_initialized && loadSavedPreference) return;

        _visible = loadSavedPreference && PlayerPrefs.HasKey(PlayerPrefsKey)
            ? PlayerPrefs.GetInt(PlayerPrefsKey, 0) == 1
            : defaultVisible;
        _initialized = true;
        ApplyToScene();
    }

    public static void SetVisible(bool visible, bool persistPreference = true)
    {
        if (!_initialized)
            Initialize(defaultVisible: false, loadSavedPreference: false);

        if (_visible == visible) return;

        _visible = visible;
        if (persistPreference)
            PlayerPrefs.SetInt(PlayerPrefsKey, visible ? 1 : 0);

        ApplyToScene();
        Changed?.Invoke(visible);
    }

    public static void Toggle()
    {
        SetVisible(!IsVisible);
    }

    public static void ApplyToScene()
    {
        AgentSkillGaugeUI gaugeUi = UnityEngine.Object.FindObjectOfType<AgentSkillGaugeUI>();
        if (gaugeUi != null)
            gaugeUi.ApplyUserHudVisibility();

        TemporalBufferGaugeUI[] temporalPanels = UnityEngine.Object.FindObjectsOfType<TemporalBufferGaugeUI>(true);
        for (int i = 0; i < temporalPanels.Length; i++)
        {
            if (temporalPanels[i] != null)
                temporalPanels[i].ApplyUserHudVisibility(_visible);
        }

        RagTrainingHudSwitchController switchUi = UnityEngine.Object.FindObjectOfType<RagTrainingHudSwitchController>();
        if (switchUi != null)
            switchUi.SyncToggleFromVisibility();
    }
}
