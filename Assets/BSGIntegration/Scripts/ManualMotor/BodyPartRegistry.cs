using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>Single source of truth for RAG ↔ Unity body part names and IDs.</summary>
public static class BodyPartRegistry
{
    const string DefaultRelativePath = "Assets/JsonFile/body_part_registry.json";

    static bool _loaded;
    static readonly Dictionary<string, BodyPartEntry> _byRagName = new Dictionary<string, BodyPartEntry>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, BodyPartEntry> _bySceneId = new Dictionary<string, BodyPartEntry>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<int, BodyPartEntry> _byNumericId = new Dictionary<int, BodyPartEntry>();
    static readonly Dictionary<string, string> _aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static bool IsLoaded => _loaded;
    public static IReadOnlyDictionary<string, BodyPartEntry> ByRagName => _byRagName;

    public static bool TryLoad(string relativePath = null)
    {
        _byRagName.Clear();
        _bySceneId.Clear();
        _byNumericId.Clear();
        _aliases.Clear();
        _loaded = false;

        string path = ResolveRegistryPath(relativePath);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[BodyPartRegistry] File not found: {path}");
            return false;
        }

        string raw = File.ReadAllText(path);
        BodyPartRegistryFileDto dto;
        try
        {
            dto = JsonUtility.FromJson<BodyPartRegistryFileDto>(raw);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BodyPartRegistry] Parse failed: {ex.Message}");
            return false;
        }

        if (dto?.canonical == null || dto.canonical.Length == 0)
        {
            Debug.LogWarning("[BodyPartRegistry] canonical array is empty.");
            return false;
        }

        for (int i = 0; i < dto.canonical.Length; i++)
            RegisterEntry(Convert(dto.canonical[i]));

        ParseAliasesObject(raw);

        _loaded = true;
        Debug.Log($"[BodyPartRegistry] Loaded {_byRagName.Count} canonical body part(s) and {_aliases.Count} alias(es) from {path}.");
        return true;
    }

    static string ResolveRegistryPath(string relativePath)
    {
        if (!string.IsNullOrWhiteSpace(relativePath) && File.Exists(relativePath))
            return relativePath;

        string dataPath = Path.Combine(Application.dataPath, "JsonFile", "body_part_registry.json");
        if (File.Exists(dataPath))
            return dataPath;

        return string.IsNullOrWhiteSpace(relativePath) ? DefaultRelativePath : relativePath;
    }

    static void RegisterEntry(BodyPartEntry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.ragName))
            return;

        _byRagName[entry.ragName] = entry;
        if (!string.IsNullOrWhiteSpace(entry.ragSceneId))
            _bySceneId[entry.ragSceneId] = entry;
        if (entry.numericId > 0)
            _byNumericId[entry.numericId] = entry;
    }

    static BodyPartEntry Convert(BodyPartEntryDto dto)
    {
        if (dto == null) return null;
        return new BodyPartEntry
        {
            numericId = dto.numericId,
            ragSceneId = dto.ragSceneId ?? string.Empty,
            ragName = dto.ragName ?? string.Empty,
            proceduralBone = dto.proceduralBone ?? string.Empty,
            mixamoBone = dto.mixamoBone ?? string.Empty,
            motorSolve = dto.motorSolve ?? string.Empty,
        };
    }

    static void ParseAliasesObject(string raw)
    {
        int keyIdx = raw.IndexOf("\"aliases\"", StringComparison.Ordinal);
        if (keyIdx < 0)
            return;

        int braceStart = raw.IndexOf('{', keyIdx);
        if (braceStart < 0)
            return;

        int depth = 1;
        int i = braceStart + 1;
        while (i < raw.Length && depth > 0)
        {
            char c = raw[i];
            if (c == '{') depth++;
            else if (c == '}') depth--;
            i++;
        }

        if (depth != 0)
            return;

        string inner = raw.Substring(braceStart + 1, i - braceStart - 2);
        MatchCollection matches = Regex.Matches(inner, "\"([^\"]+)\"\\s*:\\s*\"([^\"]+)\"");
        for (int m = 0; m < matches.Count; m++)
        {
            Match match = matches[m];
            if (match.Groups.Count < 3)
                continue;
            _aliases[match.Groups[1].Value] = match.Groups[2].Value;
        }
    }

    public static bool TryGetByRagName(string ragName, out BodyPartEntry entry)
    {
        entry = null;
        if (!_loaded || string.IsNullOrWhiteSpace(ragName))
            return false;
        return _byRagName.TryGetValue(ragName.Trim(), out entry);
    }

    public static bool TryGetBySceneId(string sceneId, out BodyPartEntry entry)
    {
        entry = null;
        if (!_loaded || string.IsNullOrWhiteSpace(sceneId))
            return false;
        return _bySceneId.TryGetValue(sceneId.Trim(), out entry);
    }

    public static bool TryGetByNumericId(int numericId, out BodyPartEntry entry)
    {
        entry = null;
        if (!_loaded || numericId <= 0)
            return false;
        return _byNumericId.TryGetValue(numericId, out entry);
    }

    /// <summary>Resolves taxonomy alias to canonical ragName, or returns input if already canonical.</summary>
    public static string ResolveAlias(string nameOrAlias)
    {
        if (string.IsNullOrWhiteSpace(nameOrAlias))
            return string.Empty;

        string key = nameOrAlias.Trim();
        if (_aliases.TryGetValue(key, out string canonical))
            return canonical;
        return key;
    }

    /// <summary>Resolves alias then looks up the canonical entry.</summary>
    public static bool TryResolve(string nameOrAlias, out BodyPartEntry entry)
    {
        entry = null;
        if (!_loaded || string.IsNullOrWhiteSpace(nameOrAlias))
            return false;

        string canonical = ResolveAlias(nameOrAlias);
        return _byRagName.TryGetValue(canonical, out entry);
    }

    public static bool IsKnownEffector(string nameOrAlias)
    {
        if (string.IsNullOrWhiteSpace(nameOrAlias))
            return false;
        if (!_loaded)
            return false;

        string canonical = ResolveAlias(nameOrAlias);
        return _byRagName.ContainsKey(canonical) || _aliases.ContainsKey(nameOrAlias.Trim());
    }
}

[Serializable]
public class BodyPartEntry
{
    public int numericId;
    public string ragSceneId;
    public string ragName;
    public string proceduralBone;
    public string mixamoBone;
    public string motorSolve;

    public bool HasMixamoBone => !string.IsNullOrWhiteSpace(mixamoBone);
    public bool HasProceduralBone => !string.IsNullOrWhiteSpace(proceduralBone);
}

[Serializable]
class BodyPartRegistryFileDto
{
    public int version;
    public BodyPartEntryDto[] canonical;
}

[Serializable]
class BodyPartEntryDto
{
    public int numericId;
    public string ragSceneId;
    public string ragName;
    public string proceduralBone;
    public string mixamoBone;
    public string motorSolve;
}
