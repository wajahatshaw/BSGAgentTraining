using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>Loads and indexes mannualBuffer.json Klein frames.</summary>
public static class ManualBufferCatalog
{
    const string DefaultRelativePath = "Assets/JsonFile/mannualBuffer.json";

    static string ResolveCatalogPath(string relativePath)
    {
        if (!string.IsNullOrWhiteSpace(relativePath) && File.Exists(relativePath))
            return relativePath;

        string dataPath = Path.Combine(Application.dataPath, "JsonFile", "mannualBuffer.json");
        if (File.Exists(dataPath))
            return dataPath;

        return string.IsNullOrWhiteSpace(relativePath) ? DefaultRelativePath : relativePath;
    }

    static bool _loaded;
    static readonly Dictionary<string, KleinFrame> _byId = new Dictionary<string, KleinFrame>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, List<KleinFrame>> _byTargetCommand = new Dictionary<string, List<KleinFrame>>(StringComparer.OrdinalIgnoreCase);
    static KleinFrame _restingFrame;

    public static bool IsLoaded => _loaded;
    public static IReadOnlyDictionary<string, KleinFrame> ById => _byId;
    public static KleinFrame RestingFrame => _restingFrame;

    public static bool TryLoad(string relativePath = null)
    {
        _byId.Clear();
        _byTargetCommand.Clear();
        _restingFrame = null;
        _loaded = false;

        string path = ResolveCatalogPath(relativePath);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[ManualBufferCatalog] File not found: {path}");
            return false;
        }

        string raw = File.ReadAllText(path);
        raw = raw.Replace("\"force_newtons\": null", "\"force_newtons\": 0");

        ManualBufferFileDto dto;
        try
        {
            dto = JsonUtility.FromJson<ManualBufferFileDto>(raw);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ManualBufferCatalog] Parse failed: {ex.Message}");
            return false;
        }

        if (dto?.manual_buffer == null || dto.manual_buffer.Length == 0)
        {
            Debug.LogWarning("[ManualBufferCatalog] manual_buffer array is empty.");
            return false;
        }

        for (int i = 0; i < dto.manual_buffer.Length; i++)
            RegisterFrame(Convert(dto.manual_buffer[i]));

        _loaded = true;
        Debug.Log($"[ManualBufferCatalog] Loaded {_byId.Count} Klein frame(s) from {path}.");
        return true;
    }

    static void RegisterFrame(KleinFrame frame)
    {
        if (frame == null || string.IsNullOrWhiteSpace(frame.kleinFrameId))
            return;

        _byId[frame.kleinFrameId] = frame;

        if (string.Equals(frame.manualCommand, "RESTING", StringComparison.OrdinalIgnoreCase))
            _restingFrame = frame;

        string key = TargetCommandKey(frame.targetObject, frame.manualCommand);
        if (!_byTargetCommand.TryGetValue(key, out List<KleinFrame> list))
        {
            list = new List<KleinFrame>();
            _byTargetCommand[key] = list;
        }
        list.Add(frame);
    }

    public static bool TryGetById(string kleinFrameId, out KleinFrame frame)
    {
        frame = null;
        if (!_loaded || string.IsNullOrWhiteSpace(kleinFrameId))
            return false;
        return _byId.TryGetValue(kleinFrameId, out frame);
    }

    public static bool TryGetNextForTargetCommand(string targetObject, string manualCommand, ref int sequenceIndex, out KleinFrame frame)
    {
        frame = null;
        if (!_loaded)
            return false;

        string normalizedTarget = NormalizeTargetObject(targetObject);
        string key = TargetCommandKey(normalizedTarget, manualCommand);
        if (!_byTargetCommand.TryGetValue(key, out List<KleinFrame> list) || list.Count == 0)
            return false;

        int idx = Mathf.Clamp(sequenceIndex, 0, list.Count - 1);
        frame = list[idx];
        return true;
    }

    public static bool TryConsumeNextForTargetCommand(string targetObject, string manualCommand, ref int sequenceIndex, out KleinFrame frame)
    {
        if (!TryGetNextForTargetCommand(targetObject, manualCommand, ref sequenceIndex, out frame))
            return false;
        sequenceIndex = Mathf.Clamp(sequenceIndex + 1, 0, int.MaxValue);
        return true;
    }

    static KleinFrame Convert(KleinFrameDto dto)
    {
        if (dto == null) return null;

        KleinFrame frame = new KleinFrame
        {
            kleinFrameId = dto.klein_frame_id ?? string.Empty,
            effectorBodyPart = dto.effector_body_part ?? string.Empty,
            rigBone = dto.rig_bone ?? string.Empty,
            manualCommand = dto.manual_command ?? string.Empty,
            targetObject = dto.target_object ?? string.Empty,
            coordinates = ToVec3(dto.coordinates),
            forceNewtons = dto.force_newtons,
            hasForceNewtons = dto.force_newtons > 0.0001f,
            durationMs = dto.duration_ms > 0 ? dto.duration_ms : 100,
            stateBefore = dto.state_before ?? string.Empty,
            stateAfter = dto.state_after ?? string.Empty,
            rigPose = ConvertPose(dto.rig_pose),
        };
        return frame;
    }

    static KleinRigPose ConvertPose(KleinRigPoseDto dto)
    {
        if (dto == null) return null;
        return new KleinRigPose
        {
            solve = dto.solve ?? string.Empty,
            endEffector = dto.end_effector ?? string.Empty,
            ikChain = dto.ik_chain ?? Array.Empty<string>(),
            ikTarget = ToVec3(dto.ik_target),
            approachAngleDeg = dto.approach_angle_deg,
            contactForceN = dto.contact_force_n,
        };
    }

    static Vector3 ToVec3(KleinVec3Dto dto)
    {
        if (dto == null) return Vector3.zero;
        return new Vector3(dto.x, dto.y, dto.z);
    }

    public static string TargetCommandKey(string targetObject, string manualCommand)
    {
        return $"{NormalizeTargetObject(targetObject)}|{manualCommand ?? string.Empty}".ToLowerInvariant();
    }

    public static string NormalizeTargetObject(string targetObject)
    {
        if (string.IsNullOrWhiteSpace(targetObject))
            return string.Empty;

        string t = targetObject.Trim();
        if (t.Equals("letter_keys", StringComparison.OrdinalIgnoreCase))
            return "p_key";
        return t;
    }

    public static string ThreadToManualCommand(string thread)
    {
        if (string.IsNullOrWhiteSpace(thread))
            return string.Empty;

        if (thread.IndexOf("depress", StringComparison.OrdinalIgnoreCase) >= 0)
            return "DEPRESSING";
        if (thread.IndexOf("press", StringComparison.OrdinalIgnoreCase) >= 0)
            return "PRESSING";
        if (thread.IndexOf("rest", StringComparison.OrdinalIgnoreCase) >= 0)
            return "RESTING";
        if (thread.IndexOf("scroll", StringComparison.OrdinalIgnoreCase) >= 0)
            return "PRESSING";
        return string.Empty;
    }

    public static string InferManualCommandFromStep(ActionSequenceStep step)
    {
        if (step == null) return string.Empty;

        string verb = step.actionVerb ?? string.Empty;
        string desc = step.description ?? string.Empty;
        string target = step.targetObjectName ?? string.Empty;

        if (verb.IndexOf("depress", StringComparison.OrdinalIgnoreCase) >= 0
            || desc.IndexOf("type", StringComparison.OrdinalIgnoreCase) >= 0
            || target.IndexOf("key", StringComparison.OrdinalIgnoreCase) >= 0 && target.IndexOf("mouse", StringComparison.OrdinalIgnoreCase) < 0)
            return "DEPRESSING";

        if (verb.IndexOf("press", StringComparison.OrdinalIgnoreCase) >= 0
            || target.IndexOf("mouse", StringComparison.OrdinalIgnoreCase) >= 0
            || target.IndexOf("scroll", StringComparison.OrdinalIgnoreCase) >= 0)
            return "PRESSING";

        if (verb.IndexOf("rest", StringComparison.OrdinalIgnoreCase) >= 0)
            return "RESTING";

        if (string.Equals(step.actionType, "act", StringComparison.OrdinalIgnoreCase))
            return "PRESSING";

        return string.Empty;
    }
}
