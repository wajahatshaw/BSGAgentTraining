using System.Collections.Generic;
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
    // 3.5→5.0 for the Avoid phase: rMin scales to max(1.25, radius*0.5)=2.5, so targets now sit
    // 2.5-5.0 m out and the agent must cross more obstacles instead of stepping straight to the goal.
    public float targetSpawnRadius = 5.0f;

    [Header("Episode")]
    [Tooltip("Zone index used to clamp the body inside the zone play area (designated player = 0).")]
    public int zoneIndex = 0;
    [Tooltip("Hips height above ground (m) below which the agent is considered fallen. Lowered " +
             "0.6→0.4: at 0.6 the agent (standing hip height ~0.98 m) was killed by any knee bend " +
             "deeper than ~40%, and the WHY-EPISODES-END log showed 89% of episodes ending on this " +
             "check with hips at 0.57-0.59 m. A crouch that deep is recoverable for a real biped, so " +
             "0.6 was terminating episodes the agent could have survived.")]
    public float fallHeight = 0.4f;
    [Tooltip("dot(root.up, worldUp) below which the body counts as tipped over and the episode resets.")]
    // 0.4→0.3: avgTipUpright was 0.37, so episodes were cut off before a recovery could be attempted.
    public float minUprightToSurvive = 0.3f;
    [Tooltip("Upright at which locomotion credit starts; full credit at reachMinUpright.")]
    // Held at 0.4 (minUprightToSurvive's old value) so lowering the survival gate buys recovery
    // time without also paying a toppling body more for travelling.
    public float stanceGateMinUpright = 0.4f;
    [Tooltip("Hips height above ground (m) at a healthy standing pose, used to normalize height obs.")]
    public float standHeight = 1.0f;
    public float reachTargetDistance = 1.0f;

    [Header("Curriculum")]
    [Tooltip("STAND PHASE (true): stand/balance only — no walk-to-target reward, target stays at spawn. " +
             "WALK PHASE (false): progress-to-target + velocity + reach rewards activate and the target " +
             "spawns away from the agent. Agent is runtime-added, so this code default is the source of " +
             "truth — inspector edits don't persist.")]
    // TRUE — back to the STAND phase. Set false on 2026-06-19 to start walking; the TensorBoard record
    // says that was premature and is what broke the run:
    //   510k-630k steps (stand phase)  -> episode length 408-508 decisions, i.e. 8-10 s of life. SOLVED.
    //   walk mode on at ~750k          -> 146, then 24.7 by 990k. Never recovered above 37.
    //   6.72M steps (walk, 1 flag)     -> 24.1 decisions, versus ~20 for a ZERO-ACTION ragdoll.
    // The zero-action test then showed why walking cannot be learned from here: the passive rig topples
    // in ~1.2 s (tipped ~80%), so the plant cannot hold a stance long enough for a stride to exist. Asking
    // for locomotion before balance was asking for something the physics does not support yet.
    // Flip back to false only when the stand phase is holding a few hundred decisions AGAIN on the
    // current plant — not on the strength of the old 510k numbers, which were measured on a different rig
    // (hip +-45, ankle 380/320, narrower feet, damping ratio 0.35).
    // Reward-shaping/target-placement only — never touches the frozen 168/21 obs/action spec, so this is
    // safe on any --resume.
    public bool stabilityOnlyTraining = false;

    [Tooltip("Gentle walk-in curriculum for a stand-warm-started policy: start the target CLOSE and in " +
             "a forward cone, then widen distance + angle as episodes accumulate. Cushions the obs- " +
             "normalizer shock (direction-to-target was pinned to 0 for the whole stand phase, so its " +
      
             "running variance is ~0 and the first non-zero value would otherwise slam to the ±5 clamp) " +
             "and asks for a small forward step first instead of an instant 180° turn + long march. " +
             "TARGET PLACEMENT ONLY — never touches the 168/21 obs/action spec, so it is fully " +
             "--resume-safe. Set false to always use the full targetSpawnRadius / 360° placement.")]
    // Off: this ramps on CompletedEpisodes, which resets every Play session, so each Unity restart put
    // targets back at 1.5 m in a ±30° cone — only ~0.3 m of travel per arrival. Its job (cushioning the
    // normalizer right after the stand flip) is long done.
    public bool useWalkTargetCurriculum = false;
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
    [Tooltip("BALANCE SHAPING: penalty on the horizontal distance between the hips and the centroid of " +
             "the GROUNDED feet, normalized by supportOffsetTolerance. This is the only term that " +
             "refers to foot placement relative to the body, i.e. the only one that can teach the agent " +
             "to STEP its support base back under its centre of mass instead of rotating about fixed " +
             "feet until it falls. Cannot be farmed by standing still: a balanced stationary body " +
             "already has ~0 offset and so already pays ~0. Deliberately similar in size to the upright " +
             "reward (0.125 during Stand) so it is a real gradient rather than a rounding error, but " +
             "below it so the agent is never better off crouching or lying down to reduce the offset.")]
    // 0.06→0.045: now multiplied by standPostureBoost, so this keeps it under boosted upright (0.125).
    public float balanceWeight = 0.045f;
    [Tooltip("Distance OUTSIDE the support polygon (m) at which the balance penalty saturates.")]
    // 0.25→0.12: 0.25 only bit once the fall was already committed, leaving no usable gradient.
    public float supportOffsetTolerance = 0.12f;
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
    // 1.5→0.5: agent moves at ~0.1 m/s, so the reward sat in the bottom 7% of its range.
    // Raise toward 1.0 if speed ever plateaus at exactly 0.5 (that is the term saturating).
    public float desiredWalkSpeed = 0.5f;
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
    [Tooltip("Agent.MaxStep in PHYSICS FRAMES (500 = ~10s). Finite so ML-Agents bootstraps via "
             + "EpisodeInterrupted() instead of treating every terminal as a failure.")]
    // 1500→500: episodes ran to the cap, leaving only ~21 episodes per policy update.
    // 750→1200 (24 s): 88% of episodes were hitting the 15 s cap mid-route, cutting off exactly the
    // obstacle-negotiation the Avoid phase needs to learn. Costs gradient diversity (~25 episodes per
    // policy update, down from ~41) — drop back if learning stalls rather than raising it further.
    public int episodeMaxSteps = 1200;
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
    // PHASE 3 ON. Walk met the prerequisite: 1.45 chained reaches/episode with 0 rejected arrivals.
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
    // 1.0→0.05: this is charged EVERY FRAME while inside obstacleHitDistance, not once like fallPenalty.
    // At 1.0 a second of contact cost -50 against ~0.22/frame income — five target arrivals — which would
    // teach freezing, not steering. 0.05 makes a ~20-frame contact cost 1.0, matching a fall.
    public float obstacleHitPenalty = 0.05f;
    [Tooltip("End the episode on a hard obstacle collision (like a fall). Off by default so the agent " +
             "learns to recover/steer away rather than being reset on every graze.")]
    public bool endEpisodeOnObstacleHit = false;
    [Tooltip("Curriculum (avoid phase): bias target placement so an obstacle sits on the straight " +
             "line from the agent to the target, forcing it to actually route around. Enable once " +
             "stand+walk are solid.")]
    // On: without it targets are placed anywhere, so most episodes had a clear straight line and the
    // agent never had to detour. This prefers goals with an obstacle on the direct path.
    public bool biasTargetBehindObstacles = true;

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
    // 4→8: with a 3.5 m radius, 4 obstacles left most agent→target corridors empty.
    public int placeholderObstacleCount = 8;

    [Header("Action smoothness")]
    [Tooltip("Penalty on squared action delta between steps (jerk). Reduces the twitchy PPO ragdoll " +
             "buzz for a more natural gait. Reward-only; safe on any resume.")]
    // 0.005→0.001: cost -0.009/step and paid the agent to move its joints as little as possible.
    // Raise to 0.002 if the gait ends up too twitchy.
    public float actionSmoothWeight = 0.001f;

    [Header("Gait (walk phase only)")]
    // 0.05→0.30: now paid once per swing (peak-ratchet) instead of every frame the foot is held up.
    [Tooltip("Reward for a full swing-foot lift to gaitClearanceTarget. Paid per swing, not per frame.")]
    public float gaitClearanceWeight = 0.30f;
    [Tooltip("Swing-foot height (m) at which the clearance reward saturates.")]
    public float gaitClearanceTarget = 0.12f;
    [Tooltip("One-off bonus each time sole support transfers between feet — i.e. an actual step.")]
    public float gaitAlternationReward = 0.30f;
    [Tooltip("Gait terms are scaled by Clamp01(velToward / this), or they pay for marching on the spot.")]
    public float gaitMinSpeedForCredit = 0.20f;

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
    // 16→48: OverlapSphereNonAlloc TRUNCATES silently, so in a populated scene (walls + stations +
    // placeholders + characters) the nearest obstacle could be dropped and the penalty under-report.
    static readonly Collider[] _obstacleOverlap = new Collider[48];
    YBotTrainingObstacleField _placeholders; // per-episode placeholder obstacles (training only)

    // --- Reward-term instrumentation (diagnostics only) ---------------------------------------
    // Each AddReward is mirrored into a per-window bucket so the TRAINING log can show WHERE the
    // reward is coming from — posture (upright/height/heading) vs actual locomotion (progress/
    // velocity). These buckets never affect learning; AddReward still does all the work. Reset
    // each time the log prints (every 10 s).
    // Gait state + diagnostics. _lastSwingSide: -1 = left foot higher, +1 = right higher, 0 = level.
    const float GaitLiftDeadband = 0.02f; // m of foot-height difference before a side counts as swinging
    int _lastSwingSide;
    float _lastFootLift, _swingPeak;
    double _accClearance, _accAlternation;
    int _stepsSingle, _stepsDouble, _stepsAirborne, _alternations;
    float _gaitWindowStart;

    double _accUpright, _accHeight, _accFoot, _accProgress, _accHeading, _accVelocity;
    double _accExistential, _accObstacle, _accSlip, _accEnergy, _accSmooth, _accFallReach;
    double _accBalance;          // balance-shaping term (hips over support base)
    float _lastSupportOffset;    // latest hips-to-support horizontal offset, m
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
        NoteEpisodeLength(StepCount);
        _notedThisEpisode = true;
    }

    void NoteEpisodeLength(int len)
    {
        _epCount++;
        _epStepsSum += len;
        if (len < _epStepsMin) _epStepsMin = len;
        if (len > _epStepsMax) _epStepsMax = len;
    }

    // MaxStep truncation is handled inside ML-Agents (EpisodeInterrupted), so it never reaches
    // NoteTermination — those episodes were invisible in the log, which read as "reset for no reason".
    bool _notedThisEpisode;
    int _lastStepCount;
    int _termTruncated;

    /// <summary>Counts an episode that ran out its MaxStep budget instead of ending in a fall.</summary>
    void NoteTruncationIfUnended()
    {
        if (_notedThisEpisode || _lastStepCount <= 0) { _notedThisEpisode = false; return; }
        _termTruncated++;
        NoteEpisodeLength(_lastStepCount);
        _notedThisEpisode = false;
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
        NoteTruncationIfUnended();
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
        // SKIPPED during the Stand phase, for the same reason as the foot-slip penalty: a protective
        // step or a fast balance correction IS a large action delta, so this term put a price on every
        // recovery attempt the agent needed to explore. Jerk shaping is for making a LEARNED gait look
        // smooth, not for teaching one. Auto-restores in the Walk phase.
        if (!stabilityOnlyTraining
            && _prevActionBuffer != null && _prevActionBuffer.Length == _actionBuffer.Length && actionSmoothWeight > 0f)
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

        ApplyGaitReward(leftGrounded, rightGrounded);

        // Lower bound is stanceGateMinUpright, NOT minUprightToSurvive — coupling them let a Stand-phase
        // tuning change silently raise Walk/Avoid locomotion credit at every tilt angle.
        float stance = Mathf.Min(
            Mathf.InverseLerp(stanceGateMinUpright, reachMinUpright, upright),     // 0 at 0.40, 1 at 0.70
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

        // BALANCE SHAPING — keep the body over the support base, or STEP so the support base gets back
        // under the body. This is the term the reward function was missing entirely.
        //
        // Nothing else in the function refers to where the feet are RELATIVE to the body. upright and
        // height say "be vertical and tall", footGrounded says "have a foot down", and that is the whole
        // of the balance signal. All of them are satisfied by standing rigidly still, so the policy has
        // no gradient telling it what to DO once it starts leaning — the only consequence of a lean is
        // the terminal fallPenalty, which arrives after the point of no return and cannot teach a
        // correction. That is exactly the observed behaviour: the body rotates about fixed feet and
        // falls, because moving the feet was never worth anything.
        //
        // Penalising the horizontal offset between the hips and the centroid of the GROUNDED feet gives
        // a dense, continuous signal that shrinks either by pulling the body back over the feet (ankle/
        // hip strategy) or by stepping a foot under the body (stepping strategy) — the policy is free to
        // discover either. It cannot be farmed by standing still, because a still, balanced body already
        // has ~0 offset and so already pays ~0; the term only bites during a lean, which is precisely
        // when guidance is needed.
        // STAND PHASE ONLY — and this gate is essential, not tidiness. WALKING LEGITIMATELY PUTS THE
        // BODY OUTSIDE ITS SUPPORT BASE: that forward lean is what converts a stance into a step, and a
        // walking human's centre of mass is outside the support polygon for most of the gait cycle. Left
        // active during the Walk phase this term would charge for the lean that locomotion requires,
        // fighting the progress/velocity rewards and pushing the policy back toward the "rotates on the
        // spot, never advances" behaviour already seen in run v1. Static balance is what it teaches, so
        // it belongs to the phase that asks for static balance; in the Walk phase upright/height, the
        // stance gate and the fall termination carry that job instead.
        // Reward-only — never touches the frozen 168/21 obs/action spec, so it is --resume-safe.
        if (stabilityOnlyTraining && balanceWeight > 0f && (leftGrounded || rightGrounded))
        {
            Vector3 support = Vector3.zero;
            int contacts = 0;
            if (leftGrounded && _leftFoot != null) { support += _leftFoot.transform.position; contacts++; }
            if (rightGrounded && _rightFoot != null) { support += _rightFoot.transform.position; contacts++; }
            if (contacts > 0)
            {
                support /= contacts;
                float offset = new Vector3(root.position.x - support.x, 0f, root.position.z - support.z).magnitude;
                _lastSupportOffset = offset;

                // Deadband: charge only for overhang past the support polygon. Measuring from the
                // centroid made single support pay the max penalty just for lifting a foot.
                offset = Mathf.Max(0f, offset - YBotLocomotionRig.FootWidth * 0.5f);
                // postureScale applies here too: this is a posture term, and unscaled it was the
                // WEAKEST signal during the phase whose whole purpose is balance.
                AddR(ref _accBalance, -balanceWeight * postureScale
                                      * Mathf.Clamp01(offset / Mathf.Max(0.01f, supportOffsetTolerance)));
            }
        }

        // Foot-slip penalty: a grounded foot should plant, not skate (moonwalk). Penalize the
        // horizontal speed of each foot while it's in contact — clamped so a transient spike can't
        // dominate. Keeps the stance natural without forcing any stepping rhythm.
        //
        // SKIPPED during the Stand phase. This is a LOOKS-NATURAL polish term, and it taxes the exact
        // behaviour the agent has to discover first: a protective step lands with real horizontal foot
        // speed, so every recovery attempt was being charged for scuffing. Competence before elegance —
        // it auto-restores when stabilityOnlyTraining flips to false for the Walk phase, where
        // moonwalking is a genuine risk worth pricing.
        if (!stabilityOnlyTraining)
        {
            float slip = 0f;
            if (leftGrounded && _leftFoot != null) slip += Mathf.Min(_leftFoot.HorizontalSpeed, 3f);
            if (rightGrounded && _rightFoot != null) slip += Mathf.Min(_rightFoot.HorizontalSpeed, 3f);
            if (slip > 0f) AddR(ref _accSlip, -footSlipWeight * slip);
        }

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
                FlashReachSuccess();
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
        TickReachFlash();
        _lastStepCount = StepCount; // MaxStep zeroes StepCount before OnEpisodeBegin sees it

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
                      $"height={perStep(_accHeight):F3} foot={perStep(_accFoot):F3} heading={perStep(_accHeading):F3} "
                      + $"balance={perStep(_accBalance):F3}(offset={_lastSupportOffset:F2}m) | " +
                      $"LOCOMOTION: progress={perStep(_accProgress):F3} velocity={perStep(_accVelocity):F3} " +
                      $"clearance={perStep(_accClearance):F3} alternation={perStep(_accAlternation):F3} | " +
                      $"COSTS: obstacle={perStep(_accObstacle):F3} slip={perStep(_accSlip):F3} " +
                      $"existential={perStep(_accExistential):F3} energy={perStep(_accEnergy):F3} smooth={perStep(_accSmooth):F3} " +
                      $"fall/reach={perStep(_accFallReach):F3}\n" +
                      $"    locomotion state — distToTarget={_lastDist:F2}m velToward={_lastVelToward:F2}m/s " +
                      $"facing={_lastFacing:F2} progressΔ/step={_lastProgress:F4}m " +
                      $"stance={perStep(_accStance):F2} facingGate={perStep(_accFacingGate):F2} " +
                      $"(stance 1.0 = earning ON ITS FEET; facingGate 1.0 = walking FORWARDS, " +
                      $"{facingBackpedalCredit:F2} = back-pedalling and earning only that fraction)\n" +
                      GaitReport());

            // SEPARATE Debug.Log on purpose: Unity's Console list view clips each entry to its first
            // few lines (the rest is only visible in the detail pane), so appending these as lines
            // 4-5 of the message above made them invisible. Its own entry always shows.
            Debug.Log($"[YBotWalker] WHY EPISODES END (last {_epCount} episodes) — " +
                      $"sank(hips<{fallHeight:F2}m)={_termSink} " +
                      $"tipped(upright<{minUprightToSurvive:F2})={_termTip} " +
                      $"nonFinite={_termNonFinite} obstacleHit={_termObstacleHit} " +
                      $"truncated(MaxStep, no fall)={_termTruncated} | " +
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
            _accBalance = 0;
            _accStance = _accFacingGate = 0;
            _accClearance = _accAlternation = 0;
            _stepsSingle = _stepsDouble = _stepsAirborne = _alternations = 0;
            _gaitWindowStart = Time.time;
            _accSteps = 0;
            _termSink = _termTip = _termNonFinite = _termObstacleHit = _termTruncated = 0;
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

    /// <summary>Height of a foot's SOLE above the ground, m. ~0 while planted, whatever the ankle angle.</summary>
    float SoleLift(YBotFootContact foot)
    {
        if (foot == null) return 0f;
        Collider c = foot.GetComponent<Collider>();
        Vector3 p = foot.transform.position;
        float soleY = c != null ? c.bounds.min.y : p.y;
        return Mathf.Max(0f, soleY - SampleGroundY(p));
    }

    /// <summary>Pays for lifting the swing foot and alternating support — a stride, not a shuffle.</summary>
    void ApplyGaitReward(bool leftGrounded, bool rightGrounded)
    {
        // Contact census for the gait log (all phases, reward-neutral).
        if (leftGrounded && rightGrounded) _stepsDouble++;
        else if (leftGrounded || rightGrounded) _stepsSingle++;
        else _stepsAirborne++;

        // Measured at the SOLE (collider bounds.min.y), not the ankle bone and not IsGrounded.
        // IsGrounded stays true until a foot clears probeDistance (0.08 m), hiding the whole range where
        // a step begins. The ankle bone is worse: ankle PITCH raises it while the sole stays planted, so
        // it read a permanent 0.05-0.08 m "lift", the ratchet never reset, and clearance paid 0.000 for
        // 400k steps. A sole never reads above ground while any part of it is touching.
        float liftL = SoleLift(_leftFoot), liftR = SoleLift(_rightFoot);
        float dy = liftL - liftR;
        _lastFootLift = Mathf.Max(liftL, liftR);
        // Reset ahead of the credit gate below, or a slow patch would leave the peak latched high.
        if (_lastFootLift < GaitLiftDeadband) _swingPeak = 0f;
        int swingSide = dy > GaitLiftDeadband ? -1 : (dy < -GaitLiftDeadband ? 1 : 0); // -1 = left is swinging
        bool alternated = swingSide != 0 && _lastSwingSide != 0 && swingSide != _lastSwingSide;
        if (alternated) _alternations++;
        if (swingSide != 0) _lastSwingSide = swingSide;

        if (stabilityOnlyTraining) return;

        // Gated on actually travelling, or both terms pay for marching on the spot.
        float credit = Mathf.Clamp01(_lastVelToward / Mathf.Max(0.01f, gaitMinSpeedForCredit));
        if (credit <= 0f) return;

        if (alternated)
            AddR(ref _accAlternation, gaitAlternationReward * credit);

        // Ratchet on the PEAK lift of each swing, reset on touchdown. Paying for the held height instead
        // let it raise one foot and keep it there forever, collecting full clearance without ever
        // stepping — which is exactly what it learned. Paying only for new peaks makes holding worth
        // nothing and small oscillations worth nothing, so the only way to collect again is to set the
        // foot down and lift the other one.
        if (gaitClearanceWeight > 0f && _lastFootLift > _swingPeak)
        {
            float cap = Mathf.Max(0.01f, gaitClearanceTarget);
            float gain = Mathf.Min(_lastFootLift, cap) - Mathf.Min(_swingPeak, cap);
            _swingPeak = _lastFootLift;
            if (gain > 0f) AddR(ref _accClearance, gaitClearanceWeight * credit * (gain / cap));
        }
    }

    // Visual-only success cue: flash the body white on a valid (on-its-feet) arrival.
    [Tooltip("Seconds the body stays white after reaching a target. 0 disables the flash.")]
    public float reachFlashSeconds = 0.4f;
    Renderer[] _bodyRenderers;
    Color[] _bodyBaseColors;
    float _reachFlashUntil;

    /// <summary>Turns the body white for reachFlashSeconds. Reward-neutral.</summary>
    void FlashReachSuccess()
    {
        if (reachFlashSeconds <= 0f) return;

        if (_bodyRenderers == null)
        {
            // Cached once: .material instantiates a copy, so doing this per arrival would leak materials.
            _bodyRenderers = GetComponentsInChildren<Renderer>(true);
            _bodyBaseColors = new Color[_bodyRenderers.Length];
            for (int i = 0; i < _bodyRenderers.Length; i++)
                if (_bodyRenderers[i] != null) _bodyBaseColors[i] = _bodyRenderers[i].material.color;
        }

        for (int i = 0; i < _bodyRenderers.Length; i++)
            if (_bodyRenderers[i] != null) _bodyRenderers[i].material.color = Color.white;

        _reachFlashUntil = Time.time + reachFlashSeconds;
    }

    /// <summary>Restores the cached colours once the flash expires. Called from FixedUpdate.</summary>
    void TickReachFlash()
    {
        if (_reachFlashUntil <= 0f || Time.time < _reachFlashUntil || _bodyRenderers == null) return;
        for (int i = 0; i < _bodyRenderers.Length; i++)
            if (_bodyRenderers[i] != null) _bodyRenderers[i].material.color = _bodyBaseColors[i];
        _reachFlashUntil = 0f;
    }

    /// <summary>Distinguishes walking from shuffling — LEG CHAIN logs joint range but never phase.</summary>
    string GaitReport()
    {
        int n = _stepsSingle + _stepsDouble + _stepsAirborne;
        if (n <= 0) return "    gait — no contact samples";
        float secs = Mathf.Max(0.001f, Time.time - _gaitWindowStart);
        return $"    gait — single={100f * _stepsSingle / n:F0}% double={100f * _stepsDouble / n:F0}% " +
               $"airborne={100f * _stepsAirborne / n:F0}% | alternations={_alternations} " +
               $"({_alternations / secs:F2}/s) footLift={_lastFootLift:F3}m — walking = alternations >0.5/s " +
               $"with footLift approaching {gaitClearanceTarget:F2}m; ~0/s is a SHUFFLE.";
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

        // Created INACTIVE. CreatePrimitive returns an ACTIVE object with a solid Wall-layer collider,
        marker.SetActive(false);
        _targetMarker = marker;
    }

    // The goal carries Reserved1 WHILE it is the goal, then reverts. Without this the goal desk is
    // perceived as plain Furniture — identical to the furniture the agent is being penalized for
    // approaching — so the ray channel says "avoid" while the target vector says "approach".
    // Role-by-runtime-tag; the penalty exemption still keys off IsCurrentTarget, not this.
    readonly List<(GameObject go, string tag)> _goalTagRestore = new List<(GameObject, string)>();

    void ApplyGoalTag()
    {
        for (int i = 0; i < _goalTagRestore.Count; i++)
            if (_goalTagRestore[i].go != null)
                ScenePhysicsLayers.SafeSetTag(_goalTagRestore[i].go, _goalTagRestore[i].tag);
        _goalTagRestore.Clear();

        if (target == null) return;
        foreach (Collider c in target.GetComponentsInChildren<Collider>(true))
        {
            if (c == null || c.isTrigger) continue; // triggers are invisible to rays and the penalty
            _goalTagRestore.Add((c.gameObject, c.tag));
            ScenePhysicsLayers.SafeSetTag(c.gameObject, ScenePhysicsLayers.TagReserved1);
        }
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
        ApplyGoalTag();

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
            case ScenePhysicsLayers.TagWall:      return wallPenaltyScale;
            // Untagged / Reserved* — priced as scenery. Reaching here for a real obstacle means it was
            // never tagged, which also makes it invisible to the ray sensor's tag channels.
            default:                              return wallPenaltyScale;
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
