using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>Loads and indexes mannualBuffer2.json Klein frames (keyed by stepId+agentId+zoneIndex).</summary>
public static class ManualBufferCatalog
{
    const string CatalogFileName = "mannualBuffer2.json";
    const string DefaultRelativePath = "Assets/JsonFile/mannualBuffer2.json";

    static string ResolveCatalogPath(string relativePath)
    {
        if (!string.IsNullOrWhiteSpace(relativePath) && File.Exists(relativePath))
            return relativePath;

        string dataPath = Path.Combine(Application.dataPath, "JsonFile", CatalogFileName);
        if (File.Exists(dataPath))
            return dataPath;

        return string.IsNullOrWhiteSpace(relativePath) ? DefaultRelativePath : relativePath;
    }

    static bool _loaded;
    static readonly Dictionary<string, KleinFrame> _byId = new Dictionary<string, KleinFrame>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, List<KleinFrame>> _byTargetCommand = new Dictionary<string, List<KleinFrame>>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, KleinFrame> _byStepKey = new Dictionary<string, KleinFrame>(StringComparer.OrdinalIgnoreCase);
    static KleinFrame _restingFrame;

    public static bool IsLoaded => _loaded;
    public static IReadOnlyDictionary<string, KleinFrame> ById => _byId;
    public static KleinFrame RestingFrame => _restingFrame;

    /// <summary>The rig binding (side + arm-chain + finger bone names) declared in the buffer, used to
    /// wire the hand/arm from DATA instead of hardcoded bone names. Taken from the first frame that
    /// declares it (the rig is the same across frames).</summary>
    public static KleinRigPose RigBinding { get; private set; }

    public static bool TryLoad(string relativePath = null)
    {
        _byId.Clear();
        _byTargetCommand.Clear();
        _byStepKey.Clear();
        _restingFrame = null;
        RigBinding = null;
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

        if (!string.IsNullOrWhiteSpace(frame.stepId))
            _byStepKey[StepKey(frame.stepId, frame.agentId, frame.zoneIndex)] = frame;

        // Capture the rig binding (bone names) from the first frame that declares one.
        if (RigBinding == null && frame.rigPose != null
            && (frame.rigPose.armChain != null || frame.rigPose.fingerBones != null))
            RigBinding = frame.rigPose;

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

    /// <summary>Exact match for a physical step by (stepId, agentId, zoneIndex) — the buffer2 association key.</summary>
    public static bool TryGetForStep(string stepId, string agentId, int zoneIndex, out KleinFrame frame)
    {
        frame = null;
        if ((!_loaded && !TryLoad()) || string.IsNullOrWhiteSpace(stepId))
            return false;
        return _byStepKey.TryGetValue(StepKey(stepId, agentId, zoneIndex), out frame);
    }

    public static string StepKey(string stepId, string agentId, int zoneIndex)
    {
        return $"{(stepId ?? string.Empty).Trim()}|{(agentId ?? string.Empty).Trim()}|{zoneIndex}".ToLowerInvariant();
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
            stepId = dto.stepId ?? string.Empty,
            agentId = dto.agentId ?? string.Empty,
            zoneIndex = dto.zoneIndex,
            effectorBodyPart = dto.effector_body_part ?? string.Empty,
            rigBone = dto.rig_bone ?? string.Empty,
            manualCommand = dto.manual_command ?? string.Empty,
            targetObject = dto.target_object ?? string.Empty,
            coordinates = ToVec3(dto.coordinates),
            sceneContactPoint = ToVec3(dto.scene_contact_point),
            hasSceneContactPoint = ToVec3(dto.scene_contact_point).sqrMagnitude > 0.0001f,
            forceNewtons = dto.force_newtons,
            hasForceNewtons = dto.force_newtons > 0.0001f,
            durationMs = dto.duration_ms > 0 ? dto.duration_ms : 100,
            stateBefore = dto.state_before ?? string.Empty,
            stateAfter = dto.state_after ?? string.Empty,
            rigPose = ConvertPose(dto.rig_pose),
            handPose = ConvertHandPose(dto.hand_pose),
            armPose = ConvertArmPose(dto.arm_pose),
        };
        return frame;
    }

    /// <summary>Map the per-frame arm_pose block. Defaults match the previous hardcoded reach so frames
    /// without an arm_pose behave exactly as before — except the elbow pole now carries a forward term so
    /// the elbow folds in front of the chest instead of behind the spine.</summary>
    static KleinArmPose ConvertArmPose(KleinArmPoseDto dto)
    {
        KleinArmPose pose = new KleinArmPose
        {
            poleSide = 0.45f,
            poleForward = 0.20f,
            poleDown = 0.28f,
            reachForwardMin = 0.36f,
            reachForwardMax = 0.50f,
            shoulderLiftDeg = 18f,
            wristHoverM = 0.012f,
            restForearmBendDeg = 0f,
            hasData = false,
        };

        if (dto == null)
            return pose;

        pose.hasData = true;
        if (dto.elbow_pole != null)
        {
            pose.poleSide = dto.elbow_pole.side;
            pose.poleForward = dto.elbow_pole.forward;
            pose.poleDown = dto.elbow_pole.down;
        }
        if (dto.reach != null)
        {
            if (dto.reach.forward_min_m > 0.0001f) pose.reachForwardMin = dto.reach.forward_min_m;
            if (dto.reach.forward_max_m > 0.0001f) pose.reachForwardMax = dto.reach.forward_max_m;
            pose.shoulderLiftDeg = dto.reach.shoulder_lift_deg;
            if (dto.reach.wrist_hover_m > 0.0001f) pose.wristHoverM = dto.reach.wrist_hover_m;
        }
        if (dto.rest != null)
            pose.restForearmBendDeg = dto.rest.forearm_bend_deg;

        return pose;
    }

    static KleinHandPose ConvertHandPose(KleinHandPoseDto dto)
    {
        if (dto == null)
            return null;

        KleinFingerPose index = ConvertFingerPose(dto.index);
        KleinFingerPose middle = ConvertFingerPose(dto.middle);
        KleinFingerPose ring = ConvertFingerPose(dto.ring);
        KleinFingerPose pinky = ConvertFingerPose(dto.pinky);
        KleinFingerPose thumb = ConvertFingerPose(dto.thumb);

        return new KleinHandPose
        {
            gesture = dto.gesture ?? string.Empty,
            index = index,
            middle = middle,
            ring = ring,
            pinky = pinky,
            thumb = thumb,
            hasData = !string.IsNullOrEmpty(dto.gesture)
                      || HasAngle(middle) || HasAngle(ring) || HasAngle(pinky) || HasAngle(thumb) || HasAngle(index),
        };
    }

    static KleinFingerPose ConvertFingerPose(KleinFingerPoseDto dto)
    {
        if (dto == null)
            return new KleinFingerPose();
        return new KleinFingerPose { mcp = dto.mcp, pip = dto.pip, dip = dto.dip };
    }

    static bool HasAngle(KleinFingerPose p)
    {
        return p != null && (Mathf.Abs(p.mcp) > 0.001f || Mathf.Abs(p.pip) > 0.001f || Mathf.Abs(p.dip) > 0.001f);
    }

    static KleinRigPose ConvertPose(KleinRigPoseDto dto)
    {
        if (dto == null) return null;
        return new KleinRigPose
        {
            solve = dto.solve ?? string.Empty,
            side = dto.side ?? string.Empty,
            endEffector = dto.end_effector ?? string.Empty,
            ikChain = dto.ik_chain ?? Array.Empty<string>(),
            armChain = ConvertArmChain(dto.arm_chain),
            fingerBones = ConvertFingerBones(dto.finger_bones),
            ikTarget = ToVec3(dto.ik_target),
            approachAngleDeg = dto.approach_angle_deg,
            contactForceN = dto.contact_force_n,
        };
    }

    static KleinArmChain ConvertArmChain(KleinArmChainDto dto)
    {
        if (dto == null) return null;
        return new KleinArmChain
        {
            shoulder = dto.shoulder,
            upperArm = dto.upper_arm,
            forearm = dto.forearm,
            hand = dto.hand,
        };
    }

    static KleinFingerBones ConvertFingerBones(KleinFingerBonesDto dto)
    {
        if (dto == null) return null;
        return new KleinFingerBones
        {
            index = dto.index,
            middle = dto.middle,
            ring = dto.ring,
            pinky = dto.pinky,
            thumb = dto.thumb,
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
