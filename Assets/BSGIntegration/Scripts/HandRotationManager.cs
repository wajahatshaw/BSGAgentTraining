using System.Collections.Generic;
using UnityEngine;

public class HandRotationManager : MonoBehaviour
{
    [System.Serializable]
    public class JointRotationData
    {
        [Header("Joint Transform")]
        public Transform jointTransform;

        [Header("Fixed Rotation")]
        public Vector3 rotation;
    }

    [System.Serializable]
    public class FingerData
    {
        public string fingerName;

        [Header("Finger Joints")]
        public List<JointRotationData> joints = new List<JointRotationData>();
    }

    [Header("All Fingers")]
    [SerializeField] private List<FingerData> fingers = new List<FingerData>();

    public Transform IndexFingerTip { get; private set; }
    public Transform RightArmPivot { get; private set; }
    public Transform RightElbowPivot { get; private set; }

    Quaternion rightArmRestRotation = Quaternion.identity;
    Quaternion rightElbowRestRotation = Quaternion.identity;
    bool rightArmRestCaptured;

    public static HandRotationManager EnsureOnAgent(GameObject agent)
    {
        if (agent == null) return null;

        HandRotationManager manager = agent.GetComponent<HandRotationManager>();
        if (manager == null)
            manager = agent.AddComponent<HandRotationManager>();

        manager.TryAutoWireProceduralHand(agent.transform);
        return manager;
    }

    public void TryAutoWireProceduralHand(Transform root)
    {
        if (root == null) return;

        Transform j1 = FindDeepChild(root, HumanBodyBuilder.RightIndexJoint1);
        Transform j2 = FindDeepChild(root, HumanBodyBuilder.RightIndexJoint2);
        Transform j3 = FindDeepChild(root, HumanBodyBuilder.RightIndexJoint3);
        IndexFingerTip = FindDeepChild(root, HumanBodyBuilder.RightIndexTip) ?? j3;
        RightArmPivot = FindDeepChild(root, HumanBodyBuilder.RightArmPivot);
        RightElbowPivot = FindDeepChild(root, HumanBodyBuilder.RightElbowPivot);

        if (j1 == null || j2 == null || j3 == null)
            TryAutoWirePlayerHands(root);

        if (!rightArmRestCaptured && RightArmPivot != null)
        {
            rightArmRestRotation = RightArmPivot.localRotation;
            rightElbowRestRotation = RightElbowPivot != null ? RightElbowPivot.localRotation : Quaternion.identity;
            rightArmRestCaptured = true;
        }

        if (j1 == null || j2 == null || j3 == null)
            return;

        fingers.Clear();
        fingers.Add(new FingerData { fingerName = "Thumb" });

        FingerData index = new FingerData { fingerName = "Index" };
        index.joints.Add(new JointRotationData { jointTransform = j1, rotation = Vector3.zero });
        index.joints.Add(new JointRotationData { jointTransform = j2, rotation = Vector3.zero });
        index.joints.Add(new JointRotationData { jointTransform = j3, rotation = Vector3.zero });
        fingers.Add(index);

        EnsureFingerTipCollider(IndexFingerTip);
    }

    public void TryAutoWirePlayerHands(Transform root)
    {
        if (root == null) return;

        Transform handGrabRoot = FindDeepChild(root, "PlayerHandGrab");
        Transform hand = handGrabRoot != null && handGrabRoot.childCount > 0
            ? handGrabRoot.GetChild(0)
            : null;

        hand ??= FindDeepChild(root, "RightHand");
        hand ??= FindDeepChild(root, "mixamorig:RightHand");

        IndexFingerTip ??= FindDeepChild(root, "RightHandIndex3")
                           ?? FindDeepChild(root, "mixamorig:RightHandIndex3")
                           ?? FindDeepChild(root, "RightIndexTip")
                           ?? hand;

        RightArmPivot ??= FindDeepChild(root, "RightArm")
                          ?? FindDeepChild(root, "mixamorig:RightArm")
                          ?? hand?.parent;

        RightElbowPivot ??= FindDeepChild(root, "RightForeArm")
                            ?? FindDeepChild(root, "mixamorig:RightForeArm");

        if (!rightArmRestCaptured && RightArmPivot != null)
        {
            rightArmRestRotation = RightArmPivot.localRotation;
            rightElbowRestRotation = RightElbowPivot != null ? RightElbowPivot.localRotation : Quaternion.identity;
            rightArmRestCaptured = true;
        }

        if (fingers.Count > 0 || IndexFingerTip == null)
        {
            EnsureFingerTipCollider(IndexFingerTip);
            return;
        }

        fingers.Clear();
        fingers.Add(new FingerData { fingerName = "Thumb" });

        Transform index1 = FindDeepChild(root, "RightHandIndex1") ?? FindDeepChild(root, "mixamorig:RightHandIndex1");
        Transform index2 = FindDeepChild(root, "RightHandIndex2") ?? FindDeepChild(root, "mixamorig:RightHandIndex2");
        Transform index3 = FindDeepChild(root, "RightHandIndex3") ?? FindDeepChild(root, "mixamorig:RightHandIndex3") ?? IndexFingerTip;

        if (index1 != null && index2 != null && index3 != null)
        {
            FingerData index = new FingerData { fingerName = "Index" };
            index.joints.Add(new JointRotationData { jointTransform = index1, rotation = Vector3.zero });
            index.joints.Add(new JointRotationData { jointTransform = index2, rotation = Vector3.zero });
            index.joints.Add(new JointRotationData { jointTransform = index3, rotation = Vector3.zero });
            fingers.Add(index);
            IndexFingerTip = index3;
        }

        EnsureFingerTipCollider(IndexFingerTip);
    }

    public void ApplyFingerRotation(int fingerIndex, List<Vector3> rotations)
    {
        if (fingerIndex < 0 || fingerIndex >= fingers.Count || rotations == null)
            return;

        FingerData finger = fingers[fingerIndex];
        int count = Mathf.Min(finger.joints.Count, rotations.Count);

        for (int i = 0; i < count; i++)
        {
            finger.joints[i].rotation = rotations[i];

            if (finger.joints[i].jointTransform != null)
                finger.joints[i].jointTransform.localRotation = Quaternion.Euler(rotations[i]);
        }
    }

    public void ApplyHandRotation(List<List<Vector3>> allRotations)
    {
        if (allRotations == null) return;

        int fingerCount = Mathf.Min(fingers.Count, allRotations.Count);
        for (int i = 0; i < fingerCount; i++)
            ApplyFingerRotation(i, allRotations[i]);
    }

    public void ApplyStoredRotations()
    {
        foreach (FingerData finger in fingers)
        {
            foreach (JointRotationData joint in finger.joints)
            {
                if (joint.jointTransform == null) continue;
                joint.jointTransform.localRotation = Quaternion.Euler(joint.rotation);
            }
        }
    }

    public void ApplyRightArmReachPose(Vector3 worldTarget, bool press)
    {
        if (RightArmPivot == null)
            TryAutoWireProceduralHand(transform);
        if (RightArmPivot == null) return;

        Vector3 toTarget = worldTarget - RightArmPivot.position;
        if (toTarget.sqrMagnitude < 0.0001f) return;

        Vector3 localDirection = RightArmPivot.parent != null
            ? RightArmPivot.parent.InverseTransformDirection(toTarget.normalized)
            : toTarget.normalized;

        // The generated upper arm hangs along local -Y, so rotate -Y toward the selected UI target.
        Quaternion reach = Quaternion.FromToRotation(Vector3.down, localDirection);
        float reachBlend = press ? 0.96f : 0.72f;
        RightArmPivot.localRotation = Quaternion.Slerp(RightArmPivot.localRotation, reach, Time.deltaTime * 16f * reachBlend);

        if (RightElbowPivot != null)
        {
            float bend = press ? -54f : -34f;
            Quaternion elbow = rightElbowRestRotation * Quaternion.Euler(bend, 0f, 0f);
            RightElbowPivot.localRotation = Quaternion.Slerp(RightElbowPivot.localRotation, elbow, Time.deltaTime * 14f);
        }
    }

    public void ResetRightArmReachPose()
    {
        if (!rightArmRestCaptured) return;
        if (RightArmPivot != null)
            RightArmPivot.localRotation = Quaternion.Slerp(RightArmPivot.localRotation, rightArmRestRotation, Time.deltaTime * 10f);
        if (RightElbowPivot != null)
            RightElbowPivot.localRotation = Quaternion.Slerp(RightElbowPivot.localRotation, rightElbowRestRotation, Time.deltaTime * 10f);
    }

    static Transform FindDeepChild(Transform root, string childName)
    {
        if (root == null || string.IsNullOrWhiteSpace(childName)) return null;
        if (root.name == childName) return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeepChild(root.GetChild(i), childName);
            if (found != null) return found;
        }

        return null;
    }

    static void EnsureFingerTipCollider(Transform tip)
    {
        if (tip == null) return;

        SphereCollider collider = tip.GetComponent<SphereCollider>();
        if (collider == null)
            collider = tip.gameObject.AddComponent<SphereCollider>();

        collider.isTrigger = true;
        collider.radius = 0.035f;
    }
}
