using System;
using UnityEngine;

/// <summary>Runtime model for one Klein Frame entry from mannualBuffer.json.</summary>
[Serializable]
public class KleinFrame
{
    public string kleinFrameId;
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
