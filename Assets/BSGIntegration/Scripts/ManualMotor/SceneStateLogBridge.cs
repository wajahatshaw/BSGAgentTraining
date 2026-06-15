using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Parses sceneStateLog[] from RAG JSON to link physical stepIds to Klein metadata.</summary>
public static class SceneStateLogBridge
{
    static readonly Dictionary<string, SceneStateKleinRef> _byStepId = new Dictionary<string, SceneStateKleinRef>(StringComparer.OrdinalIgnoreCase);
    static bool _parsed;

    public static bool IsParsed => _parsed;
    public static IReadOnlyDictionary<string, SceneStateKleinRef> ByStepId => _byStepId;

    public static void Reset()
    {
        _byStepId.Clear();
        _parsed = false;
    }

    public static bool TryParseFromRagText(string ragText)
    {
        Reset();
        if (string.IsNullOrWhiteSpace(ragText))
            return false;

        int logKey = ragText.IndexOf("\"sceneStateLog\"", StringComparison.Ordinal);
        if (logKey < 0)
            return false;

        int arrayStart = ragText.IndexOf('[', logKey);
        if (arrayStart < 0)
            return false;

        int arrayEnd = FindMatchingBracket(ragText, arrayStart, '[', ']');
        if (arrayEnd < 0)
            return false;

        string arrayBody = ragText.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
        ParseEntries(arrayBody);
        _parsed = _byStepId.Count > 0;
        if (_parsed)
            Debug.Log($"[SceneStateLogBridge] Parsed {_byStepId.Count} sceneStateLog entry(ies).");
        return _parsed;
    }

    static void ParseEntries(string arrayBody)
    {
        int i = 0;
        while (i < arrayBody.Length)
        {
            int objStart = arrayBody.IndexOf('{', i);
            if (objStart < 0) break;

            int objEnd = FindMatchingBracket(arrayBody, objStart, '{', '}');
            if (objEnd < 0) break;

            string entry = arrayBody.Substring(objStart, objEnd - objStart + 1);
            TryParseEntry(entry);
            i = objEnd + 1;
        }
    }

    static void TryParseEntry(string entryJson)
    {
        string stepId = ExtractString(entryJson, "triggeredByStepId");
        if (string.IsNullOrWhiteSpace(stepId))
            return;

        int kfKey = entryJson.IndexOf("\"kleinFrame\"", StringComparison.Ordinal);
        if (kfKey < 0)
            kfKey = entryJson.IndexOf("\"klein_frame\"", StringComparison.Ordinal);
        if (kfKey < 0)
            return;

        int kfBrace = entryJson.IndexOf('{', kfKey);
        if (kfBrace < 0)
            return;

        int kfEnd = FindMatchingBracket(entryJson, kfBrace, '{', '}');
        if (kfEnd < 0)
            return;

        string kfJson = entryJson.Substring(kfBrace, kfEnd - kfBrace + 1);

        SceneStateKleinRef entry = new SceneStateKleinRef
        {
            stepId = stepId,
            kleinFrameId = ExtractString(kfJson, "klein_frame_id"),
            thread = ExtractString(kfJson, "thread"),
            targetObject = ExtractString(kfJson, "object"),
            stateBefore = ExtractString(entryJson, "stateBefore"),
            stateAfter = ExtractString(entryJson, "stateAfter"),
        };

        if (string.IsNullOrWhiteSpace(entry.stateBefore))
            entry.stateBefore = ExtractString(kfJson, "state_before");
        if (string.IsNullOrWhiteSpace(entry.stateAfter))
            entry.stateAfter = ExtractString(kfJson, "state_after");

        _byStepId[stepId] = entry;
    }

    public static bool TryGetForStep(string stepId, out SceneStateKleinRef entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(stepId))
            return false;
        return _byStepId.TryGetValue(stepId, out entry);
    }

    static string ExtractString(string json, string key)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
            return string.Empty;

        string needle = $"\"{key}\"";
        int keyIdx = json.IndexOf(needle, StringComparison.Ordinal);
        if (keyIdx < 0)
            return string.Empty;

        int colon = json.IndexOf(':', keyIdx + needle.Length);
        if (colon < 0)
            return string.Empty;

        int q1 = json.IndexOf('"', colon + 1);
        if (q1 < 0)
            return string.Empty;

        int q2 = json.IndexOf('"', q1 + 1);
        if (q2 < 0)
            return string.Empty;

        return json.Substring(q1 + 1, q2 - q1 - 1);
    }

    static int FindMatchingBracket(string text, int openIndex, char open, char close)
    {
        int depth = 0;
        bool inString = false;
        for (int i = openIndex; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"' && (i == 0 || text[i - 1] != '\\'))
            {
                inString = !inString;
                continue;
            }
            if (inString) continue;

            if (c == open) depth++;
            else if (c == close)
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }
}
