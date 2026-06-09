using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Parses goalBufferContract from a single cognitive step JSON object (shared by AgentSequenceManager and RAG adapter).
/// </summary>
public static class GoalBufferContractParser
{
    public static bool TryParse(string stepJson, out GoalBufferContract contract)
    {
        contract = null;
        int keyIdx = stepJson.IndexOf("\"goalBufferContract\":", StringComparison.Ordinal);
        if (keyIdx == -1) return false;

        int objStart = stepJson.IndexOf('{', keyIdx);
        if (objStart == -1) return false;

        int objEnd = FindMatchingBrace(stepJson, objStart);
        if (objEnd == -1) return false;

        string contractJson = stepJson.Substring(objStart, objEnd - objStart + 1);

        int initKey = contractJson.IndexOf("\"initial_state\":", StringComparison.Ordinal);
        if (initKey == -1) return false;

        int initObjStart = contractJson.IndexOf('{', initKey);
        if (initObjStart == -1) return false;

        int initObjEnd = FindMatchingBrace(contractJson, initObjStart);
        if (initObjEnd == -1) return false;

        string initialStateJson = contractJson.Substring(initObjStart, initObjEnd - initObjStart + 1);

        var connList = ParseConnectionsArray(contractJson);

        int stackKey = initialStateJson.IndexOf("\"stack\":", StringComparison.Ordinal);
        if (stackKey >= 0)
        {
            int stackArrStart = initialStateJson.IndexOf('[', stackKey);
            if (stackArrStart == -1) return false;

            int stackArrEnd = FindMatchingBracket(initialStateJson, stackArrStart);
            if (stackArrEnd == -1) return false;

            string stackArrayJson = initialStateJson.Substring(stackArrStart, stackArrEnd - stackArrStart + 1);
            var layers = new List<GoalBufferStackLayer>();
            int pos = 1;
            while (pos < stackArrayJson.Length - 1)
            {
                while (pos < stackArrayJson.Length && IsJsonWhitespaceOrComma(stackArrayJson[pos])) pos++;
                if (pos >= stackArrayJson.Length - 1) break;

                int layerStart = stackArrayJson.IndexOf('{', pos);
                if (layerStart == -1) break;

                int layerEnd = FindMatchingBrace(stackArrayJson, layerStart);
                if (layerEnd == -1) break;

                string layerJson = stackArrayJson.Substring(layerStart, layerEnd - layerStart + 1);
                layers.Add(new GoalBufferStackLayer
                {
                    position = ExtractStringValue(layerJson, "position"),
                    type = ExtractStringValue(layerJson, "type"),
                    value = ExtractStringValue(layerJson, "value"),
                    isActive = ExtractBoolValue(layerJson, "isActive"),
                    isCompleted = ExtractBoolValue(layerJson, "isCompleted"),
                    reward = ExtractIntValue(layerJson, "reward"),
                    penalty = ExtractIntValue(layerJson, "penalty"),
                    retryAttempt = ExtractIntValue(layerJson, "retryAttempt"),
                    desireLevel = ExtractFloatValue(layerJson, "desireLevel"),
                    expectedDuration = ExtractFloatValue(layerJson, "expectedDuration")
                });

                pos = layerEnd + 1;
            }

            layers.Sort(CompareGoalBufferLayerOrder);

            contract = new GoalBufferContract
            {
                stack = layers.ToArray(),
                connections = connList.ToArray()
            };
            return contract.stack != null && contract.stack.Length > 0;
        }

        // initial_state.bottom | middle | top supports both legacy direct strings and
        // current RAG slot objects: { "type": "...", "value": "..." }.
        string bottom = ExtractInitialStateSlotValue(initialStateJson, "bottom");
        string middle = ExtractInitialStateSlotValue(initialStateJson, "middle");
        string top = ExtractInitialStateSlotValue(initialStateJson, "top");

        contract = new GoalBufferContract
        {
            stack = null,
            connections = connList.ToArray(),
            initialStateBottom = bottom,
            initialStateMiddle = middle,
            initialStateTop = top
        };

        bool anyText = contract.HasInitialStateTripleContent();
        bool hasConn = connList.Count > 0;
        return anyText || hasConn;
    }

    static List<string> ParseConnectionsArray(string contractJson)
    {
        var connList = new List<string>();
        int connKey = contractJson.IndexOf("\"connections\":", StringComparison.Ordinal);
        if (connKey != -1)
        {
            int connArrStart = contractJson.IndexOf('[', connKey);
            if (connArrStart != -1)
            {
                int connArrEnd = FindMatchingBracket(contractJson, connArrStart);
                if (connArrEnd != -1)
                {
                    string connJson = contractJson.Substring(connArrStart, connArrEnd - connArrStart + 1);
                    ParseJsonStringArray(connJson, connList);
                }
            }
        }

        return connList;
    }

    /// <summary>
    /// Non-empty Goal Buffer slot text, or null if missing/empty.
    /// Accepts "bottom": "text" and "bottom": { "type": "...", "value": "text" }.
    /// </summary>
    static string ExtractInitialStateSlotValue(string j, string key)
    {
        string searchKey = $"\"{key}\":";
        int startIdx = j.IndexOf(searchKey, StringComparison.Ordinal);
        if (startIdx == -1) return null;

        startIdx += searchKey.Length;
        while (startIdx < j.Length && char.IsWhiteSpace(j[startIdx])) startIdx++;
        if (startIdx >= j.Length) return null;

        if (startIdx + 4 <= j.Length && string.Compare(j, startIdx, "null", 0, 4, StringComparison.Ordinal) == 0)
            return null;

        if (j[startIdx] == '"')
            return ExtractJsonStringAt(j, startIdx, out _);

        if (j[startIdx] == '{')
        {
            int endIdx = FindMatchingBrace(j, startIdx);
            if (endIdx == -1) return null;

            string slotObjectJson = j.Substring(startIdx, endIdx - startIdx + 1);
            string value = ExtractNullableJsonStringValue(slotObjectJson, "value");
            if (!string.IsNullOrWhiteSpace(value)) return value;

            // Fallback for future slot-object schemas that rename "value".
            string label = ExtractNullableJsonStringValue(slotObjectJson, "label");
            if (!string.IsNullOrWhiteSpace(label)) return label;
            string text = ExtractNullableJsonStringValue(slotObjectJson, "text");
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        return null;
    }

    /// <summary>Non-empty string, or null if key missing, JSON null, or whitespace-only.</summary>
    static string ExtractNullableJsonStringValue(string j, string key)
    {
        string searchKey = $"\"{key}\":";
        int startIdx = j.IndexOf(searchKey, StringComparison.Ordinal);
        if (startIdx == -1) return null;

        startIdx += searchKey.Length;
        while (startIdx < j.Length && char.IsWhiteSpace(j[startIdx])) startIdx++;
        if (startIdx >= j.Length) return null;

        if (startIdx + 4 <= j.Length && string.Compare(j, startIdx, "null", 0, 4, StringComparison.Ordinal) == 0)
            return null;

        if (j[startIdx] != '"') return null;

        return ExtractJsonStringAt(j, startIdx, out _);
    }

    static string ExtractJsonStringAt(string j, int quoteIdx, out int endQuoteIdx)
    {
        endQuoteIdx = -1;
        if (string.IsNullOrEmpty(j) || quoteIdx < 0 || quoteIdx >= j.Length || j[quoteIdx] != '"')
            return null;

        int startIdx = quoteIdx + 1;
        int endIdx = startIdx;
        while (endIdx < j.Length)
        {
            if (j[endIdx] == '"' && (endIdx == startIdx || j[endIdx - 1] != '\\'))
                break;
            endIdx++;
        }

        if (endIdx >= j.Length) return null;
        endQuoteIdx = endIdx;

        string s = j.Substring(startIdx, endIdx - startIdx)
            .Replace("\\\"", "\"")
            .Replace("\\n", "\n")
            .Replace("\\r", "\r")
            .Replace("\\t", "\t")
            .Replace("\\\\", "\\");
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    /// <summary>Find cognitive step objects that contain GoalBuffer and parse contracts for RAG.</summary>
    public static List<RagGoalBufferStepSummary> ExtractAllFromFullJson(string json)
    {
        var results = new List<RagGoalBufferStepSummary>();
        const string marker = "\"currentCognitiveState\":\"GoalBuffer\"";
        int searchFrom = 0;
        while (searchFrom < json.Length)
        {
            int m = json.IndexOf(marker, searchFrom, StringComparison.Ordinal);
            if (m == -1) break;

            int stepStart = FindContainingObjectStart(json, m);
            if (stepStart < 0)
            {
                searchFrom = m + 1;
                continue;
            }

            int stepEnd = FindMatchingBrace(json, stepStart);
            if (stepEnd == -1)
            {
                searchFrom = m + 1;
                continue;
            }

            string stepJson = json.Substring(stepStart, stepEnd - stepStart + 1);
            if (TryParse(stepJson, out GoalBufferContract c))
            {
                string stepId = ExtractStringValue(stepJson, "stepId");
                string targetId = ExtractStringValue(stepJson, "targetObjectId");
                results.Add(new RagGoalBufferStepSummary
                {
                    step_id = stepId,
                    target_object_id = targetId,
                    contract = c
                });
            }

            searchFrom = stepEnd + 1;
        }

        return results;
    }

    /// <summary>
    /// Reload JSON from disk and read one stack layer (position + type) from that agent's Goal Buffer cognitive step.
    /// Fails if file missing, agent/step/stack missing, layer missing, or value null/whitespace — so runtime edits to basicUi_ml are respected.
    /// </summary>
    public static bool TryReadGoalBufferLayerFromDisk(
        string jsonFilePath,
        string agentProfileId,
        string position,
        string layerType,
        out string extractedValue,
        out string errorDetail)
    {
        extractedValue = null;
        errorDetail = null;

        if (string.IsNullOrWhiteSpace(jsonFilePath))
        {
            errorDetail = "no_json_path";
            return false;
        }

        if (!File.Exists(jsonFilePath))
        {
            errorDetail = "json_file_not_found";
            return false;
        }

        if (string.IsNullOrWhiteSpace(agentProfileId))
        {
            errorDetail = "no_agent_profile_id";
            return false;
        }

        string full;
        try
        {
            full = File.ReadAllText(jsonFilePath);
        }
        catch (Exception ex)
        {
            errorDetail = "read_error:" + ex.Message;
            return false;
        }

        if (!string.IsNullOrEmpty(full) && full[0] == '\uFEFF')
            full = full.Substring(1);

        return TryReadGoalBufferLayerFromFullJson(full, agentProfileId, position, layerType, out extractedValue, out errorDetail);
    }

    /// <summary>
    /// Reads a Goal Buffer stack layer from an in-memory JSON string (e.g. <see cref="SceneUILoader.EffectivePipelineJson"/> after RAG+sidecar merge).
    /// </summary>
    public static bool TryReadGoalBufferLayerFromFullJson(
        string fullJson,
        string agentProfileId,
        string position,
        string layerType,
        out string extractedValue,
        out string errorDetail)
    {
        extractedValue = null;
        errorDetail = null;

        if (string.IsNullOrWhiteSpace(fullJson))
        {
            errorDetail = "empty_json";
            return false;
        }

        if (string.IsNullOrWhiteSpace(agentProfileId))
        {
            errorDetail = "no_agent_profile_id";
            return false;
        }

        string full = fullJson;
        if (!string.IsNullOrEmpty(full) && full[0] == '\uFEFF')
            full = full.Substring(1);

        if (!TryExtractAgentProfileObjectJson(full, agentProfileId, out string agentJson, out errorDetail))
            return false;

        if (!TryExtractCognitiveActionSequenceArrayJson(agentJson, out string cogArrayJson, out errorDetail))
            return false;

        int pos = 1;
        while (pos < cogArrayJson.Length - 1)
        {
            while (pos < cogArrayJson.Length && IsJsonWhitespaceOrComma(cogArrayJson[pos])) pos++;
            if (pos >= cogArrayJson.Length - 1) break;

            int stepStart = cogArrayJson.IndexOf('{', pos);
            if (stepStart == -1) break;

            int stepEnd = FindMatchingBrace(cogArrayJson, stepStart);
            if (stepEnd == -1)
            {
                errorDetail = "unclosed_step_object";
                return false;
            }

            string stepJson = cogArrayJson.Substring(stepStart, stepEnd - stepStart + 1);
            pos = stepEnd + 1;

            if (stepJson.IndexOf("\"goalBufferContract\":", StringComparison.Ordinal) < 0)
                continue;

            if (!TryParse(stepJson, out GoalBufferContract contract) || contract.stack == null)
                continue;

            foreach (GoalBufferStackLayer layer in contract.stack)
            {
                if (layer == null) continue;
                if (!string.Equals(layer.position, position, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(layer.type, layerType, StringComparison.OrdinalIgnoreCase)) continue;

                if (string.IsNullOrWhiteSpace(layer.value))
                {
                    errorDetail = "value_empty_or_missing";
                    return false;
                }

                extractedValue = layer.value;
                return true;
            }
        }

        errorDetail = "stack_layer_not_found";
        return false;
    }

    /// <summary>
    /// Prefers merged pipeline JSON from <see cref="SceneUILoader"/> when present; otherwise reads the runtime JsonFile path.
    /// </summary>
    public static bool TryReadGoalBufferLayerForRuntime(
        string agentProfileId,
        string position,
        string layerType,
        out string extractedValue,
        out string errorDetail)
    {
        extractedValue = null;
        errorDetail = null;

        SceneUILoader sl = UnityEngine.Object.FindObjectOfType<SceneUILoader>();
        if (sl != null && !string.IsNullOrEmpty(sl.EffectivePipelineJson))
            return TryReadGoalBufferLayerFromFullJson(sl.EffectivePipelineJson, agentProfileId, position, layerType, out extractedValue, out errorDetail);

        string jsonName = AgentSequenceManager.Instance != null
            ? AgentSequenceManager.Instance.GetRuntimeJsonFileName()
            : "basicUI_ml2.json";
        string jsonPath = Path.Combine(Application.dataPath, "JsonFile", jsonName);
        return TryReadGoalBufferLayerFromDisk(jsonPath, agentProfileId, position, layerType, out extractedValue, out errorDetail);
    }

    /// <summary>
    /// Reload JSON from disk and parse <c>goalBufferContract</c> for the cognitive step whose <c>stepId</c> matches <paramref name="cognitiveStepId"/>.
    /// Used when runtime <see cref="ActionSequenceStep.goalBufferContract"/> is missing but the step exists in JSON (e.g. export timing).
    /// </summary>
    public static bool TryReadGoalBufferContractForStepFromDisk(
        string jsonFilePath,
        string agentProfileId,
        string cognitiveStepId,
        out GoalBufferContract contract,
        out string errorDetail)
    {
        contract = null;
        errorDetail = null;

        if (string.IsNullOrWhiteSpace(jsonFilePath))
        {
            errorDetail = "no_json_path";
            return false;
        }

        if (!File.Exists(jsonFilePath))
        {
            errorDetail = "json_file_not_found";
            return false;
        }

        if (string.IsNullOrWhiteSpace(agentProfileId))
        {
            errorDetail = "no_agent_profile_id";
            return false;
        }

        if (string.IsNullOrWhiteSpace(cognitiveStepId))
        {
            errorDetail = "no_cognitive_step_id";
            return false;
        }

        string full;
        try
        {
            full = File.ReadAllText(jsonFilePath);
        }
        catch (Exception ex)
        {
            errorDetail = "read_error:" + ex.Message;
            return false;
        }

        if (!string.IsNullOrEmpty(full) && full[0] == '\uFEFF')
            full = full.Substring(1);

        if (!TryExtractAgentProfileObjectJson(full, agentProfileId, out string agentJson, out errorDetail))
            return false;

        if (!TryExtractCognitiveActionSequenceArrayJson(agentJson, out string cogArrayJson, out errorDetail))
            return false;

        int pos = 1;
        while (pos < cogArrayJson.Length - 1)
        {
            while (pos < cogArrayJson.Length && IsJsonWhitespaceOrComma(cogArrayJson[pos])) pos++;
            if (pos >= cogArrayJson.Length - 1) break;

            int stepStart = cogArrayJson.IndexOf('{', pos);
            if (stepStart == -1) break;

            int stepEnd = FindMatchingBrace(cogArrayJson, stepStart);
            if (stepEnd == -1)
            {
                errorDetail = "unclosed_step_object";
                return false;
            }

            string stepJson = cogArrayJson.Substring(stepStart, stepEnd - stepStart + 1);
            pos = stepEnd + 1;

            if (stepJson.IndexOf("\"goalBufferContract\":", StringComparison.Ordinal) < 0)
                continue;

            string sid = ExtractStringValue(stepJson, "stepId");
            if (!string.Equals(sid, cognitiveStepId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!TryParse(stepJson, out contract) || contract == null)
            {
                errorDetail = "parse_failed";
                return false;
            }

            if (!contract.HasStackLayers() && !contract.HasInitialStateTripleContent())
            {
                errorDetail = "parse_failed_or_empty_stack";
                return false;
            }

            return true;
        }

        errorDetail = "goal_buffer_step_not_found";
        return false;
    }

    static bool TryExtractAgentProfileObjectJson(string fullJson, string agentId, out string agentObjectJson, out string errorDetail)
    {
        agentObjectJson = null;
        errorDetail = null;

        int ap = fullJson.IndexOf("\"agentProfiles\":", StringComparison.Ordinal);
        if (ap < 0)
        {
            errorDetail = "no_agentProfiles";
            return false;
        }

        int zoneLen = Math.Min(fullJson.Length - ap, 600000);
        string zone = fullJson.Substring(ap, zoneLen);

        string key = $"\"{agentId}\":";
        int k = zone.IndexOf(key, StringComparison.Ordinal);
        if (k < 0)
        {
            errorDetail = "agent_profile_key_not_found:" + agentId;
            return false;
        }

        int objStart = zone.IndexOf('{', k + key.Length);
        if (objStart < 0)
        {
            errorDetail = "agent_profile_no_object";
            return false;
        }

        int objEnd = FindMatchingBrace(zone, objStart);
        if (objEnd < 0)
        {
            errorDetail = "agent_profile_unclosed";
            return false;
        }

        agentObjectJson = zone.Substring(objStart, objEnd - objStart + 1);
        return true;
    }

    static bool TryExtractCognitiveActionSequenceArrayJson(string agentObjectJson, out string arrayJson, out string errorDetail)
    {
        arrayJson = null;
        errorDetail = null;

        int cog = agentObjectJson.IndexOf("\"cognitiveActionSequence\":", StringComparison.Ordinal);
        if (cog < 0)
        {
            errorDetail = "no_cognitiveActionSequence";
            return false;
        }

        int arrStart = agentObjectJson.IndexOf('[', cog);
        if (arrStart < 0)
        {
            errorDetail = "cognitiveActionSequence_no_array";
            return false;
        }

        int arrEnd = FindMatchingBracket(agentObjectJson, arrStart);
        if (arrEnd < 0)
        {
            errorDetail = "cognitiveActionSequence_unclosed_array";
            return false;
        }

        arrayJson = agentObjectJson.Substring(arrStart, arrEnd - arrStart + 1);
        return true;
    }

    static int FindContainingObjectStart(string json, int anchorInsideObject)
    {
        bool inString = false;
        int depth = 0;
        for (int i = anchorInsideObject; i >= 0; i--)
        {
            char c = json[i];
            if (c == '"' && (i == 0 || json[i - 1] != '\\')) inString = !inString;
            if (inString) continue;

            if (c == '}') depth++;
            else if (c == '{')
            {
                if (depth == 0) return i;
                depth--;
            }
        }

        return -1;
    }

    public static int FindMatchingBrace(string s, int start)
    {
        int braceCount = 0;
        bool inString = false;
        for (int i = start; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"' && (i == 0 || s[i - 1] != '\\')) inString = !inString;
            if (!inString)
            {
                if (c == '{') braceCount++;
                if (c == '}')
                {
                    braceCount--;
                    if (braceCount == 0) return i;
                }
            }
        }

        return -1;
    }

    static int FindMatchingBracket(string s, int start)
    {
        int n = 0;
        bool inString = false;
        for (int i = start; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"' && (i == 0 || s[i - 1] != '\\')) inString = !inString;
            if (!inString)
            {
                if (c == '[') n++;
                if (c == ']')
                {
                    n--;
                    if (n == 0) return i;
                }
            }
        }

        return -1;
    }

    static int CompareGoalBufferLayerOrder(GoalBufferStackLayer a, GoalBufferStackLayer b)
    {
        return GoalBufferLayerRank(a.position).CompareTo(GoalBufferLayerRank(b.position));
    }

    static int GoalBufferLayerRank(string position)
    {
        if (string.IsNullOrEmpty(position)) return 99;
        switch (position.ToLowerInvariant())
        {
            case "bottom": return 0;
            case "middle": return 1;
            case "top": return 2;
            default: return 99;
        }
    }

    static bool IsJsonWhitespaceOrComma(char c)
    {
        return c == ' ' || c == '\n' || c == '\r' || c == '\t' || c == ',';
    }

    static void ParseJsonStringArray(string arrayJson, List<string> target)
    {
        int i = 0;
        while (i < arrayJson.Length)
        {
            int q = arrayJson.IndexOf('"', i);
            if (q == -1) break;
            int q2 = arrayJson.IndexOf('"', q + 1);
            if (q2 == -1) break;
            target.Add(arrayJson.Substring(q + 1, q2 - q - 1));
            i = q2 + 1;
        }
    }

    static string ExtractStringValue(string j, string key)
    {
        string searchKey = $"\"{key}\":";
        int startIdx = j.IndexOf(searchKey, StringComparison.Ordinal);
        if (startIdx == -1) return "";

        startIdx += searchKey.Length;
        while (startIdx < j.Length && (j[startIdx] == ' ' || j[startIdx] == '\t')) startIdx++;

        if (startIdx >= j.Length || j[startIdx] != '"') return "";

        startIdx++;
        int endIdx = j.IndexOf('"', startIdx);
        if (endIdx == -1) return "";

        return j.Substring(startIdx, endIdx - startIdx);
    }

    static int ExtractIntValue(string j, string key)
    {
        string searchKey = $"\"{key}\":";
        int startIdx = j.IndexOf(searchKey, StringComparison.Ordinal);
        if (startIdx == -1) return 0;

        startIdx += searchKey.Length;
        while (startIdx < j.Length && (j[startIdx] == ' ' || j[startIdx] == '\t')) startIdx++;

        int endIdx = startIdx;
        while (endIdx < j.Length && (char.IsDigit(j[endIdx]) || j[endIdx] == '-')) endIdx++;

        if (endIdx == startIdx) return 0;
        return int.TryParse(j.Substring(startIdx, endIdx - startIdx), out int r) ? r : 0;
    }

    static float ExtractFloatValue(string j, string key)
    {
        string searchKey = $"\"{key}\":";
        int startIdx = j.IndexOf(searchKey, StringComparison.Ordinal);
        if (startIdx == -1) return 0f;

        startIdx += searchKey.Length;
        while (startIdx < j.Length && (j[startIdx] == ' ' || j[startIdx] == '\t')) startIdx++;

        int endIdx = startIdx;
        while (endIdx < j.Length && (char.IsDigit(j[endIdx]) || j[endIdx] == '.' || j[endIdx] == '-')) endIdx++;

        if (endIdx == startIdx) return 0f;
        return float.TryParse(j.Substring(startIdx, endIdx - startIdx), out float r) ? r : 0f;
    }

    static bool ExtractBoolValue(string j, string key)
    {
        string searchKey = $"\"{key}\":";
        int startIdx = j.IndexOf(searchKey, StringComparison.Ordinal);
        if (startIdx == -1) return false;

        startIdx += searchKey.Length;
        while (startIdx < j.Length && (j[startIdx] == ' ' || j[startIdx] == '\t')) startIdx++;

        if (startIdx + 4 <= j.Length && j.Substring(startIdx, 4) == "true") return true;
        return false;
    }
}

/// <summary>Lightweight summary for RAG cascade (LocalRagBootstrap).</summary>
[System.Serializable]
public class RagGoalBufferStepSummary
{
    public string step_id;
    public string target_object_id;
    public GoalBufferContract contract;
}
