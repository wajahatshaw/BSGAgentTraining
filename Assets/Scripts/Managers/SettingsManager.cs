using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class SettingsManager : MonoBehaviour
{
    #region VARIABLE
    [SerializeField] Button saveButton;
    [Header("Audio")]
    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private Slider sfxVolumeSlider;
    [SerializeField] private CustomToggleDOTween muteToggle;

    [Header("UI / Controls")]
    [SerializeField] private TMP_Dropdown uiScaleDropdown; // Small / Medium / Large
    [SerializeField] private Slider touchSensitivitySlider;
    [SerializeField] private CanvasScaler hubScaler;
    [Tooltip("Optional — same switch as the top-right HUD button (skill gauges + Zone 0 steps).")]
    [SerializeField] private CustomToggleDOTween ragTrainingHudToggle;

    [Header("Performance & Quality")]
    [SerializeField] private TMP_Dropdown graphicsQualityDropdown; // Low / Medium / High
    [SerializeField] private CustomToggleDOTween shadowsToggle;

    private const string MASTER_VOL = "MasterVolume";
    private const string SFX_VOL = "SFXVolume";
    private const string MUTE = "Mute";
    private const string UI_SCALE = "UIScale";
    private const string TOUCH_SENS = "TouchSensitivity";
    private const string GRAPHICS = "GraphicsQuality";
    private const string SHADOWS = "Shadows";
    public static Action<float> AC_TouchSensetivityChanged;

    #endregion




    void OnEnable()
    {
        RagTrainingHudVisibility.Changed += SyncRagHudToggleFromGlobal;
    }

    void OnDisable()
    {
        RagTrainingHudVisibility.Changed -= SyncRagHudToggleFromGlobal;
    }

    void Start()
    {
        LoadSettings();
        ApplyAllSettings();
        RegisterUIEvents();
        RegisterDirtyListeners();
        saveButton.interactable = false;
    }

    void SyncRagHudToggleFromGlobal(bool visible)
    {
        if (ragTrainingHudToggle == null) return;
        if (ragTrainingHudToggle.IsOn == visible) return;
        ragTrainingHudToggle.SetState(visible, animate: true);
    }

    #region Audio

    public void SetMasterVolume(float value)
    {
        AudioListener.volume = muteToggle.IsOn ? 0f : value;
        //PlayerPrefs.SetFloat(MASTER_VOL, value);
    }

    public void SetSFXVolume(float value)
    {
        //PlayerPrefs.SetFloat(SFX_VOL, value);
        // Hook this value into your SFX AudioMixer later if needed
    }

    public void SetMute(bool isMuted)
    {
        AudioListener.volume = isMuted ? 0f : masterVolumeSlider.value;
        //PlayerPrefs.SetInt(MUTE, isMuted ? 1 : 0);
    }

    #endregion

    #region UI / Control

    public void SetUIScale(int index)
    {
        float scale = 1f;

        switch (index)
        {
            case 0: scale = 0.85f; break; // Small
            case 1: scale = 1f; break;    // Medium
            case 2: scale = 1.15f; break; // Large
        }

        
        if (hubScaler != null)
            hubScaler.scaleFactor = scale;

        //PlayerPrefs.SetInt(UI_SCALE, index);
    }

    public void SetTouchSensitivity(float value)
    {
        //PlayerPrefs.SetFloat(TOUCH_SENS, value);
        // Use this value in your input / camera controller
        AC_TouchSensetivityChanged?.Invoke(value);
    }

    public void SetRagTrainingHudVisible(bool visible)
    {
        RagTrainingHudVisibility.SetVisible(visible);
        if (ragTrainingHudToggle != null && ragTrainingHudToggle.GetValue() != visible)
            ragTrainingHudToggle.SetState(visible, animate: false);
    }

    #endregion

    #region Performance & Quality

    public void SetGraphicsQuality(int index)
    {
        QualitySettings.SetQualityLevel(index);
        //PlayerPrefs.SetInt(GRAPHICS, index);
    }

    public void SetShadows(bool enabled)
    {
        QualitySettings.shadows = enabled
            ? ShadowQuality.All
            : ShadowQuality.Disable;

        //PlayerPrefs.SetInt(SHADOWS, enabled ? 1 : 0);
    }

    #endregion

    #region Init & Save

    private void RegisterUIEvents()
    {
        masterVolumeSlider.onValueChanged.AddListener(SetMasterVolume);
        sfxVolumeSlider.onValueChanged.AddListener(SetSFXVolume);
        muteToggle.OnToggleValueChanged.AddListener(SetMute);
        uiScaleDropdown.onValueChanged.AddListener(SetUIScale);
        touchSensitivitySlider.onValueChanged.AddListener(SetTouchSensitivity);
        graphicsQualityDropdown.onValueChanged.AddListener(SetGraphicsQuality);
        shadowsToggle.OnToggleValueChanged.AddListener(SetShadows);
        if (ragTrainingHudToggle != null)
            ragTrainingHudToggle.OnToggleValueChanged.AddListener(SetRagTrainingHudVisible);
    }
    private void RegisterDirtyListeners()
    {
        masterVolumeSlider.onValueChanged.AddListener(_ => MarkDirty());
        sfxVolumeSlider.onValueChanged.AddListener(_ => MarkDirty());
        muteToggle.OnToggleValueChanged.AddListener(_ => MarkDirty());
        uiScaleDropdown.onValueChanged.AddListener(_ => MarkDirty());
        touchSensitivitySlider.onValueChanged.AddListener(_ => MarkDirty());
        graphicsQualityDropdown.onValueChanged.AddListener(_ => MarkDirty());
        shadowsToggle.OnToggleValueChanged.AddListener(_ => MarkDirty());
        if (ragTrainingHudToggle != null)
            ragTrainingHudToggle.OnToggleValueChanged.AddListener(_ => MarkDirty());
    }


    private void LoadSettings()
    {
        masterVolumeSlider.value = PlayerPrefs.GetFloat(MASTER_VOL, 1f);
        sfxVolumeSlider.value = PlayerPrefs.GetFloat(SFX_VOL, 1f);
        muteToggle.IsOn = PlayerPrefs.GetInt(MUTE, 0) == 1;

        uiScaleDropdown.value = PlayerPrefs.GetInt(UI_SCALE, 1);
        touchSensitivitySlider.value = PlayerPrefs.GetFloat(TOUCH_SENS, 1f);

        graphicsQualityDropdown.value = PlayerPrefs.GetInt(GRAPHICS, 1);
        shadowsToggle.IsOn = PlayerPrefs.GetInt(SHADOWS, 1) == 1;

        RagTrainingHudVisibility.Initialize(defaultVisible: false);
        if (ragTrainingHudToggle != null)
            ragTrainingHudToggle.SetState(RagTrainingHudVisibility.IsVisible, animate: false);
    }

    private void ApplyAllSettings()
    {
        SetMasterVolume(masterVolumeSlider.value);
        SetSFXVolume(sfxVolumeSlider.value);
        SetMute(muteToggle.IsOn);
        SetUIScale(uiScaleDropdown.value);
        SetTouchSensitivity(touchSensitivitySlider.value);
        SetGraphicsQuality(graphicsQualityDropdown.value);
        SetShadows(shadowsToggle.IsOn);
        if (ragTrainingHudToggle != null)
            SetRagTrainingHudVisible(ragTrainingHudToggle.IsOn);
    }

    #endregion

    #region Button
    public void SaveSettings()
    {
        // Audio
        PlayerPrefs.SetFloat("MasterVolume", masterVolumeSlider.value);
        PlayerPrefs.SetFloat("SFXVolume", sfxVolumeSlider.value);
        PlayerPrefs.SetInt("Mute", muteToggle.IsOn ? 1 : 0);

        // UI / Control
        PlayerPrefs.SetInt("UIScale", uiScaleDropdown.value);
        PlayerPrefs.SetFloat("TouchSensitivity", touchSensitivitySlider.value);

        // Performance
        PlayerPrefs.SetInt("GraphicsQuality", graphicsQualityDropdown.value);
        PlayerPrefs.SetInt("Shadows", shadowsToggle.IsOn ? 1 : 0);
        if (ragTrainingHudToggle != null)
            PlayerPrefs.SetInt(RagTrainingHudVisibility.PlayerPrefsKey, ragTrainingHudToggle.IsOn ? 1 : 0);

        PlayerPrefs.Save();
        isDirty = false;
        saveButton.interactable = false;

        Debug.Log("Settings Saved");
    }


    public void RevertSettings()
    {
        LoadSettings();
        ApplyAllSettings();

        isDirty = false;
        saveButton.interactable = false;
        Debug.Log("Settings Reverted to Last Saved");
    }

    private bool isDirty;

    public void MarkDirty()
    {
        isDirty = true;
        saveButton.interactable = true;
    }





    #endregion
}
