using UnityEngine;

/// <summary>
/// Two-bone IK for Mixamo right arm reaching forward to a desk target.
/// Elbow pole is placed on the character's right side so the arm folds outward, not behind the back.
/// </summary>
public static class MixamoRightArmIKSolver
{
    const float MaxUpperSolveDeg = 88f;
    const float MaxForearmSolveDeg = 95f;

    public static void Apply(
        Transform clavicle,
        Transform upperArm,
        Transform forearm,
        Transform hand,
        Quaternion clavicleRest,
        Quaternion upperRest,
        Quaternion forearmRest,
        Vector3 targetWorld,
        Vector3 poleWorld,
        float weight)
    {
        weight = Mathf.Clamp01(weight);
        if (upperArm == null || forearm == null || hand == null || weight < 0.001f)
            return;

        upperArm.localRotation = upperRest;
        forearm.localRotation = forearmRest;

        Vector3 root = upperArm.position;
        Vector3 mid = forearm.position;
        Vector3 end = hand.position;

        float upperLen = Vector3.Distance(root, mid);
        float foreLen = Vector3.Distance(mid, end);
        if (upperLen < 0.05f || foreLen < 0.05f)
            return;

        Vector3 toTarget = targetWorld - root;
        float dist = toTarget.magnitude;
        if (dist < 0.05f)
            return;

        dist = Mathf.Clamp(dist, upperLen * 0.4f, upperLen + foreLen - 0.04f);
        Vector3 dir = toTarget / dist;

        Vector3 toPole = poleWorld - root;
        Vector3 bendNormal = Vector3.Cross(dir, toPole);
        if (bendNormal.sqrMagnitude < 0.01f)
            bendNormal = Vector3.Cross(dir, Vector3.right);
        bendNormal.Normalize();

        float cosShoulder = (upperLen * upperLen + dist * dist - foreLen * foreLen) / (2f * upperLen * dist);
        cosShoulder = Mathf.Clamp(cosShoulder, -1f, 1f);
        float shoulderAngle = Mathf.Acos(cosShoulder) * Mathf.Rad2Deg;

        Vector3 desiredUpperDir = Quaternion.AngleAxis(-shoulderAngle, bendNormal) * dir;
        Vector3 currentUpperDir = (mid - root).normalized;
        if (currentUpperDir.sqrMagnitude < 1e-6f)
            return;

        Quaternion upperDelta = Quaternion.FromToRotation(currentUpperDir, desiredUpperDir);
        float upperAngle = Quaternion.Angle(Quaternion.identity, upperDelta);
        if (upperAngle > MaxUpperSolveDeg)
            upperDelta = Quaternion.Slerp(Quaternion.identity, upperDelta, MaxUpperSolveDeg / upperAngle);

        upperArm.rotation = upperDelta * upperArm.rotation;

        mid = forearm.position;
        end = hand.position;
        Vector3 foreDir = (end - mid).normalized;
        Vector3 toTargetFore = (targetWorld - mid).normalized;
        if (foreDir.sqrMagnitude < 1e-6f || toTargetFore.sqrMagnitude < 1e-6f)
            return;

        Quaternion foreDelta = Quaternion.FromToRotation(foreDir, toTargetFore);
        float foreAngle = Quaternion.Angle(Quaternion.identity, foreDelta);
        if (foreAngle > MaxForearmSolveDeg)
            foreDelta = Quaternion.Slerp(Quaternion.identity, foreDelta, MaxForearmSolveDeg / foreAngle);

        forearm.rotation = foreDelta * forearm.rotation;

        upperArm.localRotation = Quaternion.Slerp(upperRest, upperArm.localRotation, weight);
        forearm.localRotation = Quaternion.Slerp(forearmRest, forearm.localRotation, weight);

        if (clavicle != null)
        {
            // Slight forward lift of clavicle — positive X raises right arm forward on Mixamo.
            Quaternion clavGoal = clavicleRest * Quaternion.Euler(22f * weight, 2f * weight, -4f * weight);
            clavicle.localRotation = Quaternion.Slerp(clavicleRest, clavGoal, weight * 0.85f);
        }
    }

    /// <summary>Interaction point in front of the body at desk height.</summary>
    public static Vector3 BuildDeskReachPoint(Transform body, Transform shoulder, Vector3 rawTarget, float reachPhase)
    {
        if (body == null || shoulder == null)
            return rawTarget;

        Vector3 forward = body.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        Vector3 right = body.right;
        float side = Mathf.Clamp(Vector3.Dot(rawTarget - shoulder.position, right), -0.28f, 0.28f);
        float forwardDist = Mathf.Lerp(0.36f, 0.5f, reachPhase);

        float deskY = rawTarget.y;
        if (deskY > shoulder.position.y - 0.08f || deskY < 0.01f)
            deskY = Mathf.Max(0.45f, shoulder.position.y - 0.48f);
        else
            deskY += 0.04f;

        Vector3 point = shoulder.position + forward * forwardDist + right * side;
        point.y = Mathf.Lerp(shoulder.position.y - 0.18f, deskY, reachPhase);
        return point;
    }

    /// <summary>Right-arm elbow pole — outside the body on the character's right.</summary>
    public static Vector3 BuildElbowPole(Transform body, Transform shoulder)
    {
        if (body == null || shoulder == null)
            return shoulder != null ? shoulder.position + Vector3.right * 0.4f : Vector3.right;

        return shoulder.position + body.right * 0.48f + Vector3.down * 0.22f;
    }
}
