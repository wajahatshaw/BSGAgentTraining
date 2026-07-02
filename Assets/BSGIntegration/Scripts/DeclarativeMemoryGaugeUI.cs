using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen-space Declarative Memory readout, styled like the Temporal Buffer gauge and
/// stacked directly beneath it. Shows a compact summary (completed step count, cognitive
/// reward, latest goal/motor output) plus a live-scrolling list of every key/value stored
/// in <see cref="ZoneDeclarativeMemory"/> — covering both cognitive outputs and physical
/// (motor / observation) frames. Read-only: the memory is written live by the cognitive /
/// physical pipeline; this panel only reflects it. Toggled by the same HUD switch.
/// </summary>
public class DeclarativeMemoryGaugeUI : MonoBehaviour
{
    public int zoneIndex = 0;
    public Canvas targetCanvas;

    [Header("Layout")]
    public float leftPadding = 20f;
    public float topPadding = 20f;
    public float gaugeWidth = 300f;
    public float panelHeight = 360f;
    public float headerHeight = 136f;
    public float skillGaugeHeight = 34f;
    public float skillGaugeSpacing = 8f;
    public float stepIndicatorHeight = 34f;
    public float temporalPanelHeight = 126f;
    public float panelStackSpacing = 10f;

    [Header("Style")]
    public Color backgroundColor = new Color(0.04f, 0.05f, 0.05f, 0.94f);
    public Color barBackgroundColor = new Color(0.18f, 0.18f, 0.20f, 1f);
    public Color fillColor = new Color(0.35f, 0.80f, 0.55f, 1f);
    public Color fullFillColor = new Color(0.25f, 0.72f, 1f, 1f);
    public Color scrollBackgroundColor = new Color(0.02f, 0.02f, 0.03f, 0.6f);
    public Color textColor = Color.white;
    public Color mutedTextColor = new Color(0.82f, 0.82f, 0.86f, 1f);

    GameObject panel;
    Text titleText;
    Text summaryText;
    Text currentStepText;
    Text currentDetailText;
    Text manualBufferText;
    Image fillImage;
    Text percentText;
    RectTransform contentRect;
    readonly List<Text> rowTexts = new List<Text>();
    ZoneDeclarativeMemory memory;
    SkillBasedActionSystem skillSystem;
    float nextRefreshTime;
    string lastRenderedListSignature = string.Empty;

    public static DeclarativeMemoryGaugeUI GetOrCreate(int zoneIndex)
    {
        string objectName = $"DeclarativeMemoryGaugeUI_Zone{zoneIndex}";
        GameObject existing = GameObject.Find(objectName);
        DeclarativeMemoryGaugeUI ui = existing != null ? existing.GetComponent<DeclarativeMemoryGaugeUI>() : null;
        if (ui == null)
        {
            GameObject go = existing ?? new GameObject(objectName);
            ui = go.AddComponent<DeclarativeMemoryGaugeUI>();
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

    /// <summary>Show or hide the declarative memory panel (controlled by the HUD switch).</summary>
    public void ApplyUserHudVisibility(bool show)
    {
        EnsurePanel();
        if (panel != null)
            panel.SetActive(show);
    }

    void Update()
    {
        if (Time.time < nextRefreshTime) return;
        nextRefreshTime = Time.time + 0.2f;
        Refresh();
    }

    void EnsureReady()
    {
        EnsureCanvas();
        EnsureMemory();
        EnsurePanel();
        RagTrainingHudVisibility.Initialize(defaultVisible: false);
        ApplyUserHudVisibility(RagTrainingHudVisibility.IsVisible);
    }

    void EnsureMemory()
    {
        // Always track the registry's authoritative instance so the panel reads exactly what the
        // writers write to (ForZone is a cheap dictionary lookup).
        ZoneDeclarativeMemory reg = ZoneDeclarativeMemory.ForZone(zoneIndex);
        if (reg == null)
        {
            // Handles script execution order (blackboard registered after this panel spawns).
            ZoneDeclarativeMemory.RebuildRegistryFromScene();
            reg = ZoneDeclarativeMemory.ForZone(zoneIndex);
        }
        if (reg == null)
        {
            // Some scenes — notably the multiplayer embed / RAG-only inference path — never create
            // the zone blackboard (only ReplicaSceneSetup.CreateAllMentalAgents does). The cognitive /
            // physical writers (BSGMLAgent, MentalAgentController, PhysicalObservationLoop, …) re-fetch
            // ZoneDeclarativeMemory.ForZone(zone) on every write and silently no-op when it's null, so
            // completed-step data is lost. Cognition doesn't begin until a scene-ready delay, and this
            // panel is created during scene setup, so creating the blackboard here (mirroring
            // ReplicaSceneSetup.EnsureZoneDeclarativeMemory) means every subsequent write lands and the
            // panel fills in live.
            GameObject memGO = new GameObject($"ZoneDeclarativeMemory_Zone{zoneIndex}");
            reg = memGO.AddComponent<ZoneDeclarativeMemory>();
            reg.zoneIndex = zoneIndex;                 // Awake ran with zoneIndex = -1; fix + re-register
            ZoneDeclarativeMemory.RebuildRegistryFromScene();

            // This scene ran without a blackboard, so its cognitive-step completion path never records
            // payload / imaginal side effects. Injecting a blackboard would otherwise re-activate the
            // orchestrator's payload/imaginal gates the scene was never satisfying (cognition stalls
            // mid-sequence). Put the zone orchestrator into permissive-gate mode so those gates
            // self-heal and the DAG keeps flowing (as it did before, when memory was null).
            CognitivePhaseOrchestrator orch = CognitivePhaseOrchestrator.GetOrCreateForZone(zoneIndex);
            if (orch != null)
                orch.permissiveGates = true;

            Debug.Log($"✅ DeclarativeMemoryGaugeUI: created ZoneDeclarativeMemory for Zone {zoneIndex} (blackboard was absent in this scene; permissive gates enabled).");
        }
        memory = reg;

        // Snapshot mannualBuffer2.json into declarative memory once at start (reference only).
        DeclarativeJsonFileStore.EnsureManualBufferStored(memory);
    }

    void EnsureCanvas()
    {
        if (targetCanvas != null) { ApplyCanvasSharpness(targetCanvas); return; }

        GameObject existing = GameObject.Find("AgentSkillGaugeCanvas");
        if (existing != null)
            targetCanvas = existing.GetComponent<Canvas>();

        if (targetCanvas != null) { ApplyCanvasSharpness(targetCanvas); return; }

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
        ApplyCanvasSharpness(targetCanvas);
    }

    /// <summary>
    /// Sharpen the shared HUD canvas so dynamic-font Text stays crisp when the canvas is
    /// scaled up on wide/high-DPI displays. Shared with the temporal buffer + skill gauges,
    /// so this improves all HUD panels, not just this one.
    /// </summary>
    static void ApplyCanvasSharpness(Canvas canvas)
    {
        if (canvas == null) return;
        canvas.pixelPerfect = true;
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler != null && scaler.dynamicPixelsPerUnit < 3f)
            scaler.dynamicPixelsPerUnit = 3f;
    }

    void EnsurePanel()
    {
        if (panel != null || targetCanvas == null) return;

        panel = new GameObject($"Zone{zoneIndex}DeclarativeMemoryPanel");
        panel.transform.SetParent(targetCanvas.transform, false);

        RectTransform rect = panel.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(gaugeWidth, panelHeight);

        Image bg = panel.AddComponent<Image>();
        bg.color = backgroundColor;

        titleText = CreateText("Title", panel.transform, new Vector2(10f, -26f), new Vector2(-10f, -6f), 13, FontStyle.Bold, textColor);
        summaryText = CreateText("Summary", panel.transform, new Vector2(10f, -46f), new Vector2(-10f, -28f), 12, FontStyle.Bold, textColor);

        GameObject barGo = new GameObject("StepBar");
        barGo.transform.SetParent(panel.transform, false);
        RectTransform barRect = barGo.AddComponent<RectTransform>();
        barRect.anchorMin = new Vector2(0f, 1f);
        barRect.anchorMax = new Vector2(1f, 1f);
        barRect.pivot = new Vector2(0f, 1f);
        barRect.offsetMin = new Vector2(10f, -68f);
        barRect.offsetMax = new Vector2(-10f, -50f);
        Image barBg = barGo.AddComponent<Image>();
        barBg.color = barBackgroundColor;

        GameObject fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(barGo.transform, false);
        RectTransform fillRect = fillGo.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        fillImage = fillGo.AddComponent<Image>();
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImage.color = fillColor;

        percentText = CreateText("Percent", barGo.transform, Vector2.zero, Vector2.zero, 11, FontStyle.Bold, textColor);
        RectTransform percentRect = percentText.GetComponent<RectTransform>();
        percentRect.anchorMin = Vector2.zero;
        percentRect.anchorMax = Vector2.one;
        percentRect.offsetMin = Vector2.zero;
        percentRect.offsetMax = Vector2.zero;
        percentText.alignment = TextAnchor.MiddleCenter;

        currentStepText = CreateText("CurrentStep", panel.transform, new Vector2(10f, -88f), new Vector2(-10f, -70f), 12, FontStyle.Bold, textColor);
        currentDetailText = CreateText("CurrentDetail", panel.transform, new Vector2(10f, -108f), new Vector2(-10f, -90f), 11, FontStyle.Normal, mutedTextColor);
        manualBufferText = CreateText("ManualBufferLine", panel.transform, new Vector2(10f, -128f), new Vector2(-10f, -110f), 11, FontStyle.Normal, mutedTextColor);

        BuildScrollList();
        RefreshPosition();
    }

    void BuildScrollList()
    {
        GameObject scrollGo = new GameObject("DetailScroll");
        scrollGo.transform.SetParent(panel.transform, false);
        RectTransform scrollRect = scrollGo.AddComponent<RectTransform>();
        scrollRect.anchorMin = new Vector2(0f, 0f);
        scrollRect.anchorMax = new Vector2(1f, 1f);
        scrollRect.offsetMin = new Vector2(8f, 8f);
        scrollRect.offsetMax = new Vector2(-8f, -headerHeight);

        Image scrollBg = scrollGo.AddComponent<Image>();
        scrollBg.color = scrollBackgroundColor;   // also serves as the scroll-wheel raycast target

        ScrollRect scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 18f;

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
        contentRect.offsetMin = new Vector2(0f, 0f);
        contentRect.offsetMax = new Vector2(0f, 0f);

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

        scroll.viewport = viewportRect;
        scroll.content = contentRect;
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
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;

        LayoutElement le = go.AddComponent<LayoutElement>();
        le.minHeight = 16f;
        le.preferredHeight = 16f;

        rowTexts.Add(text);
        return text;
    }

    void Refresh()
    {
        EnsureReady();
        if (panel == null) return;

        RefreshPosition();

        titleText.text = $"Zone {zoneIndex} Declarative Memory";

        CognitivePhaseOrchestrator orch = CognitivePhaseOrchestrator.GetOrCreateForZone(zoneIndex);

        if (memory == null)
        {
            summaryText.text = "Waiting for declarative memory";
            currentStepText.text = "";
            currentDetailText.text = "";
            manualBufferText.text = "";
            if (fillImage != null) fillImage.fillAmount = 0f;
            if (percentText != null) percentText.text = "0%";
            SetRowCount(0);
            lastRenderedListSignature = string.Empty;
            return;
        }

        IReadOnlyList<DeclarativeStepRecord> records = memory.CompletedStepRecords;

        // ── Reward = exact running sum of correct_step_reward over completed cognitive steps ──
        float reward = 0f;
        int cogDone = 0;
        for (int i = 0; i < records.Count; i++)
        {
            DeclarativeStepRecord r = records[i];
            if (r != null && r.IsCognitive) { reward += r.correct_step_reward; cogDone++; }
        }

        // ── Progress total (cognitive) from the orchestrator, matching the "M:" header ──
        int total = cogDone;
        if (orch != null)
        {
            orch.GetTotalStepCounts(out int mentalTotal, out int _);
            if (mentalTotal > 0) total = mentalTotal;
        }
        float ratio = Mathf.Clamp01((float)cogDone / Mathf.Max(1, total));

        summaryText.text = $"cog {cogDone}/{total}    reward +{reward:0.##}";
        fillImage.fillAmount = ratio;
        fillImage.color = ratio >= 1f ? fullFillColor : fillColor;
        percentText.text = $"{Mathf.RoundToInt(ratio * 100f)}%";

        // ── Current executing step: id · type · duration ──
        ActionSequenceStep active = FindActiveCognitiveStep(orch);
        if (active != null)
        {
            currentStepText.text = $"▶ now  {ShortStepId(active.stepId)}  [{Short(active.currentCognitiveState, 20)}]";
            currentDetailText.text = $"type {NA(active.actionType)} · dur {active.expectedDuration:0.##}s · target {NA(active.targetObjectId)}";
        }
        else
        {
            currentStepText.text = "▶ now  idle";
            currentDetailText.text = "";
        }

        manualBufferText.text = memory.ManualBufferStored
            ? $"manual_buffer: {memory.manualBufferFrameJson.Count} frame(s) · {memory.manualBufferJsonRaw.Length} chars"
            : "manual_buffer: not stored";

        if (!memory.ManualBufferStored)
            DeclarativeJsonFileStore.EnsureManualBufferStored(memory);

        // ── Detail: the exact declarative record stored for each completed cognitive step ──
        RefreshRecordRows(records);
    }

    /// <summary>Render cognitive step records, then mannualBuffer2.json snapshot at the end (rebuild on change).</summary>
    void RefreshRecordRows(IReadOnlyList<DeclarativeStepRecord> records)
    {
        int cogCount = 0;
        for (int i = 0; i < records.Count; i++)
            if (records[i] != null && records[i].IsCognitive) cogCount++;

        int manualFrames = memory != null && memory.ManualBufferStored ? memory.manualBufferFrameJson.Count : 0;
        string signature = $"{cogCount}|{manualFrames}|{(memory != null && memory.ManualBufferStored ? memory.manualBufferJsonRaw.Length : 0)}";
        if (signature == lastRenderedListSignature) return;
        lastRenderedListSignature = signature;

        int row = 0;
        if (cogCount == 0)
            row = SetRow(row, "(no cognitive steps completed yet)", mutedTextColor);

        for (int i = 0; i < records.Count; i++)
        {
            DeclarativeStepRecord r = records[i];
            if (r == null || !r.IsCognitive) continue;

            row = SetRow(row, $"✓ {ShortStepId(r.stepId)}  ·  {NA(r.stationName)}  ·  r+{r.correct_step_reward:0.##}", textColor);
            row = SetRow(row, $"    type {NA(r.actionType)} · dur {r.expectedDurationSec:0.##}s · act {(r.isActivated ? "yes" : "no")} · barrier {(r.isBarrier ? "yes" : "no")}", mutedTextColor);
            row = SetRow(row, $"    target {NA(r.stationId)} · dep {JoinDeps(r.dependsOn)}", mutedTextColor);
            if (!string.IsNullOrWhiteSpace(r.producesPayload))
                row = SetRow(row, $"    → {r.producesPayload} = {Short(NA(r.producedValue), 42)}", mutedTextColor);
            if (!string.IsNullOrWhiteSpace(r.imaginalStateBefore) || !string.IsNullOrWhiteSpace(r.imaginalStateAfter))
                row = SetRow(row, $"    state {NA(r.imaginalStateBefore)} → {NA(r.imaginalStateAfter)}", mutedTextColor);
            row = SetRow(row, "", mutedTextColor);   // spacer between records
        }

        row = AppendManualBufferRows(row);

        for (int i = row; i < rowTexts.Count; i++)
            if (rowTexts[i].gameObject.activeSelf) rowTexts[i].gameObject.SetActive(false);
    }

    /// <summary>Append mannualBuffer2.json snapshot rows at the bottom of the scroll list.</summary>
    int AppendManualBufferRows(int row)
    {
        if (memory == null || !memory.ManualBufferStored) return row;

        string label = string.IsNullOrWhiteSpace(memory.manualBufferFileName) ? "mannualBuffer2.json" : memory.manualBufferFileName;
        row = SetRow(row, "", mutedTextColor);
        row = SetRow(row, $"── {label} (stored at start · reference only) ──", textColor);
        row = SetRow(row, $"    {memory.manualBufferFrameJson.Count} frame(s) · {memory.manualBufferJsonRaw.Length} chars (complete file)", mutedTextColor);

        if (string.IsNullOrWhiteSpace(memory.manualBufferJsonRaw))
        {
            row = SetRow(row, "    (file empty)", mutedTextColor);
            return row;
        }

        AppendPrettyJsonRows(ref row, memory.manualBufferJsonRaw, "    ");
        return row;
    }

    void AppendPrettyJsonRows(ref int row, string json, string linePrefix)
    {
        string pretty = JsonPrettyPrinter.TryFormat(json);
        if (string.IsNullOrEmpty(pretty)) return;

        string[] lines = pretty.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrEmpty(line))
            {
                row = SetJsonRow(row, linePrefix, mutedTextColor);
                continue;
            }
            row = SetJsonRow(row, linePrefix + line, mutedTextColor);
        }
    }

    int SetJsonRow(int idx, string text, Color color)
    {
        Text t = GetOrCreateRow(idx);
        if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
        t.color = color;
        t.text = text;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        t.fontSize = 10;
        t.fontStyle = FontStyle.Normal;

        LayoutElement le = t.GetComponent<LayoutElement>();
        if (le != null)
        {
            le.minHeight = 14f;
            le.preferredHeight = 14f;
        }

        return idx + 1;
    }

    int SetRow(int idx, string text, Color color)
    {
        Text t = GetOrCreateRow(idx);
        if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
        t.color = color;
        t.text = text;
        t.fontSize = 11;
        t.fontStyle = FontStyle.Normal;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Truncate;

        LayoutElement le = t.GetComponent<LayoutElement>();
        if (le != null)
        {
            le.minHeight = 16f;
            le.preferredHeight = 16f;
        }

        return idx + 1;
    }

    static ActionSequenceStep FindActiveCognitiveStep(CognitivePhaseOrchestrator orch)
    {
        if (orch == null) return null;
        List<ActionSequenceStep> steps = orch.GetOrderedStepsSnapshot();
        ActionSequenceStep anyActive = null;
        for (int i = 0; i < steps.Count; i++)
        {
            ActionSequenceStep s = steps[i];
            if (s == null || s.isStepCompleted || !s.isActivated) continue;
            if (anyActive == null) anyActive = s;
            bool cog = string.Equals(s.agentRole, "M", StringComparison.OrdinalIgnoreCase)
                       || (!string.IsNullOrEmpty(s.targetObjectId)
                           && s.targetObjectId.StartsWith("cognitive_", StringComparison.OrdinalIgnoreCase));
            if (cog) return s;
        }
        return anyActive;
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
        // Strip a leading task prefix like "t01_" for compactness (e.g. t01_cog_s04 → cog_s04).
        int us = stepId.IndexOf('_');
        if (us > 0 && us < stepId.Length - 1 && stepId[0] == 't')
            return stepId.Substring(us + 1);
        return stepId;
    }

    void SetRowCount(int count)
    {
        for (int i = 0; i < count; i++)
        {
            Text row = GetOrCreateRow(i);
            if (!row.gameObject.activeSelf) row.gameObject.SetActive(true);
        }
        for (int i = count; i < rowTexts.Count; i++)
        {
            if (rowTexts[i].gameObject.activeSelf) rowTexts[i].gameObject.SetActive(false);
        }
    }

    void RefreshPosition()
    {
        if (panel == null) return;

        RectTransform rect = panel.GetComponent<RectTransform>();
        if (rect == null) return;

        float y;
        GameObject temporalPanel = GameObject.Find($"Zone{zoneIndex}TemporalBufferPanel");
        if (temporalPanel != null)
        {
            // Stack directly beneath the Temporal Buffer panel.
            RectTransform temporalRect = temporalPanel.GetComponent<RectTransform>();
            y = temporalRect.anchoredPosition.y - temporalRect.sizeDelta.y - panelStackSpacing;
        }
        else
        {
            // Fallback: replicate the temporal buffer's own stacking math, then add its height.
            int physicalRows = CountHudPhysicalGaugeRows();
            float stepRow = 0f;
            AgentSkillGaugeUI skillGaugeUi = FindObjectOfType<AgentSkillGaugeUI>();
            if (skillGaugeUi != null && skillGaugeUi.stepIndicatorOnlyMode && skillGaugeUi.showZone0StepIndicator)
                stepRow = stepIndicatorHeight + skillGaugeSpacing;

            float temporalY = -(topPadding + (skillGaugeHeight + skillGaugeSpacing) * physicalRows + stepRow + skillGaugeSpacing);
            y = temporalY - temporalPanelHeight - panelStackSpacing;
        }

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

    static string Short(string value, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value)) return "--";
        return value.Length <= maxLen ? value : value.Substring(0, Mathf.Max(0, maxLen - 3)) + "...";
    }
}
