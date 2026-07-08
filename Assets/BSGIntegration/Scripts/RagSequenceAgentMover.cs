using System;
using System.Collections;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Lightweight sequence runner for RAG-only runtime agents (M1/P1).
/// Mental agent (M1): runs <c>cognitiveActionSequence</c> first (cognitive stations only),
/// then any operational steps.  Physical agent (P1): waits until the zone leader's cognitive
/// pass is fully complete, then runs its operational <c>actionSequence</c> (physical targets).
/// Mental agents are <b>hard-constrained</b>: any step whose <c>targetObjectId</c> does not
/// start with <c>cognitive_</c> is silently skipped — they model brain reasoning and must
/// never walk to physical environment props. After the cognitive pass finishes we do <b>not</b>
/// run <see cref="AgentSequenceManager.GetSequence"/> for mental agents: JSON often mirrors the same
/// steps into <c>actionSequence</c>, which would re-walk every cognitive station while the physical
/// agent executes — violating cognitive-then-physical ordering.
/// Drives <see cref="HumanWalkAnimation"/> when moving.
/// Runs after <see cref="SceneGenerator"/> (script order) so tools exist when resolving targets.
/// </summary>
[DefaultExecutionOrder(50)]
public class RagSequenceAgentMover : MonoBehaviour
{
    /// <summary>Agent whose cognitive pass physical workers wait on. Set by SceneGenerator from RAG policy; defaults to M1.</summary>
    public string mentalLeaderAgentId = "M1";

    [Obsolete("Use mentalLeaderAgentId instance field.")]
    public const string MentalLeaderAgentId = "M1";

    public string agentId;
    [Tooltip("Units/sec along the ground when moving between sequence targets (SceneGenerator sets higher for mental cognitive runs).")]
    public float moveSpeed = 6.5f;
    public float reachThreshold = 1.1f;
    public float minDwellSeconds = 0.05f;
    [Tooltip("Hard upper clamp on dwell — use expectedDuration from RAG. Keep low so rapid steps don't stall.")]
    public float maxDwellSeconds = 5f;

    [Header("Proximity-guided steering (same radii as JSON proximity zones)")]
    [Tooltip("Uses ProximityDetectionSystem zones: steer away from cognitive stations that are NOT the current step target; approach the correct station freely for dwell.")]
    public bool useProximityCognitiveSteering = true;
    [Tooltip("How strongly navigation bends away from non-target cognitive proximity (0–2).")]
    [Range(0f, 2f)] public float proximitySteerAggression = 1.15f;
    [Tooltip("XZ clearance: inward motion toward a non-target station center is removed inside (zone.radius + this).")]
    public float proximityStationHullPadding = 0.45f;
    [Tooltip("Beyond the hull disc, blend toward go-around heading over this distance (meters).")]
    public float proximityApproachBand = 2.35f;

    [Header("Proximity-guided steering — physical environment props")]
    [Tooltip("Steer around solid env props (tables, software props) that are not the current step target.")]
    public bool useProximityEnvironmentSteering = true;
    [Tooltip("Extra clearance beyond env prop bounds for proximity steering (meters).")]
    public float environmentSteerHullPadding = 0.82f;
    [Tooltip("Outer blend band for large env props — wider than cognitive stations.")]
    public float environmentSteerApproachBand = 3.4f;

    [Header("Physical step arrival")]
    [Tooltip("Stand-off (m) at the parent visible hull when walking to a meronym press target.")]
    public float physicalPressApproachStandOff = 0.015f;
    [Tooltip("Max seconds the designated player tries to walk around to a DIFFERENT face on a side-switch retry before giving up and pressing from wherever it can already reach the hull. Guarantees the step keeps retrying (re-arrives) on large objects it can't circle, instead of stranding with the step de-activated.")]
    public float pressRelocateGiveUpSeconds = 4f;

    [Header("Obstacle avoidance — cognitive stations")]
    [Tooltip("Optional Physics box slides (can fight proximity steering). Prefer off when using proximity-only navigation.")]
    public bool avoidCognitiveObstacles = false;
    [Tooltip("Horizontal probe radius at waist height (meters).")]
    public float obstacleProbeRadius = 0.44f;
    [Tooltip("Height above feet for avoidance casts.")]
    public float obstacleProbeHeight = 0.5f;
    [Tooltip("How far ahead to look for blocking geometry.")]
    public float obstacleLookAhead = 2.65f;
    [Tooltip("Start spherecasts slightly forward so the probe is not inside the agent capsule (reduces bogus hits).")]
    public float obstacleCastForwardBias = 0.22f;
    [Tooltip("Extra radius when testing overlap escape (pushes agent out if already inside a station box).")]
    public float obstacleOverlapExtraRadius = 0.14f;
    [Tooltip("Degrees to sweep left/right of desired heading when looking for a clear arc (wider = easier to unstuck).")]
    public float obstacleFanMaxDegrees = 88f;
    [Tooltip("Fan ray step in degrees (smaller = more samples). Used with obstacleFanMaxDegrees.")]
    [Range(8f, 36f)] public float obstacleFanStepDegrees = 16f;
    [Tooltip("Extra score per degree of sweep from straight-ahead for clear rays — reduces hugging the obstacle with tiny angle changes.")]
    [Range(0f, 0.08f)] public float obstacleFanWideSweepBonus = 0.018f;
    [Tooltip("If the fan's best clear ray is shallower than this (degrees) vs the blocked heading, try a hard ±90°/±135°/180° style direction first.")]
    [Range(18f, 85f)] public float obstacleFanMinPickAngleDegrees = 42f;
    [Tooltip("How strongly to blend toward pure tangential slide in the fallback path (after fan search).")]
    [Range(0f, 1f)] public float obstacleDeflectStrength = 0.96f;
    [Tooltip("How much goal direction is mixed into the post-hit slide vector (lower = sharper peel-off from the face).")]
    [Range(0f, 1f)] public float obstacleSlideGoalPull = 0.48f;
    [Tooltip("When sliding along a hit normal, blend this much toward pure tangent vs geometric reflect (higher = less grazing re-hits).")]
    [Range(0f, 0.9f)] public float obstacleReflectTangentBlend = 0.48f;
    [Tooltip("Overlap escape: weight of tangent vs straight out from collider (higher = more sideways shove).")]
    [Range(0f, 0.75f)] public float obstacleOverlapSteerTangentWeight = 0.42f;

    [Header("Hard collision — kinematic bodies ignore static geometry")]
    [Tooltip("Skin offset when clamping motion against CognitiveNavObstacle (meters).")]
    public float obstacleCollisionSkin = 0.085f;
    [Tooltip("Iterations for sliding along obstacle faces after a blocked CapsuleCast.")]
    [Range(1, 8)] public int obstacleSlideIterations = 5;
    [Tooltip("Passes to resolve penetration after MovePosition (Physics.ComputePenetration).")]
    [Range(1, 10)] public int obstacleDepenetratePasses = 6;
    [Tooltip("Inflates the capsule used only for casts / overlaps so blocking triggers before the mesh visually overlaps.")]
    [Range(1f, 2f)] public float obstacleCastRadiusMultiplier = 1.22f;

    [Tooltip("XZ distance from the station solid hull to count as arrived and start dwell/interaction.")]
    [Min(0.35f)]
    public float cognitiveInteractionStandDistance = 0.92f;

    [Tooltip("Legacy tuning field — small extra pad beyond capsule radius for approach stand points.")]
    [Min(0.05f)]
    public float destinationNavRelaxDistance = 0.28f;

    [Header("Stuck recovery")]
    [Tooltip("Seconds without meaningful progress toward the step goal before forcing a wide escape heading.")]
    [Min(0.4f)] public float navStuckTimeoutSeconds = 1.05f;
    [Tooltip("How long to follow the escape heading before resuming normal steering.")]
    [Min(0.35f)] public float navEscapeDurationSeconds = 1.4f;
    [Tooltip("Move speed multiplier while escaping a stuck obstacle.")]
    [Range(1f, 2f)] public float navEscapeSpeedMultiplier = 1.32f;
    [Tooltip("Minimum XZ progress (meters) per progress check interval or the stuck timer advances.")]
    [Min(0.05f)] public float navMinProgressMeters = 0.14f;

    /// <summary>Set by SceneGenerator from AgentProfile.role; when true this mover skips any step whose target is not a cognitive station.</summary>
    public bool isMentalAgent = false;

    /// <summary>Which zone (0-3) this agent belongs to. Set by SceneGenerator from AgentProfile.zoneIndex.</summary>
    public int zoneIndex = 0;

    /// <summary>When true this agent waits for the mental leader's cognitive pass before running its own sequence.</summary>
    public bool gateOperationalOnLeaderCognitive = false;

    [Header("Multiplayer — Photon player")]
    [Tooltip("When true, movement is applied through PlayerMovement instead of transform/Rigidbody kinematic slides.")]
    public bool hostPlayerMovement = false;

    IRagPlayerMovementHost _playerMovement;

    [Header("Inference scene")]
    [Tooltip("When true, mover does not run cognitive steps (legacy fast-forward path).")]
    public bool inferenceSuppressScriptedCognitive = false;

    /// <summary>True when mental agent uses CognitiveAgentZone ONNX for locomotion; mover still owns step order + completion.</summary>
    public bool UsesInferenceCognitiveOnnxLocomotion()
    {
        if (!isMentalAgent || inferenceSuppressScriptedCognitive) return false;
        BSGMLAgent ml = GetComponent<BSGMLAgent>();
        return ml != null && ml.inferenceOnnxControlsLocomotion;
    }

    /// <summary>Photon blue player has no BSGMLAgent — menu press/selection uses timed scripted assist.</summary>
    bool UsesScriptedHostMenuAssist()
    {
        if (!hostPlayerMovement || isMentalAgent)
            return false;
        if (mlAgent == null)
            mlAgent = GetComponent<BSGMLAgent>();
        return mlAgent == null || !mlAgent.isActiveAndEnabled;
    }

    [Header("Results Reporting")]
    [Tooltip("Disabled by default for JSON/RAG scene playback. Enabling this reports each completed step to MLTrainingResultsWriter, which can generate large result files.")]
    public bool reportStepsToMLTrainingResultsWriter = false;

    /// <summary>
    /// Set to true by this component once its cognitive phase is fully done.
    /// Physical agents in the same zone poll this on the mental leader instead of going
    /// through AgentSequenceManager dictionary lookups, which can fail when sequences are
    /// loaded late or a RAG response uses a different structure.
    /// </summary>
    public bool IsCognitivePhaseComplete { get; private set; }

    /// <summary>Inference scene: mark cognitive done so zone physical ONNX agents can unlock.</summary>
    public void SetCognitivePhaseCompleteForInference(bool complete = true)
    {
        IsCognitivePhaseComplete = complete;
        if (complete)
            Debug.Log($"[RagMover] {agentId} cognitive phase complete (inference ONNX).");
    }

    private AgentSequenceData cognitive;
    private AgentSequenceData operational;
    private AgentSequenceData active;
    private bool runningCognitive;
    private float dwellTimer;
    private HumanWalkAnimation walkAnim;
    private Rigidbody rb;
    private CapsuleCollider agentCapsule;
    private BSGMLAgent mlAgent;
    private HandRotationManager handRotationManager;
    private KleinFrameExecutor kleinFrameExecutor;
    private string kleinMotorStepId;
    private bool kleinMotorCompleted;
    private bool _lastStepPhysicalContactVerified;
    // Safety cap (seconds, measured by dwellTimer once arrived) on how long a physical step will wait for
    // its Klein press to signal completion. Guarantees the step still finishes and advances even if the
    // fingertip can't physically reach the target or the motor coroutine stalls — no unbounded hang.
    [SerializeField] private float kleinMotorMaxWaitSeconds = 3.5f;

    // Wall-clock cap (seconds) on the INTERACTION phase of a physical act step — counted only AFTER the
    // agent has arrived. Backstop so the press/dwell can't hang; navigation is never capped (so a step is
    // never force-completed at the wrong place while the agent is still trying to reach the target).
    [SerializeField] private float physicalStepHardCapSeconds = 6f;
    private string _physicalStepTimerId;
    private float _physicalStepElapsed;
    private float _physicalArriveLogThrottle;

    // Progressive press retry — each failed contact attempt walks closer and presses deeper.
    private string _pressRetryStepId;
    private int _pressRetryCount;
    // Relocation give-up timer for side-switch retries — see pressRelocateGiveUpSeconds.
    private string _pressRelocateStepId;
    private int _pressRelocateSideIndex = -1;
    private float _pressRelocateElapsed;
    const float PressRetryStandOffStepM = 0.014f;
    const float PressRetryArriveSlackStepM = 0.012f;
    const float PressRetryHullStepM = 0.011f;
    const float PressRetryNudgeBaseM = 0.07f;
    const float PressRetryNudgeStepM = 0.045f;
    const float PressRetryReachBoostStep = 0.13f;
    const int PressRetryMaxLogged = 12;
    // After this many failed contacts on one approach side, re-approach the meronym from a DIFFERENT face of
    // the parent (nearest edge to the meronym first). Fixes the "keeps retrying from the same spot it can't
    // reach from" case — the agent circles to a side from which the arm can actually land on the part.
    const int PressRetriesPerSide = 2;

    // Cached reference to the zone's mental leader mover — avoids repeated GameObject.Find calls.
    private RagSequenceAgentMover _leaderMoverCache;
    private ProximityDetectionSystem _proximitySteeringCache;
    private SkillBasedActionSystem _skillCache;
    private AgentGroundMotor _groundMotor;

    // Position cache: maps targetObjectId → world position so FindObjectsOfType is never called in Update.
    private Dictionary<string, Vector3> _targetPosCache = new Dictionary<string, Vector3>(32);
    // Ids that have been looked up but not found — skip expensive scan for one full second.
    private Dictionary<string, float> _targetMissingUntil = new Dictionary<string, float>(8);

    private ImaginalThoughtBubble _imaginalThoughtBubble;

    // Goal Buffer RAG initial_state bottom/middle/top (no stack array)
    private string goalBufferTripleSessionStepId;
    private int goalBufferTripleSlotOrdinal;
    private float goalBufferTripleSlotTimer;

    // ── Three-phase orchestrator integration ──────────────────────────────────
    /// <summary>True when a CognitivePhaseOrchestrator is present and has been initialized.</summary>
    private bool _orchestratorMode;
    private bool _orchestratorInitialized;
    private bool _zoneCompletionFinalized;
    private Queue<string> _pendingOrchestratorSteps = new Queue<string>();
    private string _currentOrchestratorStepId;
    private AgentSequenceData _singleStepSequence;
    private string activeMenuStepId;
    private bool activeMenuOpened;
    private bool activeMenuPressLatched;
    private float activeMenuPressWait;
    private float activeMenuTotalWait;
    /// <summary>Per-zone orchestrator reference — set in Start() via GetOrCreateForZone.</summary>
    private CognitivePhaseOrchestrator _zoneOrchestrator;

    /// <summary>World goal for current move step — obstacle system treats destination nav as solid until we are this close in XZ.</summary>
    Vector3 _navGoalForObstacles;

    float _navStuckTimer;
    float _navLastProgressDist = -1f;
    float _navProgressCheckCooldown;
    Vector3 _navEscapeDir;
    float _navEscapeRemaining;
    const float NavProgressCheckInterval = 0.22f;
    const float MenuFingerSelectDistance = 0.85f;
    const float MenuDecisionNudgeSeconds = 0.2f;
    const float MenuDemoAssistSeconds = 2.0f;
    const float HostMenuScriptedPressSeconds = 0.38f;
    const float HostMenuSelectionArriveDistance = 1.45f;
    const float MenuCorrectPressReward = 0.06f;
    const float MenuCorrectSelectionReward = 0.12f;
    const float MenuEarlyPressPenalty = -0.025f;
    const float MenuDemoAssistPenalty = -0.04f;
    const float MenuIdlePenaltyPerSecond = -0.002f;

    void Start()
    {
        walkAnim = GetComponent<HumanWalkAnimation>();
        rb = GetComponent<Rigidbody>();
        mlAgent = GetComponent<BSGMLAgent>();
        // Only physical agents wire a hand/arm rig. Mental (M) agents are brain-only — wiring a hand rig
        // on them just spams "bone not found" and is never used.
        if (!isMentalAgent)
            handRotationManager = HandRotationManager.EnsureOnAgent(gameObject);
        if (hostPlayerMovement)
            kleinFrameExecutor = KleinFrameExecutor.EnsureOnAgent(gameObject, zoneIndex);
        _playerMovement = hostPlayerMovement ? GetComponent<IRagPlayerMovementHost>() : null;
        _groundMotor = GetComponent<AgentGroundMotor>();
        if (_groundMotor == null && !hostPlayerMovement)
            _groundMotor = gameObject.AddComponent<AgentGroundMotor>();
        if (_groundMotor != null)
        {
            _groundMotor.clampZoneIndex = zoneIndex;
            _groundMotor.SnapFeetToGround();
        }
        else if (_playerMovement != null)
        {
            _playerMovement.SetRagAutopilot(false, zoneIndex);
        }
        agentCapsule = GetComponent<CapsuleCollider>();
        if (agentCapsule == null)
            agentCapsule = GetComponentInChildren<CapsuleCollider>();
        _proximitySteeringCache = FindObjectOfType<ProximityDetectionSystem>();

        // Auto-create and subscribe to the per-zone CognitivePhaseOrchestrator.
        // This always activates orchestrator mode — no scene placement required.
        _zoneOrchestrator = CognitivePhaseOrchestrator.GetOrCreateForZone(zoneIndex);
        if (_zoneOrchestrator != null)
        {
            _orchestratorMode = true;
            if (isMentalAgent)
            {
                _zoneOrchestrator.OnCognitiveStepDispatched += HandleOrchestratorCognitiveStep;
                _zoneOrchestrator.OnBarrierReached          += OnOrchestratorBarrierReached;
            }
            else
            {
                _zoneOrchestrator.OnPhysicalStepDispatched += HandleOrchestratorPhysicalStep;
            }
            _zoneOrchestrator.OnAllStepsCompleted += OnOrchestratorAllStepsCompleted;
            Debug.Log($"[RagMover] {agentId} registered with CognitivePhaseOrchestrator zone={zoneIndex} (isMental={isMentalAgent})");
        }

        ResolveSequences();
        TryRecoverMissedOrchestratorDispatch();
    }

    void TryRecoverMissedOrchestratorDispatch()
    {
        if (!_orchestratorMode || _zoneOrchestrator == null)
            return;

        if (active != null && !string.IsNullOrEmpty(_currentOrchestratorStepId))
            return;

        if (isMentalAgent)
            TryRecoverMissedOrchestratorCognitiveDispatch();
        else
            TryRecoverMissedOrchestratorPhysicalDispatch();
    }

    void TryRecoverMissedOrchestratorCognitiveDispatch()
    {
        if (!enabled || !isMentalAgent)
            return;

        if (_zoneOrchestrator.TryGetFirstActiveCognitiveStepId(out string activeId))
            HandleOrchestratorCognitiveStep(activeId);
    }

    void TryRecoverMissedOrchestratorPhysicalDispatch()
    {
        if (!enabled || isMentalAgent)
            return;

        if (_zoneOrchestrator.TryGetFirstActivePhysicalStepId(out string activeId))
            HandleOrchestratorPhysicalStep(activeId);
    }

    /// <summary>Late-bound Photon P1 calls this after <see cref="PlayerRagPhysicalBridge"/> attaches the mover.</summary>
    public void RecoverOrchestratorDispatchIfNeeded()
    {
        TryRecoverMissedOrchestratorDispatch();
    }

    /// <summary>Rebind ground motor after <see cref="PlayerRagPhysicalAgentSetup"/> runs on the player.</summary>
    public void RefreshHostPlayerGroundMotor()
    {
        if (!hostPlayerMovement)
            return;

        _groundMotor = GetComponent<AgentGroundMotor>();
        if (_groundMotor != null)
            _groundMotor.clampZoneIndex = zoneIndex;

        agentCapsule = GetComponent<CapsuleCollider>();
        if (agentCapsule == null)
            agentCapsule = GetComponentInChildren<CapsuleCollider>();
    }

    void OnDestroy()
    {
        if (_zoneOrchestrator != null && _orchestratorMode)
        {
            if (isMentalAgent)
            {
                _zoneOrchestrator.OnCognitiveStepDispatched -= HandleOrchestratorCognitiveStep;
                _zoneOrchestrator.OnBarrierReached          -= OnOrchestratorBarrierReached;
            }
            else
            {
                _zoneOrchestrator.OnPhysicalStepDispatched -= HandleOrchestratorPhysicalStep;
            }
            _zoneOrchestrator.OnAllStepsCompleted -= OnOrchestratorAllStepsCompleted;
        }
    }

    void Update()
    {
        if (string.IsNullOrWhiteSpace(agentId))
            return;

        if (hostPlayerMovement && _playerMovement != null && !isMentalAgent)
        {
            bool drivePlayer = active != null;
            // Orchestrator DAG controls physical readiness; do not wait for full leader cognitive pass.
            if (!_orchestratorMode && gateOperationalOnLeaderCognitive)
                drivePlayer = active != null && LeaderCognitiveGateAllowsPhysicalStart();
            // Once ARRIVED at a physical target (isActivated) and until the step completes, stop the walk
            // autopilot so the agent STANDS STILL and performs the task via Klein/IK — not the walk
            // animation. Covers RESTING frames too (which hold no IK pose). Autopilot resumes once the
            // step is marked complete so the agent walks on to the next step.
            ActionSequenceStep curStep = active != null ? active.GetCurrentStep() : null;
            if (drivePlayer && curStep != null && curStep.isActivated
                && IsPhysicalManualActStep(curStep) && !curStep.isStepCompleted)
                drivePlayer = false;
            _playerMovement.SetRagAutopilot(drivePlayer, zoneIndex);
        }

        if (_zoneCompletionFinalized)
        {
            StopMovementAndClearActiveStep();
            return;
        }

        if (active == null || active.actionSequence == null || active.actionSequence.Count == 0)
        {
            // Throttle: only re-run ResolveSequences every 0.1 s while waiting — not every frame.
            if (Time.time >= _resolveThrottle)
            {
                _resolveThrottle = Time.time + 0.1f;
                ResolveSequences();
                TryRecoverMissedOrchestratorDispatch();
            }
            if (walkAnim != null) walkAnim.StopWalking();
            return;
        }

        ActionSequenceStep step = active.GetCurrentStep();

        // After last step: MoveToNextStep does not advance index — treat completed tail as "sequence done"
        if (step != null && step.isStepCompleted && !active.HasNextStep())
        {
            // Orchestrator mode: just clear active; NotifyStepCompleted was already called in the
            // dwell-complete block. Dequeue the next pending step if any.
            if (_orchestratorMode)
            {
                active = null;
                if (walkAnim != null) walkAnim.StopWalking();
                TryDequeueNextOrchestratorStep();
                return;
            }

            if (runningCognitive)
            {
                runningCognitive = false;
                if (isMentalAgent) TryMarkZoneCognitiveReadyForPhysical("cognitive sequence tail");
                // Mental: stop here — do not run mirrored/duplicate operational lists (see class summary).
                active = isMentalAgent ? null : operational;
                if (active == null || active.actionSequence == null || active.actionSequence.Count == 0)
                {
                    if (walkAnim != null) walkAnim.StopWalking();
                    return;
                }

                step = active.GetCurrentStep();
            }
            else
            {
                if (walkAnim != null) walkAnim.StopWalking();
                return;
            }
        }

        if (step == null)
        {
            if (walkAnim != null) walkAnim.StopWalking();
            if (runningCognitive && IsSequenceCompleted(active))
            {
                runningCognitive = false;
                active = isMentalAgent ? null : operational;
            }
            return;
        }

        string effectiveTargetId = ResolveEffectiveTargetObjectId(step);

        Vector3? targetPos = ResolveTargetPosition(effectiveTargetId);
        if (!targetPos.HasValue)
        {
            if (walkAnim != null) walkAnim.StopWalking();
            if (_orchestratorMode && !string.IsNullOrEmpty(step.stepId))
            {
                Debug.LogWarning($"[RagMover] {agentId} orchestrator step '{step.stepId}' target missing ({effectiveTargetId}) — auto-completing.");
                _currentOrchestratorStepId = null;
                NotifyOrchestratorStepCompleted(step.stepId);
                active = null;
                TryDequeueNextOrchestratorStep();
            }
            return;
        }
        if (!isMentalAgent && mlAgent != null)
            mlAgent.SetMlNavigationTarget(targetPos.Value, effectiveTargetId);

        // Backstop for the INTERACTION phase only: once the agent has ARRIVED, don't let the press/dwell
        // hang forever. It deliberately does NOT time the navigation phase — force-completing while the
        // agent is still walking (or stuck on the wrong obstacle) would mark the step done at the wrong
        // place. If it can't reach the target it must keep navigating around, never falsely complete.
        if (IsPhysicalManualActStep(step))
        {
            if (!string.Equals(_physicalStepTimerId, step.stepId, StringComparison.Ordinal))
            {
                _physicalStepTimerId = step.stepId;
                _physicalStepElapsed = 0f;
            }
            if (step.isActivated)
            {
                _physicalStepElapsed += Time.deltaTime;
                if (_physicalStepElapsed >= physicalStepHardCapSeconds && !step.isStepCompleted)
                {
                    if (RequiresPhysicalPressContact(step) && !HasAchievedPhysicalPressContact())
                    {
                        HandlePhysicalPressMissed(step, $"no meronym contact after {physicalStepHardCapSeconds:F0}s");
                    }
                    else
                    {
                        Debug.LogWarning($"[RagMover] {agentId} physical step '{step.stepId}' → '{step.physicalTarget}' arrived but couldn't finish in {physicalStepHardCapSeconds:F0}s — force-completing.");
                        FinishStepDwellAndComplete(step);
                    }
                    return;
                }
            }
            else
            {
                _physicalStepElapsed = 0f;   // still navigating — don't count against the cap
            }
        }

        // Mental agents are brain-only — they must NEVER walk to physical env objects.
        // If a step targets anything other than a cognitive_* station, auto-complete and skip it.
        if (isMentalAgent && !IsCognitiveStationTarget(step.targetObjectId))
        {
            Debug.LogWarning($"[RagMover] Mental agent {agentId} skipping non-cognitive step {step.stepId} → {step.targetObjectId}");
            _imaginalThoughtBubble?.Hide();
            step.isActivated = true;
            ApplyPhysicalOnlyStepReward(step);
            RecordStepProducedPayload(step);
            ApplyBufferStateSideEffects(step);
            active.MarkStepCompleted();
            active.MoveToNextStep();
            dwellTimer = 0f;
            ResetNavStuckState();
            return;
        }

        Vector3 stationCenter = targetPos.Value;
        Vector3 moveGoal = ResolveMoveGoalPosition(effectiveTargetId, stationCenter, step);
        _navGoalForObstacles = moveGoal;

        Vector3 to = moveGoal - transform.position;
        to.y = 0f;
        float dist = to.magnitude;
        float progressDist = GetApproachProgressDistance(effectiveTargetId, stationCenter, dist, step);

        bool arrived = step.isActivated;
        if (!arrived)
            arrived = HasArrivedAtStep(step, effectiveTargetId, stationCenter, dist);

        // Advance the side-switch relocation timer so a large object the agent can't circle still re-arrives
        // (presses from where it can reach) instead of stranding with the step de-activated.
        if (IsCorrectKleinPressTarget(step))
            UpdatePressRelocateTimer(step, arrived);

        // Diagnostic: while a physical act step hasn't arrived, report why (throttled ~1s) so a stuck
        // "keeps bumping the target" state is legible — distance to goal vs the object's hull.
        if (!arrived && IsPhysicalManualActStep(step) && Time.time >= _physicalArriveLogThrottle)
        {
            _physicalArriveLogThrottle = Time.time + 1f;
            GameObject dbgRoot = ResolveKleinApproachObject(step) ?? FindStationRoot(effectiveTargetId);
            float hull = dbgRoot != null ? GetVisibleFootprintDistance(dbgRoot.transform, transform.position) : -1f;
            Debug.Log($"[RagMover] {agentId} not arrived at '{step.stepId}'→'{step.physicalTarget}' (parent {effectiveTargetId}): distToGoal={dist:F2}, hullDist={hull:F2}, klein={IsCorrectKleinPressTarget(step)}, host={hostPlayerMovement}, elapsed={_physicalStepElapsed:F1}s.");
        }

        if (!arrived)
        {
            dwellTimer = 0f;
            if (!IsMenuOpenedForStep(step))
            {
                activeMenuPressWait = 0f;
                activeMenuTotalWait = 0f;
                activeMenuPressLatched = false;
                ResetMenuReachPose();
            }
            ClearGoalBufferTripleSession(step);
            _imaginalThoughtBubble?.Hide();

            // Inference: Cognitive ONNX moves; mover picks active step + fires completion when arrived.
            if (UsesInferenceCognitiveOnnxLocomotion())
            {
                BSGMLAgent cogMl = GetComponent<BSGMLAgent>();
                if (cogMl != null)
                {
                    cogMl.SetMlNavigationTarget(moveGoal, effectiveTargetId);
                    var dr = GetComponent<Unity.MLAgents.DecisionRequester>();
                    if (dr != null && dr.enabled)
                        cogMl.RequestDecision();
                }
                if (walkAnim != null) walkAnim.StartWalking();
                return;
            }

            UpdateNavStuckProgress(progressDist);

            Vector3 dir;
            float speedMult = 1f;

            if (_navEscapeRemaining > 0f)
            {
                _navEscapeRemaining -= Time.deltaTime;
                dir = _navEscapeDir.sqrMagnitude > 1e-8f ? _navEscapeDir : to.normalized;
                speedMult = navEscapeSpeedMultiplier;
            }
            else
            {
                dir = to.normalized;
                if (useProximityCognitiveSteering || avoidCognitiveObstacles)
                    dir = ComputeSteeredGroundDirection(dir, moveGoal, effectiveTargetId);

                if (_navStuckTimer >= navStuckTimeoutSeconds
                    && TryBeginNavEscape(dir, to.normalized, moveGoal, effectiveTargetId, out Vector3 escapeDir))
                {
                    _navEscapeDir = escapeDir;
                    _navEscapeRemaining = navEscapeDurationSeconds;
                    _navStuckTimer = 0f;
                    _navLastProgressDist = progressDist;
                    dir = escapeDir;
                    speedMult = navEscapeSpeedMultiplier;
                }
            }

            Vector3 delta = dir * moveSpeed * speedMult * Time.deltaTime;
            ApplyMovementDelta(delta, effectiveTargetId);

            Vector3 faceFlat = stationCenter - transform.position;
            faceFlat.y = 0f;
            if (faceFlat.sqrMagnitude < 0.04f)
                faceFlat = dir;
            if (faceFlat.sqrMagnitude > 0.001f)
            {
                float rotRate = _navEscapeRemaining > 0f ? 11f : 8f;
                Quaternion look = Quaternion.LookRotation(faceFlat.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, rotRate * Time.deltaTime);
            }
            if (walkAnim != null) walkAnim.StartWalking();
            return;
        }

        ResetNavStuckState();
        FaceTowardStation(stationCenter);

        if (UsesInferenceCognitiveOnnxLocomotion())
        {
            BSGMLAgent cogMl = GetComponent<BSGMLAgent>();
            cogMl?.ClearMlNavigationTarget();
        }

        // Arrived — latch activation, record observation, then dwell + act before completion.
        if (walkAnim != null) walkAnim.StopWalking();
        if (!step.isActivated)
        {
            step.isActivated = true;
            OnStepArrived(step);
        }

        if (isMentalAgent && runningCognitive && GoalBufferTripleOrchestrator.IsTripleModeStep(step))
        {
            _imaginalThoughtBubble?.Hide();
            if (ProcessGoalBufferTripleAtStation(step))
                return;
        }

        if (RagMenuController.IsMenuStep(step))
        {
            if (!activeMenuPressLatched)
            {
                if (!TryLatchMenuFingerPress(step, stationCenter))
                    return;
            }

            ApplyMenuHandPose(step, true);
            ApplyMenuReachPose(stationCenter, true);
        }
        else if (IsPhysicalManualActStep(step))
        {
            // Ramp press reach while dwelling so a single attempt keeps driving the finger down.
            if (hostPlayerMovement && RequiresPhysicalPressContact(step) && !HasAchievedPhysicalPressContact())
            {
                float intraBoost = Mathf.Clamp01(dwellTimer / 2.2f) * 0.28f;
                ApplyPressReachRetryBoost(step, intraBoost);
            }

            if (IsCorrectKleinPressTarget(step))
                TryDriveKleinMotorForStep(step, stationCenter);
            else
                EnsureKleinPoseCleared();
        }

        dwellTimer += Time.deltaTime;

        float dwellNeed = CognitiveProcessManager.ComputeStationDwellSeconds(step);
        dwellNeed = Mathf.Clamp(dwellNeed, minDwellSeconds, maxDwellSeconds);
        if (RagMenuController.IsMenuStep(step))
            dwellNeed = IsMenuOpenedForStep(step) ? 0.18f : 0.28f;
        else if (IsPhysicalManualActStep(step))
        {
            if (kleinFrameExecutor != null && hostPlayerMovement)
                dwellNeed = Mathf.Max(dwellNeed, kleinFrameExecutor.GetRequiredDwellSeconds(step, agentId));
            else
                dwellNeed = Mathf.Max(dwellNeed, 0.35f);
        }

        UpdateImaginalThoughtBubbleWhileDwelling(step);

        // Wait for the Klein press to finish before completing the step — up to the motor's contact window
        // (duration + pressContactTimeout), capped by kleinMotorMaxWaitSeconds so a stalled coroutine can't block forever.
        float motorWaitCap = kleinMotorMaxWaitSeconds;
        if (kleinFrameExecutor != null)
            motorWaitCap = Mathf.Max(motorWaitCap, kleinFrameExecutor.GetRequiredDwellSeconds(step, agentId));
        if (IsPhysicalManualActStep(step) && kleinFrameExecutor != null && hostPlayerMovement
            && !string.IsNullOrEmpty(kleinMotorStepId)
            && string.Equals(kleinMotorStepId, step.stepId, StringComparison.Ordinal)
            && !kleinMotorCompleted
            && dwellTimer < motorWaitCap)
            return;

        if (dwellTimer < dwellNeed)
            return;

        if (IsPhysicalManualActStep(step) && hostPlayerMovement && RequiresPhysicalPressContact(step)
            && !HasAchievedPhysicalPressContact())
        {
            HandlePhysicalPressMissed(step, "motor dwell finished without meronym contact");
            return;
        }

        if (RagMenuController.IsMenuStep(step) && !IsMenuOpenedForStep(step))
        {
            activeMenuStepId = step.stepId;
            activeMenuOpened = true;
            activeMenuPressLatched = false;
            activeMenuPressWait = 0f;
            activeMenuTotalWait = 0f;
            dwellTimer = 0f;
            _targetPosCache.Remove(RagMenuController.GetSelectedOptionTargetId(step));
            _targetMissingUntil.Remove(RagMenuController.GetSelectedOptionTargetId(step));
            RagMenuController.EnsureInScene()?.ShowButtonPressedFeedback(
                PhysicalStepTargetResolver.ResolveObjectId(step, zoneIndex), zoneIndex);
            RagMenuController.EnsureInScene()?.ShowMenuForStep(step, zoneIndex);
            Debug.Log($"[RagMover] {agentId} opened menu for {step.stepId}; selecting {RagMenuController.GetSelectedOptionTargetId(step)}");
            return;
        }

        FinishStepDwellAndComplete(step);
    }

    /// <summary>ManualModule press/type/scroll physical steps — navigate to target first, then act in place.</summary>
    static bool IsPhysicalManualActStep(ActionSequenceStep step)
    {
        if (step == null)
            return false;
        if (RagMenuController.IsMenuStep(step))
            return false;
        return string.Equals(step.actionType, "act", StringComparison.OrdinalIgnoreCase);
    }

    float GetPhysicalManualActArrivalDistance(ActionSequenceStep step)
    {
        if (IsCorrectKleinPressTarget(step))
            return GetPressContactStandDistance(step) + GetPressApproachStandOff(step);

        float stand = GetInteractionStandDistance();
        float dist = Mathf.Max(reachThreshold, stand + 0.28f);
        if (hostPlayerMovement)
            dist = Mathf.Max(dist, stand + 0.42f);
        if (IsPhysicalManualActStep(step) && !hostPlayerMovement)
            dist = Mathf.Max(dist, 1.35f);

        return dist;
    }

    public bool WasLastPhysicalStepContactVerified => _lastStepPhysicalContactVerified;

    public bool StepRequiresPressContact(ActionSequenceStep step) => RequiresPhysicalPressContact(step);

    public bool DidAchievePressContactForStep(ActionSequenceStep step)
    {
        return !RequiresPhysicalPressContact(step) || HasAchievedPhysicalPressContact();
    }

    bool RequiresPhysicalPressContact(ActionSequenceStep step)
    {
        if (step == null || !IsPhysicalManualActStep(step))
            return false;

        string meronym = ResolveStepMeronymName(step);
        if (IsRestingPhysicalMeronym(meronym))
            return false;

        if (ManualBufferCatalog.TryGetForStep(step.stepId, agentId, zoneIndex, out KleinFrame frame) && frame != null)
        {
            string cmd = FirstPhysicalCommandToken(frame.manualCommand);
            if (cmd.StartsWith("RESTING", StringComparison.OrdinalIgnoreCase)
                || string.Equals(frame.effectorBodyPart, "torso", StringComparison.OrdinalIgnoreCase))
                return false;

            if (cmd.StartsWith("PRESSING", StringComparison.OrdinalIgnoreCase)
                || cmd.StartsWith("DEPRESSING", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Never treat a press meronym as contact-optional just because Klein lookup failed.
        return IsPressPhysicalMeronym(meronym);
    }

    static string FirstPhysicalCommandToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        value = value.Trim();
        int space = value.IndexOf(' ');
        return space > 0 ? value.Substring(0, space) : value;
    }

    static bool IsRestingPhysicalMeronym(string meronym)
    {
        if (string.IsNullOrWhiteSpace(meronym))
            return false;
        return meronym.IndexOf("surface", StringComparison.OrdinalIgnoreCase) >= 0
               || meronym.IndexOf("desktop", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool IsPressPhysicalMeronym(string meronym)
    {
        if (string.IsNullOrWhiteSpace(meronym))
            return false;
        if (IsRestingPhysicalMeronym(meronym))
            return false;
        return meronym.IndexOf("key", StringComparison.OrdinalIgnoreCase) >= 0
               || meronym.IndexOf("button", StringComparison.OrdinalIgnoreCase) >= 0
               || meronym.IndexOf("wheel", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    void HandlePhysicalPressMissed(ActionSequenceStep step, string reason)
    {
        _lastStepPhysicalContactVerified = false;
        _imaginalThoughtBubble?.Hide();

        float lastGap = kleinFrameExecutor != null ? kleinFrameExecutor.ClosestTipGap : float.NaN;
        int attempt = RegisterPressRetryAttempt(step);
        float standOff = GetPressApproachStandOff(step);
        float boost = GetPressReachRetryBoost(step);

        // Does this failure roll us onto a new approach side? If so, DON'T nudge toward the meronym — the
        // agent must first walk around to the new face (nudging here would shove it into the parent from the
        // old, unreachable spot). ResolveMoveGoalPosition + HasArrivedAtPhysicalPressTarget re-route it.
        int prevSide = (attempt - 1) / PressRetriesPerSide;
        int newSide = attempt / PressRetriesPerSide;
        bool switchingSide = newSide != prevSide;
        if (switchingSide)
            Debug.Log($"[RagMover] {agentId} '{step?.stepId}' → '{step?.physicalTarget}' unreachable from side {prevSide} after {PressRetriesPerSide} tries — re-approaching from side {newSide}.");
        else
            NudgeAgentCloserToPressTarget(step, attempt);

        ClearKleinMotorSession();
        kleinMotorStepId = null;
        kleinMotorCompleted = false;
        dwellTimer = 0f;
        _physicalStepElapsed = 0f;
        _physicalStepTimerId = null;

        if (step != null)
        {
            step.isActivated = false;
            step.isStepCompleted = false;
        }

        ApplyPressReachRetryBoost(step);

        string gapText = float.IsNaN(lastGap) || lastGap >= float.MaxValue * 0.5f ? "?" : $"{lastGap:F3}m";
        Debug.LogWarning($"[RagMover] {agentId} '{step?.stepId}' → '{step?.physicalTarget}' {reason} — " +
                         $"retry #{attempt}: standOff={standOff:F3}m reachBoost={boost:F2} lastGap={gapText} (step NOT completed).");
    }

    int GetPressRetryCount(ActionSequenceStep step)
    {
        if (step == null || string.IsNullOrEmpty(step.stepId))
            return 0;
        if (!string.Equals(_pressRetryStepId, step.stepId, StringComparison.Ordinal))
            return 0;
        return _pressRetryCount;
    }

    int RegisterPressRetryAttempt(ActionSequenceStep step)
    {
        if (step == null || string.IsNullOrEmpty(step.stepId))
            return 0;

        if (!string.Equals(_pressRetryStepId, step.stepId, StringComparison.Ordinal))
        {
            _pressRetryStepId = step.stepId;
            _pressRetryCount = 0;
        }

        _pressRetryCount = Mathf.Min(_pressRetryCount + 1, PressRetryMaxLogged);
        return _pressRetryCount;
    }

    void ResetPressRetry(ActionSequenceStep step)
    {
        if (step == null || string.IsNullOrEmpty(step.stepId))
            return;
        if (string.Equals(_pressRetryStepId, step.stepId, StringComparison.Ordinal))
        {
            _pressRetryStepId = null;
            _pressRetryCount = 0;
        }
        ApplyPressReachRetryBoost(step, 0f);
    }

    float GetPressApproachStandOff(ActionSequenceStep step)
    {
        int retries = GetPressRetryCount(step);
        return Mathf.Max(-0.045f, physicalPressApproachStandOff - retries * PressRetryStandOffStepM);
    }

    /// <summary>Which approach-side attempt we're on for this press step (0 = natural first approach, then
    /// one increment per <see cref="PressRetriesPerSide"/> failed contacts). Drives face re-selection.</summary>
    int GetPressSideIndex(ActionSequenceStep step)
    {
        return GetPressRetryCount(step) / PressRetriesPerSide;
    }

    /// <summary>Counts how long the agent has been trying to walk to the current side-switch face without
    /// arriving. Resets when the step, the side, or arrival changes. Read by <see cref="HasArrivedAtPhysicalPressTarget"/>
    /// to give up relocating (press in place) so re-arrival is guaranteed on objects it can't circle.</summary>
    void UpdatePressRelocateTimer(ActionSequenceStep step, bool arrived)
    {
        if (step == null)
            return;

        int sideIdx = GetPressSideIndex(step);
        if (!string.Equals(_pressRelocateStepId, step.stepId, StringComparison.Ordinal) || _pressRelocateSideIndex != sideIdx)
        {
            _pressRelocateStepId = step.stepId;
            _pressRelocateSideIndex = sideIdx;
            _pressRelocateElapsed = 0f;
        }

        if (arrived || sideIdx == 0)
        {
            _pressRelocateElapsed = 0f;   // fresh budget for the next relocation
        }
        else
        {
            bool wasGivenUp = _pressRelocateElapsed >= pressRelocateGiveUpSeconds;
            _pressRelocateElapsed += Time.deltaTime;
            if (!wasGivenUp && _pressRelocateElapsed >= pressRelocateGiveUpSeconds)
                Debug.Log($"[RagMover] {agentId} '{step.stepId}' → '{step.physicalTarget}' couldn't circle to side {sideIdx} in {pressRelocateGiveUpSeconds:F0}s — pressing from the nearest reachable hull instead (still re-arrives/retries).");
        }
    }

    bool PressRelocateGaveUp(ActionSequenceStep step)
    {
        return step != null
               && string.Equals(_pressRelocateStepId, step.stepId, StringComparison.Ordinal)
               && _pressRelocateSideIndex == GetPressSideIndex(step)
               && _pressRelocateElapsed >= pressRelocateGiveUpSeconds;
    }

    /// <summary>Outward horizontal approach direction for the <paramref name="rank"/>-th best parent face —
    /// ordered so the face NEAREST the meronym (least arm reach-across) is rank 0, then next-nearest, etc.
    /// Used by side-switching retries to circle the parent to a reachable face.</summary>
    Vector3 GetPressApproachDir(GameObject root, GameObject meronym, int rank)
    {
        if (root == null)
            return transform.forward;

        Transform t = root.transform;
        Vector3[] cand = { t.right, -t.right, t.forward, -t.forward };

        Vector3 offDir = t.forward;
        if (meronym != null && EnvironmentSolidCollider.TryGetVisibleBounds(root.transform, out Bounds rb))
        {
            Vector3 off = meronym.transform.position - rb.center;
            off.y = 0f;
            if (off.sqrMagnitude > 1e-4f)
                offDir = off.normalized;
        }

        // Order the four faces by alignment with the meronym offset (most aligned = nearest edge first).
        int[] order = { 0, 1, 2, 3 };
        for (int i = 0; i < 4; i++)
            for (int j = i + 1; j < 4; j++)
                if (Vector3.Dot(cand[order[j]], offDir) > Vector3.Dot(cand[order[i]], offDir))
                {
                    int tmp = order[i];
                    order[i] = order[j];
                    order[j] = tmp;
                }

        Vector3 d = cand[order[((rank % 4) + 4) % 4]];
        d.y = 0f;
        return d.sqrMagnitude > 1e-6f ? d.normalized : transform.forward;
    }

    float GetPressArriveSlack(ActionSequenceStep step)
    {
        int retries = GetPressRetryCount(step);
        return Mathf.Max(0.008f, 0.06f - retries * PressRetryArriveSlackStepM);
    }

    float GetPressReachRetryBoost(ActionSequenceStep step, float extra = 0f)
    {
        return Mathf.Clamp01(GetPressRetryCount(step) * PressRetryReachBoostStep + extra);
    }

    void ApplyPressReachRetryBoost(ActionSequenceStep step, float extraBoost = 0f)
    {
        float boost = GetPressReachRetryBoost(step, extraBoost);
        if (handRotationManager == null)
            handRotationManager = HandRotationManager.EnsureOnAgent(gameObject);
        if (handRotationManager != null)
            handRotationManager.PressReachRetryBoost = boost;
        if (kleinFrameExecutor != null)
            kleinFrameExecutor.PressReachRetryBoost = boost;
    }

    void NudgeAgentCloserToPressTarget(ActionSequenceStep step, int attempt)
    {
        if (step == null || attempt <= 0)
            return;

        string parentId = PhysicalStepTargetResolver.ResolveObjectId(step, zoneIndex);
        GameObject meronym = ResolveMeronymPressTarget(step);
        GameObject root = FindStationRoot(parentId);
        Transform targetT = meronym != null ? meronym.transform : root != null ? root.transform : null;
        if (targetT == null)
            return;

        Vector3 contactTop = EnvironmentSolidCollider.GetTopContactPoint(targetT, transform.position);
        Vector3 to = contactTop - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 1e-6f)
            return;

        float nudge = Mathf.Min(PressRetryNudgeBaseM + (attempt - 1) * PressRetryNudgeStepM, 0.32f);
        ApplyMovementDelta(to.normalized * nudge, parentId);
    }

    bool HasAchievedPhysicalPressContact()
    {
        return kleinFrameExecutor != null && kleinFrameExecutor.PressContactAchieved;
    }

    void FinishStepDwellAndComplete(ActionSequenceStep step)
    {
        if (step == null || active == null)
            return;

        _lastStepPhysicalContactVerified = !RequiresPhysicalPressContact(step) || HasAchievedPhysicalPressContact();

        if (RequiresPhysicalPressContact(step) && !HasAchievedPhysicalPressContact())
        {
            HandlePhysicalPressMissed(step, "finish blocked — no verified meronym contact");
            return;
        }

        ResetPressRetry(step);
        _imaginalThoughtBubble?.Hide();
        ApplyPhysicalOnlyStepReward(step);
        RecordStepProducedPayload(step);
        ApplyBufferStateSideEffects(step);
        if (RagMenuController.IsMenuStep(step))
        {
            RecordSelectedMenuOption(step);
            RagMenuController.EnsureInScene()?.MarkOptionSelected(RagMenuController.GetSelectedOptionTargetId(step), zoneIndex);
            ApplyMenuHandPose(step, false);
            ResetMenuReachPose();
            activeMenuStepId = null;
            activeMenuOpened = false;
            activeMenuPressLatched = false;
            activeMenuPressWait = 0f;
            activeMenuTotalWait = 0f;
            RagMenuController.EnsureInScene()?.HideAllMenus();
            ResetMenuReachPose();
        }
        else if (IsPhysicalManualActStep(step))
        {
            ClearKleinMotorSession();
            if (!(hostPlayerMovement && kleinFrameExecutor != null))
            {
                ApplyMenuHandPose(step, false);
                ResetMenuReachPose();
            }
        }

        active.MarkStepCompleted();
        if (!_orchestratorMode)
            active.MoveToNextStep();
        dwellTimer = 0f;

        if (_orchestratorMode && !string.IsNullOrEmpty(step.stepId))
        {
            _currentOrchestratorStepId = null;
            NotifyOrchestratorStepCompleted(step.stepId);
        }

        if (!RagOrchestratorStepReporterRegistry.HasReporter)
        {
            if (isMentalAgent && runningCognitive)
                StartStepCompletionRewardFlash();
            else if (!isMentalAgent)
                StartStepCompletionRewardFlash();
        }
    }

    void ClearGoalBufferTripleSession(ActionSequenceStep step)
    {
        if (step == null) return;
        if (!GoalBufferTripleOrchestrator.IsTripleModeStep(step)) return;
        if (step.isStepCompleted) return;
        goalBufferTripleSessionStepId = null;
        goalBufferTripleSlotOrdinal = 0;
        goalBufferTripleSlotTimer = 0f;
    }

    void UpdateImaginalThoughtBubbleWhileDwelling(ActionSequenceStep step)
    {
        if (!isMentalAgent || !runningCognitive || step == null)
        {
            _imaginalThoughtBubble?.Hide();
            return;
        }

        if (GoalBufferTripleOrchestrator.IsTripleModeStep(step))
        {
            _imaginalThoughtBubble?.Hide();
            return;
        }

        if (!ImaginalBufferStepHelper.IsImaginalBufferStep(step))
        {
            _imaginalThoughtBubble?.Hide();
            return;
        }

        if (_imaginalThoughtBubble == null)
            _imaginalThoughtBubble = ImaginalThoughtBubble.GetOrCreate(transform);
        _imaginalThoughtBubble.Show(BuildImaginalBubbleText(step));
    }

    /// <summary>Returns true if triple flow consumed this frame (keep updating station).</summary>
    bool ProcessGoalBufferTripleAtStation(ActionSequenceStep step)
    {
        if (!string.Equals(goalBufferTripleSessionStepId, step.stepId, StringComparison.Ordinal))
        {
            goalBufferTripleSessionStepId = step.stepId;
            goalBufferTripleSlotOrdinal = 0;
            goalBufferTripleSlotTimer = 0f;

            ZoneDeclarativeMemory memInit = ZoneDeclarativeMemory.ForZone(zoneIndex);
            memInit?.ClearGoalBufferTripleExtractions();
            GoalBufferDesireUtility.TryResolveDesire(step, memInit, out float rd, out string src);
            if (memInit != null)
                memInit.SetGoalBufferResolvedDesire(step.stepId, rd, src);

            GoalBufferStationPresenter presInit = GoalBufferStationPresenter.TryGetForZone(zoneIndex);
            presInit?.ApplyPreviewForContract(step.goalBufferContract);
        }

        while (goalBufferTripleSlotOrdinal < 3
               && GoalBufferTripleOrchestrator.GetSlotText(step.goalBufferContract, (GoalBufferSlot)goalBufferTripleSlotOrdinal) == null)
        {
            goalBufferTripleSlotOrdinal++;
        }

        if (goalBufferTripleSlotOrdinal >= 3)
        {
            FinalizeGoalBufferTripleStep(step);
            return true;
        }

        goalBufferTripleSlotTimer += Time.deltaTime;
        float need = GoalBufferTripleOrchestrator.PerSlotDwellSeconds(step, step.goalBufferContract);
        need = Mathf.Clamp(need, minDwellSeconds, maxDwellSeconds);
        if (goalBufferTripleSlotTimer < need)
            return true;

        ZoneDeclarativeMemory mem = ZoneDeclarativeMemory.ForZone(zoneIndex);
        GoalBufferSlot slot = (GoalBufferSlot)goalBufferTripleSlotOrdinal;
        string text = GoalBufferTripleOrchestrator.GetSlotText(step.goalBufferContract, slot);
        if (text != null)
        {
            GoalBufferTripleOrchestrator.WriteSlotToZoneMemory(mem, step.stepId, slot, text);
            GoalBufferStationPresenter pres = GoalBufferStationPresenter.TryGetForZone(zoneIndex);
            pres?.PlayGrab(slot);
            if (walkAnim != null)
                walkAnim.PlayGoalBufferGrabPulse();
        }

        goalBufferTripleSlotOrdinal++;
        goalBufferTripleSlotTimer = 0f;

        while (goalBufferTripleSlotOrdinal < 3
               && GoalBufferTripleOrchestrator.GetSlotText(step.goalBufferContract, (GoalBufferSlot)goalBufferTripleSlotOrdinal) == null)
        {
            goalBufferTripleSlotOrdinal++;
        }

        if (goalBufferTripleSlotOrdinal >= 3)
            FinalizeGoalBufferTripleStep(step);

        return true;
    }

    void FinalizeGoalBufferTripleStep(ActionSequenceStep step)
    {
        OnStepArrived(step);
        ApplyPhysicalOnlyStepReward(step);
        RecordStepProducedPayload(step);
        ApplyBufferStateSideEffects(step);
        active.MarkStepCompleted();
        if (!_orchestratorMode)
            active.MoveToNextStep();
        dwellTimer = 0f;
        goalBufferTripleSessionStepId = null;
        goalBufferTripleSlotOrdinal = 0;
        goalBufferTripleSlotTimer = 0f;

        // Goal Buffer triple steps complete through this special path instead of the
        // normal dwell block, so notify the DAG executor here as well.
        if (_orchestratorMode && !string.IsNullOrEmpty(step.stepId))
        {
            _currentOrchestratorStepId = null;
            NotifyOrchestratorStepCompleted(step.stepId);
        }

        if (!RagOrchestratorStepReporterRegistry.HasReporter)
        {
            if (isMentalAgent && runningCognitive)
                StartStepCompletionRewardFlash();
            else if (!isMentalAgent)
                StartStepCompletionRewardFlash();
        }
    }

    Coroutine _stepRewardFlashRoutine;

    /// <summary>
    /// White flash on successful step completion — mental (cognitive) and physical (operational) RAG agents.
    /// Works with dynamically generated humanoids (all child renderers).
    /// </summary>
    void StartStepCompletionRewardFlash()
    {
        CognitiveProcessManager.EnsureExists();
        if (_stepRewardFlashRoutine != null)
            StopCoroutine(_stepRewardFlashRoutine);
        _stepRewardFlashRoutine = StartCoroutine(CoStepCompletionRewardFlash());
    }

    public void PlayNetworkStepCompletionFlash()
    {
        StartStepCompletionRewardFlash();
    }

    System.Collections.IEnumerator CoStepCompletionRewardFlash()
    {
        float sec = CognitiveProcessManager.Instance != null
            ? CognitiveProcessManager.Instance.rewardWhiteFlashSeconds
            : 2f;

        Renderer[] rends = GetComponentsInChildren<Renderer>(true);
        if (rends == null || rends.Length == 0)
        {
            _stepRewardFlashRoutine = null;
            yield break;
        }

        Color[] saved = new Color[rends.Length];
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] != null && rends[i].material != null)
                saved[i] = rends[i].material.color;
        }

        foreach (Renderer r in rends)
        {
            if (r == null || r.material == null) continue;
            r.material.color = Color.white;
        }

        yield return new WaitForSeconds(sec);

        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] != null && rends[i].material != null)
                rends[i].material.color = saved[i];
        }

        _stepRewardFlashRoutine = null;
    }

    /// <summary>
    /// Credit <see cref="ActionSequenceStep.correct_step_reward"/> to the zone physical agent only
    /// (mental mover credits Pz; physical mover credits its own P id).
    /// </summary>
    void ApplyPhysicalOnlyStepReward(ActionSequenceStep step)
    {
        if (step == null) return;

        if (_skillCache == null)
            _skillCache = FindObjectOfType<SkillBasedActionSystem>();

        if (_skillCache == null)
        {
            Debug.LogWarning($"[RagMover] {agentId}: SkillBasedActionSystem not found — reward lost for step {step?.stepId}");
            return;
        }

        string physicalRewardId;
        if (isMentalAgent)
        {
            physicalRewardId = _skillCache.GetPhysicalAgentIdForZone(zoneIndex);
            if (string.IsNullOrEmpty(physicalRewardId))
            {
                Debug.LogWarning($"[RagMover] {agentId}: No physical agent profile for zoneIndex={zoneIndex} — reward lost for step {step.stepId}");
                return;
            }
            Debug.Log($"[RagMover] 🧠 Mental {agentId} (zone {zoneIndex}) step '{step.stepId}' → +1 to {physicalRewardId}");
        }
        else
        {
            physicalRewardId = agentId;
            Debug.Log($"[RagMover] 💪 Physical {agentId} step '{step.stepId}' +1");
        }

        RagStepRewardBridge.ApplyStandardStepReward(physicalRewardId, agentId, step, _skillCache);

        if (isMentalAgent)
            RagStepRewardBridge.ApplyCognitiveZoneMlReward(zoneIndex, step?.stepId);
    }

    /// <summary>
    /// Mark current step attempted but failed (no reward), then advance.
    /// </summary>
    public void FailCurrentStepAndContinue()
    {
        if (active == null) return;
        dwellTimer = 0f;
        active.MarkStepAttemptedFailed();
    }

    void ResolveSequences()
    {
        AgentSequenceManager mgr = AgentSequenceManager.Instance;
        if (mgr == null) return;

        cognitive   = mgr.GetCognitiveSequence(agentId);
        operational = mgr.GetSequence(agentId);
        if ((operational == null || operational.actionSequence == null || operational.actionSequence.Count == 0)
            && !isMentalAgent)
        {
            operational = mgr.GetPhysicalSequence(agentId);
        }

        bool hasCognitive   = cognitive   != null && cognitive.actionSequence   != null && cognitive.actionSequence.Count   > 0;
        bool hasOperational = operational != null && operational.actionSequence != null && operational.actionSequence.Count > 0;

        // ── Orchestrator mode (Phase 2/3 interleaved dispatch) ────────────────────────────
        // When the orchestrator is active, steps are dispatched via events rather than run linearly.
        // Phase 1 (st_0 cognitive steps) are still run through the orchestrator — it starts them first
        // because only st_0 steps satisfy AreAllDependenciesMet with an empty completedSteps set.
        if (_orchestratorMode)
        {
            // Initialize the orchestrator once we have sequence data (mental leader only).
            if (isMentalAgent && hasCognitive && !_orchestratorInitialized)
                TryInitializeOrchestrator();

            // Physical agents in orchestrator mode wait for dispatch — never poll leader flag.
            if (!isMentalAgent)
            {
                runningCognitive = false;
                if (active == null)
                    active = null; // Do nothing — dispatch event will activate steps
                return;
            }

            // Mental agent: if no active step is executing, just wait for dispatch.
            if (active == null)
                return;

            return;
        }

        // ── Physical agent gate (legacy / non-orchestrator) ───────────────────────────────
        // ALL non-mental agents must ALWAYS wait for their zone's cognitive phase.
        // This is enforced by isMentalAgent alone — never bypassed by an empty mentalLeaderAgentId.
        if (!isMentalAgent)
        {
            runningCognitive = false;

            RagSequenceAgentMover leader = GetOrFindLeaderMover();

            if (leader == null)
            {
                // Leader not spawned yet — keep waiting. Log once per 3 s to help debug.
                if (Time.time - _lastLeaderSearchLog > 3f)
                {
                    _lastLeaderSearchLog = Time.time;
                    Debug.Log($"[RagMover] {agentId} (zone {zoneIndex}) waiting — no mental leader found yet " +
                              $"(looking for '{mentalLeaderAgentId}' or any M* in zone {zoneIndex})");
                }
                active = null;
                return;
            }

            if (!leader.IsCognitivePhaseComplete)
            {
                active = null;
                return;
            }

            // Leader cognitive done — physical agent starts operational steps
            Debug.Log($"[RagMover] ✅ {agentId} (zone {zoneIndex}) — leader {leader.agentId} cognitive complete, starting physical steps.");
            active = hasOperational ? operational : null;
            return;
        }

        // ── Mental agent: cognitive first, then optional operational ─────────────────────
        if (inferenceSuppressScriptedCognitive)
        {
            runningCognitive = false;
            active = null;
            return;
        }

        if (hasCognitive && !IsSequenceCompleted(cognitive))
        {
            runningCognitive = true;
            active = cognitive;
            return;
        }

        runningCognitive = false;

        // Set the flag that unblocks zone physical agents.
        if (!hasCognitive || cognitive.actionSequence == null || cognitive.actionSequence.Count == 0)
            TryMarkZoneCognitiveReadyForPhysical("no cognitive sequence");
        else if (IsSequenceCompleted(cognitive))
            TryMarkZoneCognitiveReadyForPhysical("cognitive sequence completed");

        // Physical agents: leader cognitive done → run operational. Mental agents: idle after cognitive
        // (operational is often a shallow mirror of cognitive and would duplicate the station walk).
        active = !isMentalAgent && hasOperational ? operational : null;
    }

    private float _lastLeaderSearchLog = -999f;
    private float _leaderScanCooldown  = 0f;
    private float _resolveThrottle     = 0f;   // only re-run ResolveSequences every 0.1s while waiting

    /// <summary>
    /// Finds the zone mental leader mover dynamically:
    /// 1. Return cached reference if still valid.
    /// 2. Try by explicit <see cref="mentalLeaderAgentId"/> GameObject name.
    /// 3. Fallback: scan all RagSequenceAgentMovers for one that is mental and shares our zoneIndex.
    ///    (scan throttled to once per second so it never blocks Update)
    /// </summary>
    RagSequenceAgentMover GetOrFindLeaderMover()
    {
        // Cache still valid
        if (_leaderMoverCache != null && _leaderMoverCache.isMentalAgent && _leaderMoverCache.zoneIndex == zoneIndex)
            return _leaderMoverCache;

        _leaderMoverCache = null;

        // Try by explicit leader id (O(1) name lookup)
        if (!string.IsNullOrEmpty(mentalLeaderAgentId))
        {
            GameObject go = GameObject.Find($"Agent_{mentalLeaderAgentId}");
            if (go != null)
            {
                var m = go.GetComponent<RagSequenceAgentMover>();
                if (m != null && m.isMentalAgent && m.zoneIndex == zoneIndex)
                {
                    _leaderMoverCache = m;
                    return _leaderMoverCache;
                }
            }
        }

        // Fallback zone scan — only if cooldown elapsed (avoids per-frame cost)
        if (Time.time < _leaderScanCooldown) return null;
        _leaderScanCooldown = Time.time + 1f;

        RagSequenceAgentMover[] all = FindObjectsOfType<RagSequenceAgentMover>();
        foreach (RagSequenceAgentMover m in all)
        {
            if (m == this) continue;
            if (!m.isMentalAgent) continue;
            if (m.zoneIndex != zoneIndex) continue;
            _leaderMoverCache = m;
            if (string.IsNullOrEmpty(mentalLeaderAgentId))
                mentalLeaderAgentId = m.agentId;   // cache for future name-based lookup
            Debug.Log($"[RagMover] {agentId} found zone {zoneIndex} mental leader via scan: {m.agentId}");
            return _leaderMoverCache;
        }

        return null;
    }

    /// <summary>
    /// RAG-only scenes often have no <see cref="ZoneDeclarativeMemory"/>; <see cref="BSGMLAgent"/> uses this
    /// instead of <c>zoneMemory.cognitiveReady</c> so physical agents still wait for the mental leader's cognitive pass.
    /// </summary>
    public bool LeaderCognitiveGateAllowsPhysicalStart()
    {
        if (isMentalAgent)
            return true;
        if (!gateOperationalOnLeaderCognitive)
            return true;

        RagSequenceAgentMover leader = GetOrFindLeaderMover();
        if (leader != null && leader.IsCognitivePhaseComplete)
            return true;

        CognitivePhaseOrchestrator orch = CognitivePhaseOrchestrator.GetOrCreateForZone(zoneIndex);
        if (orch != null && orch.IsCognitivePhaseComplete)
            return true;

        ZoneDeclarativeMemory mem = ZoneDeclarativeMemory.ForZone(zoneIndex);
        if (mem == null)
        {
            ZoneDeclarativeMemory.RebuildRegistryFromScene();
            mem = ZoneDeclarativeMemory.ForZone(zoneIndex);
        }

        return mem != null && mem.cognitiveReady;
    }

    /// <summary>Zone leader (M1–M4 per zone) finished cognitive phase — unblocks physical agents and orchestrator execute phase.</summary>
    public void TryMarkZoneCognitiveReadyForPhysical(string reason)
    {
        if (!isMentalAgent) return;

        RagSequenceAgentMover leader = GetOrFindLeaderMover();
        if (leader != null && leader != this)
            return;

        if (leader == null && !IsLikelyZoneMentalLeader())
            return;

        bool deferEarlyUnlock = RagInferenceSceneController.DeferPhysicalUntilLeaderCognitiveDone();
        bool leaderSequenceDone = reason != null && (
            reason.IndexOf("cognitive sequence", StringComparison.OrdinalIgnoreCase) >= 0
            || reason.IndexOf("orchestrator all steps", StringComparison.OrdinalIgnoreCase) >= 0
            || reason.IndexOf("no cognitive sequence", StringComparison.OrdinalIgnoreCase) >= 0);

        if (deferEarlyUnlock && !leaderSequenceDone)
            return;

        IsCognitivePhaseComplete = true;

        ZoneDeclarativeMemory mem = ZoneDeclarativeMemory.ForZone(zoneIndex);
        if (mem == null)
        {
            ZoneDeclarativeMemory.RebuildRegistryFromScene();
            mem = ZoneDeclarativeMemory.ForZone(zoneIndex);
        }

        if (mem != null && !mem.cognitiveReady)
        {
            mem.cognitiveReady = true;
            Debug.Log($"[RagMover] Zone {zoneIndex} cognitiveReady=true ({reason}) — physical ONNX may start (leader {agentId}).");
        }

        CognitivePhaseOrchestrator orch = CognitivePhaseOrchestrator.GetOrCreateForZone(zoneIndex);
        if (orch != null && !orch.IsCognitivePhaseComplete)
        {
            if (RagOrchestratorStepReporterRegistry.HasReporter)
                RagOrchestratorStepReporterRegistry.ReportCognitivePhaseComplete(zoneIndex);
            else
            {
                orch.NotifyCognitivePhaseCompleteForPhysicalUnlock();
                Debug.Log($"[RagMover] Zone {zoneIndex} orchestrator evaluate physical steps ({reason}).");
            }
        }
    }

    bool IsLikelyZoneMentalLeader()
    {
        if (!isMentalAgent) return false;
        if (!string.IsNullOrEmpty(mentalLeaderAgentId))
            return string.Equals(agentId, mentalLeaderAgentId, StringComparison.OrdinalIgnoreCase);
        string expected = $"M{Mathf.Clamp(zoneIndex, 0, 3) + 1}";
        return string.Equals(agentId, expected, StringComparison.OrdinalIgnoreCase);
    }

    void OnStepArrived(ActionSequenceStep step)
    {
        if (step == null) return;

        if (isMentalAgent && RagOrchestratorStepReporterRegistry.HasReporter)
            RagOrchestratorStepReporterRegistry.ReportMentalStepActivated(zoneIndex, step.stepId);

        if (!string.IsNullOrWhiteSpace(step.actionVerb))
        {
            ShowVerbLabel(step.actionVerb, Mathf.Clamp(step.expectedDuration, minDwellSeconds, maxDwellSeconds));
            if (!string.IsNullOrWhiteSpace(step.targetObjectId))
                StartCoroutine(FlashTarget(step.targetObjectId, VerbToColor(step.actionVerb), 0.4f));
        }

        RecordDeclarativeTargetObservation(step);
    }

    void RecordDeclarativeTargetObservation(ActionSequenceStep step)
    {
        if (step == null || string.IsNullOrWhiteSpace(step.targetObjectId)) return;

        GameObject target = FindTargetObjectForMetadata(step.targetObjectId);
        DeclarativeObjectMetadata metadata = target != null
            ? target.GetComponentInParent<DeclarativeObjectMetadata>()
            : null;

        DeclarativeThreadMatchResult match = DeclarativeObjectMetadata.CompareExpected(
            target,
            step.targetObjectId,
            null);

        string thread = metadata != null ? metadata.semanticThread : string.Empty;
        string state = metadata != null ? metadata.currentState : string.Empty;
        string source = metadata != null ? metadata.metadataSource : "missing";
        string fact = $"target={step.targetObjectId}; observedThread={thread}; state={state}; source={source}; match={match.isMatch}; quality={match.quality}; reason={match.reason}";

        ZoneDeclarativeMemory mem = ZoneDeclarativeMemory.ForZone(zoneIndex);
        if (mem == null)
        {
            ZoneDeclarativeMemory.RebuildRegistryFromScene();
            mem = ZoneDeclarativeMemory.ForZone(zoneIndex);
        }
        mem?.RecordCognitiveStep(step.stepId, "declarative_target_observation", fact);

        Debug.Log($"[RagMover] Declarative target observation: {fact}");
    }

    GameObject FindTargetObjectForMetadata(string targetObjectId)
    {
        if (string.IsNullOrWhiteSpace(targetObjectId)) return null;

        GameObject target = GameObject.Find($"Tool_{targetObjectId}_zone{zoneIndex}");
        if (target == null) target = GameObject.Find($"{targetObjectId}_zone{zoneIndex}");
        if (target == null) target = GameObject.Find("Tool_" + targetObjectId);
        if (target == null) target = GameObject.Find(targetObjectId);
        return target;
    }

    void RecordStepProducedPayload(ActionSequenceStep step)
    {
        if (step == null || string.IsNullOrWhiteSpace(step.producesPayload)) return;

        ZoneDeclarativeMemory mem = ZoneDeclarativeMemory.ForZone(zoneIndex);
        if (mem == null)
        {
            ZoneDeclarativeMemory.RebuildRegistryFromScene();
            mem = ZoneDeclarativeMemory.ForZone(zoneIndex);
        }
        if (mem == null) return;

        // Production Memory writes richer command records before dispatch; do not replace them
        // with a generic description on completion.
        if (ProductionMemoryRuleEngine.IsProductionMemoryStep(step)
            && mem.TryGetPayload(step.producesPayload, out _))
        {
            return;
        }

        mem.RecordProducedPayload(step.stepId, step.producesPayload, BuildProducedPayloadValue(step));
    }

    void ApplyBufferStateSideEffects(ActionSequenceStep step)
    {
        if (step == null) return;

        ZoneDeclarativeMemory mem = ZoneDeclarativeMemory.ForZone(zoneIndex);
        if (mem == null)
        {
            ZoneDeclarativeMemory.RebuildRegistryFromScene();
            mem = ZoneDeclarativeMemory.ForZone(zoneIndex);
        }
        if (mem == null) return;

        if (ImaginalBufferStepHelper.IsImaginalBufferStep(step))
        {
            mem.RecordImaginalTransition(step);
            return;
        }

        if (ConnectionsContain(step.visualModuleConnections, "imaginal_buffer") ||
            ConnectionsContain(step.manualModuleConnections, "imaginal_buffer"))
        {
            mem.SetImaginalBufferState(step.stepId, "received", $"{step.currentCognitiveState} output");
        }
    }

    static bool ConnectionsContain(string[] connections, string value)
    {
        if (connections == null || string.IsNullOrWhiteSpace(value)) return false;
        for (int i = 0; i < connections.Length; i++)
        {
            if (string.Equals(connections[i], value, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    string BuildProducedPayloadValue(ActionSequenceStep step)
    {
        if (step == null) return "";

        if (!string.IsNullOrWhiteSpace(step.description))
            return step.description;

        if (!string.IsNullOrWhiteSpace(step.currentCognitiveState))
        {
            string target = !string.IsNullOrWhiteSpace(step.targetObjectName)
                ? step.targetObjectName
                : step.targetObjectId;
            return string.IsNullOrWhiteSpace(target)
                ? $"{step.currentCognitiveState} complete"
                : $"{step.currentCognitiveState} complete at {target}";
        }

        return string.IsNullOrWhiteSpace(step.stepId) ? "step complete" : $"{step.stepId} complete";
    }

    string BuildImaginalBubbleText(ActionSequenceStep step)
    {
        if (step == null) return "Imagining...";
        if (!string.IsNullOrWhiteSpace(step.imaginalThoughtText))
            return step.imaginalThoughtText;

        string desc = !string.IsNullOrWhiteSpace(step.description)
            ? step.description
            : "Imagining next step";

        string transition = "";
        if (!string.IsNullOrWhiteSpace(step.imaginalStateBefore) || !string.IsNullOrWhiteSpace(step.imaginalStateAfter))
            transition = $"{step.imaginalStateBefore} → {step.imaginalStateAfter}";

        string payload = !string.IsNullOrWhiteSpace(step.producesPayload)
            ? $"Output: {step.producesPayload}"
            : "";

        if (!string.IsNullOrWhiteSpace(transition) && !string.IsNullOrWhiteSpace(payload))
            return $"{desc}\n{transition}\n{payload}";
        if (!string.IsNullOrWhiteSpace(transition))
            return $"{desc}\n{transition}";
        if (!string.IsNullOrWhiteSpace(payload))
            return $"{desc}\n{payload}";
        return desc;
    }

    static Color VerbToColor(string verb)
    {
        if (string.IsNullOrWhiteSpace(verb)) return Color.yellow;
        switch (verb.ToLowerInvariant())
        {
            case "examine": return new Color(0.16f, 0.50f, 0.73f); // blue
            case "type":    return new Color(0.90f, 0.49f, 0.13f); // orange
            case "click":   return new Color(0.90f, 0.49f, 0.13f); // orange
            case "verify":  return new Color(0.15f, 0.68f, 0.38f); // green
            case "navigate":return new Color(0.15f, 0.68f, 0.38f); // green
            default:        return Color.yellow;
        }
    }

    IEnumerator FlashTarget(string targetObjectId, Color flashColor, float duration)
    {
        // Use position cache for fast lookup — avoid repeated scene traversal
        GameObject target = null;
        if (_targetPosCache.ContainsKey(targetObjectId))
        {
            target = GameObject.Find($"Tool_{targetObjectId}_zone{zoneIndex}");
            if (target == null) target = GameObject.Find("Tool_" + targetObjectId);
            if (target == null) target = GameObject.Find(targetObjectId);
        }
        if (target == null) yield break;

        Renderer rend = target.GetComponentInChildren<Renderer>();
        if (rend == null) yield break;

        Color original = rend.material.color;
        rend.material.color = flashColor;
        yield return new WaitForSeconds(duration);
        if (rend != null) rend.material.color = original;
    }

    void ShowVerbLabel(string verb, float duration)
    {
        Transform labelTf = transform.Find("AgentIdLabel");
        if (labelTf == null) return;
        TextMesh tm = labelTf.GetComponent<TextMesh>();
        if (tm == null) return;
        StartCoroutine(SwapLabel(tm, verb.ToUpper(), duration));
    }

    IEnumerator SwapLabel(TextMesh tm, string tempText, float duration)
    {
        string original = tm.text;
        tm.text = tempText;
        yield return new WaitForSeconds(duration);
        if (tm != null) tm.text = original;
    }

    bool IsSequenceCompleted(AgentSequenceData seq)
    {
        if (seq == null || seq.actionSequence == null || seq.actionSequence.Count == 0) return true;
        for (int i = 0; i < seq.actionSequence.Count; i++)
        {
            if (!seq.actionSequence[i].isStepCompleted) return false;
        }
        return true;
    }

    /// <summary>
    /// Returns true if <paramref name="targetObjectId"/> is a cognitive station.
    /// Queries <see cref="SceneGenerator.IsKnownCognitiveStation"/> (data-driven, checks
    /// <c>isCognitiveStation</c> flag in loaded scene) and falls back to the project-wide
    /// "cognitive_" naming convention when no scene data is available.
    /// </summary>
    static bool IsCognitiveStationTarget(string targetObjectId)
    {
        return IsCognitiveStationTargetId(targetObjectId);
    }

    public static bool IsCognitiveStationTargetId(string targetObjectId)
    {
        return RagStepRoleClassifier.IsCognitiveStationTargetId(targetObjectId);
    }

    Vector3? ResolveTargetPosition(string targetObjectId)
    {
        if (string.IsNullOrWhiteSpace(targetObjectId))
            return null;

        if (RagMenuController.IsMenuOptionId(targetObjectId))
        {
            string runtimeName = RagMenuController.GetRuntimeOptionObjectName(targetObjectId, zoneIndex);
            GameObject option = GameObject.Find(runtimeName);
            if (option != null)
            {
                _targetPosCache[targetObjectId] = option.transform.position;
                _targetMissingUntil.Remove(targetObjectId);
                return option.transform.position;
            }
        }

        // 1. Fast cache hit — positions of static scene objects don't change
        if (_targetPosCache.TryGetValue(targetObjectId, out Vector3 cached))
            return cached;

        // 2. Back-off: if a recent lookup found nothing, skip expensive resolution until cooldown expires
        if (_targetMissingUntil.TryGetValue(targetObjectId, out float retryAt) && Time.time < retryAt)
            return null;

        // 3. SceneGenerator zone-aware lookup (fastest; data-driven, no scene traversal)
        if (SceneGenerator.Instance != null)
        {
            Vector3 pos = SceneGenerator.Instance.GetTargetPositionById(targetObjectId, zoneIndex);
            if (pos != Vector3.zero)
            {
                _targetPosCache[targetObjectId] = pos;
                return pos;
            }
        }

        // 4. Zone-scoped lookup only — never resolve tools/stations from another quadrant.
        GameObject target = GameObject.Find($"Tool_{targetObjectId}_zone{zoneIndex}");
        if (target == null) target = GameObject.Find($"{targetObjectId}_zone{zoneIndex}");

        if (target != null && ZonePlayAreaBounds.WorldPositionInZone(zoneIndex, target.transform.position))
        {
            _targetPosCache[targetObjectId] = target.transform.position;
            return target.transform.position;
        }

        GameObject extra = GameObject.Find($"cognitive_{targetObjectId}_zone{zoneIndex}");
        if (extra == null) extra = GameObject.Find($"{targetObjectId}_zone{zoneIndex}");
        if (extra != null)
        {
            _targetPosCache[targetObjectId] = extra.transform.position;
            _targetMissingUntil.Remove(targetObjectId);
            return extra.transform.position;
        }

        // Cool down retries — missing ids are polled from Update / ML obs; keep backoff long.
        _targetMissingUntil[targetObjectId] = Time.time + 8f;
        return null;
    }

    /// <summary>Resolve the spawned meronym PART GameObject a physical step targets (step.target =
    /// meronym name, step.target_id = parent entity id), so the finger presses that exact part. Tries the
    /// mover's zone then zone 0 (physical env is authored for zone 0). Null if the part isn't spawned.</summary>
    static string ResolveStepMeronymName(ActionSequenceStep step)
    {
        if (step == null)
            return string.Empty;
        if (!string.IsNullOrWhiteSpace(step.physicalTarget))
            return step.physicalTarget.Trim();
        return string.Empty;
    }

    GameObject ResolveMeronymPressTarget(ActionSequenceStep step)
    {
        string meronym = ResolveStepMeronymName(step);
        if (string.IsNullOrWhiteSpace(meronym))
            return null;

        string parentId = PhysicalStepTargetResolver.ResolveObjectId(step, zoneIndex);
        if (string.IsNullOrWhiteSpace(parentId))
            parentId = ResolveEffectiveTargetObjectId(step);

        GameObject part = MeronymPartRegistry.Get(parentId, meronym, zoneIndex)
                          ?? MeronymPartRegistry.Get(parentId, meronym, 0)
                          ?? MeronymPartRegistry.GetByName(meronym, zoneIndex)
                          ?? MeronymPartRegistry.GetByName(meronym, 0);
        return part;
    }

    /// <summary>Meronym part when spawned, else the parent scene tool — used for walk goals and arrival.</summary>
    GameObject ResolveKleinApproachObject(ActionSequenceStep step)
    {
        GameObject part = ResolveMeronymPressTarget(step);
        if (part != null)
            return part;
        if (step == null)
            return null;
        return FindStationRoot(ResolveEffectiveTargetObjectId(step));
    }

    /// <summary>XZ distance from the agent to the nearest point on a target's visible footprint (meronym
    /// parts have trigger colliders only, so the padded parent nav hull must not be used for press arrival).</summary>
    static float GetVisibleFootprintDistance(Transform target, Vector3 worldPos)
    {
        if (target == null)
            return float.MaxValue;

        if (EnvironmentSolidCollider.TryGetVisibleBounds(target, out Bounds vb))
        {
            Vector3 closest = vb.ClosestPoint(worldPos);
            Vector3 a = worldPos;
            a.y = closest.y;
            return Vector3.Distance(a, closest);
        }

        return EnvironmentSolidCollider.GetHullDistance(target, worldPos);
    }

    GameObject FindStationRoot(string targetObjectId)
    {
        GameObject target = GameObject.Find($"Tool_{targetObjectId}_zone{zoneIndex}");
        if (target == null) target = GameObject.Find($"{targetObjectId}_zone{zoneIndex}");
        if (target == null) target = GameObject.Find($"cognitive_{targetObjectId}_zone{zoneIndex}");
        if (target != null && !ZonePlayAreaBounds.WorldPositionInZone(zoneIndex, target.transform.position))
            return null;
        return target;
    }

    float GetAgentCapsuleRadius()
    {
        if (agentCapsule == null)
            agentCapsule = GetComponent<CapsuleCollider>();
        if (agentCapsule == null)
            return 0.28f;
        return agentCapsule.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
    }

    float GetInteractionStandDistance()
    {
        return Mathf.Max(cognitiveInteractionStandDistance, GetAgentCapsuleRadius() + 0.22f);
    }

    /// <summary>Tight contact distance for the correct Klein press target — the agent walks until its
    /// capsule touches the station hull so the arm can physically reach onto the surface. Measured
    /// against the hull (not the station centre) in <see cref="HasArrivedAtStep"/>.</summary>
    float GetPressContactStandDistance(ActionSequenceStep step = null)
    {
        float d = GetAgentCapsuleRadius() + 0.03f;
        if (step != null)
        {
            int retries = GetPressRetryCount(step);
            d = Mathf.Max(GetAgentCapsuleRadius() + 0.004f, d - retries * PressRetryHullStepM);
        }
        return d;
    }

    float GetApproachProgressDistance(string targetObjectId, Vector3 stationCenter, float distToMoveGoal, ActionSequenceStep step = null)
    {
        GameObject root = FindStationRoot(targetObjectId);

        if (step != null && IsCorrectKleinPressTarget(step))
        {
            GameObject approachObj = ResolveMeronymPressTarget(step) ?? root;
            if (approachObj != null)
                return GetVisibleFootprintDistance(approachObj.transform, transform.position);
        }

        if (!IsCognitiveStationTarget(targetObjectId))
            return distToMoveGoal;

        if (root == null)
            return distToMoveGoal;

        return EnvironmentSolidCollider.GetHullDistance(root.transform, transform.position);
    }

    Vector3 ResolveMoveGoalPosition(string targetObjectId, Vector3 stationCenter, ActionSequenceStep step = null)
    {
        GameObject root = FindStationRoot(targetObjectId);
        if (root == null)
            return stationCenter;

        if (IsCorrectKleinPressTarget(step))
        {
            if (_groundMotor == null)
                _groundMotor = GetComponent<AgentGroundMotor>();
            if (_groundMotor != null)
            {
                // Desk nav hull blocks the capsule before small desk-top tools (mouse/keyboard); pass
                // through desk nav so the agent can stand at the visible edge of the tool.
                GameObject desk = FindStationRoot("scene_008");
                _groundMotor.pressPassThroughNavRoot = desk != null ? desk.transform : root.transform;
            }

            GameObject meronym = ResolveMeronymPressTarget(step);
            GameObject approachObj = meronym ?? root;
            float standOff = GetPressApproachStandOff(step);
            Vector3 close;
            if (meronym != null && root != null && meronym != root)
            {
                Vector3 keyTop = EnvironmentSolidCollider.GetTopContactPoint(meronym.transform, transform.position);

                // Side 0 = natural approach from wherever the agent already is (unchanged). After repeated
                // failed contacts the side index climbs and we synthesize a reference point far out along a
                // different parent face, so GetVisibleApproachTowardPoint stands the agent on THAT face.
                int sideIndex = GetPressSideIndex(step);
                Vector3 fromRef = transform.position;
                if (sideIndex >= 1)
                {
                    Vector3 dir = GetPressApproachDir(root, meronym, sideIndex - 1);
                    float span = 2f;
                    if (EnvironmentSolidCollider.TryGetVisibleBounds(root.transform, out Bounds rb))
                        span = rb.extents.magnitude + 2f;
                    fromRef = keyTop + dir * span;
                }

                close = EnvironmentSolidCollider.GetVisibleApproachTowardPoint(
                    root.transform, keyTop, fromRef, standOff);
            }
            else
            {
                close = EnvironmentSolidCollider.GetVisibleApproachPosition(
                    approachObj.transform, transform.position, standOff);
            }
            return ZonePlayAreaBounds.ClampPosition(zoneIndex, close);
        }

        if (_groundMotor != null)
            _groundMotor.pressPassThroughNavRoot = null;

        if (!EnvironmentSolidCollider.TryGetStationSolidCollider(root.transform, out _))
            return stationCenter;

        float agentR = GetAgentCapsuleRadius();
        float pad = destinationNavRelaxDistance;
        EnvironmentSolidCollider solid = root.GetComponentInChildren<EnvironmentSolidCollider>(true);
        if (solid != null)
            pad = Mathf.Max(pad, solid.approachPadding);

        Vector3 stand = EnvironmentSolidCollider.GetVisibleApproachPosition(
            root.transform, transform.position, Mathf.Max(0.12f, agentR * 0.35f + pad * 0.5f));
        return ZonePlayAreaBounds.ClampPosition(zoneIndex, stand);
    }

    /// <summary>True for the designated player at ANY physical manual-act step — close approach + Klein motor.</summary>
    bool IsCorrectKleinPressTarget(ActionSequenceStep step)
    {
        return hostPlayerMovement && IsPhysicalManualActStep(step);
    }

    bool HasArrivedAtTarget(string targetObjectId, Vector3 stationCenter, float distToMoveGoal)
    {
        GameObject root = FindStationRoot(targetObjectId);
        float standDist = GetInteractionStandDistance();

        if (root != null && EnvironmentSolidCollider.TryGetStationSolidCollider(root.transform, out _))
        {
            float hullDist = EnvironmentSolidCollider.GetHullDistance(root.transform, transform.position);
            if (hullDist <= standDist)
                return true;
        }

        if (!IsCognitiveStationTarget(targetObjectId))
            return distToMoveGoal <= reachThreshold;

        if (root != null)
        {
            float hullDist = EnvironmentSolidCollider.GetHullDistance(root.transform, transform.position);
            if (hullDist <= standDist)
                return true;
        }

        Vector3 a = transform.position;
        a.y = 0f;
        Vector3 c = stationCenter;
        c.y = 0f;
        return Vector3.Distance(a, c) <= standDist + 0.35f && distToMoveGoal <= Mathf.Max(0.5f, reachThreshold * 0.85f);
    }

    bool HasArrivedAtStep(ActionSequenceStep step, string targetObjectId, Vector3 stationCenter, float distToMoveGoal)
    {
        if (RagMenuController.IsMenuStep(step))
        {
            if (IsMenuOpenedForStep(step) && UsesScriptedHostMenuAssist())
            {
                if (distToMoveGoal <= 0.85f)
                    return true;

                if (!string.IsNullOrWhiteSpace(step.targetObjectId))
                {
                    Vector3? buttonPos = ResolveTargetPosition(step.targetObjectId);
                    if (buttonPos.HasValue)
                    {
                        Vector3 flat = transform.position - buttonPos.Value;
                        flat.y = 0f;
                        if (flat.magnitude <= HostMenuSelectionArriveDistance)
                            return true;
                    }
                }

                return distToMoveGoal <= HostMenuSelectionArriveDistance;
            }

            return distToMoveGoal <= (IsMenuOpenedForStep(step) ? 0.65f : 0.85f);
        }

        if (IsPhysicalManualActStep(step))
        {
            float arriveDist = GetPhysicalManualActArrivalDistance(step);

            // Correct Klein target: the move goal sits right at the visible face (the agent passes
            // through the padded nav box), so arrive when it reaches that close stand point.
            if (IsCorrectKleinPressTarget(step))
                return HasArrivedAtPhysicalPressTarget(step, targetObjectId, distToMoveGoal);

            Vector3 a = transform.position;
            a.y = 0f;
            Vector3 c = stationCenter;
            c.y = 0f;
            if (Vector3.Distance(a, c) <= arriveDist)
                return true;
            return distToMoveGoal <= arriveDist;
        }

        return HasArrivedAtTarget(targetObjectId, stationCenter, distToMoveGoal);
    }

    /// <summary>
    /// Step activation once the capsule is at the visible meronym/parent surface — not a loose proximity ring.
    /// JSON proximity zones steer around non-targets only; they do not gate arrival distance here.
    /// </summary>
    bool HasArrivedAtPhysicalPressTarget(ActionSequenceStep step, string targetObjectId, float distToMoveGoal)
    {
        GameObject root = FindStationRoot(targetObjectId);
        GameObject approachObj = ResolveMeronymPressTarget(step) ?? root;
        if (approachObj == null)
            return distToMoveGoal <= GetPhysicalManualActArrivalDistance(step);

        float arriveSlack = GetPressArriveSlack(step);
        float tight = GetPressContactStandDistance(step) + arriveSlack;

        // On a side-switch retry (sideIndex >= 1) the agent must reach the NEW face before it counts as
        // arrived — otherwise it just presses-and-misses from the same unreachable spot forever. We can NOT
        // require the capsule centre to hit the stand point exactly (it sits ~capsule-radius off the hull and
        // can't overlap a solid parent), so instead we keep the realistic "against the hull" arrival but gate
        // it to being near the INTENDED face: the horizontal distance to this side's move goal must be within
        // a capsule-scaled tolerance. On the old face that distance is ~the object's width, so it fails there
        // and passes only once the agent has walked around. Side 0 keeps the original unconditional behavior.
        bool naturalSide = GetPressSideIndex(step) == 0;
        float sideReachTol = GetAgentCapsuleRadius() + 0.3f;
        // Give up circling (press in place) once the relocation timer expires, so an object the agent can't
        // walk around still re-arrives and keeps retrying instead of stranding with the step de-activated.
        bool atIntendedFace = naturalSide || distToMoveGoal <= sideReachTol || PressRelocateGaveUp(step);

        // Meronym on a parent: arrive when the capsule is against the parent's visible hull (not the
        // inflated nav box). The arm/spine IK reaches onto the meronym from there.
        if (atIntendedFace && root != null && approachObj != root)
        {
            float rootFootprint = GetVisibleFootprintDistance(root.transform, transform.position);
            if (rootFootprint <= tight)
                return true;
        }

        if (atIntendedFace)
        {
            float footprint = GetVisibleFootprintDistance(approachObj.transform, transform.position);
            if (footprint <= tight)
                return true;
        }

        if (distToMoveGoal <= GetPressApproachStandOff(step) + arriveSlack)
            return true;

        if (atIntendedFace
            && _groundMotor != null
            && _groundMotor.LastMoveBlockedFraction >= 0.65f
            && root != null
            && GetVisibleFootprintDistance(root.transform, transform.position) <= tight + 0.05f)
            return true;

        return false;
    }

    string ResolveEffectiveTargetObjectId(ActionSequenceStep step)
    {
        if (step == null)
            return string.Empty;

        if (RagMenuController.IsMenuStep(step))
        {
            if (!IsMenuOpenedForStep(step))
                return PhysicalStepTargetResolver.ResolveObjectId(step, zoneIndex);

            string selected = RagMenuController.GetSelectedOptionTargetId(step);
            return !string.IsNullOrWhiteSpace(selected)
                ? selected
                : PhysicalStepTargetResolver.ResolveObjectId(step, zoneIndex);
        }

        if (!isMentalAgent && PhysicalStepTargetResolver.IsPhysicalStep(step))
            return PhysicalStepTargetResolver.ResolveObjectId(step, zoneIndex);

        return step.targetObjectId ?? string.Empty;
    }

    bool IsMenuOpenedForStep(ActionSequenceStep step)
    {
        return step != null
               && activeMenuOpened
               && string.Equals(activeMenuStepId, step.stepId, StringComparison.Ordinal);
    }

    bool IsMenuPressRequested()
    {
        if (mlAgent == null)
            mlAgent = GetComponent<BSGMLAgent>();
        return mlAgent != null && mlAgent.IsInteractionExecuteRequested;
    }

    void RequestMenuPressDecision()
    {
        if (mlAgent == null)
            mlAgent = GetComponent<BSGMLAgent>();
        if (mlAgent == null || !mlAgent.isActiveAndEnabled) return;
        mlAgent.RequestDecision();
    }

    bool TryLatchMenuFingerPress(ActionSequenceStep step, Vector3 targetPosition)
    {
        if (UsesScriptedHostMenuAssist())
        {
            activeMenuPressWait += Time.deltaTime;
            activeMenuTotalWait += Time.deltaTime;
            ApplyMenuReachPose(targetPosition, true);
            ApplyMenuHandPose(step, true);

            if (activeMenuPressWait >= HostMenuScriptedPressSeconds)
            {
                activeMenuPressLatched = true;
                activeMenuPressWait = 0f;
                activeMenuTotalWait = 0f;
                dwellTimer = 0f;
                Debug.Log($"[RagMover] {agentId} scripted host menu press for {step.stepId}.");
                return true;
            }

            dwellTimer = 0f;
            return false;
        }

        bool pressRequested = IsMenuPressRequested();
        bool fingerClose = IsIndexFingerCloseTo(targetPosition, out float fingerDistance);
        activeMenuTotalWait += Time.deltaTime;
        ApplyMenuReachPose(targetPosition, pressRequested);

        if (activeMenuTotalWait >= MenuDemoAssistSeconds)
        {
            ApplyMenuReachPose(targetPosition, true);
            ApplyMenuHandPose(step, true);
            AddMenuTrainingReward(MenuDemoAssistPenalty);
            activeMenuPressLatched = true;
            activeMenuPressWait = 0f;
            activeMenuTotalWait = 0f;
            dwellTimer = 0f;
            Debug.LogWarning($"[RagMover] {agentId} assisted menu press for {step.stepId}; policy did not produce a close fingertip press in {MenuDemoAssistSeconds:F1}s.");
            return true;
        }

        if (!fingerClose)
        {
            ApplyMenuHandPose(step, pressRequested);
            if (pressRequested)
                AddMenuTrainingReward(MenuEarlyPressPenalty * Time.deltaTime);

            activeMenuPressWait += Time.deltaTime;
            if (activeMenuPressWait >= MenuDecisionNudgeSeconds)
            {
                RequestMenuPressDecision();
                activeMenuPressWait = 0f;
            }

            dwellTimer = 0f;
            return false;
        }

        if (!pressRequested)
        {
            ApplyMenuHandPose(step, false);
            AddMenuTrainingReward(MenuIdlePenaltyPerSecond * Time.deltaTime);

            activeMenuPressWait += Time.deltaTime;
            if (activeMenuPressWait >= MenuDecisionNudgeSeconds)
            {
                RequestMenuPressDecision();
                activeMenuPressWait = 0f;
            }

            dwellTimer = 0f;
            return false;
        }

        activeMenuPressLatched = true;
        activeMenuPressWait = 0f;
        activeMenuTotalWait = 0f;
        dwellTimer = 0f;
        AddMenuTrainingReward(IsMenuOpenedForStep(step) ? MenuCorrectSelectionReward : MenuCorrectPressReward);
        Debug.Log($"[RagMover] {agentId} menu finger press accepted for {step.stepId}; fingerDistance={fingerDistance:F2}m");
        return true;
    }

    bool IsIndexFingerCloseTo(Vector3 targetPosition, out float distance)
    {
        Vector3 fingerPosition;
        if (TryGetIndexFingerTipPosition(out fingerPosition))
        {
            distance = Vector3.Distance(fingerPosition, targetPosition);
            return distance <= MenuFingerSelectDistance;
        }

        distance = float.MaxValue;
        return false;
    }

    bool TryGetIndexFingerTipPosition(out Vector3 position)
    {
        if (handRotationManager == null)
            handRotationManager = HandRotationManager.EnsureOnAgent(gameObject);

        if (handRotationManager != null && handRotationManager.IndexFingerTip != null)
        {
            position = handRotationManager.IndexFingerTip.position;
            return true;
        }

        // Fallback keeps training scenes with older generated bodies usable while still requiring hand-space reach.
        position = transform.position + transform.forward * 0.45f + Vector3.up * 0.95f;
        return true;
    }

    void AddMenuTrainingReward(float amount)
    {
        if (Mathf.Approximately(amount, 0f)) return;
        if (mlAgent == null)
            mlAgent = GetComponent<BSGMLAgent>();
        if (mlAgent != null)
            mlAgent.AddReward(amount);
    }

    void ApplyMenuHandPose(ActionSequenceStep step, bool execute)
    {
        if (isMentalAgent) return;
        if (hostPlayerMovement && kleinFrameExecutor != null && kleinFrameExecutor.IsHoldingPose) return;
        if (handRotationManager == null)
            handRotationManager = HandRotationManager.EnsureOnAgent(gameObject);
        if (handRotationManager == null) return;
        if (handRotationManager.UsesMixamoRig) return;

        handRotationManager.ApplyFingerRotation(
            HandActionLibrary.IndexFinger,
            HandActionLibrary.GetRotationsForStep(step, execute));
    }

    void ApplyMenuReachPose(Vector3 targetPosition, bool press)
    {
        if (isMentalAgent) return;
        if (hostPlayerMovement && kleinFrameExecutor != null && kleinFrameExecutor.IsHoldingPose) return;
        if (handRotationManager == null)
            handRotationManager = HandRotationManager.EnsureOnAgent(gameObject);
        if (handRotationManager != null && handRotationManager.UsesMixamoRig && handRotationManager.ManualPoseActive) return;
        handRotationManager?.ApplyRightArmReachPose(targetPosition, press);
    }

    /// <summary>Load the Klein frames from the live RAG if they aren't already (idempotent, cheap after
    /// the first success). Covers the case where the executor bootstrapped before the RAG text was
    /// available, which would otherwise leave the catalog empty / on the legacy mannualBuffer2.json.</summary>
    void EnsureKleinFramesFromRag()
    {
        if (ManualBufferCatalog.LoadedFromRag)
            return;

        SceneUILoader loader = FindObjectOfType<SceneUILoader>();
        if (loader == null)
            return;

        string rag = !string.IsNullOrEmpty(loader.MergedRawRagJson) ? loader.MergedRawRagJson
                   : !string.IsNullOrEmpty(loader.RawJsonText) ? loader.RawJsonText
                   : loader.EffectivePipelineJson;
        if (!string.IsNullOrEmpty(rag))
            ManualBufferCatalog.LoadFromRag(rag);
    }

    bool TryDriveKleinMotorForStep(ActionSequenceStep step, Vector3 stationCenter)
    {
        if (!hostPlayerMovement || kleinFrameExecutor == null || step == null)
            return false;

        // Make sure the Klein frames came from the RAG (sceneStateLog/physicalAgents). If the executor
        // bootstrapped before SceneUILoader had the RAG text, reload from the live RAG. RAG-only — never
        // the legacy mannualBuffer2.json.
        EnsureKleinFramesFromRag();

        if (!ManualBufferCatalog.IsLoaded)
            return false;

        if (!string.Equals(kleinMotorStepId, step.stepId, StringComparison.Ordinal))
        {
            kleinMotorStepId = step.stepId;
            kleinMotorCompleted = false;
            ApplyPressReachRetryBoost(step);

            // Correct Klein target: press the exact contact point — the JSON-authored
            // scene_contact_point (Unity world space) when set, else the auto-detected TOP of the
            // station's visible mesh. Arm the Motor-style press feedback on it. Other targets keep the
            // station centre and no feedback.
            Vector3 worldTarget = stationCenter;
            // A RESTING / torso (pending-full-body) frame does nothing — it must NOT arm a press target or
            // reach for anything. Detect it up front and skip press-target resolution entirely.
            ManualBufferCatalog.TryGetForStep(step.stepId, agentId, zoneIndex, out KleinFrame kf);
            bool isRestFrame = kf != null
                && ((!string.IsNullOrEmpty(kf.manualCommand)
                        && kf.manualCommand.StartsWith("RESTING", StringComparison.OrdinalIgnoreCase))
                    || string.Equals(kf.effectorBodyPart, "torso", StringComparison.OrdinalIgnoreCase));

            if (!isRestFrame && IsCorrectKleinPressTarget(step))
            {
                GameObject root = FindStationRoot(PhysicalStepTargetResolver.ResolveObjectId(step, zoneIndex)
                                                   ?? ResolveEffectiveTargetObjectId(step));
                // Press the exact meronym PART — never the parent body (touching the mouse shell is not a button press).
                GameObject pressTarget = ResolveMeronymPressTarget(step);
                if (pressTarget == null && !string.IsNullOrWhiteSpace(ResolveStepMeronymName(step)))
                {
                    Debug.LogWarning($"[KleinPress] {step.stepId} meronym '{ResolveStepMeronymName(step)}' not spawned yet — deferring press motor.");
                    kleinMotorStepId = null;
                    return false;
                }

                pressTarget = pressTarget ?? root;
                bool fromJson = kf != null && kf.hasSceneContactPoint;

                Vector3 autoTop = pressTarget != null
                    ? EnvironmentSolidCollider.GetTopContactPoint(pressTarget.transform, transform.position)
                    : stationCenter;
                worldTarget = fromJson ? kf.sceneContactPoint : autoTop;

                kleinFrameExecutor.SetPressFeedbackTarget(pressTarget, pressTarget != null ? gameObject : null);
                Debug.Log($"[KleinPress] {step.stepId} press contact = {worldTarget} on '{(pressTarget != null ? pressTarget.name : "null")}' " +
                          $"(source: {(fromJson ? "scene_contact_point override" : "auto visible-top")}). " +
                          $"target part='{ResolveStepMeronymName(step)}' parent='{PhysicalStepTargetResolver.ResolveObjectId(step, zoneIndex)}'.");
            }
            else
            {
                kleinFrameExecutor.SetPressFeedbackTarget(null, null);
            }

            if (!kleinFrameExecutor.TryExecuteForStep(step, agentId, worldTarget, () => kleinMotorCompleted = true))
            {
                kleinMotorStepId = null;
                return false;
            }
        }

        return true;
    }

    void ClearKleinMotorSession()
    {
        kleinMotorStepId = null;
        kleinMotorCompleted = false;
        kleinFrameExecutor?.Cancel();
        if (_groundMotor != null)
            _groundMotor.pressPassThroughNavRoot = null;
    }

    /// <summary>Guarantees no Klein press pose lingers when the agent is on an act step with no
    /// resolvable RAG Klein frame. Keeps the klein hand/arm movement exclusive to steps that own a
    /// frame, even when other steps share the same target object.</summary>
    void EnsureKleinPoseCleared()
    {
        if (!hostPlayerMovement)
            return;
        if (kleinFrameExecutor != null && (kleinFrameExecutor.IsExecuting || kleinFrameExecutor.IsHoldingPose))
            kleinFrameExecutor.Cancel();
        if (!string.IsNullOrEmpty(kleinMotorStepId))
            kleinMotorStepId = null;
        if (handRotationManager != null && handRotationManager.ManualPoseActive)
            handRotationManager.ResetRightArmReachPose();
    }

    /// <summary>Late-bound by <see cref="PlayerRagPhysicalBridge"/> after Photon player bind.</summary>
    public void BindKleinFrameExecutor(KleinFrameExecutor executor, string ragText = null)
    {
        kleinFrameExecutor = executor;
        hostPlayerMovement = true;
        handRotationManager = HandRotationManager.EnsureOnAgent(gameObject);
        handRotationManager?.RefreshRigWire();
        if (kleinFrameExecutor != null && !string.IsNullOrWhiteSpace(ragText))
            kleinFrameExecutor.BootstrapFromRagText(ragText);
    }

    void ResetMenuReachPose()
    {
        if (isMentalAgent) return;
        if (handRotationManager == null)
            handRotationManager = HandRotationManager.EnsureOnAgent(gameObject);
        handRotationManager?.ResetRightArmReachPose();
    }

    void RecordSelectedMenuOption(ActionSequenceStep step)
    {
        if (!RagMenuController.IsMenuStep(step)) return;
        ZoneDeclarativeMemory mem = ZoneDeclarativeMemory.ForZone(zoneIndex);
        if (mem == null)
        {
            ZoneDeclarativeMemory.RebuildRegistryFromScene();
            mem = ZoneDeclarativeMemory.ForZone(zoneIndex);
        }
        if (mem == null) return;

        string selected = RagMenuController.GetSelectedOptionTargetId(step);
        mem.RecordCognitiveStep(step.stepId, "selected_menu_option", selected);
        mem.RecordCognitiveStep(step.stepId, "menu_step_id", step.stepId);
    }

    void FaceTowardStation(Vector3 stationCenter)
    {
        Vector3 flat = stationCenter - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.02f)
            return;
        transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
    }

    void ApplyMovementDelta(Vector3 delta, string stepTargetObjectId)
    {
        if (hostPlayerMovement)
        {
            if (_groundMotor == null)
                _groundMotor = GetComponent<AgentGroundMotor>();
            if (_groundMotor != null)
            {
                if (_groundMotor.clampZoneIndex < 0)
                    _groundMotor.clampZoneIndex = zoneIndex;
                _groundMotor.TryMoveGround(delta);
                RagPhysicalAgentCollisionFx fx = GetComponent<RagPhysicalAgentCollisionFx>();
                fx?.NotifyGroundMoveBlocked(_groundMotor.LastMoveBlockedFraction);
                return;
            }

            if (_playerMovement != null)
            {
                _playerMovement.ApplyRagGroundMovementDelta(delta);
                return;
            }
        }

        if (_groundMotor != null)
        {
            if (_groundMotor.clampZoneIndex < 0)
                _groundMotor.clampZoneIndex = zoneIndex;
            _groundMotor.TryMoveGround(delta);
            return;
        }

        if (avoidCognitiveObstacles)
            ApplyGroundMovementWithObstacleCollision(delta, stepTargetObjectId);
        else
            ApplyGroundMovementProximityOnly(delta, stepTargetObjectId);

        Vector3 clamped = ZonePlayAreaBounds.ClampPosition(zoneIndex, transform.position);
        if ((clamped - transform.position).sqrMagnitude > 1e-8f)
        {
            transform.position = clamped;
            if (rb != null)
                rb.position = clamped;
        }
    }

    void ResetNavStuckState()
    {
        _navStuckTimer = 0f;
        _navLastProgressDist = -1f;
        _navProgressCheckCooldown = 0f;
        _navEscapeRemaining = 0f;
        _navEscapeDir = Vector3.zero;
    }

    void UpdateNavStuckProgress(float distToGoal)
    {
        if (_navEscapeRemaining > 0f)
            return;

        _navProgressCheckCooldown -= Time.deltaTime;
        if (_navProgressCheckCooldown > 0f)
            return;

        _navProgressCheckCooldown = NavProgressCheckInterval;

        if (_navLastProgressDist < 0f)
        {
            _navLastProgressDist = distToGoal;
            return;
        }

        if (distToGoal > _navLastProgressDist - navMinProgressMeters)
            _navStuckTimer += NavProgressCheckInterval;
        else
            _navStuckTimer = 0f;

        _navLastProgressDist = distToGoal;
    }

    bool TryBeginNavEscape(Vector3 blockedDir, Vector3 toGoalFlat, Vector3 goalWorld, string stepTargetObjectId, out Vector3 escapeDir)
    {
        escapeDir = blockedDir;
        Vector3 b = blockedDir;
        b.y = 0f;
        if (b.sqrMagnitude < 1e-8f)
            b = transform.forward;
        b.Normalize();

        Vector3 g = toGoalFlat;
        g.y = 0f;
        if (g.sqrMagnitude < 1e-8f)
            g = b;
        else
            g.Normalize();

        float castDist = Mathf.Max(obstacleLookAhead * 1.5f, moveSpeed * 0.55f, 2.2f);
        Vector3 origin = AvoidanceCastOrigin(b);

        if (TryPickHardAlternateAvoidanceDirection(origin, b, g, castDist, stepTargetObjectId, out Vector3 hard))
        {
            escapeDir = hard;
            return true;
        }

        Vector3 flank = Vector3.Cross(Vector3.up, g).normalized;
        Vector3[] candidates = { flank, -flank, Quaternion.Euler(0f, 75f, 0f) * b, Quaternion.Euler(0f, -75f, 0f) * b };
        for (int i = 0; i < candidates.Length; i++)
        {
            Vector3 cand = candidates[i];
            cand.y = 0f;
            if (cand.sqrMagnitude < 1e-8f) continue;
            cand.Normalize();
            if (!IsHeadingBlockedByCognitive(origin, cand, castDist, stepTargetObjectId))
            {
                escapeDir = cand;
                return true;
            }
        }

        Vector3 overlap = ComputeCognitiveOverlapEscape(stepTargetObjectId);
        if (overlap.sqrMagnitude > 1e-8f)
        {
            escapeDir = overlap.normalized;
            return true;
        }

        float yaw = UnityEngine.Random.Range(82f, 108f) * (UnityEngine.Random.value > 0.5f ? 1f : -1f);
        escapeDir = (Quaternion.Euler(0f, yaw, 0f) * b).normalized;
        return true;
    }

    /// <summary>
    /// Kinematic rigidbodies do not resolve overlaps with static colliders when using MovePosition —
    /// we CapsuleCast the displacement, shorten + reflect horizontally when blocked, then resolve any
    /// penetration with Physics.ComputePenetration so agents never tunnel through CognitiveNavObstacle boxes.
    /// </summary>
    void ApplyGroundMovementWithObstacleCollision(Vector3 horizontalDelta, string stepTargetObjectId)
    {
        // Resolve existing overlap first so CapsuleCast does not start inside a nav box (Unity often lets the cast through).
        DepenetrateFromCognitiveObstacles(stepTargetObjectId);

        Vector3 xzDelta = horizontalDelta;
        xzDelta.y = 0f;
        Vector3 pos = rb != null ? rb.position : transform.position;
        Vector3 startPos = pos;

        if (IsPenetratingBlockingNavVolume(stepTargetObjectId, pos, out Vector3 overlapPush))
            xzDelta = RedirectDeltaOutOfNavOverlap(xzDelta, overlapPush, pos);

        float skin = Mathf.Max(0.02f, obstacleCollisionSkin);

        Vector3 remainder = xzDelta;
        int iterCap = Mathf.Clamp(obstacleSlideIterations, 1, 12);
        int steerOutBudget = 4;

        for (int iter = 0; iter < iterCap && remainder.sqrMagnitude > 1e-12f; iter++)
        {
            Vector3 flatDir = remainder.normalized;
            float distLeft = remainder.magnitude;

            GetCapsuleWorldEndpoints(pos, out Vector3 cp1, out Vector3 cp2, out float rad);
            rad *= Mathf.Clamp(obstacleCastRadiusMultiplier, 1f, 2f);

            // Still overlapping a blocking station (e.g. cast starts inside volume): slide sideways only.
            if (steerOutBudget > 0 && TrySteerOutOfBlockingOverlap(pos, stepTargetObjectId, ref remainder))
            {
                steerOutBudget--;
                continue;
            }

            if (!Physics.CapsuleCast(cp1, cp2, rad, flatDir, out RaycastHit hit, distLeft,
                    Physics.AllLayers, QueryTriggerInteraction.Ignore))
            {
                pos += flatDir * distLeft;
                break;
            }

            if (hit.collider != null && hit.collider.transform.root == transform.root)
            {
                pos += flatDir * distLeft;
                break;
            }

            if (!IsNavigationObstacle(hit.collider))
            {
                pos += flatDir * distLeft;
                break;
            }

            float allowed = Mathf.Max(0f, hit.distance - skin);
            // Grazing / embedded hit: force slide instead of inching forward along the same ray.
            if (allowed < 0.05f)
            {
                Vector3 n0 = hit.normal;
                n0.y = 0f;
                if (n0.sqrMagnitude > 1e-10f)
                {
                    n0.Normalize();
                    Vector3 along = Vector3.Cross(Vector3.up, n0).normalized;
                    Vector3 toGoal = _navGoalForObstacles - pos;
                    toGoal.y = 0f;
                    if (toGoal.sqrMagnitude > 1e-8f)
                    {
                        toGoal.Normalize();
                        along = Vector3.Dot(along, toGoal) >= Vector3.Dot(-along, toGoal) ? along : -along;
                    }
                    Vector3 slide = Vector3.Dot(along, remainder) >= 0f ? along : -along;
                    remainder = slide * Mathf.Max(distLeft, 0.24f);
                    continue;
                }
            }

            pos += flatDir * Mathf.Min(allowed, distLeft);

            float rest = distLeft - Mathf.Min(allowed, distLeft);
            if (rest < 1e-6f)
                break;

            Vector3 n = hit.normal;
            n.y = 0f;
            if (n.sqrMagnitude < 1e-10f)
                n = -flatDir;
            n.Normalize();

            Vector3 incoming = flatDir;
            Vector3 reflected = Vector3.ProjectOnPlane(Vector3.Reflect(incoming, n), Vector3.up);
            if (reflected.sqrMagnitude < 1e-10f)
                reflected = Vector3.Cross(Vector3.up, n);
            reflected.Normalize();

            Vector3 alongHit = Vector3.Cross(Vector3.up, n).normalized;
            Vector3 flatToNavGoal = _navGoalForObstacles - pos;
            flatToNavGoal.y = 0f;
            Vector3 peelTangent = alongHit;
            if (flatToNavGoal.sqrMagnitude > 1e-8f)
            {
                flatToNavGoal.Normalize();
                peelTangent = Vector3.Dot(alongHit, flatToNavGoal) >= Vector3.Dot(-alongHit, flatToNavGoal) ? alongHit : -alongHit;
            }
            float tb = Mathf.Clamp01(obstacleReflectTangentBlend);
            if (tb > 0.001f)
                reflected = Vector3.Normalize(Vector3.Lerp(reflected, peelTangent, tb));

            remainder = reflected * rest;
        }

        pos.y = rb != null ? rb.position.y : transform.position.y;

        // Never commit a step that newly embeds the capsule inside a solid nav box.
        if (!WasPenetratingBlockingNavVolume(stepTargetObjectId, startPos)
            && IsPenetratingBlockingNavVolume(stepTargetObjectId, pos, out _))
            pos = startPos;

        if (rb != null && rb.isKinematic)
            rb.MovePosition(pos);
        else
            transform.position = pos;

        DepenetrateFromCognitiveObstacles(stepTargetObjectId);
    }

    /// <summary>
    /// Pure translation after proximity-guided heading — optional depenetrate so kinematic agents don't stay inside nav boxes when proximity-only.
    /// </summary>
    void ApplyGroundMovementProximityOnly(Vector3 horizontalDelta, string stepTargetObjectId)
    {
        DepenetrateFromCognitiveObstacles(stepTargetObjectId);

        Vector3 d = horizontalDelta;
        d.y = 0f;

        Vector3 pos = rb != null ? rb.position : transform.position;
        Vector3 startPos = pos;

        if (IsPenetratingBlockingNavVolume(stepTargetObjectId, pos, out Vector3 overlapPush))
            d = RedirectDeltaOutOfNavOverlap(d, overlapPush, pos);

        pos += d;

        if (!WasPenetratingBlockingNavVolume(stepTargetObjectId, startPos)
            && IsPenetratingBlockingNavVolume(stepTargetObjectId, pos, out _))
            pos = startPos;
        float yKeep = rb != null ? rb.position.y : transform.position.y;
        pos.y = yKeep;

        if (rb != null && rb.isKinematic)
            rb.MovePosition(pos);
        else
            transform.position = pos;

        DepenetrateFromCognitiveObstacles(stepTargetObjectId);
    }

    /// <summary>
    /// When the capsule already overlaps a non-destination cognitive box, CapsuleCast is unreliable.
    /// Blends "shortest exit from volume" with a tangent so the next cast moves along the obstacle instead of through it.
    /// </summary>
    bool TrySteerOutOfBlockingOverlap(Vector3 assumedPos, string stepTargetObjectId, ref Vector3 remainder)
    {
        GetCapsuleWorldEndpoints(assumedPos, out Vector3 cp1, out Vector3 cp2, out float rad);
        rad *= Mathf.Clamp(obstacleCastRadiusMultiplier, 1f, 2f);

        Collider[] ov = Physics.OverlapCapsule(cp1, cp2, Mathf.Max(rad - 0.02f, 0.06f),
            Physics.AllLayers, QueryTriggerInteraction.Ignore);

        Vector3 sumExit = Vector3.zero;
        int count = 0;

        for (int i = 0; i < ov.Length; i++)
        {
            Collider o = ov[i];
            if (o == null || o.isTrigger || o.transform.root == transform.root)
                continue;
            if (!IsNavigationObstacle(o))
                continue;
            if (!NavColliderBlocksMovement(stepTargetObjectId, o, assumedPos))
                continue;

            Vector3 sample = assumedPos + Vector3.up * Mathf.Max(0.35f, obstacleProbeHeight * 0.7f);
            Vector3 closest = o.ClosestPoint(sample);
            Vector3 exitDir = closest - sample;
            exitDir.y = 0f;
            if (exitDir.sqrMagnitude < 1e-12f)
            {
                exitDir = assumedPos - o.bounds.center;
                exitDir.y = 0f;
            }

            if (exitDir.sqrMagnitude < 1e-12f)
                continue;

            sumExit += exitDir.normalized;
            count++;
        }

        if (count == 0)
            return false;

        Vector3 steer = sumExit / count;
        steer.y = 0f;
        if (steer.sqrMagnitude < 1e-12f)
            return false;
        steer.Normalize();

        Vector3 tangent = Vector3.Cross(Vector3.up, steer).normalized;
        float tw = Mathf.Clamp(obstacleOverlapSteerTangentWeight, 0f, 0.75f);
        Vector3 blended = Vector3.Normalize(steer * (1f - tw) + tangent * tw);

        remainder = blended * remainder.magnitude;
        return true;
    }

    void GetCapsuleWorldEndpoints(Vector3 assumedRootPosition, out Vector3 p1, out Vector3 p2, out float radius)
    {
        Vector3 displacement = assumedRootPosition - transform.position;
        Transform tr = transform;

        if (agentCapsule != null)
        {
            CapsuleCollider c = agentCapsule;
            Vector3 scale = tr.lossyScale;
            radius = c.radius * Mathf.Max(scale.x, scale.z);

            Vector3 center = tr.TransformPoint(c.center) + displacement;

            float extent = Mathf.Max(0.05f, c.height * scale.y * 0.5f - radius);

            Vector3 localAxis = Vector3.up;
            if (c.direction == 0) localAxis = Vector3.right;
            else if (c.direction == 2) localAxis = Vector3.forward;

            Vector3 worldAxis = tr.TransformDirection(localAxis).normalized;

            p1 = center - worldAxis * extent;
            p2 = center + worldAxis * extent;
            return;
        }

        radius = 0.42f * Mathf.Max(tr.lossyScale.x, tr.lossyScale.z);
        Vector3 mid = assumedRootPosition + Vector3.up * Mathf.Max(0.85f * tr.lossyScale.y, 0.6f);
        float half = Mathf.Max(0.72f * tr.lossyScale.y - radius, 0.14f);
        p1 = mid - Vector3.up * half;
        p2 = mid + Vector3.up * half;
    }

    void DepenetrateFromCognitiveObstacles(string stepTargetObjectId)
    {
        if (agentCapsule == null)
            return;

        int passes = Mathf.Clamp(obstacleDepenetratePasses, 1, 14);
        for (int pass = 0; pass < passes; pass++)
        {
            GetCapsuleWorldEndpoints(transform.position, out Vector3 cp1, out Vector3 cp2, out float rad);

            Collider[] ov = Physics.OverlapCapsule(cp1, cp2, Mathf.Max(rad - 0.02f, 0.06f),
                Physics.AllLayers, QueryTriggerInteraction.Ignore);

            Vector3 correction = Vector3.zero;
            int hits = 0;

            for (int i = 0; i < ov.Length; i++)
            {
                Collider other = ov[i];
                if (other == null || other.isTrigger || other.transform.root == transform.root)
                    continue;
                if (!IsNavigationObstacle(other))
                    continue;
                if (!NavColliderBlocksMovement(stepTargetObjectId, other, transform.position))
                    continue;

                if (!Physics.ComputePenetration(agentCapsule, transform.position, transform.rotation,
                        other, other.transform.position, other.transform.rotation,
                        out Vector3 dir, out float pushDist))
                    continue;

                // Minimal translation that separates agent (A) from obstacle (B): translate A by direction * distance.
                Vector3 xz = Vector3.ProjectOnPlane(dir * pushDist, Vector3.up);
                correction += xz;
                hits++;
            }

            if (hits == 0)
                break;

            Vector3 np = transform.position + correction / hits;
            np.y = transform.position.y;

            transform.position = np;
            if (rb != null)
                rb.position = np;
        }
    }

    /// <summary>
    /// Skirts <see cref="CognitiveStationInteractable"/> volumes (SceneGenerator adds <c>CognitiveNavObstacle</c>).
    /// Uses overlap escape + wide arc sampling so agents do not stall head-on against boxes.
    /// Does not deflect around the current step destination so the agent can still reach station centers.
    /// </summary>
    Vector3 ComputeSteeredGroundDirection(Vector3 desiredFlat, Vector3 goalWorld, string stepTargetObjectId)
    {
        Vector3 d = desiredFlat;
        d.y = 0f;
        if (d.sqrMagnitude < 1e-8f) return desiredFlat;
        d.Normalize();

        Vector3 flatToGoal = goalWorld - transform.position;
        flatToGoal.y = 0f;
        Vector3 goalDir = flatToGoal.sqrMagnitude > 1e-8f ? flatToGoal.normalized : d;

        // Primary: proximity rings from RAG JSON — reflect around wrong stations; allow full approach to current target.
        if (_proximitySteeringCache == null)
            _proximitySteeringCache = FindObjectOfType<ProximityDetectionSystem>();

            if (useProximityCognitiveSteering && proximitySteerAggression > 0.001f &&
            _proximitySteeringCache != null && !string.IsNullOrWhiteSpace(stepTargetObjectId))
        {
            d = _proximitySteeringCache.BlendMovementAwayFromNonTargetCognitiveZones(
                transform.position,
                stepTargetObjectId,
                d,
                goalDir,
                proximitySteerAggression,
                proximityStationHullPadding,
                proximityApproachBand,
                zoneIndex);
            d.y = 0f;
            if (d.sqrMagnitude > 1e-8f) d.Normalize();
        }

        if (useProximityEnvironmentSteering && proximitySteerAggression > 0.001f &&
            _proximitySteeringCache != null && !string.IsNullOrWhiteSpace(stepTargetObjectId))
        {
            d = _proximitySteeringCache.BlendMovementAwayFromNonTargetEnvironmentObstacles(
                transform.position,
                stepTargetObjectId,
                d,
                goalDir,
                proximitySteerAggression,
                environmentSteerHullPadding,
                environmentSteerApproachBand,
                zoneIndex);
            d.y = 0f;
            if (d.sqrMagnitude > 1e-8f) d.Normalize();
        }

        float castDist = Mathf.Max(obstacleLookAhead, moveSpeed * 0.45f, 1.35f);

        // Proximity-only: overlap + short cast — prefer wide hard yaws over shallow fan nudges.
        if (!avoidCognitiveObstacles)
        {
            Vector3 escapeOnly = ComputeCognitiveOverlapEscape(stepTargetObjectId);
            if (escapeOnly.sqrMagnitude > 1e-8f)
            {
                escapeOnly.y = 0f;
                return escapeOnly.normalized;
            }

            float cd = Mathf.Max(castDist * 0.92f, moveSpeed * Time.deltaTime * 3.5f);
            Vector3 originP = AvoidanceCastOrigin(d);
            if (Physics.SphereCast(originP, obstacleProbeRadius, d, out RaycastHit hitP, cd,
                    Physics.AllLayers, QueryTriggerInteraction.Ignore)
                && IsNavigationObstacle(hitP.collider)
                && NavColliderBlocksMovement(stepTargetObjectId, hitP.collider, transform.position))
            {
                if (TryPickHardAlternateAvoidanceDirection(originP, d, goalDir, cd, stepTargetObjectId, out Vector3 hardP))
                    return hardP;

                if (TryPickFanAvoidanceDirection(originP, d, goalDir, cd, stepTargetObjectId, out Vector3 fanP))
                    return fanP;
            }

            return d;
        }

        // Physics nav boxes — overlap escape first, then cast / fan / tangential slide.
        // If we're already intersecting a blocking station collider, step outward first (fixes "stuck in place").
        Vector3 escape = ComputeCognitiveOverlapEscape(stepTargetObjectId);
        if (escape.sqrMagnitude > 1e-8f)
        {
            escape.y = 0f;
            return escape.normalized;
        }

        Vector3 origin = AvoidanceCastOrigin(d);

        if (!Physics.SphereCast(origin, obstacleProbeRadius, d, out RaycastHit hit, castDist,
                Physics.AllLayers, QueryTriggerInteraction.Ignore))
            return d;

        if (!IsNavigationObstacle(hit.collider))
            return d;

        if (!NavColliderBlocksMovement(stepTargetObjectId, hit.collider, transform.position))
            return d;

        // Another station blocks the lane — try ±90°/±135° first so we do not orbit with tiny fan tweaks.
        if (TryPickHardAlternateAvoidanceDirection(origin, d, goalDir, castDist, stepTargetObjectId, out Vector3 hardFirst))
            return hardFirst;

        if (TryPickFanAvoidanceDirection(origin, d, goalDir, castDist, stepTargetObjectId, out Vector3 fanDir))
            return fanDir;

        if (TryPickHardAlternateAvoidanceDirection(origin, d, goalDir, castDist * 1.25f, stepTargetObjectId, out Vector3 hardNoFan))
            return hardNoFan;

        Vector3 n = hit.normal;
        n.y = 0f;
        if (n.sqrMagnitude < 1e-8f)
            return d;
        n.Normalize();

        Vector3 tangent = Vector3.Cross(Vector3.up, n).normalized;
        Vector3 slide = Vector3.Dot(tangent, flatToGoal) >= Vector3.Dot(-tangent, flatToGoal) ? tangent : -tangent;

        // Strong tangential bias + partial goal pull so we don't scrape along the face forever.
        float goalMix = Mathf.Clamp01(obstacleSlideGoalPull);
        Vector3 mixed = Vector3.Normalize(slide * obstacleDeflectStrength + goalDir * (1f - obstacleDeflectStrength) * goalMix);

        if (Physics.SphereCast(origin, obstacleProbeRadius * 0.9f, mixed, out RaycastHit hit2,
                castDist * 0.9f, Physics.AllLayers, QueryTriggerInteraction.Ignore)
            && IsNavigationObstacle(hit2.collider)
            && NavColliderBlocksMovement(stepTargetObjectId, hit2.collider, transform.position))
        {
            mixed = slide;
        }

        mixed.y = 0f;
        return mixed.sqrMagnitude > 1e-8f ? mixed.normalized : slide;
    }

    Vector3 AvoidanceCastOrigin(Vector3 flatDirection)
    {
        Vector3 f = flatDirection;
        f.y = 0f;
        if (f.sqrMagnitude < 1e-8f) f = transform.forward;
        f.Normalize();
        return transform.position + Vector3.up * obstacleProbeHeight + f * obstacleCastForwardBias;
    }

    Vector3 ComputeCognitiveOverlapEscape(string stepTargetObjectId)
    {
        Vector3 center = transform.position + Vector3.up * (obstacleProbeHeight * 0.65f);
        float r = obstacleProbeRadius + obstacleOverlapExtraRadius;
        Collider[] cols = Physics.OverlapSphere(center, r, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        if (cols == null || cols.Length == 0) return Vector3.zero;

        Vector3 sum = Vector3.zero;
        int n = 0;
        for (int i = 0; i < cols.Length; i++)
        {
            Collider c = cols[i];
            if (c == null || c.isTrigger) continue;
            if (c.transform.root == transform.root) continue;
            if (!IsNavigationObstacle(c)) continue;
            if (!NavColliderBlocksMovement(stepTargetObjectId, c, transform.position)) continue;

            Vector3 away = center - c.bounds.center;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-6f)
                away = Vector3.Cross(Vector3.up, transform.forward);
            sum += away.normalized;
            n++;
        }

        return n > 0 ? sum / n : Vector3.zero;
    }

    bool TryPickFanAvoidanceDirection(Vector3 origin, Vector3 desiredDir, Vector3 goalDir, float castDist,
        string stepTargetObjectId, out Vector3 chosen)
    {
        chosen = desiredDir;
        float maxDeg = Mathf.Clamp(obstacleFanMaxDegrees, 22f, 89f);
        float stepDeg = Mathf.Clamp(obstacleFanStepDegrees, 8f, 36f);
        int stepCount = Mathf.Max(2, Mathf.RoundToInt((2f * maxDeg) / stepDeg));

        float bestScore = float.NegativeInfinity;
        Vector3 bestDir = desiredDir;
        bool anyClear = false;
        float wideBonus = Mathf.Max(0f, obstacleFanWideSweepBonus);

        for (int i = 0; i <= stepCount; i++)
        {
            float deltaDeg = -maxDeg + i * (2f * maxDeg / stepCount);
            Vector3 cand = Quaternion.Euler(0f, deltaDeg, 0f) * desiredDir;
            cand.y = 0f;
            if (cand.sqrMagnitude < 1e-8f) continue;
            cand.Normalize();

            if (IsHeadingBlockedByCognitive(origin, cand, castDist, stepTargetObjectId))
                continue;

            anyClear = true;
            // Reward wider clear arcs so we do not pick a barely-off heading that immediately re-hits the same face.
            float score = Vector3.Dot(cand, goalDir) * 2.1f + Mathf.Abs(deltaDeg) * wideBonus;
            if (score > bestScore)
            {
                bestScore = score;
                bestDir = cand;
            }
        }

        if (!anyClear) return false;

        chosen = bestDir.normalized;
        return true;
    }

    /// <summary>
    /// Picks a <b>discrete</b> new horizontal direction (±90°, ±135°, 180°, etc. from the blocked ray or goal flank)
    /// instead of fine-tuning the same heading — used when the fan would only clear at a very shallow angle.
    /// </summary>
    bool TryPickHardAlternateAvoidanceDirection(Vector3 origin, Vector3 blockedDir, Vector3 goalDir, float castDist,
        string stepTargetObjectId, out Vector3 chosen)
    {
        chosen = blockedDir;
        Vector3 b = blockedDir;
        b.y = 0f;
        if (b.sqrMagnitude < 1e-8f) return false;
        b.Normalize();

        Vector3 g = goalDir;
        g.y = 0f;
        if (g.sqrMagnitude < 1e-8f) g = b;
        else g.Normalize();

        // Yaws (deg) applied to blocked dir first (break contact), then goal-orthogonal flanks.
        float[] yawsBlocked =
        {
            90f, -90f, 135f, -135f, 180f, 72f, -72f, 55f, -55f
        };
        float[] yawsGoal =
        {
            90f, -90f, 50f, -50f, 120f, -120f
        };

        Vector3 best = b;
        float bestDot = -2f;
        bool anyClear = false;

        for (int k = 0; k < yawsBlocked.Length; k++)
        {
            Vector3 cand = Quaternion.Euler(0f, yawsBlocked[k], 0f) * b;
            cand.y = 0f;
            if (cand.sqrMagnitude < 1e-8f) continue;
            cand.Normalize();
            if (IsHeadingBlockedByCognitive(origin, cand, castDist, stepTargetObjectId))
                continue;
            anyClear = true;
            float sc = Vector3.Dot(cand, g);
            if (sc > bestDot)
            {
                bestDot = sc;
                best = cand;
            }
        }

        for (int k = 0; k < yawsGoal.Length; k++)
        {
            Vector3 cand = Quaternion.Euler(0f, yawsGoal[k], 0f) * g;
            cand.y = 0f;
            if (cand.sqrMagnitude < 1e-8f) continue;
            cand.Normalize();
            if (IsHeadingBlockedByCognitive(origin, cand, castDist, stepTargetObjectId))
                continue;
            anyClear = true;
            float sc = Vector3.Dot(cand, g);
            if (sc > bestDot)
            {
                bestDot = sc;
                best = cand;
            }
        }

        if (!anyClear)
            return false;

        chosen = best.normalized;
        return true;
    }

    bool IsHeadingBlockedByCognitive(Vector3 origin, Vector3 flatDir, float castDist, string stepTargetObjectId, int recurseDepth = 0)
    {
        if (recurseDepth > 6)
            return false;

        if (!Physics.SphereCast(origin, obstacleProbeRadius, flatDir, out RaycastHit hit, castDist,
                Physics.AllLayers, QueryTriggerInteraction.Ignore))
            return false;

        if (IsColliderOnSelf(hit.collider))
            return IsHeadingBlockedByCognitive(origin + flatDir * 0.1f, flatDir, castDist * 0.9f, stepTargetObjectId, recurseDepth + 1);

        if (!IsNavigationObstacle(hit.collider))
            return true;

        if (!NavColliderBlocksMovement(stepTargetObjectId, hit.collider, origin))
            return false;

        return true;
    }

    bool NavColliderBlocksMovement(string stepTargetObjectId, Collider c, Vector3 agentWorldPos)
    {
        if (!IsNavigationObstacle(c))
            return false;

        return !IsDestinationNavigationObstacle(stepTargetObjectId, c);
    }

    static bool IsDestinationNavigationObstacle(string stepTargetObjectId, Collider c)
    {
        if (IsDestinationCognitiveStation(stepTargetObjectId, c))
            return true;

        Transform root = c.transform;
        while (root.parent != null)
        {
            if (root.GetComponent<DeclarativeObjectMetadata>() != null
                || root.name.StartsWith("Tool_", StringComparison.Ordinal))
            {
                break;
            }
            root = root.parent;
        }

        GameObject toolRoot = root.gameObject;
        DeclarativeObjectMetadata meta = toolRoot.GetComponent<DeclarativeObjectMetadata>();
        if (meta != null && !string.IsNullOrEmpty(meta.objectId))
        {
            if (StationIdsMatch(meta.objectId, stepTargetObjectId))
                return true;
        }

        string name = toolRoot.name;
        if (name.StartsWith("Tool_", StringComparison.Ordinal))
        {
            string id = name.Substring("Tool_".Length);
            int zoneIdx = id.IndexOf("_zone", StringComparison.OrdinalIgnoreCase);
            if (zoneIdx > 0)
                id = id.Substring(0, zoneIdx);
            if (StationIdsMatch(id, stepTargetObjectId))
                return true;
        }

        return StationIdsMatch(name, stepTargetObjectId);
    }

    bool WasPenetratingBlockingNavVolume(string stepTargetObjectId, Vector3 pos)
    {
        return IsPenetratingBlockingNavVolume(stepTargetObjectId, pos, out _);
    }

    bool IsPenetratingBlockingNavVolume(string stepTargetObjectId, Vector3 pos, out Vector3 combinedPushOut)
    {
        combinedPushOut = Vector3.zero;
        if (agentCapsule == null)
            return false;

        GetCapsuleWorldEndpoints(pos, out Vector3 cp1, out Vector3 cp2, out float rad);
        rad *= Mathf.Clamp(obstacleCastRadiusMultiplier, 1f, 2f);

        Collider[] ov = Physics.OverlapCapsule(cp1, cp2, Mathf.Max(rad - 0.04f, 0.06f),
            Physics.AllLayers, QueryTriggerInteraction.Ignore);

        int hits = 0;
        for (int i = 0; i < ov.Length; i++)
        {
            Collider other = ov[i];
            if (other == null || other.isTrigger || other.transform.root == transform.root)
                continue;
            if (!IsNavigationObstacle(other))
                continue;
            if (!NavColliderBlocksMovement(stepTargetObjectId, other, pos))
                continue;

            if (Physics.ComputePenetration(agentCapsule, pos, transform.rotation,
                    other, other.transform.position, other.transform.rotation,
                    out Vector3 dir, out float pushDist) && pushDist > 0.001f)
            {
                combinedPushOut += Vector3.ProjectOnPlane(dir * pushDist, Vector3.up);
                hits++;
            }
            else
            {
                Vector3 away = pos - other.bounds.center;
                away.y = 0f;
                if (away.sqrMagnitude > 1e-8f)
                {
                    combinedPushOut += away.normalized * 0.35f;
                    hits++;
                }
            }
        }

        if (hits == 0)
            return false;

        combinedPushOut /= hits;
        return combinedPushOut.sqrMagnitude > 1e-10f;
    }

    Vector3 RedirectDeltaOutOfNavOverlap(Vector3 delta, Vector3 pushOut, Vector3 pos)
    {
        pushOut.y = 0f;
        if (pushOut.sqrMagnitude < 1e-10f)
            return delta;

        pushOut.Normalize();
        float mag = delta.magnitude;
        if (mag < 1e-8f)
            mag = moveSpeed * Time.deltaTime;

        Vector3 d = delta / Mathf.Max(mag, 1e-6f);
        if (Vector3.Dot(d, -pushOut) <= 0.25f)
            return delta;

        Vector3 tangent = Vector3.Cross(Vector3.up, pushOut).normalized;
        Vector3 toGoal = _navGoalForObstacles - pos;
        toGoal.y = 0f;
        if (toGoal.sqrMagnitude > 1e-8f)
        {
            toGoal.Normalize();
            if (Vector3.Dot(tangent, toGoal) < Vector3.Dot(-tangent, toGoal))
                tangent = -tangent;
        }

        return Vector3.Normalize(pushOut * 0.35f + tangent * 0.65f) * mag;
    }

    bool IsColliderOnSelf(Collider c)
    {
        return c != null && c.transform.root == transform.root;
    }

    /// <summary>Cognitive stations (interactable root) and explicit SceneGenerator nav boxes.</summary>
    static bool IsNavigationObstacle(Collider c)
    {
        if (c == null) return false;
        if (c.GetComponent<EnvironmentSolidCollider>() != null) return true;
        if (c.GetComponentInParent<EnvironmentSolidCollider>() != null) return true;
        if (c.GetComponentInParent<CognitiveStationInteractable>() != null) return true;
        return string.Equals(c.gameObject.name, "CognitiveNavObstacle", StringComparison.Ordinal);
    }

    static bool IsDestinationCognitiveStation(string stepTargetObjectId, Collider c)
    {
        var station = c.GetComponentInParent<CognitiveStationInteractable>();
        if (station == null || string.IsNullOrEmpty(station.stationId)) return false;
        return StationIdsMatch(station.stationId, stepTargetObjectId);
    }

    /// <summary>Matches zone-suffixed tool ids (e.g. cognitive_005 vs cognitive_005_zone2).</summary>
    public static bool StationIdsMatch(string stationId, string stepTargetId)
    {
        if (string.IsNullOrEmpty(stationId) || string.IsNullOrEmpty(stepTargetId)) return false;
        if (string.Equals(stationId, stepTargetId, StringComparison.OrdinalIgnoreCase)) return true;

        bool stepHasZone = stepTargetId.IndexOf("_zone", StringComparison.OrdinalIgnoreCase) >= 0;

        if (!stepHasZone && stationId.StartsWith(stepTargetId + "_zone", StringComparison.OrdinalIgnoreCase))
            return true;
        if (stepHasZone && !stationId.Contains("_zone") && stepTargetId.StartsWith(stationId + "_zone", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    // ── Three-phase orchestrator helpers ──────────────────────────────────────

    /// <summary>
    /// Called by the orchestrator when a cognitive step is ready to execute (agentRole=="M").
    /// Only processes steps that are found in this mover's cognitive or operational sequences.
    /// </summary>
    void HandleOrchestratorCognitiveStep(string stepId)
    {
        if (!isMentalAgent || !enabled) return;

        ActionSequenceStep step = FindStepById(stepId);
        if (step == null)
        {
            // Step not in our sequences — auto-complete it so the DAG can advance
            Debug.LogWarning($"[RagMover] {agentId} received orchestrator cognitive dispatch '{stepId}' but step not found — auto-completing.");
            NotifyOrchestratorStepCompleted(stepId);
            return;
        }

        if (active != null && _currentOrchestratorStepId != null)
        {
            _pendingOrchestratorSteps.Enqueue(stepId);
            return;
        }

        ActivateOrchestratorStep(step);
    }

    /// <summary>
    /// Called by the orchestrator when a physical step is ready to execute (agentRole=="P").
    /// </summary>
    void HandleOrchestratorPhysicalStep(string stepId)
    {
        if (isMentalAgent || !enabled) return;

        ActionSequenceStep step = FindStepById(stepId);
        if (step == null)
        {
            Debug.LogWarning($"[RagMover] {agentId} received orchestrator physical dispatch '{stepId}' but step not found — auto-completing.");
            NotifyOrchestratorStepCompleted(stepId);
            return;
        }

        if (active != null && _currentOrchestratorStepId != null)
        {
            _pendingOrchestratorSteps.Enqueue(stepId);
            return;
        }

        ActivateOrchestratorStep(step);
    }

    /// <summary>
    /// Barrier notification from the orchestrator. Mirrors IsCognitivePhaseComplete for
    /// legacy compatibility (e.g. BSGMLAgent polling the mental leader's flag).
    /// </summary>
    void OnOrchestratorBarrierReached(string barrierStepId, string closesSubTask, string opensSubTask)
    {
        CognitivePhaseOrchestrator orch = CognitivePhaseOrchestrator.GetOrCreateForZone(zoneIndex);
        string openingSubTask = orch != null ? orch.OpeningSubTaskId : "st_0";
        if (string.Equals(closesSubTask, openingSubTask, StringComparison.Ordinal)
            && !RagInferenceSceneController.DeferPhysicalUntilLeaderCognitiveDone())
            TryMarkZoneCognitiveReadyForPhysical($"barrier {barrierStepId} closed {openingSubTask}");
    }

    void OnOrchestratorAllStepsCompleted(int completedZoneIndex)
    {
        if (completedZoneIndex != zoneIndex) return;
        if (_zoneCompletionFinalized) return;

        _zoneCompletionFinalized = true;
        if (isMentalAgent)
            TryMarkZoneCognitiveReadyForPhysical("orchestrator all steps complete");
        else
            IsCognitivePhaseComplete = true;
        MarkAllZoneAgentsCompleted();
        StopMovementAndClearActiveStep();

        Debug.Log($"[RagMover] Zone {zoneIndex} complete. Agent {agentId} marked complete and movement stopped.");
    }

    void MarkAllZoneAgentsCompleted()
    {
        SkillBasedActionSystem skillSystem = _skillCache != null ? _skillCache : FindObjectOfType<SkillBasedActionSystem>();
        _skillCache = skillSystem;
        if (skillSystem != null)
        {
            skillSystem.MarkZoneAgentsCompleted(zoneIndex);
            return;
        }

        SceneUILoader loader = FindObjectOfType<SceneUILoader>();
        if (loader?.sceneData?.agentProfiles == null) return;
        foreach (var kvp in loader.sceneData.agentProfiles)
        {
            AgentProfile profile = kvp.Value;
            if (profile == null || profile.zoneIndex != zoneIndex) continue;
            profile.isCompleted = true;
            if (profile.desireLevel > 0f && profile.skillLevel < profile.desireLevel)
                profile.skillLevel = profile.desireLevel;
        }
    }

    void StopMovementAndClearActiveStep()
    {
        _pendingOrchestratorSteps.Clear();
        _currentOrchestratorStepId = null;
        active = null;
        runningCognitive = false;
        dwellTimer = 0f;
        ClearKleinMotorSession();
        activeMenuStepId = null;
        activeMenuOpened = false;
        activeMenuPressLatched = false;
        activeMenuPressWait = 0f;
        activeMenuTotalWait = 0f;
        _imaginalThoughtBubble?.Hide();
        RagMenuController.EnsureInScene()?.HideAllMenus();
        ResetMenuReachPose();
        if (!isMentalAgent && mlAgent != null)
            mlAgent.ClearMlNavigationTarget();
        if (hostPlayerMovement && _playerMovement != null)
            _playerMovement.SetRagAutopilot(false, zoneIndex);
        if (walkAnim != null) walkAnim.StopWalking();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    /// <summary>
    /// Sets the given step as the active single-step sequence for immediate execution.
    /// Reuses the _singleStepSequence container to avoid GC allocations.
    /// </summary>
    void ActivateOrchestratorStep(ActionSequenceStep step)
    {
        step.isActivated   = false;
        step.isStepCompleted = false;
        if (RagMenuController.IsMenuStep(step))
        {
            activeMenuStepId = step.stepId;
            activeMenuOpened = false;
            activeMenuPressLatched = false;
            activeMenuPressWait = 0f;
            activeMenuTotalWait = 0f;
            RagMenuController.EnsureInScene()?.HideAllMenus();
        }

        if (_singleStepSequence == null)
            _singleStepSequence = new AgentSequenceData(agentId);

        _singleStepSequence.actionSequence.Clear();
        _singleStepSequence.actionSequence.Add(step);
        _singleStepSequence.currentStepIndex = 0;

        _currentOrchestratorStepId = step.stepId;
        active = _singleStepSequence;
        runningCognitive = isMentalAgent;
        dwellTimer = 0f;

        Debug.Log($"[RagMover] {agentId} activating orchestrator step: {step.stepId} → {step.targetObjectId} ({step.currentCognitiveState})");
    }

    /// <summary>Dequeues and activates the next pending orchestrator step if any.</summary>
    void TryDequeueNextOrchestratorStep()
    {
        if (_pendingOrchestratorSteps.Count == 0) return;
        string nextId = _pendingOrchestratorSteps.Dequeue();
        ActionSequenceStep nextStep = FindStepById(nextId);
        if (nextStep != null)
            ActivateOrchestratorStep(nextStep);
        else
            NotifyOrchestratorStepCompleted(nextId);
    }

    ActionSequenceStep FindStepById(string stepId)
    {
        if (string.IsNullOrEmpty(stepId)) return null;

        if (cognitive != null)
            foreach (var s in cognitive.actionSequence)
                if (string.Equals(s.stepId, stepId, StringComparison.Ordinal)) return s;

        if (operational != null)
            foreach (var s in operational.actionSequence)
                if (string.Equals(s.stepId, stepId, StringComparison.Ordinal)) return s;

        // Orchestrator registry is the fallback (contains merged physical+cognitive steps)
        return _zoneOrchestrator?.GetStep(stepId);
    }

    void NotifyOrchestratorStepCompleted(string stepId)
    {
        if (string.IsNullOrEmpty(stepId))
            return;

        if (RagOrchestratorStepReporterRegistry.HasReporter)
            RagOrchestratorStepReporterRegistry.ReportStepCompleted(zoneIndex, stepId, isMentalAgent);
        else
            _zoneOrchestrator?.NotifyStepCompleted(stepId);
    }

    /// <summary>
    /// Initializes the CognitivePhaseOrchestrator with sequence data and JSON metadata.
    /// Called once by the mental leader mover when it has enough data to proceed.
    /// </summary>
    void TryInitializeOrchestrator()
    {
        if (_orchestratorInitialized) return;
        if (!isMentalAgent) return;

        if (_zoneOrchestrator == null) return;

        // Find the physical agent for this zone
        string physicalAgentId = "P1";
        var allMovers = FindObjectsOfType<RagSequenceAgentMover>();
        foreach (var m in allMovers)
        {
            if (!m.isMentalAgent && m.zoneIndex == zoneIndex)
            {
                physicalAgentId = m.agentId;
                break;
            }
        }

        string json = "";
        SceneUILoader loader = FindObjectOfType<SceneUILoader>();
        if (loader != null)
        {
            if (!string.IsNullOrEmpty(loader.MergedRawRagJson))
                json = loader.MergedRawRagJson;
            else if (!string.IsNullOrEmpty(loader.RawJsonText))
                json = loader.RawJsonText;
            else if (!string.IsNullOrEmpty(loader.EffectivePipelineJson))
                json = loader.EffectivePipelineJson;
        }

        _zoneOrchestrator.Initialize(agentId, physicalAgentId, json);
        _orchestratorInitialized = true;

        Debug.Log($"[RagMover] {agentId} initialized CognitivePhaseOrchestrator zone={zoneIndex} (physAgent={physicalAgentId}, json={json.Length} chars)");
    }

    /// <summary>
    /// ML-Agents calls EndEpisode when the zone DAG completes; clear mover gates so the next episode can run RAG locomotion again.
    /// </summary>
    public void PrepareForMlAgentsEpisodeRestart()
    {
        _zoneCompletionFinalized = false;
        IsCognitivePhaseComplete = false;
        StopMovementAndClearActiveStep();
    }

    /// <summary>
    /// Reset orchestrator progress and every <see cref="RagSequenceAgentMover"/> in this zone (mental + physical).
    /// </summary>
    public static void ResetZoneForMlAgentsEpisode(int zoneIndex)
    {
        if (BsgIntegrationSettings.ShouldHoldMultiplayerIdleAfterZoneComplete
            && BsgIntegrationSettings.IsZoneMlRunComplete(zoneIndex))
            return;

        CognitivePhaseOrchestrator orch = CognitivePhaseOrchestrator.GetOrCreateForZone(zoneIndex);
        orch?.ResetProgressForMlAgentsEpisodeRestart();

        RagSequenceAgentMover[] movers = FindObjectsOfType<RagSequenceAgentMover>();
        foreach (RagSequenceAgentMover m in movers)
        {
            if (m == null || m.zoneIndex != zoneIndex) continue;
            m.PrepareForMlAgentsEpisodeRestart();
        }
    }
}

