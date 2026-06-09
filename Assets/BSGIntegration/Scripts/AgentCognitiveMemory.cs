using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable]
public class CognitivePayload
{
    public string payloadType;
    public string payloadValue;
    public string sourceStationId;
    public float timestamp;
}

[Serializable]
public class CognitiveStepRecord
{
    public string stepId;
    public int stepOrder;
    public string cognitiveState;
    public string targetObjectId;
    public string sourceStationId;
    public float startedAt;
    public float completedAt;

    public string intention;
    public string goal;
    public string visualSummary;
    public string retrievalSummary;
    public string declarativeMatch;
    public string productionDecision;
    public string motorCommand;
}

public class AgentCognitiveMemory : MonoBehaviour
{
    [Header("Runtime Cognitive Memory")]
    public string intention;
    public string goal;
    public string visualInfo;
    public string retrievedSchema;
    public string commandState;
    public string currentPhase;

    [SerializeField] private List<CognitivePayload> payloadHistory = new List<CognitivePayload>();
    [SerializeField] private List<CognitiveStepRecord> stepRecords = new List<CognitiveStepRecord>();
    private readonly Dictionary<string, string> memorySlots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private const int MAX_PAYLOAD_HISTORY = 64;
    private const int MAX_STEP_RECORDS = 128;

    public IReadOnlyList<CognitivePayload> PayloadHistory => payloadHistory;
    public IReadOnlyList<CognitiveStepRecord> StepRecords => stepRecords;

    public void Store(string key, string value, string sourceStationId)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        memorySlots[key] = value ?? string.Empty;
        payloadHistory.Add(new CognitivePayload
        {
            payloadType = key,
            payloadValue = value ?? string.Empty,
            sourceStationId = sourceStationId,
            timestamp = Time.time
        });

        if (payloadHistory.Count > MAX_PAYLOAD_HISTORY)
        {
            payloadHistory.RemoveAt(0);
        }

        ApplyKnownSlot(key, value);
    }

    public bool TryGet(string key, out string value)
    {
        return memorySlots.TryGetValue(key, out value);
    }

    public void BeginCognitiveStep(
        string stepId,
        int stepOrder,
        string cognitiveState,
        string targetObjectId,
        string sourceStationId)
    {
        if (string.IsNullOrWhiteSpace(stepId))
        {
            return;
        }

        CognitiveStepRecord existing = stepRecords.LastOrDefault(r => string.Equals(r.stepId, stepId, StringComparison.OrdinalIgnoreCase));
        if (existing != null && existing.completedAt <= 0f)
        {
            return;
        }

        CognitiveStepRecord record = new CognitiveStepRecord
        {
            stepId = stepId,
            stepOrder = stepOrder,
            cognitiveState = cognitiveState ?? string.Empty,
            targetObjectId = targetObjectId ?? string.Empty,
            sourceStationId = sourceStationId ?? string.Empty,
            startedAt = Time.time,
            completedAt = 0f
        };

        stepRecords.Add(record);
        TrimStepRecords();
    }

    public void CompleteCognitiveStep(
        string stepId,
        int stepOrder,
        string cognitiveState,
        string targetObjectId,
        string sourceStationId,
        string intentionValue,
        string goalValue,
        string visualSummaryValue,
        string retrievalSummaryValue,
        string declarativeMatchValue,
        string productionDecisionValue,
        string motorCommandValue)
    {
        if (string.IsNullOrWhiteSpace(stepId))
        {
            return;
        }

        CognitiveStepRecord record = stepRecords.LastOrDefault(r => string.Equals(r.stepId, stepId, StringComparison.OrdinalIgnoreCase) && r.completedAt <= 0f);
        if (record == null)
        {
            record = new CognitiveStepRecord
            {
                stepId = stepId,
                stepOrder = stepOrder,
                cognitiveState = cognitiveState ?? string.Empty,
                targetObjectId = targetObjectId ?? string.Empty,
                sourceStationId = sourceStationId ?? string.Empty,
                startedAt = Time.time
            };
            stepRecords.Add(record);
        }

        record.stepOrder = stepOrder;
        record.cognitiveState = cognitiveState ?? record.cognitiveState ?? string.Empty;
        record.targetObjectId = targetObjectId ?? record.targetObjectId ?? string.Empty;
        record.sourceStationId = sourceStationId ?? record.sourceStationId ?? string.Empty;
        record.completedAt = Time.time;

        record.intention = intentionValue ?? string.Empty;
        record.goal = goalValue ?? string.Empty;
        record.visualSummary = visualSummaryValue ?? string.Empty;
        record.retrievalSummary = retrievalSummaryValue ?? string.Empty;
        record.declarativeMatch = declarativeMatchValue ?? string.Empty;
        record.productionDecision = productionDecisionValue ?? string.Empty;
        record.motorCommand = motorCommandValue ?? string.Empty;

        MirrorStepOutputsToSlots(record);
        TrimStepRecords();
    }

    public CognitiveStepRecord GetLatestStepRecord()
    {
        if (stepRecords.Count == 0)
        {
            return null;
        }
        return stepRecords[stepRecords.Count - 1];
    }

    public IReadOnlyList<CognitiveStepRecord> GetStepRecords()
    {
        return stepRecords;
    }

    private void MirrorStepOutputsToSlots(CognitiveStepRecord record)
    {
        if (record == null)
        {
            return;
        }

        Store("step_id", record.stepId, record.sourceStationId);
        Store("step_state", record.cognitiveState, record.sourceStationId);
        Store("step_target", record.targetObjectId, record.sourceStationId);
        Store("phase", record.cognitiveState, record.sourceStationId);

        if (!string.IsNullOrWhiteSpace(record.intention)) Store("intention", record.intention, record.sourceStationId);
        if (!string.IsNullOrWhiteSpace(record.goal)) Store("goal", record.goal, record.sourceStationId);
        if (!string.IsNullOrWhiteSpace(record.visualSummary)) Store("visual_info", record.visualSummary, record.sourceStationId);
        if (!string.IsNullOrWhiteSpace(record.retrievalSummary)) Store("retrieved_schema", record.retrievalSummary, record.sourceStationId);
        if (!string.IsNullOrWhiteSpace(record.declarativeMatch)) Store("declarative_match", record.declarativeMatch, record.sourceStationId);
        if (!string.IsNullOrWhiteSpace(record.productionDecision)) Store("production_rule", record.productionDecision, record.sourceStationId);
        if (!string.IsNullOrWhiteSpace(record.motorCommand)) Store("command_state", record.motorCommand, record.sourceStationId);
    }

    private void TrimStepRecords()
    {
        if (stepRecords.Count > MAX_STEP_RECORDS)
        {
            int removeCount = stepRecords.Count - MAX_STEP_RECORDS;
            stepRecords.RemoveRange(0, removeCount);
        }
    }

    void ApplyKnownSlot(string key, string value)
    {
        string lowered = key.ToLowerInvariant();
        if (lowered.Contains("intent")) intention = value;
        else if (lowered.Contains("goal")) goal = value;
        else if (lowered.Contains("visual")) visualInfo = value;
        else if (lowered.Contains("retriev")) retrievedSchema = value;
        else if (lowered.Contains("command")) commandState = value;
        else if (lowered.Contains("phase")) currentPhase = value;
    }
}
