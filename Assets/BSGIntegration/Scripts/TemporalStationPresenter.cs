using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public enum TemporalStationDisplayMode
{
    Module,
    Buffer
}

/// <summary>
/// World-space readout for the Temporal Module and Temporal Buffer stations.
/// Module mode shows the live timer summary; Buffer mode shows timing memory/history.
/// </summary>
[DefaultExecutionOrder(56)]
public class TemporalStationPresenter : MonoBehaviour
{
    public int zoneIndex = 0;
    public TemporalStationDisplayMode displayMode = TemporalStationDisplayMode.Module;
    public float uiHeight = 2.55f;
    public float refreshSeconds = 0.12f;

    GameObject uiRoot;
    TextMesh titleText;
    TextMesh bodyText;
    LineRenderer progressRing;
    TemporalCognitionRuntime runtime;
    float nextRefreshTime;

    public static TemporalStationPresenter GetOrCreateForStation(
        GameObject stationGo,
        TemporalStationDisplayMode mode,
        int zoneIndex)
    {
        if (stationGo == null) return null;

        TemporalStationPresenter presenter = stationGo.GetComponent<TemporalStationPresenter>();
        if (presenter == null)
            presenter = stationGo.AddComponent<TemporalStationPresenter>();

        presenter.displayMode = mode;
        presenter.zoneIndex = zoneIndex;
        presenter.ApplyPalette();
        presenter.EnsureRuntime();
        presenter.UpdateText();
        Debug.Log($"[TemporalStationPresenter] Attached {mode} UI to {stationGo.name} for zone {zoneIndex}.");
        return presenter;
    }

    void Awake()
    {
        BuildUI();
        EnsureRuntime();
        UpdateText();
    }

    void OnEnable()
    {
        EnsureRuntime();
        if (runtime != null)
            runtime.Changed += OnRuntimeChanged;
        UpdateText();
    }

    void OnDisable()
    {
        if (runtime != null)
            runtime.Changed -= OnRuntimeChanged;
    }

    void LateUpdate()
    {
        BillboardToCamera();

        if (Time.time >= nextRefreshTime)
        {
            nextRefreshTime = Time.time + refreshSeconds;
            UpdateText();
        }
    }

    void OnRuntimeChanged()
    {
        UpdateText();
    }

    void EnsureRuntime()
    {
        if (runtime != null) return;
        runtime = TemporalCognitionRuntime.GetOrCreate(zoneIndex);
    }

    void BuildUI()
    {
        if (uiRoot != null) return;

        uiRoot = new GameObject("TemporalUI_Root");
        uiRoot.transform.SetParent(transform, false);
        uiRoot.transform.localPosition = Vector3.up * uiHeight;

        GameObject titleGo = new GameObject("TemporalUI_Title");
        titleGo.transform.SetParent(uiRoot.transform, false);
        titleGo.transform.localPosition = new Vector3(0f, 0.33f, 0f);
        titleText = titleGo.AddComponent<TextMesh>();
        bool isModuleUi = displayMode == TemporalStationDisplayMode.Module;
        titleText.fontSize = isModuleUi ? 24 : 28;
        titleText.fontStyle = FontStyle.Bold;
        titleText.anchor = TextAnchor.MiddleCenter;
        titleText.alignment = TextAlignment.Center;
        titleText.characterSize = isModuleUi ? 0.040f : 0.052f;
        titleText.color = displayMode == TemporalStationDisplayMode.Module
            ? new Color(0.65f, 0.90f, 1f)
            : new Color(0.84f, 0.78f, 1f);
        MeshRenderer titleRenderer = titleGo.GetComponent<MeshRenderer>();
        if (titleRenderer != null) titleRenderer.sortingOrder = 80;

        GameObject bodyGo = new GameObject("TemporalUI_Body");
        bodyGo.transform.SetParent(uiRoot.transform, false);
        bodyGo.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        bodyText = bodyGo.AddComponent<TextMesh>();
        bodyText.fontSize = isModuleUi ? 14 : 17;
        bodyText.fontStyle = FontStyle.Normal;
        bodyText.anchor = TextAnchor.UpperCenter;
        bodyText.alignment = TextAlignment.Center;
        bodyText.characterSize = isModuleUi ? 0.036f : 0.044f;
        bodyText.color = new Color(0.92f, 0.92f, 0.92f);
        MeshRenderer bodyRenderer = bodyGo.GetComponent<MeshRenderer>();
        if (bodyRenderer != null) bodyRenderer.sortingOrder = 80;

        GameObject ringGo = new GameObject("TemporalUI_Ring");
        ringGo.transform.SetParent(uiRoot.transform, false);
        ringGo.transform.localPosition = new Vector3(0f, 0.16f, 0f);
        progressRing = ringGo.AddComponent<LineRenderer>();
        progressRing.loop = true;
        progressRing.useWorldSpace = false;
        progressRing.startWidth = 0.018f;
        progressRing.endWidth = 0.018f;
        progressRing.positionCount = 44;

        Shader spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader != null)
            progressRing.material = new Material(spriteShader);

        Color ringColor = displayMode == TemporalStationDisplayMode.Module
            ? new Color(0.65f, 0.90f, 1f)
            : new Color(0.84f, 0.78f, 1f);
        progressRing.startColor = ringColor;
        progressRing.endColor = ringColor;

        float radius = displayMode == TemporalStationDisplayMode.Module ? 0.40f : 0.48f;
        for (int i = 0; i < progressRing.positionCount; i++)
        {
            float angle = (float)i / progressRing.positionCount * Mathf.PI * 2f;
            progressRing.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
        }

        ApplyPalette();
    }

    void ApplyPalette()
    {
        Color accent = displayMode == TemporalStationDisplayMode.Module
            ? new Color(0.65f, 0.90f, 1f)
            : new Color(0.84f, 0.78f, 1f);

        if (titleText != null)
            titleText.color = accent;
        if (progressRing != null)
        {
            progressRing.startColor = accent;
            progressRing.endColor = accent;
        }
    }

    void UpdateText()
    {
        if (titleText == null || bodyText == null) return;
        EnsureRuntime();

        if (runtime == null)
        {
            titleText.text = "TEMPORAL";
            bodyText.text = "Waiting for runtime";
            return;
        }

        if (displayMode == TemporalStationDisplayMode.Module)
            WriteModuleText();
        else
            WriteBufferText();
    }

    void WriteModuleText()
    {
        if (progressRing != null) progressRing.enabled = true;
        titleText.fontSize = 32;
        titleText.characterSize = 0.075f;
        titleText.text = "TEMPORAL MODULE";

        TemporalStepTimingRecord active = runtime.GetPrimaryActiveRecord();
        runtime.GetStepCounts(out int mentalDone, out int physicalDone, out int mentalTotal, out int physicalTotal);

        StringBuilder sb = new StringBuilder(512);
        sb.Append("Task elapsed: ").Append(FormatSeconds(runtime.ElapsedSceneSeconds)).AppendLine();
        float plannedTaskSec = PlannedTaskSeconds(runtime.Snapshot);
        if (plannedTaskSec > 0f)
            sb.Append("Task plan: ").Append(FormatSeconds(plannedTaskSec)).AppendLine();
        sb.Append("Phase: ").Append(PhaseName(runtime.CurrentPhase)).Append(" | ").Append(runtime.CurrentSubTaskId).AppendLine();

        if (runtime.TryGetSubTaskTiming(runtime.CurrentSubTaskId, out TemporalSubTaskTiming st))
        {
            sb.Append("Subtask plan: ")
                .Append(FormatSeconds(st.startTimeSec))
                .Append(" -> ")
                .Append(FormatSeconds(st.endTimeSec))
                .AppendLine();
        }

        if (active != null)
        {
            float elapsed = active.ActualElapsedSec(runtime.ElapsedSceneSeconds);
            float expected = Mathf.Max(0.001f, active.expectedDurationSec);
            float remaining = expected - elapsed;
            sb.Append("Active: ").Append(active.stepId)
                .Append(" [").Append(RoleLabel(active)).Append("]").AppendLine();
            sb.Append("Target: ").Append(ShortName(active.targetObjectName, active.targetObjectId)).AppendLine();
            sb.Append("Expected: ").Append(FormatSeconds(expected))
                .Append(" | actual: ").Append(FormatSeconds(elapsed)).AppendLine();
            sb.Append(remaining >= 0f ? "Remaining: " : "Over: ")
                .Append(FormatSeconds(Mathf.Abs(remaining))).AppendLine();
            sb.Append(ProgressBar(elapsed / expected, 16));
        }
        else
        {
            sb.Append("Active: none").AppendLine();
            sb.Append(ProgressBar(0f, 16));
        }

        sb.AppendLine();
        sb.Append("Done M/P: ")
            .Append(mentalDone).Append("/").Append(mentalTotal)
            .Append(" | ")
            .Append(physicalDone).Append("/").Append(physicalTotal).AppendLine();

        TemporalRagSnapshot snap = runtime.Snapshot;
        if (snap != null)
        {
            sb.Append("RAG timed steps: ").Append(snap.totalStepsTimed)
                .Append(" | parallel: ").Append(snap.totalParallelInstances);
            if (snap.moduleElapsedMs > 0f)
            {
                sb.AppendLine();
                sb.Append("RAG elapsed: ").Append(FormatSeconds(snap.moduleElapsedMs / 1000f));
                if (snap.moduleOverBudgetMs > 0f)
                    sb.Append(" | over: ").Append(FormatSeconds(snap.moduleOverBudgetMs / 1000f));
            }
        }

        bodyText.text = sb.ToString();
    }

    void WriteBufferText()
    {
        if (progressRing != null) progressRing.enabled = false;
        titleText.fontSize = 20;
        titleText.characterSize = 0.045f;
        titleText.text = "TEMPORAL BUFFER";
        bodyText.text = "Timing shown in HUD";
    }

    void BillboardToCamera()
    {
        if (uiRoot == null) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        uiRoot.transform.rotation = Quaternion.LookRotation(
            uiRoot.transform.position - cam.transform.position,
            Vector3.up);
    }

    static string PhaseName(int phase)
    {
        switch (phase)
        {
            case CognitivePhaseOrchestrator.PHASE_IMAGINE: return "Imagine";
            case CognitivePhaseOrchestrator.PHASE_EXECUTE: return "Execute";
            case CognitivePhaseOrchestrator.PHASE_TRANSITION: return "Transition";
            default: return "Phase " + phase;
        }
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

    static string ProgressBar(float ratio, int width)
    {
        ratio = Mathf.Clamp01(ratio);
        int filled = Mathf.RoundToInt(ratio * width);
        StringBuilder sb = new StringBuilder(width + 8);
        sb.Append("[");
        for (int i = 0; i < width; i++)
            sb.Append(i < filled ? "#" : "-");
        sb.Append("] ").Append(Mathf.RoundToInt(ratio * 100f)).Append("%");
        return sb.ToString();
    }

    static string FormatSeconds(float seconds)
    {
        return Mathf.Max(0f, seconds).ToString("0.0") + "s";
    }

    static float PlannedTaskSeconds(TemporalRagSnapshot snapshot)
    {
        if (snapshot == null || snapshot.subTasksById == null) return 0f;

        float maxEnd = 0f;
        foreach (var kvp in snapshot.subTasksById)
        {
            if (kvp.Value != null && kvp.Value.endTimeSec > maxEnd)
                maxEnd = kvp.Value.endTimeSec;
        }
        return maxEnd;
    }

    static string ShortName(string name, string fallback)
    {
        string value = FirstNonEmpty(name, fallback, "");
        if (value.Length <= 26) return value;
        return value.Substring(0, 23) + "...";
    }

    static string FirstNonEmpty(params string[] values)
    {
        if (values == null) return "";
        for (int i = 0; i < values.Length; i++)
            if (!string.IsNullOrWhiteSpace(values[i]))
                return values[i];
        return "";
    }
}
