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

    // CLEAN-ROOM RAG ik_chain driver — the bones named in the active frame's rig_pose.ik_chain, resolved
    // on the rig and CCD-solved to the RAG ik_target. This is the ONLY thing that moves the body in RAG
    // mode (no default arm/finger pose).
    readonly List<Transform> _ragChain = new List<Transform>(8);
    readonly Dictionary<Transform, Quaternion> _ragChainRest = new Dictionary<Transform, Quaternion>();
    string _ragChainSig;

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
    bool fingerRestCaptured;

    Animator[] _rigAnimators;
    bool _rigAnimatorsSuppressed;
    bool _manualPoseActive;
    bool _loggedWireStatus;
    bool _loggedRegistrySummary;

    KleinFrame _manualFrame;
    Vector3 _manualTarget;
    float _manualPressPhase;
    float _manualForce;

    Vector3 _lockedReachTarget;
    bool _hasLockedReachTarget;
    float _smoothedReachWeight;
    float _measuredTipDrop = 0.08f;   // live wrist→fingertip vertical gap, so the wrist is placed to land the tip exactly on the surface

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

        if (IndexFingerTip == null)
            IndexFingerTip = TryResolveBoneFromRegistry(root, "right_index_fingertip_pad", preferMixamo: false);

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
        LogRegistrySummaryOnce();
    }

    void LogRegistrySummaryOnce()
    {
        if (_loggedRegistrySummary)
            return;

        if (!BodyPartRegistry.IsLoaded && !BodyPartRegistry.TryLoad())
            return;

        _loggedRegistrySummary = true;
        if (BodyPartRegistry.TryGetByRagName("torso", out BodyPartEntry torso)
            && BodyPartRegistry.TryGetByRagName("fovea", out BodyPartEntry fovea)
            && BodyPartRegistry.TryGetByRagName("right_index_fingertip_pad", out BodyPartEntry fingertip))
        {
            Debug.Log($"[HandRotationManager] Body part registry: torso={torso.numericId}, fovea={fovea.numericId}, fingertip={fingertip.numericId}");
        }
    }

    Transform TryResolveBoneFromRegistry(Transform root, string ragName, bool preferMixamo)
    {
        if (root == null || string.IsNullOrWhiteSpace(ragName))
            return null;

        if (!BodyPartRegistry.IsLoaded && !BodyPartRegistry.TryLoad())
            return null;

        if (!BodyPartRegistry.TryResolve(ragName, out BodyPartEntry entry))
            return null;

        if (preferMixamo && entry.HasMixamoBone)
        {
            Transform mixamo = FindDeepChild(root, entry.mixamoBone);
            if (mixamo != null)
                return mixamo;
        }

        if (entry.HasProceduralBone)
        {
            Transform procedural = FindDeepChild(root, entry.proceduralBone);
            if (procedural != null)
                return procedural;
        }

        if (!preferMixamo && entry.HasMixamoBone)
            return FindDeepChild(root, entry.mixamoBone);

        return null;
    }

    /// <summary>Resolve a bone from its name in the rig binding (standard right-hand mixamo names supplied
    /// by <see cref="RagKleinFrameSource.BuildDefaultRightHandBinding"/>). Tolerates the "mixamorig:" prefix
    /// and "_end" tip suffix. A miss logs a warning (the rig simply lacks that bone — e.g. a non-hand rig).</summary>
    static Transform ResolveBoneName(Transform root, string jsonName, string label)
    {
        if (string.IsNullOrWhiteSpace(jsonName))
            return null;

        Transform t = FindBoneFlexible(root, jsonName);
        if (t == null)
            Debug.LogWarning($"[HandRotationManager] rig bone '{jsonName}' ({label}) not found under '{root?.name}' — " +
                             $"this rig doesn't expose that bone (fine for non-hand rigs; the hand IK just skips it).");
        return t;
    }

    /// <summary>Find a bone by its exact name, tolerating only the "mixamorig:" prefix and "_end" tip
    /// suffix (the SAME bone under two common rig conventions) — never a different bone.</summary>
    static Transform FindBoneFlexible(Transform root, string name)
    {
        if (root == null || string.IsNullOrWhiteSpace(name))
            return null;

        Transform t = FindDeepChild(root, name);
        if (t != null) return t;

        string stripped = name;
        int colon = name.IndexOf(':');
        if (colon >= 0)
        {
            stripped = name.Substring(colon + 1);   // unprefixed rig
            t = FindDeepChild(root, stripped);
            if (t != null) return t;
        }

        // Toggle the "_end" tip suffix (some rigs name the tip "Index4", others "Index4_end").
        string a = name.EndsWith("_end") ? name.Substring(0, name.Length - 4) : name + "_end";
        t = FindDeepChild(root, a);
        if (t != null) return t;
        if (!ReferenceEquals(stripped, name))
        {
            string b = stripped.EndsWith("_end") ? stripped.Substring(0, stripped.Length - 4) : stripped + "_end";
            t = FindDeepChild(root, b);
            if (t != null) return t;
        }
        return null;
    }

    static string BoneAt(string[] arr, int i) => arr != null && i >= 0 && i < arr.Length ? arr[i] : null;

    public void TryAutoWirePlayerHands(Transform root)
    {
        if (root == null) return;

        Transform yBot = FindDeepChild(root, "Y Bot");
        bool useYBot = yBot != null && yBot.gameObject.activeInHierarchy;
        Transform searchRoot = useYBot ? yBot : root;

        // Rig bone names (arm_chain / finger_bones / end_effector) come from the standard right-hand
        // mixamo binding in code (ManualBufferCatalog.RigBinding defaults to
        // RagKleinFrameSource.BuildDefaultRightHandBinding) — NOT from mannualBuffer2.json. No file load.
        KleinRigPose binding = ManualBufferCatalog.RigBinding;
        KleinArmChain armChain = binding?.armChain;
        KleinFingerBones fb = binding?.fingerBones;

        Transform hand = ResolveBoneName(searchRoot, armChain?.hand, "arm_chain.hand");

        if (!useYBot)
        {
            Transform handGrabRoot = FindDeepChild(root, "PlayerHandGrab");
            if (handGrabRoot != null && handGrabRoot.childCount > 0)
                hand = handGrabRoot.GetChild(0);
        }

        // Tip = last declared index bone (…Index4_end) or rig_pose end_effector. FindBoneFlexible tolerates
        // the "_end" suffix difference, so a rig that names it "…Index4" still resolves from the same entry.
        string tipName = BoneAt(fb?.index, (fb?.index?.Length ?? 0) - 1) ?? binding?.endEffector;
        IndexFingerTip = ResolveBoneName(searchRoot, tipName, "finger_bones.index tip / end_effector") ?? hand;

        RightHandRoot = hand;
        RightArmPivot = ResolveBoneName(searchRoot, armChain?.upperArm, "arm_chain.upper_arm") ?? hand?.parent;
        RightShoulderPivot = ResolveBoneName(searchRoot, armChain?.shoulder, "arm_chain.shoulder");
        RightElbowPivot = ResolveBoneName(searchRoot, armChain?.forearm, "arm_chain.forearm");

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

        Transform index1 = ResolveBoneName(searchRoot, BoneAt(fb?.index, 0), "finger_bones.index[0]");
        Transform index2 = ResolveBoneName(searchRoot, BoneAt(fb?.index, 1), "finger_bones.index[1]");
        Transform index3 = ResolveBoneName(searchRoot, BoneAt(fb?.index, 2), "finger_bones.index[2]") ?? IndexFingerTip;
        Transform index4 = ResolveBoneName(searchRoot, BoneAt(fb?.index, 3), "finger_bones.index[3] tip");

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

            // Capture finger rest rotations ONCE, like the arm/shoulder/hand captures above.
            // Re-capturing on every re-wire would read the already-flexed pose as "rest", so each
            // press would compound the 38°/52° flex and the finger would spin continuously.
            if (!fingerRestCaptured)
            {
                if (index1 != null) index1RestRotation = index1.localRotation;
                if (index2 != null) index2RestRotation = index2.localRotation;
                if (index3 != null) index3RestRotation = index3.localRotation;
                fingerRestCaptured = true;
            }
        }

        BuildRightIndexChain(useYBot, RightHandRoot ?? hand, index1, index2, index3, index4);
        EnsureFingerTipCollider(IndexFingerTip);
        WireOtherFingers(searchRoot);
        LogRegistrySummaryOnce();
    }

    // Middle/ring/pinky/thumb curl support — needed to form a realistic pointing-press hand shape
    // (the klein frame only specifies the index effector; the other fingers come from hand_pose).
    struct CurlFinger { public Transform j1, j2, j3; public Quaternion r1, r2, r3; public float flexSign; public bool signDetected; }
    CurlFinger _middleFinger, _ringFinger, _pinkyFinger, _thumbFinger;
    bool _otherFingersCaptured;

    void WireOtherFingers(Transform searchRoot)
    {
        if (searchRoot == null) return;
        KleinFingerBones fb = ManualBufferCatalog.RigBinding?.fingerBones;
        _middleFinger = WireCurlFinger(searchRoot, "Middle", fb?.middle);
        _ringFinger = WireCurlFinger(searchRoot, "Ring", fb?.ring);
        _pinkyFinger = WireCurlFinger(searchRoot, "Pinky", fb?.pinky);
        _thumbFinger = WireCurlFinger(searchRoot, "Thumb", fb?.thumb);
        _otherFingersCaptured = true;
    }

    CurlFinger WireCurlFinger(Transform root, string name, string[] boneNames)
    {
        CurlFinger f = new CurlFinger();
        f.j1 = ResolveBoneName(root, BoneAt(boneNames, 0), "finger_bones." + name.ToLowerInvariant() + "[0]");
        f.j2 = ResolveBoneName(root, BoneAt(boneNames, 1), "finger_bones." + name.ToLowerInvariant() + "[1]");
        f.j3 = ResolveBoneName(root, BoneAt(boneNames, 2), "finger_bones." + name.ToLowerInvariant() + "[2]");
        // Capture rest ONCE (don't clobber with an already-curled pose on re-wire).
        if (!_otherFingersCaptured)
        {
            if (f.j1 != null) f.r1 = f.j1.localRotation;
            if (f.j2 != null) f.r2 = f.j2.localRotation;
            if (f.j3 != null) f.r3 = f.j3.localRotation;
        }
        else
        {
            // Preserve the previously captured rest for an already-known finger.
            CurlFinger prev = name == "Middle" ? _middleFinger : name == "Ring" ? _ringFinger : name == "Pinky" ? _pinkyFinger : _thumbFinger;
            f.r1 = prev.r1; f.r2 = prev.r2; f.r3 = prev.r3;
        }
        return f;
    }

    /// <summary>Curl middle/ring/pinky/thumb per the klein hand_pose so the hand forms a pointing press.</summary>
    void ApplyOtherFingerCurls(KleinHandPose pose, float weight)
    {
        if (pose == null || !pose.hasData) return;
        float w = Mathf.Clamp01(weight);
        CurlOneFinger(ref _middleFinger, pose.middle, w);
        CurlOneFinger(ref _ringFinger, pose.ring, w);
        CurlOneFinger(ref _pinkyFinger, pose.pinky, w);
        CurlOneFinger(ref _thumbFinger, pose.thumb, w);
    }

    void CurlOneFinger(ref CurlFinger f, KleinFingerPose fp, float weight)
    {
        if (f.j1 == null || fp == null) return;

        // Bone-behavior check: detect (once) which local-Z direction actually flexes this finger
        // TOWARD the palm (i.e. brings the tip closer to the wrist). This makes the curl realistic
        // on any rig instead of hardcoding a sign that may hyperextend toward the back of the hand.
        if (!f.signDetected)
        {
            f.flexSign = DetectFlexSign(f);
            f.signDetected = true;
        }

        float s = f.flexSign;
        float wgt = Mathf.Clamp01(weight);
        // Per-joint bend angles (degrees) come straight from hand_pose in mannualBuffer2.json.
        if (f.j1 != null) f.j1.localRotation = f.r1 * Quaternion.Euler(0f, 0f, s * fp.mcp * wgt);
        if (f.j2 != null) f.j2.localRotation = f.r2 * Quaternion.Euler(0f, 0f, s * fp.pip * wgt);
        if (f.j3 != null) f.j3.localRotation = f.r3 * Quaternion.Euler(0f, 0f, s * fp.dip * wgt);
    }

    /// <summary>
    /// Returns the local-Z sign that flexes the finger toward the palm. Probes a small +/- rotation
    /// on the base joint and keeps whichever brings the fingertip CLOSER to the wrist (real flexion
    /// closes the hand). Robust to rig axis/handedness conventions.
    /// </summary>
    float DetectFlexSign(CurlFinger f)
    {
        Transform tip = f.j3 ?? f.j2 ?? f.j1;
        if (tip == null || f.j1 == null || RightHandRoot == null)
            return -1f;

        Vector3 wrist = RightHandRoot.position;
        Quaternion orig = f.j1.localRotation;

        f.j1.localRotation = f.r1 * Quaternion.Euler(0f, 0f, 20f);
        float dPlus = Vector3.Distance(tip.position, wrist);

        f.j1.localRotation = f.r1 * Quaternion.Euler(0f, 0f, -20f);
        float dMinus = Vector3.Distance(tip.position, wrist);

        f.j1.localRotation = orig;
        return dMinus <= dPlus ? -1f : 1f;   // flexion = the sign that closes the finger toward the wrist
    }

    void ResetOtherFingerCurls()
    {
        ResetCurlFinger(_middleFinger);
        ResetCurlFinger(_ringFinger);
        ResetCurlFinger(_pinkyFinger);
        ResetCurlFinger(_thumbFinger);
    }

    void ResetCurlFinger(CurlFinger f)
    {
        if (f.j1 != null) f.j1.localRotation = f.r1;
        if (f.j2 != null) f.j2.localRotation = f.r2;
        if (f.j3 != null) f.j3.localRotation = f.r3;
    }

    /// <summary>Re-resolve finger/arm bones after Y Bot visual is enabled on the Photon player.</summary>
    public void RefreshRigWire()
    {
        if (_manualPoseActive)
            return;

        fingers.Clear();
        _rightIndexChain.Clear();
        _fingerRestRotations.Clear();
        // Do NOT reset the arm/shoulder/hand rest-captured flags here. RefreshRigWire runs every step
        // while the locomotion animator is still playing; re-capturing would read a mid-run arm swing as
        // "rest" and the IK would then reach from behind the back. Keep the first clean capture (the
        // finger rest flags are already preserved the same way) — the bones re-resolve to the same rig.
        IndexFingerTip = null;
        RightHandRoot = null;
        RightArmPivot = null;
        RightElbowPivot = null;
        RightShoulderPivot = null;
        UsesMixamoRig = false;
        _rigAnimators = null;
        _loggedWireStatus = false;
        _loggedRegistrySummary = false;
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
        _measuredTipDrop = 0.08f;
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
        // CLEAN-ROOM RAG MODE (first, before any default rig wiring): a Klein frame that declares an
        // ik_chain drives ONLY those bones, CCD-solved toward the target. It resolves its own bones from
        // the RAG names, so it needs NO default rig binding / wiring / MixamoManualPose. Whatever the RAG
        // lists in ik_chain is the entire body movement — nothing else, no mannualBuffer2 defaults.
        if (_manualFrame != null && _manualFrame.rigPose != null
            && _manualFrame.rigPose.ikChain != null && _manualFrame.rigPose.ikChain.Length >= 2)
        {
            ApplyKleinFramePureIk(_manualFrame, _manualTarget, _manualPressPhase);
            return;
        }

        // Reach-only (menu, no Klein frame) still uses the legacy wired arm pose.
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

        // Right-arm shaping is data-driven from mannualBuffer2.json (arm_pose). When a frame omits it,
        // these defaults reproduce the previous reach — except the elbow pole now carries a forward term.
        KleinArmPose armPose = _manualFrame != null ? _manualFrame.armPose : null;
        float poleSide = armPose != null ? armPose.poleSide : 0.45f;
        float poleForward = armPose != null ? armPose.poleForward : 0.20f;
        float poleDown = armPose != null ? armPose.poleDown : 0.28f;
        float shoulderLiftDeg = armPose != null ? armPose.shoulderLiftDeg : 18f;
        float wristHover = armPose != null ? armPose.wristHoverM : 0.012f;
        float reachFwdMin = armPose != null ? armPose.reachForwardMin : 0.36f;
        float reachFwdMax = armPose != null ? armPose.reachForwardMax : 0.50f;

        Transform shoulderRef = shoulder ?? upper;
        Vector3 surfacePoint;
        Vector3 fingerContact = Vector3.zero;
        bool kleinFingerPress = false;
        if (_manualFrame != null && _hasLockedReachTarget)
        {
            // Klein press onto the TOP surface. The two-bone IK positions the WRIST (hand bone); the
            // index points DOWN from it. We place the wrist so the FINGERTIP lands exactly where we want:
            // hovering above the surface during the approach, then settling onto it (with a tiny, force-
            // scaled press-in) at full press — so it physically touches, never hovers and never sinks deep.
            Vector3 topContact = _lockedReachTarget;

            float pressForceN = ResolveFrameForce(_manualFrame);
            float pressDepth = Mathf.Clamp(pressForceN, 0f, 1f) * 0.006f * pressW;   // press in a few mm at full press
            float approachHover = (wristHover + 0.03f) * (1f - pressW);              // bleeds to 0 as the press completes

            // Where the FINGERTIP should be this frame (world Y), then place the WRIST a measured
            // finger-drop above it so the down-pointing tip actually arrives there.
            float desiredTipY = topContact.y + approachHover - pressDepth;
            Vector3 wristGoal = new Vector3(topContact.x, desiredTipY + _measuredTipDrop, topContact.z);

            // Clamp the WRIST goal to arm length so the shoulder/elbow can physically reach it (the arm
            // still extends forward from the shoulder, never folding back across the chest).
            float armLen = Vector3.Distance(upper.position, forearm.position)
                         + Vector3.Distance(forearm.position, hand.position);
            float maxReach = Mathf.Max(0.1f, armLen);
            Vector3 toWrist = wristGoal - shoulderRef.position;
            if (toWrist.magnitude > maxReach)
                wristGoal = shoulderRef.position + toWrist.normalized * maxReach;

            surfacePoint = wristGoal;
            fingerContact = topContact;   // the hand aims at the real top-surface contact, not the raised wrist
            kleinFingerPress = true;
        }
        else
        {
            surfacePoint = _hasLockedReachTarget
                ? MixamoRightArmIKSolver.BuildDeskReachPoint(transform, shoulderRef, _lockedReachTarget, w, reachFwdMin, reachFwdMax)
                : MixamoRightArmIKSolver.BuildDeskReachPoint(transform, shoulderRef, upper.position + transform.forward * 0.42f, w, reachFwdMin, reachFwdMax);
            surfacePoint += Vector3.down * (pressW * 0.018f);
        }

        Vector3 pole = MixamoRightArmIKSolver.BuildElbowPole(transform, shoulderRef, poleSide, poleForward, poleDown);
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
            w,
            shoulderLiftDeg);

        // Aim the hand at the real contact (so the palm orients toward the button), not at the
        // raised wrist goal.
        ApplyMixamoHandAim(kleinFingerPress ? fingerContact : surfacePoint, w);

        // True finger IK: CCD-solve the RAG's ik_chain (RightHand→Index1..Index4_end) so the index TIP
        // lands on the actual contact (the meronym part), with the approach direction taken from the RAG's
        // approach_angle_deg (90° = press straight down) and the sink scaled by contact_force_n. This
        // replaces the old fixed-angle point so the finger follows the RAG, not a hardcoded bend table.
        if (kleinFingerPress && _manualFrame != null && _manualFrame.UsesUnityIk)
        {
            float approachDeg = _manualFrame.rigPose != null && _manualFrame.rigPose.approachAngleDeg > 0.01f
                ? _manualFrame.rigPose.approachAngleDeg : 90f;
            Vector3 horiz = RightHandRoot != null ? (RightHandRoot.position - fingerContact) : transform.forward;
            horiz.y = 0f;
            horiz = horiz.sqrMagnitude > 1e-6f ? horiz.normalized : transform.forward;
            // approach_angle_deg: 0° = press horizontally (finger comes from the wrist side), 90° = press
            // straight down (finger comes from directly above). Blend the approach normal accordingly.
            Vector3 approachNormal = Vector3.Slerp(horiz, Vector3.up, Mathf.Clamp01(approachDeg / 90f));
            ApplyMixamoIndexFingerContactIK(_manualFrame, fingerContact, approachNormal, pressW, w);
        }
        else
            ApplyMixamoIndexFingerContact(pressW, w);

        // Curl the non-index fingers per the klein hand_pose so the hand forms a pointing press.
        if (_manualFrame != null && _manualFrame.handPose != null && _manualFrame.handPose.hasData)
            ApplyOtherFingerCurls(_manualFrame.handPose, pressW);

        // Measure the live vertical gap from the wrist to the posed fingertip and feed it back next frame.
        // The wrist goal above uses this so the down-pointing tip converges onto the exact surface point,
        // regardless of the rig's finger length/angle — so the press physically touches (no hover, no deep sink).
        if (kleinFingerPress && IndexFingerTip != null && RightHandRoot != null)
        {
            float drop = RightHandRoot.position.y - IndexFingerTip.position.y;   // wrist → fingertip vertical gap
            _measuredTipDrop = Mathf.Clamp(Mathf.Lerp(_measuredTipDrop, drop, 0.5f), 0.02f, 0.25f);
        }
    }

    void ApplyMixamoHandAim(Vector3 surfaceTarget, float weight)
    {
        if (RightHandRoot == null || !handRestCaptured)
            return;

        // Aim from the hand root (its position is stable when the hand rotates about its own
        // pivot). Using the fingertip here creates a feedback loop — rotating the hand moves the
        // tip, which changes the aim, which spins the hand — especially once the hand is near the
        // target and the aim vector shrinks toward zero.
        Vector3 aimOrigin = RightHandRoot.position;
        Vector3 toSurface = surfaceTarget - aimOrigin;
        if (toSurface.sqrMagnitude < 0.0025f)   // < 5cm: too close to aim stably, hold rest-relative
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

    float _indexFlexSign = -1f;
    bool _indexSignDetected;

    /// <summary>
    /// Index finger pose driven by hand_pose.index per-joint angles (mcp/pip/dip degrees) from
    /// mannualBuffer2.json — kept mostly straight to point. All angles are in the JSON (no code
    /// constants): raise index.mcp to angle it down for a steeper press, lower it to point flatter.
    /// </summary>
    void ApplyMixamoIndexPoint(KleinFrame frame, float pressWeight)
    {
        if (fingers.Count <= HandActionLibrary.IndexFinger)
            return;
        FingerData index = fingers[HandActionLibrary.IndexFinger];
        Transform j1 = index.joints.Count > 0 ? index.joints[0].jointTransform : null;
        Transform j2 = index.joints.Count > 1 ? index.joints[1].jointTransform : null;
        Transform j3 = index.joints.Count > 2 ? index.joints[2].jointTransform : null;
        if (j1 == null)
            return;

        if (!_indexSignDetected)
        {
            CurlFinger probe = new CurlFinger { j1 = j1, j2 = j2, j3 = j3, r1 = index1RestRotation, r2 = index2RestRotation, r3 = index3RestRotation };
            _indexFlexSign = DetectFlexSign(probe);
            _indexSignDetected = true;
        }

        KleinFingerPose fp = frame?.handPose != null ? frame.handPose.index : null;
        if (fp == null)
            fp = new KleinFingerPose { mcp = 14f, pip = 6f, dip = 4f };   // gentle point fallback

        float press = Mathf.Clamp01(pressWeight);
        float s = _indexFlexSign;

        // force_newtons adds a little extra fingertip flex as the press completes (pip/dip only, so the
        // finger keeps pointing). A harder commanded press visibly drives the tip further into the
        // surface; force is clamped to [0,1] N so the extra bend stays in a gentle, natural range.
        float pressForceN = ResolveFrameForce(frame);
        float forceFlex = Mathf.Clamp(pressForceN, 0f, 1f) * press;
        float pipBend = fp.pip * press + forceFlex * 8f;
        float dipBend = fp.dip * press + forceFlex * 14f;

        j1.localRotation = index1RestRotation * Quaternion.Euler(0f, 0f, s * fp.mcp * press);
        if (j2 != null) j2.localRotation = index2RestRotation * Quaternion.Euler(0f, 0f, s * pipBend);
        if (j3 != null) j3.localRotation = index3RestRotation * Quaternion.Euler(0f, 0f, s * dipBend);
    }

    /// <summary>Resolve a frame's press force in Newtons with the same precedence the motor executor
    /// uses: top-level force_newtons first, then rig_pose.contact_force_n, else 0.</summary>
    float ResolveFrameForce(KleinFrame frame)
    {
        if (frame == null)
            return 0f;
        if (frame.hasForceNewtons)
            return frame.forceNewtons;
        return frame.rigPose != null ? frame.rigPose.contactForceN : 0f;
    }

    /// <summary>
    /// Data-driven Mixamo index-finger contact. CCD-solves the klein-frame ik_chain
    /// (RightHand→Index1→Index2→Index3→Index4_end, prewired in <see cref="_rightIndexChain"/>) so the
    /// end_effector tip lands on the contact point. approach_angle_deg sets the contact direction and
    /// contact_force_n scales how far the tip sinks into the surface as the press ramps in. The chain
    /// is reset to its captured rest each frame so the per-frame CCD is deterministic (no accumulation).
    /// </summary>
    void ApplyMixamoIndexFingerContactIK(KleinFrame frame, Vector3 contactPoint, Vector3 approachNormal, float pressWeight, float reachWeight)
    {
        ResetMixamoIndexToRest();

        if (_rightIndexChain.Count < 2)
        {
            ApplyMixamoIndexFingerContact(pressWeight, reachWeight);   // chain unavailable — fall back
            return;
        }

        if (approachNormal.sqrMagnitude < 1e-6f)
            approachNormal = Vector3.up;
        approachNormal.Normalize();

        float force = frame?.rigPose != null && frame.rigPose.contactForceN > 0.0001f
            ? frame.rigPose.contactForceN
            : (frame != null && frame.hasForceNewtons ? frame.forceNewtons : 0.25f);

        // Hover just above the contact when unpressed; sink in proportional to press × contact force.
        float press = Mathf.Clamp01(pressWeight);
        float pressDepth = press * Mathf.Clamp(force, 0.05f, 1f) * 0.02f;
        float hover = 0.012f * (1f - press);
        Vector3 ikTarget = contactPoint + approachNormal * (hover - pressDepth);

        FingerChainIKSolver.Solve(_rightIndexChain, ikTarget, approachNormal);
    }

    /// <summary>
    /// CLEAN-ROOM RAG pose. CCD-solves EXACTLY the bones named in <c>frame.rig_pose.ik_chain</c> toward
    /// the world contact — approach_angle_deg sets the approach direction, contact_force_n the press-in
    /// depth. NOTHING else moves: no default two-bone arm pose, no elbow pole, no fixed finger angles.
    /// So whatever bones the RAG lists in ik_chain are the entire body movement. An empty/short chain
    /// (e.g. torso RESTING, or a hand-only chain that can't reach) intentionally leaves the rest of the
    /// body at rest — that visible gap tells you which bones to ADD to the RAG's ik_chain.
    /// </summary>
    public void ApplyKleinFramePureIk(KleinFrame frame, Vector3 worldTarget, float pressPhase)
    {
        if (frame == null || frame.rigPose == null)
            return;
        string[] names = frame.rigPose.ikChain;
        if (names == null || names.Length < 2)
            return;   // RAG defines no drivable chain for this frame (e.g. torso RESTING) — move nothing.

        string sig = string.Join("|", names);
        if (!string.Equals(sig, _ragChainSig, System.StringComparison.Ordinal))
        {
            _ragChainSig = sig;
            _ragChain.Clear();
            Transform searchRoot = RigSearchRoot();
            foreach (string n in names)
            {
                Transform b = ResolveBoneName(searchRoot, n, "rag ik_chain");
                if (b == null) continue;
                _ragChain.Add(b);
                if (!_ragChainRest.ContainsKey(b)) _ragChainRest[b] = b.localRotation;
            }
            Debug.Log($"[HandRotationManager] RAG ik_chain '{frame.kleinFrameId}' resolved {_ragChain.Count}/{names.Length} bone(s): {sig}");
        }
        if (_ragChain.Count < 2)
            return;

        SuppressRigAnimators();

        // Reset the chain to captured rest so CCD is deterministic (no per-frame accumulation).
        foreach (Transform b in _ragChain)
            if (b != null && _ragChainRest.TryGetValue(b, out Quaternion rest))
                b.localRotation = rest;

        float approachDeg = frame.rigPose.approachAngleDeg > 0.01f ? frame.rigPose.approachAngleDeg : 90f;
        Vector3 horiz = worldTarget - _ragChain[0].position;
        horiz.y = 0f;
        horiz = horiz.sqrMagnitude > 1e-6f ? horiz.normalized : transform.forward;
        // 90° = press straight down (normal up); 0° = horizontal from the chain-root side.
        Vector3 approachNormal = Vector3.Slerp(horiz, Vector3.up, Mathf.Clamp01(approachDeg / 90f));

        float force = frame.hasForceNewtons ? frame.forceNewtons
                    : (frame.rigPose.contactForceN > 0.0001f ? frame.rigPose.contactForceN : 0.25f);
        float press = Mathf.Clamp01(pressPhase);
        float sink = press * Mathf.Clamp(force, 0.05f, 1f) * 0.02f;
        float hover = 0.012f * (1f - press);
        Vector3 ikTarget = worldTarget + approachNormal * (hover - sink);

        FingerChainIKSolver.Solve(_ragChain, ikTarget, approachNormal);
    }

    /// <summary>Root under which to resolve rig_pose.ik_chain bone names (the animated Y Bot rig if present).</summary>
    Transform RigSearchRoot()
    {
        Transform yBot = FindDeepChild(transform, "Y Bot");
        return (yBot != null && yBot.gameObject.activeInHierarchy) ? yBot : transform;
    }

    /// <summary>Approx world length of the index finger (sum of joint segments to the tip).</summary>
    float EstimateIndexFingerLength()
    {
        if (fingers.Count <= HandActionLibrary.IndexFinger)
            return 0.08f;
        FingerData index = fingers[HandActionLibrary.IndexFinger];
        float len = 0f;
        Transform prev = null;
        for (int i = 0; i < index.joints.Count; i++)
        {
            Transform t = index.joints[i].jointTransform;
            if (t == null) continue;
            if (prev != null) len += Vector3.Distance(prev.position, t.position);
            prev = t;
        }
        if (IndexFingerTip != null && prev != null && IndexFingerTip != prev)
            len += Vector3.Distance(prev.position, IndexFingerTip.position);
        return len > 0.001f ? len : 0.08f;
    }

    void ResetMixamoIndexToRest()
    {
        if (fingers.Count <= HandActionLibrary.IndexFinger)
            return;
        FingerData index = fingers[HandActionLibrary.IndexFinger];
        if (index.joints.Count > 0 && index.joints[0].jointTransform != null)
            index.joints[0].jointTransform.localRotation = index1RestRotation;
        if (index.joints.Count > 1 && index.joints[1].jointTransform != null)
            index.joints[1].jointTransform.localRotation = index2RestRotation;
        if (index.joints.Count > 2 && index.joints[2].jointTransform != null)
            index.joints[2].jointTransform.localRotation = index3RestRotation;
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
        {
            // Slight natural elbow bend at rest (arm_pose.rest.forearm_bend_deg) so the idle arm isn't
            // ramrod-straight. 0 (no arm_pose) keeps the exact captured rest.
            float restBend = _manualFrame != null && _manualFrame.armPose != null ? _manualFrame.armPose.restForearmBendDeg : 0f;
            RightElbowPivot.localRotation = restBend > 0.01f
                ? rightElbowRestRotation * Quaternion.Euler(restBend, 0f, 0f)
                : rightElbowRestRotation;
        }
        if (handRestCaptured && RightHandRoot != null)
            RightHandRoot.localRotation = rightHandRestRotation;
        ResetFingerCurl();
        ResetOtherFingerCurls();
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
