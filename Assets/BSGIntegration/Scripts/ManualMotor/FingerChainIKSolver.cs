using System.Collections.Generic;
using UnityEngine;

/// <summary>Lightweight CCD IK for a finger bone chain (no Animation Rigging package).</summary>
public static class FingerChainIKSolver
{
    const int DefaultIterations = 4;

    public static void Solve(IReadOnlyList<Transform> chain, Vector3 worldTarget, Vector3 approachNormal, int iterations = DefaultIterations)
    {
        if (chain == null || chain.Count < 2)
            return;

        approachNormal = approachNormal.sqrMagnitude > 0.0001f ? approachNormal.normalized : Vector3.down;
        Vector3 biasedTarget = worldTarget - approachNormal * 0.01f;

        for (int iter = 0; iter < iterations; iter++)
        {
            for (int i = chain.Count - 2; i >= 0; i--)
            {
                Transform bone = chain[i];
                Transform tip = chain[chain.Count - 1];
                if (bone == null || tip == null) continue;

                Vector3 toTip = tip.position - bone.position;
                Vector3 toTarget = biasedTarget - bone.position;
                if (toTip.sqrMagnitude < 1e-8f || toTarget.sqrMagnitude < 1e-8f)
                    continue;

                Quaternion delta = Quaternion.FromToRotation(toTip.normalized, toTarget.normalized);
                float angle = Quaternion.Angle(Quaternion.identity, delta);
                if (angle > 55f)
                    delta = Quaternion.Slerp(Quaternion.identity, delta, 55f / angle);

                bone.rotation = delta * bone.rotation;
            }
        }
    }
}
