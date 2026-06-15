using UnityEngine;

/// <summary>
/// Index-finger open/close on Mixamo rigs — local Z flex only (no hand-bone twist).
/// </summary>
public static class MixamoIndexFingerFlex
{
    public static void Apply(
        Transform joint1,
        Transform joint2,
        Transform joint3,
        Quaternion rest1,
        Quaternion rest2,
        Quaternion rest3,
        float reachWeight,
        float pressWeight)
    {
        reachWeight = Mathf.Clamp01(reachWeight);
        pressWeight = Mathf.Clamp01(pressWeight);

        // Reach: finger extended toward surface. Press: curl into pad contact.
        float contact = Mathf.Max(reachWeight * 0.35f, pressWeight);

        float mcp = Mathf.Lerp(4f, 38f, contact);
        float pip = Mathf.Lerp(2f, 52f, contact);
        float dip = Mathf.Lerp(1f, 30f, contact);

        SetJointFlex(joint1, rest1, mcp);
        SetJointFlex(joint2, rest2, pip);
        SetJointFlex(joint3, rest3, dip);
    }

    static void SetJointFlex(Transform bone, Quaternion rest, float curlZDegrees)
    {
        if (bone == null)
            return;

        Quaternion target = rest * Quaternion.Euler(0f, 0f, curlZDegrees);
        bone.localRotation = Quaternion.RotateTowards(rest, target, Mathf.Abs(curlZDegrees) + 0.01f);
    }
}
