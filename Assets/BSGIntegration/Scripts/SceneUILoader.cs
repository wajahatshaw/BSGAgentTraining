using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System;

[System.Serializable]
public class SkillRequirement
{
    public string onetSkillCode;
    public string skillName;
    public string category;
    public float requiredLevel;
    public bool isCritical;
        public float reward;  // Added for skill progression
}

[System.Serializable]
public class AvailableSkill
{
    public string onetSkillCode;
    public string skillName;
    public string category;
    public float availableLevel;
    public bool isProficient;
}

[System.Serializable]
public class AgentProfile
{
    public string agentId;
    public string name;
    public string role;
    public PositionData position;
    public string color;
    public float skillLevel;  // Current skill level (0-100 scale)
    public float desireLevel; // Target skill level for completion (0-100 scale)
    /// <summary>Optional RAG alias for desire cap; copied into <see cref="desireLevel"/> when present.</summary>
    public float agentDesireLevel;
    public bool isCompleted;  // Whether agent has reached 100% of desireLevel
    public AvailableSkill[] availableSkills;  // Added for learned skills
    public Dictionary<string, float> onetSkillLevels;
    public int    zoneIndex;      // 0–3: which zone this agent belongs to
    public string leaderAgentId;  // for physical agents: the mental agent to gate on
    /// <summary>Optional ML-Agents behavior name (YAML key). If empty, runtime uses PhysicalAgentZone{zoneIndex}.</summary>
    public string mlBehaviorName;
    [Tooltip("Optional YAML behavior for zone cognitive (M_A) brain. Default CognitiveAgentZone{zone}.")]
    public string mlCognitiveBehaviorName;
}

[System.Serializable]
public class PositionData
{
    public float x;
    public float y;
    public float z;
}

[System.Serializable]
public class Precondition
{
    public string objectId;
    public string requiredState;
    public bool requiredValue;
}

[System.Serializable]
public class StateChange
{
    public string objectId;
    public string property;
    public string newValue;
}

[System.Serializable]
public class SuccessCriteria
{
    public string objectId;
    public string property;
    public string expectedValue;
}

[System.Serializable]
public class WorkflowStep
{
    public string stepId;
    public string person;
    public string tool;
    public string actionVerb;
    public string actionType;
    public SkillRequirement[] requiredSkills;
    public Precondition[] preconditions;
    public StateChange[] expectedStateChanges;
    public SuccessCriteria successCriteria;
    public float estimatedDuration;
    public string[] dependsOnSteps;
}

[System.Serializable]
public class ToolProperties
{
    public string temperature;
    public string power;
    public bool locked;
    public bool isCognitiveStation;
    public string stationType;
    public string bufferOrModuleType;
    public string stationAction;
}

[System.Serializable]
public class ToolState
{
    public string objectId;
    public string name;
    public string type;
    public string shape;
    public string initialState;
    public bool visible = true;
    public string moduleType;
    public string bufferType;
    public string actr_layer;
    public string brain_region;
    public string stationAction;
    public bool isCognitiveStation;
    public bool isAvailable;
    public bool isActive;
    public string currentUser;
    public PositionData position;
    public string color;
    public ToolProperties properties;
    public DeclarativeObjectData declarativeMetadata;
    public SkillRequirement[] requiredSkills;  // Added for skill-based learning
}

[System.Serializable]
public class Dependency
{
    public string stepId;
    public string[] dependsOn;
}

[System.Serializable]
public class PlanData
{
    public string planId;
    public string planName;
    public string description;
    public float estimatedTotalDuration;
    public string priority;
}

[System.Serializable]
public class SceneData
{
    public string scene_id;
    public PlanData plan;
    public Dictionary<string, ToolState> initialStates;
    public Dependency[] dependencies;
    public WorkflowStep[] sequence;
    public Dictionary<string, AgentProfile> agentProfiles;
}

[DefaultExecutionOrder(-40)]
public class SceneUILoader : MonoBehaviour
{
    [Header("UI References")]
    public Text workerTitleText;
    public Text currentTaskText;
    public Text taskDetailsText;
    public Text stepProgressText;
    
    [Header("JSON File")]
    [Tooltip("Pipeline JSON under Assets/JsonFile/ (or StreamingAssets). Default matches ML multi-zone scene (basicUI_ml2.json). Override per scene prefab if needed.")]
    public string jsonFileName = "basicUI_ml2.json";

    [Header("Per-zone RAG (optional)")]
    [Tooltip("Four filenames (zone 0–3) under Assets/JsonFile or StreamingAssets. When all four are set, merges into one pipeline.")]
    public string[] ragJsonFileNamesByZone = new string[0];

    [Header("Dynamic RAG (optional)")]
    [Tooltip("If set at runtime, call ReloadSceneDataFromUrl() from a button/menu or test harness — GET returns flat pipeline JSON or a full RAG envelope (same shapes as disk load). Awake always loads from disk first unless you defer generation.")]
    public string ragPipelineJsonUrl = "";

    [Tooltip("After a successful URL reload, call SceneGenerator.RegenerateScene() on the same GameObject so tools/agents match the new JSON.")]
    public bool regenerateSceneAfterUrlLoad = true;

    [Tooltip("Mental agent id inside RAG unity_scene.data.mentalAgents[] whose steps become cognitiveActionSequence. Leave blank to auto-pick the first mental agent declared in the RAG data.")]
    public string ragMentalAgentId = "";

    [Header("Zone filter")]
    [Tooltip("Bitmask of zones to keep after load (bit 0 = zone 0). 0 = all zones.")]
    public int spawnZonesMask = 0;
    
    public SceneData sceneData; // Changed to public for access by AgentSequenceManager and MLTrainingResultsWriter

    /// <summary>Flattened JSON after RAG normalization — used by AgentSequenceManager and Goal Buffer disk-style readers.</summary>
    public string EffectivePipelineJson { get; private set; }
    public string RawJsonText { get; private set; }

    /// <summary>Primary raw RAG envelope when using per-zone files (zone 0 file), or same as <see cref="RawJsonText"/>.</summary>
    public string MergedRawRagJson { get; private set; }

    /// <summary>Per-zone raw JSON (length 4) when <see cref="ragJsonFileNamesByZone"/> is used.</summary>
    public string[] PerZoneRawRagJson { get; private set; }

    /// <summary>True when the main file was normalized from a RAG envelope.</summary>
    public bool LoadedFromRagSidecarMerge { get; private set; }

    private int currentStepIndex = 0;

    void Awake()
    {
        LoadSceneData();
    }
    
    void Start()
    {
        UpdateUI();
    }
    
    void LoadSceneData()
    {
        try
        {
            if (ragJsonFileNamesByZone != null && ragJsonFileNamesByZone.Length == 4
                && !string.IsNullOrWhiteSpace(ragJsonFileNamesByZone[0]))
            {
                if (TryLoadAndMergeFourZoneRagFiles(out string mergedFlat, out string[] rawByZone))
                {
                    PerZoneRawRagJson = rawByZone;
                    MergedRawRagJson = rawByZone[0];
                    RawJsonText = rawByZone[0];
                    ProcessMergedZonePipeline(mergedFlat, rawByZone);
                    return;
                }

                Debug.LogWarning("[SceneUILoader] Per-zone RAG merge failed — falling back to jsonFileName.");
            }

            string jsonPath = ResolvePrimaryJsonPath(out string streamingPath, out string assetsPath);
            if (string.IsNullOrEmpty(jsonPath))
            {
                Debug.LogError($"JSON file not found at {streamingPath} or {assetsPath}");
                return;
            }

            string json = File.ReadAllText(jsonPath);
            Debug.Log($"Loaded JSON from: {jsonPath}");
            MergedRawRagJson = null;
            PerZoneRawRagJson = null;
            ProcessLoadedJson(json);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error loading JSON: {e.Message}");
        }
    }

    bool TryLoadAndMergeFourZoneRagFiles(out string mergedFlatPipeline, out string[] rawByZone)
    {
        mergedFlatPipeline = null;
        rawByZone = new string[4];
        var mergedProfiles = new Dictionary<string, AgentProfile>(StringComparer.OrdinalIgnoreCase);
        var mergedInitialStates = new Dictionary<string, ToolState>(StringComparer.OrdinalIgnoreCase);
        string sceneId = "merged_rag_zones";
        PlanData mergedPlan = null;

        for (int z = 0; z < 4; z++)
        {
            string fileName = ragJsonFileNamesByZone[z];
            if (string.IsNullOrWhiteSpace(fileName))
            {
                Debug.LogError($"[SceneUILoader] ragJsonFileNamesByZone[{z}] is empty.");
                return false;
            }

            string path = ResolveJsonPathForFileName(fileName.Trim(), out _, out _);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError($"[SceneUILoader] Zone {z} JSON not found: {fileName}");
                return false;
            }

            string raw = File.ReadAllText(path);
            rawByZone[z] = raw;

            string flat = raw;
            if (RagSceneJsonBridge.IsRagEnvelope(raw))
            {
                if (!RagSceneJsonBridge.TryBuildFlatPipelineJsonFromRagOnly(raw, ragMentalAgentId, out string normalized, out string err))
                {
                    Debug.LogError($"[SceneUILoader] Zone {z} RAG normalize failed: {err}");
                    return false;
                }
                flat = normalized;
            }

            var profiles = ParseAgentProfilesStatic(flat);
            foreach (var kvp in profiles)
            {
                if (kvp.Value != null)
                    kvp.Value.zoneIndex = z;
                mergedProfiles[kvp.Key] = kvp.Value;
            }

            Dictionary<string, string> stationActions = ParseCognitiveStationActionsStatic(flat);
            var states = ParseInitialStatesStatic(flat, stationActions);
            foreach (var kvp in states)
                mergedInitialStates[kvp.Key] = kvp.Value;

            if (z == 0)
            {
                SceneData first = JsonUtility.FromJson<SceneData>(flat);
                if (first != null && !string.IsNullOrEmpty(first.scene_id))
                    sceneId = first.scene_id + "_merged";
                if (first?.plan != null)
                    mergedPlan = first.plan;
            }

            Debug.Log($"[SceneUILoader] Zone {z} loaded {fileName} ({profiles.Count} agents).");
        }

        mergedFlatPipeline = BuildMergedFlatJson(sceneId, mergedPlan, mergedProfiles, mergedInitialStates);
        Debug.Log($"[SceneUILoader] Merged 4 zone RAG files → pipeline ({mergedFlatPipeline.Length} chars, {mergedProfiles.Count} agents).");
        return true;
    }

    static string BuildMergedFlatJson(
        string sceneId,
        PlanData plan,
        Dictionary<string, AgentProfile> profiles,
        Dictionary<string, ToolState> initialStates)
    {
        var sb = new System.Text.StringBuilder(8192);
        sb.Append("{\"scene_id\":\"").Append(EscapeJson(sceneId)).Append("\"");
        if (plan != null)
            sb.Append(",\"plan\":").Append(JsonUtility.ToJson(plan));
        sb.Append(",\"agentProfiles\":{");
        bool first = true;
        foreach (var kvp in profiles)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(EscapeJson(kvp.Key)).Append("\":");
            sb.Append(JsonUtility.ToJson(kvp.Value));
        }
        sb.Append("},\"initialStates\":{");
        first = true;
        foreach (var kvp in initialStates)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(EscapeJson(kvp.Key)).Append("\":");
            sb.Append(JsonUtility.ToJson(kvp.Value));
        }
        sb.Append("}}");
        return sb.ToString();
    }

    static string EscapeJson(string s) =>
        string.IsNullOrEmpty(s) ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    void ProcessMergedZonePipeline(string mergedFlat, string[] rawByZone)
    {
        LoadedFromRagSidecarMerge = true;
        EffectivePipelineJson = mergedFlat;
        try
        {
            sceneData = JsonUtility.FromJson<SceneData>(mergedFlat);
            if (sceneData == null)
            {
                Debug.LogError("[SceneUILoader] Failed to parse merged zone pipeline.");
                return;
            }

            ParseDictionariesFromJSON(mergedFlat);
            FilterSceneDataBySpawnMask();

            if (rawByZone != null && rawByZone.Length > 0 && RagSceneJsonBridge.IsRagEnvelope(rawByZone[0]))
                RagSceneJsonBridge.EnsurePhysicalTargetsFromRag(rawByZone[0], sceneData);

            AgentSequenceManager asm = AgentSequenceManager.Instance;
            if (asm != null) asm.ReloadFromLoader(this);

            SkillBasedActionSystem skillSys = FindObjectOfType<SkillBasedActionSystem>();
            if (skillSys != null) skillSys.LoadData();

            if (MLTrainingResultsWriter.Instance != null)
                MLTrainingResultsWriter.Instance.OnScenePipelineReloaded(this);

            Debug.Log($"[SceneUILoader] Merged zone pipeline ready: {sceneData.agentProfiles?.Count ?? 0} agents.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[SceneUILoader] ProcessMergedZonePipeline: {e.Message}");
        }
    }

    string ResolveJsonPathForFileName(string fileName, out string streamingPath, out string assetsPath)
    {
        streamingPath = Path.Combine(Application.streamingAssetsPath, fileName);
        assetsPath = Path.Combine(Application.dataPath, "JsonFile", fileName);
        if (File.Exists(streamingPath)) return streamingPath;
        if (File.Exists(assetsPath)) return assetsPath;
        return null;
    }

    /// <summary>
    /// Parses raw file or HTTP body: flat pipeline JSON or full RAG envelope (normalized via <see cref="RagSceneJsonBridge"/>).
    /// </summary>
    void ProcessLoadedJson(string json)
    {
        LoadedFromRagSidecarMerge = false;
        EffectivePipelineJson = null;
        try
        {
            RawJsonText = json;
            MergedRawRagJson = json;
            PerZoneRawRagJson = null;

            string pipelineJson = json;
            if (RagSceneJsonBridge.IsRagEnvelope(json))
            {
                if (!RagSceneJsonBridge.TryBuildFlatPipelineJsonFromRagOnly(json, ragMentalAgentId, out string normalized, out string normalizeErr))
                {
                    Debug.LogError($"RAG normalization failed: {normalizeErr}");
                    return;
                }

                pipelineJson = normalized;
                LoadedFromRagSidecarMerge = true;
                // Capture whichever mental agent the bridge actually selected so the Inspector field stays in sync
                if (!string.IsNullOrWhiteSpace(RagSceneJsonBridge.LastParsedLeaderAgentId))
                    ragMentalAgentId = RagSceneJsonBridge.LastParsedLeaderAgentId;
                Debug.Log($"[SceneUILoader] RAG envelope normalized to flat pipeline ({normalized.Length} chars). Mental leader: {ragMentalAgentId}");
            }

            EffectivePipelineJson = pipelineJson;

            sceneData = JsonUtility.FromJson<SceneData>(pipelineJson);

            if (sceneData == null)
            {
                Debug.LogError("Failed to parse JSON data");
                return;
            }

            if (LoadedFromRagSidecarMerge &&
                RagSceneJsonBridge.TryParseRagSceneMeta(json, out string ragSceneId, out RagTaskContextLite ctx))
            {
                if (!string.IsNullOrEmpty(ragSceneId))
                    sceneData.scene_id = ragSceneId;
                sceneData.plan = RagSceneJsonBridge.BuildPlanFromRagContext(ctx, ragSceneId);
            }

            Debug.Log("JsonUtility loaded basic structure, now parsing dictionaries manually...");
            ParseDictionariesFromJSON(pipelineJson);
            FilterSceneDataBySpawnMask();

            if (LoadedFromRagSidecarMerge && sceneData?.initialStates != null)
                RagSceneJsonBridge.EnsurePhysicalTargetsFromRag(json, sceneData);

            Debug.Log($"Successfully loaded scene: {sceneData.scene_id}");
            Debug.Log($"Found {sceneData.sequence?.Length ?? 0} workflow steps");
            Debug.Log($"AgentProfiles count: {sceneData.agentProfiles?.Count ?? 0}");
            Debug.Log($"InitialStates count: {sceneData.initialStates?.Count ?? 0}");

            AgentSequenceManager asm = AgentSequenceManager.Instance;
            if (asm != null) asm.ReloadFromLoader(this);

            // Keep SkillBasedActionSystem and ML exports in sync with the new JSON (not only AgentSequenceManager).
            SkillBasedActionSystem skillSys = FindObjectOfType<SkillBasedActionSystem>();
            if (skillSys != null)
            {
                skillSys.LoadData();
                Debug.Log("[SceneUILoader] SkillBasedActionSystem.LoadData() after pipeline JSON load.");
            }
            if (MLTrainingResultsWriter.Instance != null)
                MLTrainingResultsWriter.Instance.OnScenePipelineReloaded(this);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error parsing JSON: {e.Message}");
        }
    }

    /// <summary>GET a RAG envelope or flat pipeline JSON from a server, then replace the loaded scene (e.g. local mock RAG).</summary>
    public IEnumerator ReloadSceneDataFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            Debug.LogWarning("[SceneUILoader] ReloadSceneDataFromUrl: empty URL.");
            yield break;
        }

        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            req.timeout = 120;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[SceneUILoader] GET failed ({url}): {req.error}");
                yield break;
            }

            string body = req.downloadHandler.text;
            Debug.Log($"[SceneUILoader] Loaded {body.Length} chars from URL.");
            ProcessLoadedJson(body);
            UpdateUI();

            if (regenerateSceneAfterUrlLoad)
            {
                SceneGenerator gen = GetComponent<SceneGenerator>();
                if (gen != null)
                    gen.RegenerateScene();
                else
                    Debug.LogWarning("[SceneUILoader] No SceneGenerator on this object — geometry not rebuilt after URL load.");
            }
        }
    }

    string ResolvePrimaryJsonPath(out string streamingPath, out string assetsPath)
    {
        streamingPath = Path.Combine(Application.streamingAssetsPath, jsonFileName);
        assetsPath = Path.Combine(Application.dataPath, "JsonFile", jsonFileName);
        if (File.Exists(streamingPath))
            return streamingPath;
        if (File.Exists(assetsPath))
            return assetsPath;
        return null;
    }
    
    void ParseDictionariesFromJSON(string json)
    {
        try
        {
            Dictionary<string, string> stationActions = ParseCognitiveStationActionsStatic(json);
            sceneData.agentProfiles = ParseAgentProfilesStatic(json);
            sceneData.initialStates = ParseInitialStatesStatic(json, stationActions);
            Debug.Log($"Dictionary parse complete. Agents: {sceneData.agentProfiles.Count}, InitialStates: {sceneData.initialStates.Count}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error parsing dictionaries: {e.Message}");
        }
    }

    /// <summary>Strip agents/tools outside <see cref="spawnZonesMask"/> (0 = keep all).</summary>
    public void FilterSceneDataBySpawnMask()
    {
        int mask = spawnZonesMask != 0 ? spawnZonesMask : BsgIntegrationSettings.SpawnZonesMask;
        if (mask == 0 || sceneData == null)
            return;

        if (sceneData.agentProfiles != null)
        {
            var kept = new Dictionary<string, AgentProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in sceneData.agentProfiles)
            {
                int zone = kvp.Value?.zoneIndex ?? ExtractZoneIndexFromId(kvp.Key, 0);
                if ((mask & (1 << zone)) != 0)
                    kept[kvp.Key] = kvp.Value;
            }
            sceneData.agentProfiles = kept;
        }

        if (sceneData.initialStates != null)
        {
            var kept = new Dictionary<string, ToolState>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in sceneData.initialStates)
            {
                int zone = ExtractZoneIndexFromId(kvp.Key, 0);
                if ((mask & (1 << zone)) != 0)
                    kept[kvp.Key] = kvp.Value;
            }
            sceneData.initialStates = kept;
        }

        Debug.Log($"[SceneUILoader] Zone filter mask={mask}: agents={sceneData.agentProfiles?.Count ?? 0}, tools={sceneData.initialStates?.Count ?? 0}");
    }

    static int ExtractZoneIndexFromId(string id, int fallback)
    {
        if (string.IsNullOrWhiteSpace(id)) return fallback;
        int zoneIdx = id.LastIndexOf("_zone", StringComparison.OrdinalIgnoreCase);
        if (zoneIdx < 0) return fallback;
        string suffix = id.Substring(zoneIdx + 5);
        return int.TryParse(suffix, out int zone) ? zone : fallback;
    }

    public static Dictionary<string, AgentProfile> ParseAgentProfilesStatic(string fullJson)
    {
        var result = new Dictionary<string, AgentProfile>();
        string section = ExtractNamedObjectSection(fullJson, "agentProfiles");
        if (string.IsNullOrEmpty(section)) return result;

        foreach (var kvp in ParseTopLevelObjectEntries(section))
        {
            AgentProfile profile = JsonUtility.FromJson<AgentProfile>(kvp.Value);
            if (profile == null) continue;

            if (string.IsNullOrWhiteSpace(profile.agentId))
                profile.agentId = kvp.Key;
            if (profile.availableSkills == null)
                profile.availableSkills = new AvailableSkill[0];

            if (profile.agentDesireLevel > 0f)
                profile.desireLevel = profile.agentDesireLevel;

            result[kvp.Key] = profile;
        }

        return result;
    }

    public static Dictionary<string, ToolState> ParseInitialStatesStatic(string fullJson, Dictionary<string, string> stationActions)
    {
        var result = new Dictionary<string, ToolState>();
        string section = ExtractNamedObjectSection(fullJson, "initialStates");
        if (string.IsNullOrEmpty(section)) return result;

        foreach (var kvp in ParseTopLevelObjectEntries(section))
        {
            ToolState state = JsonUtility.FromJson<ToolState>(kvp.Value);
            if (state == null) continue;

            if (string.IsNullOrWhiteSpace(state.objectId))
                state.objectId = kvp.Key;
            if (string.IsNullOrWhiteSpace(state.name))
                state.name = kvp.Key.Replace("_", " ");
            if (string.IsNullOrWhiteSpace(state.type))
                state.type = "tool";
            if (state.requiredSkills == null)
                state.requiredSkills = new SkillRequirement[0];
            if (string.IsNullOrWhiteSpace(state.stationAction) && !string.IsNullOrWhiteSpace(state.properties?.stationAction))
                state.stationAction = state.properties.stationAction;
            if ((string.IsNullOrWhiteSpace(state.stationAction) || state.stationAction == "learn") &&
                stationActions != null &&
                stationActions.TryGetValue(kvp.Key, out string mappedAction) &&
                !string.IsNullOrWhiteSpace(mappedAction))
            {
                state.stationAction = mappedAction;
            }
            if (string.IsNullOrWhiteSpace(state.moduleType) && !string.IsNullOrWhiteSpace(state.properties?.bufferOrModuleType))
                state.moduleType = state.properties.bufferOrModuleType;

            bool idLooksCognitive = kvp.Key.StartsWith("cognitive_", StringComparison.OrdinalIgnoreCase);
            bool layerLooksCognitive = !string.IsNullOrWhiteSpace(state.actr_layer);
            bool propertyLooksCognitive = state.properties != null && (state.properties.isCognitiveStation || !string.IsNullOrWhiteSpace(state.properties.stationType));
            state.isCognitiveStation = state.isCognitiveStation || idLooksCognitive || layerLooksCognitive || propertyLooksCognitive;

            result[kvp.Key] = state;
        }

        return result;
    }

    public static Dictionary<string, string> ParseCognitiveStationActionsStatic(string fullJson)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string cognitiveSection = ExtractNamedObjectSection(fullJson, "cognitiveInteraction");
        if (string.IsNullOrEmpty(cognitiveSection)) return result;

        string wrapped = "{ " + cognitiveSection + " }";
        string actionSection = ExtractNamedObjectSection(wrapped, "stationActions");
        if (string.IsNullOrEmpty(actionSection)) return result;

        int i = 0;
        while (i < actionSection.Length)
        {
            while (i < actionSection.Length && (char.IsWhiteSpace(actionSection[i]) || actionSection[i] == ',')) i++;
            if (i >= actionSection.Length || actionSection[i] != '"') break;

            int keyStart = i + 1;
            int keyEnd = actionSection.IndexOf('"', keyStart);
            if (keyEnd < 0) break;
            string key = actionSection.Substring(keyStart, keyEnd - keyStart);

            int colon = actionSection.IndexOf(':', keyEnd);
            if (colon < 0) break;
            int valueStart = colon + 1;
            while (valueStart < actionSection.Length && char.IsWhiteSpace(actionSection[valueStart])) valueStart++;
            if (valueStart >= actionSection.Length || actionSection[valueStart] != '"')
            {
                i = valueStart + 1;
                continue;
            }

            int valueEnd = actionSection.IndexOf('"', valueStart + 1);
            if (valueEnd < 0) break;
            string value = actionSection.Substring(valueStart + 1, valueEnd - valueStart - 1);
            result[key] = value;
            i = valueEnd + 1;
        }

        return result;
    }

    static string ExtractNamedObjectSection(string json, string sectionName)
    {
        string token = $"\"{sectionName}\"";
        int keyIndex = json.IndexOf(token, StringComparison.Ordinal);
        if (keyIndex < 0) return null;

        int colonIndex = json.IndexOf(":", keyIndex + token.Length, StringComparison.Ordinal);
        if (colonIndex < 0) return null;

        int openIndex = json.IndexOf("{", colonIndex + 1, StringComparison.Ordinal);
        if (openIndex < 0) return null;

        int closeIndex = FindMatchingBrace(json, openIndex);
        if (closeIndex < 0) return null;

        return json.Substring(openIndex + 1, closeIndex - openIndex - 1);
    }

    static Dictionary<string, string> ParseTopLevelObjectEntries(string objectBody)
    {
        var result = new Dictionary<string, string>();
        int i = 0;
        while (i < objectBody.Length)
        {
            while (i < objectBody.Length && (char.IsWhiteSpace(objectBody[i]) || objectBody[i] == ',')) i++;
            if (i >= objectBody.Length) break;
            if (objectBody[i] != '"') break;

            int keyStart = i + 1;
            int keyEnd = objectBody.IndexOf('"', keyStart);
            if (keyEnd < 0) break;
            string key = objectBody.Substring(keyStart, keyEnd - keyStart);

            int colon = objectBody.IndexOf(":", keyEnd, StringComparison.Ordinal);
            if (colon < 0) break;

            int valueStart = colon + 1;
            while (valueStart < objectBody.Length && char.IsWhiteSpace(objectBody[valueStart])) valueStart++;
            if (valueStart >= objectBody.Length || objectBody[valueStart] != '{')
            {
                i = valueStart + 1;
                continue;
            }

            int valueEnd = FindMatchingBrace(objectBody, valueStart);
            if (valueEnd < 0) break;

            string valueJson = objectBody.Substring(valueStart, valueEnd - valueStart + 1);
            result[key] = valueJson;
            i = valueEnd + 1;
        }

        return result;
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
    
    void UpdateUI()
    {
        if (sceneData == null) return;
        
        // Update Worker Title
        if (workerTitleText != null && sceneData.sequence != null && sceneData.sequence.Length > 0)
        {
            string currentPerson = sceneData.sequence[currentStepIndex].person;
            workerTitleText.text = $"Worker: {currentPerson}";
        }
        
        // Update Current Task
        if (currentTaskText != null && sceneData.sequence != null && sceneData.sequence.Length > 0)
        {
            var currentStep = sceneData.sequence[currentStepIndex];
            string taskDescription = $"{currentStep.stepId} - {currentStep.actionVerb} on {currentStep.tool}";
            currentTaskText.text = $"Current Task: {taskDescription}";
        }
        
        // Update Task Details
        if (taskDetailsText != null && sceneData.sequence != null && sceneData.sequence.Length > 0)
        {
            var currentStep = sceneData.sequence[currentStepIndex];
            string details = $"Action: {currentStep.actionVerb}\n";
            details += $"Tool: {currentStep.tool}\n";
            details += $"Duration: {currentStep.estimatedDuration} minutes\n";
            details += $"Type: {currentStep.actionType}";
            
            if (currentStep.requiredSkills != null && currentStep.requiredSkills.Length > 0)
            {
                details += $"\nRequired Skills:";
                foreach (var skill in currentStep.requiredSkills)
                {
                    details += $"\n- {skill.skillName} (Level {skill.requiredLevel})";
                    if (skill.isCritical) details += " [CRITICAL]";
                }
            }
            
            taskDetailsText.text = details;
        }
        
        // Update Step Progress
        if (stepProgressText != null && sceneData.sequence != null)
        {
            stepProgressText.text = $"Step {currentStepIndex + 1} of {sceneData.sequence.Length}";
        }
    }
    
    // Public method to advance to next step
    public void NextStep()
    {
        if (sceneData?.sequence != null && currentStepIndex < sceneData.sequence.Length - 1)
        {
            currentStepIndex++;
            UpdateUI();
        }
    }
    
    // Public method to go to previous step
    public void PreviousStep()
    {
        if (currentStepIndex > 0)
        {
            currentStepIndex--;
            UpdateUI();
        }
    }
    
    // Method to get current step data
    public WorkflowStep GetCurrentStep()
    {
        if (sceneData?.sequence != null && currentStepIndex < sceneData.sequence.Length)
        {
            return sceneData.sequence[currentStepIndex];
        }
        return null;
    }
    
    // Method to check if current step can be executed
    public bool CanExecuteCurrentStep()
    {
        var currentStep = GetCurrentStep();
        if (currentStep == null) return false;
        
        // Check dependencies
        if (currentStep.dependsOnSteps != null && currentStep.dependsOnSteps.Length > 0)
        {
            // This is a simplified check - in a real implementation you'd track completed steps
            return false; // For now, assume dependent steps can't be executed
        }
        
        return true;
    }
    
    // Method to get scene data for scene generation
    public SceneData GetSceneData()
    {
        return sceneData;
    }

    /// <summary>Force a fresh parse of jsonFileName. Call after changing jsonFileName at runtime.</summary>
    public void ForceReloadSceneData()
    {
        LoadSceneData();
    }

    /// <summary>Starts <see cref="ReloadSceneDataFromUrl"/> using <see cref="ragPipelineJsonUrl"/>.</summary>
    public void ForceReloadSceneDataFromConfiguredUrl()
    {
        if (string.IsNullOrWhiteSpace(ragPipelineJsonUrl))
        {
            Debug.LogWarning("[SceneUILoader] ragPipelineJsonUrl is empty.");
            return;
        }
        StartCoroutine(ReloadSceneDataFromUrl(ragPipelineJsonUrl.Trim()));
    }
}
