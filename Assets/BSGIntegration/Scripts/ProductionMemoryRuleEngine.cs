using System;
using System.Text;
using UnityEngine;

/// <summary>
/// Small runtime rule layer for Production Memory steps.
/// It does not replace the DAG; it turns JSON Production Memory contracts into
/// blackboard commands that downstream visual/manual/retrieval steps can consume.
/// </summary>
public static class ProductionMemoryRuleEngine
{
    public static bool IsProductionMemoryStep(ActionSequenceStep step)
    {
        return step != null
            && string.Equals(step.currentCognitiveState, "ProductionMemory", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryEvaluateAndRecord(ActionSequenceStep step, ZoneDeclarativeMemory memory, out ProductionMemoryCommandRecord command)
    {
        command = null;
        if (!IsProductionMemoryStep(step) || memory == null) return false;
        if (!memory.HasPayloads(step.consumesPayload)) return false;

        ClassifyConnections(step.productionMemoryConnections, out bool hasVisual, out bool hasManual, out bool hasRetrieve);

        string commandType = ResolveCommandType(hasVisual, hasManual, hasRetrieve);
        string payloadKey = string.IsNullOrWhiteSpace(step.producesPayload)
            ? $"production_command_{step.stepId}"
            : step.producesPayload;

        command = new ProductionMemoryCommandRecord
        {
            stepId = step.stepId,
            payloadKey = payloadKey,
            commandType = commandType,
            description = step.description ?? string.Empty,
            targetObjectId = step.targetObjectId ?? string.Empty,
            targetObjectName = step.targetObjectName ?? string.Empty,
            connectionsSummary = step.productionMemoryConnections != null
                ? string.Join(",", step.productionMemoryConnections)
                : string.Empty,
            consumedPayloadSummary = memory.BuildPayloadSummary(step.consumesPayload),
            knowledgeSummary = BuildKnowledgeSummary(step, memory),
            hasVisualCommand = hasVisual,
            hasManualCommand = hasManual,
            hasRetrievalCommand = hasRetrieve
        };

        memory.RecordProductionMemoryCommand(step.stepId, command);
        RecordModuleCommandAliases(step, memory, command);
        return true;
    }

    static void RecordModuleCommandAliases(ActionSequenceStep step, ZoneDeclarativeMemory memory, ProductionMemoryCommandRecord command)
    {
        string value = command.ToBlackboardValue();

        if (command.hasVisualCommand)
            memory.RecordProducedPayload(step.stepId, "visual_command", value);

        if (command.hasManualCommand)
            memory.RecordProducedPayload(step.stepId, "manual_command", value);

        if (command.hasRetrievalCommand)
        {
            memory.RecordProducedPayload(step.stepId, "retrieval_query", command.knowledgeSummary);
            if (string.Equals(command.payloadKey, "retrieval_schema", StringComparison.OrdinalIgnoreCase))
                memory.RecordProducedPayload(step.stepId, "retrieval_schema", command.knowledgeSummary);
        }

        if (command.hasVisualCommand && command.hasManualCommand)
        {
            string parallelKey = !string.IsNullOrWhiteSpace(step.parallelGroupId)
                ? $"parallel_command_{step.parallelGroupId}"
                : command.payloadKey;
            memory.RecordProducedPayload(step.stepId, parallelKey, value);
        }
    }

    static void ClassifyConnections(string[] connections, out bool hasVisual, out bool hasManual, out bool hasRetrieve)
    {
        hasVisual = false;
        hasManual = false;
        hasRetrieve = false;

        if (connections == null) return;
        for (int i = 0; i < connections.Length; i++)
        {
            string c = connections[i];
            if (string.IsNullOrWhiteSpace(c)) continue;

            if (string.Equals(c, "visual_buffer", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c, "visual_location_buffer", StringComparison.OrdinalIgnoreCase))
                hasVisual = true;

            if (string.Equals(c, "manual_buffer", StringComparison.OrdinalIgnoreCase))
                hasManual = true;

            if (string.Equals(c, "goal_buffer", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c, "retrieval_buffer", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c, "declarative_module", StringComparison.OrdinalIgnoreCase))
                hasRetrieve = true;
        }
    }

    static string ResolveCommandType(bool hasVisual, bool hasManual, bool hasRetrieve)
    {
        if (hasVisual && hasManual) return "LOOK + ACT";
        if (hasManual) return "ACT";
        if (hasVisual) return "LOOK";
        if (hasRetrieve) return "RETRIEVE";
        return "COMMAND";
    }

    static string BuildKnowledgeSummary(ActionSequenceStep step, ZoneDeclarativeMemory memory)
    {
        StringBuilder sb = new StringBuilder(256);

        AppendPart(sb, "description", step.description);
        AppendPart(sb, "consumed", memory.BuildPayloadSummary(step.consumesPayload));
        AppendPart(sb, "goal", memory.goal);
        AppendPart(sb, "sub_goal", memory.goalBufferSubGoal);
        AppendPart(sb, "artifact", memory.goalBufferArtifactReference);
        AppendPart(sb, "smart_key", memory.goalBufferSmartKeyResult);
        AppendPart(sb, "retrieval_schema", memory.retrievalSchema);
        AppendPart(sb, "production_contract", Compact(step.productionMemoryContractJson));
        AppendPart(sb, "imaginal_contract", Compact(step.imaginalBufferContractJson));
        AppendPart(sb, "goal_contract", Compact(step.GetContractJson("goalBufferContract")));

        return sb.Length > 0 ? sb.ToString() : "json_contract_context";
    }

    static void AppendPart(StringBuilder sb, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (sb.Length > 0) sb.Append(" | ");
        sb.Append(key).Append("=").Append(value);
    }

    static string Compact(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        string compact = value.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        while (compact.Contains("  "))
            compact = compact.Replace("  ", " ");
        return compact.Length > 220 ? compact.Substring(0, 217) + "..." : compact;
    }
}

public class ProductionMemoryCommandRecord
{
    public string stepId;
    public string payloadKey;
    public string commandType;
    public string description;
    public string targetObjectId;
    public string targetObjectName;
    public string connectionsSummary;
    public string consumedPayloadSummary;
    public string knowledgeSummary;
    public bool hasVisualCommand;
    public bool hasManualCommand;
    public bool hasRetrievalCommand;

    public string ToBlackboardValue()
    {
        return $"type={commandType}; target={targetObjectId}; connections={connectionsSummary}; consumed={consumedPayloadSummary}; knowledge={knowledgeSummary}; description={description}";
    }
}
