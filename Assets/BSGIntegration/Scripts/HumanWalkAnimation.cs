using UnityEngine;

/// <summary>
/// Procedural walking animation for the pivot-based human body of MA/MB/MC agents.
///
/// Architecture
/// ────────────
/// All rotations happen on PIVOT objects whose origin is at the joint (hip, knee, shoulder,
/// elbow).  Mesh objects hang as children offset downward, so rotation naturally swings
/// the limb from its top like a real joint — no sliding or spinning in place.
///
/// Walk cycle (only active when IsMoving == true)
/// ───────────────────────────────────────────────
///  • Hip pivots  → forward/backward swing (sin wave, 180° out of phase L vs R)
///  • Knee pivots → bend on the swinging-back leg (rectified sin, so only bends rearward)
///  • Arm pivots  → oppose leg swing (natural counterbalance)
///  • Elbow pivots→ subtle secondary bend follow
///  • BodyRoot    → slight vertical bob (sin at double frequency)
///  • Torso       → subtle counter-yaw
///
/// Idle (IsMoving == false, IsFrozen == false)
/// ───────────────────────────────────────────
///  • Gentle torso sway + head micro-nod
///  • All limbs blend back to rest pose
///
/// Frozen
/// ──────
///  • Instantly snaps everything to rest pose
/// </summary>
public class HumanWalkAnimation : MonoBehaviour
{
    // ── Tunable gait parameters ───────────────────────────────────────────────
    [Header("Gait")]
    public float walkCycleSpeed  = 3.0f;   // angular frequency (rad/s)
    public float legSwingDeg     = 35f;    // upper-leg forward/back arc (degrees)
    public float kneeBendDeg     = 30f;    // max knee bend on rearward leg
    public float armSwingDeg     = 25f;    // upper-arm swing arc
    public float elbowBendDeg    = 14f;    // passive elbow follow
    public float bodyBobHeight   = 0.018f; // vertical bob amplitude (local units)
    public float blendSpeed      = 8f;     // how fast walk/idle blends in or out

    [Header("Idle")]
    public float idleSwayDeg     = 1.8f;   // torso idle sway amplitude
    public float idleSwaySpeed   = 1.0f;   // rad/s for idle sway

    // ── Public state ──────────────────────────────────────────────────────────
    public bool IsMoving { get; private set; }
    public bool IsFrozen { get; private set; }

    // ── Cached pivots ─────────────────────────────────────────────────────────
    private Transform _bodyRoot;
    private Transform _leftHip,  _rightHip;
    private Transform _leftKnee, _rightKnee;
    private Transform _leftArm,  _rightArm;
    private Transform _leftElbow,_rightElbow;
    private Transform _torso,    _head;

    // ── Rest-pose rotations (snapped to on freeze / blend-out) ────────────────
    private Quaternion _rBodyRoot;
    private Quaternion _rLHip,  _rRHip;
    private Quaternion _rLKnee, _rRKnee;
    private Quaternion _rLArm,  _rRArm;
    private Quaternion _rLElbow,_rRElbow;
    private Quaternion _rTorso, _rHead;
    private Vector3    _bodyRootRestPos;

    // ── Internal animation state ──────────────────────────────────────────────
    private float _phase     = 0f;   // master gait phase (radians)
    private float _idlePhase = 0f;   // idle sway phase
    private float _blendT    = 0f;   // 0 = rest, 1 = full walk
    private bool  _ready     = false;

    [Header("Goal Buffer only — reach / grab pulse")]
    [Tooltip("Short forward reach at Goal Buffer when a bottom/middle/top ball is collected.")]
    public float goalBufferGrabPulseDuration = 0.38f;
    private float _goalBufferGrabPulseRem;

    // ─────────────────────────────────────────────────────────────────────────
    // Unity lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    void Start()  => CachePivots();

    void Update()
    {
        if (!_ready) { CachePivots(); return; }

        if (IsFrozen)
        {
            SnapToRest();
            return;
        }

        if (IsMoving)
        {
            _blendT  = Mathf.MoveTowards(_blendT, 1f, Time.deltaTime * blendSpeed);
            _phase  += walkCycleSpeed * Time.deltaTime;
        }
        else
        {
            _blendT     = Mathf.MoveTowards(_blendT, 0f, Time.deltaTime * blendSpeed);
            _idlePhase += idleSwaySpeed * Time.deltaTime;
        }

        ApplyWalkCycle(_blendT);
        ApplyIdleSway(1f - _blendT);
        if (_goalBufferGrabPulseRem > 0f)
            ApplyGoalBufferGrabOverlay();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Call when the agent starts moving toward a target.</summary>
    public void StartWalking()
    {
        if (IsFrozen) return;
        IsMoving = true;
    }

    /// <summary>Call when the agent arrives or stops.</summary>
    public void StopWalking()
    {
        IsMoving = false;
    }

    /// <summary>Brief arms-forward cue while interacting with Goal Buffer triple balls — does not affect other stations.</summary>
    public void PlayGoalBufferGrabPulse()
    {
        if (IsFrozen) return;
        _goalBufferGrabPulseRem = Mathf.Max(_goalBufferGrabPulseRem, goalBufferGrabPulseDuration);
    }

    /// <summary>Locks all motion (agent is waiting / frozen between passes).</summary>
    public void Freeze()
    {
        IsFrozen = true;
        IsMoving = false;
        SnapToRest();
    }

    /// <summary>Re-enables animation when the agent resumes a cognitive pass.</summary>
    public void Unfreeze()
    {
        IsFrozen = false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Pivot caching
    // ─────────────────────────────────────────────────────────────────────────

    private void CachePivots()
    {
        _bodyRoot  = FindDeep(HumanBodyBuilder.BodyRootBone);
        _leftHip   = FindDeep(HumanBodyBuilder.LeftHipPivot);
        _rightHip  = FindDeep(HumanBodyBuilder.RightHipPivot);
        _leftKnee  = FindDeep(HumanBodyBuilder.LeftKneePivot);
        _rightKnee = FindDeep(HumanBodyBuilder.RightKneePivot);
        _leftArm   = FindDeep(HumanBodyBuilder.LeftArmPivot);
        _rightArm  = FindDeep(HumanBodyBuilder.RightArmPivot);
        _leftElbow = FindDeep(HumanBodyBuilder.LeftElbowPivot);
        _rightElbow= FindDeep(HumanBodyBuilder.RightElbowPivot);
        _torso     = FindDeep(HumanBodyBuilder.TorsoBone);
        _head      = FindDeep(HumanBodyBuilder.HeadBone);

        _ready = _leftHip != null && _rightHip != null;
        if (!_ready) return;

        // Capture rest poses
        _rBodyRoot      = _bodyRoot  != null ? _bodyRoot.localRotation  : Quaternion.identity;
        _bodyRootRestPos= _bodyRoot  != null ? _bodyRoot.localPosition  : Vector3.zero;
        _rLHip          = _leftHip.localRotation;
        _rRHip          = _rightHip.localRotation;
        _rLKnee         = _leftKnee  != null ? _leftKnee.localRotation  : Quaternion.identity;
        _rRKnee         = _rightKnee != null ? _rightKnee.localRotation : Quaternion.identity;
        _rLArm          = _leftArm   != null ? _leftArm.localRotation   : Quaternion.identity;
        _rRArm          = _rightArm  != null ? _rightArm.localRotation  : Quaternion.identity;
        _rLElbow        = _leftElbow != null ? _leftElbow.localRotation : Quaternion.identity;
        _rRElbow        = _rightElbow!= null ? _rightElbow.localRotation: Quaternion.identity;
        _rTorso         = _torso     != null ? _torso.localRotation     : Quaternion.identity;
        _rHead          = _head      != null ? _head.localRotation      : Quaternion.identity;
    }

    private Transform FindDeep(string boneName)
    {
        // Searches entire child hierarchy (pivots may be nested)
        return transform.Find(boneName) ?? DeepFind(transform, boneName);
    }

    private static Transform DeepFind(Transform t, string name)
    {
        foreach (Transform child in t)
        {
            if (child.name == name) return child;
            Transform found = DeepFind(child, name);
            if (found != null) return found;
        }
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Walk cycle — ONLY ACTIVE when IsMoving == true
    // ─────────────────────────────────────────────────────────────────────────

    private void ApplyWalkCycle(float blend)
    {
        if (blend < 0.001f)
        {
            BlendAllToRest(Time.deltaTime * blendSpeed * 2f);
            return;
        }

        float sin  = Mathf.Sin(_phase);               // -1 .. +1
        float cos  = Mathf.Cos(_phase);

        // ── Leg swing (L and R are 180° apart) ────────────────────────────────
        float legL =  sin * legSwingDeg;   // left hip angle
        float legR = -sin * legSwingDeg;   // right hip (opposite)

        SetRot(_leftHip,  Slerp(_rLHip,  _rLHip  * Qx( legL), blend));
        SetRot(_rightHip, Slerp(_rRHip,  _rRHip  * Qx( legR), blend));

        // ── Knee bend: only on the leg swinging backward (negative sin for left) ──
        // When sin < 0 → left leg going back → bend left knee
        // When sin > 0 → right leg going back → bend right knee
        float kneeLBend = kneeBendDeg * Mathf.Max(0f, -sin);  // left knee bends rearward
        float kneeRBend = kneeBendDeg * Mathf.Max(0f,  sin);  // right knee bends rearward

        SetRot(_leftKnee,  Slerp(_rLKnee,  _rLKnee  * Qx(-kneeLBend), blend));
        SetRot(_rightKnee, Slerp(_rRKnee,  _rRKnee  * Qx(-kneeRBend), blend));

        // ── Arm swing (opposite to legs for natural counterbalance) ───────────
        float armL = -sin * armSwingDeg;
        float armR =  sin * armSwingDeg;

        SetRot(_leftArm,  Slerp(_rLArm,  _rLArm  * Qx(armL), blend));
        SetRot(_rightArm, Slerp(_rRArm,  _rRArm  * Qx(armR), blend));

        // ── Elbow passive follow ───────────────────────────────────────────────
        float elbowPhase = Mathf.Sin(_phase + 0.5f);
        float elbowL =  elbowPhase * elbowBendDeg;
        float elbowR = -elbowPhase * elbowBendDeg;

        SetRot(_leftElbow,  Slerp(_rLElbow,  _rLElbow  * Qx(elbowL), blend));
        SetRot(_rightElbow, Slerp(_rRElbow,  _rRElbow  * Qx(elbowR), blend));

        // ── Torso counter-twist ────────────────────────────────────────────────
        float torsoYaw = cos * 3.5f;
        SetRot(_torso, Slerp(_rTorso, _rTorso * Quaternion.Euler(0f, torsoYaw, 0f), blend));

        // ── Vertical body bob (double-frequency, so bob twice per full step) ──
        if (_bodyRoot != null)
        {
            float bob = Mathf.Abs(sin) * bodyBobHeight;  // always positive → go up on each step
            _bodyRoot.localPosition = Vector3.Lerp(
                _bodyRootRestPos,
                _bodyRootRestPos + new Vector3(0f, bob, 0f),
                blend);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Idle sway — only when NOT walking
    // ─────────────────────────────────────────────────────────────────────────

    private void ApplyIdleSway(float weight)
    {
        if (weight < 0.01f) return;

        float sway = Mathf.Sin(_idlePhase) * idleSwayDeg;
        if (_torso != null)
            _torso.localRotation = Quaternion.Slerp(
                _torso.localRotation,
                _rTorso * Quaternion.Euler(0f, sway, sway * 0.3f),
                weight * Time.deltaTime * 3f);

        if (_head != null)
        {
            float nod = Mathf.Sin(_idlePhase * 1.4f) * 1.2f;
            _head.localRotation = Quaternion.Slerp(
                _head.localRotation,
                _rHead * Quaternion.Euler(nod, sway * 0.4f, 0f),
                weight * Time.deltaTime * 3f);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void SnapToRest()
    {
        SetRot(_leftHip,   _rLHip);   SetRot(_rightHip,   _rRHip);
        SetRot(_leftKnee,  _rLKnee);  SetRot(_rightKnee,  _rRKnee);
        SetRot(_leftArm,   _rLArm);   SetRot(_rightArm,   _rRArm);
        SetRot(_leftElbow, _rLElbow); SetRot(_rightElbow, _rRElbow);
        SetRot(_torso,     _rTorso);  SetRot(_head,       _rHead);
        if (_bodyRoot != null) { _bodyRoot.localPosition = _bodyRootRestPos; }
        _blendT = 0f;
    }

    private void BlendAllToRest(float t)
    {
        SmoothRot(_leftHip,   _rLHip,   t); SmoothRot(_rightHip,   _rRHip,   t);
        SmoothRot(_leftKnee,  _rLKnee,  t); SmoothRot(_rightKnee,  _rRKnee,  t);
        SmoothRot(_leftArm,   _rLArm,   t); SmoothRot(_rightArm,   _rRArm,   t);
        SmoothRot(_leftElbow, _rLElbow, t); SmoothRot(_rightElbow, _rRElbow, t);
        SmoothRot(_torso,     _rTorso,  t); SmoothRot(_head,       _rHead,   t);
        if (_bodyRoot != null)
            _bodyRoot.localPosition = Vector3.Lerp(_bodyRoot.localPosition, _bodyRootRestPos, t);
    }

    private static void SetRot(Transform t, Quaternion r)
    { if (t != null) t.localRotation = r; }

    private static void SmoothRot(Transform t, Quaternion target, float speed)
    { if (t != null) t.localRotation = Quaternion.Slerp(t.localRotation, target, speed); }

    private static Quaternion Slerp(Quaternion a, Quaternion b, float t)
        => Quaternion.Slerp(a, b, t);

    private static Quaternion Qx(float deg)
        => Quaternion.Euler(deg, 0f, 0f);

    void ApplyGoalBufferGrabOverlay()
    {
        if (!_ready || _leftArm == null || _rightArm == null) return;

        _goalBufferGrabPulseRem -= Time.deltaTime;
        float u = goalBufferGrabPulseDuration > 0.01f
            ? Mathf.Clamp01(_goalBufferGrabPulseRem / goalBufferGrabPulseDuration)
            : 0f;
        float w = u * u;

        Quaternion reachL = _rLArm * Quaternion.Euler(-50f, 6f, 0f);
        Quaternion reachR = _rRArm * Quaternion.Euler(-50f, -6f, 0f);
        _leftArm.localRotation = Quaternion.Slerp(_leftArm.localRotation, reachL, w * 0.85f * Time.deltaTime * 18f);
        _rightArm.localRotation = Quaternion.Slerp(_rightArm.localRotation, reachR, w * 0.85f * Time.deltaTime * 18f);

        if (_leftElbow != null && _rightElbow != null)
        {
            Quaternion bendL = _rLElbow * Quaternion.Euler(-35f, 0f, 0f);
            Quaternion bendR = _rRElbow * Quaternion.Euler(-35f, 0f, 0f);
            _leftElbow.localRotation = Quaternion.Slerp(_leftElbow.localRotation, bendL, w * 0.9f * Time.deltaTime * 16f);
            _rightElbow.localRotation = Quaternion.Slerp(_rightElbow.localRotation, bendR, w * 0.9f * Time.deltaTime * 16f);
        }

        if (_goalBufferGrabPulseRem <= 0f)
            _goalBufferGrabPulseRem = 0f;
    }
}
