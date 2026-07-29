using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// ML-Agents PPO locomotion agent for the designated Y-Bot. Drives the ArticulationBody rig
/// (<see cref="YBotLocomotionRig"/>) toward a target while staying upright. Built and wired at
/// runtime by <see cref="YBotLocomotionInstaller"/>; registers as behavior <c>PhysicalAgentZone0</c>
/// (one policy / one ONNX — see Achitecture.md).
///
/// Observation layout (size = 2*TotalDof + 16, set on BehaviorParameters by the installer):
///   per DOF: jointPosition, jointVelocity        (2 * TotalDof)
///   root up vector (local)                        3
///   root linear velocity (local)                  3
///   root angular velocity (local)                 3
///   height of hips above ground                   1
///   direction to target (local, xz-normalized)    3
///   forward alignment to target                   1
///   foot grounded flags (L, R)                    2
/// Actions: TotalDof continuous, each in [-1,1] mapped to that DOF's joint limit.
/// </summary>
[RequireComponent(typeof(YBotLocomotionRig))]
public class YBotWalkerAgent : Agent
{
    [Header("Target")]
    public Transform target;
    public float targetSpawnRadius = 6f;

    [Header("Episode")]
    [Tooltip("Zone index used to clamp the body inside the zone play area (designated player = 0).")]
    public int zoneIndex = 0;
    [Tooltip("Hips height above ground (m) below which the agent is considered fallen.")]
    public float fallHeight = 0.6f;
    [Tooltip("dot(root.up, worldUp) below which the body counts as tipped over and the episode resets. " +
             "Without this a backward-tipped body keeps its hips above fallHeight and gets stuck, never resetting.")]
    public float minUprightToSurvive = 0.4f;
    [Tooltip("Hips height above ground (m) at a healthy standing pose, used to normalize height obs.")]
    public float standHeight = 1.0f;
    public float reachTargetDistance = 1.0f;

    [Header("Curriculum")]
    [Tooltip("Week 1: stand/balance only — no walk-to-target reward; target stays at spawn. " +
             "Week 2 (walking): set false so progress-to-target + reach rewards activate and the " +
             "target spawns away from the agent. Agent is runtime-added, so this code default is " +
             "the source of truth — inspector edits don't persist.")]
    public bool stabilityOnlyTraining = false;

    [Tooltip("Gentle walk-in curriculum for a stand-warm-started policy: start the target CLOSE and in " +
             "a forward cone, then widen distance + angle as episodes accumulate. Cushions the obs- " +
             "normalizer shock (direction-to-target was pinned to 0 for the whole stand phase, so its " +
             "running variance is ~0 and the first non-zero value would otherwise slam to the ±5 clamp) " +
             "and asks for a small forward step first instead of an instant 180° turn + long march. " +
             "TARGET PLACEMENT ONLY — never touches the 168/21 obs/action spec, so it is fully " +
             "--resume-safe. Set false to always use the full targetSpawnRadius / 360° placement.")]
    public bool useWalkTargetCurriculum = true;
    [Tooltip("Walk curriculum: target distance (m) at episode 0. Ramps up to targetSpawnRadius.")]
    public float walkCurriculumStartRadius = 1.5f;
    [Tooltip("Walk curriculum: half-angle (deg) of the forward cone the target spawns within at " +
             "episode 0. Opens to 180 (full circle) as the curriculum completes.")]
    public float walkCurriculumStartConeDeg = 30f;
    [Tooltip("Walk curriculum: completed episodes over which distance ramps to targetSpawnRadius and " +
             "the cone opens to 360°. Set 0 to disable ramping (always full radius/angle). Note: this " +
             "counts episodes in the CURRENT Play session, so each --resume re-ramps gently from close " +
             "— which is exactly the safe behaviour we want after any restart.")]
    public int walkCurriculumEpisodesToFull = 400;

    [Header("Reward weights")]
    // Week 2 (walking) mix: progress-to-target is dominant, but upright/height/alive are kept so
    // the warm-started balance from the stand checkpoint isn't forgotten while learning to walk.
    public float uprightWeight = 0.15f;
    public float heightWeight = 0.05f;
    public float footGroundedWeight = 0.02f;
    [Tooltip("Penalty per (m/s) of horizontal speed of a foot WHILE it is grounded — stops a planted " +
             "foot from skating/moonwalking so the stance looks natural. No hard gait rhythm is forced.")]
    public float footSlipWeight = 0.02f;
    public float progressWeight = 1.0f;
    [Tooltip("Layer 1 (body direction): per-step reward for the body's forward axis pointing at the " +
             "target. Positive when facing the target, negative when facing away — so the agent learns " +
             "to TURN and walk forward toward the goal (realistic) instead of strafing/back-pedalling. " +
             "Kept small so it nudges heading without overwhelming progress or letting the agent farm " +
             "reward by standing still and just facing the target.")]
    public float headingWeight = 0.05f;
    [Tooltip("Dense per-step reward for the root's velocity TOWARD the target, normalized to " +
             "desiredWalkSpeed. This is what makes a stand-expert actually start walking: standing " +
             "still earns 0 here, so idling stops being optimal.")]
    public float velocityWeight = 0.4f; // raised 0.2→0.4 (Phase A): moving toward the target now pays more than idling
    [Tooltip("Walk speed (m/s) at which the velocity-toward-target reward saturates — prevents lunging/sprinting.")]
    public float desiredWalkSpeed = 1.5f;
    [Tooltip("Small per-step time cost (subtracted) so standing idle is never free — discourages the " +
             "agent from balancing in place instead of walking to the target. Replaces the old alive bonus. " +
             "Walk mode only (not applied during stabilityOnlyTraining).")]
    public float existentialPenalty = 0.02f; // raised 0.006→0.02 (Phase A): standing idle bleeds reward so walking becomes optimal
    public float energyPenalty = 0.0002f;
    public float fallPenalty = 1.0f;
    public float reachReward = 2.0f;

    [Header("Obstacle avoidance (avoid phase — off during pure walk)")]
    [Tooltip("Master switch for the whole obstacle-avoidance layer: the proximity/hit penalty AND the " +
             "per-episode placeholder obstacle field. Curriculum: keep FALSE for the Walk phase so the " +
             "agent learns pure walk-to-target first, then set TRUE and --resume for the Avoid phase. " +
             "Reward + scene only — never changes the frozen 168/21 obs/action spec, so it is resume-safe. " +
             "The ray sensor (perception) is always built regardless; this only controls the INCENTIVE.")]
    public bool enableObstacleAvoidance = false;
    [Tooltip("Weight of the proximity penalty applied as the body nears a non-target obstacle " +
             "(Wall/Station/Prop/Human). The RayPerceptionSensor gives the policy the PERCEPTION; " +
             "this penalty gives it the INCENTIVE to route around. Kept below progressWeight so " +
             "reaching the target stays dominant.")]
    public float obstacleAvoidWeight = 0.15f;
    [Tooltip("Body-to-obstacle distance (m) at/under which the proximity penalty is at full strength; " +
             "it ramps to zero at this radius.")]
    public float obstacleContactRadius = 0.9f;
    [Tooltip("Body-to-obstacle distance (m) treated as a hard collision — applies fallPenalty-style " +
             "obstacleHitPenalty and optionally ends the episode.")]
    public float obstacleHitDistance = 0.35f;
    public float obstacleHitPenalty = 1.0f;
    [Tooltip("End the episode on a hard obstacle collision (like a fall). Off by default so the agent " +
             "learns to recover/steer away rather than being reset on every graze.")]
    public bool endEpisodeOnObstacleHit = false;
    [Tooltip("Curriculum (avoid phase): bias target placement so an obstacle sits on the straight " +
             "line from the agent to the target, forcing it to actually route around. Enable once " +
             "stand+walk are solid.")]
    public bool biasTargetBehindObstacles = false;

    [Header("Per-category avoidance (reward-only, resume-safe)")]
    [Tooltip("Multiplier on the proximity + hit penalty per obstacle category. Living things (Human, " +
             "Agent) cost more to bump into than static scenery, so the policy keeps a wider berth " +
             "around people. Pure reward shaping — changing these never alters the obs/action spec, " +
             "so it is safe on any --resume.")]
    public float wallPenaltyScale = 1.0f;
    public float stationPenaltyScale = 1.0f;
    public float furniturePenaltyScale = 1.0f;
    public float propPenaltyScale = 1.0f;
    public float agentPenaltyScale = 1.5f;
    public float humanPenaltyScale = 2.0f;

    [Header("Training placeholders (scene-only, resume-safe)")]
    [Tooltip("Spawn a per-episode field of placeholder obstacles (boxes/cylinders on the Wall layer, " +
             "tagged Furniture/Prop/Wall) so the ray sensor + avoidance reward have things to learn " +
             "against BEFORE the real office scene exists. Turn OFF once training runs in the real " +
             "populated scene. Scene-only — never affects the frozen obs/action spec.")]
    public bool spawnTrainingPlaceholders = true;
    [Tooltip("Number of placeholder obstacles spawned per episode (randomized position/size/type).")]
    public int placeholderObstacleCount = 4;

    [Header("Action smoothness")]
    [Tooltip("Penalty on squared action delta between steps (jerk). Reduces the twitchy PPO ragdoll " +
             "buzz for a more natural gait. Reward-only; safe on any resume.")]
    public float actionSmoothWeight = 0.005f;

    YBotLocomotionRig _rig;
    YBotFootContact _leftFoot;
    YBotFootContact _rightFoot;
    float _prevTargetDistance;
    bool _ownsTarget;
    GameObject _targetMarker; // the desk box under the target; hidden during stability training
    float[] _actionBuffer;
    float[] _prevActionBuffer;   // for the action-smoothness (jerk) penalty
    int _actionsReceived;        // >0 confirms the Python trainer is sending actions
    float _nextTrainingLogTime;  // throttle for the clean current-training log
    int _obstacleLayerMask;      // 0 = uninitialized, -1 = no Wall layer, else (1 << wallLayer)
    static readonly Collider[] _obstacleOverlap = new Collider[16];
    YBotTrainingObstacleField _placeholders; // per-episode placeholder obstacles (training only)

    // --- Reward-term instrumentation (diagnostics only) ---------------------------------------
    // Each AddReward is mirrored into a per-window bucket so the TRAINING log can show WHERE the
    // reward is coming from — posture (upright/height/heading) vs actual locomotion (progress/
    // velocity). These buckets never affect learning; AddReward still does all the work. Reset
    // each time the log prints (every 10 s).
    double _accUpright, _accHeight, _accFoot, _accProgress, _accHeading, _accVelocity;
    double _accExistential, _accObstacle, _accSlip, _accEnergy, _accSmooth, _accFallReach;
    int _accSteps;               // reward-steps counted since the last log print
    float _lastDist, _lastVelToward, _lastFacing, _lastProgress; // latest instantaneous values

    // Mirror an AddReward into a diagnostics bucket (learning unchanged).
    void AddR(ref double bucket, float r) { AddReward(r); bucket += r; }

    public static int ObservationSize(int totalDof) => 2 * totalDof + 16;

    public void Wire(YBotLocomotionRig rig, YBotFootContact left, YBotFootContact right)
    {
        _rig = rig;
        _leftFoot = left;
        _rightFoot = right;
    }

    public override void Initialize()
    {
        if (_rig == null) _rig = GetComponent<YBotLocomotionRig>();

        // One-time policy/connection diagnostic — if actionsReceived stays 0 this tells us why.
        var bp = GetComponent<Unity.MLAgents.Policies.BehaviorParameters>();
        bool comms = Academy.IsInitialized && Academy.Instance.IsCommunicatorOn;
        Debug.Log($"[YBotWalker] Initialize — behavior='{(bp != null ? bp.BehaviorName : "?")}', " +
                  $"behaviorType={(bp != null ? bp.BehaviorType.ToString() : "?")} (must be Default for trainer), " +
                  $"obs={(bp != null ? bp.BrainParameters.VectorObservationSize : -1)}, " +
                  $"actions={(bp != null ? bp.BrainParameters.ActionSpec.NumContinuousActions : -1)}, " +
                  $"model={(bp != null && bp.Model != null ? bp.Model.name : "none")}, communicatorOn={comms}");
    }

    public override void OnEpisodeBegin()
    {
        if (_rig == null || !_rig.IsBuilt) return;

        _rig.ResetToSpawn();
        EnsureTarget();
        RandomizeTarget();

        // Per-episode placeholder obstacle field (training only; off in the real populated scene).
        // Spawned AFTER the target is placed so it can avoid dropping an obstacle on the goal, and
        // only in the Avoid phase (enableObstacleAvoidance) — the pure Walk phase and stability mode
        // have nothing to avoid, so obstacles would just be noise.
        if (spawnTrainingPlaceholders && !stabilityOnlyTraining && enableObstacleAvoidance)
        {
            if (_placeholders == null)
                _placeholders = YBotTrainingObstacleField.GetOrCreate(zoneIndex);
            _placeholders.Randomize(_rig.Root.transform.position, target, zoneIndex,
                                    placeholderObstacleCount, reachTargetDistance);
        }
        else if (_placeholders != null)
        {
            _placeholders.Clear();
        }

        _prevTargetDistance = PlanarDistanceToTarget();
        _prevActionBuffer = null; // avoid a spurious jerk spike across the episode boundary
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        int expected = 0;
        var bp = GetComponent<BehaviorParameters>();
        if (bp != null)
            expected = bp.BrainParameters.VectorObservationSize;

        int size = _rig != null ? ObservationSize(_rig.TotalDof) : 0;

        // Non-finite physics → emit zeros so ML-Agents keeps returning actions (memory gotcha #6).
        if (_rig == null || !_rig.IsBuilt || !_rig.HasValidPhysicsState())
        {
            for (int i = 0; i < size; i++) sensor.AddObservation(0f);
            if (expected > 0 && expected != size)
                Debug.LogError($"[YBotWalker] Observation mismatch (invalid physics): BehaviorParameters expects {expected}, rig emits {size}.");
            return;
        }

        // Per-DOF joint state.
        foreach (var j in _rig.Joints)
        {
            var ab = j.body;
            int n = ab != null ? ab.dofCount : 0;
            for (int d = 0; d < j.dofCount; d++)
            {
                float pos = (ab != null && d < n) ? ab.jointPosition[d] : 0f;
                float vel = (ab != null && d < n) ? ab.jointVelocity[d] : 0f;
                sensor.AddObservation(Mathf.Clamp(pos, -10f, 10f));
                sensor.AddObservation(Mathf.Clamp(vel, -20f, 20f));
            }
        }

        Transform root = _rig.Root.transform;
        sensor.AddObservation(root.InverseTransformDirection(Vector3.up));               // 3
        sensor.AddObservation(root.InverseTransformDirection(_rig.Root.linearVelocity)); // 3
        sensor.AddObservation(root.InverseTransformDirection(_rig.Root.angularVelocity));// 3

        float groundY = SampleGroundY(root.position);
        sensor.AddObservation(Mathf.Clamp((root.position.y - groundY) / standHeight, -2f, 2f)); // 1

        Vector3 toTarget = PlanarToTarget();
        Vector3 localToTarget = root.InverseTransformDirection(toTarget.normalized);
        sensor.AddObservation(localToTarget); // 3
        sensor.AddObservation(Vector3.Dot(new Vector3(root.forward.x, 0f, root.forward.z).normalized, toTarget.normalized)); // 1

        sensor.AddObservation(_leftFoot != null && _leftFoot.IsGrounded ? 1f : 0f);  // 1
        sensor.AddObservation(_rightFoot != null && _rightFoot.IsGrounded ? 1f : 0f);// 1

        if (expected > 0 && expected != size)
            Debug.LogError($"[YBotWalker] Observation mismatch: BehaviorParameters expects {expected}, CollectObservations emits {size}. Re-enter Play so the Academy re-handshakes.");
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (_rig == null || !_rig.IsBuilt) return;

        _actionsReceived++;

        var cont = actions.ContinuousActions;
        if (_actionBuffer == null || _actionBuffer.Length != _rig.TotalDof)
            _actionBuffer = new float[_rig.TotalDof];
        for (int i = 0; i < _actionBuffer.Length; i++)
            _actionBuffer[i] = i < cont.Length ? cont[i] : 0f;
        _rig.ApplyActions(_actionBuffer);

        // Action-smoothness (jerk) penalty: squared change from the previous step's actions. Applied
        // in every mode (natural posture in stand, natural gait in walk). Reward-only.
        if (_prevActionBuffer != null && _prevActionBuffer.Length == _actionBuffer.Length && actionSmoothWeight > 0f)
        {
            float jerk = 0f;
            for (int i = 0; i < _actionBuffer.Length; i++)
            {
                float d = _actionBuffer[i] - _prevActionBuffer[i];
                jerk += d * d;
            }
            AddR(ref _accSmooth, -actionSmoothWeight * jerk);
        }
        if (_prevActionBuffer == null || _prevActionBuffer.Length != _actionBuffer.Length)
            _prevActionBuffer = new float[_actionBuffer.Length];
        System.Array.Copy(_actionBuffer, _prevActionBuffer, _actionBuffer.Length);

        // Fall / non-finite handling lives in FixedUpdate so it also runs with no trainer attached.
        if (!_rig.HasValidPhysicsState())
            return;

        Transform root = _rig.Root.transform;
        float groundY = SampleGroundY(root.position);
        float height = root.position.y - groundY;
        if (height < fallHeight)
            return;

        _accSteps++; // count this as a reward-applying step for the per-step breakdown

        // Upright posture: dot(local up, world up).
        float upright = Vector3.Dot(root.up, Vector3.up);
        AddR(ref _accUpright, uprightWeight * Mathf.Clamp01(upright));

        float normHeight = Mathf.Clamp((height - fallHeight) / (standHeight - fallHeight), 0f, 1f);
        AddR(ref _accHeight, heightWeight * normHeight);

        // Foot-contact shaping for a REALISTIC walking gait. Walking has a single-support phase
        // (one foot planted while the other swings forward), so we must NOT require both feet down
        // — that would punish every step. Instead reward "at least one foot grounded" (lets the
        // agent balance on one foot mid-stride) and only penalize being fully airborne (hopping/
        // flying). Stepping then emerges naturally from the dominant progress reward + upright.
        bool leftGrounded = _leftFoot != null && _leftFoot.IsGrounded;
        bool rightGrounded = _rightFoot != null && _rightFoot.IsGrounded;
        if (leftGrounded || rightGrounded)
            AddR(ref _accFoot, footGroundedWeight);
        else
            AddR(ref _accFoot, -footGroundedWeight); // both feet off the ground = hop/jump, not a walk

        float dist = PlanarDistanceToTarget();
        _lastDist = dist;
        if (!stabilityOnlyTraining && progressWeight > 0f)
        {
            float progress = _prevTargetDistance - dist;
            _prevTargetDistance = dist;
            _lastProgress = progress;
            AddR(ref _accProgress, progressWeight * progress);
        }
        else
        {
            _prevTargetDistance = dist;
        }

        // Layer 1 — body direction. Reward the body's forward axis pointing at the target so the
        // agent turns to walk FORWARD toward it (realistic) instead of strafing or back-pedalling.
        // This is a reward only: the agent rotates SMOOTHLY using its own joint drives — we never
        // snap the transform. Skipped once it's basically on top of the target (heading is undefined
        // there) and during stability mode. Uses the SAME forward axis as the heading observation
        // (CollectObservations), so if facing ever looks off, both adjust together.
        if (!stabilityOnlyTraining && dist > reachTargetDistance)
        {
            Vector3 toTarget = PlanarToTarget().normalized;
            Vector3 fwd = new Vector3(root.forward.x, 0f, root.forward.z).normalized;
            float facing = Vector3.Dot(fwd, toTarget); // 1 = facing target, -1 = facing away
            _lastFacing = facing;
            AddR(ref _accHeading, headingWeight * facing);

            // Dense velocity-toward-target reward: pays every step the agent actually MOVES toward
            // the goal, saturating at desiredWalkSpeed. Standing still scores 0 here, so a stand-expert
            // can no longer farm posture rewards by idling — moving toward the target is what pays.
            Vector3 planarVel = _rig.Root.linearVelocity; planarVel.y = 0f;
            float velToward = Vector3.Dot(planarVel, toTarget);
            _lastVelToward = velToward;
            AddR(ref _accVelocity, velocityWeight * Mathf.Clamp01(velToward / desiredWalkSpeed));
        }

        // Foot-slip penalty: a grounded foot should plant, not skate (moonwalk). Penalize the
        // horizontal speed of each foot while it's in contact — clamped so a transient spike can't
        // dominate. Keeps the stance natural without forcing any stepping rhythm.
        float slip = 0f;
        if (leftGrounded && _leftFoot != null) slip += Mathf.Min(_leftFoot.HorizontalSpeed, 3f);
        if (rightGrounded && _rightFoot != null) slip += Mathf.Min(_rightFoot.HorizontalSpeed, 3f);
        if (slip > 0f) AddR(ref _accSlip, -footSlipWeight * slip);

        // Obstacle avoidance (Avoid phase only): penalize nearing / colliding with any non-target
        // physical object. Perception comes from the RayPerceptionSensor; this is the incentive.
        // Gated behind enableObstacleAvoidance so the Walk phase stays pure walk-to-target.
        if (!stabilityOnlyTraining && enableObstacleAvoidance)
            ApplyObstacleAvoidanceReward(root.position);

        // Existential cost (walk mode only): small per-step time penalty so idling is never free.
        // During stability training, standing IS the goal, so no time cost is applied there.
        if (!stabilityOnlyTraining)
            AddR(ref _accExistential, -existentialPenalty);

        float effort = 0f;
        for (int i = 0; i < cont.Length; i++) effort += cont[i] * cont[i];
        AddR(ref _accEnergy, -energyPenalty * effort);

        if (!stabilityOnlyTraining && dist < reachTargetDistance)
        {
            AddR(ref _accFallReach, reachReward);
            EndEpisode();
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var cont = actionsOut.ContinuousActions;
        for (int i = 0; i < cont.Length; i++) cont[i] = 0f;
    }

    /// <summary>
    /// Runs every physics step regardless of whether a trainer is attached, so the body is kept
    /// inside zone 0 and reset when it falls — even during a no-trainer Play (where OnActionReceived
    /// never fires). Without this the ragdoll drifts off the zone onto a neighbouring ground.
    /// </summary>
    void FixedUpdate()
    {
        if (_rig == null || !_rig.IsBuilt) return;

        ArticulationBody root = _rig.Root;

        // Clean CURRENT-training status (replaces the legacy BSG summary spam). Shows whether the
        // trainer is actually driving the agent and how training is progressing.
        if (Time.time >= _nextTrainingLogTime)
        {
            _nextTrainingLogTime = Time.time + 10f;
            float gY = SampleGroundY(root.transform.position);
            float up = Vector3.Dot(root.transform.up, Vector3.up);
            int s = Mathf.Max(1, _accSteps); // avoid /0 on the first window
            System.Func<double, float> perStep = acc => (float)(acc / s);
            Debug.Log($"[YBotWalker] TRAINING — completedEpisodes={CompletedEpisodes}, " +
                      $"actionsReceived={_actionsReceived} ({(_actionsReceived > 0 ? "trainer SENDING actions" : "NO actions — trainer not connected")}), " +
                      $"stepThisEpisode={StepCount}, episodeReward={GetCumulativeReward():F2}, " +
                      $"upright={up:F2}, hipHeight={(root.transform.position.y - gY):F2}\n" +
                      $"    reward/step (last {_accSteps} steps) — POSTURE: upright={perStep(_accUpright):F3} " +
                      $"height={perStep(_accHeight):F3} foot={perStep(_accFoot):F3} heading={perStep(_accHeading):F3} | " +
                      $"LOCOMOTION: progress={perStep(_accProgress):F3} velocity={perStep(_accVelocity):F3} | " +
                      $"COSTS: obstacle={perStep(_accObstacle):F3} slip={perStep(_accSlip):F3} " +
                      $"existential={perStep(_accExistential):F3} energy={perStep(_accEnergy):F3} smooth={perStep(_accSmooth):F3} " +
                      $"fall/reach={perStep(_accFallReach):F3}\n" +
                      $"    locomotion state — distToTarget={_lastDist:F2}m velToward={_lastVelToward:F2}m/s " +
                      $"facing={_lastFacing:F2} progressΔ/step={_lastProgress:F4}m");
            // Reset the diagnostics window.
            _accUpright = _accHeight = _accFoot = _accProgress = _accHeading = _accVelocity = 0;
            _accExistential = _accObstacle = _accSlip = _accEnergy = _accSmooth = _accFallReach = 0;
            _accSteps = 0;
        }

        // Non-finite → reset.
        if (!_rig.HasValidPhysicsState())
        {
            AddR(ref _accFallReach, -fallPenalty);
            EndEpisode();
            return;
        }

        // Clamp the root inside the zone play area so a fallen/flailing body can't slide off-zone.
        Vector3 pos = root.transform.position;
        Vector3 clamped = ZonePlayAreaBounds.ClampPosition(zoneIndex, pos);
        if ((new Vector2(clamped.x - pos.x, clamped.z - pos.z)).sqrMagnitude > 1e-4f)
        {
            root.TeleportRoot(new Vector3(clamped.x, pos.y, clamped.z), root.transform.rotation);
            root.linearVelocity = Vector3.zero;
            pos = root.transform.position;
        }

        // Fall → terminate + reset (returns the body to its in-zone spawn via OnEpisodeBegin).
        // Two fall conditions: hips dropped too low, OR the body tipped over. The tilt check is
        // essential — a rigid body that tips backward keeps its hips above fallHeight and would
        // otherwise stay stuck forever, so the episode never resets and nothing can be learned.
        float groundY = SampleGroundY(pos);
        float upright = Vector3.Dot(root.transform.up, Vector3.up);
        if (pos.y - groundY < fallHeight || upright < minUprightToSurvive)
        {
            AddR(ref _accFallReach, -fallPenalty);
            EndEpisode();
        }
    }

    // --- helpers ---

    Vector3 PlanarToTarget()
    {
        if (target == null || _rig == null) return Vector3.forward;
        Vector3 d = target.position - _rig.Root.transform.position;
        d.y = 0f;
        return d;
    }

    float PlanarDistanceToTarget() => PlanarToTarget().magnitude;

    void EnsureTarget()
    {
        if (target != null) return; 
        var go = new GameObject("YBotWalkerTarget");
        target = go.transform;
        _ownsTarget = true;

        // The goal is a DESK-scale box (office desk / workstation), not an abstract point — so the
        // ray sensor perceives the destination as a real "Furniture" object the way it will in the
        // real scene. It sits on the ground at target.position with a non-trigger collider on the
        // Wall layer. It is NEVER penalized: IsCurrentTarget excludes it (it is a child of target),
        // and SampleGroundY skips it, so the desk box can't corrupt ground sampling. Reach is decided
        // on planar (xz) distance, so the agent arrives in front of the desk rather than climbing it.
        var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = "YBotWalkerTargetDesk";
        marker.transform.SetParent(target, false);
        // Desk dimensions: 1.35 m (W) x 0.75 m (H) x 0.70 m (D); base resting on the ground point.
        marker.transform.localScale = new Vector3(1.35f, 0.75f, 0.70f);
        marker.transform.localPosition = new Vector3(0f, 0.375f, 0f);
        ScenePhysicsLayers.ApplyEnvironmentLayer(marker);            // Wall layer → seen by the rays
        ScenePhysicsLayers.SafeSetTag(marker, ScenePhysicsLayers.TagFurniture);
        var rend = marker.GetComponent<Renderer>();
        if (rend != null) rend.material.color = new Color(0.2f, 0.8f, 1f); // cyan desk = the goal
        _targetMarker = marker;
    }

    void RandomizeTarget()
    {
        if (target == null || _rig == null) return;

        if (stabilityOnlyTraining)
        {
            // Stand phase: there is no destination — the agent just balances in place. HIDE the desk
            // box (disable its GameObject → no collider, no render). If it stayed visible it would sit
            // ON the agent (target == agent position) and its solid Wall-layer collider would collide
            // with the agent's legs and shove it around, corrupting standing training.
            if (_targetMarker != null && _targetMarker.activeSelf) _targetMarker.SetActive(false);
            Vector3 standPos = _rig.Root.transform.position;
            standPos.y = SampleGroundY(standPos);
            target.position = standPos;
            return;
        }

        // Walk/avoid phase: the desk is a real destination away from the agent — show it.
        if (_targetMarker != null && !_targetMarker.activeSelf) _targetMarker.SetActive(true);

        Vector3 origin = _rig.Root.transform.position;

        // Gentle walk-in curriculum (see useWalkTargetCurriculum): ramp target distance from close→full
        // and the spawn cone from a narrow forward wedge→full circle over the first episodes of the
        // session. Keeps the direction-to-target obs small/consistent so the normalizer re-learns
        // variance smoothly after the all-zero stand phase, and asks for a small forward step first.
        // The Avoid phase (biasTargetBehindObstacles) needs any-direction placement, so it uses full.
        float tCurr = (useWalkTargetCurriculum && !biasTargetBehindObstacles && walkCurriculumEpisodesToFull > 0)
            ? Mathf.Clamp01((float)CompletedEpisodes / walkCurriculumEpisodesToFull)
            : 1f;
        float radiusMax = Mathf.Lerp(walkCurriculumStartRadius, targetSpawnRadius, tCurr);
        float coneDeg = (useWalkTargetCurriculum && !biasTargetBehindObstacles)
            ? Mathf.Lerp(walkCurriculumStartConeDeg, 180f, tCurr)
            : 180f;

        // Forward heading in the XZ plane (fallback to world forward if degenerate). Cone is centred here.
        Vector3 fwd = new Vector3(_rig.Root.transform.forward.x, 0f, _rig.Root.transform.forward.z);
        fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
        float baseAngle = Mathf.Atan2(fwd.x, fwd.z); // so dir = (sin, cos) points along forward at offset 0

        int attempts = biasTargetBehindObstacles ? 8 : 1;
        Vector3 chosen = origin;
        for (int a = 0; a < attempts; a++)
        {
            float coneRad = coneDeg * Mathf.Deg2Rad;
            float ang = baseAngle + Random.Range(-coneRad, coneRad);       // forward cone (full circle when coneDeg=180)
            float rMin = Mathf.Max(reachTargetDistance + 0.75f, radiusMax * 0.5f); // always beyond reach so it isn't instantly "done"
            float r = Mathf.Lerp(rMin, Mathf.Max(rMin + 0.25f, radiusMax), Random.value);
            Vector3 pos = origin + new Vector3(Mathf.Sin(ang) * r, 0f, Mathf.Cos(ang) * r);
            pos = ZonePlayAreaBounds.ClampPosition(zoneIndex, pos); // keep the goal inside the zone
            pos.y = SampleGroundY(pos);
            chosen = pos;

            // Curriculum: prefer a target with an obstacle between the agent and it, so the agent
            // must route around instead of walking straight. Accept the first blocked candidate;
            // otherwise the last candidate stands (unbiased fallback).
            if (!biasTargetBehindObstacles)
                break;
            int mask = ObstacleMask();
            if (mask != 0)
            {
                Vector3 a0 = origin + Vector3.up * 0.9f;
                Vector3 b0 = new Vector3(pos.x, a0.y, pos.z);
                if (Physics.Linecast(a0, b0, mask, QueryTriggerInteraction.Ignore))
                    break;
            }
        }
        target.position = chosen;
    }

    /// <summary>
    /// Proximity/collision penalty against non-target physical objects on the Wall layer. Uses a
    /// cheap OverlapSphere around the hips — no per-bone collision components needed — and applies
    /// the "identity (tag) vs role (runtime reference)" rule: the agent's CURRENT target object is
    /// never penalized, everything else (Wall/Station/Prop/Human) is.
    /// </summary>
    void ApplyObstacleAvoidanceReward(Vector3 bodyPos)
    {
        int mask = ObstacleMask();
        if (mask == 0) return;

        int n = Physics.OverlapSphereNonAlloc(bodyPos, obstacleContactRadius, _obstacleOverlap, mask, QueryTriggerInteraction.Ignore);
        float worstProximityPenalty = 0f; // proximity * category scale, worst over all obstacles
        float worstHitScale = 0f;         // category scale of the closest hard-hit obstacle (0 = none)
        for (int i = 0; i < n; i++)
        {
            Collider c = _obstacleOverlap[i];
            if (c == null) continue;
            if (c.GetComponentInParent<ArticulationBody>() != null) continue; // skip the agent's own rig
            if (IsCurrentTarget(c.transform)) continue;                       // never punish the goal

            Vector3 closest = c.ClosestPoint(bodyPos);
            Vector3 planar = new Vector3(bodyPos.x - closest.x, 0f, bodyPos.z - closest.z);
            float d = planar.magnitude;
            float prox = Mathf.Clamp01(1f - d / Mathf.Max(0.01f, obstacleContactRadius));
            float scale = CategoryPenaltyScale(c.tag); // living things cost more than scenery
            float scaledProx = prox * scale;
            if (scaledProx > worstProximityPenalty) worstProximityPenalty = scaledProx;
            if (d < obstacleHitDistance && scale > worstHitScale) worstHitScale = scale;
        }

        if (worstProximityPenalty > 0f)
            AddR(ref _accObstacle, -obstacleAvoidWeight * worstProximityPenalty);
        if (worstHitScale > 0f)
        {
            AddR(ref _accObstacle, -obstacleHitPenalty * worstHitScale);
            if (endEpisodeOnObstacleHit)
                EndEpisode();
        }
    }

    /// <summary>
    /// Per-category penalty multiplier for a hit collider's tag. Reward shaping only — living things
    /// (Human, Agent) are costlier to bump than static scenery. Never changes the obs/action spec.
    /// </summary>
    float CategoryPenaltyScale(string tag)
    {
        switch (tag)
        {
            case ScenePhysicsLayers.TagHuman:     return humanPenaltyScale;
            case ScenePhysicsLayers.TagAgent:     return agentPenaltyScale;
            case ScenePhysicsLayers.TagStation:   return stationPenaltyScale;
            case ScenePhysicsLayers.TagFurniture: return furniturePenaltyScale;
            case ScenePhysicsLayers.TagProp:      return propPenaltyScale;
            default:                              return wallPenaltyScale; // Wall + Untagged scenery
        }
    }

    /// <summary>
    /// Cached obstacle overlap mask: the "Wall" layer (static scenery) plus the "Character" layer
    /// (other agents/humans), matching the ray sensor. Returns 0 when neither layer is defined.
    /// </summary>
    int ObstacleMask()
    {
        if (_obstacleLayerMask == 0)
        {
            int wall = LayerMask.NameToLayer(ScenePhysicsLayers.EnvironmentLayerName);
            int character = LayerMask.NameToLayer(ScenePhysicsLayers.CharacterLayerName);
            int m = 0;
            if (wall >= 0) m |= (1 << wall);
            if (character >= 0) m |= (1 << character);
            _obstacleLayerMask = m != 0 ? m : -1;
        }
        return _obstacleLayerMask == -1 ? 0 : _obstacleLayerMask;
    }

    /// <summary>True when <paramref name="hit"/> is (part of) the agent's current target object.</summary>
    bool IsCurrentTarget(Transform hit)
    {
        if (target == null || hit == null) return false;
        for (Transform t = hit; t != null; t = t.parent)
            if (t == target || t == target.parent)
                return true;
        return false;
    }

    /// <summary>
    /// Ground Y under a point. For ground-tagged colliders use bounds.max.y (top surface) — raw
    /// hit.point.y can report a box's BOTTOM face and bury the feet (memory gotcha #5).
    /// </summary>
    float SampleGroundY(Vector3 worldPos)
    {
        Vector3 origin = worldPos + Vector3.up * 5f;
        var hits = Physics.RaycastAll(origin, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore);
        float best = 0f;
        bool found = false;
        foreach (var h in hits)
        {
            if (h.collider.GetComponentInParent<ArticulationBody>() != null) continue; // skip the rig
            if (IsCurrentTarget(h.collider.transform)) continue;                       // skip the desk goal
            float topY = h.collider.bounds.max.y;
            if (!found || topY > best) { best = topY; found = true; }
        }
        return found ? best : 0f;
    }

    void OnDestroy()
    {
        if (_ownsTarget && target != null) Destroy(target.gameObject);
    }
}
