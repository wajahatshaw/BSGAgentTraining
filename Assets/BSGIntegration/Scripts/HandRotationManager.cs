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
    public Transform RightHandRoot { get; private set; }
    public Transform RightArmPivot { get; private set; }
    public Transform RightElbowPivot { get; private set; }
    public Transform RightShoulderPivot { get; private set; }
    public bool UsesMixamoRig { get; private set; }

    readonly List<Transform> _rightIndexChain = new List<Transform>(8);
    readonly Dictionary<Transform, Quaternion> _fingerRestRotations = new Dictionary<Transform, Quaternion>();

    Quaternion rightArmRestRotation = Quaternion.identity;
    Quaternion rightElbowRestRotation = Quaternion.identity;
    Quaternion rightShoulderRestRotation = Quaternion.identity;
    Quaternion rightHandRestRotation = Quaternion.identity;
    Quaternion index1RestRotation = Quaternion.identity;
    Quaternion index2RestRotation = Quaternion.identity;
    Quaternion index3RestRotation = Quaternion.identity;
    bool rightArmRestCaptured;
    bool shoulderRestCaptured;
    bool handRestCaptured;

    Animator[] _rigAnimators;
    bool _rigAnimatorsSuppressed;
    bool _manualPoseActive;
    bool _loggedWireStatus;

    KleinFrame _manualFrame;
    Vector3 _manualTarget;
    float _manualPressPhase;
    float _manualForce;

    Vector3 _lockedReachTarget;
    bool _hasLockedReachTarget;
    float _smoothedReachWeight;

    public bool ManualPoseActive => _manualPoseActive;

    public static HandRotationManager EnsureOnAgent(GameObject agent)
    {
        if (agent == null) return null;

        HandRotationManager manager = agent.GetComponent<HandRotationManager>();
        if (manager == null)
            manager = agent.AddComponent<HandRotationManager>();

        manager.TryAutoWirePlayerHands(agent.transform);
        if (!manager.UsesMixamoRig)
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

        Transform yBot = FindDeepChild(root, "Y Bot");
        bool useYBot = yBot != null && yBot.gameObject.activeInHierarchy;
        Transform searchRoot = useYBot ? yBot : root;

        Transform hand = FindDeepChild(searchRoot, "mixamorig:RightHand")
                         ?? FindDeepChild(searchRoot, "RightHand");

        if (!useYBot)
        {
            Transform handGrabRoot = FindDeepChild(root, "PlayerHandGrab");
            if (handGrabRoot != null && handGrabRoot.childCount > 0)
                hand = handGrabRoot.GetChild(0);
        }

        IndexFingerTip = FindDeepChild(searchRoot, "mixamorig:RightHandIndex4_end")
                         ?? FindDeepChild(searchRoot, "RightHandIndex4_end")
                         ?? FindDeepChild(searchRoot, "mixamorig:RightHandIndex3")
                         ?? FindDeepChild(searchRoot, "RightHandIndex3")
                         ?? FindDeepChild(searchRoot, "RightIndexTip")
                         ?? hand;

        RightHandRoot = hand;
        RightArmPivot = FindDeepChild(searchRoot, "mixamorig:RightArm")
                        ?? FindDeepChild(searchRoot, "RightArm")
                        ?? hand?.parent;
        RightShoulderPivot = FindDeepChild(searchRoot, "mixamorig:RightShoulder")
                             ?? FindDeepChild(searchRoot, "RightShoulder");
        RightElbowPivot = FindDeepChild(searchRoot, "mixamorig:RightForeArm")
                          ?? FindDeepChild(searchRoot, "RightForeArm");

        UsesMixamoRig = useYBot
                        || RightShoulderPivot != null
                        || (RightArmPivot != null && RightArmPivot.name.IndexOf("mixamorig", System.StringComparison.OrdinalIgnoreCase) >= 0);

        if (!rightArmRestCaptured && RightArmPivot != null)
        {
            rightArmRestRotation = RightArmPivot.localRotation;
            rightElbowRestRotation = RightElbowPivot != null ? RightElbowPivot.localRotation : Quaternion.identity;
            rightArmRestCaptured = true;
        }

        if (!shoulderRestCaptured && RightShoulderPivot != null)
        {
            rightShoulderRestRotation = RightShoulderPivot.localRotation;
            shoulderRestCaptured = true;
        }

        if (!handRestCaptured && RightHandRoot != null)
        {
            rightHandRestRotation = RightHandRoot.localRotation;
            handRestCaptured = true;
        }

        Transform index1 = FindDeepChild(searchRoot, "mixamorig:RightHandIndex1") ?? FindDeepChild(searchRoot, "RightHandIndex1");
        Transform index2 = FindDeepChild(searchRoot, "mixamorig:RightHandIndex2") ?? FindDeepChild(searchRoot, "RightHandIndex2");
        Transform index3 = FindDeepChild(searchRoot, "mixamorig:RightHandIndex3") ?? FindDeepChild(searchRoot, "RightHandIndex3") ?? IndexFingerTip;
        Transform index4 = FindDeepChild(searchRoot, "mixamorig:RightHandIndex4_end") ?? FindDeepChild(searchRoot, "RightHandIndex4_end");

        fingers.Clear();
        fingers.Add(new FingerData { fingerName = "Thumb" });

        if (index1 != null && index2 != null && index3 != null)
        {
            FingerData index = new FingerData { fingerName = "Index" };
            index.joints.Add(new JointRotationData { jointTransform = index1, rotation = Vector3.zero });
            index.joints.Add(new JointRotationData { jointTransform = index2, rotation = Vector3.zero });
            index.joints.Add(new JointRotationData { jointTransform = index3, rotation = Vector3.zero });
            fingers.Add(index);
            IndexFingerTip = index4 ?? index3;
            if (index1 != null) index1RestRotation = index1.localRotation;
            if (index2 != null) index2RestRotation = index2.localRotation;
            if (index3 != null) index3RestRotation = index3.localRotation;
        }

        BuildRightIndexChain(useYBot, RightHandRoot ?? hand, index1, index2, index3, index4);
        EnsureFingerTipCollider(IndexFingerTip);
    }

    /// <summary>Re-resolve finger/arm bones after Y Bot visual is enabled on the Photon player.</summary>
    public void RefreshRigWire()
    {
        if (_manualPoseActive)
            return;

        fingers.Clear();
        _rightIndexChain.Clear();
        _fingerRestRotations.Clear();
        rightArmRestCaptured = false;
        shoulderRestCaptured = false;
        handRestCaptured = false;
        IndexFingerTip = null;
        RightHandRoot = null;
        RightArmPivot = null;
        RightElbowPivot = null;
        RightShoulderPivot = null;
        UsesMixamoRig = false;
        _rigAnimators = null;
        _loggedWireStatus = false;
        _hasLockedReachTarget = false;
        _smoothedReachWeight = 0f;
        TryAutoWirePlayerHands(transform);
        CacheRigAnimators();
    }

    void CacheRigAnimators()
    {
        Transform yBot = FindDeepChild(transform, "Y Bot");
        if (yBot != null)
            _rigAnimators = yBot.GetComponentsInChildren<Animator>(true);
        else
            _rigAnimators = GetComponentsInChildren<Animator>(true);
    }

    void SuppressRigAnimators()
    {
        if (_rigAnimators == null || _rigAnimators.Length == 0)
            CacheRigAnimators();

        foreach (Animator animator in _rigAnimators)
        {
            if (animator == null || !animator.enabled)
                continue;
            animator.enabled = false;
            _rigAnimatorsSuppressed = true;
        }
    }

    void RestoreRigAnimators()
    {
        if (!_rigAnimatorsSuppressed || _rigAnimators == null)
            return;

        foreach (Animator animator in _rigAnimators)
        {
            if (animator != null)
                animator.enabled = true;
        }

        _rigAnimatorsSuppressed = false;
    }

    public void ActivateManualPose()
    {
        _manualPoseActive = true;
        if (_rigAnimators == null || _rigAnimators.Length == 0)
            CacheRigAnimators();
    }

    public void DeactivateManualPose()
    {
        _manualPoseActive = false;
        _manualFrame = null;
        _manualTarget = Vector3.zero;
        _manualPressPhase = 0f;
        _manualForce = 0f;
        _hasLockedReachTarget = false;
        _smoothedReachWeight = 0f;
        RestoreRigAnimators();
    }

    public void SetManualMotorPose(KleinFrame frame, Vector3 worldTarget, float pressPhase, float forceNewtons)
    {
        ActivateManualPose();
        _manualFrame = frame;
        _manualTarget = worldTarget;
        _manualPressPhase = pressPhase;
        _manualForce = forceNewtons;
    }

    public void SetManualReachPose(Vector3 worldTarget, float pressPhase)
    {
        ActivateManualPose();
        _manualFrame = null;
        _manualTarget = worldTarget;
        _manualPressPhase = pressPhase;
        _manualForce = 0f;
    }

    void LateUpdate()
    {
        if (!_manualPoseActive)
            return;

        if (!_loggedWireStatus)
        {
            _loggedWireStatus = true;
            Debug.Log($"[HandRotationManager] Manual pose active — mixamo={UsesMixamoRig}, shoulder={RightShoulderPivot?.name}, arm={RightArmPivot?.name}, tip={IndexFingerTip?.name}, animators={_rigAnimators?.Length ?? 0}");
        }

        SuppressRigAnimators();
        ApplyPendingManualPose();
    }

    void ApplyPendingManualPose()
    {
        if (RightArmPivot == null && RightShoulderPivot == null)
        {
            TryAutoWirePlayerHands(transform);
            if (RightArmPivot == null && RightShoulderPivot == null)
                return;
        }

        if (UsesMixamoRig)
        {
            ApplyMixamoManualPose();
            return;
        }

        bool press = _manualPressPhase > 0.25f;
        if (_manualFrame != null)
        {
            ApplyRightArmReachPoseImmediate(_manualTarget, press, _manualPressPhase);
            ApplyKleinFrameFingerIk(_manualFrame, _manualTarget, _manualPressPhase, _manualForce);
        }
        else if (_manualTarget.sqrMagnitude > 0.0001f)
        {
            ApplyRightArmReachPoseImmediate(_manualTarget, _manualPressPhase > 0.35f, _manualPressPhase);
        }
    }

    void LockReachTarget(Vector3 rawWorldTarget)
    {
        if (!_hasLockedReachTarget || (rawWorldTarget - _lockedReachTarget).sqrMagnitude > 0.08f)
        {
            _lockedReachTarget = rawWorldTarget;
            _hasLockedReachTarget = true;
        }
    }

    void ApplyMixamoManualPose()
    {
        float goalWeight = Mathf.Clamp01(_manualPressPhase);
        _smoothedReachWeight = Mathf.MoveTowards(_smoothedReachWeight, goalWeight, Time.deltaTime * 12f);
        float weight = _smoothedReachWeight;

        if (weight < 0.001f && goalWeight < 0.001f)
        {
            RestoreMixamoArmRest();
            return;
        }

        if (_manualTarget.sqrMagnitude > 0.0001f)
            LockReachTarget(_manualTarget);

        Transform shoulder = RightShoulderPivot;
        Transform upper = RightArmPivot;
        Transform forearm = RightElbowPivot;
        Transform hand = RightHandRoot;
        if (upper == null || forearm == null || hand == null)
            return;

        float w = Mathf.Max(weight, goalWeight > 0.02f ? Mathf.Min(goalWeight, 0.3f) : 0f);
        float pressW = Mathf.Clamp01(_manualPressPhase);

        Transform shoulderRef = shoulder ?? upper;
        Vector3 surfacePoint = _hasLockedReachTarget
            ? MixamoRightArmIKSolver.BuildDeskReachPoint(transform, shoulderRef, _lockedReachTarget, w)
            : MixamoRightArmIKSolver.BuildDeskReachPoint(transform, shoulderRef, upper.position + transform.forward * 0.42f, w);

        surfacePoint += Vector3.down * (pressW * 0.018f);

        Vector3 pole = MixamoRightArmIKSolver.BuildElbowPole(transform, shoulderRef);
        MixamoRightArmIKSolver.Apply(
            shoulder,
            upper,
            forearm,
            hand,
            rightShoulderRestRotation,
            rightArmRestRotation,
            rightElbowRestRotation,
            surfacePoint,
            pole,
            w);

        ApplyMixamoHandAim(surfacePoint, w);
        ApplyMixamoIndexFingerContact(pressW, w);
    }

    void ApplyMixamoHandAim(Vector3 surfaceTarget, float weight)
    {
        if (RightHandRoot == null || !handRestCaptured)
            return;

        Vector3 aimOrigin = IndexFingerTip != null ? IndexFingerTip.position : RightHandRoot.position;
        Vector3 toSurface = surfaceTarget - aimOrigin;
        if (toSurface.sqrMagnitude < 0.0001f)
            return;

        Transform parent = RightHandRoot.parent;
        Vector3 localDir = parent != null
            ? parent.InverseTransformDirection(toSurface.normalized)
            : transform.InverseTransformDirection(toSurface.normalized);

        float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(localDir.y, -1f, 1f)) * Mathf.Rad2Deg, -32f, 32f);
        float yaw = Mathf.Clamp(Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg, -22f, 22f);
        Quaternion aimed = rightHandRestRotation * Quaternion.Euler(pitch * 0.55f, yaw * 0.4f, 0f);
        RightHandRoot.localRotation = Quaternion.Slerp(rightHandRestRotation, aimed, weight * 0.9f);
    }

    void ApplyMixamoIndexFingerContact(float pressWeight, float reachWeight)
    {
        Transform j1 = null;
        Transform j2 = null;
        Transform j3 = null;

        if (fingers.Count > HandActionLibrary.IndexFinger)
        {
            FingerData index = fingers[HandActionLibrary.IndexFinger];
            if (index.joints.Count > 0) j1 = index.joints[0].jointTransform;
            if (index.joints.Count > 1) j2 = index.joints[1].jointTransform;
            if (index.joints.Count > 2) j3 = index.joints[2].jointTransform;
        }

        MixamoIndexFingerFlex.Apply(
            j1,
            j2,
            j3,
            index1RestRotation,
            index2RestRotation,
            index3RestRotation,
            reachWeight,
            pressWeight);
    }

    void RestoreMixamoArmRest()
    {
        if (shoulderRestCaptured && RightShoulderPivot != null)
            RightShoulderPivot.localRotation = rightShoulderRestRotation;
        if (rightArmRestCaptured && RightArmPivot != null)
            RightArmPivot.localRotation = rightArmRestRotation;
        if (RightElbowPivot != null)
            RightElbowPivot.localRotation = rightElbowRestRotation;
        if (handRestCaptured && RightHandRoot != null)
            RightHandRoot.localRotation = rightHandRestRotation;
        ResetFingerCurl();
    }

    void BuildRightIndexChain(bool mixamoRig, Transform hand, Transform index1, Transform index2, Transform index3, Transform index4)
    {
        _rightIndexChain.Clear();
        _fingerRestRotations.Clear();

        if (!mixamoRig && hand != null)
            AddChainBone(hand);
        if (index1 != null) AddChainBone(index1);
        if (index2 != null) AddChainBone(index2);
        if (index3 != null) AddChainBone(index3);
        if (index4 != null) AddChainBone(index4);
        else if (IndexFingerTip != null) AddChainBone(IndexFingerTip);
    }

    void AddChainBone(Transform bone)
    {
        if (bone == null) return;
        if (_rightIndexChain.Contains(bone)) return;
        _rightIndexChain.Add(bone);
        _fingerRestRotations[bone] = bone.localRotation;
    }

    public void ApplyKleinFrameFingerIk(KleinFrame frame, Vector3 worldTarget, float pressPhase, float forceNewtons)
    {
        if (UsesMixamoRig)
            return;

        if (_rightIndexChain.Count < 2)
            TryAutoWirePlayerHands(transform);

        if (_rightIndexChain.Count >= 2)
        {
            float approachDeg = frame?.rigPose != null && frame.rigPose.approachAngleDeg > 0.01f
                ? frame.rigPose.approachAngleDeg
                : 90f;
            Vector3 approachNormal = Quaternion.AngleAxis(approachDeg, Vector3.right) * Vector3.down;
            Vector3 ikTarget = SanitizeReachTarget(worldTarget) + Vector3.up * (0.02f * (1f - pressPhase));
            FingerChainIKSolver.Solve(_rightIndexChain, ikTarget, approachNormal);
        }

        float proceduralCurl = Mathf.Clamp01(pressPhase) * Mathf.Clamp(forceNewtons * 120f, 8f, 55f);
        ApplyFingerCurl(proceduralCurl);
    }

    /// <summary>Clamp interaction point to a natural desk-reach zone in front of the character.</summary>
    Vector3 SanitizeReachTarget(Vector3 rawWorldTarget)
    {
        Transform shoulder = RightShoulderPivot ?? RightArmPivot;
        if (shoulder == null)
            return rawWorldTarget;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        Vector3 toRaw = rawWorldTarget - shoulder.position;
        Vector3 flat = new Vector3(toRaw.x, 0f, toRaw.z);
        Vector3 aim = flat.sqrMagnitude > 0.04f ? flat.normalized : forward;
        if (Vector3.Dot(aim, forward) < 0.2f)
            aim = Vector3.Slerp(forward, aim, 0.25f).normalized;

        float reachDist = Mathf.Clamp(flat.magnitude, 0.28f, 0.62f);
        float deskY = Mathf.Clamp(rawWorldTarget.y, shoulder.position.y - 0.72f, shoulder.position.y - 0.08f);
        if (rawWorldTarget.y > shoulder.position.y + 0.05f)
            deskY = shoulder.position.y - 0.38f;

        return new Vector3(shoulder.position.x, deskY, shoulder.position.z) + aim * reachDist;
    }

    public void ApplyFingerCurl(float curlDegrees)
    {
        if (fingers.Count <= HandActionLibrary.IndexFinger)
            return;

        FingerData index = fingers[HandActionLibrary.IndexFinger];
        if (index.joints.Count == 0)
            return;

        float d0 = curlDegrees * 0.35f;
        float d1 = curlDegrees * 0.55f;
        float d2 = curlDegrees * 0.25f;

        if (index.joints.Count > 0 && index.joints[0].jointTransform != null)
            index.joints[0].jointTransform.localRotation = _fingerRestRotations.TryGetValue(index.joints[0].jointTransform, out Quaternion r0)
                ? r0 * Quaternion.Euler(0f, 0f, d0) : Quaternion.Euler(0f, 0f, d0);
        if (index.joints.Count > 1 && index.joints[1].jointTransform != null)
            index.joints[1].jointTransform.localRotation = _fingerRestRotations.TryGetValue(index.joints[1].jointTransform, out Quaternion r1)
                ? r1 * Quaternion.Euler(0f, 0f, d1) : Quaternion.Euler(0f, 0f, d1);
        if (index.joints.Count > 2 && index.joints[2].jointTransform != null)
            index.joints[2].jointTransform.localRotation = _fingerRestRotations.TryGetValue(index.joints[2].jointTransform, out Quaternion r2)
                ? r2 * Quaternion.Euler(0f, 0f, d2) : Quaternion.Euler(0f, 0f, d2);
    }

    public void ResetFingerCurl()
    {
        if (UsesMixamoRig)
        {
            if (fingers.Count > HandActionLibrary.IndexFinger)
            {
                FingerData index = fingers[HandActionLibrary.IndexFinger];
                if (index.joints.Count > 0 && index.joints[0].jointTransform != null)
                    index.joints[0].jointTransform.localRotation = index1RestRotation;
                if (index.joints.Count > 1 && index.joints[1].jointTransform != null)
                    index.joints[1].jointTransform.localRotation = index2RestRotation;
                if (index.joints.Count > 2 && index.joints[2].jointTransform != null)
                    index.joints[2].jointTransform.localRotation = index3RestRotation;
            }
            return;
        }

        foreach (KeyValuePair<Transform, Quaternion> kvp in _fingerRestRotations)
        {
            if (kvp.Key != null)
                kvp.Key.localRotation = kvp.Value;
        }

        foreach (FingerData finger in fingers)
        {
            foreach (JointRotationData joint in finger.joints)
                joint.rotation = Vector3.zero;
        }
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
        if (RightArmPivot == null && RightShoulderPivot == null)
        {
            TryAutoWirePlayerHands(transform);
            if (RightArmPivot == null && RightShoulderPivot == null)
                TryAutoWireProceduralHand(transform);
        }
        if (RightArmPivot == null && RightShoulderPivot == null) return;

        if (UsesMixamoRig)
        {
            SetManualReachPose(worldTarget, press ? 1f : 0.45f);
            return;
        }

        ApplyRightArmReachPoseImmediate(worldTarget, press, press ? 1f : 0.45f);
    }

    void ApplyRightArmReachPoseImmediate(Vector3 worldTarget, bool press, float reachPhase)
    {
        if (UsesMixamoRig)
            return;

        Vector3 sanitized = SanitizeReachTarget(worldTarget);
        ApplyProceduralArmReachPose(sanitized, press, reachPhase);
    }

    void ApplyProceduralArmReachPose(Vector3 worldTarget, bool press, float reachPhase)
    {
        if (RightArmPivot == null) return;

        Vector3 toTarget = worldTarget - RightArmPivot.position;
        if (toTarget.sqrMagnitude < 0.0001f) return;

        Vector3 localDirection = RightArmPivot.parent != null
            ? RightArmPivot.parent.InverseTransformDirection(toTarget.normalized)
            : toTarget.normalized;

        Quaternion reach = Quaternion.FromToRotation(Vector3.down, localDirection);
        float reachBlend = press ? 0.96f : 0.72f;
        float phase = Mathf.Clamp01(reachPhase > 0f ? reachPhase : (press ? 1f : 0.65f));
        float step = Time.deltaTime * 10f * reachBlend * Mathf.Max(phase, 0.35f);
        RightArmPivot.localRotation = Quaternion.Slerp(RightArmPivot.localRotation, reach, step);

        if (RightElbowPivot != null)
        {
            float bend = Mathf.Lerp(-34f, -54f, phase);
            Quaternion elbow = rightElbowRestRotation * Quaternion.Euler(bend, 0f, 0f);
            RightElbowPivot.localRotation = Quaternion.Slerp(RightElbowPivot.localRotation, elbow, Time.deltaTime * 12f);
        }
    }

    public void ResetRightArmReachPose()
    {
        if (UsesMixamoRig)
        {
            DeactivateManualPose();
            RestoreMixamoArmRest();
            return;
        }

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
