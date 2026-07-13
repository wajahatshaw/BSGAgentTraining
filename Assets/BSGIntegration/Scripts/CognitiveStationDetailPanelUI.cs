using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen-space scrollable panel that appears whenever a mental agent cognitive step is
/// dispatched (walking to station) or activated (dwelling at station). Shows only the
/// current cognitive step's data: consumes, produces, contracts, and values extracted
/// during that step (via stepLog). Clears when the step changes.
/// </summary>
[DefaultExecutionOrder(150)]
public class CognitiveStationDetailPanelUI : MonoBehaviour
{
    public int zoneIndex = 0;
    public Canvas targetCanvas;

    [Header("Layout")]
    [Tooltip("Minimum inset from the top-right corner when no HUD switch is found.")]
    public float rightPadding = 24f;
    public float topPadding = 24f;
    [Tooltip("Gap between this panel's right edge and the HUD switch's left edge.")]
    public float hudSwitchGap = 16f;
    public float panelWidth = 360f;
    public float panelHeight = 520f;

    [Header("Style")]
    public Color backgroundColor = new Color(0.04f, 0.05f, 0.08f, 0.94f);
    public Color scrollBackgroundColor = new Color(0.02f, 0.02f, 0.04f, 0.65f);
    public Color textColor = Color.white;
    public Color mutedTextColor = new Color(0.82f, 0.82f, 0.86f, 1f);
    public Color accentColor = new Color(0.35f, 0.80f, 0.95f, 1f);
    public Color approachingColor = new Color(0.95f, 0.78f, 0.35f, 1f);

    GameObject panel;
    Text titleText;
    Text subtitleText;
    RectTransform contentRect;
    ScrollRect detailScroll;
    readonly List<Text> rowTexts = new List<Text>();

    ZoneDeclarativeMemory memory;
    CognitivePhaseOrchestrator orchestrator;
    string lastRenderedSignature = string.Empty;
    string lastTrackedStepId = string.Empty;
    bool scrollToTopOnRebuild;
    float nextRefreshTime;

    public static CognitiveStationDetailPanelUI GetOrCreate(int zoneIndex)
    {
        return EnsureForZone(zoneIndex);
    }

    /// <summary>Creates the panel if needed (called from orchestrator dispatch).</summary>
    public static CognitiveStationDetailPanelUI EnsureForZone(int zoneIndex)
    {
        string objectName = $"CognitiveStationDetailPanelUI_Zone{zoneIndex}";
        GameObject existing = GameObject.Find(objectName);
        CognitiveStationDetailPanelUI ui = existing != null
            ? existing.GetComponent<CognitiveStationDetailPanelUI>()
            : null;
        if (ui == null)
        {
            GameObject go = existing ?? new GameObject(objectName);
            if (!go.activeSelf) go.SetActive(true);
            ui = go.GetComponent<CognitiveStationDetailPanelUI>();
            if (ui == null)
                ui = go.AddComponent<CognitiveStationDetailPanelUI>();
        }

        ui.zoneIndex = zoneIndex;
        ui.EnsureReady();
        ui.Refresh();
        return ui;
    }

    void Start()
    {
        EnsureReady();
        Refresh();
    }

    void OnEnable()
    {
        BindOrchestrator();
    }

    void OnDisable()
    {
        UnbindOrchestrator();
    }

    void BindOrchestrator()
    {
        UnbindOrchestrator();
        orchestrator = CognitivePhaseOrchestrator.GetOrCreateForZone(zoneIndex);
        if (orchestrator == null) return;
        orchestrator.OnCognitiveStepDispatched += OnOrchestratorStepChanged;
        orchestrator.OnStepCompleted += OnOrchestratorStepChanged;
    }

    void UnbindOrchestrator()
    {
        if (orchestrator == null) return;
        orchestrator.OnCognitiveStepDispatched -= OnOrchestratorStepChanged;
        orchestrator.OnStepCompleted -= OnOrchestratorStepChanged;
        orchestrator = null;
    }

    void OnOrchestratorStepChanged(string stepId)
    {
        lastRenderedSignature = string.Empty;
        nextRefreshTime = 0f;
        Refresh();
    }

    public void ApplyUserHudVisibility(bool show)
    {
        // Station detail panel is independent of the training HUD toggle — always refresh.
        Refresh();
    }

    void Update()
    {
        if (Time.time < nextRefreshTime) return;
        nextRefreshTime = Time.time + 0.12f;
        Refresh();
    }

    void EnsureReady()
    {
        EnsureCanvas();
        EnsureMemory();
        EnsurePanel();
        if (orchestrator == null)
            BindOrchestrator();
    }

    void EnsureMemory()
    {
        ZoneDeclarativeMemory reg = ZoneDeclarativeMemory.ForZone(zoneIndex);
        if (reg == null)
        {
            ZoneDeclarativeMemory.RebuildRegistryFromScene();
            reg = ZoneDeclarativeMemory.ForZone(zoneIndex);
        }
        memory = reg;
    }

    void EnsureCanvas()
    {
        if (targetCanvas != null) return;

        GameObject existing = GameObject.Find("AgentSkillGaugeCanvas");
        if (existing != null)
        {
            targetCanvas = existing.GetComponent<Canvas>();
            if (targetCanvas != null && !existing.activeSelf)
                existing.SetActive(true);
        }

        if (targetCanvas != null)
        {
            if (targetCanvas.sortingOrder < 2100)
                targetCanvas.sortingOrder = 2100;
            return;
        }

        GameObject canvasGO = new GameObject("AgentSkillGaugeCanvas");
        targetCanvas = canvasGO.AddComponent<Canvas>();
        targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        targetCanvas.sortingOrder = 2100;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0f;

        canvasGO.AddComponent<GraphicRaycaster>();
    }

    void EnsurePanel()
    {
        if (panel != null && detailScroll == null)
        {
            UnityEngine.Object.Destroy(panel);
            panel = null;
            titleText = null;
            subtitleText = null;
            contentRect = null;
            rowTexts.Clear();
        }

        if (panel != null || targetCanvas == null) return;

        panel = new GameObject($"Zone{zoneIndex}CognitiveStationDetailPanel");
        panel.transform.SetParent(targetCanvas.transform, false);

        RectTransform rect = panel.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.sizeDelta = new Vector2(panelWidth, panelHeight);
        RefreshPanelPosition();

        Image bg = panel.AddComponent<Image>();
        bg.color = backgroundColor;

        titleText = CreateText("Title", panel.transform, new Vector2(10f, -26f), new Vector2(-10f, -6f),
            13, FontStyle.Bold, accentColor);
        subtitleText = CreateText("Subtitle", panel.transform, new Vector2(10f, -48f), new Vector2(-10f, -28f),
            11, FontStyle.Normal, mutedTextColor);

        BuildScrollList();
    }

    /// <summary>Keep the panel to the left of the HUD switch (top-right controls).</summary>
    void RefreshPanelPosition()
    {
        if (panel == null) return;

        RectTransform rect = panel.GetComponent<RectTransform>();
        if (rect == null) return;

        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.sizeDelta = new Vector2(panelWidth, panelHeight);

        float insetRight = rightPadding;
        float insetTop = topPadding;

        GameObject switchGo = GameObject.Find(RagTrainingHudSwitchController.SwitchObjectName);
        if (switchGo != null)
        {
            RectTransform switchRect = switchGo.GetComponent<RectTransform>();
            if (switchRect != null)
            {
                Vector3[] corners = new Vector3[4];
                switchRect.GetWorldCorners(corners);
                float switchLeftScreenX = corners[0].x;

                Canvas canvas = targetCanvas != null ? targetCanvas : panel.GetComponentInParent<Canvas>();
                float scale = 1f;
                if (canvas != null)
                {
                    CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
                    if (scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
                        scale = Screen.width / scaler.referenceResolution.x;
                }

                float panelRightScreenX = switchLeftScreenX - hudSwitchGap;
                insetRight = Mathf.Max(rightPadding, (Screen.width - panelRightScreenX) / Mathf.Max(0.01f, scale));
                insetTop = Mathf.Max(topPadding, topPadding + 8f);
            }
        }
        else
        {
            // Default clearance for chat/settings row when switch is absent.
            insetRight = Mathf.Max(rightPadding, 100f);
            insetTop = Mathf.Max(topPadding, 96f);
        }

        rect.anchoredPosition = new Vector2(-insetRight, -insetTop);
    }

    void BuildScrollList()
    {
        GameObject scrollGo = new GameObject("DetailScroll");
        scrollGo.transform.SetParent(panel.transform, false);
        RectTransform scrollRect = scrollGo.AddComponent<RectTransform>();
        scrollRect.anchorMin = new Vector2(0f, 0f);
        scrollRect.anchorMax = new Vector2(1f, 1f);
        scrollRect.offsetMin = new Vector2(8f, 8f);
        scrollRect.offsetMax = new Vector2(-22f, -58f);

        Image scrollBg = scrollGo.AddComponent<Image>();
        scrollBg.color = scrollBackgroundColor;

        detailScroll = scrollGo.AddComponent<ScrollRect>();
        detailScroll.horizontal = false;
        detailScroll.vertical = true;
        detailScroll.movementType = ScrollRect.MovementType.Clamped;
        detailScroll.scrollSensitivity = 24f;
        detailScroll.inertia = true;

        GameObject scrollbarGo = new GameObject("Scrollbar");
        scrollbarGo.transform.SetParent(scrollGo.transform, false);
        RectTransform sbRect = scrollbarGo.AddComponent<RectTransform>();
        sbRect.anchorMin = new Vector2(1f, 0f);
        sbRect.anchorMax = new Vector2(1f, 1f);
        sbRect.pivot = new Vector2(1f, 1f);
        sbRect.sizeDelta = new Vector2(12f, 0f);
        sbRect.anchoredPosition = Vector2.zero;

        Image sbTrack = scrollbarGo.AddComponent<Image>();
        sbTrack.color = new Color(1f, 1f, 1f, 0.08f);

        Scrollbar scrollbar = scrollbarGo.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;

        GameObject handleGo = new GameObject("Handle");
        handleGo.transform.SetParent(scrollbarGo.transform, false);
        RectTransform handleRect = handleGo.AddComponent<RectTransform>();
        handleRect.anchorMin = Vector2.zero;
        handleRect.anchorMax = Vector2.one;
        handleRect.offsetMin = new Vector2(2f, 2f);
        handleRect.offsetMax = new Vector2(-2f, -2f);
        Image handleImg = handleGo.AddComponent<Image>();
        handleImg.color = new Color(0.75f, 0.78f, 0.82f, 0.85f);
        scrollbar.handleRect = handleRect;
        scrollbar.targetGraphic = handleImg;

        detailScroll.verticalScrollbar = scrollbar;
        detailScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

        GameObject viewportGo = new GameObject("Viewport");
        viewportGo.transform.SetParent(scrollGo.transform, false);
        RectTransform viewportRect = viewportGo.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;
        viewportRect.pivot = new Vector2(0f, 1f);
        viewportGo.AddComponent<RectMask2D>();

        GameObject contentGo = new GameObject("Content");
        contentGo.transform.SetParent(viewportGo.transform, false);
        contentRect = contentGo.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;

        VerticalLayoutGroup layout = contentGo.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(6, 6, 4, 4);
        layout.spacing = 2f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        detailScroll.viewport = viewportRect;
        detailScroll.content = contentRect;
    }

    Text CreateText(string name, Transform parent, Vector2 offsetMin, Vector2 offsetMax,
        int fontSize, FontStyle style, Color color)
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
        return text;
    }

    Text GetOrCreateRow(int index)
    {
        if (index < rowTexts.Count)
            return rowTexts[index];

        GameObject go = new GameObject($"Row{index}");
        go.transform.SetParent(contentRect, false);

        Text text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 11;
        text.fontStyle = FontStyle.Normal;
        text.color = mutedTextColor;
        text.alignment = TextAnchor.UpperLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.resizeTextForBestFit = false;

        ContentSizeFitter fitter = go.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        LayoutElement le = go.AddComponent<LayoutElement>();
        le.minHeight = 16f;
        le.flexibleWidth = 1f;

        rowTexts.Add(text);
        return text;
    }

    void Refresh()
    {
        EnsureReady();

        if (orchestrator == null)
            BindOrchestrator();
        if (orchestrator == null)
            orchestrator = CognitivePhaseOrchestrator.GetOrCreateForZone(zoneIndex);

        ActionSequenceStep current = FindCurrentCognitiveStep(orchestrator, zoneIndex);
        bool show = current != null;

        EnsurePanel();
        if (panel == null || titleText == null || subtitleText == null) return;

        RefreshPanelPosition();
        panel.SetActive(show);

        if (!show)
        {
            if (!string.IsNullOrEmpty(lastTrackedStepId))
            {
                lastTrackedStepId = string.Empty;
                lastRenderedSignature = string.Empty;
            }
            return;
        }

        string phaseLabel = current.isActivated ? "active" : "approaching";
        titleText.text = current.isActivated
            ? $"▶ {NA(current.targetObjectName)}"
            : $"→ {NA(current.targetObjectName)}";
        titleText.color = current.isActivated ? accentColor : approachingColor;
        subtitleText.text = $"{ShortStepId(current.stepId)} · {NA(current.currentCognitiveState)} · {phaseLabel}";

        if (!string.Equals(current.stepId, lastTrackedStepId, StringComparison.Ordinal))
        {
            lastTrackedStepId = current.stepId;
            lastRenderedSignature = string.Empty;
            scrollToTopOnRebuild = true;
        }

        string signature = BuildLiveSignature(current);
        if (signature == lastRenderedSignature) return;
        lastRenderedSignature = signature;

        RefreshLiveRows(current);
    }

    struct StepWrite
    {
        public string key;
        public string value;
    }

    string BuildLiveSignature(ActionSequenceStep step)
    {
        if (step == null) return string.Empty;

        var sb = new StringBuilder(512);
        sb.Append(step.stepId).Append('|');
        sb.Append(step.isActivated).Append('|');
        sb.Append(step.isStepCompleted).Append('|');
        sb.Append(orchestrator != null && orchestrator.IsStepActive(step.stepId)).Append('|');

        List<StepWrite> writes = CollectStepWrites(step.stepId);
        for (int i = 0; i < writes.Count; i++)
            sb.Append(writes[i].key).Append('=').Append(writes[i].value).Append(';');

        if (!string.IsNullOrWhiteSpace(step.producesPayload) && memory != null
            && memory.TryGetPayload(step.producesPayload, out string produced))
            sb.Append('|').Append(step.producesPayload).Append('=').Append(produced);

        if (TemporalCognitionRuntime.TryGetForZone(zoneIndex, out TemporalCognitionRuntime temporal)
            && temporal.BufferMemory.TryGetActive(step.stepId, out TemporalStepTimingRecord timing))
        {
            sb.Append('|').Append(timing.action).Append('|').Append(timing.ActualElapsedSec(Time.time));
        }

        return sb.ToString();
    }

    void RefreshLiveRows(ActionSequenceStep step)
    {
        DeclarativeStepRecord record = DeclarativeStepRecord.Build(step, memory);
        List<StepWrite> stepWrites = CollectStepWrites(step.stepId);
        int row = 0;
        int stationNum = DeclarativeStepRecord.CognitiveNumber(step.targetObjectId);

        row = SetRow(row, $"── {phasePrefix(step)} · {NA(record.stationName)} ({NA(record.stationId)}) ──",
            step.isActivated ? accentColor : approachingColor);
        row = SetRow(row, $"step {ShortStepId(record.stepId)} · order {record.stepOrder} · type {NA(record.actionType)} · dur {record.expectedDurationSec:0.##}s", textColor);
        row = SetRow(row, $"state {NA(record.stationState)} · kind {record.stationKind} · subtask {NA(record.subTaskId)}", mutedTextColor);
        if (!string.IsNullOrWhiteSpace(record.description))
            row = SetRow(row, $"desc {record.description}", mutedTextColor);
        row = SetRow(row, "", mutedTextColor);

        row = SetRow(row, "── consume (this step) ──", textColor);
        row = AppendPayloadRows(ref row, record.consumesPayload, required: true);

        row = SetRow(row, "── produce (this step) ──", textColor);
        row = AppendProduceRow(ref row, record, stepWrites);

        row = AppendConnectionRows(ref row, step);
        row = AppendStationDefinitionRows(ref row, step, record, stationNum);
        row = AppendStepContractRows(ref row, step);
        row = AppendStepExtractedRows(ref row, record, stepWrites);
        row = AppendTemporalRows(ref row, step);

        for (int i = row; i < rowTexts.Count; i++)
        {
            if (rowTexts[i].gameObject.activeSelf)
                rowTexts[i].gameObject.SetActive(false);
        }

        RebuildScrollContent(scrollToTopOnRebuild);
        scrollToTopOnRebuild = false;
    }

    void RebuildScrollContent(bool resetToTop)
    {
        if (contentRect == null) return;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);

        if (detailScroll != null && resetToTop)
            detailScroll.verticalNormalizedPosition = 1f;
    }

    /// <summary>All key/value pairs written to the blackboard for this stepId only.</summary>
    List<StepWrite> CollectStepWrites(string stepId)
    {
        var writes = new List<StepWrite>(16);
        if (memory == null || string.IsNullOrWhiteSpace(stepId))
            return writes;

        const string arrow = " → ";
        IReadOnlyList<string> log = memory.StepLog;
        for (int i = 0; i < log.Count; i++)
        {
            string line = log[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.IndexOf("STEP DONE", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (line.IndexOf("+REWARD", StringComparison.Ordinal) >= 0) continue;

            int arrowIdx = line.IndexOf(arrow, StringComparison.Ordinal);
            if (arrowIdx < 0) continue;

            // Require exact stepId immediately before the arrow: "... t01_cog_s02 → key=value"
            string beforeArrow = line.Substring(0, arrowIdx).TrimEnd();
            if (!beforeArrow.EndsWith(stepId, StringComparison.OrdinalIgnoreCase)) continue;

            string payload = line.Substring(arrowIdx + arrow.Length);
            int eq = payload.IndexOf('=');
            if (eq <= 0) continue;

            string key = payload.Substring(0, eq).Trim();
            string value = payload.Substring(eq + 1).Trim();
            if (string.IsNullOrEmpty(key)) continue;

            AppendUniqueWrite(writes, key, value);
        }

        string prefix = stepId + "_";
        List<KeyValuePair<string, string>> slots = memory.GetDataSlotsSnapshot();
        for (int i = 0; i < slots.Count; i++)
        {
            string key = slots[i].Key;
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            string shortKey = key.Substring(prefix.Length);
            AppendUniqueWrite(writes, shortKey, slots[i].Value);
        }

        return writes;
    }

    static void AppendUniqueWrite(List<StepWrite> writes, string key, string value)
    {
        for (int i = 0; i < writes.Count; i++)
        {
            if (string.Equals(writes[i].key, key, StringComparison.OrdinalIgnoreCase))
            {
                writes[i] = new StepWrite { key = key, value = value };
                return;
            }
        }
        writes.Add(new StepWrite { key = key, value = value });
    }

    int AppendStepExtractedRows(ref int row, DeclarativeStepRecord record, List<StepWrite> stepWrites)
    {
        row = SetRow(row, "", mutedTextColor);
        row = SetRow(row, "── extracted this step ──", textColor);

        if (stepWrites.Count == 0)
        {
            row = SetRow(row, "(no data written yet — waiting for dwell/extraction)", mutedTextColor);
        }
        else
        {
            for (int i = 0; i < stepWrites.Count; i++)
                row = SetRow(row, $"{stepWrites[i].key} = {stepWrites[i].value}", mutedTextColor);
        }

        return row;
    }

    static string phasePrefix(ActionSequenceStep step) =>
        step != null && step.isActivated ? "LIVE" : "EN ROUTE";

    int AppendProduceRow(ref int row, DeclarativeStepRecord record, List<StepWrite> stepWrites)
    {
        if (string.IsNullOrWhiteSpace(record.producesPayload))
            return SetRow(row, "(none)", mutedTextColor);

        string value = null;
        if (stepWrites != null)
        {
            for (int i = 0; i < stepWrites.Count; i++)
            {
                if (string.Equals(stepWrites[i].key, record.producesPayload, StringComparison.OrdinalIgnoreCase))
                {
                    value = stepWrites[i].value;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(value))
            value = record.producedValue;

        row = SetRow(row, $"→ {record.producesPayload} = {NA(value)}", mutedTextColor);
        return row;
    }

    /// <summary>Step definition preview from JSON — not cumulative blackboard state.</summary>
    int AppendStationDefinitionRows(ref int row, ActionSequenceStep step, DeclarativeStepRecord record, int stationNum)
    {
        switch (stationNum)
        {
            case 1: return AppendIntentionalDefinition(ref row, step, record);
            case 2: return AppendDeclarativeDefinition(ref row, step, record);
            case 3: return AppendProductionMemoryDefinition(ref row, step, record);
            case 4: return AppendVisualModuleDefinition(ref row, step, record);
            case 5: return AppendManualModuleDefinition(ref row, step, record);
            case 6: return AppendTemporalModuleDefinition(ref row, step, record);
            case 8: return AppendGoalBufferDefinition(ref row, step, record);
            case 9: return AppendImaginalDefinition(ref row, step, record);
            case 10: return AppendRetrievalDefinition(ref row, step, record);
            case 11: return AppendVisualBufferDefinition(ref row, record);
            case 12: return AppendVisualLocationDefinition(ref row, step, record);
            case 13: return AppendManualBufferDefinition(ref row, step, record);
            case 14: return AppendTemporalBufferDefinition(ref row, step, record);
            default: return row;
        }
    }

    int AppendIntentionalDefinition(ref int row, ActionSequenceStep step, DeclarativeStepRecord record)
    {
        row = SetRow(row, "", mutedTextColor);
        row = SetRow(row, "── intentional module (step def) ──", textColor);
        if (record?.station?.intentional != null)
        {
            row = SetRow(row, $"desire = {NA(record.station.intentional.desire)}", mutedTextColor);
            row = SetRow(row, $"intent_action = {NA(record.station.intentional.intentAction)}", mutedTextColor);
        }
        return row;
    }

    int AppendDeclarativeDefinition(ref int row, ActionSequenceStep step, DeclarativeStepRecord record)
    {
        row = SetRow(row, "", mutedTextColor);
        row = SetRow(row, "── declarative module (step def) ──", textColor);
        if (record?.station?.declarative != null)
            row = SetRow(row, $"source = {NA(record.station.declarative.source)}", mutedTextColor);
        return row;
    }

    int AppendProductionMemoryDefinition(ref int row, ActionSequenceStep step, DeclarativeStepRecord record)
    {
        row = SetRow(row, "", mutedTextColor);
        row = SetRow(row, "── production memory (step def) ──", textColor);
        ProductionMemoryCommandUI.ClassifyCommand(step, out string label, out _);
        row = SetRow(row, $"command_type {label}", mutedTextColor);
        if (record?.station?.production != null)
        {
            row = SetRow(row, $"command = {NA(record.station.production.command)}", mutedTextColor);
            row = SetRow(row, $"target_buffer = {NA(record.station.production.targetBuffer)}", mutedTextColor);
        }
        return row;
    }

    int AppendVisualModuleDefinition(ref int row, ActionSequenceStep step, DeclarativeStepRecord record)
    {
        row = SetRow(row, "", mutedTextColor);
        row = SetRow(row, "── visual module (step def) ──", textColor);
        if (record?.station?.visualModule != null)
        {
            row = SetRow(row, $"entity = {NA(record.station.visualModule.entity)}", mutedTextColor);
            row = SetRow(row, $"scanning_mode = {NA(record.station.visualModule.scanningMode)}", mutedTextColor);
        }
        return row;
    }

    int AppendManualModuleDefinition(ref int row, ActionSequenceStep step, DeclarativeStepRecord record)
    {
        row = SetRow(row, "", mutedTextColor);
        row = SetRow(row, "── manual module (step def) ──", textColor);
        if (record?.station?.manualModule != null)
        {
            row = SetRow(row, $"target = {NA(record.station.manualModule.target)}", mutedTextColor);
            row = SetRow(row, $"effector = {NA(record.station.manualModule.effector)}", mutedTextColor);
        }
        return row;
    }

    int AppendTemporalModuleDefinition(ref int row, ActionSequenceStep step, DeclarativeStepRecord record)
    {
        row = SetRow(row, "", mutedTextColor);
        row = SetRow(row, "── temporal module (step def) ──", textColor);
        if (record?.station?.temporalModule != null)
        {
            var t = record.station.temporalModule;
            row = SetRow(row, $"elapsed_ms = {t.elapsedMs} · remaining_ms = {t.remainingMs}", mutedTextColor);
            row = SetRow(row, $"time_block = {NA(t.timeBlock)}", mutedTextColor);
        }
        return row;
    }

    int AppendGoalBufferDefinition(ref int row, ActionSequenceStep step, DeclarativeStepRecord record)
    {
        row = SetRow(row, "", mutedTextColor);
        row = SetRow(row, "── goal buffer (step def) ──", textColor);

        GoalBufferContract contract = step?.goalBufferContract;
        if (contract != null)
        {
            if (contract.IsTripleModeContract())
            {
                row = SetRow(row, "mode triple (bottom/middle/top)", mutedTextColor);
                AppendFieldIfSet(ref row, "bottom", contract.initialStateBottom);
                AppendFieldIfSet(ref row, "middle", contract.initialStateMiddle);
                AppendFieldIfSet(ref row, "top", contract.initialStateTop);
            }
            else if (contract.HasStackLayers())
            {
                row = SetRow(row, $"mode stack ({contract.stack.Length} layer(s))", mutedTextColor);
                for (int i = 0; i < contract.stack.Length; i++)
                {
                    GoalBufferStackLayer layer = contract.stack[i];
                    if (layer == null) continue;
                    row = SetRow(row,
                        $"  [{layer.position}/{layer.type}] {NA(layer.value)}",
                        mutedTextColor);
                }
            }
        }

        if (step.resolvedGoalBufferDesireLevel > 0f)
            row = SetRow(row, $"resolved_desire = {step.resolvedGoalBufferDesireLevel:0.##} ({NA(step.resolvedGoalBufferDesireSource)})", mutedTextColor);

        return row;
    }

    int AppendImaginalDefinition(ref int row, ActionSequenceStep step, DeclarativeStepRecord record)
    {
        row = SetRow(row, "", mutedTextColor);
        row = SetRow(row, "── imaginal buffer (step def) ──", textColor);
        row = SetRow(row, $"state {NA(record.imaginalStateBefore)} → {NA(record.imaginalStateAfter)}", mutedTextColor);
        if (!string.IsNullOrWhiteSpace(step.imaginalThoughtText))
            row = SetRow(row, $"thought = {step.imaginalThoughtText}", mutedTextColor);
        if (!string.IsNullOrWhiteSpace(record.imaginationStatement))
            row = SetRow(row, $"imagination = {record.imaginationStatement}", mutedTextColor);
        return row;
    }

    int AppendRetrievalDefinition(ref int row, ActionSequenceStep step, DeclarativeStepRecord record)
    {
        row = SetRow(row, "", mutedTextColor);
        row = SetRow(row, "── retrieval buffer (step def) ──", textColor);
        if (record?.station?.retrieval != null)
        {
            row = SetRow(row, $"schema = {NA(record.station.retrieval.schema)}", mutedTextColor);
            row = SetRow(row, $"retrieval_cue = {NA(record.station.retrieval.retrievalCue)}", mutedTextColor);
        }
        return row;
    }

    int AppendVisualBufferDefinition(ref int row, DeclarativeStepRecord record)
    {
        row = SetRow(row, "", mutedTextColor);
        row = SetRow(row, "── visual buffer (step def) ──", textColor);
        if (record?.station?.visualBuffer != null)
        {
            var v = record.station.visualBuffer;
            row = SetRow(row, $"entity = {NA(v.entity)}", mutedTextColor);
            row = SetRow(row, $"state {NA(v.entityStateBefore)} → {NA(v.entityStateAfter)}", mutedTextColor);
        }
        return row;
    }

    int AppendVisualLocationDefinition(ref int row, ActionSequenceStep step, DeclarativeStepRecord record)
    {
        row = SetRow(row, "", mutedTextColor);
        row = SetRow(row, "── visual location (step def) ──", textColor);
        if (record?.station?.visualLocation != null)
        {
            var v = record.station.visualLocation;
            row = SetRow(row, $"target = {NA(v.target)}", mutedTextColor);
            row = SetRow(row, $"pos = ({v.x:0.##}, {v.y:0.##}, {v.z:0.##})", mutedTextColor);
        }
        return row;
    }

    int AppendManualBufferDefinition(ref int row, ActionSequenceStep step, DeclarativeStepRecord record)
    {
        row = SetRow(row, "", mutedTextColor);
        row = SetRow(row, "── manual buffer (step def) ──", textColor);
        if (record?.station?.manualBuffer != null)
        {
            var m = record.station.manualBuffer;
            row = SetRow(row, $"command = {NA(m.command)} · target = {NA(m.target)}", mutedTextColor);
        }
        return row;
    }

    int AppendTemporalBufferDefinition(ref int row, ActionSequenceStep step, DeclarativeStepRecord record)
    {
        row = SetRow(row, "", mutedTextColor);
        row = SetRow(row, "── temporal buffer (step def) ──", textColor);
        if (record?.station?.temporalBuffer != null)
        {
            var t = record.station.temporalBuffer;
            row = SetRow(row, $"action = {NA(t.action)} · sequencing = {NA(t.sequencing)}", mutedTextColor);
        }
        return row;
    }

    void AppendFieldIfSet(ref int row, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        row = SetRow(row, $"{key} = {value}", mutedTextColor);
    }

    int AppendStepContractRows(ref int row, ActionSequenceStep step)
    {
        if (step?.contractJsonByName == null || step.contractJsonByName.Count == 0)
            return AppendSingleContractFallback(ref row, step);

        row = SetRow(row, "", mutedTextColor);
        row = SetRow(row, "── station contract (this step) ──", textColor);

        foreach (KeyValuePair<string, string> kv in step.contractJsonByName)
        {
            if (string.IsNullOrWhiteSpace(kv.Value)) continue;
            row = SetRow(row, $"[{kv.Key}]", textColor);
            row = AppendPrettyJsonLines(ref row, kv.Value, "    ", maxLines: 512);
        }

        return row;
    }

    int AppendConnectionRows(ref int row, ActionSequenceStep step)
    {
        if (step == null) return row;

        bool any = HasConnections(step);
        if (!any) return row;

        row = SetRow(row, "", mutedTextColor);
        row = SetRow(row, "── module/buffer connections ──", textColor);

        if (step.productionMemoryConnections != null && step.productionMemoryConnections.Length > 0)
            row = SetRow(row, $"production_memory → {string.Join(", ", step.productionMemoryConnections)}", mutedTextColor);
        if (step.visualModuleConnections != null && step.visualModuleConnections.Length > 0)
            row = SetRow(row, $"visual_module → {string.Join(", ", step.visualModuleConnections)}", mutedTextColor);
        if (step.manualModuleConnections != null && step.manualModuleConnections.Length > 0)
            row = SetRow(row, $"manual_module → {string.Join(", ", step.manualModuleConnections)}", mutedTextColor);
        if (step.imaginalBufferConnections != null && step.imaginalBufferConnections.Length > 0)
            row = SetRow(row, $"imaginal_buffer → {string.Join(", ", step.imaginalBufferConnections)}", mutedTextColor);
        if (step.goalBufferContract?.connections != null && step.goalBufferContract.connections.Length > 0)
            row = SetRow(row, $"goal_buffer → {string.Join(", ", step.goalBufferContract.connections)}", mutedTextColor);

        return row;
    }

    static bool HasConnections(ActionSequenceStep step)
    {
        return (step.productionMemoryConnections != null && step.productionMemoryConnections.Length > 0)
               || (step.visualModuleConnections != null && step.visualModuleConnections.Length > 0)
               || (step.manualModuleConnections != null && step.manualModuleConnections.Length > 0)
               || (step.imaginalBufferConnections != null && step.imaginalBufferConnections.Length > 0)
               || (step.goalBufferContract?.connections != null && step.goalBufferContract.connections.Length > 0);
    }

    int AppendSingleContractFallback(ref int row, ActionSequenceStep step)
    {
        string json = DeclarativeStepRecord.Build(step, memory)?.stationContractJson;
        if (string.IsNullOrWhiteSpace(json)) return row;

        row = SetRow(row, "", mutedTextColor);
        row = SetRow(row, "── station contract (this step) ──", textColor);
        return AppendPrettyJsonLines(ref row, json, "    ", maxLines: 512);
    }
    int AppendPrettyJsonLines(ref int row, string json, string prefix, int maxLines)
    {
        string pretty = JsonPrettyPrinter.TryFormat(json);
        if (string.IsNullOrEmpty(pretty))
        {
            row = SetRow(row, prefix + Short(json, 120), mutedTextColor);
            return row;
        }

        string[] lines = pretty.Split('\n');
        int limit = Mathf.Min(lines.Length, maxLines);
        for (int i = 0; i < limit; i++)
            row = SetJsonRow(row, prefix + lines[i], mutedTextColor);
        if (lines.Length > maxLines)
            row = SetRow(row, $"{prefix}… ({lines.Length - maxLines} more lines)", mutedTextColor);
        return row;
    }

    int AppendTemporalRows(ref int row, ActionSequenceStep step)
    {
        if (!TemporalCognitionRuntime.TryGetForZone(zoneIndex, out TemporalCognitionRuntime temporal))
            return row;

        if (!temporal.BufferMemory.TryGetActive(step.stepId, out TemporalStepTimingRecord timing))
            return row;

        row = SetRow(row, "", mutedTextColor);
        row = SetRow(row, "── temporal timing (this step) ──", textColor);
        row = SetRow(row, $"action {NA(timing.action)} · seq {NA(timing.sequencing)} · mode {NA(timing.executionMode)}", mutedTextColor);
        row = SetRow(row, $"elapsed {timing.ActualElapsedSec(Time.time):0.##}s · block {NA(timing.timeBlock)} · parallel {NA(timing.parallelGroupId)}", mutedTextColor);
        row = SetRow(row, $"expected {timing.expectedDurationSec:0.##}s · start {timing.startTimeSec:0.##}s", mutedTextColor);
        return row;
    }

    int AppendPayloadRows(ref int row, string[] keys, bool required)
    {
        if (keys == null || keys.Length == 0)
            return SetRow(row, required ? "(none required)" : "(none)", mutedTextColor);

        bool any = false;
        for (int i = 0; i < keys.Length; i++)
        {
            string key = keys[i];
            if (string.IsNullOrWhiteSpace(key)) continue;
            any = true;

            string value = "<missing>";
            if (memory != null && memory.TryGetPayload(key, out string live))
                value = live;
            row = SetRow(row, $"← {key} = {value}", mutedTextColor);
        }

        if (!any)
            row = SetRow(row, required ? "(none required)" : "(none)", mutedTextColor);
        return row;
    }

    /// <summary>
    /// Returns the current cognitive step: prefer activated (dwelling), else dispatched (en route),
    /// with fallbacks through the orchestrator registry and mental mover.
    /// </summary>
    static ActionSequenceStep FindCurrentCognitiveStep(CognitivePhaseOrchestrator orch, int forZoneIndex)
    {
        ActionSequenceStep fromOrch = FindFromOrchestrator(orch);
        if (fromOrch != null)
            return fromOrch;

        if (RagSequenceAgentMover.TryGetActiveMentalStepForZone(forZoneIndex, out ActionSequenceStep fromMover))
            return fromMover;

        return null;
    }

    static ActionSequenceStep FindFromOrchestrator(CognitivePhaseOrchestrator orch)
    {
        if (orch == null) return null;

        ActionSequenceStep quick = orch.GetFirstActiveCognitiveStep();
        if (quick != null && !quick.isStepCompleted)
            return quick;

        List<ActionSequenceStep> steps = orch.GetOrderedStepsSnapshot();
        ActionSequenceStep enRoute = null;

        for (int i = 0; i < steps.Count; i++)
        {
            ActionSequenceStep s = steps[i];
            if (s == null || s.isStepCompleted) continue;
            if (!IsCognitiveStep(s)) continue;

            bool dispatched = orch.IsStepActive(s.stepId);
            if (!dispatched && !s.isActivated) continue;

            if (s.isActivated)
                return s;

            if (enRoute == null)
                enRoute = s;
        }

        return enRoute;
    }

    static bool IsCognitiveStep(ActionSequenceStep step) =>
        RagStepRoleClassifier.IsMentalAgentStep(step);

    int SetRow(int idx, string text, Color color)
    {
        Text t = GetOrCreateRow(idx);
        if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
        t.color = color;
        t.text = text;
        t.fontSize = 11;
        t.fontStyle = FontStyle.Normal;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.alignment = TextAnchor.UpperLeft;

        ContentSizeFitter fitter = t.GetComponent<ContentSizeFitter>();
        if (fitter != null)
        {
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        LayoutElement le = t.GetComponent<LayoutElement>();
        if (le != null)
        {
            le.minHeight = 16f;
            le.preferredHeight = -1f;
            le.flexibleWidth = 1f;
        }

        return idx + 1;
    }

    int SetJsonRow(int idx, string text, Color color)
    {
        Text t = GetOrCreateRow(idx);
        if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
        t.color = color;
        t.text = text;
        t.fontSize = 10;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.alignment = TextAnchor.UpperLeft;

        ContentSizeFitter fitter = t.GetComponent<ContentSizeFitter>();
        if (fitter != null)
        {
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        LayoutElement le = t.GetComponent<LayoutElement>();
        if (le != null)
        {
            le.minHeight = 14f;
            le.preferredHeight = -1f;
            le.flexibleWidth = 1f;
        }

        return idx + 1;
    }

    static string NA(string s) => string.IsNullOrWhiteSpace(s) ? "—" : s;

    static string JoinDeps(string[] deps)
    {
        if (deps == null || deps.Length == 0) return "—";
        var parts = new List<string>(deps.Length);
        for (int i = 0; i < deps.Length; i++) parts.Add(ShortStepId(deps[i]));
        return string.Join(",", parts);
    }

    static string ShortStepId(string stepId)
    {
        if (string.IsNullOrWhiteSpace(stepId)) return "--";
        int us = stepId.IndexOf('_');
        if (us > 0 && us < stepId.Length - 1 && stepId[0] == 't')
            return stepId.Substring(us + 1);
        return stepId;
    }

    static string Short(string value, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value)) return "--";
        return value.Length <= maxLen ? value : value.Substring(0, Mathf.Max(0, maxLen - 3)) + "...";
    }
}
