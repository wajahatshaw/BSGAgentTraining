using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// PO/RAG files use an API envelope and <c>unity_scene.data</c> shape.
/// This bridge normalizes that payload into a flat, SceneData-compatible JSON document used by runtime systems.
/// </summary>
public static class RagSceneJsonBridge
{
    /// <summary>Populated each time TryBuildFlatPipelineJsonFromRagOnly runs. Used by SceneGenerator to configure agent movers.</summary>
    public static string LastParsedLeaderAgentId = "M1";
    const float ZoneContentInset = 17.5f;

    static RagZoneLayoutSpacing ActiveLayoutSpacing =>
        BsgIntegrationSettings.ZoneLayoutSpacingOverride ?? RagZoneLayoutSpacing.SoloDefaults;

    static float EnvironmentSpacingMultiplier => ActiveLayoutSpacing.environmentSpacingMultiplier;
    static Vector2 EnvironmentGridAnchor => ActiveLayoutSpacing.environmentGridAnchor;
    static Vector2 EnvironmentPlacementOffset => ActiveLayoutSpacing.environmentPlacementOffset;

    static float CognitiveSpacingMultiplier => ActiveLayoutSpacing.cognitiveSpacingMultiplier;
    static Vector2 CognitiveGridAnchor => ActiveLayoutSpacing.cognitiveGridAnchor;
    static Vector2 CognitivePlacementOffset => ActiveLayoutSpacing.cognitivePlacementOffset;
    static float CognitiveOverflowGridStep => ActiveLayoutSpacing.cognitiveOverflowGridStep;
    static float CognitiveModuleBufferGapMultiplier => ActiveLayoutSpacing.cognitiveModuleBufferGapMultiplier;

    public static bool IsRagEnvelope(string rawJson)
    {
        if (string.IsNullOrEmpty(rawJson)) return false;
        return rawJson.IndexOf("\"unity_scene\"", StringComparison.Ordinal) >= 0 &&
               rawJson.IndexOf("\"result\"", StringComparison.Ordinal) >= 0;
    }

    /// <summary>Extracts the JSON object for result.unity_scene.data (inner scene payload).</summary>
    public static bool TryExtractUnitySceneDataJson(string ragFullText, out string dataJson, out string error)
    {
        dataJson = null;
        error = null;
        if (string.IsNullOrEmpty(ragFullText))
        {
            error = "empty";
            return false;
        }

        int us = ragFullText.IndexOf("\"unity_scene\"", StringComparison.Ordinal);
        if (us < 0)
        {
            error = "no_unity_scene";
            return false;
        }

        int dataKey = ragFullText.IndexOf("\"data\"", us, StringComparison.Ordinal);
        if (dataKey < 0)
        {
            error = "no_data_key";
            return false;
        }

        int colon = ragFullText.IndexOf(':', dataKey + 6);
        if (colon < 0)
        {
            error = "no_data_colon";
            return false;
        }

        int openBrace = ragFullText.IndexOf('{', colon + 1);
        if (openBrace < 0)
        {
            error = "no_data_brace";
            return false;
        }

        int closeBrace = FindMatchingBrace(ragFullText, openBrace);
        if (closeBrace < 0)
        {
            error = "unclosed_data_object";
            return false;
        }

        dataJson = ragFullText.Substring(openBrace, closeBrace - openBrace + 1);
        return true;
    }

    /// <summary>
    /// Builds a flat, SceneData-compatible JSON from RAG envelope only (no sidecar).
    /// </summary>
    public static bool TryBuildFlatPipelineJsonFromRagOnly(
        string ragFullText,
        string preferredMentalAgentId,
        out string flatJson,
        out string error)
    {
        flatJson = null;
        error = null;

        if (!TryExtractUnitySceneDataJson(ragFullText, out string dataJson, out string dataErr))
        {
            error = dataErr;
            return false;
        }

        SceneStateLogBridge.TryParseFromRagText(ragFullText);

        RagUnityDataInnerModel model = JsonUtility.FromJson<RagUnityDataInnerModel>(dataJson);
        if (model == null)
        {
            error = "parse_unity_data_failed";
            return false;
        }

        ApplyWorkstationWorldPositionsFromRag(ragFullText, model.sceneEntities);

        string sceneId = string.IsNullOrWhiteSpace(model.sceneId) ? "rag_scene" : model.sceneId;
        PlanData plan = BuildPlanFromRagContext(model.taskContext, sceneId);

        string selectedMentalId = preferredMentalAgentId;
        if (string.IsNullOrWhiteSpace(selectedMentalId) && model.mentalAgents != null && model.mentalAgents.Length > 0)
            selectedMentalId = model.mentalAgents[0].agentId;
        if (string.IsNullOrWhiteSpace(selectedMentalId))
            selectedMentalId = "M1";

        if (!TryExtractMentalAgentStepsArrayJson(dataJson, selectedMentalId, out string mentalSteps, out _))
            mentalSteps = "[]";
        if (!TryExtractPhysicalAgentStepsArrayJson(dataJson, out string physicalSteps, out _))
            physicalSteps = "[]";

        List<RagCognitiveObject> cognitiveObjects = new List<RagCognitiveObject>();
        if (model.cognitiveObjects != null)
            cognitiveObjects.AddRange(model.cognitiveObjects);

        string initialStatesJson = BuildInitialStatesJson(cognitiveObjects, model.sceneEntities, model.zones, model.targetObjects);
        string stationActionsJson = BuildStationActionsJson(cognitiveObjects, model.zones);

        // Read cognitive execution policy to determine leader agent
        string leaderAgentId = ExtractCognitiveLeaderFromJson(dataJson);
        if (string.IsNullOrWhiteSpace(leaderAgentId))
            leaderAgentId = string.IsNullOrWhiteSpace(selectedMentalId) ? "M1" : selectedMentalId;
        LastParsedLeaderAgentId = leaderAgentId;

        string agentProfilesJson = BuildAgentProfilesJson(dataJson, model.mentalAgents, model.physicalAgents);

        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"scene_id\": \"").Append(EscapeJson(sceneId)).Append("\",\n");
        sb.Append("  \"plan\": {\n");
        sb.Append("    \"planId\": \"").Append(EscapeJson(plan.planId)).Append("\",\n");
        sb.Append("    \"planName\": \"").Append(EscapeJson(plan.planName)).Append("\",\n");
        sb.Append("    \"description\": \"").Append(EscapeJson(plan.description)).Append("\",\n");
        sb.Append("    \"estimatedTotalDuration\": ").Append(ToJsonNumber(plan.estimatedTotalDuration)).Append(",\n");
        sb.Append("    \"priority\": \"").Append(EscapeJson(plan.priority)).Append("\"\n");
        sb.Append("  },\n");
        sb.Append("  \"cognitiveInteraction\": {\n");
        sb.Append("    \"enabled\": true,\n");
        sb.Append("    \"spawnBeforeAgentActions\": true,\n");
        sb.Append("    \"interactionMode\": \"light\",\n");
        sb.Append("    \"defaultProximityRadius\": 2.0,\n");
        sb.Append("    \"stationActions\": ").Append(stationActionsJson).Append("\n");
        sb.Append("  },\n");
        sb.Append("  \"proximityDetection\": {\n");
        sb.Append("    \"enabled\": true,\n");
        sb.Append("    \"detectionRadius\": 3.0,\n");
        sb.Append("    \"updateFrequency\": 0.1,\n");
        sb.Append("    \"proximityZones\": []\n");
        sb.Append("  },\n");
        sb.Append("  \"initialStates\": ").Append(initialStatesJson).Append(",\n");
        sb.Append("  \"dependencies\": [],\n");
        sb.Append("  \"sequence\": [],\n");
        sb.Append("  \"agentProfiles\": ").Append(agentProfilesJson).Append("\n");
        sb.Append("}\n");

        flatJson = sb.ToString();
        return true;
    }

    /// <summary>
    /// Merges RAG inner steps into the sidecar replica JSON so existing parsers and AgentSequenceManager keep working.
    /// </summary>
    public static bool TryMergeSidecarWithRag(
        string ragFullText,
        string sidecarFullText,
        string primaryAgentKey,
        string ragMentalAgentId,
        out string mergedFlatJson,
        out string error)
    {
        mergedFlatJson = null;
        error = null;

        if (!TryExtractUnitySceneDataJson(ragFullText, out string dataJson, out string extErr))
        {
            error = extErr;
            return false;
        }

        if (string.IsNullOrEmpty(sidecarFullText))
        {
            error = "empty_sidecar";
            return false;
        }

        string mentalStepsArray;
        if (string.IsNullOrWhiteSpace(ragMentalAgentId)) ragMentalAgentId = "M1";
        if (!TryExtractMentalAgentStepsArrayJson(dataJson, ragMentalAgentId, out mentalStepsArray, out string mErr))
        {
            error = "mental_steps:" + mErr;
            return false;
        }

        string physicalStepsArray;
        if (!TryExtractPhysicalAgentStepsArrayJson(dataJson, out physicalStepsArray, out string pErr))
        {
            error = "physical_steps:" + pErr;
            return false;
        }

        string agentProfilesSection = ExtractNamedObjectSection(sidecarFullText, "agentProfiles");
        if (string.IsNullOrEmpty(agentProfilesSection))
        {
            error = "sidecar_no_agentProfiles";
            return false;
        }

        var entries = ParseTopLevelObjectEntries(agentProfilesSection);
        if (!entries.TryGetValue(primaryAgentKey, out string agentObjJson))
        {
            error = "sidecar_missing_agent:" + primaryAgentKey;
            return false;
        }

        string patchedAgent = ReplaceJsonArrayForKey(agentObjJson, "cognitiveActionSequence", mentalStepsArray);
        patchedAgent = ReplaceJsonArrayForKey(patchedAgent, "actionSequence", physicalStepsArray);
        entries[primaryAgentKey] = patchedAgent;

        string newAgentProfilesBody = BuildAgentProfilesObjectBody(entries);
        string merged = ReplaceNamedObjectValue(sidecarFullText, "agentProfiles", newAgentProfilesBody);
        mergedFlatJson = merged;
        return true;
    }

    public static bool TryParseRagSceneMeta(string ragFullText, out string sceneId, out RagTaskContextLite taskContext)
    {
        sceneId = null;
        taskContext = null;
        if (!TryExtractUnitySceneDataJson(ragFullText, out string dataJson, out _))
            return false;
        var root = JsonUtility.FromJson<RagUnityDataInnerLite>(dataJson);
        if (root == null) return false;
        sceneId = root.sceneId;
        taskContext = root.taskContext;
        return !string.IsNullOrEmpty(sceneId) || root.taskContext != null;
    }

    /// <summary>Collect physical step targets from RAG inner JSON and add placeholder ToolState clones.</summary>
    public static void EnsurePhysicalTargetsFromRag(
        string ragFullText,
        SceneData sceneData,
        string cloneSourceObjectId = "tool_001")
    {
        if (sceneData?.initialStates == null || !TryExtractUnitySceneDataJson(ragFullText, out string dataJson, out _))
            return;

        if (!TryExtractPhysicalAgentStepsArrayJson(dataJson, out string physArray, out _))
            return;

        var ids = new HashSet<string>(StringComparer.Ordinal);
        CollectTargetObjectIdsFromStepsArray(physArray, ids);
        if (!sceneData.initialStates.TryGetValue(cloneSourceObjectId, out ToolState template))
        {
            foreach (var kvp in sceneData.initialStates)
            {
                if (!kvp.Value.isCognitiveStation)
                {
                    template = kvp.Value;
                    break;
                }
            }
        }

        if (template == null) return;

        int ghost = 0;
        foreach (string tid in ids)
        {
            if (string.IsNullOrWhiteSpace(tid) || sceneData.initialStates.ContainsKey(tid))
                continue;
            if (tid.StartsWith("cognitive_", StringComparison.OrdinalIgnoreCase))
                continue;
            // RAG initialStates use per-zone keys (e.g. scene_006_zone0) from sceneEntities[].worldPosition.
            // Physical steps still reference the bare id (scene_006). Do not spawn duplicate ghosts.
            if (InitialStatesHaveZoneQualifiedTarget(sceneData.initialStates, tid))
                continue;

            ToolState clone = CloneToolStateForAbstractTarget(template, tid, ghost++);
            sceneData.initialStates[tid] = clone;
            Debug.Log($"[RagSceneJsonBridge] Added placeholder initialState for abstract physical target '{tid}'");
        }
    }

    /// <summary>
    /// Returns true if <paramref name="bareTargetId"/> or any <c>{bareTargetId}_zone{N}</c> key exists.
    /// </summary>
    static bool InitialStatesHaveZoneQualifiedTarget(Dictionary<string, ToolState> initialStates, string bareTargetId)
    {
        if (initialStates == null || string.IsNullOrWhiteSpace(bareTargetId))
            return false;

        string zonePrefix = bareTargetId + "_zone";
        foreach (string key in initialStates.Keys)
        {
            if (key == null) continue;
            if (string.Equals(key, bareTargetId, StringComparison.Ordinal))
                return true;
            if (!key.StartsWith(zonePrefix, StringComparison.Ordinal) || key.Length <= zonePrefix.Length)
                continue;
            string suffix = key.Substring(zonePrefix.Length);
            if (suffix.Length > 0 && int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                return true;
        }

        return false;
    }

    static ToolState CloneToolStateForAbstractTarget(ToolState template, string newId, int index)
    {
        string json = JsonUtility.ToJson(template);
        ToolState t = JsonUtility.FromJson<ToolState>(json);
        if (t == null) t = new ToolState();
        t.objectId = newId;
        t.name = string.IsNullOrWhiteSpace(t.name) ? newId : t.name + " (RAG)";
        t.type = "abstract";
        t.shape = "cube";
        if (t.position == null) t.position = new PositionData();
        t.position.x = template.position.x + 0.5f * index;
        t.position.y = template.position.y;
        t.position.z = template.position.z + 0.35f * index;
        if (t.properties == null) t.properties = new ToolProperties();
        t.properties.isCognitiveStation = false;
        t.isCognitiveStation = false;
        t.isAvailable = true;
        t.isActive = false;
        if (t.requiredSkills == null) t.requiredSkills = new SkillRequirement[0];
        return t;
    }

    static void CollectTargetObjectIdsFromStepsArray(string stepsArrayJson, HashSet<string> into)
    {
        int pos = 0;
        while (pos < stepsArrayJson.Length)
        {
            int key = stepsArrayJson.IndexOf("\"targetObjectId\"", pos, StringComparison.Ordinal);
            if (key < 0) break;
            int colon = stepsArrayJson.IndexOf(':', key);
            if (colon < 0) break;
            int q1 = stepsArrayJson.IndexOf('"', colon + 1);
            if (q1 < 0) break;
            int q2 = stepsArrayJson.IndexOf('"', q1 + 1);
            if (q2 < 0) break;
            string id = stepsArrayJson.Substring(q1 + 1, q2 - q1 - 1);
            if (!string.IsNullOrEmpty(id)) into.Add(id);
            pos = q2 + 1;
        }

        // Parent sceneEntities[] ids for physical meronym targets (current + legacy key names).
        CollectQuotedFieldValues(stepsArrayJson, "main_target_object_id", into);
        CollectQuotedFieldValues(stepsArrayJson, "target_parent_id", into);
        CollectQuotedFieldValues(stepsArrayJson, "target_id", into);
    }

    static void CollectQuotedFieldValues(string json, string fieldName, HashSet<string> into)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(fieldName) || into == null)
            return;

        string needle = "\"" + fieldName + "\"";
        int pos = 0;
        while (pos < json.Length)
        {
            int key = json.IndexOf(needle, pos, StringComparison.Ordinal);
            if (key < 0) break;
            int colon = json.IndexOf(':', key);
            if (colon < 0) break;
            int q1 = json.IndexOf('"', colon + 1);
            if (q1 < 0) break;
            int q2 = json.IndexOf('"', q1 + 1);
            if (q2 < 0) break;
            string id = json.Substring(q1 + 1, q2 - q1 - 1);
            if (!string.IsNullOrEmpty(id)) into.Add(id);
            pos = q2 + 1;
        }
    }

    /// <summary>
    /// World XZ for each <c>cognitive_XXX</c> id — three VERTICAL columns (front wall → back wall = +Z),
    /// 6+5+5 unique slots for all 16 stations (no shared cells / overlaps).
    /// </summary>
    static bool TryGetLegacyZone01CognitivePosition(string objectId, out float x, out float z)
    {
        x = z = 0f;
        if (string.IsNullOrWhiteSpace(objectId)) return false;

        // THREE even columns front→back (vary Z, fixed X). 6 + 5 + 5 = all 16 stations with UNIQUE slots
        // so none share a cell. Wide X (±14) keeps clear aisles between columns after multiplayer band-fit.
        //   LEFT  x=-14 : 001–006
        //   MID   x=  0 : 007–011
        //   RIGHT x= 14 : 012–016
        switch (objectId.Trim().ToUpperInvariant())
        {
            // LEFT column — front → back
            case "COGNITIVE_001": x = -14.0f; z = 3.0f;  break;
            case "COGNITIVE_002": x = -14.0f; z = 6.0f;  break;
            case "COGNITIVE_003": x = -14.0f; z = 9.0f;  break;
            case "COGNITIVE_004": x = -14.0f; z = 12.0f; break;
            case "COGNITIVE_005": x = -14.0f; z = 15.0f; break;
            case "COGNITIVE_006": x = -14.0f; z = 18.0f; break;
            // MID column — front → back
            case "COGNITIVE_007": x = 0.0f;   z = 3.0f;  break;
            case "COGNITIVE_008": x = 0.0f;   z = 6.75f; break;
            case "COGNITIVE_009": x = 0.0f;   z = 10.5f; break;
            case "COGNITIVE_010": x = 0.0f;   z = 14.25f; break;
            case "COGNITIVE_011": x = 0.0f;   z = 18.0f; break;
            // RIGHT column — front → back
            case "COGNITIVE_012": x = 14.0f;  z = 3.0f;  break;
            case "COGNITIVE_013": x = 14.0f;  z = 6.75f; break;
            case "COGNITIVE_014": x = 14.0f;  z = 10.5f; break;
            case "COGNITIVE_015": x = 14.0f;  z = 14.25f; break;
            case "COGNITIVE_016": x = 14.0f;  z = 18.0f; break;
            case "COGNITIVE_017": x = 0.0f;   z = 20.0f; break;
            default: return false;
        }

        // Uniform spread about the grid anchor + placement offset (respects the anchor's spacing knob).
        SpreadCognitiveLocal(ref x, ref z);
        return true;
    }

    /// <summary>Pushes buffer rows farther from the module row (legacy z=9) so benches do not touch modules.</summary>
    static void ApplyModuleBufferRowGap(string objectId, ref float x, ref float z)
    {
        float mul = CognitiveModuleBufferGapMultiplier;
        if (mul <= 1.001f || string.IsNullOrWhiteSpace(objectId))
            return;

        if (!TryParseCognitiveNumericIndex(objectId, out int idx) || idx < 7)
            return;

        const float moduleRowZ = 9f;
        float delta = z - moduleRowZ;
        if (Mathf.Abs(delta) < 0.01f)
            return;

        z = moduleRowZ + delta * mul;
    }

    static bool TryParseCognitiveNumericIndex(string objectId, out int index)
    {
        index = 0;
        if (string.IsNullOrWhiteSpace(objectId))
            return false;

        string id = objectId.Trim();
        const string prefix = "cognitive_";
        if (!id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string tail = id.Substring(prefix.Length);
        int zoneIdx = tail.IndexOf("_zone", StringComparison.OrdinalIgnoreCase);
        if (zoneIdx > 0)
            tail = tail.Substring(0, zoneIdx);

        return int.TryParse(tail, out index) && index > 0;
    }

    static void SpreadEnvironmentLocal(ref float x, ref float z)
    {
        if (Mathf.Abs(EnvironmentSpacingMultiplier - 1f) >= 0.001f)
        {
            x = EnvironmentGridAnchor.x + (x - EnvironmentGridAnchor.x) * EnvironmentSpacingMultiplier;
            z = EnvironmentGridAnchor.y + (z - EnvironmentGridAnchor.y) * EnvironmentSpacingMultiplier;
        }

        x += EnvironmentPlacementOffset.x;
        z += EnvironmentPlacementOffset.y;
    }

    static void SpreadCognitiveLocal(ref float x, ref float z)
    {
        if (Mathf.Abs(CognitiveSpacingMultiplier - 1f) >= 0.001f)
        {
            x = CognitiveGridAnchor.x + (x - CognitiveGridAnchor.x) * CognitiveSpacingMultiplier;
            z = CognitiveGridAnchor.y + (z - CognitiveGridAnchor.y) * CognitiveSpacingMultiplier;
        }

        x += CognitivePlacementOffset.x;
        z += CognitivePlacementOffset.y;
    }

    static string LegacyZone01CognitiveColorHex(string objectId, string displayName, bool isBuffer)
    {
        if (isBuffer && !string.IsNullOrEmpty(displayName) &&
            displayName.IndexOf("imaginal", StringComparison.OrdinalIgnoreCase) >= 0)
            return "#7EC8E3";
        return isBuffer ? "#9B7BDE" : "#5FAEE8";
    }

    /// <summary>World-space origin for each zone in the 2x2 grid (ZoneHalf=20, GridShiftZ=18).</summary>
    static Vector3 ZoneWorldOffset(int zoneIndex)
    {
        switch (zoneIndex)
        {
            case 0: return new Vector3(-20f, 0f, -2f);
            case 1: return new Vector3( 20f, 0f, -2f);
            case 2: return new Vector3(-20f, 0f, 38f);
            case 3: return new Vector3( 20f, 0f, 38f);
            default: return Vector3.zero;
        }
    }

    /// <summary>When the RAG carries <c>sceneLayout.workstation</c>, bake each physical entity's
    /// <c>worldPosition</c> from restsOn + workstation offsets so spawn is data-driven.</summary>
    static string ExtractSceneLayoutText(string ragText)
    {
        if (string.IsNullOrEmpty(ragText))
            return string.Empty;
        int keyIdx = ragText.IndexOf("\"sceneLayout\"", StringComparison.Ordinal);
        if (keyIdx < 0)
            return string.Empty;
        int objStart = ragText.IndexOf('{', keyIdx);
        if (objStart < 0)
            return string.Empty;
        int objEnd = FindMatchingBrace(ragText, objStart);
        if (objEnd < 0)
            return string.Empty;
        return ragText.Substring(objStart, objEnd - objStart + 1);
    }

    static void ApplyWorkstationWorldPositionsFromRag(string ragFullText, RagSceneEntity[] sceneEntities)
    {
        if (sceneEntities == null || sceneEntities.Length == 0 || string.IsNullOrWhiteSpace(ragFullText))
            return;

        if (MeronymJsonParser.UsesAuthoredWorldPositions(ExtractSceneLayoutText(ragFullText)))
            return;

        WorkstationLayout layout = MeronymJsonParser.ParseLayout(ragFullText);
        if (layout == null || layout.IsEmpty)
            return;

        foreach (RagSceneEntity entity in sceneEntities)
        {
            if (entity == null || string.IsNullOrWhiteSpace(entity.id))
                continue;
            LayoutEntity baked = layout.Get(entity.id.Trim());
            if (baked == null)
                continue;

            entity.worldPosition = new RagVector3Json
            {
                x = baked.worldPosition.x,
                y = baked.worldPosition.y,
                z = baked.worldPosition.z,
            };
        }
    }

    static string BuildInitialStatesJson(
        List<RagCognitiveObject> cognitiveObjects,
        RagSceneEntity[] sceneEntities = null,
        RagZoneConfig[] zones = null,
        RagTargetObject[] legacyTargetObjects = null)
    {
        var sb = new StringBuilder();
        sb.Append("{");

        bool first = true;
        bool hasZones = zones != null && zones.Length > 0;
        int numZones = hasZones ? zones.Length : 1;
        bool useSceneEntities = sceneEntities != null && sceneEntities.Length > 0;

        for (int zoneN = 0; zoneN < numZones; zoneN++)
        {
            int zoneIndex = hasZones ? zones[zoneN].zoneIndex : 0;
            Vector3 offset = hasZones ? ZoneWorldOffset(zoneIndex) : Vector3.zero;
            int overflowSlot = 0;

            RagTargetObject[] zoneTargets = hasZones ? zones[zoneN].targetObjects : legacyTargetObjects;
            HashSet<string> cognitiveObjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> sceneEntityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < cognitiveObjects.Count; i++)
            {
                RagCognitiveObject c = cognitiveObjects[i] ?? new RagCognitiveObject();
                string baseId = string.IsNullOrWhiteSpace(c.id) ? $"cognitive_{i + 1:D3}" : c.id;
                if (!string.IsNullOrWhiteSpace(c.id))
                    cognitiveObjectIds.Add(baseId);
                if (!IsCognitiveStationType(c))
                    continue;

                string stationKey = hasZones ? $"{baseId}_zone{zoneIndex}" : baseId;
                string stationType = string.IsNullOrWhiteSpace(c.type) ? "module" : c.type.ToLowerInvariant();
                bool isBuffer = stationType.Contains("buffer");
                string shape = isBuffer ? "sphere" : "capsule";
                string typeJson = isBuffer ? "cognitive_buffer" : "cognitive_module";

                float lx, lz;
                if (!TryGetLegacyZone01CognitivePosition(baseId, out lx, out lz))
                {
                    lx = -13.5f + (overflowSlot % 8) * CognitiveOverflowGridStep;
                    lz = 19f + (overflowSlot / 8) * CognitiveOverflowGridStep;
                    SpreadCognitiveLocal(ref lx, ref lz);
                    overflowSlot++;
                }
                float wx = lx + offset.x;
                float wz = lz + offset.z;
                // Per-station zone clamp is only for the standalone/solo path. When a scene anchor is active
                // (multiplayer band-fit), it re-fits the whole cognitive cluster inside the zone walls in one
                // proportional pass — so this clamp is redundant AND harmful there: it clamps each axis
                // independently, hard-pinning any station that overshoots two insets (e.g. far-left + far-back
                // 007/008/014) to the SAME zone corner, collapsing them onto each other. Also skip while the
                // multiplayer embed flag is set (JSON may be built before UseSceneAnchorLayout is synced).
                bool anchorRemapsPositions = BsgIntegrationSettings.HasSceneAnchorLayout
                    && BsgIntegrationSettings.UseSceneAnchorLayout;
                if (hasZones && !anchorRemapsPositions && !BsgIntegrationSettings.MultiplayerEmbedMode)
                    ClampWorldPositionToZone(offset, ref wx, ref wz);

                string displayName = string.IsNullOrWhiteSpace(c.name) ? baseId : c.name;
                string effectiveState = EffectiveCognitiveObjectState(c);
                string action = InferActionFromState(effectiveState);
                string colorHex = LegacyZone01CognitiveColorHex(baseId, displayName, isBuffer);

                if (!first) sb.Append(",");
                first = false;
                sb.Append("\n    \"").Append(EscapeJson(stationKey)).Append("\": {");
                sb.Append("\n      \"objectId\": \"").Append(EscapeJson(stationKey)).Append("\",");
                sb.Append("\n      \"name\": \"").Append(EscapeJson(displayName)).Append("\",");
                sb.Append("\n      \"type\": \"").Append(typeJson).Append("\",");
                sb.Append("\n      \"shape\": \"").Append(shape).Append("\",");
                sb.Append("\n      \"moduleType\": \"").Append(EscapeJson(effectiveState ?? displayName ?? stationType)).Append("\",");
                sb.Append("\n      \"initialState\": \"").Append(EscapeJson(effectiveState)).Append("\",");
                sb.Append("\n      \"visible\": true,");
                sb.Append("\n      \"stationAction\": \"").Append(EscapeJson(action)).Append("\",");
                sb.Append("\n      \"isCognitiveStation\": true,");
                sb.Append("\n      \"isAvailable\": true,");
                sb.Append("\n      \"isActive\": false,");
                sb.Append("\n      \"currentUser\": null,");
                sb.Append("\n      \"position\": { \"x\": ").Append(ToJsonNumber(wx)).Append(", \"y\": 0.5, \"z\": ").Append(ToJsonNumber(wz)).Append(" },");
                sb.Append("\n      \"color\": \"").Append(colorHex).Append("\",");
                sb.Append("\n      \"properties\": {");
                sb.Append("\n        \"isCognitiveStation\": true,");
                sb.Append("\n        \"stationType\": \"").Append(EscapeJson(isBuffer ? "buffer" : "module")).Append("\",");
                sb.Append("\n        \"bufferOrModuleType\": \"").Append(EscapeJson(effectiveState ?? displayName ?? stationType)).Append("\",");
                sb.Append("\n        \"stationAction\": \"").Append(EscapeJson(action)).Append("\"");
                sb.Append("\n      },");
                AppendDeclarativeMetadataJson(sb, stationKey, displayName, typeJson, effectiveState, c, null);
                sb.Append(",");
                sb.Append("\n      \"requiredSkills\": []");
                sb.Append("\n    }");
            }

            for (int i = 0; i < cognitiveObjects.Count; i++)
            {
                RagCognitiveObject c = cognitiveObjects[i] ?? new RagCognitiveObject();
                if (!IsSceneEntityType(c))
                    continue;

                string baseId = string.IsNullOrWhiteSpace(c.id) ? $"scene_entity_{i + 1:D3}" : c.id.Trim();
                RagTargetObject placement = FindTargetObject(zoneTargets, baseId);
                AppendSceneEntityInitialState(sb, ref first, c, placement, baseId, hasZones, zoneIndex, offset, i);
            }

            // Physical environment: prefer sceneEntities (zone 0 layout) over zones[].targetObjects meronym cubes.
            if (useSceneEntities && zoneIndex == 0)
            {
                for (int s = 0; s < sceneEntities.Length; s++)
                {
                    RagSceneEntity entity = sceneEntities[s];
                    if (!ShouldSpawnSceneEntity(entity))
                        continue;

                    string baseId = entity.id.Trim();
                    if (!sceneEntityIds.Add(baseId))
                        continue;

                    AppendRagSceneEntityInitialState(sb, ref first, entity, baseId, hasZones, zoneIndex, offset, s);
                }
            }
            else if (!useSceneEntities && zoneTargets != null)
            {
                for (int t = 0; t < zoneTargets.Length; t++)
                {
                    RagTargetObject to = zoneTargets[t];
                    if (to == null || string.IsNullOrWhiteSpace(to.id)) continue;
                    string baseId = to.id.Trim();
                    if (baseId.StartsWith("cognitive_", StringComparison.OrdinalIgnoreCase)) continue;
                    if (cognitiveObjectIds.Contains(baseId)) continue;

                    string toolKey = hasZones ? $"{baseId}_zone{zoneIndex}" : baseId;
                    float lx2 = to.position != null ? to.position.x : 4f + t * 2f;
                    float ly  = to.position != null ? to.position.y : 0.5f;
                    float lz2 = to.position != null ? to.position.z : -6f - t * 0.5f;
                    SpreadEnvironmentLocal(ref lx2, ref lz2);
                    float wx2 = lx2 + offset.x;
                    float wz2 = lz2 + offset.z;
                    if (hasZones) ClampWorldPositionToZone(offset, ref wx2, ref wz2);
                    string wname = string.IsNullOrWhiteSpace(to.name) ? baseId : to.name;
                    string wshape = string.IsNullOrWhiteSpace(to.shape) ? "cube" : to.shape.ToLowerInvariant();
                    string wcolor = string.IsNullOrWhiteSpace(to.color) ? "#95A5A6" : to.color;

                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append("\n    \"").Append(EscapeJson(toolKey)).Append("\": {");
                    sb.Append("\n      \"objectId\": \"").Append(EscapeJson(toolKey)).Append("\",");
                    sb.Append("\n      \"name\": \"").Append(EscapeJson(wname)).Append("\",");
                    sb.Append("\n      \"type\": \"target_object\",");
                    sb.Append("\n      \"shape\": \"").Append(EscapeJson(wshape)).Append("\",");
                    sb.Append("\n      \"isCognitiveStation\": false,");
                    sb.Append("\n      \"isAvailable\": true,");
                    sb.Append("\n      \"isActive\": false,");
                    sb.Append("\n      \"currentUser\": null,");
                    sb.Append("\n      \"position\": { \"x\": ").Append(ToJsonNumber(wx2)).Append(", \"y\": ").Append(ToJsonNumber(ly)).Append(", \"z\": ").Append(ToJsonNumber(wz2)).Append(" },");
                    sb.Append("\n      \"color\": \"").Append(EscapeJson(wcolor)).Append("\",");
                    sb.Append("\n      \"properties\": { \"isCognitiveStation\": false },");
                    AppendDeclarativeMetadataJson(sb, toolKey, wname, "target_object", "", null, to);
                    sb.Append(",");
                    sb.Append("\n      \"requiredSkills\": []");
                    sb.Append("\n    }");
                }
            }
        }

        sb.Append("\n  }");
        return sb.ToString();
    }

    static string BuildStationActionsJson(List<RagCognitiveObject> cognitiveObjects, RagZoneConfig[] zones = null)
    {
        var sb = new StringBuilder();
        sb.Append("{");
        bool first = true;
        bool hasZones = zones != null && zones.Length > 0;
        int numZones = hasZones ? zones.Length : 1;

        for (int zoneN = 0; zoneN < numZones; zoneN++)
        {
            int zoneIndex = hasZones ? zones[zoneN].zoneIndex : 0;
            for (int i = 0; i < cognitiveObjects.Count; i++)
            {
                RagCognitiveObject c = cognitiveObjects[i];
                if (c == null || string.IsNullOrWhiteSpace(c.id))
                    continue;
                if (!IsCognitiveStationType(c))
                    continue;

                string baseId = c.id.Trim();
                string stationKey = hasZones ? $"{baseId}_zone{zoneIndex}" : baseId;
                if (!first) sb.Append(", ");
                first = false;
                sb.Append("\"").Append(EscapeJson(stationKey)).Append("\": \"").Append(EscapeJson(InferActionFromState(EffectiveCognitiveObjectState(c)))).Append("\"");
            }
        }

        sb.Append("}");
        return sb.ToString();
    }

    static string EffectiveCognitiveObjectState(RagCognitiveObject c)
    {
        if (c == null) return "";
        if (!string.IsNullOrWhiteSpace(c.initialState)) return c.initialState;
        if (!string.IsNullOrWhiteSpace(c.currentCognitiveState)) return c.currentCognitiveState;
        return "";
    }

    static bool IsCognitiveStationType(RagCognitiveObject c)
    {
        if (c == null) return false;
        string type = (c.type ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(type))
            return !string.IsNullOrWhiteSpace(c.id) && c.id.StartsWith("cognitive_", StringComparison.OrdinalIgnoreCase);
        return type == "module" || type == "buffer" || type == "cognitive_module" || type == "cognitive_buffer";
    }

    static bool IsSceneEntityType(RagCognitiveObject c)
    {
        if (c == null || IsCognitiveStationType(c)) return false;
        string type = (c.type ?? string.Empty).Trim().ToLowerInvariant();
        switch (type)
        {
            case "tool":
            case "artifact":
            case "material":
            case "ui_element":
            case "equipment":
            case "data":
            case "body_part":
                return true;
            default:
                return !string.IsNullOrWhiteSpace(c.id);
        }
    }

    static RagTargetObject FindTargetObject(RagTargetObject[] targets, string objectId)
    {
        if (targets == null || string.IsNullOrWhiteSpace(objectId)) return null;
        for (int i = 0; i < targets.Length; i++)
        {
            RagTargetObject target = targets[i];
            if (target == null) continue;
            if (string.Equals(target.id, objectId, StringComparison.OrdinalIgnoreCase))
                return target;
        }
        return null;
    }

    static void AppendSceneEntityInitialState(
        StringBuilder sb,
        ref bool first,
        RagCognitiveObject source,
        RagTargetObject placement,
        string baseId,
        bool hasZones,
        int zoneIndex,
        Vector3 offset,
        int fallbackIndex)
    {
        string toolKey = hasZones ? $"{baseId}_zone{zoneIndex}" : baseId;
        float lx = placement != null && placement.position != null ? placement.position.x : FallbackSceneEntityX(fallbackIndex);
        float ly = placement != null && placement.position != null ? placement.position.y : 0.5f;
        float lz = placement != null && placement.position != null ? placement.position.z : FallbackSceneEntityZ(fallbackIndex);
        SpreadEnvironmentLocal(ref lx, ref lz);
        float wx = lx + offset.x;
        float wz = lz + offset.z;
        if (hasZones) ClampWorldPositionToZone(offset, ref wx, ref wz);

        string displayName = placement != null && !string.IsNullOrWhiteSpace(placement.name)
            ? placement.name
            : (string.IsNullOrWhiteSpace(source.name) ? baseId : source.name);
        string type = string.IsNullOrWhiteSpace(source.type) ? "tool" : source.type.Trim().ToLowerInvariant();
        string shape = placement != null && !string.IsNullOrWhiteSpace(placement.shape)
            ? placement.shape.Trim().ToLowerInvariant()
            : DefaultSceneEntityShape(type);
        string color = placement != null && !string.IsNullOrWhiteSpace(placement.color)
            ? placement.color
            : DefaultSceneEntityColor(type);
        string state = EffectiveCognitiveObjectState(source);

        if (!first) sb.Append(",");
        first = false;
        sb.Append("\n    \"").Append(EscapeJson(toolKey)).Append("\": {");
        sb.Append("\n      \"objectId\": \"").Append(EscapeJson(toolKey)).Append("\",");
        sb.Append("\n      \"name\": \"").Append(EscapeJson(displayName)).Append("\",");
        sb.Append("\n      \"type\": \"").Append(EscapeJson(type)).Append("\",");
        sb.Append("\n      \"shape\": \"").Append(EscapeJson(shape)).Append("\",");
        sb.Append("\n      \"initialState\": \"").Append(EscapeJson(state)).Append("\",");
        sb.Append("\n      \"visible\": ").Append(source.visible ? "true" : "false").Append(",");
        sb.Append("\n      \"isCognitiveStation\": false,");
        sb.Append("\n      \"isAvailable\": true,");
        sb.Append("\n      \"isActive\": false,");
        sb.Append("\n      \"currentUser\": null,");
        sb.Append("\n      \"position\": { \"x\": ").Append(ToJsonNumber(wx)).Append(", \"y\": ").Append(ToJsonNumber(ly)).Append(", \"z\": ").Append(ToJsonNumber(wz)).Append(" },");
        sb.Append("\n      \"color\": \"").Append(EscapeJson(color)).Append("\",");
        sb.Append("\n      \"properties\": { \"isCognitiveStation\": false },");
        AppendDeclarativeMetadataJson(sb, toolKey, displayName, type, state, source, placement);
        sb.Append(",");
        sb.Append("\n      \"requiredSkills\": []");
        sb.Append("\n    }");
    }

    static bool ShouldSpawnSceneEntity(RagSceneEntity entity)
    {
        if (entity == null || string.IsNullOrWhiteSpace(entity.id))
            return false;
        return entity.visible;
    }

    static void AppendRagSceneEntityInitialState(
        StringBuilder sb,
        ref bool first,
        RagSceneEntity entity,
        string baseId,
        bool hasZones,
        int zoneIndex,
        Vector3 offset,
        int fallbackIndex)
    {
        string toolKey = hasZones ? $"{baseId}_zone{zoneIndex}" : baseId;

        float lx = entity.worldPosition != null ? entity.worldPosition.x
            : entity.position != null ? entity.position.x
            : FallbackSceneEntityX(fallbackIndex);
        float ly = entity.worldPosition != null ? entity.worldPosition.y
            : entity.position != null ? entity.position.y
            : 0.5f;
        float lz = entity.worldPosition != null ? entity.worldPosition.z
            : entity.position != null ? entity.position.z
            : FallbackSceneEntityZ(fallbackIndex);

        float wx = lx + offset.x;
        float wz = lz + offset.z;
        if (hasZones) ClampWorldPositionToZone(offset, ref wx, ref wz);

        string displayName = string.IsNullOrWhiteSpace(entity.name) ? baseId : entity.name;
        string type = string.IsNullOrWhiteSpace(entity.type) ? "tool" : entity.type.Trim().ToLowerInvariant();
        string shape = entity.geometry != null && !string.IsNullOrWhiteSpace(entity.geometry.shape)
            ? entity.geometry.shape.Trim().ToLowerInvariant()
            : DefaultSceneEntityShape(type);
        string color = DefaultSceneEntityColor(type);
        string state = string.IsNullOrWhiteSpace(entity.initialState) ? string.Empty : entity.initialState;
        RagCognitiveObject metadataSource = ToCognitiveObjectAdapter(entity);

        if (!first) sb.Append(",");
        first = false;
        sb.Append("\n    \"").Append(EscapeJson(toolKey)).Append("\": {");
        sb.Append("\n      \"objectId\": \"").Append(EscapeJson(toolKey)).Append("\",");
        sb.Append("\n      \"name\": \"").Append(EscapeJson(displayName)).Append("\",");
        sb.Append("\n      \"type\": \"").Append(EscapeJson(type)).Append("\",");
        sb.Append("\n      \"shape\": \"").Append(EscapeJson(shape)).Append("\",");
        sb.Append("\n      \"initialState\": \"").Append(EscapeJson(state)).Append("\",");
        sb.Append("\n      \"visible\": ").Append(entity.visible ? "true" : "false").Append(",");
        sb.Append("\n      \"isCognitiveStation\": false,");
        sb.Append("\n      \"isAvailable\": true,");
        sb.Append("\n      \"isActive\": false,");
        sb.Append("\n      \"currentUser\": null,");
        sb.Append("\n      \"position\": { \"x\": ").Append(ToJsonNumber(wx)).Append(", \"y\": ").Append(ToJsonNumber(ly)).Append(", \"z\": ").Append(ToJsonNumber(wz)).Append(" },");
        sb.Append("\n      \"color\": \"").Append(EscapeJson(color)).Append("\",");
        sb.Append("\n      \"properties\": { \"isCognitiveStation\": false },");
        AppendDeclarativeMetadataJson(sb, toolKey, displayName, type, state, metadataSource, null);
        sb.Append(",");
        sb.Append("\n      \"requiredSkills\": []");
        sb.Append("\n    }");
    }

    static RagCognitiveObject ToCognitiveObjectAdapter(RagSceneEntity entity)
    {
        if (entity == null)
            return new RagCognitiveObject();

        return new RagCognitiveObject
        {
            id = entity.id,
            type = entity.type,
            name = entity.name,
            initialState = entity.initialState,
            visible = entity.visible
        };
    }

    static void AppendDeclarativeMetadataJson(
        StringBuilder sb,
        string objectId,
        string displayName,
        string type,
        string state,
        RagCognitiveObject source,
        RagTargetObject placement)
    {
        string sourceThread = FirstNonEmpty(
            source?.semanticThread,
            source?.thread,
            source?.hypernymThread,
            placement?.semanticThread,
            placement?.thread,
            placement?.hypernymThread);
        bool hasRealFrame = source?.declarativeMetadata != null || placement?.declarativeMetadata != null || !string.IsNullOrWhiteSpace(sourceThread);
        string effectiveThread = string.IsNullOrWhiteSpace(sourceThread)
            ? BuildPlaceholderThread(objectId, displayName, type)
            : sourceThread;
        DeclarativeObjectData frame = source?.declarativeMetadata ?? placement?.declarativeMetadata;

        sb.Append("\n      \"declarativeMetadata\": {");
        sb.Append("\n        \"objectId\": \"").Append(EscapeJson(FirstNonEmpty(frame?.objectId, objectId))).Append("\",");
        sb.Append("\n        \"displayName\": \"").Append(EscapeJson(FirstNonEmpty(frame?.displayName, displayName, objectId))).Append("\",");
        sb.Append("\n        \"semanticThread\": \"").Append(EscapeJson(FirstNonEmpty(frame?.semanticThread, frame?.thread, effectiveThread))).Append("\",");
        sb.Append("\n        \"thread\": \"").Append(EscapeJson(FirstNonEmpty(frame?.thread, frame?.semanticThread, effectiveThread))).Append("\",");
        sb.Append("\n        \"relations\": ");
        AppendRelationsArrayJson(sb, frame?.relations);
        sb.Append(",");
        sb.Append("\n        \"derivatives\": ");
        AppendDerivativeArrayJson(sb, frame?.derivatives, state);
        sb.Append(",");
        sb.Append("\n        \"sequences\": ");
        AppendSequencesArrayJson(sb, frame?.sequences);
        sb.Append(",");
        sb.Append("\n        \"frequency\": ").Append(ToJsonNumber(frame != null ? frame.frequency : 0f)).Append(",");
        sb.Append("\n        \"duration\": ").Append(ToJsonNumber(frame != null ? frame.duration : 0f)).Append(",");
        sb.Append("\n        \"currentState\": \"").Append(EscapeJson(FirstNonEmpty(frame?.currentState, state))).Append("\",");
        sb.Append("\n        \"expectedState\": \"").Append(EscapeJson(frame?.expectedState ?? string.Empty)).Append("\",");
        sb.Append("\n        \"rawDeclarativeJson\": \"").Append(EscapeJson(frame?.rawDeclarativeJson ?? string.Empty)).Append("\",");
        sb.Append("\n        \"metadataSource\": \"").Append(hasRealFrame ? "rag_declarative_frame" : "unity_placeholder").Append("\",");
        sb.Append("\n        \"isPlaceholder\": ").Append(hasRealFrame ? "false" : "true");
        sb.Append("\n      }");
    }

    static void AppendRelationsArrayJson(StringBuilder sb, DeclarativeRelationData[] relations)
    {
        if (relations == null || relations.Length == 0)
        {
            sb.Append("[]");
            return;
        }

        sb.Append("[");
        for (int i = 0; i < relations.Length; i++)
        {
            DeclarativeRelationData r = relations[i] ?? new DeclarativeRelationData();
            if (i > 0) sb.Append(",");
            sb.Append("{");
            sb.Append("\"relationType\":\"").Append(EscapeJson(r.relationType)).Append("\",");
            sb.Append("\"sourceId\":\"").Append(EscapeJson(r.sourceId)).Append("\",");
            sb.Append("\"targetId\":\"").Append(EscapeJson(r.targetId)).Append("\",");
            sb.Append("\"description\":\"").Append(EscapeJson(r.description)).Append("\"");
            sb.Append("}");
        }
        sb.Append("]");
    }

    static void AppendDerivativeArrayJson(StringBuilder sb, DeclarativeDerivativeData[] derivatives, string state)
    {
        if (derivatives != null && derivatives.Length > 0)
        {
            sb.Append("[");
            for (int i = 0; i < derivatives.Length; i++)
            {
                DeclarativeDerivativeData d = derivatives[i] ?? new DeclarativeDerivativeData();
                if (i > 0) sb.Append(",");
                sb.Append("{");
                sb.Append("\"property\":\"").Append(EscapeJson(d.property)).Append("\",");
                sb.Append("\"fromState\":\"").Append(EscapeJson(d.fromState)).Append("\",");
                sb.Append("\"toState\":\"").Append(EscapeJson(d.toState)).Append("\",");
                sb.Append("\"bufferTarget\":\"").Append(EscapeJson(d.bufferTarget)).Append("\",");
                sb.Append("\"description\":\"").Append(EscapeJson(d.description)).Append("\"");
                sb.Append("}");
            }
            sb.Append("]");
            return;
        }

        if (string.IsNullOrWhiteSpace(state))
        {
            sb.Append("[]");
            return;
        }

        sb.Append("[{");
        sb.Append("\"property\":\"state\",");
        sb.Append("\"fromState\":\"").Append(EscapeJson(state)).Append("\",");
        sb.Append("\"toState\":\"\",");
        sb.Append("\"bufferTarget\":\"\",");
        sb.Append("\"description\":\"Placeholder derivative until declarative memory frame supplies state transitions.\"");
        sb.Append("}]");
    }

    static void AppendSequencesArrayJson(StringBuilder sb, DeclarativeSequenceData[] sequences)
    {
        if (sequences == null || sequences.Length == 0)
        {
            sb.Append("[]");
            return;
        }

        sb.Append("[");
        for (int i = 0; i < sequences.Length; i++)
        {
            DeclarativeSequenceData s = sequences[i] ?? new DeclarativeSequenceData();
            if (i > 0) sb.Append(",");
            sb.Append("{");
            sb.Append("\"sequenceId\":\"").Append(EscapeJson(s.sequenceId)).Append("\",");
            sb.Append("\"order\":").Append(s.order).Append(",");
            sb.Append("\"action\":\"").Append(EscapeJson(s.action)).Append("\",");
            sb.Append("\"targetObjectId\":\"").Append(EscapeJson(s.targetObjectId)).Append("\",");
            sb.Append("\"startTimeSec\":").Append(ToJsonNumber(s.startTimeSec)).Append(",");
            sb.Append("\"durationSec\":").Append(ToJsonNumber(s.durationSec));
            sb.Append("}");
        }
        sb.Append("]");
    }

    static string BuildPlaceholderThread(string objectId, string displayName, string type)
    {
        string typeToken = string.IsNullOrWhiteSpace(type) ? "object" : SanitizeThreadToken(type);
        string nameToken = SanitizeThreadToken(FirstNonEmpty(displayName, objectId, "unknown"));
        return $"placeholder/{typeToken}/{nameToken}";
    }

    static string SanitizeThreadToken(string value)
    {
        return (value ?? string.Empty).Trim().Replace(' ', '_').ToLowerInvariant();
    }

    static string FirstNonEmpty(params string[] values)
    {
        if (values == null) return string.Empty;
        for (int i = 0; i < values.Length; i++)
            if (!string.IsNullOrWhiteSpace(values[i]))
                return values[i];
        return string.Empty;
    }

    static float FallbackSceneEntityX(int index)
    {
        return 4f + (index % 4) * 3f;
    }

    static float FallbackSceneEntityZ(int index)
    {
        return -6f - (index / 4) * 3f;
    }

    static string DefaultSceneEntityShape(string type)
    {
        if (string.Equals(type, "body_part", StringComparison.OrdinalIgnoreCase))
            return "sphere";
        return "cube";
    }

    static string DefaultSceneEntityColor(string type)
    {
        switch ((type ?? string.Empty).ToLowerInvariant())
        {
            case "tool": return "#3498DB";
            case "artifact": return "#F1C40F";
            case "material": return "#95A5A6";
            case "ui_element": return "#9B59B6";
            case "equipment": return "#E67E22";
            case "body_part": return "#E8B08D";
            default: return "#95A5A6";
        }
    }

    static void ClampWorldPositionToZone(Vector3 zoneOffset, ref float x, ref float z)
    {
        x = Mathf.Clamp(x, zoneOffset.x - ZoneContentInset, zoneOffset.x + ZoneContentInset);
        z = Mathf.Clamp(z, zoneOffset.z - ZoneContentInset, zoneOffset.z + ZoneContentInset);
    }

    static string[] s_mentalColorPalette = new[]
    {
        "#8E44AD", "#9B59B6", "#6C3483", "#A569BD", "#7D3C98"
    };

    static string BuildAgentProfilesJson(string dataJson, RagMentalAgentLite[] mentalAgents, RagPhysicalAgentLite[] physicalAgents = null)
    {
        var sb = new StringBuilder();
        sb.Append("{");
        bool first = true;

        // Extract M1's steps as a template fallback for agents without their own steps
        string templateMentalSteps = "[]";
        if (mentalAgents != null && mentalAgents.Length > 0)
        {
            // Use the first agent that has steps as the template
            foreach (var tmpl in mentalAgents)
            {
                if (tmpl == null) continue;
                if (TryExtractMentalAgentStepsArrayJson(dataJson, tmpl.agentId, out string ts, out _) &&
                    !string.IsNullOrWhiteSpace(ts) && ts != "[]")
                {
                    templateMentalSteps = ts;
                    break;
                }
            }
        }
        else
        {
            TryExtractMentalAgentStepsArrayJson(dataJson, "M1", out templateMentalSteps, out _);
            if (string.IsNullOrWhiteSpace(templateMentalSteps)) templateMentalSteps = "[]";
        }

        if (mentalAgents != null && mentalAgents.Length > 0)
        {
            for (int m = 0; m < mentalAgents.Length; m++)
            {
                RagMentalAgentLite ma = mentalAgents[m];
                if (ma == null || string.IsNullOrWhiteSpace(ma.agentId)) continue;
                string mid = ma.agentId.Trim();

                if (!TryExtractMentalAgentStepsArrayJson(dataJson, mid, out string mSteps, out _) ||
                    string.IsNullOrWhiteSpace(mSteps) || mSteps == "[]")
                    mSteps = templateMentalSteps;

                string mcolor = s_mentalColorPalette[m % s_mentalColorPalette.Length];
                Vector3 mOffset = ZoneWorldOffset(ma.zoneIndex);
                float mx = mOffset.x - 6f;
                float mz = mOffset.z + 1f;
                ClampWorldPositionToZone(mOffset, ref mx, ref mz);

                if (!first) sb.Append(",");
                first = false;
                sb.Append("\n    \"").Append(EscapeJson(mid)).Append("\": {");
                sb.Append("\n      \"agentId\": \"").Append(EscapeJson(mid)).Append("\",");
                sb.Append("\n      \"name\": \"Mental Agent ").Append(EscapeJson(mid)).Append("\",");
                sb.Append("\n      \"role\": \"Mental\",");
                sb.Append("\n      \"position\": { \"x\": ").Append(ToJsonNumber(mx)).Append(", \"y\": 1.0, \"z\": ").Append(ToJsonNumber(mz)).Append(" },");
                sb.Append("\n      \"color\": \"").Append(mcolor).Append("\",");
                sb.Append("\n      \"skillLevel\": 0,");
                sb.Append("\n      \"desireLevel\": 100,");
                sb.Append("\n      \"isCompleted\": false,");
                sb.Append("\n      \"availableSkills\": [],");
                sb.Append("\n      \"actionSequence\": [],");
                sb.Append("\n      \"zoneIndex\": ").Append(ma.zoneIndex).Append(",");
                sb.Append("\n      \"enableCognitiveActionSequence\": true,");
                sb.Append("\n      \"cognitiveActionSequence\": ").Append(string.IsNullOrWhiteSpace(mSteps) ? "[]" : mSteps);
                sb.Append("\n    }");
            }
        }
        else
        {
            // Fallback: single M1
            first = false;
            sb.Append("\n    \"M1\": {");
            sb.Append("\n      \"agentId\": \"M1\",");
            sb.Append("\n      \"name\": \"Mental Agent M1\",");
            sb.Append("\n      \"role\": \"Mental\",");
            sb.Append("\n      \"position\": { \"x\": -26.0, \"y\": 1.0, \"z\": -1.0 },");
            sb.Append("\n      \"color\": \"#8E44AD\",");
            sb.Append("\n      \"skillLevel\": 0,");
            sb.Append("\n      \"desireLevel\": 100,");
            sb.Append("\n      \"isCompleted\": false,");
            sb.Append("\n      \"availableSkills\": [],");
            sb.Append("\n      \"actionSequence\": [],");
            sb.Append("\n      \"zoneIndex\": 0,");
            sb.Append("\n      \"enableCognitiveActionSequence\": true,");
            sb.Append("\n      \"cognitiveActionSequence\": ").Append(string.IsNullOrWhiteSpace(templateMentalSteps) ? "[]" : templateMentalSteps);
            sb.Append("\n    }");
        }

        // null  = "physicalAgents" key absent from JSON → use hardcoded P1 fallback
        // [] or [...]  = key is present → always respect it (even if empty = user wants no agents)
        bool hasExplicitAgents = physicalAgents != null;
        string cogTemplate = string.IsNullOrWhiteSpace(LastParsedLeaderAgentId) ? "M1" : LastParsedLeaderAgentId;
        string cogStepsForPhysical = "[]";
        if (TryExtractMentalAgentStepsArrayJson(dataJson, cogTemplate, out string leaderSteps, out _))
            cogStepsForPhysical = leaderSteps;
        if (string.IsNullOrWhiteSpace(cogStepsForPhysical) || cogStepsForPhysical == "[]")
            cogStepsForPhysical = templateMentalSteps;

        if (hasExplicitAgents)
        {
            for (int p = 0; p < physicalAgents.Length; p++)
            {
                RagPhysicalAgentLite pa = physicalAgents[p];
                if (pa == null || string.IsNullOrWhiteSpace(pa.agentId)) continue;
                string pid = pa.agentId.Trim();
                string pname = string.IsNullOrWhiteSpace(pa.name) ? ("Physical Agent " + pid) : pa.name;

                // Prefer explicit spawnPosition; otherwise derive from zone offset
                Vector3 pOffset = ZoneWorldOffset(pa.zoneIndex);
                float px = pa.spawnPosition != null ? pa.spawnPosition.x : pOffset.x + 2f;
                float py = pa.spawnPosition != null ? pa.spawnPosition.y : 1f;
                float pz = pa.spawnPosition != null ? pa.spawnPosition.z : pOffset.z + 1f;
                ClampWorldPositionToZone(pOffset, ref px, ref pz);
                string color = string.IsNullOrWhiteSpace(pa.spawnColor)
                    ? (p == 0 ? "#27AE60" : "#E67E22")
                    : pa.spawnColor;

                // Find the mental agent in the same zone to use as leader
                string leaderForZone = null;
                if (mentalAgents != null)
                {
                    foreach (var ma in mentalAgents)
                    {
                        if (ma != null && ma.zoneIndex == pa.zoneIndex)
                        {
                            leaderForZone = ma.agentId;
                            break;
                        }
                    }
                }
                if (string.IsNullOrWhiteSpace(leaderForZone))
                    leaderForZone = cogTemplate;

                // Use that zone's leader steps if available
                string pCogSteps = cogStepsForPhysical;
                if (leaderForZone != cogTemplate &&
                    TryExtractMentalAgentStepsArrayJson(dataJson, leaderForZone, out string zSteps, out _) &&
                    !string.IsNullOrWhiteSpace(zSteps) && zSteps != "[]")
                    pCogSteps = zSteps;

                string actionStepsForPid = "[]";
                if (TryExtractPhysicalAgentStepsArrayJson(dataJson, pid, out string pidSteps, out _) &&
                    !string.IsNullOrWhiteSpace(pidSteps) && pidSteps != "[]")
                    actionStepsForPid = pidSteps;
                else if (TryExtractPhysicalAgentStepsArrayJson(dataJson, null, out string anySteps, out _) &&
                         !string.IsNullOrWhiteSpace(anySteps))
                    actionStepsForPid = anySteps;

                sb.Append(",");
                sb.Append("\n    \"").Append(EscapeJson(pid)).Append("\": {");
                sb.Append("\n      \"agentId\": \"").Append(EscapeJson(pid)).Append("\",");
                sb.Append("\n      \"name\": \"").Append(EscapeJson(pname)).Append("\",");
                sb.Append("\n      \"role\": \"Physical\",");
                sb.Append("\n      \"position\": { \"x\": ").Append(ToJsonNumber(px)).Append(", \"y\": ").Append(ToJsonNumber(py)).Append(", \"z\": ").Append(ToJsonNumber(pz)).Append(" },");
                sb.Append("\n      \"color\": \"").Append(EscapeJson(color)).Append("\",");
                sb.Append("\n      \"skillLevel\": 0,");
                sb.Append("\n      \"desireLevel\": 100,");
                sb.Append("\n      \"isCompleted\": false,");
                sb.Append("\n      \"availableSkills\": [],");
                sb.Append("\n      \"zoneIndex\": ").Append(pa.zoneIndex).Append(",");
                sb.Append("\n      \"leaderAgentId\": \"").Append(EscapeJson(leaderForZone)).Append("\",");
                sb.Append("\n      \"actionSequence\": ").Append(actionStepsForPid).Append(",");
                sb.Append("\n      \"enableCognitiveActionSequence\": true,");
                sb.Append("\n      \"cognitiveActionSequence\": ").Append(pCogSteps);
                sb.Append("\n    }");
            }
        }
        else
        {
            string fallbackPhys = "[]";
            if (!TryExtractPhysicalAgentStepsArrayJson(dataJson, null, out fallbackPhys, out _) ||
                string.IsNullOrWhiteSpace(fallbackPhys))
                fallbackPhys = "[]";

            // Fallback: single hardcoded P1 when physicalAgents array is absent
            sb.Append(",");
            sb.Append("\n    \"P1\": {");
            sb.Append("\n      \"agentId\": \"P1\",");
            sb.Append("\n      \"name\": \"Physical Agent P1\",");
            sb.Append("\n      \"role\": \"Physical\",");
            sb.Append("\n      \"position\": { \"x\": -22.0, \"y\": 1.0, \"z\": -1.0 },");
            sb.Append("\n      \"color\": \"#27AE60\",");
            sb.Append("\n      \"skillLevel\": 0,");
            sb.Append("\n      \"desireLevel\": 100,");
            sb.Append("\n      \"isCompleted\": false,");
            sb.Append("\n      \"availableSkills\": [],");
            sb.Append("\n      \"zoneIndex\": 0,");
            sb.Append("\n      \"leaderAgentId\": \"M1\",");
            sb.Append("\n      \"actionSequence\": ").Append(fallbackPhys).Append(",");
            sb.Append("\n      \"enableCognitiveActionSequence\": true,");
            sb.Append("\n      \"cognitiveActionSequence\": ").Append(cogStepsForPhysical);
            sb.Append("\n    }");
        }

        sb.Append("\n  }");
        return sb.ToString();
    }

    static string ExtractCognitiveLeaderFromJson(string dataJson)
    {
        if (string.IsNullOrEmpty(dataJson)) return null;
        string policyKey = "\"cognitiveExecutionPolicy\"";
        int pIdx = dataJson.IndexOf(policyKey, StringComparison.Ordinal);
        if (pIdx < 0) return null;
        int objStart = dataJson.IndexOf('{', pIdx + policyKey.Length);
        if (objStart < 0) return null;
        int objEnd = FindMatchingBrace(dataJson, objStart);
        if (objEnd < 0) return null;
        string policyBody = dataJson.Substring(objStart, objEnd - objStart + 1);
        return ExtractJsonStringValue(policyBody, "sharedCognitiveTemplateAgentId");
    }

    static string InferActionFromState(string state)
    {
        string s = (state ?? string.Empty).ToLowerInvariant();
        if (s.Contains("goal")) return "identify";
        if (s.Contains("retrieval") || s.Contains("declarative")) return "retrieve";
        if (s.Contains("production")) return "compare";
        if (s.Contains("visual")) return "perceive";
        if (s.Contains("manual") || s.Contains("motor")) return "move";
        if (s.Contains("aural")) return "listen";
        return "learn";
    }

    static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    static string ToJsonNumber(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    static string BuildAgentProfilesObjectBody(Dictionary<string, string> entries)
    {
        var sb = new StringBuilder();
        bool first = true;
        foreach (var kvp in entries)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("\n    \"").Append(EscapeJsonKey(kvp.Key)).Append("\": ").Append(kvp.Value);
        }

        sb.Append("\n  ");
        return sb.ToString();
    }

    static string EscapeJsonKey(string key)
    {
        return key.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    static string ReplaceNamedObjectValue(string fullJson, string sectionName, string newInnerBodyWithTrailingCommaSafe)
    {
        string token = "\"" + sectionName + "\"";
        int keyIndex = fullJson.IndexOf(token, StringComparison.Ordinal);
        if (keyIndex < 0) return fullJson;

        int colonIndex = fullJson.IndexOf(":", keyIndex + token.Length, StringComparison.Ordinal);
        if (colonIndex < 0) return fullJson;

        int openIndex = fullJson.IndexOf('{', colonIndex + 1);
        if (openIndex < 0) return fullJson;

        int closeIndex = FindMatchingBrace(fullJson, openIndex);
        if (closeIndex < 0) return fullJson;

        return fullJson.Substring(0, openIndex + 1) + newInnerBodyWithTrailingCommaSafe + fullJson.Substring(closeIndex);
    }

    static string ReplaceJsonArrayForKey(string objectJson, string key, string newArrayIncludingBrackets)
    {
        string token = "\"" + key + "\"";
        int k = objectJson.IndexOf(token, StringComparison.Ordinal);
        if (k < 0)
        {
            Debug.LogWarning($"[RagSceneJsonBridge] Key '{key}' not found in agent object; leaving unchanged.");
            return objectJson;
        }

        int colon = objectJson.IndexOf(':', k + token.Length);
        if (colon < 0) return objectJson;

        int bracket = objectJson.IndexOf('[', colon + 1);
        if (bracket < 0) return objectJson;

        int close = FindMatchingBracket(objectJson, bracket);
        if (close < 0) return objectJson;

        return objectJson.Substring(0, bracket) + newArrayIncludingBrackets + objectJson.Substring(close + 1);
    }

    public static bool TryExtractMentalAgentStepsArrayJson(
        string unityDataInnerJson,
        string mentalAgentId,
        out string stepsArrayJson,
        out string error)
    {
        stepsArrayJson = null;
        error = null;

        int ma = unityDataInnerJson.IndexOf("\"mentalAgents\"", StringComparison.Ordinal);
        if (ma < 0)
        {
            error = "no_mentalAgents";
            return false;
        }

        int arrOpen = unityDataInnerJson.IndexOf('[', ma);
        if (arrOpen < 0)
        {
            error = "no_mentalAgents_array";
            return false;
        }

        int arrClose = FindMatchingBracket(unityDataInnerJson, arrOpen);
        if (arrClose < 0)
        {
            error = "unclosed_mentalAgents";
            return false;
        }

        int pos = arrOpen + 1;
        while (pos < arrClose)
        {
            int objStart = unityDataInnerJson.IndexOf('{', pos);
            if (objStart < 0 || objStart >= arrClose) break;
            int objEnd = FindMatchingBrace(unityDataInnerJson, objStart);
            if (objEnd < 0 || objEnd > arrClose) break;

            string obj = unityDataInnerJson.Substring(objStart, objEnd - objStart + 1);
            string agentIdValue = ExtractJsonStringValue(obj, "agentId");
            if (string.Equals(agentIdValue, mentalAgentId, StringComparison.OrdinalIgnoreCase))
            {
                int stepsKey = obj.IndexOf("\"steps\"", StringComparison.Ordinal);
                if (stepsKey < 0)
                {
                    error = "no_steps_in_mental_agent";
                    return false;
                }

                int colon = obj.IndexOf(':', stepsKey);
                int sq = obj.IndexOf('[', colon + 1);
                if (sq < 0)
                {
                    error = "no_steps_array";
                    return false;
                }

                int sqEnd = FindMatchingBracket(obj, sq);
                if (sqEnd < 0)
                {
                    error = "unclosed_steps";
                    return false;
                }

                stepsArrayJson = obj.Substring(sq, sqEnd - sq + 1);
                return true;
            }

            pos = objEnd + 1;
        }

        error = "mental_agent_not_found:" + mentalAgentId;
        return false;
    }

    static string ExtractJsonStringValue(string json, string key)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
            return null;

        string token = "\"" + key + "\"";
        int keyIdx = json.IndexOf(token, StringComparison.Ordinal);
        if (keyIdx < 0) return null;

        int colonIdx = json.IndexOf(':', keyIdx + token.Length);
        if (colonIdx < 0) return null;

        int i = colonIdx + 1;
        while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
        if (i >= json.Length || json[i] != '"') return null;

        int start = i + 1;
        int end = start;
        while (end < json.Length)
        {
            if (json[end] == '"' && json[end - 1] != '\\')
                break;
            end++;
        }
        if (end >= json.Length) return null;
        return json.Substring(start, end - start);
    }

    /// <summary>
    /// Extracts operational <c>steps</c> from <c>physicalAgents[]</c> (preferred) or legacy singular <c>physicalAgent</c>.
    /// When <paramref name="preferredPhysicalAgentId"/> is set, uses that entry's steps; otherwise first non-empty steps array.
    /// </summary>
    public static bool TryExtractPhysicalAgentStepsArrayJson(
        string unityDataInnerJson,
        out string stepsArrayJson,
        out string error)
    {
        return TryExtractPhysicalAgentStepsArrayJson(unityDataInnerJson, null, out stepsArrayJson, out error);
    }

    public static bool TryExtractPhysicalAgentStepsArrayJson(
        string unityDataInnerJson,
        string preferredPhysicalAgentId,
        out string stepsArrayJson,
        out string error)
    {
        stepsArrayJson = null;
        error = null;

        if (TryExtractPhysicalStepsFromAgentsArray(unityDataInnerJson, preferredPhysicalAgentId, out stepsArrayJson))
            return true;

        int legacyIdx = IndexOfSingularPhysicalAgentKey(unityDataInnerJson);
        if (legacyIdx < 0)
        {
            error = "no_physical_agent_steps";
            return false;
        }

        int objStart = unityDataInnerJson.IndexOf('{', legacyIdx);
        if (objStart < 0)
        {
            error = "no_physicalAgent_object";
            return false;
        }

        int objEnd = FindMatchingBrace(unityDataInnerJson, objStart);
        if (objEnd < 0)
        {
            error = "unclosed_physicalAgent";
            return false;
        }

        string obj = unityDataInnerJson.Substring(objStart, objEnd - objStart + 1);
        int stepsKey = obj.IndexOf("\"steps\"", StringComparison.Ordinal);
        if (stepsKey < 0)
        {
            error = "no_steps_in_physical";
            return false;
        }

        int colon = obj.IndexOf(':', stepsKey);
        int sq = obj.IndexOf('[', colon + 1);
        if (sq < 0)
        {
            error = "no_physical_steps_array";
            return false;
        }

        int sqEnd = FindMatchingBracket(obj, sq);
        if (sqEnd < 0)
        {
            error = "unclosed_physical_steps";
            return false;
        }

        stepsArrayJson = obj.Substring(sq, sqEnd - sq + 1);
        return true;
    }

    /// <summary>
    /// "\"physicalAgent\":" but not "\"physicalAgents\"" (substring collision).
    /// </summary>
    static int IndexOfSingularPhysicalAgentKey(string json)
    {
        const string tok = "\"physicalAgent\"";
        int i = 0;
        while (i < json.Length)
        {
            int idx = json.IndexOf(tok, i, StringComparison.Ordinal);
            if (idx < 0)
                return -1;
            int afterTok = idx + tok.Length;
            if (afterTok < json.Length && json[afterTok] == 's')
            {
                i = idx + 1;
                continue;
            }

            return idx;
        }

        return -1;
    }

    static bool TryExtractPhysicalStepsFromAgentsArray(
        string unityDataInnerJson,
        string preferredPhysicalAgentId,
        out string stepsArrayJson)
    {
        stepsArrayJson = null;
        int pa = unityDataInnerJson.IndexOf("\"physicalAgents\"", StringComparison.Ordinal);
        if (pa < 0)
            return false;

        int arrOpen = unityDataInnerJson.IndexOf('[', pa);
        if (arrOpen < 0)
            return false;

        int arrClose = FindMatchingBracket(unityDataInnerJson, arrOpen);
        if (arrClose < 0)
            return false;

        string firstNonEmpty = null;
        int pos = arrOpen + 1;
        while (pos < arrClose)
        {
            int objStart = unityDataInnerJson.IndexOf('{', pos);
            if (objStart < 0 || objStart >= arrClose)
                break;
            int objEnd = FindMatchingBrace(unityDataInnerJson, objStart);
            if (objEnd < 0 || objEnd > arrClose)
                break;

            string obj = unityDataInnerJson.Substring(objStart, objEnd - objStart + 1);
            string agentIdValue = ExtractJsonStringValue(obj, "agentId");

            int stepsKey = obj.IndexOf("\"steps\"", StringComparison.Ordinal);
            if (stepsKey >= 0)
            {
                int colon = obj.IndexOf(':', stepsKey);
                int sq = obj.IndexOf('[', colon + 1);
                if (sq >= 0)
                {
                    int sqEnd = FindMatchingBracket(obj, sq);
                    if (sqEnd >= 0)
                    {
                        string blob = obj.Substring(sq, sqEnd - sq + 1);
                        if (!string.IsNullOrWhiteSpace(blob) && blob != "[]")
                        {
                            if (!string.IsNullOrWhiteSpace(preferredPhysicalAgentId) &&
                                string.Equals(agentIdValue, preferredPhysicalAgentId, StringComparison.OrdinalIgnoreCase))
                            {
                                stepsArrayJson = blob;
                                return true;
                            }

                            if (firstNonEmpty == null)
                                firstNonEmpty = blob;
                        }
                    }
                }
            }

            pos = objEnd + 1;
        }

        if (!string.IsNullOrWhiteSpace(firstNonEmpty))
        {
            stepsArrayJson = firstNonEmpty;
            return true;
        }

        return false;
    }

    static Dictionary<string, string> ParseTopLevelObjectEntries(string objectBody)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
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

    static string ExtractNamedObjectSection(string json, string sectionName)
    {
        string token = "\"" + sectionName + "\"";
        int keyIndex = json.IndexOf(token, StringComparison.Ordinal);
        if (keyIndex < 0) return null;

        int colonIndex = json.IndexOf(":", keyIndex + token.Length, StringComparison.Ordinal);
        if (colonIndex < 0) return null;

        int openIndex = json.IndexOf('{', colonIndex + 1);
        if (openIndex < 0) return null;

        int closeIndex = FindMatchingBrace(json, openIndex);
        if (closeIndex < 0) return null;

        return json.Substring(openIndex + 1, closeIndex - openIndex - 1);
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

    public static string ResolveJsonFilePathBoth(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        string streamingPath = Path.Combine(Application.streamingAssetsPath, fileName);
        if (File.Exists(streamingPath)) return streamingPath;
        string assetsPath = Path.Combine(Application.dataPath, "JsonFile", fileName);
        if (File.Exists(assetsPath)) return assetsPath;
        return null;
    }

    public static PlanData BuildPlanFromRagContext(RagTaskContextLite ctx, string sceneIdFallback)
    {
        var p = new PlanData();
        p.planId = string.IsNullOrEmpty(sceneIdFallback) ? "rag_plan" : sceneIdFallback + "_rag";
        p.planName = ctx != null && !string.IsNullOrEmpty(ctx.objective) ? ctx.objective : "RAG workflow";
        p.description = ctx != null && !string.IsNullOrEmpty(ctx.task) ? ctx.task : "";
        p.estimatedTotalDuration = ctx != null ? Mathf.Max(0.1f, ctx.totalDurationSec) : 15f;
        p.priority = "high";
        return p;
    }
}

[Serializable]
public class RagUnityDataInnerLite
{
    public string sceneId;
    public RagTaskContextLite taskContext;
}

[Serializable]
public class RagUnityDataInnerModel
{
    public string sceneId;
    public RagTaskContextLite taskContext;
    public RagCognitiveObject[] cognitiveObjects;
    public RagSceneEntity[] sceneEntities;
    public RagMentalAgentLite[] mentalAgents;
    public RagZoneConfig[] zones;
    public RagTargetObject[] targetObjects;
    public RagPhysicalAgentLite[] physicalAgents;
}

[Serializable]
public class RagZoneConfig
{
    public int zoneIndex;
    public string zoneName;
    public string groundColor;
    public string mentalAgentId;
    public string physicalAgentId;
    public RagTargetObject[] targetObjects;
}

[Serializable]
public class RagTargetObject
{
    public string id;
    public string name;
    public string shape;
    public string color;
    public RagVector3Json position;
    public string semanticThread;
    public string thread;
    public string hypernymThread;
    public DeclarativeObjectData declarativeMetadata;
}

[Serializable]
public class RagPhysicalAgentLite
{
    public string agentId;
    public string name;
    public int zoneIndex;
    public RagVector3Json spawnPosition;
    public string spawnColor;
}

[Serializable]
public class RagVector3Json
{
    public float x;
    public float y;
    public float z;
}

[Serializable]
public class RagSceneEntityGeometryLite
{
    public string type;
    public string shape;
}

[Serializable]
public class RagSceneEntity
{
    public string id;
    public string type;
    public string name;
    public bool visible = true;
    public RagSceneEntityGeometryLite geometry;
    public RagVector3Json position;
    public RagVector3Json worldPosition;
    public string initialState;
    public string placementRole;
}

[Serializable]
public class RagCognitiveObject
{
    public string id;
    public string type;
    public string name;
    public string initialState;
    public string currentCognitiveState;
    public string semanticThread;
    public string thread;
    public string hypernymThread;
    public DeclarativeObjectData declarativeMetadata;
    public bool visible = true;
}

[Serializable]
public class RagMentalAgentLite
{
    public string agentId;
    public int zoneIndex;
}

[Serializable]
public class RagTaskContextLite
{
    public string task;
    public string objective;
    public string agent;
    public string occupation;
    public float totalDurationSec;
}
