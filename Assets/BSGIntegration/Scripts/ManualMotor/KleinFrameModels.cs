using System;
using UnityEngine;

// "DTO" = Data Transfer Object
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
    public Vector3 sceneContactPoint;     // Unity world-space press point (our field; backend coords are UI-space)
    public bool hasSceneContactPoint;
    public float forceNewtons;
    public bool hasForceNewtons;
    public int durationMs;
    public string stateBefore;
    public string stateAfter;
    public KleinRigPose rigPose;
    public KleinHandPose handPose;
    public KleinArmPose armPose;

    public bool UsesUnityIk =>
        rigPose != null && string.Equals(rigPose.solve, "unity_ik", StringComparison.OrdinalIgnoreCase);

    public bool IsPendingFullBody =>
        rigPose != null && string.Equals(rigPose.solve, "pending_full_body_rig", StringComparison.OrdinalIgnoreCase);
}

[Serializable]
public class KleinRigPose
{
    public string solve;
    public string side;            // "right" / "left" — which hand/arm this frame drives
    public string endEffector;
    public string[] ikChain;
    public KleinArmChain armChain;       // bone names the arm IK rotates (shoulder→upper-arm→forearm→hand)
    public KleinFingerBones fingerBones; // per-finger joint bone names curled by hand_pose
    public Vector3 ikTarget;
    public float approachAngleDeg;
    public float contactForceN;
}

/// <summary>The four arm-chain bone names the two-bone IK rotates to reach the contact.</summary>
[Serializable]
public class KleinArmChain
{
    public string shoulder;   // clavicle
    public string upperArm;   // shoulder joint
    public string forearm;    // elbow
    public string hand;       // wrist
}

/// <summary>Per-finger joint bone names (base → tip), so the rig binding is declared in data, not code.</summary>
[Serializable]
public class KleinFingerBones
{
    public string[] index;
    public string[] middle;
    public string[] ring;
    public string[] pinky;
    public string[] thumb;
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

/// <summary>
/// Right-arm shaping (shoulder -> upper arm -> elbow -> wrist only; NOT the full body). Keeps the
/// two-bone arm IK reaching from the FRONT with the elbow folded down-and-out, so the hand raises from
/// the front instead of swinging up from the spine and the limb never passes through the torso.
/// </summary>
[Serializable]
public class KleinArmPose
{
    public bool hasData;

    // Elbow pole offset from the shoulder, in metres, in the character's own axes. The forward term is
    // what keeps the elbow in front of the chest so the arm never folds behind the backbone.
    public float poleSide;      // + = character's right (folds the elbow out of the torso)
    public float poleForward;   // + = in front of the chest
    public float poleDown;      // metres below the shoulder

    // Forward reach envelope (metres from the shoulder) for the approach pose.
    public float reachForwardMin;
    public float reachForwardMax;

    // Clavicle/shoulder lift (degrees) as the hand rises toward the contact.
    public float shoulderLiftDeg;

    // Wrist hover above the surface (metres) so the down-pointing fingertip settles on the top.
    public float wristHoverM;

    // Resting elbow bend (degrees) so the held arm isn't ramrod-straight between presses.
    public float restForearmBendDeg;
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
    public KleinVec3Dto scene_contact_point;
    public float force_newtons;
    public int duration_ms;
    public string state_before;
    public string state_after;
    public KleinRigPoseDto rig_pose;
    public KleinHandPoseDto hand_pose;
    public KleinArmPoseDto arm_pose;
}

[Serializable]
class KleinArmPoseDto
{
    public KleinElbowPoleDto elbow_pole;
    public KleinArmReachDto reach;
    public KleinArmRestDto rest;
}

[Serializable]
class KleinElbowPoleDto
{
    public float side;
    public float forward;
    public float down;
}

[Serializable]
class KleinArmReachDto
{
    public float forward_min_m;
    public float forward_max_m;
    public float shoulder_lift_deg;
    public float wrist_hover_m;
}

[Serializable]
class KleinArmRestDto
{
    public float forearm_bend_deg;
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
    public string side;
    public string end_effector;
    public string[] ik_chain;
    public KleinArmChainDto arm_chain;
    public KleinFingerBonesDto finger_bones;
    public KleinVec3Dto ik_target;
    public float approach_angle_deg;
    public float contact_force_n;
}

[Serializable]
class KleinArmChainDto
{
    public string shoulder;
    public string upper_arm;
    public string forearm;
    public string hand;
}

[Serializable]
class KleinFingerBonesDto
{
    public string[] index;
    public string[] middle;
    public string[] ring;
    public string[] pinky;
    public string[] thumb;
}

[Serializable]
class KleinVec3Dto
{
    public float x;
    public float y;
    public float z;
}
