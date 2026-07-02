using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Loads mannualBuffer2.json from disk into <see cref="ZoneDeclarativeMemory"/> at game start.
/// Reference-only snapshot — the hand/IK system still reads the file via <see cref="ManualBufferCatalog"/>.
/// </summary>
public static class DeclarativeJsonFileStore
{
    const string ManualBufferFileName = "mannualBuffer2.json";

    public static void EnsureAllStored(ZoneDeclarativeMemory mem) => EnsureManualBufferStored(mem);

    public static void EnsureManualBufferStored(ZoneDeclarativeMemory mem)
    {
        if (mem == null || mem.ManualBufferStored) return;

        string raw = ReadJsonFile(ManualBufferFileName);
        raw = SanitizeManualBufferJson(raw);
        List<string> frameJson = JsonArrayExtractor.ExtractTopLevelArrayElements(raw, "manual_buffer");

        mem.StoreManualBuffer(ManualBufferFileName, raw, frameJson);

        Debug.Log($"[DeclarativeJsonFileStore] Stored {ManualBufferFileName} into Zone{mem.zoneIndex} declarative memory " +
                  $"({frameJson.Count} frame(s), {raw.Length} chars raw).");
    }

    static string SanitizeManualBufferJson(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        return raw.Replace("\"force_newtons\": null", "\"force_newtons\": 0");
    }

    public static string ReadJsonFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;

        try
        {
            string streaming = Path.Combine(Application.streamingAssetsPath, fileName);
            if (File.Exists(streaming)) return File.ReadAllText(streaming);

            string assets = Path.Combine(Application.dataPath, "JsonFile", fileName);
            if (File.Exists(assets)) return File.ReadAllText(assets);

            Debug.LogWarning($"[DeclarativeJsonFileStore] {fileName} not found at {streaming} or {assets}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[DeclarativeJsonFileStore] read {fileName} failed: {ex.Message}");
        }

        return string.Empty;
    }
}

/// <summary>Extracts raw JSON object strings from a named top-level array property.</summary>
static class JsonArrayExtractor
{
    public static List<string> ExtractTopLevelArrayElements(string json, string arrayPropertyName)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(arrayPropertyName))
            return results;

        string needle = $"\"{arrayPropertyName}\"";
        int keyIdx = json.IndexOf(needle, StringComparison.Ordinal);
        if (keyIdx < 0) return results;

        int arrayStart = json.IndexOf('[', keyIdx);
        if (arrayStart < 0) return results;

        int i = arrayStart + 1;
        while (i < json.Length)
        {
            i = SkipWhitespace(json, i);
            if (i >= json.Length) break;
            if (json[i] == ']') break;

            if (json[i] == '{')
            {
                if (TryReadJsonObject(json, i, out int endExclusive, out string obj))
                {
                    results.Add(obj);
                    i = endExclusive;
                    continue;
                }
            }

            i++;
        }

        return results;
    }

    static int SkipWhitespace(string s, int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        return i;
    }

    static bool TryReadJsonObject(string s, int start, out int endExclusive, out string obj)
    {
        endExclusive = start;
        obj = null;
        if (start >= s.Length || s[start] != '{') return false;

        int depth = 0;
        bool inString = false;
        bool escape = false;

        for (int i = start; i < s.Length; i++)
        {
            char c = s[i];
            if (inString)
            {
                if (escape) { escape = false; continue; }
                if (c == '\\') { escape = true; continue; }
                if (c == '"') inString = false;
                continue;
            }

            if (c == '"') { inString = true; continue; }
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    endExclusive = i + 1;
                    obj = s.Substring(start, endExclusive - start);
                    return true;
                }
            }
        }

        return false;
    }
}
