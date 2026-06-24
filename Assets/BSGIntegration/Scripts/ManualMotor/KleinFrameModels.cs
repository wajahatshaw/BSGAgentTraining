using System;
using UnityEngine;

/// <summary>Runtime model for one Klein Frame entry from mannualBuffer.json.</summary>
[Serializable]
public class KleinFrame
{
    public string kleinFrameId;
    public string stepId;
    public string agentId;
    public int zoneIndex;
    public string effectorBodyPart;
    public string rigBone;
    public string manualCommand;
    public string targetObject;
    public Vector3 coordinates;
    public float forceNewtons;
    public bool hasForceNewtons;
    public int durationMs;
    public string stateBefore;
    public string stateAfter;
    public KleinRigPose rigPose;
    public KleinHandPose handPose;

    public bool UsesUnityIk =>
        rigPose != null && string.Equals(rigPose.solve, "unity_ik", StringComparison.OrdinalIgnoreCase);

    public bool IsPendingFullBody =>
        rigPose != null && string.Equals(rigPose.solve, "pending_full_body_rig", StringComparison.OrdinalIgnoreCase);
}

[Serializable]
public class KleinRigPose
{
    public string solve;
    public string endEffector;
    public string[] ikChain;
    public Vector3 ikTarget;
    public float approachAngleDeg;
    public float contactForceN;
}

/// <summary>Per-joint bend (degrees toward the palm) for one finger: mcp=base knuckle, pip=middle, dip=tip.</summary>
[Serializable]
public class KleinFingerPose
{
    public float mcp;
    public float pip;
    public float dip;
}

/// <summary>Whole-hand pose: per-finger, per-joint bend angles (degrees) driving the press shape.</summary>
[Serializable]
public class KleinHandPose
{
    public string gesture;
    public KleinFingerPose index;
    public KleinFingerPose middle;
    public KleinFingerPose ring;
    public KleinFingerPose pinky;
    public KleinFingerPose thumb;
    public bool hasData;
}

/// <summary>One sceneStateLog entry linking a physical step to Klein metadata.</summary>
public class SceneStateKleinRef
{
    public string stepId;
    public string kleinFrameId;
    public string thread;
    public string targetObject;
    public string stateBefore;
    public string stateAfter;
}

/// <summary>JsonUtility DTOs for mannualBuffer.json (null floats sanitized before parse).</summary>
[Serializable]
class ManualBufferFileDto
{
    public KleinFrameDto[] manual_buffer;
}

[Serializable]
class KleinFrameDto
{
    public string klein_frame_id;
    public string stepId;
    public string agentId;
    public int zoneIndex;
    public string effector_body_part;
    public string rig_bone;
    public string manual_command;
    public string target_object;
    public KleinVec3Dto coordinates;
    public float force_newtons;
    public int duration_ms;
    public string state_before;
    public string state_after;
    public KleinRigPoseDto rig_pose;
    public KleinHandPoseDto hand_pose;
}

[Serializable]
class KleinFingerPoseDto
{
    public float mcp;
    public float pip;
    public float dip;
}

[Serializable]
class KleinHandPoseDto
{
    public string gesture;
    public KleinFingerPoseDto index;
    public KleinFingerPoseDto middle;
    public KleinFingerPoseDto ring;
    public KleinFingerPoseDto pinky;
    public KleinFingerPoseDto thumb;
}

[Serializable]
class KleinRigPoseDto
{
    public string solve;
    public string end_effector;
    public string[] ik_chain;
    public KleinVec3Dto ik_target;
    public float approach_angle_deg;
    public float contact_force_n;
}

[Serializable]
class KleinVec3Dto
{
    public float x;
    public float y;
    public float z;
}
