using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Converts the designated player's Mixamo Y-Bot skeleton into an ArticulationBody ragdoll
/// suitable for ML-Agents PPO locomotion, and exposes the actuated joints for the agent.
///
/// Reduced rig (skipped bones stay rigid relative to their nearest AB ancestor):
///   Hips (root, free) · Spine (spherical) · UpLeg L/R (spherical) · Leg L/R (revolute knee)
///   Foot L/R (revolute ankle) · Arm L/R (spherical shoulder) · ForeArm L/R (revolute elbow)
///
/// Bakes in the hard-won stability fixes (see locomotion memory):
///   • unit scale assumed (caller forces scale=1 via DesignatedPhysicalPlayerAppearance.LocomotionRigActive)
///   • self-collision ignored across all body pairs (AB only auto-ignores parent↔child)
///   • capped drive stiffness/force, damping floor
///   • bumped solver iterations
///   • ground sampled from collider bounds.max.y (top face), never raw hit.point.y
///   • ResetAllJoints zeroes reduced-coordinate jointPosition/jointVelocity (not just rigid vel)
/// </summary>
[DisallowMultipleComponent]
public class YBotLocomotionRig : MonoBehaviour
{
    // --- tunables (raised for stand/stability curriculum — stiff enough to hold pose, damped to avoid jitter) ---
    public const float MaxStiffness = 500f;
    public const float MaxForceLimit = 400f;
    public const float MinDampingRatio = 0.35f; // damping >= ratio * stiffness

    // --- one-way hinges (knee / elbow) ---------------------------------------------------------
    // A knee and an elbow bend in ONE direction only. Driving them symmetrically (±swingLimit) lets
    // the knee fold BACKWARDS, and a backwards knee cannot carry body weight — the hips sink while
    // the torso stays vertical, which is exactly the 89%-"sank" failure the walker log reports.
    // These joints therefore get an asymmetric range: full flexion one way, a few degrees of slack
    // the other way (real joints are not perfectly rigid at full extension).
    public const float HingeHyperextendSlackDeg = 5f;

    // Which jointPosition sign corresponds to FLEXION (bending the right way). This depends on the
    // Mixamo bone axis and cannot be read off the code, so it is a single flip point:
    //   +1 => flexion is positive.  -1 => flexion is negative.
    // HOW TO VERIFY: the rig logs "HINGE RANGE" every 10 s with the observed knee angle range. If the
    // knees sit pinned near 0 and the bot walks stiff-legged, this sign is wrong — flip it to -1f.
    public const float HingeFlexionSign = 1f;

    [System.Serializable]
    public class Joint
    {
        public ArticulationBody body;
        public string boneSuffix;
        public bool spherical;     // true = 3 DOF (x/y/z drive), false = revolute (x drive)
        public int dofStartIndex;  // index into the flat action/obs vector
        public int dofCount;       // 3 (spherical) or 1 (revolute)
        public bool oneWayHinge;   // knee/elbow — tracked so the HINGE RANGE log can verify the sign
        public bool isHip;         // hip — all 3 DOF logged separately to identify the abduction axis
    }

    // Bone suffixes (matched against transform names that may carry a "mixamorig:" namespace).
    const string Hips = "Hips";

    // jointType, sphericalSwingLimit, twistLimit, stiffness, forceLimit, mass
    struct BoneSpec
    {
        public string suffix;
        public bool spherical;
        public float swingLimit;   // degrees (spherical) / lower-upper half-range (revolute)
        public bool oneWayHinge;   // knee/elbow: full flexion one way, HingeHyperextendSlackDeg the other
        public bool isFoot;        // gets a FLAT BOX sole instead of a capsule — see AddFootBoxCollider
        public bool isHip;         // per-axis range is logged so abduction can be identified
        public float stiffness;
        public float forceLimit;
        public float mass;
        public float colliderRadius;
    }

    static readonly BoneSpec[] Specs =
    {
        // suffix,        spherical, swing, stiff,  force,  mass, radius
        new BoneSpec{ suffix="Spine",     spherical=true,  swingLimit=20f, stiffness=420f, forceLimit=350f, mass=8f,   colliderRadius=0.12f },
        // Hip: 45f -> 30f. The knee data ruled the knee OUT as the cause of the collapse (it peaks at
        // ~56 deg, which only drops the hips to ~0.59 m, but they reach 0.37 m), while the torso stays
        // vertical (upright 0.98) and tipping is only 7%. The remaining mechanism that fits all of that
        // is the legs SPLAYING sideways — hip abduction — i.e. sliding into the splits with a straight
        // back. Symmetric +-45 deg on all three axes makes that freely available. 30 deg is
        // sign-agnostic (it narrows every axis, so it cannot be applied backwards) and still leaves
        // enough hip flexion for a walking stride.
        new BoneSpec{ suffix="LeftUpLeg",  spherical=true,  swingLimit=30f, isHip=true, stiffness=500f, forceLimit=400f, mass=6f,   colliderRadius=0.09f },
        new BoneSpec{ suffix="RightUpLeg", spherical=true,  swingLimit=30f, isHip=true, stiffness=500f, forceLimit=400f, mass=6f,   colliderRadius=0.09f },
        // Knee: ONE-WAY hinge — 90° of flexion, only 5° the other way. Symmetric ±80° let it fold
        // backwards, which cannot bear weight (see HingeFlexionSign).
        new BoneSpec{ suffix="LeftLeg",    spherical=false, swingLimit=90f, oneWayHinge=true, stiffness=500f, forceLimit=400f, mass=4f,   colliderRadius=0.07f }, // knee
        new BoneSpec{ suffix="RightLeg",   spherical=false, swingLimit=90f, oneWayHinge=true, stiffness=500f, forceLimit=400f, mass=4f,   colliderRadius=0.07f },
        // Ankle stays a SYMMETRIC hinge (real ankles dorsiflex and plantarflex both ways), but the foot
        // gets a flat BOX sole — a capsule here degenerates to a sphere and destroys the support polygon.
        new BoneSpec{ suffix="LeftFoot",   spherical=false, swingLimit=35f, isFoot=true, stiffness=380f, forceLimit=320f, mass=1.5f, colliderRadius=0.06f }, // ankle
        new BoneSpec{ suffix="RightFoot",  spherical=false, swingLimit=35f, isFoot=true, stiffness=380f, forceLimit=320f, mass=1.5f, colliderRadius=0.06f },
        new BoneSpec{ suffix="LeftArm",    spherical=true,  swingLimit=45f, stiffness=280f, forceLimit=220f, mass=2f,   colliderRadius=0.06f }, // shoulder
        new BoneSpec{ suffix="RightArm",   spherical=true,  swingLimit=45f, stiffness=280f, forceLimit=220f, mass=2f,   colliderRadius=0.06f },
        // Elbow: same one-way hinge as the knee (less critical for standing, but it should not
        // hyperextend either — a backwards arm changes the mass distribution the balance relies on).
        new BoneSpec{ suffix="LeftForeArm",spherical=false, swingLimit=80f, oneWayHinge=true, stiffness=240f, forceLimit=180f, mass=1.5f, colliderRadius=0.05f }, // elbow
        new BoneSpec{ suffix="RightForeArm",spherical=false,swingLimit=80f, oneWayHinge=true, stiffness=240f, forceLimit=180f, mass=1.5f, colliderRadius=0.05f },
    };

    public ArticulationBody Root { get; private set; }       // Hips
    public IReadOnlyList<Joint> Joints => _joints;
    public int TotalDof { get; private set; }
    public bool IsBuilt => Root != null && _joints.Count > 0;

    readonly List<Joint> _joints = new List<Joint>();
    readonly List<ArticulationBody> _allBodies = new List<ArticulationBody>();
    Vector3 _rootSpawnLocalPos;
    Quaternion _rootSpawnLocalRot;

    /// <summary>
    /// Builds the AB rig under <paramref name="skeletonRoot"/> (the "Y Bot" transform). Returns false
    /// if the Hips bone or required bones are missing. Idempotent — returns the existing build.
    /// </summary>
    public bool Build(Transform skeletonRoot)
    {
        if (IsBuilt)
            return true;
        if (skeletonRoot == null)
        {
            Debug.LogError("[YBotLocomotionRig] No skeleton root provided.");
            return false;
        }

        Transform hips = FindBone(skeletonRoot, Hips);
        if (hips == null)
        {
            Debug.LogError("[YBotLocomotionRig] Could not find Hips bone under skeleton — aborting rig build.");
            return false;
        }

        // Stiffer global solver so stacked joint drives don't diverge (memory gotcha #6).
        Physics.defaultSolverIterations = Mathf.Max(Physics.defaultSolverIterations, 60);
        Physics.defaultSolverVelocityIterations = Mathf.Max(Physics.defaultSolverVelocityIterations, 12);

        // --- root: Hips ---
        Root = EnsureBody(hips);
        Root.immovable = false;
        Root.useGravity = true;
        Root.mass = 12f;
        AddCapsuleCollider(hips, 0.14f);
        _allBodies.Add(Root);

        // --- actuated bones ---
        int dofCursor = 0;
        foreach (BoneSpec spec in Specs)
        {
            Transform bone = FindBone(skeletonRoot, spec.suffix);
            if (bone == null)
            {
                Debug.LogWarning($"[YBotLocomotionRig] Bone '{spec.suffix}' not found — skipped.");
                continue;
            }

            ArticulationBody ab = EnsureBody(bone);
            ab.useGravity = true;
            ab.mass = spec.mass;
            ConfigureJoint(ab, spec);
            if (UseBoxFeet && spec.isFoot)
                AddFootBoxCollider(bone, spec.colliderRadius);
            else
                AddCapsuleCollider(bone, spec.colliderRadius);

            int dof = spec.spherical ? 3 : 1;
            _joints.Add(new Joint
            {
                body = ab,
                boneSuffix = spec.suffix,
                spherical = spec.spherical,
                dofStartIndex = dofCursor,
                dofCount = dof,
                oneWayHinge = spec.oneWayHinge,
                isHip = spec.isHip
            });
            dofCursor += dof;
            _allBodies.Add(ab);
        }

        TotalDof = dofCursor;

        IgnoreSelfCollisions();

        // Cache spawn pose for resets (local to the rig's parent so re-spawn is deterministic).
        _rootSpawnLocalPos = Root.transform.localPosition;
        _rootSpawnLocalRot = Root.transform.localRotation;

        Debug.Log($"[YBotLocomotionRig] Built AB rig: {_joints.Count} actuated joints, {TotalDof} DOF, {_allBodies.Count} bodies.");
        WarnIfRigidbodyAncestor();
        return true;
    }

    /// <summary>
    /// An ArticulationBody root must NOT live under a Rigidbody — Unity's articulation solver
    /// destabilizes (links separate / scatter) when it does. The designated player root carries a
    /// kinematic Rigidbody for RAG movement; in locomotion mode it must be removed/disabled.
    /// </summary>
    void WarnIfRigidbodyAncestor()
    {
        Transform t = Root.transform.parent;
        while (t != null)
        {
            Rigidbody rb = t.GetComponent<Rigidbody>();
            if (rb != null)
            {
                Debug.LogError($"[YBotLocomotionRig] ArticulationBody root '{Root.name}' is nested under Rigidbody '{rb.name}'. " +
                               "This destabilizes the articulation (limbs scatter). Remove/disable that Rigidbody for locomotion.");
                return;
            }
            t = t.parent;
        }
    }

    // Deferred one-shot diagnostics: dofCount/isRoot are only valid after the physics system has
    // processed the articulation (next FixedUpdate), so we log there rather than inside Build().
    bool _loggedDiagnostics;

    // Observed flexion range per one-way hinge, so HingeFlexionSign can be verified from a live run
    // instead of guessed. Reset every log window.
    readonly Dictionary<string, Vector2> _hingeRange = new Dictionary<string, Vector2>();
    float _nextHingeLogTime;

    void FixedUpdate()
    {
        TrackHingeRanges();

        if (_loggedDiagnostics || !IsBuilt) return;
        _loggedDiagnostics = true;

        var sb = new System.Text.StringBuilder("[YBotLocomotionRig] DIAGNOSTICS (post-init):\n");
        foreach (ArticulationBody ab in _allBodies)
        {
            ArticulationBody parentAb = null;
            Transform t = ab.transform.parent;
            while (t != null) { var p = t.GetComponent<ArticulationBody>(); if (p != null) { parentAb = p; break; } t = t.parent; }
            Collider col = ab.GetComponent<Collider>();
            Vector3 size = col != null ? col.bounds.size : Vector3.zero;
            sb.AppendLine($"  {ab.name}: isRoot={ab.isRoot} dof={ab.dofCount} joint={ab.jointType} mass={ab.mass:0.0} parentAB={(parentAb != null ? parentAb.name : "<NONE>")} colSize={size}");
        }
        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// Records the min/max angle each one-way hinge actually reaches and logs it every 10 s. This is
    /// how HingeFlexionSign gets verified rather than assumed:
    ///   • healthy  — the knee sweeps a real positive range, e.g. min=-4 max=62 (sign is CORRECT)
    ///   • WRONG    — the knee stays pinned in the 5° slack band, e.g. min=-4 max=1, and the bot walks
    ///                stiff-legged. Flip HingeFlexionSign to -1f.
    /// jointPosition is in RADIANS for articulation DOFs, so it is converted for readability.
    /// </summary>
    void TrackHingeRanges()
    {
        if (!IsBuilt) return;

        foreach (Joint j in _joints)
        {
            if (j.body == null || j.body.dofCount < 1) continue;
            // One-way hinges log their single DOF; hips log all three SEPARATELY, because the axis
            // that moves during a collapse is the one that needs a tighter limit — and which axis is
            // abduction cannot be read off the code.
            int dofs = j.oneWayHinge ? 1 : (j.isHip ? Mathf.Min(3, j.body.dofCount) : 0);
            for (int d = 0; d < dofs; d++)
            {
                float deg = j.body.jointPosition[d] * Mathf.Rad2Deg;
                if (float.IsNaN(deg) || float.IsInfinity(deg)) continue;

                string key = j.oneWayHinge ? j.boneSuffix : $"{j.boneSuffix}.{(char)('x' + d)}";
                if (_hingeRange.TryGetValue(key, out Vector2 r))
                    _hingeRange[key] = new Vector2(Mathf.Min(r.x, deg), Mathf.Max(r.y, deg));
                else
                    _hingeRange[key] = new Vector2(deg, deg);
            }
        }

        if (Time.time < _nextHingeLogTime || _hingeRange.Count == 0) return;
        _nextHingeLogTime = Time.time + 10f;

        var sb = new System.Text.StringBuilder(
            $"[YBotLocomotionRig] HINGE RANGE (deg, last 10s) — HingeFlexionSign={HingeFlexionSign:+0;-0} : ");
        foreach (var kv in _hingeRange)
            sb.Append($"{kv.Key}[min={kv.Value.x:F0} max={kv.Value.y:F0}] ");
        sb.Append("| knee pinned inside the ±5° slack band => sign is WRONG, flip HingeFlexionSign.");
        Debug.Log(sb.ToString());
        _hingeRange.Clear();
    }

    static ArticulationBody EnsureBody(Transform t)
    {
        ArticulationBody ab = t.GetComponent<ArticulationBody>();
        if (ab == null)
            ab = t.gameObject.AddComponent<ArticulationBody>();
        return ab;
    }

    void ConfigureJoint(ArticulationBody ab, BoneSpec spec)
    {
        float stiffness = Mathf.Min(spec.stiffness, MaxStiffness);
        float force = Mathf.Min(spec.forceLimit, MaxForceLimit);
        float damping = Mathf.Max(stiffness * MinDampingRatio, 1f);

        // Symmetric by default (spherical joints swing both ways). One-way hinges (knee/elbow) get
        // full flexion in the HingeFlexionSign direction and only a few degrees the other way, so
        // they cannot fold backwards and lose the ability to bear weight.
        float lower = -spec.swingLimit;
        float upper = spec.swingLimit;
        if (spec.oneWayHinge)
        {
            if (HingeFlexionSign >= 0f) { lower = -HingeHyperextendSlackDeg; upper = spec.swingLimit; }
            else                        { lower = -spec.swingLimit;          upper = HingeHyperextendSlackDeg; }
        }

        ArticulationDrive drive = new ArticulationDrive
        {
            stiffness = stiffness,
            damping = damping,
            forceLimit = force,
            target = 0f,
            lowerLimit = lower,
            upperLimit = upper
        };

        if (spec.spherical)
        {
            ab.jointType = ArticulationJointType.SphericalJoint;
            ab.twistLock = ArticulationDofLock.LimitedMotion;
            ab.swingYLock = ArticulationDofLock.LimitedMotion;
            ab.swingZLock = ArticulationDofLock.LimitedMotion;
            ab.xDrive = drive; // twist
            ab.yDrive = drive; // swing1
            ab.zDrive = drive; // swing2
        }
        else
        {
            ab.jointType = ArticulationJointType.RevoluteJoint;
            ab.twistLock = ArticulationDofLock.LimitedMotion;
            ab.xDrive = drive;
        }
    }

    // --- feet -----------------------------------------------------------------------------------
    // A biped stands on its SUPPORT POLYGON — the area enclosed by the soles. A capsule foot has no
    // such area: CapsuleCollider.height counts the two hemispherical caps, so with radius 0.06 and an
    // ankle->toe length of ~0.12-0.15 m the cylindrical section is ~0 and the collider degenerates
    // into a 6 cm SPHERE. Balancing on two spheres is close to impossible — they roll sideways
    // (tipped) and offer nothing to resist the hips descending (sank). Every reference biped rig
    // (ML-Agents Walker, MuJoCo humanoid) uses BOX feet for exactly this reason.
    //
    // Set false to fall back to the old capsule feet if the box turns out to be mis-oriented (check
    // the "FOOT BOX" build log and look at the collider in the Editor).
    public const bool UseBoxFeet = true;

    // Scope self-collision to non-adjacent pairs so the legs cannot scissor through each other. Set
    // false to restore the old "ignore every pair" behaviour if the articulation destabilizes.
    public const bool ScopedSelfCollision = true;
    // 0.10 -> 0.20 m. The zero-action test was decisive: with NO policy at all the rig never sinks
    // (sank=0) but tips 100% of the time, at exactly step 61 every run (min=max=61). A perfectly
    // repeatable passive topple means the spawn pose is statically unstable — the centre of mass
    // starts outside the support polygon — and the Mixamo bind pose has the feet almost touching, so
    // there is virtually no LATERAL support. Widening the sole widens that polygon without needing to
    // know which hip axis is abduction (which the code cannot determine). Effectively this stands the
    // bot with its feet apart. Reduce once it can balance actively.
    public const float FootWidth = 0.20f;   // m, medial-lateral
    public const float FootThickness = 0.05f; // m, sole thickness
    public const float FootHeelBehindAnkle = 0.07f; // m, heel extent behind the ankle joint

    /// <summary>
    /// Box (flat-soled) collider for a foot bone, giving the biped a real support polygon.
    /// Orientation is derived from the bind pose: "forward" is the bone-local axis pointing at the toe
    /// child, "down" is the bone-local axis closest to world down (the rig is authored standing, so
    /// world down is a reliable proxy for the sole direction). Both are snapped to the dominant
    /// cardinal axis because BoxCollider is axis-aligned in local space and Mixamo bones are
    /// axis-aligned in practice. The chosen axes are logged so they can be verified.
    /// </summary>
    static void AddFootBoxCollider(Transform bone, float radius)
    {
        if (bone.GetComponent<Collider>() != null)
            return;

        // Toe direction in bone-local space (fall back to local forward if there is no child).
        Vector3 localToToe = bone.childCount > 0
            ? bone.InverseTransformPoint(bone.GetChild(0).position)
            : Vector3.forward * 0.12f;
        int fwdAxis = AxisOfLargest(localToToe);
        float toeLen = Mathf.Max(Mathf.Abs(localToToe[fwdAxis]), 0.10f);
        float fwdSign = Mathf.Sign(localToToe[fwdAxis] == 0f ? 1f : localToToe[fwdAxis]);

        // Sole direction in bone-local space.
        Vector3 localDown = bone.InverseTransformDirection(Vector3.down);
        int downAxis = AxisOfLargest(localDown);
        if (downAxis == fwdAxis) // degenerate bind pose — pick any other axis
            downAxis = (fwdAxis + 1) % 3;
        float downSign = Mathf.Sign(localDown[downAxis] == 0f ? 1f : localDown[downAxis]);

        int sideAxis = 3 - fwdAxis - downAxis; // the remaining axis of {0,1,2}

        var box = bone.gameObject.AddComponent<BoxCollider>();
        Vector3 size = Vector3.zero;
        size[fwdAxis] = toeLen + FootHeelBehindAnkle; // heel..toe
        size[downAxis] = FootThickness;
        size[sideAxis] = FootWidth;
        box.size = size;

        // Centre it: shifted toward the toe by half the heel-to-toe midpoint, and DOWN to the sole so
        // the ankle sits above the foot rather than inside it.
        Vector3 centre = Vector3.zero;
        centre[fwdAxis] = fwdSign * (toeLen - FootHeelBehindAnkle) * 0.5f;
        centre[downAxis] = downSign * (radius - FootThickness * 0.5f);
        box.center = centre;

        Debug.Log($"[YBotLocomotionRig] FOOT BOX '{bone.name}': size={size} centre={centre} " +
                  $"(fwdAxis={fwdAxis} sign={fwdSign:+0;-0}, downAxis={downAxis} sign={downSign:+0;-0}, sideAxis={sideAxis}) " +
                  $"— verify in the Editor that the box lies FLAT under the ankle, heel to toe.");
    }

    static void AddCapsuleCollider(Transform bone, float radius)
    {
        if (bone.GetComponent<Collider>() != null)
            return;

        CapsuleCollider cap = bone.gameObject.AddComponent<CapsuleCollider>();
        cap.radius = radius;

        // Length from this bone to its first child bone; fall back to a small default.
        float length = 0.2f;
        if (bone.childCount > 0)
        {
            Transform child = bone.GetChild(0);
            length = Vector3.Distance(bone.position, child.position);
            // Orient along the local axis pointing most toward the child.
            Vector3 localDir = bone.InverseTransformPoint(child.position);
            cap.direction = AxisOfLargest(localDir);
            cap.center = localDir * 0.5f;
        }
        cap.height = Mathf.Max(length, radius * 2f);
    }

    static int AxisOfLargest(Vector3 v)
    {
        v = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        if (v.x >= v.y && v.x >= v.z) return 0;
        if (v.y >= v.x && v.y >= v.z) return 1;
        return 2;
    }

    /// <summary>
    /// Disable collisions between every pair of body colliders. AB only auto-ignores direct
    /// parent↔child links; non-adjacent capsules overlap and explode into a star-burst.
    /// Ground/target contact is unaffected (those colliders are not in this set).
    /// </summary>
    void IgnoreSelfCollisions()
    {
        var cols = new List<Collider>();
        var bodies = new List<ArticulationBody>();
        foreach (ArticulationBody ab in _allBodies)
        {
            Collider c = ab.GetComponent<Collider>();
            if (c != null) { cols.Add(c); bodies.Add(ab); }
        }

        // The old code ignored EVERY pair, which let the legs scissor straight through each other.
        // Crossed legs collapse the support polygon (both feet can end up on the same side of the
        // centre of mass), so the body has nothing underneath it — a direct cause of BOTH toppling and
        // hip-sinking. Real thighs collide; these did not.
        //
        // Adjacent (parent<->child) pairs MUST stay ignored: their capsules share the joint and
        // overlap by construction, and colliding them makes the articulation explode. Non-adjacent
        // pairs that ALREADY overlap in the bind pose (e.g. the two thigh capsules, which sit ~0.18 m
        // apart at radius 0.09 and so may just touch) would explode for the same reason, so those are
        // detected with ComputePenetration and left ignored too. Everything else — shin vs shin, foot
        // vs foot, shin vs opposite thigh — gets REAL collision, which is what blocks leg crossing.
        // One-flip revert to the old behaviour (ignore EVERY pair) if scoped collision destabilizes
        // the articulation. Symmetric with UseBoxFeet.
        if (!ScopedSelfCollision)
        {
            for (int i = 0; i < cols.Count; i++)
                for (int j = i + 1; j < cols.Count; j++)
                    Physics.IgnoreCollision(cols[i], cols[j], true);
            Debug.Log("[YBotLocomotionRig] SELF-COLLISION: all pairs ignored (ScopedSelfCollision=false).");
            return;
        }

        int enabled = 0, adjacent = 0, overlapping = 0;
        for (int i = 0; i < cols.Count; i++)
        {
            for (int j = i + 1; j < cols.Count; j++)
            {
                if (IsAdjacent(bodies[i], bodies[j]))
                {
                    Physics.IgnoreCollision(cols[i], cols[j], true);
                    adjacent++;
                }
                else if (OverlapsInBindPose(cols[i], cols[j]))
                {
                    Physics.IgnoreCollision(cols[i], cols[j], true);
                    overlapping++;
                }
                else
                {
                    Physics.IgnoreCollision(cols[i], cols[j], false);
                    enabled++;
                }
            }
        }

        Debug.Log($"[YBotLocomotionRig] SELF-COLLISION scoped: {enabled} pair(s) COLLIDE (blocks leg " +
                  $"crossing), {adjacent} ignored as parent/child, {overlapping} ignored as already " +
                  $"overlapping in the bind pose. If the rig explodes on spawn, raise the bind-pose " +
                  $"overlap margin or revert to ignoring all pairs.");
    }

    /// <summary>True if either body is the other's nearest ArticulationBody ancestor.</summary>
    static bool IsAdjacent(ArticulationBody a, ArticulationBody b)
        => NearestBodyAncestor(a) == b || NearestBodyAncestor(b) == a;

    static ArticulationBody NearestBodyAncestor(ArticulationBody ab)
    {
        Transform t = ab.transform.parent;
        while (t != null)
        {
            var p = t.GetComponent<ArticulationBody>();
            if (p != null) return p;
            t = t.parent;
        }
        return null;
    }

    /// <summary>
    /// True if two colliders already interpenetrate in the current (bind) pose. Such a pair cannot be
    /// allowed to collide — the solver would resolve the initial penetration explosively.
    /// </summary>
    static bool OverlapsInBindPose(Collider a, Collider b)
    {
        return Physics.ComputePenetration(
            a, a.transform.position, a.transform.rotation,
            b, b.transform.position, b.transform.rotation,
            out _, out _);
    }

    /// <summary>Set every actuated DOF drive target from a flat [-1,1] action vector.</summary>
    public void ApplyActions(System.Collections.Generic.IList<float> actions)
    {
        if (!IsBuilt) return;

        foreach (Joint j in _joints)
        {
            ArticulationBody ab = j.body;
            if (ab == null) continue;

            if (j.spherical)
            {
                SetDriveTarget(ab, 0, MapToLimit(ab.xDrive, Sample(actions, j.dofStartIndex + 0)));
                SetDriveTarget(ab, 1, MapToLimit(ab.yDrive, Sample(actions, j.dofStartIndex + 1)));
                SetDriveTarget(ab, 2, MapToLimit(ab.zDrive, Sample(actions, j.dofStartIndex + 2)));
            }
            else
            {
                SetDriveTarget(ab, 0, MapToLimit(ab.xDrive, Sample(actions, j.dofStartIndex + 0)));
            }
        }
    }

    static float Sample(System.Collections.Generic.IList<float> a, int i)
        => (a != null && i >= 0 && i < a.Count) ? Mathf.Clamp(a[i], -1f, 1f) : 0f;

    /// <summary>
    /// Maps a normalized action [-1,1] onto a drive's angular limits, PIVOTING ON 0° so that a
    /// neutral action always means "joint at rest" (leg straight, arm straight).
    ///
    /// The old form — Lerp(lower, upper, (norm+1)/2) — mapped 0 to the MIDPOINT of the range. That is
    /// identical to this for a symmetric range, but with the asymmetric knee range (-5°..+90°) it
    /// would make a neutral action command a permanent 42° crouch, which is worse than the bug being
    /// fixed. Pivoting on zero keeps neutral == straight for every joint.
    /// </summary>
    static float MapToLimit(ArticulationDrive d, float norm)
        => norm >= 0f ? Mathf.Lerp(0f, d.upperLimit, norm)
                      : Mathf.Lerp(0f, d.lowerLimit, -norm);

    static void SetDriveTarget(ArticulationBody ab, int axis, float target)
    {
        switch (axis)
        {
            case 0: { var d = ab.xDrive; d.target = target; ab.xDrive = d; break; }
            case 1: { var d = ab.yDrive; d.target = target; ab.yDrive = d; break; }
            default: { var d = ab.zDrive; d.target = target; ab.zDrive = d; break; }
        }
    }

    /// <summary>
    /// Full reset to spawn pose. Zeroes reduced-coordinate jointPosition/jointVelocity on every
    /// non-root body (memory gotcha #8 — a blown-up joint angle otherwise persists through resets),
    /// zeroes drive targets, and re-seats the root via TeleportRoot.
    /// </summary>
    public void ResetToSpawn()
    {
        if (!IsBuilt) return;

        // Re-seat root at its cached spawn pose (world space derived from current parent).
        Transform parent = Root.transform.parent;
        Vector3 worldPos = parent != null ? parent.TransformPoint(_rootSpawnLocalPos) : _rootSpawnLocalPos;
        Quaternion worldRot = parent != null ? parent.rotation * _rootSpawnLocalRot : _rootSpawnLocalRot;
        Root.TeleportRoot(worldPos, worldRot);
        Root.linearVelocity = Vector3.zero;
        Root.angularVelocity = Vector3.zero;

        foreach (Joint j in _joints)
        {
            ArticulationBody ab = j.body;
            if (ab == null) continue;

            // Zero reduced-coordinate state.
            int n = ab.dofCount;
            if (n > 0)
            {
                var pos = ab.jointPosition;
                var vel = ab.jointVelocity;
                for (int d = 0; d < n; d++) { pos[d] = 0f; vel[d] = 0f; }
                ab.jointPosition = pos;
                ab.jointVelocity = vel;
            }
            ab.linearVelocity = Vector3.zero;
            ab.angularVelocity = Vector3.zero;

            // Zero drive targets.
            SetDriveTarget(ab, 0, 0f);
            if (j.spherical) { SetDriveTarget(ab, 1, 0f); SetDriveTarget(ab, 2, 0f); }
        }
    }

    /// <summary>True when the root physics state is finite (guards observations — memory gotcha #6).</summary>
    public bool HasValidPhysicsState()
    {
        if (!IsBuilt) return false;
        Vector3 p = Root.transform.position;
        Vector3 v = Root.linearVelocity;
        return IsFinite(p) && IsFinite(v) && Mathf.Abs(p.y) < 1e4f;
    }

    static bool IsFinite(Vector3 v)
        => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)
          || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

    /// <summary>
    /// Bone lookup by suffix, tolerant of a "mixamorig:" (or "_"/" ") namespace prefix.
    /// Uses full distinct suffixes (e.g. "LeftUpLeg", "LeftLeg") so there is no substring overlap.
    /// </summary>
    public static Transform FindBone(Transform root, string suffix)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            if (n == suffix
                || n.EndsWith(":" + suffix)
                || n.EndsWith("_" + suffix)
                || n.EndsWith(" " + suffix))
                return t;
        }
        return null;
    }
}
