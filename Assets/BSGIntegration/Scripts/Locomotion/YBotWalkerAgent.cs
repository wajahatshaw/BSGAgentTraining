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
    // Lowered 6→2.5: at ~0.36 m/s and a ~110-step (~2.2 s) life the agent covers <1 m per episode, so
    // a 3-6 m target was unreachable and the reach bonus almost never fired. Widen this back out only
    // once the chained-reach count in the WHY-EPISODES-END log is consistently high.
    public float targetSpawnRadius = 2.5f;

    [Header("Episode")]
    [Tooltip("Zone index used to clamp the body inside the zone play area (designated player = 0).")]
    public int zoneIndex = 0;
    [Tooltip("Hips height above ground (m) below which the agent is considered fallen. Lowered " +
             "0.6→0.4: at 0.6 the agent (standing hip height ~0.98 m) was killed by any knee bend " +
             "deeper than ~40%, and the WHY-EPISODES-END log showed 89% of episodes ending on this " +
             "check with hips at 0.57-0.59 m. A crouch that deep is recoverable for a real biped, so " +
             "0.6 was terminating episodes the agent could have survived.")]
    public float fallHeight = 0.4f;
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
    // Lowered 0.15→0.05 and 0.05→0.02: posture was 78% of ALL reward the agent earned (0.241/step of
    // 0.31), so standing upright WAS the optimal policy and walking was a rounding error. Posture is
    // now maintenance shaping only — the fall termination and minUprightToSurvive remain the real
    // enforcement, so balance is still required, it just stops being the whole objective.
    public float uprightWeight = 0.05f;
    public float heightWeight = 0.02f;
    [Tooltip("Multiplier applied to uprightWeight/heightWeight ONLY while stabilityOnlyTraining is " +
             "true. The 0.15→0.05 cut exists because posture was out-competing locomotion — but in the " +
             "Stand phase there IS no locomotion reward to compete with, so the cut just leaves a weak " +
             "corrective gradient (0.077/step) against a fallPenalty of 1.0, while failures are 69% " +
             "'tipped' (lateral balance needs fine correction). Scaling here instead of editing the " +
             "weights means the walk-phase values auto-restore when the flag flips back — no manual " +
             "revert to forget. 2.5 => upright 0.125, height 0.05 during Stand.")]
    public float standPostureBoost = 2.5f;
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
    // Lowered 0.05 → 0.015. At 0.05 this was the LARGEST positive term in the whole function and it
    // could be collected by standing still and merely rotating to face the target — which is exactly
    // the "rotates on the spot, faces the desk, never advances" behaviour observed. Facing should nudge
    // the heading, not be a living wage.
    public float headingWeight = 0.015f;
    [Tooltip("Fraction of the locomotion reward still paid when the agent moves toward the target " +
             "while facing AWAY from it (back-pedalling). The facing gate ramps from this value at " +
             "facing<=0 to 1.0 at facing>=0.7, so walking forwards pays 1/this times more than " +
             "reversing. NOT zero on purpose: back-pedalling is the agent's current main locomotion " +
             "mode, and removing all of its income at once would likely collapse it back to standing " +
             "still and lose the movement it has. Lower this toward 0 once forward walking is " +
             "established and facing in the TRAINING log is reliably positive.")]
    public float facingBackpedalCredit = 0.25f;
    [Tooltip("Dense per-step reward for the root's velocity TOWARD the target, normalized to " +
             "desiredWalkSpeed. This is what makes a stand-expert actually start walking: standing " +
             "still earns 0 here, so idling stops being optimal.")]
    public float velocityWeight = 0.4f; // raised 0.2→0.4 (Phase A): moving toward the target now pays more than idling
    [Tooltip("Walk speed (m/s) at which the velocity-toward-target reward saturates — prevents lunging/sprinting.")]
    public float desiredWalkSpeed = 1.5f;
    [Tooltip("Small per-step time cost (subtracted) so standing idle is never free — discourages the " +
             "agent from balancing in place instead of walking to the target. Replaces the old alive bonus. " +
             "Walk mode only (not applied during stabilityOnlyTraining).")]
    // Raised 0.02 → 0.05. Together with headingWeight 0.05→0.015 this drops idle income from
    // ~0.102/step to ~0.034/step while walking still pays ~0.32/step — roughly a 9x preference for
    // moving. Deliberately NOT pushed high enough to make idling negative: with termination available
    // and fallPenalty only 1.0, a negative per-step income teaches the agent to fall on purpose to stop
    // the bleeding, which is a worse failure than idling.
    public float existentialPenalty = 0.05f;
    public float energyPenalty = 0.0002f;
    // HELD AT 1.0 ON PURPOSE — do not raise without evidence. Briefly set to 10.0 on the theory that
    // terminating was a cheap escape hatch (a -1 terminal is small next to the ~0.3-0.5/step a
    // degrading body bleeds). Reverted: the supporting evidence did not hold up. The "avgSinkHeight
    // 0.36-0.39 vs a 0.40 threshold means it sinks deliberately" reading is a MEASUREMENT ARTIFACT —
    // that value is sampled on the first frame that crosses below the threshold, so it always lands
    // just under 0.40 whether the sink is deliberate or accidental. Same for the tight 63-74 step
    // spread, which any near-deterministic policy falling from a fixed spawn would produce.
    // Raising it is also actively risky: a large terminal penalty makes early walking attempts (which
    // DO fall) expensive, and the likely outcome is a policy that freezes rigidly rather than risks a
    // step. Revisit only if episode length rises while TARGETS REACHED stays 0, and change it ALONE.
    public float fallPenalty = 1.0f;
    [Tooltip("Agent.MaxStep (decision steps) applied by YBotLocomotionInstaller. Set because MaxStep " +
             "defaulted to 0 (unlimited): the ONLY way an episode could end was a fall, so EVERY " +
             "terminal in the PPO buffer was a failure bootstrapped with V=0 and the critic never saw " +
             "a single trajectory where surviving paid off. A finite MaxStep makes ML-Agents call " +
             "EpisodeInterrupted() instead of EndEpisode(), which bootstraps the value estimate " +
             "properly. Also bounds the CHAINED-target episode, which was otherwise unbounded. " +
             "MaxStep is compared against StepCount, which counts PHYSICS FRAMES (not decisions), so " +
             "1500 = 1500 * 0.02s = ~30s of life. NOTE: while episodes " +
             "still end in a fall at ~67 steps this never fires and changes NOTHING — it is a " +
             "correctness fix that only becomes load-bearing once episodes actually get long. Do not " +
             "expect it to improve anything on its own.")]
    public int episodeMaxSteps = 1500;
    // Raised 2.0→10.0. At ~0.2 reward/step a +2 bonus was indistinguishable from noise; arrival needs
    // to register as a clear spike now that reaching chains the target instead of ending the episode.
    public float reachReward = 10.0f;

    [Header("Reach validity — the agent must ARRIVE ON ITS FEET")]
    [Tooltip("Minimum dot(up, worldUp) for a reach to count. Without this the agent can DIVE: a " +
             "toppling body's hips travel 0.3-0.6 m horizontally, enough to cross reachTargetDistance " +
             "from the nearest spawn, and OnActionReceived only skips below fallHeight — so mid-fall " +
             "(hips ~0.55 m, upright ~0.5) the reach still fired. That paid +10 against a -1 fall " +
             "penalty: a net +9 for falling over, which is far easier than walking. Arrival must " +
             "therefore require a standing posture.")]
    public float reachMinUpright = 0.7f;
    [Tooltip("Minimum hip height as a FRACTION of standHeight for a reach to count. Blocks the " +
             "'collapse forward into the target' variant that keeps the torso vertical while sinking.")]
    public float reachMinHeightFrac = 0.75f;

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
    // Mean stance gate over the window. This is the direct read-out of the dive fix: ~1.0 means
    // locomotion reward is being earned on the feet, low values mean the agent is still collecting
    // (or trying to collect) while toppling.
    double _accStance;
    // Mean facing gate over the window — the read-out for the back-pedalling fix. Rises toward 1.0 as
    // the agent turns to walk forwards; sits near facingBackpedalCredit while it still reverses.
    double _accFacingGate;

    // --- Termination-cause instrumentation (diagnostics only) ----------------------------------
    // The two fall conditions (hips sank below fallHeight vs body tipped past minUprightToSurvive)
    // share one code path, so the log cannot tell them apart — yet they have OPPOSITE fixes:
    // sinking = the legs aren't holding the body up, tipping = balance/coordination. These counters
    // split them, plus episode-length stats (is the ~100-step episode real or a log artifact?) and
    // the margin values AT the moment of failure (how close to the threshold it was). Pure logging:
    // no reward, no observation, no action-space effect.
    // Termination causes since the last log. obstacleHit only fires in the Avoid phase
    // (enableObstacleAvoidance + endEpisodeOnObstacleHit); it stays 0 during the pure Walk phase.
    int _termSink, _termTip, _termNonFinite, _termObstacleHit;
    // Reach is no longer a termination (targets chain), so it is counted separately — otherwise it
    // would pollute the episode-length stats with mid-episode events.
    int _reachCount;
    // Crossings of the reach radius REJECTED because the agent was not on its feet. A high number
    // here means the policy is actively trying to dive at the target rather than walk to it.
    int _reachRejected;
    double _sinkHeightSum, _tipUprightSum;              // value at failure, to see how marginal it was
    int _epCount; long _epStepsSum; int _epStepsMin = int.MaxValue, _epStepsMax;

    /// <summary>
    /// Records one episode termination for the diagnostics log. Call IMMEDIATELY BEFORE EndEpisode()
    /// — EndEpisode() zeroes StepCount, so the length must be captured first.
    /// </summary>
    void NoteTermination(ref int causeCounter)
    {
        causeCounter++;
        int len = StepCount;
        _epCount++;
        _epStepsSum += len;
        if (len < _epStepsMin) _epStepsMin = len;
        if (len > _epStepsMax) _epStepsMax = len;
    }

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

        if (episodeMaxSteps > 0) MaxStep = episodeMaxSteps;

        // FORWARD-AXIS CHECK (diagnostic only — no reward, no observation, no action effect).

        if (_rig != null && _rig.IsBuilt && _rig.Root != null)
        {
            Vector3 charFwd = transform.forward;
            Vector3 hipsFwd = _rig.Root.transform.forward;
            Debug.Log($"[YBotWalker] FORWARD-AXIS CHECK — dot(hips.forward, character.forward)={Vector3.Dot(hipsFwd, charFwd):F3} " +
                      $"dot(hips.right, character.forward)={Vector3.Dot(_rig.Root.transform.right, charFwd):F3} " +
                      $"dot(hips.up, worldUp)={Vector3.Dot(_rig.Root.transform.up, Vector3.up):F3} | " +
                      $"+1 = aligned (facing is real), -1 = INVERTED (heading reward + obs 55 are backwards), " +
                      $"|right| = 1 = rotated 90deg");
        }

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

        // DEFERRED SPAWN-DEPENDENT SETUP — do NOT place the target or take the distance baseline here.

        _spawnPendingFrames = SpawnSettleFrames;
        _reachGraceFrames = ReachGraceFrames;
        _prevActionBuffer = null; // avoid a spurious jerk spike across the episode boundary
    }

    // --- deferred spawn setup ---------------------------------------------------------------------
    // Physics frames to wait after a reset before trusting Root.transform.position. 2 is enough (the
    // solver applies the teleport + zeroed joint state on the next step); 3 gives a frame of margin.
    const int SpawnSettleFrames = 3;
    // Frames after a reset during which a reach cannot be credited. Covers the stand-up transient, so
    // a reset artefact or the settling motion itself can never collect the +10. Reward-only.
    const int ReachGraceFrames = 15;
    int _spawnPendingFrames;
    int _reachGraceFrames;

    /// <summary>
    /// Spawn-relative setup, run from FixedUpdate once the physics reset has actually been applied.
    /// Places the target, spawns the placeholder field and takes the progress baseline — all from the
    /// TRUE spawn pose rather than the pose the previous episode ended in.
    /// </summary>
    void ApplyDeferredSpawnSetup()
    {
        if (_rig == null || !_rig.IsBuilt) return;

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
        {
            if (!stabilityOnlyTraining)
            {
                AddR(ref _accExistential, -existentialPenalty);
                _prevTargetDistance = PlanarDistanceToTarget();
            }
            return;
        }

        _accSteps++; // count this as a reward-applying step for the per-step breakdown

        // Upright posture: dot(local up, world up).
        float upright = Vector3.Dot(root.up, Vector3.up);
        // Posture weights are boosted during the Stand phase only — see standPostureBoost.
        float postureScale = stabilityOnlyTraining ? Mathf.Max(1f, standPostureBoost) : 1f;
        AddR(ref _accUpright, uprightWeight * postureScale * Mathf.Clamp01(upright));

        float normHeight = Mathf.Clamp((height - fallHeight) / (standHeight - fallHeight), 0f, 1f);
        AddR(ref _accHeight, heightWeight * postureScale * normHeight);

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

        float stance = Mathf.Min(
            Mathf.InverseLerp(minUprightToSurvive, reachMinUpright, upright),      // 0 at 0.40, 1 at 0.70
            Mathf.InverseLerp(fallHeight, reachMinHeightFrac * standHeight, height)); // 0 at 0.40m, 1 at 0.75m
        _accStance += stance;

        float dist = PlanarDistanceToTarget();
        _lastDist = dist;

        // FACING GATE — locomotion credit also requires travelling FORWARDS.
        float facingGate = 1f;
        if (dist > 0.01f)
        {
            Vector3 fwdPlanar = new Vector3(root.forward.x, 0f, root.forward.z).normalized;
            float facingNow = Vector3.Dot(fwdPlanar, PlanarToTarget().normalized);
            facingGate = Mathf.Lerp(facingBackpedalCredit, 1f, Mathf.InverseLerp(0f, 0.7f, facingNow));
        }
        _accFacingGate += facingGate;
        float locomotionGate = stance * facingGate;

      
        if (!stabilityOnlyTraining && progressWeight > 0f && _spawnPendingFrames <= 0)
        {
            float progress = _prevTargetDistance - dist;
            _prevTargetDistance = dist;
            _lastProgress = progress;
            float progressReward = progressWeight * progress;
            if (progressReward > 0f) progressReward *= locomotionGate;
            AddR(ref _accProgress, progressReward);
        }
        else if (_spawnPendingFrames <= 0)
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
            // Clamp(-1,1), NOT Clamp01: with Clamp01 moving AWAY from the target cost nothing, so an
            // agent could oscillate toward-and-away, collect on every approach and pay nothing on
            // every retreat — free reward for shuffling in place, while progress telescoped to zero.
            // Symmetric shaping is also what keeps the intended optimum intact (potential-based
            // shaping only preserves the optimal policy when it is symmetric).
            // Stance-gated (see the stance calculation above): the burst of velocity a toppling body
            // generates toward the target was the single largest source of dive income (+3.2 of the
            // +2.9 net). Positive credit only while standing; the negative half is left at full
            // strength so retreating is never made cheap by a bad posture.
            float velocityReward = velocityWeight * Mathf.Clamp(velToward / desiredWalkSpeed, -1f, 1f);
            if (velocityReward > 0f) velocityReward *= locomotionGate; // on its feet AND facing forwards
            AddR(ref _accVelocity, velocityReward);
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


        if (!stabilityOnlyTraining && _reachGraceFrames <= 0 && dist < reachTargetDistance)
        {
            // The arrival must happen ON ITS FEET — see reachMinUpright. A dive that crosses the reach
            // radius mid-topple is NOT an arrival: it pays nothing and does not chain the target, so
            // the +10 cannot be farmed by falling forward. The target stays put, so if the agent
            // recovers and stands up inside the radius it collects legitimately on a later step.
            bool onFeet = upright >= reachMinUpright && height >= reachMinHeightFrac * standHeight;
            if (!onFeet)
            {
                _reachRejected++; // measures how often it is TRYING to dive
            }
            else
            {
                // TARGET CHAINING — pay the reach bonus and place a NEW target, but do NOT end the
                // episode. Ending it here made success self-defeating: the agent forfeited every
                // future per-step reward (~0.2/step over a ~110-step life) to collect a one-off
                // bonus, so its value function correctly learned that arriving is a bad trade and it
                // avoided the goal. Chaining keeps the income stream alive, so arriving is now
                // strictly profitable — this is what ML-Agents' own Walker/Crawler examples do.
                AddR(ref _accFallReach, reachReward);
                _reachCount++;
                RandomizeTarget();
                _prevTargetDistance = PlanarDistanceToTarget(); // no phantom progress spike
            }
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

        // Deferred spawn setup — see OnEpisodeBegin. Runs once the solver has actually applied the
        // reset, so target placement and the progress baseline use the TRUE spawn pose. Placed before
        // the fall checks below so the first episode frame is never judged against a stale target.
        if (_reachGraceFrames > 0) _reachGraceFrames--;
        if (_spawnPendingFrames > 0)
        {
            _spawnPendingFrames--;
            if (_spawnPendingFrames == 0)
                ApplyDeferredSpawnSetup();
        }

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
                      $"facing={_lastFacing:F2} progressΔ/step={_lastProgress:F4}m " +
                      $"stance={perStep(_accStance):F2} facingGate={perStep(_accFacingGate):F2} " +
                      $"(stance 1.0 = earning ON ITS FEET; facingGate 1.0 = walking FORWARDS, " +
                      $"{facingBackpedalCredit:F2} = back-pedalling and earning only that fraction)");

            // SEPARATE Debug.Log on purpose: Unity's Console list view clips each entry to its first
            // few lines (the rest is only visible in the detail pane), so appending these as lines
            // 4-5 of the message above made them invisible. Its own entry always shows.
            Debug.Log($"[YBotWalker] WHY EPISODES END (last {_epCount} episodes) — " +
                      $"sank(hips<{fallHeight:F2}m)={_termSink} " +
                      $"tipped(upright<{minUprightToSurvive:F2})={_termTip} " +
                      $"nonFinite={_termNonFinite} obstacleHit={_termObstacleHit} | " +
                      $"TARGETS REACHED (on feet, chained)={_reachCount} " +
                      $"reachRejected(dive/not-standing)={_reachRejected}\n" +
                      $"    at failure: avgSinkHeight={(_termSink > 0 ? _sinkHeightSum / _termSink : 0):F2}m " +
                      $"avgTipUpright={(_termTip > 0 ? _tipUprightSum / _termTip : 0):F2} | " +
                      $"episode length avg={(_epCount > 0 ? (double)_epStepsSum / _epCount : 0):F0} steps " +
                      $"min={(_epCount > 0 ? _epStepsMin : 0)} max={_epStepsMax} " +
                      $"(~{(_epCount > 0 ? (double)_epStepsSum / _epCount * Time.fixedDeltaTime : 0):F1}s of life; " +
                      $"needs ~{(_lastDist > 0f && _lastVelToward > 0.01f ? _lastDist / _lastVelToward : -1f):F1}s " +
                      $"to reach at current speed)");

            // Reset the diagnostics window.
            _accUpright = _accHeight = _accFoot = _accProgress = _accHeading = _accVelocity = 0;
            _accExistential = _accObstacle = _accSlip = _accEnergy = _accSmooth = _accFallReach = 0;
            _accStance = _accFacingGate = 0;
            _accSteps = 0;
            _termSink = _termTip = _termNonFinite = _termObstacleHit = 0;
            _reachCount = _reachRejected = 0;
            _sinkHeightSum = _tipUprightSum = 0;
            _epCount = 0; _epStepsSum = 0; _epStepsMin = int.MaxValue; _epStepsMax = 0;
        }

        // Non-finite → reset.
        if (!_rig.HasValidPhysicsState())
        {
            AddR(ref _accFallReach, -fallPenalty);
            NoteTermination(ref _termNonFinite);
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
        float hipHeight = pos.y - groundY;
        bool sank = hipHeight < fallHeight;                 // legs failed to hold the body up
        bool tipped = upright < minUprightToSurvive;        // body toppled over
        if (sank || tipped)
        {
            AddR(ref _accFallReach, -fallPenalty);
            // Attribute to the condition that actually tripped. When BOTH are true the body has
            // already collapsed, so the ordering is arbitrary — sinking is recorded first because a
            // buckling leg is what usually drags the tilt down with it.
            if (sank) { _sinkHeightSum += hipHeight; NoteTermination(ref _termSink); }
            else      { _tipUprightSum += upright;  NoteTermination(ref _termTip); }
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

            float rMin = Mathf.Max(reachTargetDistance + 0.25f, radiusMax * 0.5f);
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
            {
                NoteTermination(ref _termObstacleHit);
                EndEpisode();
            }
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
