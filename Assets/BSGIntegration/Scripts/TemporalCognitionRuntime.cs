using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Parsed, stable view of the RAG temporal module/buffer sections.
/// The runtime can still operate without these summaries by using live step events.
/// </summary>
public class TemporalRagSnapshot
{
    public float moduleBudgetMs;
    public float moduleElapsedMs;
    public float moduleRemainingMs;
    public float moduleOverBudgetMs;
    public string timeBlock = "";
    public string domain = "";
    public int totalStepsTimed;
    public int totalParallelInstances;

    public readonly Dictionary<string, TemporalRagAction> actionsByStepId =
        new Dictionary<string, TemporalRagAction>(StringComparer.OrdinalIgnoreCase);

    public readonly Dictionary<string, TemporalParallelEvent> parallelByGroupId =
        new Dictionary<string, TemporalParallelEvent>(StringComparer.OrdinalIgnoreCase);

    public readonly Dictionary<string, TemporalSubTaskTiming> subTasksById =
        new Dictionary<string, TemporalSubTaskTiming>(StringComparer.OrdinalIgnoreCase);

    public string finalAction = "";
    public string finalSequencing = "";
    public string finalExecutionMode = "";
}

public class TemporalRagAction
{
    public string stepId = "";
    public string subTaskId = "";
    public string action = "";
    public string sequencing = "";
    public string executionMode = "";
    public float stepElapsedMs;
    public string timeBlockAtStep = "";
}

public class TemporalParallelEvent
{
    public string parallelGroupId = "";
    public string subTaskId = "";
    public float timestampMs;
    public float durationMs;
    public string[] members = new string[0];
}

public class TemporalSubTaskTiming
{
    public string subTaskId = "";
    public string label = "";
    public float startTimeSec;
    public float endTimeSec;
    public int mentalStepCount;
    public int physicalStepCount;
}

public class TemporalStepTimingRecord
{
    public string stepId = "";
    public string agentRole = "";
    public string subTaskId = "";
    public string targetObjectId = "";
    public string targetObjectName = "";
    public string description = "";
    public string action = "";
    public string sequencing = "";
    public string executionMode = "";
    public string parallelGroupId = "";
    public string timeBlock = "";
    public float expectedDurationSec;
    public float startTimeSec;
    public float endTimeSec;
    public bool completed;

    public float ActualElapsedSec(float nowSec)
    {
        return completed ? Mathf.Max(0f, endTimeSec - startTimeSec) : Mathf.Max(0f, nowSec - startTimeSec);
    }
}

/// <summary>
/// Runtime memory for the Temporal Buffer. The Temporal Module writes active/completed
/// timing records here; UI and other systems read from this single memory object.
/// </summary>
public class TemporalBufferRuntimeMemory
{
    readonly Dictionary<string, TemporalStepTimingRecord> activeRecords =
        new Dictionary<string, TemporalStepTimingRecord>(StringComparer.OrdinalIgnoreCase);

    readonly List<TemporalStepTimingRecord> completedRecords = new List<TemporalStepTimingRecord>();

    public IReadOnlyDictionary<string, TemporalStepTimingRecord> ActiveRecords => activeRecords;
    public IReadOnlyList<TemporalStepTimingRecord> CompletedRecords => completedRecords;
    public int ActiveCount => activeRecords.Count;
    public float overallGameTimeSec;

    public void StoreActive(TemporalStepTimingRecord record)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.stepId)) return;
        activeRecords[record.stepId] = record;
    }

    public bool TryGetActive(string stepId, out TemporalStepTimingRecord record)
    {
        record = null;
        return !string.IsNullOrWhiteSpace(stepId) && activeRecords.TryGetValue(stepId, out record);
    }

    public void Complete(TemporalStepTimingRecord record)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.stepId)) return;
        activeRecords.Remove(record.stepId);
        completedRecords.Add(record);
    }

    public TemporalStepTimingRecord GetPrimaryActiveRecord()
    {
        TemporalStepTimingRecord latest = null;
        foreach (var kvp in activeRecords)
        {
            if (latest == null || kvp.Value.startTimeSec >= latest.startTimeSec)
                latest = kvp.Value;
        }
        return latest;
    }

    public List<TemporalStepTimingRecord> GetRecentCompleted(int maxCount)
    {
        List<TemporalStepTimingRecord> result = new List<TemporalStepTimingRecord>();
        int start = Mathf.Max(0, completedRecords.Count - Mathf.Max(0, maxCount));
        for (int i = start; i < completedRecords.Count; i++)
            result.Add(completedRecords[i]);
        return result;
    }
}

/// <summary>
/// Lightweight parser for the RAG temporal module and buffer sections.
/// It intentionally avoids schema-wide dependencies because the main runtime uses a normalized JSON shape.
/// </summary>
public static class TemporalRagParser
{
    public static TemporalRagSnapshot ParseFromSceneLoader(SceneUILoader loader)
    {
        if (loader == null) return new TemporalRagSnapshot();

        string raw = !string.IsNullOrEmpty(loader.RawJsonText)
            ? loader.RawJsonText
            : loader.EffectivePipelineJson;

        return Parse(raw);
    }

    public static TemporalRagSnapshot Parse(string json)
    {
        TemporalRagSnapshot snapshot = new TemporalRagSnapshot();
        if (string.IsNullOrEmpty(json)) return snapshot;

        string dataJson = json;
        if (RagSceneJsonBridge.IsRagEnvelope(json) &&
            RagSceneJsonBridge.TryExtractUnitySceneDataJson(json, out string extracted, out _))
        {
            dataJson = extracted;
        }

        ParseTemporalModule(dataJson, snapshot);
        ParseTemporalBuffer(dataJson, snapshot);
        ParseSubTasks(dataJson, snapshot);
        return snapshot;
    }

    static void ParseTemporalModule(string json, TemporalRagSnapshot snapshot)
    {
        string moduleJson = ExtractNamedObject(json, "temporalModule");
        if (string.IsNullOrEmpty(moduleJson)) return;

        snapshot.moduleBudgetMs = ExtractFloat(moduleJson, "totalBudgetMs");
        snapshot.moduleElapsedMs = ExtractFloat(moduleJson, "elapsedMs");
        snapshot.moduleRemainingMs = ExtractFloat(moduleJson, "remainingMs");
        snapshot.moduleOverBudgetMs = ExtractFloat(moduleJson, "overBudgetMs");
        snapshot.timeBlock = ExtractString(moduleJson, "timeBlock");
        snapshot.domain = ExtractString(moduleJson, "domain");

        string parallelJson = ExtractNamedArray(moduleJson, "parallelismEvents");
        ParseParallelEvents(parallelJson, snapshot);
    }

    static void ParseTemporalBuffer(string json, TemporalRagSnapshot snapshot)
    {
        string bufferJson = ExtractNamedObject(json, "temporalBuffer");
        if (string.IsNullOrEmpty(bufferJson)) return;

        snapshot.finalAction = ExtractString(bufferJson, "finalAction");
        snapshot.finalSequencing = ExtractString(bufferJson, "finalSequencing");
        snapshot.finalExecutionMode = ExtractString(bufferJson, "finalExecutionMode");
        string finalBlock = ExtractString(bufferJson, "finalTimeBlock");
        if (string.IsNullOrWhiteSpace(snapshot.timeBlock))
            snapshot.timeBlock = finalBlock;

        string totalsJson = ExtractNamedObject(bufferJson, "totals");
        if (!string.IsNullOrEmpty(totalsJson))
        {
            snapshot.totalStepsTimed = ExtractInt(totalsJson, "totalStepsTimed");
            snapshot.totalParallelInstances = ExtractInt(totalsJson, "totalParallelInstances");
        }

        string actionsJson = ExtractNamedArray(bufferJson, "actionsLog");
        foreach (string item in EnumerateObjects(actionsJson))
        {
            TemporalRagAction action = new TemporalRagAction
            {
                stepId = ExtractString(item, "stepId"),
                subTaskId = NormalizeSubTaskId(ExtractRawValue(item, "subTaskId")),
                action = ExtractString(item, "action"),
                sequencing = ExtractString(item, "sequencing"),
                executionMode = ExtractString(item, "executionMode"),
                stepElapsedMs = ExtractFloat(item, "stepElapsedMs"),
                timeBlockAtStep = ExtractString(item, "timeBlockAtStep")
            };

            if (!string.IsNullOrWhiteSpace(action.stepId))
                snapshot.actionsByStepId[action.stepId] = action;
        }

        string parallelJson = ExtractNamedArray(bufferJson, "parallelEventsLog");
        ParseParallelEvents(parallelJson, snapshot);
    }

    static void ParseParallelEvents(string arrayJson, TemporalRagSnapshot snapshot)
    {
        if (string.IsNullOrEmpty(arrayJson)) return;

        foreach (string item in EnumerateObjects(arrayJson))
        {
            TemporalParallelEvent pe = new TemporalParallelEvent
            {
                parallelGroupId = ExtractString(item, "parallelGroupId"),
                subTaskId = NormalizeSubTaskId(ExtractRawValue(item, "subTaskId")),
                timestampMs = ExtractFloat(item, "timestampMs"),
                durationMs = ExtractFloat(item, "durationMs"),
                members = ExtractStringArray(item, "members")
            };

            if (!string.IsNullOrWhiteSpace(pe.parallelGroupId))
                snapshot.parallelByGroupId[pe.parallelGroupId] = pe;
        }
    }

    static void ParseSubTasks(string json, TemporalRagSnapshot snapshot)
    {
        string arrayJson = ExtractNamedArray(json, "subTasks");
        if (string.IsNullOrEmpty(arrayJson)) return;

        foreach (string item in EnumerateObjects(arrayJson))
        {
            TemporalSubTaskTiming st = new TemporalSubTaskTiming
            {
                subTaskId = ExtractString(item, "subTaskId"),
                label = ExtractString(item, "label"),
                startTimeSec = ExtractFloat(item, "startTimeSec"),
                endTimeSec = ExtractFloat(item, "endTimeSec"),
                mentalStepCount = ExtractInt(item, "mentalStepCount"),
                physicalStepCount = ExtractInt(item, "physicalStepCount")
            };

            if (!string.IsNullOrWhiteSpace(st.subTaskId))
                snapshot.subTasksById[st.subTaskId] = st;
        }
    }

    static IEnumerable<string> EnumerateObjects(string arrayJson)
    {
        if (string.IsNullOrEmpty(arrayJson)) yield break;

        int pos = 0;
        while (pos < arrayJson.Length)
        {
            int start = arrayJson.IndexOf('{', pos);
            if (start < 0) yield break;

            int end = FindMatchingBrace(arrayJson, start);
            if (end < 0) yield break;

            yield return arrayJson.Substring(start, end - start + 1);
            pos = end + 1;
        }
    }

    static string ExtractNamedObject(string json, string key)
    {
        int keyIndex = FindKey(json, key);
        if (keyIndex < 0) return "";
        int colon = json.IndexOf(':', keyIndex);
        if (colon < 0) return "";
        int start = json.IndexOf('{', colon + 1);
        if (start < 0) return "";
        int end = FindMatchingBrace(json, start);
        return end < 0 ? "" : json.Substring(start, end - start + 1);
    }

    static string ExtractNamedArray(string json, string key)
    {
        int keyIndex = FindKey(json, key);
        if (keyIndex < 0) return "";
        int colon = json.IndexOf(':', keyIndex);
        if (colon < 0) return "";
        int start = json.IndexOf('[', colon + 1);
        if (start < 0) return "";
        int end = FindMatchingBracket(json, start);
        return end < 0 ? "" : json.Substring(start, end - start + 1);
    }

    static int FindKey(string json, string key)
    {
        return string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)
            ? -1
            : json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
    }

    static string ExtractString(string json, string key)
    {
        int idx = FindKey(json, key);
        if (idx < 0) return "";
        int colon = json.IndexOf(':', idx);
        if (colon < 0) return "";
        int pos = colon + 1;
        while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;
        if (pos >= json.Length || json[pos] != '"') return "";
        pos++;

        int end = pos;
        while (end < json.Length)
        {
            if (json[end] == '"' && (end == 0 || json[end - 1] != '\\'))
                break;
            end++;
        }
        if (end >= json.Length) return "";
        return UnescapeJson(json.Substring(pos, end - pos));
    }

    static string ExtractRawValue(string json, string key)
    {
        int idx = FindKey(json, key);
        if (idx < 0) return "";
        int colon = json.IndexOf(':', idx);
        if (colon < 0) return "";
        int pos = colon + 1;
        while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;
        int end = pos;
        while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != '\n' && json[end] != '\r')
            end++;
        return json.Substring(pos, end - pos).Trim().Trim('"');
    }

    static int ExtractInt(string json, string key)
    {
        return Mathf.RoundToInt(ExtractFloat(json, key));
    }

    static float ExtractFloat(string json, string key)
    {
        string raw = ExtractRawValue(json, key);
        if (string.IsNullOrWhiteSpace(raw) || raw == "null") return 0f;
        return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : 0f;
    }

    static string[] ExtractStringArray(string json, string key)
    {
        string arrayJson = ExtractNamedArray(json, key);
        if (string.IsNullOrEmpty(arrayJson)) return new string[0];

        List<string> values = new List<string>();
        int pos = 1;
        while (pos < arrayJson.Length - 1)
        {
            int quote = arrayJson.IndexOf('"', pos);
            if (quote < 0) break;
            int end = quote + 1;
            while (end < arrayJson.Length)
            {
                if (arrayJson[end] == '"' && arrayJson[end - 1] != '\\')
                    break;
                end++;
            }
            if (end >= arrayJson.Length) break;
            values.Add(UnescapeJson(arrayJson.Substring(quote + 1, end - quote - 1)));
            pos = end + 1;
        }
        return values.ToArray();
    }

    static string NormalizeSubTaskId(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "null") return "";
        return raw.StartsWith("st_", StringComparison.OrdinalIgnoreCase) ? raw : "st_" + raw;
    }

    static string UnescapeJson(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\\"", "\"")
            .Replace("\\n", "\n")
            .Replace("\\r", "\r")
            .Replace("\\t", "\t")
            .Replace("\\\\", "\\");
    }

    static int FindMatchingBrace(string text, int openIndex)
    {
        int depth = 0;
        bool inString = false;
        for (int i = openIndex; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"' && (i == 0 || text[i - 1] != '\\')) inString = !inString;
            if (inString) continue;
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    static int FindMatchingBracket(string text, int openIndex)
    {
        int depth = 0;
        bool inString = false;
        for (int i = openIndex; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"' && (i == 0 || text[i - 1] != '\\')) inString = !inString;
            if (inString) continue;
            if (c == '[') depth++;
            else if (c == ']')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }
}

/// <summary>
/// Zone-scoped temporal blackboard. It records live planned-vs-actual step timings
/// from the orchestrator and exposes them to Temporal Module/Buffer world-space UI.
/// </summary>
public class TemporalCognitionRuntime : MonoBehaviour
{
    private static readonly Dictionary<int, TemporalCognitionRuntime> instances =
        new Dictionary<int, TemporalCognitionRuntime>();

    public static TemporalCognitionRuntime GetOrCreate(int zoneIndex)
    {
        if (instances.TryGetValue(zoneIndex, out TemporalCognitionRuntime existing) && existing != null)
            return existing;

        if (PlayModeQuitGuard.IsQuitting) return null;

        GameObject go = new GameObject($"TemporalCognitionRuntime_Zone{zoneIndex}");
        TemporalCognitionRuntime runtime = go.AddComponent<TemporalCognitionRuntime>();
        runtime.zoneIndex = zoneIndex;
        instances[zoneIndex] = runtime;
        return runtime;
    }

    public static bool TryGetForZone(int zoneIndex, out TemporalCognitionRuntime runtime)
    {
        return instances.TryGetValue(zoneIndex, out runtime) && runtime != null;
    }

    public int zoneIndex;
    public TemporalRagSnapshot Snapshot { get; private set; } = new TemporalRagSnapshot();
    public TemporalBufferRuntimeMemory BufferMemory { get; private set; } = new TemporalBufferRuntimeMemory();
    public event Action Changed;

    CognitivePhaseOrchestrator orchestrator;
    float sceneStartTime = -1f;
    float nextTickNotifyTime;
    bool subscribed;
    readonly HashSet<string> delayPenalizedStepIds =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<TemporalStepTimingRecord> CompletedRecords => BufferMemory.CompletedRecords;
    public int ActiveCount => BufferMemory.ActiveCount;
    public int CurrentPhase => orchestrator != null ? orchestrator.CurrentPhase : CognitivePhaseOrchestrator.PHASE_IMAGINE;
    public string CurrentSubTaskId => orchestrator != null ? orchestrator.CurrentSubTaskId : "st_0";
    public float ElapsedSceneSeconds => sceneStartTime < 0f ? 0f : Mathf.Max(0f, Time.time - sceneStartTime);

    void Awake()
    {
        instances[zoneIndex] = this;
        ReloadTemporalSnapshot();
        Subscribe();
    }

    void OnEnable()
    {
        Subscribe();
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    void OnDestroy()
    {
        Unsubscribe();
        if (instances.TryGetValue(zoneIndex, out TemporalCognitionRuntime stored) && stored == this)
            instances.Remove(zoneIndex);
    }

    void Update()
    {
        if (Time.time < nextTickNotifyTime) return;
        nextTickNotifyTime = Time.time + 0.15f;
        BufferMemory.overallGameTimeSec = ElapsedSceneSeconds;
        if (BufferMemory.ActiveCount > 0)
        {
            CheckActiveStepsForDelayPenalty();
            PublishTemporalState(null, "tick");
            NotifyChanged();
        }
    }

    void CheckActiveStepsForDelayPenalty()
    {
        if (!RagMlExtrinsicRewardHub.UseSparseStepTimeoutPenalty) return;

        float now = ElapsedSceneSeconds;
        float threshold = RagMlExtrinsicRewardHub.DelayPenaltyThreshold;

        foreach (var kvp in BufferMemory.ActiveRecords)
        {
            TemporalStepTimingRecord record = kvp.Value;
            if (record == null || string.IsNullOrEmpty(record.stepId)) continue;
            if (delayPenalizedStepIds.Contains(record.stepId)) continue;
            if (record.expectedDurationSec <= 0f) continue;

            float elapsed = record.ActualElapsedSec(now);
            if (elapsed <= threshold * record.expectedDurationSec) continue;

            delayPenalizedStepIds.Add(record.stepId);
            float overdueFactor = elapsed / record.expectedDurationSec;
            RagMlExtrinsicRewardHub.ApplyDelayPenalty(zoneIndex, record.stepId, overdueFactor);
        }
    }

    public void ReloadTemporalSnapshot()
    {
        SceneUILoader loader = FindObjectOfType<SceneUILoader>();
        Snapshot = TemporalRagParser.ParseFromSceneLoader(loader);
        if (loader != null)
        {
            string raw = !string.IsNullOrEmpty(loader.MergedRawRagJson) ? loader.MergedRawRagJson : loader.RawJsonText;
            RagMlExtrinsicRewardHub.LoadEfficiencyPenaltiesFromJson(raw);
        }
        NotifyChanged();
    }

    public TemporalStepTimingRecord GetPrimaryActiveRecord()
    {
        return BufferMemory.GetPrimaryActiveRecord();
    }

    public bool TryGetSubTaskTiming(string subTaskId, out TemporalSubTaskTiming timing)
    {
        timing = null;
        return Snapshot != null &&
               !string.IsNullOrWhiteSpace(subTaskId) &&
               Snapshot.subTasksById.TryGetValue(subTaskId, out timing);
    }

    public void GetStepCounts(out int mentalCompleted, out int physicalCompleted, out int mentalTotal, out int physicalTotal)
    {
        mentalCompleted = 0;
        physicalCompleted = 0;
        mentalTotal = 0;
        physicalTotal = 0;

        if (orchestrator == null) return;
        orchestrator.GetCompletedStepCounts(out mentalCompleted, out physicalCompleted);
        orchestrator.GetTotalStepCounts(out mentalTotal, out physicalTotal);
    }

    public List<TemporalStepTimingRecord> GetRecentCompleted(int maxCount)
    {
        return BufferMemory.GetRecentCompleted(maxCount);
    }

    void Subscribe()
    {
        if (subscribed) return;
        orchestrator = CognitivePhaseOrchestrator.GetOrCreateForZone(zoneIndex);
        if (orchestrator == null) return;

        orchestrator.OnCognitiveStepDispatched += OnStepDispatched;
        orchestrator.OnPhysicalStepDispatched += OnStepDispatched;
        orchestrator.OnStepCompleted += OnStepCompleted;
        orchestrator.OnBarrierReached += OnBarrierReached;
        orchestrator.OnPhaseChanged += OnPhaseChanged;
        orchestrator.OnAllStepsCompleted += OnAllStepsCompleted;
        subscribed = true;
    }

    void Unsubscribe()
    {
        if (!subscribed || orchestrator == null) return;

        orchestrator.OnCognitiveStepDispatched -= OnStepDispatched;
        orchestrator.OnPhysicalStepDispatched -= OnStepDispatched;
        orchestrator.OnStepCompleted -= OnStepCompleted;
        orchestrator.OnBarrierReached -= OnBarrierReached;
        orchestrator.OnPhaseChanged -= OnPhaseChanged;
        orchestrator.OnAllStepsCompleted -= OnAllStepsCompleted;
        subscribed = false;
    }

    void OnStepDispatched(string stepId)
    {
        if (string.IsNullOrWhiteSpace(stepId)) return;
        if (sceneStartTime < 0f) sceneStartTime = Time.time;

        ActionSequenceStep step = orchestrator != null ? orchestrator.GetStep(stepId) : null;
        TemporalStepTimingRecord record = BuildRecord(stepId, step);
        BufferMemory.StoreActive(record);
        BufferMemory.overallGameTimeSec = ElapsedSceneSeconds;
        PublishTemporalState(record, "active");
        NotifyChanged();
    }

    void OnStepCompleted(string stepId)
    {
        if (string.IsNullOrWhiteSpace(stepId)) return;
        if (sceneStartTime < 0f) sceneStartTime = Time.time;

        if (!BufferMemory.TryGetActive(stepId, out TemporalStepTimingRecord record))
        {
            ActionSequenceStep step = orchestrator != null ? orchestrator.GetStep(stepId) : null;
            record = BuildRecord(stepId, step);
        }

        record.completed = true;
        record.endTimeSec = ElapsedSceneSeconds;
        float actualSec = record.ActualElapsedSec(ElapsedSceneSeconds);
        BufferMemory.Complete(record);
        BufferMemory.overallGameTimeSec = ElapsedSceneSeconds;
        PublishTemporalState(record, "completed");

        if (record.expectedDurationSec > 0f)
            RagMlExtrinsicRewardHub.ApplyEfficiencyBonus(zoneIndex, stepId, record.expectedDurationSec, actualSec);

        string physicalId = ZoneAgentIds.TryResolvePhysicalAgentId(zoneIndex);
        if (!string.IsNullOrEmpty(physicalId))
            MLTrainingResultsWriter.RecordStepCompletionTime(physicalId, stepId, actualSec);

        NotifyChanged();
    }

    void OnBarrierReached(string barrierStepId, string closesSubTaskId, string opensSubTaskId)
    {
        NotifyChanged();
    }

    void OnPhaseChanged(int phase)
    {
        NotifyChanged();
    }

    void OnAllStepsCompleted(int completedZoneIndex)
    {
        if (completedZoneIndex == zoneIndex)
            NotifyChanged();
    }

    TemporalStepTimingRecord BuildRecord(string stepId, ActionSequenceStep step)
    {
        TemporalStepTimingRecord record = new TemporalStepTimingRecord
        {
            stepId = stepId,
            startTimeSec = ElapsedSceneSeconds,
            endTimeSec = 0f,
            completed = false
        };

        if (step != null)
        {
            record.agentRole = step.agentRole;
            record.subTaskId = step.subTaskId;
            record.targetObjectId = step.targetObjectId;
            record.targetObjectName = step.targetObjectName;
            record.description = step.description;
            record.parallelGroupId = step.parallelGroupId;
            record.expectedDurationSec = CognitiveProcessManager.ComputeStationDwellSeconds(step);
        }

        if (Snapshot != null && Snapshot.actionsByStepId.TryGetValue(stepId, out TemporalRagAction action))
        {
            record.action = action.action;
            record.sequencing = action.sequencing;
            record.executionMode = action.executionMode;
            record.timeBlock = action.timeBlockAtStep;
        }
        else if (step != null)
        {
            record.action = !string.IsNullOrWhiteSpace(step.actionVerb)
                ? step.actionVerb.ToUpperInvariant()
                : step.currentCognitiveState;
            record.sequencing = !string.IsNullOrWhiteSpace(step.parallelGroupId) ? "while" : "then";
            record.executionMode = !string.IsNullOrWhiteSpace(step.parallelGroupId) ? "parallel" : "sequential";
            record.timeBlock = Snapshot != null ? Snapshot.timeBlock : "";
        }

        return record;
    }

    void PublishTemporalState(TemporalStepTimingRecord record, string status)
    {
        ZoneDeclarativeMemory mem = ZoneDeclarativeMemory.ForZone(zoneIndex);
        if (mem == null)
        {
            ZoneDeclarativeMemory.RebuildRegistryFromScene();
            mem = ZoneDeclarativeMemory.ForZone(zoneIndex);
        }
        if (mem == null) return;

        TemporalStepTimingRecord active = record != null && !record.completed
            ? record
            : BufferMemory.GetPrimaryActiveRecord();

        if (active != null)
        {
            float elapsed = active.ActualElapsedSec(ElapsedSceneSeconds);
            string value =
                $"step={active.stepId};role={ResolveRole(active)};status={status};start={active.startTimeSec:F2};" +
                $"elapsed={elapsed:F2};expected={active.expectedDurationSec:F2};target={active.targetObjectId}";
            mem.RecordCognitiveStep(active.stepId, "temporal_active_step", value);
        }

        if (record != null && record.completed)
        {
            string value =
                $"step={record.stepId};role={ResolveRole(record)};start={record.startTimeSec:F2};end={record.endTimeSec:F2};" +
                $"duration={record.ActualElapsedSec(ElapsedSceneSeconds):F2};expected={record.expectedDurationSec:F2};target={record.targetObjectId}";
            mem.RecordCognitiveStep(record.stepId, "temporal_last_completed_step", value);
        }

        mem.RecordCognitiveStep("temporal_runtime", "temporal_game_time", BufferMemory.overallGameTimeSec.ToString("F2", CultureInfo.InvariantCulture));
        mem.RecordCognitiveStep("temporal_runtime", "temporal_completed_count", BufferMemory.CompletedRecords.Count.ToString(CultureInfo.InvariantCulture));
    }

    static string ResolveRole(TemporalStepTimingRecord record)
    {
        if (record == null) return "";
        if (!string.IsNullOrWhiteSpace(record.agentRole)) return record.agentRole;
        return !string.IsNullOrWhiteSpace(record.targetObjectId) &&
               record.targetObjectId.StartsWith("cognitive_", StringComparison.OrdinalIgnoreCase)
            ? "M"
            : "P";
    }

    void NotifyChanged()
    {
        Changed?.Invoke();
    }
}
