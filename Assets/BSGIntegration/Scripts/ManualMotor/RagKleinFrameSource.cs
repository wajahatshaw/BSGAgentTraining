using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Builds Klein frames for the Manual Module motor straight from the RAG (basicUI_ml2.json) instead of
/// the standalone mannualBuffer2.json. The join the RAG describes is:
///
///   physicalAgents[].steps[].klein_frame_id  →  sceneStateLog[].klein_frame_id
///
/// For each physical step we look up its Klein frame in sceneStateLog and read the body-movement data
/// directly from there: rig_bone (the actual rig body bone that moves — NOT the abstract effector name),
/// rig_pose.ik_chain / ik_target, manualCommand (PRESSING / DEPRESSING / RESTING), coordinates (used as
/// the contact-point metadata, replacing the old scene_contact_point), forceNewtons and durationMs (the
/// force applied and how long the movement lasts). habit_loop / reward blocks are intentionally ignored.
///
/// The RAG does not carry the mixamo rig binding (arm-chain / finger-bone names) or a per-finger hand
/// pose, so those are supplied here as the standard right-hand defaults (identical to what the legacy
/// buffer declared) — they describe the fixed rig, not the per-step motion.
/// </summary>
public static class RagKleinFrameSource
{
    /// <summary>Parse physicalAgents + sceneStateLog out of the RAG text and build one KleinFrame per
    /// (agent, physical step) whose klein_frame_id resolves to a sceneStateLog entry.</summary>
    public static bool TryBuildFrames(string ragText, out List<KleinFrame> frames)
    {
        frames = new List<KleinFrame>();
        if (string.IsNullOrWhiteSpace(ragText))
            return false;

        Dictionary<string, RagSceneStateEntryDto> byKleinId = ParseSceneStateLog(ragText);
        if (byKleinId.Count == 0)
            return false;

        List<RagPhysAgentDto> agents = ParsePhysicalAgents(ragText);
        if (agents.Count == 0)
        {
            Debug.LogWarning($"[RagKleinFrameSource] parsed {byKleinId.Count} sceneStateLog frame(s) but 0 physicalAgents — cannot build klein frames.");
            return false;
        }

        foreach (RagPhysAgentDto agent in agents)
        {
            if (agent?.steps == null)
                continue;

            foreach (RagPhysStepDto step in agent.steps)
            {
                if (step == null || string.IsNullOrWhiteSpace(step.stepId) || string.IsNullOrWhiteSpace(step.klein_frame_id))
                    continue;
                if (!byKleinId.TryGetValue(step.klein_frame_id, out RagSceneStateEntryDto entry) || entry == null)
                    continue;

                frames.Add(BuildFrame(agent, step, entry));
            }
        }

        Debug.Log($"[RagKleinFrameSource] parsed {byKleinId.Count} sceneStateLog frame(s) + {agents.Count} physical agent(s) → built {frames.Count} klein frame(s) from the RAG.");
        return frames.Count > 0;
    }

    static KleinFrame BuildFrame(RagPhysAgentDto agent, RagPhysStepDto step, RagSceneStateEntryDto entry)
    {
        string command = FirstToken(entry.manualCommand);
        Vector3 coord = ToVec3(entry.coordinates);

        return new KleinFrame
        {
            kleinFrameId = entry.klein_frame_id ?? step.klein_frame_id ?? string.Empty,
            stepId = step.stepId ?? string.Empty,
            agentId = agent.agentId ?? string.Empty,
            zoneIndex = agent.zoneIndex,
            effectorBodyPart = entry.effector ?? string.Empty,
            // Drive the rig from the actual body bone the RAG names, not the abstract effector.
            rigBone = !string.IsNullOrWhiteSpace(entry.rig_bone) ? entry.rig_bone : (entry.rig_pose?.end_effector ?? string.Empty),
            manualCommand = command,
            targetObject = entry.target ?? string.Empty,
            // "coordinates" is the RAG's per-step contact point (metres). We keep it as metadata / the IK
            // target — the actual Unity world press point stays resolved from the live scene station's
            // visible top (hasSceneContactPoint = false), so the finger physically lands on the object.
            coordinates = coord,
            sceneContactPoint = Vector3.zero,
            hasSceneContactPoint = false,
            forceNewtons = entry.forceNewtons,
            hasForceNewtons = entry.forceNewtons > 0.0001f,
            durationMs = entry.durationMs > 0 ? entry.durationMs : 100,
            stateBefore = entry.stateBefore ?? string.Empty,
            stateAfter = entry.stateAfter ?? string.Empty,
            rigPose = BuildRigPose(entry, coord),
            // The updated RAG does NOT define per-finger curl angles OR arm/shoulder poses, so we invent
            // none: handPose and armPose are null. The body movement is driven SOLELY by CCD-solving the
            // RAG's rig_pose.ik_chain toward ik_target (approach_angle_deg + contact_force_n). To move the
            // arm/shoulder, add those bones to the RAG's ik_chain.
            handPose = null,
            armPose = null,
        };
    }

    static KleinRigPose BuildRigPose(RagSceneStateEntryDto entry, Vector3 coord)
    {
        KleinRigPose binding = BuildDefaultRightHandBinding();
        RagRigPoseDto rp = entry.rig_pose;

        return new KleinRigPose
        {
            solve = !string.IsNullOrWhiteSpace(rp?.solve) ? rp.solve : "unity_ik",
            side = "right",
            endEffector = !string.IsNullOrWhiteSpace(rp?.end_effector)
                ? rp.end_effector
                : (!string.IsNullOrWhiteSpace(entry.rig_bone) ? entry.rig_bone : binding.endEffector),
            ikChain = (rp?.ik_chain != null && rp.ik_chain.Length > 0) ? rp.ik_chain : binding.ikChain,
            // Arm-chain and finger-bone names describe the fixed rig, not per-step data — the RAG omits
            // them, so use the standard right-hand binding.
            armChain = binding.armChain,
            fingerBones = binding.fingerBones,
            ikTarget = rp?.ik_target != null ? ToVec3(rp.ik_target) : coord,
            approachAngleDeg = rp != null ? rp.approach_angle_deg : 90f,
            contactForceN = rp != null && rp.contact_force_n > 0.0001f ? rp.contact_force_n : entry.forceNewtons,
        };
    }

    /// <summary>The standard right-hand mixamo binding (bone names + pointing-index chain). This is the
    /// fixed description of the rig body, identical to what the legacy buffer declared.</summary>
    public static KleinRigPose BuildDefaultRightHandBinding()
    {
        return new KleinRigPose
        {
            solve = "unity_ik",
            side = "right",
            endEffector = "mixamorig:RightHandIndex4_end",
            ikChain = new[]
            {
                "mixamorig:RightHand",
                "mixamorig:RightHandIndex1",
                "mixamorig:RightHandIndex2",
                "mixamorig:RightHandIndex3",
                "mixamorig:RightHandIndex4_end",
            },
            armChain = new KleinArmChain
            {
                shoulder = "mixamorig:RightShoulder",
                upperArm = "mixamorig:RightArm",
                forearm = "mixamorig:RightForeArm",
                hand = "mixamorig:RightHand",
            },
            fingerBones = new KleinFingerBones
            {
                index = new[] { "mixamorig:RightHandIndex1", "mixamorig:RightHandIndex2", "mixamorig:RightHandIndex3", "mixamorig:RightHandIndex4_end" },
                middle = new[] { "mixamorig:RightHandMiddle1", "mixamorig:RightHandMiddle2", "mixamorig:RightHandMiddle3" },
                ring = new[] { "mixamorig:RightHandRing1", "mixamorig:RightHandRing2", "mixamorig:RightHandRing3" },
                pinky = new[] { "mixamorig:RightHandPinky1", "mixamorig:RightHandPinky2", "mixamorig:RightHandPinky3" },
                thumb = new[] { "mixamorig:RightHandThumb1", "mixamorig:RightHandThumb2", "mixamorig:RightHandThumb3" },
            },
            ikTarget = Vector3.zero,
            approachAngleDeg = 90f,
            contactForceN = 0.25f,
        };
    }

    // ── RAG text parsing ──────────────────────────────────────────────────────

    static Dictionary<string, RagSceneStateEntryDto> ParseSceneStateLog(string ragText)
    {
        var byId = new Dictionary<string, RagSceneStateEntryDto>(StringComparer.OrdinalIgnoreCase);

        string arrayText = ExtractArray(ragText, "sceneStateLog");
        if (string.IsNullOrEmpty(arrayText))
            return byId;

        // Only the entry-level Klein fields matter. The huge nested "kleinFrame" blob (sexpr strings,
        // reward/habit trees, "T-2" object keys, deep nesting) makes Unity's JsonUtility choke or return
        // empty — so replace it with null BEFORE parsing. Then sanitize null floats on the mapped fields.
        string slim = StripNestedValue(arrayText, "kleinFrame");
        string sanitized = SanitizeNullFloats(slim, "forceNewtons", "approach_angle_deg", "contact_force_n");

        RagSceneStateArrayDto parsed = null;
        try { parsed = JsonUtility.FromJson<RagSceneStateArrayDto>("{\"items\":" + sanitized + "}"); }
        catch (Exception ex) { Debug.LogError($"[RagKleinFrameSource] sceneStateLog parse failed: {ex.Message}"); }

        if (parsed?.items != null)
        {
            foreach (RagSceneStateEntryDto e in parsed.items)
            {
                if (e != null && !string.IsNullOrWhiteSpace(e.klein_frame_id))
                    byId[e.klein_frame_id] = e;
            }
        }

        return byId;
    }

    static List<RagPhysAgentDto> ParsePhysicalAgents(string ragText)
    {
        var agents = new List<RagPhysAgentDto>();

        string arrayText = ExtractArray(ragText, "physicalAgents");
        if (string.IsNullOrEmpty(arrayText))
            return agents;

        // We only need agentId / zoneIndex / steps[stepId, klein_frame_id]. The steps carry large nested
        // objects (rig_pose, selection, manualModuleContract) that bloat the array to ~240 KB and can make
        // JsonUtility fail — strip them so only the fields we read remain.
        string slim = StripNestedValue(arrayText, "rig_pose");
        slim = StripNestedValue(slim, "selection");
        slim = StripNestedValue(slim, "manualModuleContract");

        RagPhysAgentArrayDto parsed = null;
        try { parsed = JsonUtility.FromJson<RagPhysAgentArrayDto>("{\"items\":" + slim + "}"); }
        catch (Exception ex) { Debug.LogError($"[RagKleinFrameSource] physicalAgents parse failed: {ex.Message}"); }

        if (parsed?.items != null)
            agents.AddRange(parsed.items);

        return agents;
    }

    /// <summary>Replace every <c>"key": { ... }</c> object value with <c>"key": null</c> so JsonUtility
    /// doesn't have to tokenize a huge/deeply-nested block it would ignore anyway. String-aware, so braces
    /// inside strings (e.g. sexpr) don't confuse the matcher.</summary>
    static string StripNestedValue(string json, string key)
    {
        string needle = "\"" + key + "\"";
        var sb = new StringBuilder(json.Length);
        int search = 0;
        while (true)
        {
            int k = json.IndexOf(needle, search, StringComparison.Ordinal);
            if (k < 0)
            {
                sb.Append(json, search, json.Length - search);
                break;
            }
            int colon = json.IndexOf(':', k + needle.Length);
            int brace = colon >= 0 ? json.IndexOf('{', colon) : -1;
            // Only strip when the value is an object (nothing but whitespace between ':' and '{').
            if (colon < 0 || brace < 0 || json.Substring(colon + 1, brace - colon - 1).Trim().Length != 0)
            {
                sb.Append(json, search, (k + needle.Length) - search);
                search = k + needle.Length;
                continue;
            }
            int end = FindMatchingBracket(json, brace, '{', '}');
            if (end < 0)
            {
                sb.Append(json, search, json.Length - search);
                break;
            }
            sb.Append(json, search, colon + 1 - search);   // "…key":
            sb.Append(" null");
            search = end + 1;
        }
        return sb.ToString();
    }

    /// <summary>Return the JSON array text (including brackets) that follows the given key.</summary>
    static string ExtractArray(string text, string key)
    {
        int keyIdx = text.IndexOf($"\"{key}\"", StringComparison.Ordinal);
        if (keyIdx < 0)
            return string.Empty;

        int arrayStart = text.IndexOf('[', keyIdx);
        if (arrayStart < 0)
            return string.Empty;

        int arrayEnd = FindMatchingBracket(text, arrayStart, '[', ']');
        if (arrayEnd < 0)
            return string.Empty;

        return text.Substring(arrayStart, arrayEnd - arrayStart + 1);
    }

    static string SanitizeNullFloats(string json, params string[] keys)
    {
        foreach (string key in keys)
            json = Regex.Replace(json, $"\"{Regex.Escape(key)}\"\\s*:\\s*null", $"\"{key}\": 0");
        return json;
    }

    static string FirstToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        int space = value.IndexOf(' ');
        return space < 0 ? value.Trim() : value.Substring(0, space).Trim();
    }

    static Vector3 ToVec3(KleinVec3Dto dto)
    {
        return dto == null ? Vector3.zero : new Vector3(dto.x, dto.y, dto.z);
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

// ── JsonUtility DTOs for the RAG (unknown fields are ignored) ──────────────────

[Serializable]
class RagRigPoseDto
{
    public string solve;
    public string end_effector;
    public string[] ik_chain;
    public KleinVec3Dto ik_target;
    public float approach_angle_deg;
    public float contact_force_n;
}

[Serializable]
class RagSceneStateEntryDto
{
    public string klein_frame_id;
    public string effector;
    public string manualCommand;
    public string target;
    public string target_id;
    public KleinVec3Dto coordinates;
    public float forceNewtons;
    public int durationMs;
    public string stateBefore;
    public string stateAfter;
    public string rig_bone;
    public string rig_bone_status;
    public RagRigPoseDto rig_pose;
}

[Serializable]
class RagSceneStateArrayDto
{
    public RagSceneStateEntryDto[] items;
}

[Serializable]
class RagPhysStepDto
{
    public string stepId;
    public string klein_frame_id;
}

[Serializable]
class RagPhysAgentDto
{
    public string agentId;
    public int zoneIndex;
    public RagPhysStepDto[] steps;
}

[Serializable]
class RagPhysAgentArrayDto
{
    public RagPhysAgentDto[] items;
}
