using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen-space Temporal Buffer readout styled like the agent skill gauges.
/// The buffer station remains a world object; detailed timing memory is shown here.
/// </summary>
public class TemporalBufferGaugeUI : MonoBehaviour
{
    public int zoneIndex = 0;
    public Canvas targetCanvas;

    [Header("Layout")]
    public float leftPadding = 20f;
    public float topPadding = 20f;
    public float gaugeWidth = 270f;
    public float panelHeight = 126f;
    public float skillGaugeHeight = 34f;
    public float skillGaugeSpacing = 8f;
    public float stepIndicatorHeight = 34f;

    [Header("Style")]
    public Color backgroundColor = new Color(0.04f, 0.04f, 0.05f, 0.94f);
    public Color barBackgroundColor = new Color(0.18f, 0.18f, 0.20f, 1f);
    public Color activeFillColor = new Color(0.70f, 0.58f, 1f, 1f);
    public Color completedFillColor = new Color(0.25f, 0.72f, 1f, 1f);
    public Color textColor = Color.white;
    public Color mutedTextColor = new Color(0.82f, 0.82f, 0.86f, 1f);

    GameObject panel;
    Text titleText;
    Text activeText;
    Text detailText;
    Text memoryText;
    Image activeFill;
    Text activePercentText;
    TemporalCognitionRuntime runtime;
    SkillBasedActionSystem skillSystem;
    float nextRefreshTime;

    public static TemporalBufferGaugeUI GetOrCreate(int zoneIndex)
    {
        string objectName = $"TemporalBufferGaugeUI_Zone{zoneIndex}";
        GameObject existing = GameObject.Find(objectName);
        TemporalBufferGaugeUI ui = existing != null ? existing.GetComponent<TemporalBufferGaugeUI>() : null;
        if (ui == null)
        {
            GameObject go = existing ?? new GameObject(objectName);
            ui = go.AddComponent<TemporalBufferGaugeUI>();
        }

        ui.zoneIndex = zoneIndex;
        ui.EnsureReady();
        ui.Refresh();
        ui.ApplyUserHudVisibility(RagTrainingHudVisibility.IsVisible);
        return ui;
    }

    void Start()
    {
        RagTrainingHudVisibility.Initialize(defaultVisible: false);
        EnsureReady();
        Refresh();
        ApplyUserHudVisibility(RagTrainingHudVisibility.IsVisible);
    }

    /// <summary>Show or hide the temporal buffer status panel (controlled by the HUD switch).</summary>
    public void ApplyUserHudVisibility(bool show)
    {
        EnsurePanel();
        if (panel != null)
            panel.SetActive(show);
    }

    void OnEnable()
    {
        EnsureRuntime();
        if (runtime != null)
            runtime.Changed += OnRuntimeChanged;
    }

    void OnDisable()
    {
        if (runtime != null)
            runtime.Changed -= OnRuntimeChanged;
    }

    void Update()
    {
        if (Time.time < nextRefreshTime) return;
        nextRefreshTime = Time.time + 0.15f;
        Refresh();
    }

    void OnRuntimeChanged()
    {
        Refresh();
    }

    void EnsureReady()
    {
        EnsureCanvas();
        EnsureRuntime();
        EnsurePanel();
        RagTrainingHudVisibility.Initialize(defaultVisible: false);
        ApplyUserHudVisibility(RagTrainingHudVisibility.IsVisible);
    }

    void EnsureRuntime()
    {
        if (runtime != null) return;
        runtime = TemporalCognitionRuntime.GetOrCreate(zoneIndex);
    }

    void EnsureCanvas()
    {
        if (targetCanvas != null) return;

        GameObject existing = GameObject.Find("AgentSkillGaugeCanvas");
        if (existing != null)
            targetCanvas = existing.GetComponent<Canvas>();

        if (targetCanvas != null) return;

        GameObject canvasGO = new GameObject("AgentSkillGaugeCanvas");
        targetCanvas = canvasGO.AddComponent<Canvas>();
        targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        targetCanvas.sortingOrder = 2000;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0f;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

        canvasGO.AddComponent<GraphicRaycaster>();
    }

    void EnsurePanel()
    {
        if (panel != null || targetCanvas == null) return;

        panel = new GameObject($"Zone{zoneIndex}TemporalBufferPanel");
        panel.transform.SetParent(targetCanvas.transform, false);

        RectTransform rect = panel.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(gaugeWidth, panelHeight);

        Image bg = panel.AddComponent<Image>();
        bg.color = backgroundColor;

        titleText = CreateText("Title", panel.transform, new Vector2(10f, -26f), new Vector2(-10f, -4f), 13, FontStyle.Bold, textColor);
        activeText = CreateText("ActiveStep", panel.transform, new Vector2(10f, -48f), new Vector2(-10f, -28f), 12, FontStyle.Bold, textColor);

        GameObject barGo = new GameObject("ActiveTimingBar");
        barGo.transform.SetParent(panel.transform, false);
        RectTransform barRect = barGo.AddComponent<RectTransform>();
        barRect.anchorMin = new Vector2(0f, 1f);
        barRect.anchorMax = new Vector2(1f, 1f);
        barRect.pivot = new Vector2(0f, 1f);
        barRect.offsetMin = new Vector2(10f, -72f);
        barRect.offsetMax = new Vector2(-10f, -52f);
        Image barBg = barGo.AddComponent<Image>();
        barBg.color = barBackgroundColor;

        GameObject fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(barGo.transform, false);
        RectTransform fillRect = fillGo.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        activeFill = fillGo.AddComponent<Image>();
        activeFill.type = Image.Type.Filled;
        activeFill.fillMethod = Image.FillMethod.Horizontal;
        activeFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        activeFill.color = activeFillColor;

        activePercentText = CreateText("ActivePercent", barGo.transform, Vector2.zero, Vector2.zero, 11, FontStyle.Bold, textColor);
        RectTransform percentRect = activePercentText.GetComponent<RectTransform>();
        percentRect.anchorMin = Vector2.zero;
        percentRect.anchorMax = Vector2.one;
        percentRect.offsetMin = Vector2.zero;
        percentRect.offsetMax = Vector2.zero;
        activePercentText.alignment = TextAnchor.MiddleCenter;

        detailText = CreateText("TimingDetail", panel.transform, new Vector2(10f, -94f), new Vector2(-10f, -74f), 11, FontStyle.Normal, mutedTextColor);
        memoryText = CreateText("MemoryDetail", panel.transform, new Vector2(10f, -122f), new Vector2(-10f, -96f), 11, FontStyle.Normal, mutedTextColor);

        RefreshPosition();
    }

    Text CreateText(string name, Transform parent, Vector2 offsetMin, Vector2 offsetMax, int fontSize, FontStyle style, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        Text text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;

        Shadow shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(1.2f, -1.2f);

        return text;
    }

    void Refresh()
    {
        EnsureReady();
        if (panel == null) return;

        RefreshPosition();

        if (runtime == null)
        {
            titleText.text = $"Zone {zoneIndex} Temporal Buffer";
            activeText.text = "Waiting for runtime";
            detailText.text = "";
            memoryText.text = "";
            if (activeFill != null) activeFill.fillAmount = 0f;
            if (activePercentText != null) activePercentText.text = "0%";
            return;
        }

        TemporalStepTimingRecord active = runtime.GetPrimaryActiveRecord();
        titleText.text = $"Zone {zoneIndex} Temporal Buffer";

        if (active != null)
        {
            float elapsed = active.ActualElapsedSec(runtime.ElapsedSceneSeconds);
            float expected = Mathf.Max(0.001f, active.expectedDurationSec);
            float ratio = Mathf.Clamp01(elapsed / expected);

            activeText.text = $"Now: {Short(active.stepId, 18)} [{RoleLabel(active)}]";
            detailText.text = $"start {FormatSeconds(active.startTimeSec)} | elapsed {FormatSeconds(elapsed)} / exp {FormatSeconds(expected)}";
            activeFill.fillAmount = ratio;
            activeFill.color = ratio >= 1f ? completedFillColor : activeFillColor;
            activePercentText.text = $"{Mathf.RoundToInt(ratio * 100f)}%";
        }
        else
        {
            activeText.text = "Now: idle";
            detailText.text = "start -- | elapsed 0.0s / exp --";
            activeFill.fillAmount = 0f;
            activeFill.color = activeFillColor;
            activePercentText.text = "0%";
        }

        List<TemporalStepTimingRecord> recent = runtime.GetRecentCompleted(1);
        string latest = recent.Count > 0
            ? $"last {Short(recent[0].stepId, 13)} [{RoleLabel(recent[0])}] {FormatSeconds(recent[0].ActualElapsedSec(runtime.ElapsedSceneSeconds))}"
            : "last none";
        memoryText.text = $"game {FormatSeconds(runtime.BufferMemory.overallGameTimeSec)} | stored {runtime.BufferMemory.CompletedRecords.Count} | {latest}";
    }

    void RefreshPosition()
    {
        if (panel == null) return;

        RectTransform rect = panel.GetComponent<RectTransform>();
        if (rect == null) return;

        int physicalRows = CountHudPhysicalGaugeRows();
        float stepRow = 0f;
        AgentSkillGaugeUI skillGaugeUi = FindObjectOfType<AgentSkillGaugeUI>();
        if (skillGaugeUi != null && skillGaugeUi.stepIndicatorOnlyMode && skillGaugeUi.showZone0StepIndicator)
            stepRow = stepIndicatorHeight + skillGaugeSpacing;

        float y = -(topPadding + (skillGaugeHeight + skillGaugeSpacing) * physicalRows + stepRow + skillGaugeSpacing);
        rect.anchoredPosition = new Vector2(leftPadding, y);
        rect.sizeDelta = new Vector2(gaugeWidth, panelHeight);
    }

    int CountHudPhysicalGaugeRows()
    {
        AgentSkillGaugeUI skillGaugeUi = FindObjectOfType<AgentSkillGaugeUI>();
        if (skillGaugeUi != null && skillGaugeUi.stepIndicatorOnlyMode)
            return 0;

        int zoneFilter = skillGaugeUi != null && skillGaugeUi.showOnlyPhysicalRagAgents
            ? skillGaugeUi.showOnlyZoneIndex
            : -1;

        int count = CountPhysicalRagAgents(zoneFilter);
        if (count > 0) return count;

        // JSONWorkflowSceneML / multiplayer embed: one zone-0 gauge row when profiles are still loading.
        return zoneFilter == 0 ? 1 : 4;
    }

    int CountPhysicalRagAgents(int zoneFilter = -1)
    {
        if (skillSystem == null)
            skillSystem = FindObjectOfType<SkillBasedActionSystem>();
        if (skillSystem == null) return zoneFilter == 0 ? 1 : 4;

        Dictionary<string, AgentProfile> profiles = skillSystem.GetAllAgentProfiles();
        if (profiles == null || profiles.Count == 0) return zoneFilter == 0 ? 1 : 4;

        int count = 0;
        foreach (var kvp in profiles)
        {
            AgentProfile profile = kvp.Value;
            if (profile == null) continue;
            if (zoneFilter >= 0 && profile.zoneIndex != zoneFilter) continue;

            string id = !string.IsNullOrEmpty(profile.agentId) ? profile.agentId : kvp.Key;
            bool physical = !string.IsNullOrEmpty(id) &&
                            id.Length >= 2 &&
                            id.StartsWith("P", StringComparison.OrdinalIgnoreCase) &&
                            char.IsDigit(id[1]);
            if (!physical && !string.IsNullOrWhiteSpace(profile.role))
                physical = profile.role.Trim().Equals("Physical", StringComparison.OrdinalIgnoreCase);
            if (physical) count++;
        }

        if (count > 0) return count;
        return zoneFilter == 0 ? 1 : 4;
    }

    static string RoleLabel(TemporalStepTimingRecord record)
    {
        if (record == null) return "";
        if (string.Equals(record.agentRole, "P", StringComparison.OrdinalIgnoreCase)) return "physical";
        if (string.Equals(record.agentRole, "M", StringComparison.OrdinalIgnoreCase)) return "mental";
        return string.IsNullOrWhiteSpace(record.targetObjectId) || !record.targetObjectId.StartsWith("cognitive_", StringComparison.OrdinalIgnoreCase)
            ? "physical"
            : "mental";
    }

    static string FormatSeconds(float seconds)
    {
        return Mathf.Max(0f, seconds).ToString("0.0") + "s";
    }

    static string Short(string value, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value)) return "--";
        return value.Length <= maxLen ? value : value.Substring(0, Mathf.Max(0, maxLen - 3)) + "...";
    }
}
