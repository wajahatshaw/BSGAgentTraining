using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

/// <summary>
/// BSG ML-Agent - Controls agent movement and behavior for ML training
/// </summary>
public class BSGMLAgent : Agent
{
    [Header("Agent Identity")]
    public string agentId;
    public string behaviorName;
    /// <summary>When set by <see cref="SetRagSpawnPreserve"/>, spawn and episode resets use the RAG scene position instead of legacy SIMPLE_* layout.</summary>
    [HideInInspector] public bool ragSpawnPreserveActive;
    [HideInInspector] public Vector3 ragSpawnPreservePosition;
    /// <summary>Zone index (0-3) matching the 4-quadrant layout. Set by ReplicaSceneSetup at runtime.</summary>
    public int zoneIndex = -1;

    /// <summary>Called from <see cref="MLAgentAttacher"/> for RAG physical agents so Initialize/Start do not teleport off JSON placements.</summary>
    public void SetRagSpawnPreserve(Vector3 worldPosition)
    {
        ragSpawnPreserveActive = true;
        ragSpawnPreservePosition = worldPosition;
    }

    // ── Agent role ────────────────────────────────────────────────────────
    public enum AgentRole { Physical, Mental }
    [Header("Agent Role")]
    [Tooltip("Physical = RL-driven task agent (P). Mental = cognitive-only (M) — handled by MentalAgent.cs.")]
    public AgentRole agentRole = AgentRole.Physical;
    [Tooltip("Physical agent stays idle until ZoneDeclarativeMemory.cognitiveReady == true.")]
    public bool waitForCognitiveReady = true;

    [HideInInspector]
    [Tooltip("Inference scene: ONNX policy drives transform; RagSequenceAgentMover stays off.")]
    public bool inferenceOnnxControlsLocomotion;

    [HideInInspector] public string MlNavigationStationId;
    Vector3? _mlNavigationTarget;
    public bool HasMlNavigationTarget => _mlNavigationTarget.HasValue;

    public void SetMlNavigationTarget(Vector3 worldPos, string stationId)
    {
        _mlNavigationTarget = worldPos;
        MlNavigationStationId = stationId;
    }

    public void ClearMlNavigationTarget()
    {
        _mlNavigationTarget = null;
        MlNavigationStationId = null;
    }

    public bool TryGetMlNavigationTarget(out Vector3 pos)
    {
        if (_mlNavigationTarget.HasValue)
        {
            pos = _mlNavigationTarget.Value;
            return true;
        }
        pos = default;
        return false;
    }

    /// <summary>
    /// Current orchestrator / sequence physical target for <see cref="RagTrainingObservationBuilder"/> when Temporal runtime is off.
    /// </summary>
    public bool TryGetRagPhysicalObservationTarget(out Vector3 worldPos, out string targetObjectId)
    {
        worldPos = Vector3.zero;
        targetObjectId = "";

        if (_mlNavigationTarget.HasValue)
        {
            worldPos = _mlNavigationTarget.Value;
            targetObjectId = MlNavigationStationId ?? "";
            return true;
        }

        ActionSequenceStep step = mySequence != null ? mySequence.GetCurrentStep() : null;
        if (step == null && _ragOrchestratorPhysicalMode && !string.IsNullOrEmpty(_orchestratorActiveStepId))
            step = FindPhysicalStepById(_orchestratorActiveStepId);
        if (step == null && actionSequenceData != null)
            step = actionSequenceData.GetCurrentStep();

        if (step == null || string.IsNullOrWhiteSpace(step.targetObjectId))
            return false;

        targetObjectId = ResolvePhysicalStepTargetId(step);
        Vector3? resolved = ResolvePhysicalTargetPosition(targetObjectId);
        if (!resolved.HasValue)
            return false;

        worldPos = resolved.Value;
        return true;
    }
    [Tooltip("Set by PhysicalObservationLoop while it is scanning the scene — bypasses the idle gate.")]
    public bool isDoingObservationScan = false;
    public bool physicalUnlocked = false;
    /// <summary>When true, all operational (physical JSON) steps are done — scripted movement stops for this episode.</summary>
    private bool physicalOperationalSequenceFinished = false;
    private ZoneDeclarativeMemory zoneMemory;
    
    [Header("Movement Settings")]
    public float moveSpeed = 2f;
    public float rotationSpeed = 90f;
    public float maxDistance = 15f;

    [Header("Boundary Settings")]
    [Tooltip("Hard clamp agent inside wall bounds each frame.")]
    public bool constrainToPlayArea = true;
    [Tooltip("Fallback min X inside walls.")]
    public float playAreaMinX = -11f;
    [Tooltip("Fallback max X inside walls.")]
    public float playAreaMaxX = 11f;
    [Tooltip("Fallback min Z inside walls.")]
    public float playAreaMinZ = -11f;
    [Tooltip("Fallback max Z inside walls.")]
    public float playAreaMaxZ = 11f;
    // Probe interval for Academy communicator (scripted vs RL physical locomotion).
    private const float MlTrainerProbeInterval = 0.25f;
    private float _mlTrainerProbeTime = -100f;
    private bool _mlTrainerCommunicatorConnected;

    [Tooltip("Inset from wall colliders so agents do not overlap walls.")]
    public float wallInsetPadding = 0.6f;
    
    [Header("Skill Levels (from JSON)")]
    public float skillLevel = 0f;
    public float desireLevel = 0f;
    
    [Header("ML-Agents Skill Integration")]
    public bool useMLForActions = true; // Let ML decide actions instead of Unity scripts
    public bool enableDetailedLogs = false;
    
    [Header("Connection Timing")]
    [Tooltip("Seconds to wait before allowing ML decision requests at startup. Use 0 to skip (immediate decisions).")]
    public float agentConnectionWaitSeconds = 3f;
    
    private Rigidbody rb;
    private Vector3 startPosition;
    private MonoBehaviour existingMovement;
    private bool mlAgentActive = false;
    
    // Skill system integration
    private SkillBasedActionSystem skillSystem;
    private AgentProximity agentProximity;
    private string currentNearbyTool = null;
    private float episodeTotalReward = 0f;
    public float EpisodeTotalReward => episodeTotalReward; // Public property to access reward
    public int episodeSteps = 0; // Made public for MLAgentsDebugger access
    private int positiveActions = 0;
    private int negativeActions = 0;
    private int neutralActions = 0;
    private MLTrainingLogger trainingLogger;
    private float lastStatsUpdate = 0f;
    private float statsUpdateInterval = 1f; // Update stats every 1 second for terminal
    private float lastConsoleLogUpdate = 0f;
    private float consoleLogInterval = 2f; // Log to Unity Console every 2 seconds
    private bool connectionWindowComplete = false;
    private bool pendingDecisionRequest = false;
    private bool playAreaBoundsInitialized = false;
    
    // Pathfinding and sequence management
    private AgentSequenceManager sequenceManager;
    private AgentSequenceData mySequence;
    private AgentSequenceData actionSequenceData;
    private AgentSequenceData cognitiveSequenceData;
    private AgentCognitiveMemory cognitiveMemory;
    private bool cognitivePhaseActive;
    private string activeCognitiveStepId;
    private string activeCognitiveMemoryStepId;
    private List<Vector3> activeCognitiveRoute = new List<Vector3>();
    private int activeCognitiveRouteIndex = 0;
    private LineRenderer cognitiveThreadLine;
    private Renderer[] cachedAgentRenderers;
    private Dictionary<Renderer, Color> defaultAgentColors = new Dictionary<Renderer, Color>();
    private string completedCognitiveHighlightTargetId;
    private bool hasCompletedCognitiveHighlightTargetPos = false;
    private Vector3 completedCognitiveHighlightTargetPos = Vector3.zero;
    private float cognitiveCompletionFlashRemaining = 0f;
    private float CognitiveCompletionFlashDurationSeconds
    {
        get
        {
            CognitiveProcessManager.EnsureExists();
            return CognitiveProcessManager.Instance != null
                ? CognitiveProcessManager.Instance.rewardWhiteFlashSeconds
                : 2f;
        }
    }
    private float cognitivePenaltyFlashRemaining = 0f;
    private float goalBufferPostPenaltyWait = 0f;

    // RAG ML: orchestrator dispatches physical steps while RagSequenceAgentMover is disabled.
    private bool _ragOrchestratorPhysicalMode;
    private bool _ragOrchestratorSubscribed;
    private string _orchestratorActiveStepId;
    private readonly Queue<string> _pendingOrchestratorStepIds = new Queue<string>(8);
    private AgentSequenceData _orchestratorStepContainer;
    private CognitivePhaseOrchestrator _zoneOrchestratorRef;
    private Coroutine _ragStepFlashRoutine;
    private int goalBufferLayerIndex = -1;
    private int goalBufferTripleSlotOrdinal = -1;
    private float goalBufferTripleSlotTimer = 0f;
    private HumanWalkAnimation _humanWalkAnim;
    private AgentGroundMotor _groundMotor;
    private ImaginalThoughtBubble _imaginalThoughtBubble;
    private const int GoalBufferDiskMaxRetries = 64;
    private float goalBufferLayerHoldTimer = 0f;
    public bool useActrCognitiveRoute = false;
    [Header("Cognitive Flow (Scripted)")]
    public float cognitiveMovementSpeedMultiplier = 4.0f;
    public float minCognitiveMoveSpeed = 4.0f;
    [Tooltip("When P-agent runs scripted cognitive Goal Buffer: chance per layer dwell to fail (black flash + penalty + retry). 0 = succeed after first dwell.")]
    [Range(0f, 1f)]
    public float goalBufferScriptedMissProbability = 0f;
    [Header("Physical Action Flow (Scripted)")]
    public float physicalMovementSpeed = 3.5f;        // base speed for physical action steps
    public float physicalMovementSpeedMultiplier = 1.0f; // keep at 1 for natural RL-like movement
    private Vector3 previousPosition; // Track previous position for direction calculation
    private float previousDistanceToTarget = float.MaxValue;
    private bool isAtTarget = false;
    private float timeAtTarget = 0f; // Track time spent at target (for learn actions)
    private float timeReachedTarget = 0f; // Track when target was first reached (for move actions)
    private const float TARGET_HOLD_TIME = 0.5f; // Must stay at target for 0.5 seconds before step completes (prevents ML oscillation)
    
    // Store current ML actions for debugging (accessible across methods)
    private int currentMoveAction = 0;
    private int currentRotateAction = 1;
    private int currentToolAction = 0;
    private bool hasNotifiedFirstAction = false;
    private HandRotationManager handRotationManager;
    private string activeMenuStepId;
    private bool activeMenuOpened;
    private bool activeMenuPressLatched;
    private float activeMenuInferenceWait;
    private Vector3 _lastInferenceWalkPos;
    private bool _inferenceWalkPosInitialized;
    private const float MenuInferenceFingerSelectDistance = 0.85f;
    private const float MenuInferencePressDwellSeconds = 0.18f;
    private const float MenuInferenceOpenDwellSeconds = 0.28f;
    private const float MenuInferenceAssistSeconds = 1.4f;
    private const float MenuInferenceAssistSecondsFast = 0.35f;
    private const float MenuInferenceArrivalHysteresis = 0.45f;
    /// <summary>ML-Agents calls OnEpisodeBegin every new episode; first episode relies on ReplicaSceneSetup delayed StartCognitiveProcess.</summary>
    private int _mlEpisodesBegun;
    
    public override void Initialize()
    {
        // Check if agent ID is set (MLAgentAttacher should set this; may run before attach — normalize GameObject name).
        if (string.IsNullOrEmpty(agentId))
        {
            agentId = gameObject.name;
            Debug.LogWarning($"⚠️ Agent ID not set for {gameObject.name}, using GameObject name");
        }
        agentId = ZoneAgentIds.NormalizeProfileAgentId(agentId, zoneIndex);

        // Bind to this zone's declarative memory (P-agent uses it to unlock after cognitive process)
        if (zoneIndex >= 0)
            zoneMemory = ZoneDeclarativeMemory.ForZone(zoneIndex);

        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }
        if (agentRole == AgentRole.Physical)
            handRotationManager = HandRotationManager.EnsureOnAgent(gameObject);
        
        // CRITICAL: Always configure Rigidbody properly (even if it already existed)
        rb.useGravity = false; // Disable gravity to prevent jumping/falling
        rb.linearDamping = 1f; // Reduced for better movement responsiveness
            rb.angularDamping = 5f;
        rb.freezeRotation = true; // Prevent rotation on all axes
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ | RigidbodyConstraints.FreezePositionY; // Freeze Y position to prevent jumping
        rb.interpolation = RigidbodyInterpolation.Interpolate; // Smoother movement
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous; // Better collision detection
        rb.mass = 1f; // Standard mass
        if (ragSpawnPreserveActive)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        
        // Set up physics material to reduce friction and allow agents to slide past each other
        PhysicsMaterial agentPhysicsMaterial = new PhysicsMaterial("AgentPhysics");
        agentPhysicsMaterial.dynamicFriction = 0.1f;
        agentPhysicsMaterial.staticFriction = 0.1f;
        agentPhysicsMaterial.bounciness = 0f;
        
        // Apply physics material to collider
        Collider agentCollider = GetComponent<Collider>();
        if (agentCollider != null)
        {
            agentCollider.material = agentPhysicsMaterial;
        }
        
        Vector3 pos;
        if (MLAgentAttacher.TryConsumePendingAttachSeed(gameObject, out MLAgentAttacher.PendingMlAttachSeed pending))
        {
            if (!string.IsNullOrWhiteSpace(pending.agentId))
                agentId = ZoneAgentIds.NormalizeProfileAgentId(pending.agentId, pending.zoneIndex);
            if (pending.zoneIndex >= 0)
                zoneIndex = pending.zoneIndex;
            pos = pending.spawnWorld;
            ragSpawnPreserveActive = true;
            ragSpawnPreservePosition = pos;
            ApplyWorldPositionPreservingRigidbody(pos);
            Debug.Log($"📍 [{agentId}] Host/designated spawn preserved: {pos}");
        }
        else
        {
            RagSequenceAgentMover hostMover = GetComponent<RagSequenceAgentMover>();
            if (hostMover != null && hostMover.hostPlayerMovement)
            {
                Vector3? designated = BsgIntegrationSettings.TryResolveDesignatedPhysicalSpawnWorld?.Invoke(transform.position.y);
                pos = designated ?? transform.position;
                ragSpawnPreserveActive = true;
                ragSpawnPreservePosition = pos;
                ApplyWorldPositionPreservingRigidbody(pos);
                Debug.Log($"📍 [{agentId}] Photon physical player spawn preserved: {pos}");
            }
            else
            {
                pos = GetUniquePositionForAgent();
                transform.position = pos;
                Debug.Log($"📍 [{agentId}] Initial spawn position: {pos}");
            }
        }
        
        // Reset velocities to prevent any initial movement
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        
        startPosition = transform.position;
        CacheDefaultAgentColors();
        
        // Find and disable existing movement components
        DisableExistingMovement();
        
        // Find skill system and proximity component for ML-Agents integration
        skillSystem = FindObjectOfType<SkillBasedActionSystem>();
        agentProximity = GetComponent<AgentProximity>();
        cognitiveMemory = GetComponent<AgentCognitiveMemory>();
        if (cognitiveMemory == null)
        {
            cognitiveMemory = gameObject.AddComponent<AgentCognitiveMemory>();
        }
        trainingLogger = MLTrainingLogger.Instance;
        
        if (RagInferenceSceneController.IsInferenceSceneActive() || agentConnectionWaitSeconds <= 0f)
            connectionWindowComplete = true;
        else
            StartCoroutine(BeginConnectionWindow());
        
        // Initialize sequence manager - CRITICAL: Ensure it's loaded before accessing
        sequenceManager = AgentSequenceManager.Instance;
        if (sequenceManager != null)
        {
            // Wait a frame for sequences to load if needed
            StartCoroutine(WaitForSequenceLoad());
        }
        else
        {
            Debug.LogError($"❌ [{agentId}] AgentSequenceManager.Instance is null!");
        }
        
        previousPosition = transform.position;
        previousDistanceToTarget = float.MaxValue;
        InitializePlayAreaBoundsIfNeeded();
        
        mlAgentActive = true;

        if (agentRole == AgentRole.Physical && !string.IsNullOrEmpty(agentId))
            RagStepRewardBridge.RegisterPhysicalMlAgentForRewards(this);
        if (agentRole == AgentRole.Mental)
            RagStepRewardBridge.RegisterCognitiveMlAgentForRewards(this);
        if (skillSystem != null)
            RagStepRewardBridge.PrimeSkillSystemCache(skillSystem);

        // CRITICAL: Force position correction immediately after initialization
        StartCoroutine(ForcePositionCorrection(0f));
        
        Debug.Log($"🤖 BSGMLAgent initialized: {agentId} (Behavior: {behaviorName}, Speed: {moveSpeed}, ML-Actions: {useMLForActions})");
        
        // CRITICAL: Log initial state
        Debug.Log($"📍 [{agentId}] Initial position: {transform.position}");
        Debug.Log($"🎯 [{agentId}] Rigidbody useGravity: {rb.useGravity}, constraints: {rb.constraints}");
    }
    
    /// <summary>
    /// Check if OnActionReceived is being called within 5 seconds
    /// </summary>
    private System.Collections.IEnumerator CheckMLActionReceived()
    {
        yield return new WaitForSeconds(5f);

        var decisionRequester = GetComponent<Unity.MLAgents.DecisionRequester>();

        if (IsCognitiveGateBlocked())
        {
            Debug.Log($"⏸️ [{agentId}] ML action check skipped while cognitive gate is closed");
            yield break;
        }

        if (!connectionWindowComplete)
        {
            Debug.Log($"⏳ [{agentId}] ML action check deferred until connection window completes");
            yield break;
        }

        // During scripted cognitive phase, RL actions may be intentionally sparse/paused.
        if (cognitivePhaseActive || (decisionRequester != null && !decisionRequester.enabled))
        {
            Debug.Log($"⏸️ [{agentId}] ML action check skipped during cognitive scripted phase");
            yield break;
        }
        
        if (episodeSteps == 0)
        {
            Debug.LogWarning($"⚠️ [{agentId}] OnActionReceived not observed in first 5 seconds (can happen during gated/scripted startup).");
            Debug.LogWarning($"⚠️ [{agentId}] Check: Python server, BehaviorType, DecisionRequester, and cognitive gate state.");
            
            // Log current component status
            var behaviorParams = GetComponent<Unity.MLAgents.Policies.BehaviorParameters>();
            
            Debug.LogWarning($"⚠️ [{agentId}] BehaviorParameters: {(behaviorParams != null ? "EXISTS" : "MISSING")}");
            Debug.LogWarning($"⚠️ [{agentId}] DecisionRequester: {(decisionRequester != null ? "EXISTS" : "MISSING")}");
            
            if (behaviorParams != null)
            {
                Debug.LogWarning($"⚠️ [{agentId}] BehaviorName: {behaviorParams.BehaviorName}, BehaviorType: {behaviorParams.BehaviorType}");
            }
        }
        else
        {
            Debug.Log($"✅ [{agentId}] OnActionReceived IS WORKING! Received {episodeSteps} actions in 5 seconds.");
        }
    }
    
    /// <summary>
    /// Wait for sequence manager to load sequences, then get our sequence
    /// </summary>
    private System.Collections.IEnumerator WaitForSequenceLoad()
    {
        // Wait up to 1 second for sequences to load
        int attempts = 0;
        while (sequenceManager != null && attempts < 50) // 50 attempts = ~1 second at 50fps
        {
            actionSequenceData = GetActionSequenceWithFallback();
            cognitiveSequenceData = GetCognitiveSequenceWithFallback();

            if (actionSequenceData != null && actionSequenceData.actionSequence.Count > 0)
            {
                cognitivePhaseActive = cognitiveSequenceData != null && cognitiveSequenceData.actionSequence.Count > 0;
                // Defer to MentalAgentSpawner if one exists for this zone — P-agent waits for cognitiveReady
                if (cognitivePhaseActive && zoneIndex >= 0 && MentalAgentSpawner.ForZone(zoneIndex) != null)
                    cognitivePhaseActive = false;
                if (agentRole == AgentRole.Mental && RagSequenceMoverOwnsAgentProcess())
                    cognitivePhaseActive = false;
                mySequence = cognitivePhaseActive ? cognitiveSequenceData : actionSequenceData;

                Debug.Log($"✅ [{agentId}] Loaded actionSequence with {actionSequenceData.actionSequence.Count} steps");
                if (cognitivePhaseActive)
                {
                    Debug.Log($"🧠 [{agentId}] Loaded cognitiveActionSequence with {cognitiveSequenceData.actionSequence.Count} steps (runs first)");
                }
                Debug.Log($"🎯 [{agentId}] First active step: {mySequence.GetCurrentStep()?.stepId} -> {mySequence.GetCurrentStep()?.targetObjectId}");
                
                // Now that sequence is loaded, request decision to start movement
                if (!IsCognitiveGateBlocked())
                {
                    RequestDecisionOrQueue("sequence load complete");
                }
                else
                {
                    Debug.Log($"⏸️ [{agentId}] Sequence loaded, waiting for cognitive startup authorization");
                }
                Debug.Log($"📡 [{agentId}] Waiting for ML-Agents server to send actions via OnActionReceived()");
                
                // CRITICAL: Log behavior parameters to confirm ML setup
                var behaviorParams = GetComponent<Unity.MLAgents.Policies.BehaviorParameters>();
                if (behaviorParams != null)
                {
                    Debug.Log($"🔧 [{agentId}] BehaviorParameters: Name={behaviorParams.BehaviorName}, Type={behaviorParams.BehaviorType}, VectorObs={behaviorParams.BrainParameters.VectorObservationSize}");
                }
                
                var decisionRequester = GetComponent<Unity.MLAgents.DecisionRequester>();
                if (decisionRequester != null)
                {
                    Debug.Log($"⏰ [{agentId}] DecisionRequester: Period={decisionRequester.DecisionPeriod}, TakeActionsBetween={decisionRequester.TakeActionsBetweenDecisions}");
                }
                
                yield break;
            }
            
            yield return null;
            attempts++;
        }
        
        if (agentRole == AgentRole.Mental)
        {
            if (!IsCognitiveGateBlocked())
                RequestDecisionOrQueue("mental brain — sequence optional");
            yield break;
        }

        if (actionSequenceData == null || actionSequenceData.actionSequence.Count == 0)
        {
            Debug.LogError($"❌ [{agentId}] Failed to load sequence after {attempts} attempts!");
        }
        else
        {
            // Log the agent's unique sequence to verify different targets
            var firstStep = mySequence.GetCurrentStep();
            if (firstStep != null)
            {
                Debug.Log($"🎯 [{agentId}] UNIQUE SEQUENCE CONFIRMED - First target: {firstStep.targetObjectId} ({firstStep.targetObjectName})");
                Debug.Log($"📋 [{agentId}] Full sequence preview:");
                for (int i = 0; i < Math.Min(3, mySequence.actionSequence.Count); i++)
                {
                    var step = mySequence.actionSequence[i];
                    Debug.Log($"   Step {i+1}: {step.actionType} -> {step.targetObjectId} ({step.targetObjectName})");
                }
            }
        }
    }
    
    private IEnumerator BeginConnectionWindow()
    {
        if (agentConnectionWaitSeconds <= 0f)
        {
            connectionWindowComplete = true;
            if (pendingDecisionRequest)
            {
                pendingDecisionRequest = false;
                RequestDecision();
            }
            yield break;
        }
        
        Debug.Log($"⏳ [{agentId}] ML connection window started ({agentConnectionWaitSeconds:0.#}s)");
        float elapsed = 0f;
        while (elapsed < agentConnectionWaitSeconds)
        {
            float shown = Mathf.Min(elapsed + 1f, agentConnectionWaitSeconds);
            Debug.Log($"⏳ [{agentId}] Waiting for ML-Agents connection... ({shown:0.#}s / {agentConnectionWaitSeconds:0.#}s)");
            yield return new WaitForSecondsRealtime(1f);
            elapsed += 1f;
        }
        
        connectionWindowComplete = true;
        Debug.Log($"✅ [{agentId}] ML connection window complete - decisions enabled");
        
        if (pendingDecisionRequest)
        {
            pendingDecisionRequest = false;
            RequestDecision();
            Debug.Log($"🚀 [{agentId}] Released queued ML decision request after connection window");
        }
    }
    
    private void RequestDecisionOrQueue(string reason)
    {
        if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
            return;

        if (connectionWindowComplete)
        {
            RequestDecision();
            Debug.Log($"🚀 [{agentId}] Requested ML decision ({reason})");
            return;
        }
        
        pendingDecisionRequest = true;
        Debug.Log($"⏸️ [{agentId}] Delaying ML decision ({reason}) until {agentConnectionWaitSeconds:0.#}s connection window completes");
    }
    
    /// <summary>
    /// Force position correction to prevent stacking and jumping
    /// </summary>
    private System.Collections.IEnumerator ForcePositionCorrection(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (ragSpawnPreserveActive && DelegatesLocomotionToRagMover())
        {
            AgentGroundMotor motor = GetComponent<AgentGroundMotor>();
            if (motor != null)
                motor.SnapFeetToGround();
            yield break;
        }

        Vector3 correctPos = GetUniquePositionForAgent();
        if (!ragSpawnPreserveActive)
            correctPos.y = 1f;

        transform.position = correctPos;
        
        // If multiple agents are at same position, spread them out
        if (rb != null)
        {
            // Disable gravity completely and lock Y
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ | RigidbodyConstraints.FreezePositionY;
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        }
        
        Debug.Log($"📍 [{agentId}] Position FORCED to unique: {correctPos}");
    }
    
    void ApplyWorldPositionPreservingRigidbody(Vector3 worldPos)
    {
        if (rb == null)
            rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            bool wasKinematic = rb.isKinematic;
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = worldPos;
            rb.isKinematic = wasKinematic;
        }
        else
        {
            transform.position = worldPos;
        }
    }

    /// <summary>
    /// Get unique position for this agent based on agentId
    /// </summary>
    private Vector3 GetUniquePositionForAgent()
    {
        if (ragSpawnPreserveActive)
            return ragSpawnPreservePosition;

        if (agentId.Contains("Technician_01") || agentId.Contains("SIMPLE_Technician_01") || agentId == "SIMPLE_Technician_01")
        {
            return new Vector3(-8f, 1f, -6f);
        }
        else if (agentId.Contains("Technician_02") || agentId.Contains("SIMPLE_Technician_02") || agentId == "SIMPLE_Technician_02")
        {
            return new Vector3(8f, 1f, 6f);
        }
        else if (agentId.Contains("Supervisor_01") || agentId.Contains("SIMPLE_Supervisor_01") || agentId == "SIMPLE_Supervisor_01")
        {
            return new Vector3(8f, 1f, -6f);
        }
        else if (agentId.Contains("Supervisor_02") || agentId.Contains("SIMPLE_Supervisor_02") || agentId == "SIMPLE_Supervisor_02")
        {
            return new Vector3(-8f, 1f, 6f);
        }
        else
        {
            // Default unique position based on hash of agentId
            int hash = Mathf.Abs(agentId.GetHashCode());
            float x = ((hash % 20) - 10f) * 1.5f;
            float z = (((hash / 20) % 20) - 10f) * 1.5f;
            return new Vector3(x, 1f, z);
        }
    }
    
    void Start()
    {
        if (!ragSpawnPreserveActive || !DelegatesLocomotionToRagMover())
        {
            Vector3 correctPos = GetUniquePositionForAgent();
            transform.position = correctPos;
            Debug.Log($"📍 [{agentId}] Start() - Position FORCED to unique: {correctPos}");
        }
        else
        {
            AgentGroundMotor motor = GetComponent<AgentGroundMotor>();
            if (motor != null)
                motor.SnapFeetToGround();
        }

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ | RigidbodyConstraints.FreezePositionY;
        }
        
        // Request decision only after cognition gate authorizes movement.
        if (!IsCognitiveGateBlocked())
        {
            RequestDecisionOrQueue("start initialization");
            Debug.Log($"🎯 [{agentId}] Start() - Initial ML decision request processed/queued");
        }
        else
        {
            Debug.Log($"⏸️ [{agentId}] Start() - Cognitive gate blocked initial decision");
        }
        Debug.Log($"📊 [{agentId}] Start() - Sequence loaded: {mySequence != null}, Steps: {mySequence?.actionSequence.Count ?? 0}");
        
        // CRITICAL: Start a coroutine to check if OnActionReceived is being called
        StartCoroutine(CheckMLActionReceived());
        
        // Check if ML-Agents Academy is initialized
        if (Unity.MLAgents.Academy.Instance != null)
        {
            Debug.Log($"✅ [{agentId}] Academy initialized - ML-Agents ready for training");
        }
        else
        {
            Debug.LogWarning($"⚠️ [{agentId}] Academy not initialized - ML-Agents may not be ready");
        }

        if (agentRole == AgentRole.Physical && ragSpawnPreserveActive && zoneIndex >= 0)
            SubscribeRagOrchestratorPhysical();

        SubscribeOrchestratorEpisodeCompletion();
    }

    void ProcessMentalAgentActions(ActionBuffers actions)
    {
        lastActionReceivedTime = Time.time;
        episodeSteps++;

        // RAG mental: mover drives motion unless inference cognitive ONNX locomotion is on.
        RagSequenceAgentMover ragMover = GetComponent<RagSequenceAgentMover>();
        if (ragMover != null && ragMover.enabled && ragMover.isMentalAgent && !ragMover.UsesInferenceCognitiveOnnxLocomotion())
            return;

        if (actions.DiscreteActions.Length < 2)
            return;

        int moveAction = actions.DiscreteActions[0];
        int rotateAction = actions.DiscreteActions.Length > 1 ? actions.DiscreteActions[1] : 1;

        Vector3 moveDirection = Vector3.zero;
        if (moveAction == 1)
            moveDirection = transform.forward * moveSpeed * Time.fixedDeltaTime;
        else if (moveAction == 2)
            moveDirection = -transform.forward * moveSpeed * Time.fixedDeltaTime;

        float rotation = 0f;
        if (rotateAction == 0)
            rotation = -rotationSpeed * Time.fixedDeltaTime;
        else if (rotateAction == 2)
            rotation = rotationSpeed * Time.fixedDeltaTime;

        transform.Rotate(0f, rotation, 0f);

        if (rb != null)
        {
            moveDirection.y = 0f;
            if (moveDirection.sqrMagnitude > 0.0001f)
            {
                Vector3 targetVelocity = moveDirection / Time.fixedDeltaTime;
                rb.linearVelocity = new Vector3(targetVelocity.x, 0f, targetVelocity.z);
            }
            else
                rb.linearVelocity = new Vector3(0f, 0f, 0f);
        }

        if (TryGetMlNavigationTarget(out Vector3 nav))
        {
            Vector3 flat = nav - transform.position;
            flat.y = 0f;
            if (flat.magnitude < 2.2f)
                AddReward(0.05f);
        }
    }

    void WriteMentalNavigationHeuristic(ActionSegment<int> discreteActions)
    {
        if (discreteActions.Length < 2)
            return;

        if (!TryGetMlNavigationTarget(out Vector3 target))
        {
            discreteActions[0] = 0;
            discreteActions[1] = 1;
            if (discreteActions.Length > 2) discreteActions[2] = 0;
            return;
        }

        Vector3 to = target - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.01f)
        {
            discreteActions[0] = 0;
            discreteActions[1] = 1;
        }
        else
        {
            float angle = Vector3.SignedAngle(transform.forward, to.normalized, Vector3.up);
            discreteActions[0] = 1;
            if (angle < -15f) discreteActions[1] = 0;
            else if (angle > 15f) discreteActions[1] = 2;
            else discreteActions[1] = 1;
        }

        if (discreteActions.Length > 2)
            discreteActions[2] = 0;
    }

    void OnDestroy()
    {
        RagStepRewardBridge.UnregisterPhysicalMlAgentForRewards(this);
        RagStepRewardBridge.UnregisterCognitiveMlAgentForRewards(this);
        UnsubscribeRagOrchestratorPhysical();
        UnsubscribeOrchestratorEpisodeCompletion();
    }

    bool _subscribedOrchestratorEpisodeCompletion;

    void SubscribeOrchestratorEpisodeCompletion()
    {
        if (_subscribedOrchestratorEpisodeCompletion) return;
        if (!ragSpawnPreserveActive || agentRole != AgentRole.Physical || zoneIndex < 0) return;

        CognitivePhaseOrchestrator orch = CognitivePhaseOrchestrator.GetOrCreateForZone(zoneIndex);
        if (orch == null) return;

        orch.OnAllStepsCompleted += HandleOrchestratorAllStepsCompletedForZone;
        _subscribedOrchestratorEpisodeCompletion = true;
    }

    void UnsubscribeOrchestratorEpisodeCompletion()
    {
        if (!_subscribedOrchestratorEpisodeCompletion || zoneIndex < 0) return;
        CognitivePhaseOrchestrator orch = CognitivePhaseOrchestrator.GetOrCreateForZone(zoneIndex);
        if (orch != null)
            orch.OnAllStepsCompleted -= HandleOrchestratorAllStepsCompletedForZone;
        _subscribedOrchestratorEpisodeCompletion = false;
    }

    void HandleOrchestratorAllStepsCompletedForZone(int completedZoneIndex)
    {
        if (completedZoneIndex != zoneIndex) return;
        if (!ragSpawnPreserveActive || agentRole != AgentRole.Physical) return;

        if (BsgIntegrationSettings.ShouldHoldMultiplayerIdleAfterZoneComplete)
        {
            BsgIntegrationSettings.MarkZoneMlRunComplete(zoneIndex);
            DisableMlDecisionsForZone(zoneIndex);
            Debug.Log($"[BSGMLAgent] {agentId} zone {zoneIndex} — all RAG steps complete; holding idle (multiplayer, no DAG restart).");
        }
        else
        {
            Debug.Log($"[BSGMLAgent] {agentId} zone {zoneIndex} — all RAG steps complete, ending ML episode.");
        }

        EndEpisode();
    }

    static void DisableMlDecisionsForZone(int zoneIndex)
    {
        BSGMLAgent[] agents = FindObjectsOfType<BSGMLAgent>();
        for (int i = 0; i < agents.Length; i++)
        {
            BSGMLAgent ml = agents[i];
            if (ml == null || ml.zoneIndex != zoneIndex) continue;
            DecisionRequester dr = ml.GetComponent<DecisionRequester>();
            if (dr != null)
                dr.enabled = false;
        }
    }

    /// <summary>Called from <see cref="RagRuntimeMLBootstrap"/> after ML attach — keeps humanoid look.</summary>
    public void ConfigureRagPhysicalPresentation()
    {
        HumanBodyBuilder.EnsureBareCapsuleHidden(gameObject);

        Transform legacyProxLabel = transform.Find($"{agentId}_ActionLabel");
        if (legacyProxLabel != null)
            Destroy(legacyProxLabel.gameObject);

        AgentProximity legacyProx = GetComponent<AgentProximity>();
        if (legacyProx != null && HumanBodyBuilder.HasHumanoidBody(gameObject))
            Destroy(legacyProx);

        HumanWalkAnimation walk = GetComponent<HumanWalkAnimation>();
        if (walk != null)
        {
            walk.enabled = true;
            walk.Unfreeze();
        }

        HandRotationManager.EnsureOnAgent(gameObject);

        if (rb == null)
            rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        CacheDefaultAgentColors();
        SubscribeRagOrchestratorPhysical();
        SubscribeOrchestratorEpisodeCompletion();
    }

    /// <summary>
    /// True when <see cref="RagSequenceAgentMover"/> owns step flow on this GameObject (RAG M1–M4 or P*).
    /// ML must not reset pose, run parallel cognitive FixedUpdate, or apply policy locomotion.
    /// </summary>
    bool RagSequenceMoverOwnsAgentProcess()
    {
        RagSequenceAgentMover mover = GetComponent<RagSequenceAgentMover>();
        return mover != null && mover.enabled;
    }

    /// <summary>
    /// True when <see cref="RagSequenceAgentMover"/> handles locomotion; ML agent must not compete for transform/orchestrator physical dispatch.
    /// </summary>
    bool DelegatesLocomotionToRagMover()
    {
        if (inferenceOnnxControlsLocomotion)
            return false;
        if (!RagSequenceMoverOwnsAgentProcess()) return false;
        RagSequenceAgentMover mover = GetComponent<RagSequenceAgentMover>();
        if (agentRole == AgentRole.Mental)
            return mover.isMentalAgent;
        return ragSpawnPreserveActive && agentRole == AgentRole.Physical;
    }

    /// <summary>Replica M_A only: defer VisitStation to policy when Python is connected (RAG mental agents stay scripted).</summary>
    public bool ShouldDeferCognitiveLocomotionToMl()
    {
        if (agentRole != AgentRole.Mental) return false;
        RagSequenceAgentMover mover = GetComponent<RagSequenceAgentMover>();
        if (mover != null && mover.enabled && mover.isMentalAgent)
            return false;
        return IsMlTrainerCommunicatorConnected();
    }

    void SubscribeRagOrchestratorPhysical()
    {
        if (DelegatesLocomotionToRagMover()) return;
        if (_ragOrchestratorSubscribed || zoneIndex < 0) return;
        _zoneOrchestratorRef = CognitivePhaseOrchestrator.GetOrCreateForZone(zoneIndex);
        if (_zoneOrchestratorRef == null) return;

        _ragOrchestratorPhysicalMode = true;
        _zoneOrchestratorRef.OnPhysicalStepDispatched += OnOrchestratorPhysicalStepDispatched;
        _ragOrchestratorSubscribed = true;
        Debug.Log($"[BSGMLAgent] {agentId} subscribed to orchestrator physical dispatch (zone {zoneIndex}).");
    }

    void UnsubscribeRagOrchestratorPhysical()
    {
        if (!_ragOrchestratorSubscribed || _zoneOrchestratorRef == null) return;
        _zoneOrchestratorRef.OnPhysicalStepDispatched -= OnOrchestratorPhysicalStepDispatched;
        _ragOrchestratorSubscribed = false;
    }

    bool IsZonePrimaryPhysicalAgent()
    {
        if (zoneIndex < 0) return true;
        string zoneP = ZoneAgentIds.TryResolvePhysicalAgentId(zoneIndex);
        if (string.IsNullOrEmpty(zoneP)) return true;
        string self = ZoneAgentIds.NormalizeProfileAgentId(agentId, zoneIndex);
        return string.Equals(self, zoneP, StringComparison.OrdinalIgnoreCase);
    }

    void OnOrchestratorPhysicalStepDispatched(string stepId)
    {
        if (!_ragOrchestratorPhysicalMode || agentRole != AgentRole.Physical) return;
        if (string.IsNullOrEmpty(stepId)) return;
        if (!IsZonePrimaryPhysicalAgent()) return;

        if (!string.IsNullOrEmpty(_orchestratorActiveStepId))
        {
            _pendingOrchestratorStepIds.Enqueue(stepId);
            return;
        }

        ActivateOrchestratorPhysicalStep(stepId);
    }

    void ActivateOrchestratorPhysicalStep(string stepId)
    {
        ActionSequenceStep step = FindPhysicalStepById(stepId);
        if (step == null)
        {
            Debug.LogWarning($"[BSGMLAgent] {agentId} orchestrator physical step '{stepId}' not found — auto-completing.");
            NotifyCognitivePhaseOrchestratorStepCompleted(stepId);
            return;
        }

        ActionSequenceStep runtimeStep = CloneOrchestratorPhysicalStep(step);
        runtimeStep.isActivated = false;
        runtimeStep.isStepCompleted = false;

        if (_orchestratorStepContainer == null)
            _orchestratorStepContainer = new AgentSequenceData(agentId);

        _orchestratorStepContainer.actionSequence.Clear();
        _orchestratorStepContainer.actionSequence.Add(runtimeStep);
        _orchestratorStepContainer.currentStepIndex = 0;

        mySequence = _orchestratorStepContainer;
        _orchestratorActiveStepId = stepId;
        activePhysicalStepId = null;
        timeAtTarget = 0f;
        isAtTarget = false;
        timeReachedTarget = 0f;
        physicalStuckTimer = 0f;
        physicalDeflectRemaining = 0f;
        physicalStuckLastPos = transform.position;
        physicalUnlocked = true;
        physicalOperationalSequenceFinished = false;
        ResetMenuSubActionState(runtimeStep);

        Debug.Log($"[BSGMLAgent] {agentId} orchestrator physical step → {step.stepId} @ {step.targetObjectId} ({step.actionType})");

        if (sequenceManager != null)
            SyncPhysicalStepTarget();

        var dr = GetComponent<Unity.MLAgents.DecisionRequester>();
        if (dr != null && !dr.enabled)
            dr.enabled = true;
        RequestDecision();
    }

    void TryRecoverMissedOrchestratorPhysicalDispatch()
    {
        if (!_ragOrchestratorPhysicalMode || !IsZonePrimaryPhysicalAgent()) return;
        if (!string.IsNullOrEmpty(_orchestratorActiveStepId)) return;

        if (_zoneOrchestratorRef == null)
            _zoneOrchestratorRef = CognitivePhaseOrchestrator.GetOrCreateForZone(zoneIndex);
        if (_zoneOrchestratorRef == null) return;

        if (_zoneOrchestratorRef.TryGetFirstActivePhysicalStepId(out string activeId))
            ActivateOrchestratorPhysicalStep(activeId);
    }

    ActionSequenceStep FindPhysicalStepById(string stepId)
    {
        if (string.IsNullOrEmpty(stepId)) return null;

        if (actionSequenceData?.actionSequence != null)
        {
            foreach (ActionSequenceStep s in actionSequenceData.actionSequence)
            {
                if (s != null && string.Equals(s.stepId, stepId, StringComparison.Ordinal))
                    return s;
            }
        }

        return _zoneOrchestratorRef != null ? _zoneOrchestratorRef.GetStep(stepId) : null;
    }

    /// <summary>Runtime copy so MarkStepCompleted on the dispatch container does not mutate actionSequenceData.</summary>
    static ActionSequenceStep CloneOrchestratorPhysicalStep(ActionSequenceStep source)
    {
        if (source == null) return null;

        var d = new ActionSequenceStep
        {
            stepId = source.stepId,
            stepOrder = source.stepOrder,
            actionVerb = source.actionVerb,
            actionType = source.actionType,
            currentCognitiveState = source.currentCognitiveState,
            preposition = source.preposition,
            targetObjectId = source.targetObjectId,
            targetObjectName = source.targetObjectName,
            actionSubType = source.actionSubType,
            menuId = source.menuId,
            menuOptions = source.menuOptions != null ? (string[])source.menuOptions.Clone() : null,
            selectedMenuOptionObjectId = source.selectedMenuOptionObjectId,
            menuContract = CloneMenuContract(source.menuContract),
            description = source.description,
            expectedDuration = source.expectedDuration,
            startTimeSec = source.startTimeSec,
            endTimeSec = source.endTimeSec,
            correct_step_reward = source.correct_step_reward,
            producesPayload = source.producesPayload,
            subTaskId = source.subTaskId,
            isActivated = false,
            isStepCompleted = false,
            isBarrier = source.isBarrier,
            parallelGroupId = source.parallelGroupId,
            agentRole = source.agentRole,
            dependsOn = source.dependsOn != null ? (string[])source.dependsOn.Clone() : null,
            consumesPayload = source.consumesPayload != null ? (string[])source.consumesPayload.Clone() : null,
            canRunInParallelWith = source.canRunInParallelWith != null ? (string[])source.canRunInParallelWith.Clone() : null,
        };
        return d;
    }

    static MenuContract CloneMenuContract(MenuContract source)
    {
        if (source == null) return null;

        var clone = new MenuContract
        {
            selectedOptionId = source.selectedOptionId,
            panelStyle = source.panelStyle,
            buttonTargetId = source.buttonTargetId,
            initialPhase = source.initialPhase,
            options = null
        };

        if (source.options != null)
        {
            clone.options = new MenuContractOption[source.options.Length];
            for (int i = 0; i < source.options.Length; i++)
            {
                MenuContractOption option = source.options[i];
                clone.options[i] = option == null
                    ? null
                    : new MenuContractOption { id = option.id, label = option.label };
            }
        }

        return clone;
    }

    void FinishOrchestratorPhysicalStep()
    {
        if (!_ragOrchestratorPhysicalMode) return;

        _orchestratorActiveStepId = null;
        physicalUnlocked = false;  // always reset; ActivateOrchestratorPhysicalStep re-sets true for pending steps
        mySequence = null;
        activePhysicalStepId = null;
        timeAtTarget = 0f;
        isAtTarget = false;

        if (_pendingOrchestratorStepIds.Count > 0)
            ActivateOrchestratorPhysicalStep(_pendingOrchestratorStepIds.Dequeue());
    }

    static bool IsBareCapsuleRenderer(Renderer renderer, Transform agentRoot)
    {
        return renderer != null && agentRoot != null && renderer.gameObject == agentRoot.gameObject;
    }
    
    private float lastActionReceivedTime = 0f;
    private const float ACTION_TIMEOUT = 2f; // If no action received in 2 seconds, use heuristic
    
    void EnsureRagOrchestratorPhysicalSubscription()
    {
        if (agentRole != AgentRole.Physical || zoneIndex < 0 || !ragSpawnPreserveActive) return;
        if (!_ragOrchestratorSubscribed)
            SubscribeRagOrchestratorPhysical();
    }

    public void EnsureRagOrchestratorPhysicalSubscriptionPublic() => EnsureRagOrchestratorPhysicalSubscription();

    void FixedUpdate()
    {
        if (rb == null) return;

        if (agentRole == AgentRole.Physical)
        {
            EnsureRagOrchestratorPhysicalSubscription();
            if (RagInferenceSceneController.IsInferenceSceneActive())
                TryRecoverMissedOrchestratorPhysicalDispatch();
        }

        if (DelegatesLocomotionToRagMover())
        {
            EnforceZoneBoundsForRagMover();
            return;
        }

        // ── Physical agent idle gate ──────────────────────────────────────
        // P-agent stays frozen until the zone's cognitive process is complete.
        // Exception: isDoingObservationScan = true means PhysicalObservationLoop
        // is actively moving the agent to scan tools — let FixedUpdate proceed.
        if (agentRole == AgentRole.Physical && waitForCognitiveReady && !physicalUnlocked
            && !isDoingObservationScan)
        {
            // DAG interleave: orchestrator already dispatched a physical step — do not wait for full cognitive.
            if (!string.IsNullOrEmpty(_orchestratorActiveStepId))
                physicalUnlocked = true;

            if (!physicalUnlocked)
            {
                if (_ragOrchestratorPhysicalMode && !inferenceOnnxControlsLocomotion)
                {
                    EnforcePlayAreaBounds();
                    ForceIdle();
                    return;
                }

                if (zoneMemory == null && zoneIndex >= 0)
                    zoneMemory = ZoneDeclarativeMemory.ForZone(zoneIndex);

                bool ready;
                RagSequenceAgentMover zoneMover = GetComponent<RagSequenceAgentMover>();
                bool leaderReady = zoneMover != null && zoneMover.LeaderCognitiveGateAllowsPhysicalStart();
                if (zoneMemory != null)
                    ready = zoneMemory.cognitiveReady || leaderReady;
                else
                    ready = zoneMover == null || leaderReady;

                if (!ready)
                {
                    EnforcePlayAreaBounds();
                    ForceIdle();
                    return;
                }

                // Inference DAG: steps arrive only via OnPhysicalStepDispatched — never bulk-run actionSequenceData[0].
                if (_ragOrchestratorPhysicalMode)
                {
                    TryRecoverMissedOrchestratorPhysicalDispatch();
                    if (string.IsNullOrEmpty(_orchestratorActiveStepId))
                    {
                        EnforcePlayAreaBounds();
                        ForceIdle();
                        return;
                    }
                }
                else
                {
                    physicalUnlocked = true;
                    mySequence = actionSequenceData;
                    if (actionSequenceData != null)
                    {
                        actionSequenceData.currentStepIndex = 0;
                        isAtTarget = false;
                        timeAtTarget = 0f;
                        timeReachedTarget = 0f;
                        activePhysicalStepId = null;
                        physicalStuckTimer = 0f;
                        physicalStuckLastPos = transform.position;
                        physicalDeflectRemaining = 0f;
                    }

                    var drOnUnlock = GetComponent<Unity.MLAgents.DecisionRequester>();
                    if (drOnUnlock != null && !drOnUnlock.enabled)
                        drOnUnlock.enabled = true;
                    RequestDecision();

                    if (zoneMemory != null && !string.IsNullOrWhiteSpace(zoneMemory.resolvedTargetId))
                    {
                        if (cognitiveMemory != null)
                            cognitiveMemory.Store("resolvedTargetId", zoneMemory.resolvedTargetId, "declarative_memory");
                        Debug.Log($"▶ [{agentId}] Physical action STARTING. Cognitive resolved target: {zoneMemory.resolvedTargetId} | Step 1: {actionSequenceData?.actionSequence?[0]?.targetObjectId}");
                    }
                    else
                    {
                        Debug.Log($"▶ [{agentId}] Physical action STARTING. Step 1: {actionSequenceData?.actionSequence?[0]?.targetObjectId}");
                    }
                }
            }
        }
        // ── End idle gate ─────────────────────────────────────────────────

        UpdateCognitiveCompletionHighlight();
        UpdateCognitivePenaltyFlash();

        // During the scripted cognitive phase, don't let the per-step analysis gate block movement.
        // The cognitive sequence drives itself — it doesn't need RL decisions.
        bool gateBlocked = !cognitivePhaseActive && IsCognitiveGateBlocked();
        if (gateBlocked)
        {
            EnforcePlayAreaBounds();
            ForceIdle();
            return;
        }

        // Environment scan: only clamp to play area; skip ML/stacking/velocity logic so MovePosition works.
        if (isDoingObservationScan)
        {
            EnforcePlayAreaBounds();
            return;
        }

        // Cognitive flow runs every physics tick via scripted movement (independent of RL action cadence).
        if (cognitivePhaseActive && mySequence != null)
        {
            ExecuteCognitiveScriptedStep();
            EnforcePlayAreaBounds();
            return;
        }

        // Physical action sequence (after cognitive + PhysicalObservationLoop):
        // - Trainer connected + Default → RL / path rewards in OnActionReceived.
        // - No trainer or HeuristicOnly → Unity MoveTowards in ExecutePhysicalScriptedStep (same as legacy fallback).
        bool orchestratorPhysicalStepActive = _ragOrchestratorPhysicalMode
            && !string.IsNullOrEmpty(_orchestratorActiveStepId)
            && mySequence != null;
        if (orchestratorPhysicalStepActive)
            physicalUnlocked = true;
        else if (_ragOrchestratorPhysicalMode && IsZonePrimaryPhysicalAgent())
            TryRecoverMissedOrchestratorPhysicalDispatch();

        // Never run the full physicalAgents[] list while in DAG orchestrator mode (causes 1/20 log flood + wrong step).
        bool legacyPhysicalSequenceActive = !_ragOrchestratorPhysicalMode
            && physicalUnlocked
            && actionSequenceData != null;
        if (orchestratorPhysicalStepActive || legacyPhysicalSequenceActive)
        {
            if (physicalOperationalSequenceFinished)
            {
                EnforcePlayAreaBounds();
                ForceIdle();
                return;
            }

            // Orchestrator dispatches one DAG step at a time via _orchestratorStepContainer — do not overwrite.
            if (!orchestratorPhysicalStepActive && mySequence != actionSequenceData)
                mySequence = actionSequenceData;

            SyncPhysicalStepTarget();

            if (UseScriptedPhysicalLocomotion())
            {
                ExecutePhysicalScriptedStep();
                EnforcePlayAreaBounds();
                return;
            }

            if (inferenceOnnxControlsLocomotion)
            {
                EvaluatePhysicalStepArrivalOnly();
                SyncInferenceWalkPresentation();
                EnforcePlayAreaBounds();
                return;
            }
            // Training RL path: menu steps need sub-flow management (button press → menu show → option press)
            // every FixedUpdate. Regular move steps are completed via CalculatePathfindingRewards instead.
            ActionSequenceStep _rlMenuCheck = mySequence?.GetCurrentStep();
            if (_rlMenuCheck != null && RagMenuController.IsMenuStep(_rlMenuCheck))
            {
                EvaluatePhysicalStepArrivalOnly();
                EnforcePlayAreaBounds();
                return;
            }
            // Training RL path: fall through for Y-lock / stacking after OnActionReceived applies velocity.
        }
        
        // CRITICAL: Check if ML-Agents server is still sending actions
        // If not receiving actions, ensure decision requests continue
        float timeSinceLastAction = Time.time - lastActionReceivedTime;
        if (timeSinceLastAction > ACTION_TIMEOUT && lastActionReceivedTime > 0)
        {
            if (!IsCognitiveGateBlocked())
            {
                Debug.LogWarning($"⚠️ [{agentId}] No ML actions received for {timeSinceLastAction:F1}s - Requesting decision again (last action: {lastActionReceivedTime:F2}, now: {Time.time:F2})");
                RequestDecision();
            }
            
            // Log DecisionRequester status
            var decisionRequester = GetComponent<Unity.MLAgents.DecisionRequester>();
            if (decisionRequester != null)
            {
                Debug.LogWarning($"⚠️ [{agentId}] DecisionRequester: Period={decisionRequester.DecisionPeriod}, TakeActionsBetween={decisionRequester.TakeActionsBetweenDecisions}");
            }
        }
        
        // Log decision request status every 5 seconds for debugging
        if (Time.frameCount % 250 == 0) // ~5 seconds at 50fps
        {
            var decisionRequester = GetComponent<Unity.MLAgents.DecisionRequester>();
            if (decisionRequester != null)
            {
                Debug.Log($"📊 [{agentId}] DecisionRequester active: Period={decisionRequester.DecisionPeriod}, Last action received: {timeSinceLastAction:F1}s ago, Episode steps: {episodeSteps}");
            }
        }
        
        // Ensure DecisionRequester is continuously requesting decisions
        var dr = GetComponent<Unity.MLAgents.DecisionRequester>();
        if (dr == null)
        {
            Debug.LogError($"❌ [{agentId}] DecisionRequester component missing! Agents won't receive ML actions.");
        }
        
        // CRITICAL: Constantly ensure Y position stays at 1 (prevent any jumping/falling)
        Vector3 currentPos = transform.position;
        bool needsCorrection = false;
        
        // Check if Y position is wrong
        if (Mathf.Abs(currentPos.y - 1f) > 0.05f)
        {
            currentPos.y = 1f;
            needsCorrection = true;
        }
        
        // Check if agent is stacking with another agent (same X/Z position)
        // This prevents agents from being on top of each other
        BSGMLAgent[] allAgents = FindObjectsOfType<BSGMLAgent>();
        foreach (var otherAgent in allAgents)
        {
            if (otherAgent == this || otherAgent.agentId == agentId) continue;
            
            Vector3 otherPos = otherAgent.transform.position;
            float horizontalDistance = Vector2.Distance(new Vector2(currentPos.x, currentPos.z), new Vector2(otherPos.x, otherPos.z));
            
            // If agents are too close horizontally (within 1 unit - stacking), push this agent away
            if (horizontalDistance < 1f && Mathf.Abs(currentPos.y - otherPos.y) < 2f)
            {
                // Calculate direction away from other agent
                Vector3 direction = (currentPos - otherPos).normalized;
                if (direction.magnitude < 0.1f)
                {
                    // If agents are exactly on top, use direction based on agentId hash
                    float angle = Mathf.Abs(agentId.GetHashCode()) % 360;
                    direction = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad));
                }
                
                // Push away by at least 3 units to prevent immediate re-stacking
                currentPos.x += direction.x * 3f;
                currentPos.z += direction.z * 3f;
                currentPos.y = 1f; // Ensure Y is correct
                needsCorrection = true;
                
                Debug.Log($"🚫 [{agentId}] Detected stacking with {otherAgent.agentId}, pushing away by 3 units");
            }
        }
        
        // Apply corrections if needed
        if (needsCorrection)
        {
            transform.position = currentPos;
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        }
        
        // Always ensure gravity is off and constraints are applied
        if (rb.useGravity)
        {
            rb.useGravity = false;
        }
        
        if ((rb.constraints & RigidbodyConstraints.FreezePositionY) == 0)
        {
            rb.constraints |= RigidbodyConstraints.FreezePositionY;
        }
        
        // Remove any Y velocity
        Vector3 velocity = rb.linearVelocity;
        if (Mathf.Abs(velocity.y) > 0.01f)
        {
            rb.linearVelocity = new Vector3(velocity.x, 0f, velocity.z);
        }
    }
    
    private void DisableExistingMovement()
    {
        // Disable various movement components
        var simpleMovement = GetComponent<SimpleEntityMovement>();
        if (simpleMovement != null)
        {
            simpleMovement.enabled = false;
            existingMovement = simpleMovement;
        }
        
        var singleMovement = GetComponent<SingleMovementController>();
        if (singleMovement != null)
        {
            singleMovement.enabled = false;
            existingMovement = singleMovement;
        }

        // RagSequenceAgentMover must stay enabled on RAG physical agents (ML delegates locomotion).
    }
    
    public override void OnEpisodeBegin()
    {
        _mlEpisodesBegun++;

        // Log previous episode summary before starting new one (if we had steps)
        if (episodeSteps > 0)
        {
            LogEpisodeSummary();
        }

        // Multiplayer embed: one full DAG run then idle — do not reset orchestrator / step HUD to 0.
        if (BsgIntegrationSettings.ShouldHoldMultiplayerIdleAfterZoneComplete
            && BsgIntegrationSettings.IsZoneMlRunComplete(zoneIndex))
        {
            ForceIdle();
            Debug.Log($"[{agentId}] Zone {zoneIndex} run complete — skipping ML episode restart (multiplayer idle).");
            return;
        }
        
        // RAG mental (M1–M4): RagSequenceAgentMover drives stations — do not teleport to stale ragSpawnPreserve on ML episode ticks.
        bool ragMentalObsOnly = agentRole == AgentRole.Mental && RagSequenceMoverOwnsAgentProcess();
        if (!ragMentalObsOnly)
        {
            Vector3 resetPos = GetUniquePositionForAgent();
            if (!ragSpawnPreserveActive)
                resetPos.y = 1f;
            transform.position = resetPos;
            AgentGroundMotor groundMotor = GetComponent<AgentGroundMotor>();
            if (groundMotor != null)
                groundMotor.SnapFeetToGround();
            transform.rotation = Quaternion.identity;
            startPosition = resetPos;
        }
        else
        {
            AgentGroundMotor groundMotor = GetComponent<AgentGroundMotor>();
            if (groundMotor != null)
                groundMotor.SnapFeetToGround();
        }

        _inferenceWalkPosInitialized = false;
        
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.useGravity = false; // Ensure gravity is off
            // Ensure constraints are still applied
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ | RigidbodyConstraints.FreezePositionY;
        }
        
        // Disable existing movement during ML-Agent control
        if (existingMovement != null)
        {
            existingMovement.enabled = false;
        }
        
        // Reset physical-unlock gate for new episode
        physicalUnlocked      = false;
        physicalOperationalSequenceFinished = false;
        isDoingObservationScan = false;
        _orchestratorActiveStepId = null;
        activePhysicalStepId = null;
        timeAtTarget = 0f;
        activeMenuStepId = null;
        activeMenuOpened = false;
        activeMenuPressLatched = false;
        activeMenuInferenceWait = 0f;
        _pendingOrchestratorStepIds.Clear();
        currentToolAction = 0;
        if (agentRole == AgentRole.Physical)
        {
            RagMenuController.EnsureInScene()?.HideAllMenus();
            ApplyHandPoseForStep(mySequence != null ? mySequence.GetCurrentStep() : null, false);
        }
        if (zoneIndex >= 0)
        {
            // After EndEpisode (zone DAG complete), RagSequenceAgentMover stays finalized unless we reset — restores scripted locomotion next episode.
            if (ragSpawnPreserveActive && agentRole == AgentRole.Physical)
                RagSequenceAgentMover.ResetZoneForMlAgentsEpisode(zoneIndex);

            zoneMemory = ZoneDeclarativeMemory.ForZone(zoneIndex);
            // Do NOT call zoneMemory.ResetForEpisode() here alone — ML-Agents calls OnEpisodeBegin every
            // new episode while MentalAgentSpawner only ran StartCognitiveProcess once at scene load,
            // leaving the blackboard empty forever after the first reset.
            MentalAgentSpawner spawner = MentalAgentSpawner.ForZone(zoneIndex);
            if (spawner != null)
            {
                // Episode 1: ReplicaSceneSetup starts cognitive after ~0.5s — avoid racing a second Start here.
                if (_mlEpisodesBegun > 1)
                {
                    spawner.ResetForEpisode();
                    spawner.StartCognitiveProcess();
                }
            }
            else
            {
                zoneMemory?.ResetForEpisode();
            }
        }

        // Reset PhysicalObservationLoop so it can re-scan next episode
        PhysicalObservationLoop obsLoop = GetComponent<PhysicalObservationLoop>();
        if (obsLoop != null) obsLoop.ResetForEpisode();

        // Reset episode statistics
        episodeTotalReward = 0f;
        episodeSteps = 0;
        positiveActions = 0;
        negativeActions = 0;
        neutralActions = 0;
        currentNearbyTool = null;
        
        // Reset action tracking for timeout detection
        lastActionReceivedTime = Time.time; // Initialize to current time
        hasNotifiedFirstAction = false;
        
        // Reset pathfinding state
        previousPosition = transform.position;
        previousDistanceToTarget = float.MaxValue;
        isAtTarget = false;
        timeAtTarget = 0f;
        timeReachedTarget = 0f; // Reset target reached time
        
        // Ensure sequence is loaded
        if (sequenceManager == null)
        {
            sequenceManager = AgentSequenceManager.Instance;
        }
        
        if (sequenceManager != null)
        {
            actionSequenceData = GetActionSequenceWithFallback();
            cognitiveSequenceData = GetCognitiveSequenceWithFallback();
        }
        
        // Reset sequence(s) to first step
        if (actionSequenceData != null)
        {
            actionSequenceData.currentStepIndex = 0;
        }
        if (cognitiveSequenceData != null)
        {
            cognitiveSequenceData.currentStepIndex = 0;
        }

        cognitivePhaseActive = cognitiveSequenceData != null && cognitiveSequenceData.actionSequence != null && cognitiveSequenceData.actionSequence.Count > 0;
        // When a MentalAgentSpawner owns this zone's cognitive process, the P-agent must NOT
        // run its own cognitive steps — it just waits for zoneMemory.cognitiveReady.
        if (cognitivePhaseActive && zoneIndex >= 0 && MentalAgentSpawner.ForZone(zoneIndex) != null)
            cognitivePhaseActive = false;
        if (agentRole == AgentRole.Mental && RagSequenceMoverOwnsAgentProcess())
            cognitivePhaseActive = false;
        mySequence = cognitivePhaseActive ? cognitiveSequenceData : actionSequenceData;
        activeCognitiveStepId = null;
        activeCognitiveMemoryStepId = null;
        activeCognitiveRoute.Clear();
        activeCognitiveRouteIndex = 0;
        completedCognitiveHighlightTargetId = null;
        hasCompletedCognitiveHighlightTargetPos = false;
        completedCognitiveHighlightTargetPos = Vector3.zero;
        cognitiveCompletionFlashRemaining = 0f;
        if (agentProximity != null)
        {
            agentProximity.SetExternalColorOverride(false);
        }
        RestoreDefaultAgentColors();
        ClearCognitiveThreadLine();

        if (mySequence != null)
        {
            mySequence.currentStepIndex = 0;
            string phase = cognitivePhaseActive ? "cognitive" : "action";
            Debug.Log($"🔄 [{agentId}] Reset to {phase} step 1: {mySequence.GetCurrentStep()?.stepId} -> {mySequence.GetCurrentStep()?.targetObjectId}");
        }
        else if (ragSpawnPreserveActive && agentRole == AgentRole.Physical && DelegatesLocomotionToRagMover())
        {
            Debug.LogWarning($"⚠️ [{agentId}] OnEpisodeBegin: BSGMLAgent sequence empty — RAG orchestrator/RagSequenceAgentMover still drives steps (lookup id: {ZoneAgentIds.NormalizeProfileAgentId(agentId, zoneIndex)}).");
        }
        else
        {
            Debug.LogError($"❌ [{agentId}] OnEpisodeBegin: Sequence not loaded! (lookup: {ZoneAgentIds.NormalizeProfileAgentId(agentId, zoneIndex)})");
        }
        
        // Log episode start for ML training
        Debug.Log($"🔄 [ML-EPISODE] {agentId} Episode Begin - Pos: {transform.position} | Skill: {skillLevel:F1}/{desireLevel:F1} ({GetSkillPercentage()*100:F1}%) | Steps: {mySequence?.actionSequence.Count ?? 0}");
        
        // Also log immediately to show continuous updates
        UpdateUnityConsoleLogs();
        
        // Request immediate decision only when cognition startup gate is open.
        if (!IsCognitiveGateBlocked())
        {
            RequestDecisionOrQueue("episode begin");
            Debug.Log($"🚀 [{agentId}] OnEpisodeBegin: ML decision request processed/queued");
        }
        else
        {
            Debug.Log($"⏸️ [{agentId}] OnEpisodeBegin: Waiting for cognitive startup gate");
        }
        
        // Episode-end notifications run in LogEpisodeSummary() (called at the start of the next episode when episodeSteps > 0).
    }
    
    /// <summary>
    /// Fills discrete buffers for ML-Agents when the behavior uses heuristic or disconnected Default.
    /// After cognitive unlock, physical motion may still be driven by <see cref="ExecutePhysicalScriptedStep"/>
    /// in FixedUpdate when <see cref="UseScriptedPhysicalLocomotion"/> is true — this method then only
    /// satisfies the action contract (idle) while Unity scripts move the agent.
    /// </summary>
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discreteActions = actionsOut.DiscreteActions;

        if (agentRole == AgentRole.Mental)
        {
            WriteMentalNavigationHeuristic(discreteActions);
            return;
        }

        if (IsCognitiveGateBlocked())
        {
            WriteIdleHeuristic(discreteActions);
            return;
        }
        
        // Log when Heuristic is called (should happen when ML server isn't responding)
        if (Time.frameCount % 100 == 0) // Log every ~2 seconds at 50fps
        {
            float timeSinceLastAction = Time.time - lastActionReceivedTime;
            Debug.Log($"🔧 [{agentId}] Heuristic() called - ML actions not received for {timeSinceLastAction:F1}s, using heuristic fallback");
        }
        
        // If sequence not loaded yet, just move forward (basic fallback)
        if (sequenceManager == null || mySequence == null || mySequence.actionSequence.Count == 0)
        {
            Debug.LogWarning($"⚠️ [{agentId}] Heuristic called but sequence not loaded yet - using fallback movement");
            discreteActions[0] = 1; // Forward
            discreteActions[1] = 1; // Straight
            if (discreteActions.Length > 2)
            {
                discreteActions[2] = 0; // No tool action
            }
            return;
        }
        
        ActionSequenceStep currentStep = mySequence.GetCurrentStep();
        if (currentStep == null)
        {
            // No current step - stop
            discreteActions[0] = 0; // Stop
            discreteActions[1] = 1; // Straight
            if (discreteActions.Length > 2)
            {
                discreteActions[2] = 0; // No tool action
            }
            return;
        }
        
        // Only use heuristic for "move" action types
        if (currentStep.actionType != "move") 
        {
            // For learn actions, just rotate toward target
            discreteActions[0] = 0; // Stop
            discreteActions[1] = 1; // Straight (don't rotate)
            if (discreteActions.Length > 2)
            {
                discreteActions[2] = 0; // No tool action
            }
            return;
        }
        
        // Get direction to current target
        Vector3 directionToTarget = sequenceManager.GetDirectionToTarget(agentId, transform.position, zoneIndex);
        float distanceToTarget = sequenceManager.GetDistanceToTarget(agentId, transform.position, zoneIndex);
        
        // Check if reached target
        if (distanceToTarget <= 3.0f)
        {
            // At target - stop and rotate
            discreteActions[0] = 0; // Stop
            discreteActions[1] = 1; // Straight
            if (discreteActions.Length > 2)
            {
                discreteActions[2] = 0; // No tool action
            }
            return;
        }
        
        // Calculate angle to target
        Vector3 forward = transform.forward;
        forward.y = 0;
        directionToTarget.y = 0;
        
        float angleToTarget = Vector3.SignedAngle(forward, directionToTarget, Vector3.up);
        
        // Movement decision
        discreteActions[0] = 1; // Forward (always move forward toward target)
        
        // Rotation decision based on angle to target
        if (angleToTarget < -15f)
        {
            discreteActions[1] = 0; // Rotate left
        }
        else if (angleToTarget > 15f)
        {
            discreteActions[1] = 2; // Rotate right
        }
        else
        {
            discreteActions[1] = 1; // Go straight (aligned with target)
        }
        
        // Tool action (none while moving)
        if (discreteActions.Length > 2)
        {
            discreteActions[2] = 0; // No tool action while moving
        }
    }
    
    public override void CollectObservations(VectorSensor sensor)
    {
        if (agentRole == AgentRole.Mental && zoneIndex >= 0)
        {
            RagCognitiveTrainingObservationBuilder.AppendObservations(sensor, this);
            return;
        }

        // RAG replica physical agents: orchestrator-, payload-, and timing-aware observations (still 30 floats).
        if (ragSpawnPreserveActive && zoneIndex >= 0 && agentRole == AgentRole.Physical)
        {
            RagTrainingObservationBuilder.AppendObservations(sensor, this);
            return;
        }

        // Enhanced observation space: 30 values for ML-Agents skill learning
        // CRITICAL: This must match BehaviorParameters.VectorObservationSize = 30
        
        // 1-5: Agent skills (5 values)
        sensor.AddObservation(skillLevel / 100f); // Current skill level (0-1)
        sensor.AddObservation(desireLevel / 100f); // Target skill level (0-1)
        sensor.AddObservation(GetSkillPercentage()); // Progress percentage (0-1)
        sensor.AddObservation(HasSkill("repair") ? 1f : 0f);
        sensor.AddObservation(HasSkill("inspect") ? 1f : 0f);
        
        // 6-10: Tool proximity distances (5 values) - normalized to 0-1
        float dist1 = GetDistanceToTool("tool_001");
        float dist2 = GetDistanceToTool("tool_002");
        float dist3 = GetDistanceToTool("tool_003");
        float dist4 = GetDistanceToTool("tool_004");
        float dist5 = GetDistanceToTool("workbench_001");
        
        sensor.AddObservation(Mathf.Clamp01(dist1 / 20f)); // 0 = very close, 1 = far
        sensor.AddObservation(Mathf.Clamp01(dist2 / 20f));
        sensor.AddObservation(Mathf.Clamp01(dist3 / 20f));
        sensor.AddObservation(Mathf.Clamp01(dist4 / 20f));
        sensor.AddObservation(Mathf.Clamp01(dist5 / 20f));
        
        // 11-15: Agent state (5 values)
        sensor.AddObservation(transform.position.x / 10f);
        sensor.AddObservation(transform.position.z / 10f);
        sensor.AddObservation(transform.eulerAngles.y / 360f);
        sensor.AddObservation(Vector3.Distance(transform.position, startPosition) / 20f);
        sensor.AddObservation(mlAgentActive ? 1f : 0f);
        
        // 16-20: Nearby tool requirements (if in proximity) - NEW for ML learning
        string nearbyTool = GetNearbyToolId();
        if (!string.IsNullOrEmpty(nearbyTool) && skillSystem != null)
        {
            float minRequired = GetMinRequiredLevel(nearbyTool);
            float maxRequired = GetMaxRequiredLevel(nearbyTool);
            float avgRequired = GetAvgRequiredLevel(nearbyTool);
            float canLearn = (skillLevel >= minRequired) ? 1f : 0f; // Can learn at least one skill
            float isProficient = (skillLevel >= maxRequired) ? 1f : 0f; // Knows all skills
            
            sensor.AddObservation(minRequired / 100f);
            sensor.AddObservation(maxRequired / 100f);
            sensor.AddObservation(avgRequired / 100f);
            sensor.AddObservation(canLearn);
            sensor.AddObservation(isProficient);
        }
        else
        {
            // No tool nearby - zero out these observations
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
        }
        
        // 21-25: Skill capability vs tool requirements - NEW for ML decision making
        sensor.AddObservation(GetSkillVsToolComparison("tool_001"));
        sensor.AddObservation(GetSkillVsToolComparison("tool_002"));
        sensor.AddObservation(GetSkillVsToolComparison("tool_003"));
        sensor.AddObservation(GetSkillVsToolComparison("tool_004"));
        sensor.AddObservation(GetSkillVsToolComparison("workbench_001"));
        
        // 26-30: Pathfinding observations - NEW for ML navigation learning
        if (sequenceManager != null && mySequence != null)
        {
            ActionSequenceStep currentStep = mySequence.GetCurrentStep();
            if (currentStep != null)
            {
                // 26-27: Direction to CURRENT step's target (normalized X, Z components)
                // CRITICAL: This is the target from currentStep.targetObjectId (sequence order)
                Vector3 directionToTarget = sequenceManager.GetDirectionToTarget(agentId, transform.position, zoneIndex);
                sensor.AddObservation(directionToTarget.x); // Normalized direction X (-1 to 1)
                sensor.AddObservation(directionToTarget.z); // Normalized direction Z (-1 to 1)
                
                // 28: Distance to CURRENT step's target (normalized 0-1, where 0=at target, 1=far)
                // CRITICAL: This enforces sequence order - only rewards distance to current step's target
                float distanceToTarget = sequenceManager.GetDistanceToTarget(agentId, transform.position, zoneIndex);
                sensor.AddObservation(Mathf.Clamp01(distanceToTarget / 20f)); // Normalize to 0-1
                
                // 29: Current step number (normalized 0-1) - helps ML know sequence position
                float stepProgress = mySequence.currentStepIndex / (float)Mathf.Max(1, mySequence.actionSequence.Count - 1);
                sensor.AddObservation(stepProgress);
                
                // 30: Is at CORRECT target (1=yes and correct target, 0=no or wrong target)
                // CRITICAL: Only true if at current step's target (enforces sequence order)
                bool atTarget = sequenceManager.HasReachedTarget(agentId, transform.position, 3.0f, zoneIndex);
                // Reuse nearbyTool from outer scope (already declared at line 706)
                string currentTargetId = currentStep.targetObjectId;
                // Check if at correct target: must be at target AND (no nearby tool OR nearby tool matches current target)
                bool atCorrectTarget = atTarget && (string.IsNullOrEmpty(nearbyTool) || nearbyTool == currentTargetId);
                sensor.AddObservation(atCorrectTarget ? 1f : 0f);
            }
            else
            {
                // No current step - zero out pathfinding observations
                sensor.AddObservation(0f); // direction X
                sensor.AddObservation(0f); // direction Z
                sensor.AddObservation(1f); // distance (far)
                sensor.AddObservation(1f); // step progress (completed)
                sensor.AddObservation(0f); // at target
            }
        }
        else
        {
            // No sequence manager - zero out pathfinding observations
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
            sensor.AddObservation(1f);
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
        }
    }
    
    private float GetSkillPercentage()
    {
        if (desireLevel <= 0) return 0f;
        return Mathf.Clamp01(skillLevel / desireLevel);
    }
    
    private string GetNearbyToolId()
    {
        if (agentProximity != null)
        {
            // Check if agent is in proximity of any tool
            Collider[] hitColliders = Physics.OverlapSphere(transform.position, 3f);
            foreach (var hit in hitColliders)
            {
                string toolId = ExtractToolIdFromName(hit.gameObject.name);
                if (!string.IsNullOrEmpty(toolId))
                {
                    currentNearbyTool = toolId;
                    return toolId;
                }
            }
        }
        currentNearbyTool = null;
        return null;
    }
    
    private string ExtractToolIdFromName(string objName)
    {
        // Extract tool ID from GameObject name (handles variations like "tool_001", "REPLICA_tool_001", etc.)
        if (objName.Contains("tool_001")) return "tool_001";
        if (objName.Contains("tool_002")) return "tool_002";
        if (objName.Contains("tool_003")) return "tool_003";
        if (objName.Contains("tool_004")) return "tool_004";
        if (objName.Contains("workbench_001")) return "workbench_001";
        return null;
    }
    
    private float GetMinRequiredLevel(string toolId)
    {
        if (skillSystem == null || !skillSystem.IsDataReady()) return 0f;
        
        var toolState = skillSystem.GetToolState(toolId);
        if (toolState == null || toolState.requiredSkills == null || toolState.requiredSkills.Length == 0)
            return 0f;
        
        float min = float.MaxValue;
        foreach (var skill in toolState.requiredSkills)
        {
            if (skill.requiredLevel < min)
                min = skill.requiredLevel;
        }
        return min == float.MaxValue ? 0f : min;
    }
    
    private float GetMaxRequiredLevel(string toolId)
    {
        if (skillSystem == null || !skillSystem.IsDataReady()) return 0f;
        
        var toolState = skillSystem.GetToolState(toolId);
        if (toolState == null || toolState.requiredSkills == null || toolState.requiredSkills.Length == 0)
            return 0f;
        
        float max = 0f;
        foreach (var skill in toolState.requiredSkills)
        {
            if (skill.requiredLevel > max)
                max = skill.requiredLevel;
        }
        return max;
    }
    
    private float GetAvgRequiredLevel(string toolId)
    {
        if (skillSystem == null || !skillSystem.IsDataReady()) return 0f;
        
        var toolState = skillSystem.GetToolState(toolId);
        if (toolState == null || toolState.requiredSkills == null || toolState.requiredSkills.Length == 0)
            return 0f;
        
        float sum = 0f;
        foreach (var skill in toolState.requiredSkills)
        {
            sum += skill.requiredLevel;
        }
        return sum / toolState.requiredSkills.Length;
    }
    
    private float GetSkillVsToolComparison(string toolId)
    {
        // Returns: -1 if insufficient, 0 if can learn, +1 if proficient
        if (skillSystem == null || !skillSystem.IsDataReady()) return -1f;
        
        float minRequired = GetMinRequiredLevel(toolId);
        float maxRequired = GetMaxRequiredLevel(toolId);
        
        if (minRequired == 0 && maxRequired == 0) return 0f; // No requirements
        
        if (skillLevel >= maxRequired) return 1f; // Proficient
        if (skillLevel >= minRequired) return 0f; // Can learn
        return -1f; // Insufficient
    }
    
    /// <summary>Wire zone M_A / RAG mental (M1–M4) for CognitiveAgentZoneN training after components exist.</summary>
    public void ConfigureAsCognitiveBrain(int zi, string cognitiveAgentId, string behavior, float cognitiveMoveSpeed)
    {
        zoneIndex = Mathf.Clamp(zi, 0, 3);
        agentId = cognitiveAgentId;
        behaviorName = behavior;
        agentRole = AgentRole.Mental;
        waitForCognitiveReady = false;
        moveSpeed = cognitiveMoveSpeed > 0f ? cognitiveMoveSpeed : moveSpeed;
        ragSpawnPreserveActive = true;
        ragSpawnPreservePosition = transform.position;
        mlAgentActive = true;

        var bp = GetComponent<Unity.MLAgents.Policies.BehaviorParameters>();
        if (bp != null)
            bp.BehaviorName = behavior;

        RagStepRewardBridge.RegisterCognitiveMlAgentForRewards(this);
        if (!IsCognitiveGateBlocked())
            RequestDecisionOrQueue("ConfigureAsCognitiveBrain");
        Debug.Log($"🧠 [{agentId}] Cognitive brain configured → {behavior} (zone {zoneIndex})");
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (agentRole == AgentRole.Mental)
        {
            ProcessMentalAgentActions(actions);
            return;
        }

        if (DelegatesLocomotionToRagMover())
        {
            currentMoveAction = actions.DiscreteActions.Length > 0 ? actions.DiscreteActions[0] : 0;
            currentRotateAction = actions.DiscreteActions.Length > 1 ? actions.DiscreteActions[1] : 1;
            currentToolAction = actions.DiscreteActions.Length > 2 ? actions.DiscreteActions[2] : 0;
            ActionSequenceStep delegatedPhysicalStep = mySequence != null ? mySequence.GetCurrentStep() : null;
            ApplyHandPoseForStep(delegatedPhysicalStep, currentToolAction == 2);
            ForceIdle();
            return;
        }

        if (IsCognitiveGateBlocked())
        {
            ForceIdle();
            return;
        }

        // CRITICAL: Update last action received time to track if ML server is responding
        lastActionReceivedTime = Time.time;

        // During cognitive flow we ignore RL actions and keep scripted movement in FixedUpdate.
        if (cognitivePhaseActive && mySequence != null)
            return;

        // PhysicalObservationLoop drives the capsule via MovePosition — ML actions must not fight it
        // or apply the maxDistance teleport (looks like constant "respawn" while visiting tools).
        if (isDoingObservationScan)
        {
            ForceIdle();
            return;
        }

        // Physical phase after full cognitive + observation: apply policy/heuristic actions here
        // (move / rotate / tool branch) so PPO trains real control. Only idle out when sequence is done.
        if (physicalUnlocked && physicalOperationalSequenceFinished)
        {
            ForceIdle();
            return;
        }

        // Scripted fallback: FixedUpdate drives MoveTowards — do not apply discrete policy velocities here.
        if (UseScriptedPhysicalLocomotion())
        {
            ForceIdle();
            return;
        }
        
        // CRITICAL: Log that OnActionReceived is being called (first few times)
        if (episodeSteps < 5)
        {
            Debug.Log($"✅ [{agentId}] OnActionReceived CALLED! Episode Steps: {episodeSteps}, Actions Length: {actions.DiscreteActions.Length}");
        }
        
        // Safety check: ensure we have discrete actions
        // Now expecting 3 branches: [move, rotate, tool_action]
        if (actions.DiscreteActions.Length < 2)
        {
            Debug.LogError($"❌ [{agentId}] OnActionReceived: Not enough discrete actions. Expected at least 2, got {actions.DiscreteActions.Length}");
            Debug.LogError($"❌ [{agentId}] CRITICAL: Action space mismatch! Unity expects [3,3,3] discrete actions, but ML model is sending {actions.DiscreteActions.Length} actions.");
            Debug.LogError($"❌ [{agentId}] This means BehaviorParameters in Unity doesn't match the ML model's action space.");
            
            // Use fallback actions to prevent crash
            Debug.LogWarning($"⚠️ [{agentId}] Using fallback actions: move=0 (stop), rotate=1 (straight)");
            // Don't return - continue with fallback values
        }
        
        episodeSteps++;

        if (!hasNotifiedFirstAction)
        {
            hasNotifiedFirstAction = true;
            if (PersonaCognitiveControlSystem.Instance != null)
            {
                PersonaCognitiveControlSystem.Instance.NotifyFirstActionStarted(agentId);
            }
        }

        // Update custom statistics for ML-Agents terminal (every N seconds)
        if (Time.time - lastStatsUpdate >= statsUpdateInterval)
        {
            UpdateMLAgentsStats();
            lastStatsUpdate = Time.time;
        }
        
        // Continuously update Unity Console logs (separate from terminal stats)
        if (Time.time - lastConsoleLogUpdate >= consoleLogInterval)
        {
            UpdateUnityConsoleLogs();
            lastConsoleLogUpdate = Time.time;
        }
        
        // Actions: 3 discrete branches (or 2 if old config)
        // Branch 0: Move (0=stop, 1=forward, 2=backward)
        // Branch 1: Rotate (0=left, 1=straight, 2=right)
        // Branch 2: Tool Action (0=none, 1=positive, 2=negative/neutral) ← NEW
        
        // Extract actions with fallback values if action space is wrong
        int moveAction = actions.DiscreteActions.Length > 0 ? actions.DiscreteActions[0] : 0; // Default: stop
        int rotateAction = actions.DiscreteActions.Length > 1 ? actions.DiscreteActions[1] : 1; // Default: straight
        int toolAction = actions.DiscreteActions.Length > 2 ? actions.DiscreteActions[2] : 0; // Default: no tool action
        
        // Store actions as class members for debugging in other methods
        currentMoveAction = moveAction;
        currentRotateAction = rotateAction;
        currentToolAction = toolAction;

        ActionSequenceStep activePhysicalStep = mySequence != null ? mySequence.GetCurrentStep() : null;
        if (RagMenuController.IsMenuStep(activePhysicalStep))
        {
            if (IsMenuOpenedForStep(activePhysicalStep))
            {
                RagMenuController.EnsureInScene()?.ShowMenuForStep(activePhysicalStep, zoneIndex);
                RagMenuController.EnsureInScene()?.ReassertVisibleMenus();
            }
            else
                RagMenuController.EnsureInScene()?.HideAllMenus();
            ApplyHandPoseForStep(activePhysicalStep, toolAction > 0);
            string menuTargetId = ResolvePhysicalStepTargetId(activePhysicalStep);
            Vector3? menuTargetPos = ResolvePhysicalTargetPosition(menuTargetId);
            if (menuTargetPos.HasValue)
                ApplyHandReachPose(menuTargetPos.Value, toolAction == 2);
        }
        
        // CRITICAL: Log ML action received every 10 steps (more frequent for debugging)
        if (episodeSteps % 10 == 0)
        {
            Debug.Log($"🤖 [{agentId}] ML ACTION RECEIVED: move={moveAction} (0=stop,1=fwd,2=back), rotate={rotateAction} (0=L,1=S,2=R), tool={toolAction} | Episode Steps: {episodeSteps}");
        }
        
        // CRITICAL: Always log first few actions to confirm ML is working
        if (episodeSteps <= 5)
        {
            Debug.Log($"🚀 [{agentId}] FIRST ML ACTIONS: move={moveAction}, rotate={rotateAction}, tool={toolAction} | Step: {episodeSteps} | Total Actions: {actions.DiscreteActions.Length}");
            
            // Log the exact action values received
            for (int i = 0; i < actions.DiscreteActions.Length; i++)
            {
                Debug.Log($"🔍 [{agentId}] Action[{i}] = {actions.DiscreteActions[i]}");
            }
        }
        
        // Apply movement
        Vector3 moveDirection = Vector3.zero;
        if (moveAction == 1) // Forward
        {
            moveDirection = transform.forward * moveSpeed * Time.fixedDeltaTime;
        }
        else if (moveAction == 2) // Backward
        {
            moveDirection = -transform.forward * moveSpeed * Time.fixedDeltaTime;
        }
        
        // Apply rotation
        float rotation = 0f;
        if (rotateAction == 0) // Left
        {
            rotation = -rotationSpeed * Time.fixedDeltaTime;
        }
        else if (rotateAction == 2) // Right
        {
            rotation = rotationSpeed * Time.fixedDeltaTime;
        }
        
        transform.Rotate(0, rotation, 0);
        
        // Store distance BEFORE movement for comparison
        float distanceBeforeMove = float.MaxValue;
        if (sequenceManager != null && mySequence != null)
        {
            // CRITICAL: Always calculate distance before movement, even if previousDistanceToTarget is MaxValue
            distanceBeforeMove = sequenceManager.GetDistanceToTarget(agentId, transform.position, zoneIndex);
            
            // If this is the first calculation, use current distance as baseline
            if (previousDistanceToTarget >= float.MaxValue - 1f)
            {
                previousDistanceToTarget = distanceBeforeMove;
            }
        }
        
        bool usedGroundMotor = false;
        Vector3 positionBeforeGroundMove = transform.position;

        // Inference uses the same ground motor style as the training RAG mover so the body walks
        // through the world instead of being dragged by Rigidbody velocity.
        if (inferenceOnnxControlsLocomotion && agentRole == AgentRole.Physical)
        {
            if (_groundMotor == null)
                _groundMotor = GetComponent<AgentGroundMotor>() ?? gameObject.AddComponent<AgentGroundMotor>();
            if (_groundMotor != null)
            {
                if (_groundMotor.clampZoneIndex < 0)
                    _groundMotor.clampZoneIndex = zoneIndex;
                _groundMotor.TryMoveGround(moveDirection);
                usedGroundMotor = true;
                if (rb != null)
                    rb.linearVelocity = Vector3.zero;
            }
        }

        // Apply movement with physics
        if (!usedGroundMotor && rb != null && moveDirection != Vector3.zero)
        {
            // Ensure movement is only horizontal (Y stays at 1)
            moveDirection.y = 0;

            // Use velocity-based movement for smoother physics interaction
            // This helps agents push past each other instead of getting stuck
            Vector3 targetVelocity = moveDirection / Time.fixedDeltaTime;
            rb.linearVelocity = new Vector3(targetVelocity.x, 0f, targetVelocity.z);
        }
        else if (!usedGroundMotor && rb != null)
        {
            // Stop movement when no action
            rb.linearVelocity = new Vector3(0f, 0f, 0f);
        }

        // Drive walk animation for physical ONNX locomotion — scripted path calls StartWalking()
        // inside ExecutePhysicalScriptedStep but ONNX path never did, so agent slid without animation.
        if (inferenceOnnxControlsLocomotion && agentRole == AgentRole.Physical)
        {
            if (_humanWalkAnim == null)
                _humanWalkAnim = GetComponent<HumanWalkAnimation>() ?? GetComponentInChildren<HumanWalkAnimation>(true);
            if (_humanWalkAnim != null)
            {
                float actualMove = Vector3.Distance(transform.position, positionBeforeGroundMove);
                if (moveAction != 0 || actualMove > 0.002f)
                    _humanWalkAnim.StartWalking();
                else
                    _humanWalkAnim.StopWalking();
            }
        }
        
        // Constantly ensure Y position is locked (prevent jumping)
        if (rb != null && Mathf.Abs(transform.position.y - 1f) > 0.1f)
        {
            Vector3 correctedPos = transform.position;
            correctedPos.y = 1f;
            transform.position = correctedPos;
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z); // Remove Y velocity
        }
        
        // Ensure Y position stays at 1 (apply after movement)
        if (rb != null)
        {
            Vector3 currentPos = transform.position;
            if (Mathf.Abs(currentPos.y - 1f) > 0.01f)
            {
                currentPos.y = 1f;
                transform.position = currentPos;
            }
        }
        
        // PHASE 4: Calculate pathfinding rewards AFTER movement is applied (only if sequence is loaded)
        if (sequenceManager != null && mySequence != null && mySequence.actionSequence.Count > 0)
        {
            // CRITICAL: Check if sequence is complete (in case it completed in previous frame)
            if (mySequence.GetCurrentStep() == null)
            {
                CheckAndRewardSequenceCompletion();
            }
            else
            {
                CalculatePathfindingRewards(moveDirection, distanceBeforeMove);
            }
        }
        
        // CRITICAL: Log movement every 10 steps for debugging (more frequent)
        if (enableDetailedLogs && episodeSteps % 10 == 0)
        {
            string stepInfo = mySequence != null && mySequence.GetCurrentStep() != null 
                ? $"Step: {mySequence.GetCurrentStep().stepId} -> {mySequence.GetCurrentStep().targetObjectId}" 
                : "No step";
            Vector3 velocity = rb != null ? rb.linearVelocity : Vector3.zero;
            Debug.Log($"🏃 [{agentId}] Moving: {stepInfo} | Pos: {transform.position} | Velocity: {velocity} | Action: move={moveAction}, rotate={rotateAction}, tool={toolAction}");
        }
        
        // CRITICAL: ML-Agents Tool Action Handling
        // Check if agent is in proximity and ML wants to interact
        string nearbyTool = GetNearbyToolId();
        if (useMLForActions && !string.IsNullOrEmpty(nearbyTool) && toolAction > 0 && skillSystem != null)
        {
            ProcessMLToolAction(nearbyTool, toolAction);
        }
        
        // Legacy radial leash from episode start. Scripted physical motion used to return before this block,
        // so it never ran during MoveTowards. RL crosses >15 m in 40×40 replica zones → constant snap-back
        // to startPosition and agents look "frozen". Post-cognitive phase uses per-zone play-area clamps only.
        if (!physicalUnlocked && !isDoingObservationScan)
        {
            float distance = Vector3.Distance(transform.position, startPosition);
            if (distance > maxDistance)
            {
                Vector3 resetPos = startPosition;
                resetPos.y = 1f;
                transform.position = resetPos;
                if (rb != null)
                    rb.linearVelocity = Vector3.zero;
            }
        }

        EnforcePlayAreaBounds();

        if (UsesRagHudRewardSystem() && RagMlExtrinsicRewardHub.IsMlTrainingEnabled && !DelegatesLocomotionToRagMover())
            RagMlExtrinsicRewardHub.ApplyPerStepDriftPenalty(this);
        
        // Update previous position and distance for next frame
        previousPosition = transform.position;
        if (sequenceManager != null)
        {
            previousDistanceToTarget = sequenceManager.GetDistanceToTarget(agentId, transform.position, zoneIndex);
        }
    }

    private void InitializePlayAreaBoundsIfNeeded()
    {
        if (playAreaBoundsInitialized)
        {
            return;
        }

        playAreaBoundsInitialized = true;

        // Try to infer from known replica wall names.
        GameObject north = GameObject.Find("REPLICA_North_Wall");
        GameObject south = GameObject.Find("REPLICA_South_Wall");
        GameObject east = GameObject.Find("REPLICA_East_Wall");
        GameObject west = GameObject.Find("REPLICA_West_Wall");

        if (north == null || south == null || east == null || west == null)
        {
            return;
        }

        Collider nCol = north.GetComponent<Collider>();
        Collider sCol = south.GetComponent<Collider>();
        Collider eCol = east.GetComponent<Collider>();
        Collider wCol = west.GetComponent<Collider>();
        if (nCol == null || sCol == null || eCol == null || wCol == null)
        {
            return;
        }

        playAreaMinX = wCol.bounds.max.x;
        playAreaMaxX = eCol.bounds.min.x;
        playAreaMinZ = sCol.bounds.max.z;
        playAreaMaxZ = nCol.bounds.min.z;
    }

    void EnforceZoneBoundsForRagMover()
    {
        if (zoneIndex < 0) return;

        Vector3 pos = transform.position;
        Vector3 clamped = ZonePlayAreaBounds.ClampPosition(zoneIndex, pos);
        if ((clamped - pos).sqrMagnitude < 1e-8f) return;

        if (rb != null)
            rb.MovePosition(clamped);
        else
            transform.position = clamped;
    }

    private void EnforcePlayAreaBounds()
    {
        if (!constrainToPlayArea)
        {
            return;
        }

        InitializePlayAreaBoundsIfNeeded();

        float minX = Mathf.Min(playAreaMinX, playAreaMaxX);
        float maxX = Mathf.Max(playAreaMinX, playAreaMaxX);
        float minZ = Mathf.Min(playAreaMinZ, playAreaMaxZ);
        float maxZ = Mathf.Max(playAreaMinZ, playAreaMaxZ);

        float inset = Mathf.Max(0f, wallInsetPadding);
        minX += inset;
        maxX -= inset;
        minZ += inset;
        maxZ -= inset;

        Vector3 pos = transform.position;
        float clampedX = Mathf.Clamp(pos.x, minX, maxX);
        float clampedZ = Mathf.Clamp(pos.z, minZ, maxZ);
        if (Mathf.Abs(pos.x - clampedX) > 0.0001f || Mathf.Abs(pos.z - clampedZ) > 0.0001f)
        {
            transform.position = new Vector3(clampedX, 1f, clampedZ);
            if (rb != null)
            {
                rb.linearVelocity = new Vector3(0f, 0f, 0f);
                rb.angularVelocity = Vector3.zero;
            }
            if (UsesRagHudRewardSystem())
                RagMlExtrinsicRewardHub.ApplyBoundaryViolationPenalty(this);
        }
    }

    private void CacheDefaultAgentColors()
    {
        defaultAgentColors.Clear();
        cachedAgentRenderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in cachedAgentRenderers)
        {
            if (renderer == null || renderer.material == null || IsBareCapsuleRenderer(renderer, transform))
            {
                continue;
            }

            Color baselineColor;
            if (TryGetMaterialColor(renderer.material, out baselineColor))
            {
                defaultAgentColors[renderer] = baselineColor;
            }
        }
    }

    private void SetAgentColor(Color color)
    {
        if (cachedAgentRenderers == null || cachedAgentRenderers.Length == 0)
        {
            CacheDefaultAgentColors();
        }

        foreach (Renderer renderer in cachedAgentRenderers)
        {
            if (renderer == null || renderer.material == null || IsBareCapsuleRenderer(renderer, transform))
            {
                continue;
            }

            TrySetMaterialColor(renderer.material, color);
        }
    }

    private void RestoreDefaultAgentColors()
    {
        foreach (KeyValuePair<Renderer, Color> kv in defaultAgentColors)
        {
            if (kv.Key == null || kv.Key.material == null)
            {
                continue;
            }

            TrySetMaterialColor(kv.Key.material, kv.Value);
        }
    }

    private void UpdateCognitiveCompletionHighlight()
    {
        if (cognitiveCompletionFlashRemaining <= 0f)
        {
            if (agentProximity != null)
            {
                agentProximity.SetExternalColorOverride(false);
            }
            if (!string.IsNullOrWhiteSpace(completedCognitiveHighlightTargetId))
            {
                completedCognitiveHighlightTargetId = null;
            }
            hasCompletedCognitiveHighlightTargetPos = false;
            completedCognitiveHighlightTargetPos = Vector3.zero;
            RestoreDefaultAgentColors();
            return;
        }

        cognitiveCompletionFlashRemaining -= Time.fixedDeltaTime;

        // Keep white for the full timer even after locomotion to the next cognitive station begins.
        // (Proximity-based early cancel made the flash invisible on the first frame of movement.)
        if (cognitiveCompletionFlashRemaining > 0f)
        {
            if (agentProximity != null)
            {
                agentProximity.SetExternalColorOverride(true);
            }
            SetAgentColor(Color.white);
        }
    }

    private void UpdateCognitivePenaltyFlash()
    {
        if (cognitivePenaltyFlashRemaining <= 0f) return;

        cognitivePenaltyFlashRemaining -= Time.fixedDeltaTime;
        if (cognitivePenaltyFlashRemaining > 0f)
        {
            if (agentProximity != null) agentProximity.SetExternalColorOverride(true);
            SetAgentColor(Color.black);
        }
        else
        {
            cognitivePenaltyFlashRemaining = 0f;
            RestoreDefaultAgentColors();
            if (agentProximity != null) agentProximity.SetExternalColorOverride(false);
        }
    }

    /// <summary>
    /// Exactly one incomplete layer isActive=true (current task); completed layers stay isActive=false, isCompleted=true.
    /// </summary>
    private static void ResetGoalBufferStackLayerStateForStep(ActionSequenceStep step)
    {
        if (!IsGoalBufferStackStep(step)) return;
        GoalBufferStackLayer[] stack = step.goalBufferContract.stack;
        for (int i = 0; i < stack.Length; i++)
        {
            if (stack[i] == null) continue;
            stack[i].isActive = false;
            stack[i].isCompleted = false;
        }
    }

    private static void RefreshGoalBufferStackActiveFlags(GoalBufferStackLayer[] stack, int currentProcessingIndex)
    {
        if (stack == null) return;
        for (int i = 0; i < stack.Length; i++)
        {
            GoalBufferStackLayer L = stack[i];
            if (L == null) continue;
            if (L.isCompleted)
            {
                L.isActive = false;
                continue;
            }

            L.isActive = i == currentProcessingIndex;
        }
    }

    private static bool IsGoalBufferStackStep(ActionSequenceStep step)
    {
        return step != null
            && string.Equals(step.currentCognitiveState, "GoalBuffer", StringComparison.OrdinalIgnoreCase)
            && step.goalBufferContract != null
            && step.goalBufferContract.stack != null
            && step.goalBufferContract.stack.Length > 0;
    }

    private static bool IsGoalBufferTripleStep(ActionSequenceStep step)
    {
        return step != null
            && string.Equals(step.currentCognitiveState, "GoalBuffer", StringComparison.OrdinalIgnoreCase)
            && step.goalBufferContract != null
            && step.goalBufferContract.IsTripleModeContract();
    }

    private bool ProcessGoalBufferTripleIfActive(
        ActionSequenceStep currentStep,
        string effectiveTargetId,
        string effectiveTargetName,
        Vector3 targetPos)
    {
        if (!IsGoalBufferTripleStep(currentStep)) return false;

        float priorResolvedDesire = currentStep.resolvedGoalBufferDesireLevel;
        string priorResolvedSource = currentStep.resolvedGoalBufferDesireSource ?? string.Empty;
        if (zoneMemory == null && zoneIndex >= 0)
            zoneMemory = ZoneDeclarativeMemory.ForZone(zoneIndex);

        GoalBufferDesireUtility.TryResolveDesire(currentStep, zoneMemory, out float resolvedDesire, out string desireSource);
        if (zoneMemory != null)
            zoneMemory.SetGoalBufferResolvedDesire(currentStep.stepId, resolvedDesire, desireSource);
        if (enableDetailedLogs
            && resolvedDesire > 0f
            && (!Mathf.Approximately(priorResolvedDesire, resolvedDesire)
                || !string.Equals(priorResolvedSource, desireSource, StringComparison.Ordinal)))
        {
            Debug.Log($"[{agentId}] Goal Buffer desire reference resolved to {resolvedDesire:F1} via {desireSource}");
        }

        if (cognitivePenaltyFlashRemaining > 0f)
            return true;

        if (goalBufferPostPenaltyWait > 0f)
        {
            goalBufferPostPenaltyWait -= Time.fixedDeltaTime;
            return true;
        }

        if (goalBufferTripleSlotOrdinal < 0)
        {
            goalBufferTripleSlotOrdinal = 0;
            goalBufferTripleSlotTimer = 0f;
            zoneMemory?.ClearGoalBufferTripleExtractions();
            GoalBufferStationPresenter pres0 = GoalBufferStationPresenter.TryGetForZone(zoneIndex);
            pres0?.ApplyPreviewForContract(currentStep.goalBufferContract);
        }

        while (goalBufferTripleSlotOrdinal < 3
               && GoalBufferTripleOrchestrator.GetSlotText(currentStep.goalBufferContract, (GoalBufferSlot)goalBufferTripleSlotOrdinal) == null)
        {
            goalBufferTripleSlotOrdinal++;
        }

        if (goalBufferTripleSlotOrdinal >= 3)
        {
            CompleteGoalBufferTripleCognitiveStep(currentStep, effectiveTargetId, effectiveTargetName, targetPos);
            return true;
        }

        goalBufferTripleSlotTimer += Time.fixedDeltaTime;
        float need = GoalBufferTripleOrchestrator.PerSlotDwellSeconds(currentStep, currentStep.goalBufferContract);
        if (goalBufferTripleSlotTimer < need)
            return true;

        GoalBufferSlot slot = (GoalBufferSlot)goalBufferTripleSlotOrdinal;
        string text = GoalBufferTripleOrchestrator.GetSlotText(currentStep.goalBufferContract, slot);
        if (text != null)
        {
            GoalBufferTripleOrchestrator.WriteSlotToZoneMemory(zoneMemory, currentStep.stepId, slot, text);
            GoalBufferStationPresenter.TryGetForZone(zoneIndex)?.PlayGrab(slot);
            if (_humanWalkAnim == null)
                _humanWalkAnim = GetComponent<HumanWalkAnimation>() ?? GetComponentInChildren<HumanWalkAnimation>(true);
            _humanWalkAnim?.PlayGoalBufferGrabPulse();
        }

        goalBufferTripleSlotOrdinal++;
        goalBufferTripleSlotTimer = 0f;

        while (goalBufferTripleSlotOrdinal < 3
               && GoalBufferTripleOrchestrator.GetSlotText(currentStep.goalBufferContract, (GoalBufferSlot)goalBufferTripleSlotOrdinal) == null)
        {
            goalBufferTripleSlotOrdinal++;
        }

        if (goalBufferTripleSlotOrdinal >= 3)
            CompleteGoalBufferTripleCognitiveStep(currentStep, effectiveTargetId, effectiveTargetName, targetPos);

        return true;
    }

    private void CompleteGoalBufferTripleCognitiveStep(
        ActionSequenceStep currentStep,
        string effectiveTargetId,
        string effectiveTargetName,
        Vector3 targetPos)
    {
        mySequence.MarkStepCompleted();
        CompleteCognitiveStepRecord(currentStep, effectiveTargetId, effectiveTargetName, targetPos, true, UsesRagHudRewardSystem() ? 1f : 0f);
        ApplyRagCognitiveStepSuccess(currentStep);
        TriggerCognitiveCompletionFlash(effectiveTargetId, targetPos);
        NotifyCognitiveStepCompleted(currentStep, effectiveTargetId);

        Debug.Log($"🏁 [{agentId}] Goal Buffer triple step done: {currentStep.stepId}");
        bool wasLastStep = !mySequence.HasNextStep();
        mySequence.MoveToNextStep();
        activeCognitiveStepId = null;
        activeCognitiveMemoryStepId = null;
        activeCognitiveRoute.Clear();
        activeCognitiveRouteIndex = 0;
        ClearCognitiveThreadLine();
        timeAtTarget = 0f;
        goalBufferTripleSlotOrdinal = -1;
        goalBufferTripleSlotTimer = 0f;
        goalBufferPostPenaltyWait = 0f;
        cognitivePenaltyFlashRemaining = 0f;
        if (wasLastStep)
            CheckAndRewardSequenceCompletion();
    }

    void UpdateImaginalThoughtBubbleForCognitiveHold(ActionSequenceStep currentStep)
    {
        if (agentRole != AgentRole.Mental)
        {
            _imaginalThoughtBubble?.Hide();
            return;
        }

        if (currentStep == null || !ImaginalBufferStepHelper.IsImaginalBufferStep(currentStep))
        {
            _imaginalThoughtBubble?.Hide();
            return;
        }

        if (_imaginalThoughtBubble == null)
            _imaginalThoughtBubble = ImaginalThoughtBubble.GetOrCreate(transform);
        _imaginalThoughtBubble.Show(BuildImaginalBubbleText(currentStep));
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

    private bool ProcessGoalBufferStackIfActive(ActionSequenceStep currentStep, string effectiveTargetId, string effectiveTargetName, Vector3 targetPos)
    {
        if (!IsGoalBufferStackStep(currentStep)) return false;

        float priorResolvedDesire = currentStep.resolvedGoalBufferDesireLevel;
        string priorResolvedSource = currentStep.resolvedGoalBufferDesireSource ?? string.Empty;
        if (zoneMemory == null && zoneIndex >= 0)
            zoneMemory = ZoneDeclarativeMemory.ForZone(zoneIndex);

        GoalBufferDesireUtility.TryResolveDesire(currentStep, zoneMemory, out float resolvedDesire, out string desireSource);
        if (zoneMemory != null)
            zoneMemory.SetGoalBufferResolvedDesire(currentStep.stepId, resolvedDesire, desireSource);
        if (enableDetailedLogs
            && resolvedDesire > 0f
            && (!Mathf.Approximately(priorResolvedDesire, resolvedDesire)
                || !string.Equals(priorResolvedSource, desireSource, StringComparison.Ordinal)))
        {
            Debug.Log($"[{agentId}] Goal Buffer desire reference resolved to {resolvedDesire:F1} via {desireSource}");
        }

        if (cognitivePenaltyFlashRemaining > 0f)
            return true;

        if (goalBufferPostPenaltyWait > 0f)
        {
            goalBufferPostPenaltyWait -= Time.fixedDeltaTime;
            return true;
        }

        GoalBufferStackLayer[] stack = currentStep.goalBufferContract.stack;
        if (goalBufferLayerIndex < 0)
        {
            goalBufferLayerIndex = 0;
            goalBufferLayerHoldTimer = 0f;
        }

        while (goalBufferLayerIndex < stack.Length)
        {
            GoalBufferStackLayer L = stack[goalBufferLayerIndex];
            if (L != null && !L.isCompleted) break;
            goalBufferLayerIndex++;
            goalBufferLayerHoldTimer = 0f;
        }

        if (goalBufferLayerIndex >= stack.Length)
        {
            CompleteGoalBufferStackCognitiveStep(currentStep, effectiveTargetId, effectiveTargetName, targetPos);
            return true;
        }

        RefreshGoalBufferStackActiveFlags(stack, goalBufferLayerIndex);

        GoalBufferStackLayer layer = stack[goalBufferLayerIndex];
        float currentDynamicSkillLevel = GetDynamicSkillLevel();
        if (!GoalBufferDesireUtility.IsLayerEligible(currentDynamicSkillLevel, layer.desireLevel))
        {
            layer.retryAttempt++;
            layer.isActive = false;
            layer.isCompleted = false;

            if (enableDetailedLogs)
            {
                Debug.LogWarning(
                    $"[{agentId}] Goal Buffer layer {layer.position}/{layer.type} skipped: " +
                    $"skill {currentDynamicSkillLevel:F1} < desire {layer.desireLevel:F1}");
            }

            goalBufferLayerIndex++;
            goalBufferLayerHoldTimer = 0f;

            while (goalBufferLayerIndex < stack.Length)
            {
                GoalBufferStackLayer skipped = stack[goalBufferLayerIndex];
                if (skipped != null && !skipped.isCompleted) break;
                goalBufferLayerIndex++;
                goalBufferLayerHoldTimer = 0f;
            }

            if (goalBufferLayerIndex >= stack.Length)
                CompleteGoalBufferStackCognitiveStep(currentStep, effectiveTargetId, effectiveTargetName, targetPos);

            return true;
        }

        goalBufferLayerHoldTimer += Time.fixedDeltaTime;
        float need = Mathf.Max(0.12f, layer.expectedDuration);
        if (goalBufferLayerHoldTimer < need)
            return true;

        if (goalBufferScriptedMissProbability > 0f && UnityEngine.Random.value < goalBufferScriptedMissProbability)
        {
            cognitivePenaltyFlashRemaining = 1f;
            int penBase = layer.penalty > 0 ? layer.penalty : 2;
            AddCognitiveStackPenalty(penBase);
            goalBufferLayerHoldTimer = 0f;
            goalBufferPostPenaltyWait = Mathf.Max(0.5f, layer.expectedDuration);
            return true;
        }

        string profileId = NormalizeToSkillId(agentId);
        string diskErr = null;
        string diskValue = null;
        bool diskOk = !string.IsNullOrEmpty(profileId) &&
            GoalBufferContractParser.TryReadGoalBufferLayerForRuntime(
                profileId, layer.position, layer.type,
                out diskValue, out diskErr);
        if (!diskOk)
        {
            if (layer.retryAttempt < GoalBufferDiskMaxRetries)
                layer.retryAttempt++;
            cognitivePenaltyFlashRemaining = 1f;
            int penBaseDisk = layer.penalty > 0 ? layer.penalty : 1;
            AddCognitiveStackPenalty(penBaseDisk);
            goalBufferLayerHoldTimer = 0f;
            goalBufferPostPenaltyWait = Mathf.Max(0.5f, layer.expectedDuration);
            if (enableDetailedLogs)
            {
                string reason = string.IsNullOrEmpty(profileId) ? "no_profile_id" : diskErr;
                Debug.LogWarning($"[{agentId}] Goal Buffer disk validation failed {layer.position}/{layer.type}: {reason} — retry");
            }
            return true;
        }

        layer.value = diskValue;
        RecordGoalBufferLayerDeclarative(currentStep, layer, diskValue);
        if (layer.reward > 0)
            AddCognitiveStepReward(layer.reward);

        layer.isActive = false;
        layer.isCompleted = true;

        goalBufferLayerIndex++;
        goalBufferLayerHoldTimer = 0f;

        while (goalBufferLayerIndex < stack.Length)
        {
            GoalBufferStackLayer L2 = stack[goalBufferLayerIndex];
            if (L2 != null && !L2.isCompleted) break;
            goalBufferLayerIndex++;
            goalBufferLayerHoldTimer = 0f;
        }

        if (goalBufferLayerIndex >= stack.Length)
            CompleteGoalBufferStackCognitiveStep(currentStep, effectiveTargetId, effectiveTargetName, targetPos);

        return true;
    }

    public float GetDynamicSkillLevel()
    {
        if (skillSystem != null && skillSystem.IsDataReady())
        {
            string skillId = NormalizeToSkillId(agentId);
            AgentProfile agentProfile = skillSystem.GetAgentProfile(skillId);
            if (agentProfile != null)
            {
                skillLevel = agentProfile.skillLevel;
                return skillLevel;
            }
        }

        return skillLevel;
    }

    private void RecordGoalBufferLayerDeclarative(ActionSequenceStep step, GoalBufferStackLayer layer, string valueOverride = null)
    {
        if (zoneIndex < 0 || step == null || layer == null) return;

        if (zoneMemory == null)
            zoneMemory = ZoneDeclarativeMemory.ForZone(zoneIndex);
        if (zoneMemory == null) return;

        string key = string.IsNullOrEmpty(layer.type) ? "goal_buffer_layer" : layer.type;
        string val = valueOverride ?? layer.value ?? "";
        zoneMemory.RecordCognitiveStep(step.stepId, key, val);
    }

    private void CompleteGoalBufferStackCognitiveStep(ActionSequenceStep currentStep, string effectiveTargetId, string effectiveTargetName, Vector3 targetPos)
    {
        mySequence.MarkStepCompleted();
        CompleteCognitiveStepRecord(currentStep, effectiveTargetId, effectiveTargetName, targetPos, true, UsesRagHudRewardSystem() ? 1f : 0f);
        ApplyRagCognitiveStepSuccess(currentStep);
        TriggerCognitiveCompletionFlash(effectiveTargetId, targetPos);
        NotifyCognitiveStepCompleted(currentStep, effectiveTargetId);

        Debug.Log($"🏁 [{agentId}] Goal Buffer stack step done: {currentStep.stepId} (per-layer rewards applied)");
        bool wasLastStep = !mySequence.HasNextStep();
        mySequence.MoveToNextStep();
        activeCognitiveStepId = null;
        activeCognitiveMemoryStepId = null;
        activeCognitiveRoute.Clear();
        activeCognitiveRouteIndex = 0;
        ClearCognitiveThreadLine();
        timeAtTarget = 0f;
        goalBufferLayerIndex = -1;
        goalBufferLayerHoldTimer = 0f;
        goalBufferPostPenaltyWait = 0f;
        cognitivePenaltyFlashRemaining = 0f;
        if (wasLastStep)
            CheckAndRewardSequenceCompletion();
    }

    private static bool TryGetMaterialColor(Material mat, out Color color)
    {
        color = Color.white;
        if (mat == null)
        {
            return false;
        }

        if (mat.HasProperty("_Color"))
        {
            color = mat.color;
            return true;
        }

        if (mat.HasProperty("_BaseColor"))
        {
            color = mat.GetColor("_BaseColor");
            return true;
        }

        return false;
    }

    private static bool TrySetMaterialColor(Material mat, Color color)
    {
        if (mat == null)
        {
            return false;
        }

        bool updated = false;
        if (mat.HasProperty("_Color"))
        {
            mat.color = color;
            updated = true;
        }

        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", color);
            updated = true;
        }

        return updated;
    }

    private void TriggerCognitiveCompletionFlash(string targetId, Vector3 targetPos)
    {
        completedCognitiveHighlightTargetId = targetId;
        completedCognitiveHighlightTargetPos = targetPos;
        hasCompletedCognitiveHighlightTargetPos = true;
        cognitiveCompletionFlashRemaining = CognitiveCompletionFlashDurationSeconds;
        if (agentProximity != null)
        {
            agentProximity.SetExternalColorOverride(true);
        }
        SetAgentColor(Color.white);
    }

    private void NotifyCognitiveStepCompleted(ActionSequenceStep step, string effectiveTargetId)
    {
        if (step == null)
        {
            return;
        }

        NotifyCognitivePhaseOrchestratorStepCompleted(step.stepId);

        if (PersonaCognitiveControlSystem.Instance != null)
        {
            PersonaCognitiveControlSystem.Instance.NotifyCognitiveStepCompleted(agentId, step.stepId, effectiveTargetId);
            PersonaCognitiveControlSystem.Instance.NotifyStepTransition(agentId, step.stepId);
        }

        if (MLTrainingResultsWriter.Instance != null)
        {
            MLTrainingResultsWriter.Instance.UpdateAgentResults(agentId);
        }
    }

    private AgentCognitiveMemory GetCognitiveMemory()
    {
        if (cognitiveMemory == null)
        {
            cognitiveMemory = GetComponent<AgentCognitiveMemory>();
            if (cognitiveMemory == null)
            {
                cognitiveMemory = gameObject.AddComponent<AgentCognitiveMemory>();
            }
        }

        return cognitiveMemory;
    }

    private ActionSequenceStep GetOperationalStepContext()
    {
        ActionSequenceStep operationalStep = actionSequenceData != null ? actionSequenceData.GetCurrentStep() : null;
        if (operationalStep == null)
        {
            actionSequenceData = GetActionSequenceWithFallback();
            operationalStep = actionSequenceData != null ? actionSequenceData.GetCurrentStep() : null;
        }

        return operationalStep;
    }

    private static string ResolveDisplayTarget(string targetId, string targetName)
    {
        if (!string.IsNullOrWhiteSpace(targetName))
        {
            return targetName;
        }

        if (!string.IsNullOrWhiteSpace(targetId))
        {
            return targetId;
        }

        return "unknown_target";
    }

    private void BeginCognitiveStepRecord(ActionSequenceStep step, string sourceStationId)
    {
        if (step == null || string.IsNullOrWhiteSpace(step.stepId))
        {
            return;
        }

        if (string.Equals(activeCognitiveMemoryStepId, step.stepId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        AgentCognitiveMemory memory = GetCognitiveMemory();
        memory.BeginCognitiveStep(
            step.stepId,
            step.stepOrder,
            step.currentCognitiveState,
            step.targetObjectId,
            sourceStationId);
        activeCognitiveMemoryStepId = step.stepId;
    }

    /// <summary>
    /// P-agent JSON cognitive steps update AgentCognitiveMemory only unless we also mirror here.
    /// ZoneDeclarativeMemory is what the ACT-R declarative HUD reads; M-agents write it too — this keeps RAG progress and blackboard in sync.
    /// </summary>
    private void PushCognitiveOutputsToZoneDeclarative(
        ActionSequenceStep step,
        string intention,
        string goal,
        string visualSummary,
        string retrievalSummary,
        string declarativeMatch,
        string productionDecision,
        string motorCommand,
        float pAgentCognitiveReward)
    {
        if (agentRole != AgentRole.Physical || zoneIndex < 0) return;
        if (!cognitivePhaseActive) return;

        if (zoneMemory == null)
            zoneMemory = ZoneDeclarativeMemory.ForZone(zoneIndex);
        if (zoneMemory == null) return;

        string sid = string.IsNullOrEmpty(step?.stepId) ? "cog_step" : step.stepId;

        void Rec(string key, string val)
        {
            if (string.IsNullOrWhiteSpace(val)) return;
            zoneMemory.RecordCognitiveStep(sid, key, val);
        }

        Rec("intention", intention);
        Rec("goal", goal);
        Rec("visual_summary", visualSummary);
        Rec("retrieval_schema", retrievalSummary);
        Rec("declarative_match", declarativeMatch);
        Rec("production_rule", productionDecision);
        Rec("motor_command", motorCommand);

        zoneMemory.MarkCognitiveStepComplete(sid);

        float add = pAgentCognitiveReward >= 0f
            ? Mathf.Max(zoneMemory.rewardPerStep, pAgentCognitiveReward * 0.05f)
            : zoneMemory.rewardPerStep;
        zoneMemory.AddStepReward(add, sid);
    }

    private void PushBranchCognitiveStepToZoneDeclarative(ActionSequenceStep step)
    {
        if (agentRole != AgentRole.Physical || zoneIndex < 0 || step == null) return;
        if (!cognitivePhaseActive) return;

        if (zoneMemory == null)
            zoneMemory = ZoneDeclarativeMemory.ForZone(zoneIndex);
        if (zoneMemory == null) return;

        string sid = string.IsNullOrEmpty(step.stepId) ? "branch" : step.stepId;
        string note = string.IsNullOrEmpty(step.description)
            ? $"{step.actionType} @ {step.currentCognitiveState}"
            : step.description;
        zoneMemory.RecordCognitiveStep(sid, "branch_transition", note);
        zoneMemory.MarkCognitiveStepComplete(sid);
        zoneMemory.AddStepReward(zoneMemory.rewardPerStep, sid);
    }

    private void CompleteCognitiveStepRecord(
        ActionSequenceStep step,
        string effectiveTargetId,
        string effectiveTargetName,
        Vector3 targetPos,
        bool targetResolved,
        float pAgentCognitiveRewardForZone = -1f)
    {
        if (step == null || string.IsNullOrWhiteSpace(step.stepId))
        {
            return;
        }

        AgentCognitiveMemory memory = GetCognitiveMemory();
        ActionSequenceStep operational = GetOperationalStepContext();
        string operationalTargetId = operational != null ? operational.targetObjectId : string.Empty;
        string operationalTargetName = operational != null ? operational.targetObjectName : string.Empty;
        string operationalAction = operational != null ? operational.actionType : "move";
        string operationalStepId = operational != null ? operational.stepId : "unknown_step";
        string resolvedTargetLabel = ResolveDisplayTarget(operationalTargetId, operationalTargetName);
        string activeCognitiveTarget = ResolveDisplayTarget(effectiveTargetId, effectiveTargetName);

        string priorIntention = memory.TryGet("intention", out string existingIntention) ? existingIntention : string.Empty;
        string priorGoal = memory.TryGet("goal", out string existingGoal) ? existingGoal : string.Empty;
        string priorVisual = memory.TryGet("visual_info", out string existingVisual) ? existingVisual : string.Empty;
        string priorRetrieval = memory.TryGet("retrieved_schema", out string existingRetrieval) ? existingRetrieval : string.Empty;
        string priorDeclarative = memory.TryGet("declarative_match", out string existingDeclarative) ? existingDeclarative : string.Empty;
        string priorProduction = memory.TryGet("production_rule", out string existingProduction) ? existingProduction : string.Empty;
        string priorMotor = memory.TryGet("command_state", out string existingMotor) ? existingMotor : string.Empty;

        string state = (step.currentCognitiveState ?? string.Empty).ToLowerInvariant();
        string intention = priorIntention;
        string goal = priorGoal;
        string visualSummary = priorVisual;
        string retrievalSummary = priorRetrieval;
        string declarativeMatch = priorDeclarative;
        string productionDecision = priorProduction;
        string motorCommand = priorMotor;

        string targetMeta = targetResolved
            ? $"target={activeCognitiveTarget} pos=({targetPos.x:F1},{targetPos.z:F1})"
            : $"target-missing fallback={activeCognitiveTarget}";

        if (state.Contains("intent"))
        {
            intention = $"Intent: {operationalAction} toward {resolvedTargetLabel} ({operationalStepId})";
        }
        else if (state.Contains("goal"))
        {
            goal = $"Goal: complete {operationalStepId} with {resolvedTargetLabel}";
        }
        else if (state.Contains("visual"))
        {
            visualSummary = $"Visual observe: {targetMeta}";
        }
        else if (state.Contains("environment"))
        {
            visualSummary = $"Environment scan selected operational target {resolvedTargetLabel} ({operationalTargetId})";
        }
        else if (state.Contains("retriev"))
        {
            retrievalSummary = $"Retrieved context token [{operationalAction}:{operationalTargetId}] from {activeCognitiveTarget}";
        }
        else if (state.Contains("declarative"))
        {
            declarativeMatch = $"Declarative match: schema({operationalAction}) compatible with {resolvedTargetLabel}";
        }
        else if (state.Contains("production"))
        {
            productionDecision = $"Production rule: IF ready THEN {operationalAction} {operationalTargetId}";
        }
        else if (state.Contains("motor"))
        {
            motorCommand = $"Motor command: execute {operationalAction} to {resolvedTargetLabel}";
        }

        memory.CompleteCognitiveStep(
            step.stepId,
            step.stepOrder,
            step.currentCognitiveState,
            effectiveTargetId,
            effectiveTargetId,
            intention,
            goal,
            visualSummary,
            retrievalSummary,
            declarativeMatch,
            productionDecision,
            motorCommand);

        PushCognitiveOutputsToZoneDeclarative(
            step,
            intention,
            goal,
            visualSummary,
            retrievalSummary,
            declarativeMatch,
            productionDecision,
            motorCommand,
            pAgentCognitiveRewardForZone);

        activeCognitiveMemoryStepId = null;
    }

    private void ExecuteCognitiveScriptedStep()
    {
        ActionSequenceStep currentStep = mySequence.GetCurrentStep();
        if (currentStep == null)
        {
            ClearCognitiveThreadLine();
            CheckAndRewardSequenceCompletion();
            return;
        }

        // Branch/dispatch steps have no physical target — complete them immediately so sequence advances.
        bool isBranchStep = string.Equals(currentStep.actionType, "branch", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(currentStep.actionType, "dispatch", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(currentStep.actionType, "transition", StringComparison.OrdinalIgnoreCase);
        if (isBranchStep)
        {
            if (!currentStep.isActivated)
            {
                currentStep.isActivated = true;
                Debug.Log($"🧠 [{agentId}] Branch/dispatch step {currentStep.stepId} auto-completed (no physical target)");
            }
            mySequence.MarkStepCompleted();
            ApplyRagCognitiveStepSuccess(currentStep);
            NotifyCognitiveStepCompleted(currentStep, currentStep.stepId);
            PushBranchCognitiveStepToZoneDeclarative(currentStep);
            bool wasLastStep = !mySequence.HasNextStep();
            mySequence.MoveToNextStep();
            activeCognitiveStepId = null;
            activeCognitiveMemoryStepId = null;
            activeCognitiveRoute.Clear();
            activeCognitiveRouteIndex = 0;
            ClearCognitiveThreadLine();
            timeAtTarget = 0f;
            if (wasLastStep) CheckAndRewardSequenceCompletion();
            return;
        }

        string effectiveTargetId = currentStep.targetObjectId;
        string effectiveTargetName = currentStep.targetObjectName;
        ResolveDynamicCognitiveTarget(currentStep, ref effectiveTargetId, ref effectiveTargetName);
        Vector3? targetPosMaybe = sequenceManager != null ? sequenceManager.GetTargetPositionById(effectiveTargetId, zoneIndex) : null;
        if (!targetPosMaybe.HasValue)
        {
            Debug.LogWarning($"⚠️ [{agentId}] Cognitive step target not found: {effectiveTargetId} (from {currentStep.stepId})");
            BeginCognitiveStepRecord(currentStep, effectiveTargetId);
            mySequence.MarkStepCompleted();
            CompleteCognitiveStepRecord(currentStep, effectiveTargetId, effectiveTargetName, transform.position, false, -1f);
            NotifyCognitiveStepCompleted(currentStep, effectiveTargetId);
            mySequence.MoveToNextStep();
            activeCognitiveStepId = null;
            activeCognitiveMemoryStepId = null;
            ClearCognitiveThreadLine();
            return;
        }

        Vector3 targetPos = targetPosMaybe.Value;
        targetPos.y = 1f;

        if (!string.Equals(activeCognitiveStepId, currentStep.stepId, StringComparison.OrdinalIgnoreCase))
        {
            activeCognitiveStepId = currentStep.stepId;
            BeginCognitiveStepRecord(currentStep, effectiveTargetId);
            activeCognitiveRoute.Clear();
            activeCognitiveRouteIndex = 0;
            goalBufferLayerIndex = -1;
            goalBufferLayerHoldTimer = 0f;
            goalBufferTripleSlotOrdinal = -1;
            goalBufferTripleSlotTimer = 0f;
            goalBufferPostPenaltyWait = 0f;
            cognitivePenaltyFlashRemaining = 0f;
            ResetGoalBufferStackLayerStateForStep(currentStep);
            if (useActrCognitiveRoute)
            {
                TryBuildCognitiveRoute(currentStep, out activeCognitiveRoute);
            }
        }

        // Optional deterministic ACT-R route points before direct target handoff.
        if (useActrCognitiveRoute && activeCognitiveRoute != null && activeCognitiveRouteIndex < activeCognitiveRoute.Count)
        {
            float routeDuration = Mathf.Max(0.1f, currentStep.expectedDuration * 0.25f);
            float perNode = routeDuration / Mathf.Max(1, activeCognitiveRoute.Count);
            Vector3 nodeTarget = activeCognitiveRoute[activeCognitiveRouteIndex];
            float nodeDistance = Vector3.Distance(transform.position, nodeTarget);
            float nodeSpeed = Mathf.Max(minCognitiveMoveSpeed, (nodeDistance / Mathf.Max(0.01f, perNode)) * cognitiveMovementSpeedMultiplier);
            Vector3 routePos = Vector3.MoveTowards(transform.position, nodeTarget, nodeSpeed * Time.fixedDeltaTime);
            routePos.y = 1f;
            transform.position = routePos;

            if (Vector3.Distance(transform.position, nodeTarget) <= 1.2f)
            {
                activeCognitiveRouteIndex++;
            }
        }

        UpdateCognitiveThreadLine(targetPos);

        // Mode B fallback and final approach: direct MoveTowards target.
        float targetDistance = Vector3.Distance(transform.position, targetPos);
        float effectiveDuration = Mathf.Max(0.05f, currentStep.expectedDuration);
        float desiredSpeed = (targetDistance / effectiveDuration) * Mathf.Max(1f, cognitiveMovementSpeedMultiplier);
        float moveSpeed = Mathf.Max(minCognitiveMoveSpeed, desiredSpeed);
        Vector3 nextPos = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.fixedDeltaTime);
        nextPos.y = 1f;
        transform.position = nextPos;
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        const float stopDistance = 3.5f;
        float dist = Vector3.Distance(transform.position, targetPos);
        if (dist <= stopDistance)
        {
            if (!currentStep.isActivated)
            {
                currentStep.isActivated = true;
                Debug.Log($"🧠 [{agentId}] Activated cognitive step {currentStep.stepId} at {effectiveTargetId} ({currentStep.currentCognitiveState})");
            }

            // Goal Buffer: RAG initial_state triple (bottom/middle/top) or legacy stack + disk.
            if (ProcessGoalBufferTripleIfActive(currentStep, effectiveTargetId, effectiveTargetName, targetPos))
            {
                _imaginalThoughtBubble?.Hide();
                return;
            }
            if (ProcessGoalBufferStackIfActive(currentStep, effectiveTargetId, effectiveTargetName, targetPos))
            {
                _imaginalThoughtBubble?.Hide();
                return;
            }

            UpdateImaginalThoughtBubbleForCognitiveHold(currentStep);

            timeAtTarget += Time.fixedDeltaTime;
            CognitiveProcessManager.EnsureExists();
            float requiredHold = CognitiveProcessManager.Instance != null
                ? CognitiveProcessManager.Instance.ResolveDwellSeconds(currentStep, 0f, 2.5f)
                : Mathf.Max(0.12f, currentStep.expectedDuration);
            if (timeAtTarget >= requiredHold)
            {
                _imaginalThoughtBubble?.Hide();
                mySequence.MarkStepCompleted();
                if (UsesRagHudRewardSystem())
                {
                    CompleteCognitiveStepRecord(currentStep, effectiveTargetId, effectiveTargetName, targetPos, true, 1f);
                    ApplyRagCognitiveStepSuccess(currentStep);
                }
                else
                {
                    float completionReward = 4.0f;
                    CompleteCognitiveStepRecord(currentStep, effectiveTargetId, effectiveTargetName, targetPos, true, completionReward);
                    AddReward(completionReward);
                    episodeTotalReward += completionReward;
                    UpdateAgentSkillLevelFromReward(completionReward);
                }
                TriggerCognitiveCompletionFlash(effectiveTargetId, targetPos);
                NotifyCognitiveStepCompleted(currentStep, effectiveTargetId);

                Debug.Log($"🏁 [{agentId}] Cognitive step completed: {currentStep.stepId} (held {timeAtTarget:F2}s / required {requiredHold:F2}s)");
                bool wasLastStep = !mySequence.HasNextStep();
                mySequence.MoveToNextStep();
                activeCognitiveStepId = null;
                activeCognitiveMemoryStepId = null;
                activeCognitiveRoute.Clear();
                activeCognitiveRouteIndex = 0;
                ClearCognitiveThreadLine();
                timeAtTarget = 0f;
                if (wasLastStep)
                {
                    CheckAndRewardSequenceCompletion();
                }
            }
        }
        else
        {
            _imaginalThoughtBubble?.Hide();
            timeAtTarget = 0f;
            if (IsGoalBufferStackStep(currentStep))
            {
                goalBufferLayerIndex = -1;
                goalBufferLayerHoldTimer = 0f;
            }
            if (IsGoalBufferTripleStep(currentStep))
            {
                goalBufferTripleSlotOrdinal = -1;
                goalBufferTripleSlotTimer = 0f;
            }
        }
    }

    // ── Physical scripted movement (fallback when no trainer / HeuristicOnly) ───
    // Used when UseScriptedPhysicalLocomotion() is true; otherwise RL applies in OnActionReceived.
    private string  activePhysicalStepId    = null;
    private Vector3 physicalStuckLastPos    = Vector3.zero;
    private float   physicalStuckTimer      = 0f;
    private Vector3 physicalDeflectDir      = Vector3.zero;
    private float   physicalDeflectRemaining = 0f;
    private const float PHYSICAL_STUCK_TIMEOUT  = 1.8f;  // seconds without progress = deflect
    private const float PHYSICAL_DEFLECT_SECS   = 1.0f;  // how long to move in deflect direction

    /// <summary>
    /// Called every FixedUpdate while the physical action phase is active.
    /// Resolves the current step's real scene-object position and registers it into
    /// AgentSequenceManager so RL observations (direction/distance) and
    /// CalculatePathfindingRewards always point at the correct physical target.
    /// </summary>
    private void SyncPhysicalStepTarget()
    {
        if (sequenceManager == null || mySequence == null) return;

        ActionSequenceStep currentStep = mySequence.GetCurrentStep();
        if (currentStep == null)
        {
            CheckAndRewardSequenceCompletion();
            return;
        }

        // Only re-sync when we move to a new step (avoids redundant scene lookups every frame).
        if (activePhysicalStepId == currentStep.stepId) return;

        activePhysicalStepId     = currentStep.stepId;
        timeAtTarget             = 0f;
        isAtTarget               = false;
        timeReachedTarget        = 0f;
        physicalStuckTimer       = 0f;
        physicalStuckLastPos     = transform.position;
        physicalDeflectRemaining = 0f;

        if (RagMenuController.IsMenuStep(currentStep))
        {
            if (IsMenuOpenedForStep(currentStep))
            {
                RagMenuController.EnsureInScene()?.ShowMenuForStep(currentStep, zoneIndex);
                RagMenuController.EnsureInScene()?.ReassertVisibleMenus();
            }
            else
                RagMenuController.EnsureInScene()?.HideAllMenus();
        }

        // In-place act/scene_ steps reference virtual objects with no Unity scene position.
        // Register the agent's current position directly so EvaluatePhysicalStepArrival
        // can accumulate dwell time immediately without failing the position lookup.
        if (IsInPlacePhysicalStep(currentStep))
        {
            Vector3 inPlacePos = transform.position;
            sequenceManager.RegisterToolPosition(currentStep.targetObjectId, inPlacePos);
            Debug.Log($"▶ [{agentId}] Physical RL step START (in-place act): {currentStep.stepId} → {currentStep.targetObjectId}");
            if (PersonaCognitiveControlSystem.Instance != null)
                PersonaCognitiveControlSystem.Instance.NotifyFirstActionStarted(agentId);
            return;
        }

        // ── Use cognitive output as primary target source ──────────────────────
        // The M-agents write the resolved physical target into ZoneDeclarativeMemory
        // during their cognitive pass (motor command / resolvedTargetId slot).
        // We use that instead of the raw JSON targetObjectId so the physical agent
        // acts on what the cognitive process decided, not hard-coded JSON values.
        string cognitiveTarget = null;
        if (zoneMemory != null && !string.IsNullOrWhiteSpace(zoneMemory.resolvedTargetId))
            cognitiveTarget = zoneMemory.resolvedTargetId;

        // Also check if the cognitive process stored a step-specific override in dataSlots
        // (M-agent may write e.g. "step1_target" or "motor_target" during branch C).
        if (string.IsNullOrWhiteSpace(cognitiveTarget) && zoneMemory != null)
        {
            zoneMemory.TryGet("motor_target",   out cognitiveTarget);
            if (string.IsNullOrWhiteSpace(cognitiveTarget))
                zoneMemory.TryGet("resolved_target", out cognitiveTarget);
        }

        // Only apply the cognitive override to "move" steps (learn steps always reference
        // the specific tool from JSON since each step targets a different skill station).
        string targetId;
        if (currentStep.actionType == "move" && !string.IsNullOrWhiteSpace(cognitiveTarget))
        {
            targetId = cognitiveTarget;
            Debug.Log($"▶ [{agentId}] Physical RL step START (cognitive target): {currentStep.stepId} → {targetId}");
        }
        else
        {
            // Fallback: use JSON actionSequence targetObjectId
            targetId = ResolvePhysicalStepTargetId(currentStep);
            Debug.Log($"▶ [{agentId}] Physical RL step START (json fallback): {currentStep.stepId} → {targetId}");
        }

        // Resolve the actual world position directly from the scene (avoids cache pollution).
        Vector3? pos = ResolvePhysicalTargetPosition(targetId);

        // If cognitive target not found in scene, fall back to JSON target.
        if (!pos.HasValue && !string.IsNullOrWhiteSpace(cognitiveTarget) && targetId != PhysicalStepTargetResolver.ResolveObjectId(currentStep, zoneIndex))
        {
            string jsonFallback = PhysicalStepTargetResolver.ResolveObjectId(currentStep, zoneIndex);
            Debug.LogWarning($"[{agentId}] Cognitive target '{targetId}' not found in scene — falling back to physical target '{jsonFallback}'");
            targetId = jsonFallback;
            pos = ResolvePhysicalTargetPosition(targetId);
        }

        if (pos.HasValue)
        {
            sequenceManager.RegisterToolPosition(targetId, pos.Value);
            string aliasId = PhysicalStepTargetResolver.ResolveObjectId(currentStep, zoneIndex);
            if (!string.IsNullOrWhiteSpace(aliasId) && !string.Equals(aliasId, targetId, StringComparison.OrdinalIgnoreCase))
                sequenceManager.RegisterToolPosition(aliasId, pos.Value);
        }
        else
        {
            Debug.LogWarning($"[{agentId}] SyncPhysicalStepTarget: target not found in scene: {targetId} – skipping step");
            mySequence.MarkStepCompleted();
            NotifyCognitivePhaseOrchestratorStepCompleted(currentStep.stepId);
            if (_ragOrchestratorPhysicalMode)
                FinishOrchestratorPhysicalStep();
            else
                mySequence.MoveToNextStep();
            activePhysicalStepId = null;
        }

        if (PersonaCognitiveControlSystem.Instance != null)
        {
            PersonaCognitiveControlSystem.Instance.NotifyFirstActionStarted(agentId);
            // Do NOT call NotifyStepTransition here — that triggers RunPerStepAnalysis
            // which temporarily closes the gate and blocks RL movement.
        }
    }

    /// <summary>
    /// ONNX inference: policy moves the agent; RAG DAG still advances when the agent reaches the step target.
    /// </summary>
    void EvaluatePhysicalStepArrivalOnly()
    {
        if (!TryGetActivePhysicalStepTarget(out ActionSequenceStep currentStep, out string targetId, out Vector3 rawTarget))
            return;

        EvaluatePhysicalStepArrival(currentStep, targetId, rawTarget);
    }

    bool TryGetActivePhysicalStepTarget(out ActionSequenceStep currentStep, out string targetId, out Vector3 rawTarget)
    {
        currentStep = null;
        targetId = null;
        rawTarget = Vector3.zero;

        if (mySequence == null) return false;
        if (!_ragOrchestratorPhysicalMode && actionSequenceData == null) return false;

        currentStep = mySequence.GetCurrentStep();
        if (currentStep == null)
        {
            CheckAndRewardSequenceCompletion();
            return false;
        }

        if (currentStep.isStepCompleted)
        {
            if (_ragOrchestratorPhysicalMode && !string.IsNullOrEmpty(_orchestratorActiveStepId))
            {
                NotifyCognitivePhaseOrchestratorStepCompleted(currentStep.stepId);
                FinishOrchestratorPhysicalStep();
            }
            else
                CheckAndRewardSequenceCompletion();
            return false;
        }

        targetId = ResolvePhysicalStepTargetId(currentStep);
        if (RagMenuController.IsMenuStep(currentStep))
        {
            if (IsMenuOpenedForStep(currentStep))
            {
                RagMenuController.EnsureInScene()?.ShowMenuForStep(currentStep, zoneIndex);
                RagMenuController.EnsureInScene()?.ReassertVisibleMenus();
            }
            else
                RagMenuController.EnsureInScene()?.HideAllMenus();
        }
        Vector3? targetPosMaybe = RagMenuController.IsMenuStep(currentStep)
            ? null
            : sequenceManager?.GetCurrentTargetPosition(agentId, zoneIndex);
        if (!targetPosMaybe.HasValue)
            targetPosMaybe = ResolvePhysicalTargetPosition(targetId);

        if (!targetPosMaybe.HasValue)
        {
            Debug.LogWarning($"[{agentId}] Physical step target not found: {targetId} ({currentStep.stepId}) — skipping");
            mySequence.MarkStepCompleted();
            NotifyCognitivePhaseOrchestratorStepCompleted(currentStep.stepId);
            if (_ragOrchestratorPhysicalMode)
                FinishOrchestratorPhysicalStep();
            else
                mySequence.MoveToNextStep();
            activePhysicalStepId = null;
            timeAtTarget = 0f;
            return false;
        }

        rawTarget = targetPosMaybe.Value;
        if (!RagMenuController.IsMenuStep(currentStep))
            rawTarget.y = 1f;

        if (inferenceOnnxControlsLocomotion && agentRole == AgentRole.Physical)
            SetMlNavigationTarget(rawTarget, targetId);

        if (activePhysicalStepId != currentStep.stepId)
        {
            activePhysicalStepId = currentStep.stepId;
            timeAtTarget = 0f;
            physicalStuckTimer = 0f;
            physicalDeflectRemaining = 0f;
            physicalStuckLastPos = transform.position;
            string mode = IsInPlacePhysicalStep(currentStep) ? "in-place act" : "navigate";
            Debug.Log($"▶ [{agentId}] Physical step START ({mode}): {currentStep.stepId} ({currentStep.actionType}) → {targetId} pos={rawTarget}");
            if (PersonaCognitiveControlSystem.Instance != null)
                PersonaCognitiveControlSystem.Instance.NotifyFirstActionStarted(agentId);
        }

        if (IsInPlacePhysicalStep(currentStep))
            rawTarget = transform.position;

        return true;
    }

    static bool IsInPlacePhysicalStep(ActionSequenceStep step)
    {
        if (step == null) return false;
        if (RagMenuController.IsMenuStep(step))
            return false;
        if (string.Equals(step.actionType, "act", StringComparison.OrdinalIgnoreCase))
            return true;
        string tid = step.targetObjectId;
        return !string.IsNullOrEmpty(tid)
               && tid.StartsWith("scene_", StringComparison.OrdinalIgnoreCase);
    }

    string ResolvePhysicalStepTargetId(ActionSequenceStep step)
    {
        if (step == null) return "";

        if (RagMenuController.IsMenuStep(step))
        {
            if (!IsMenuOpenedForStep(step))
                return PhysicalStepTargetResolver.ResolveObjectId(step, zoneIndex);

            string selected = RagMenuController.GetSelectedOptionTargetId(step);
            if (!string.IsNullOrWhiteSpace(selected))
                return selected;
        }

        return PhysicalStepTargetResolver.ResolveObjectId(step, zoneIndex);
    }

    void ResetMenuSubActionState(ActionSequenceStep step)
    {
        if (!RagMenuController.IsMenuStep(step))
            return;
        activeMenuStepId = step.stepId;
        activeMenuOpened = false;
        activeMenuPressLatched = false;
        RagMenuController.EnsureInScene()?.HideAllMenus();
        ApplyHandPoseForStep(step, false);
        ResetHandReachPose();
    }

    bool IsMenuOpenedForStep(ActionSequenceStep step)
    {
        return step != null
               && !string.IsNullOrEmpty(activeMenuStepId)
               && string.Equals(activeMenuStepId, step.stepId, StringComparison.Ordinal)
               && activeMenuOpened;
    }

    float GetMenuInferenceAssistSeconds()
    {
        return RagInferenceSceneController.IsInferenceSceneActive()
            ? MenuInferenceAssistSecondsFast
            : MenuInferenceAssistSeconds;
    }

    bool TryAcceptInferenceMenuPress(ActionSequenceStep step, Vector3 targetPosition)
    {
        if (!RagMenuController.IsMenuStep(step))
            return true;

        bool pressRequested = IsInteractionExecuteRequested;
        bool fingerClose = IsIndexFingerCloseTo(targetPosition, out float fingerDistance);
        float assistSeconds = GetMenuInferenceAssistSeconds();
        activeMenuInferenceWait += Time.fixedDeltaTime;

        if (activeMenuInferenceWait >= assistSeconds)
        {
            activeMenuPressLatched = true;
            activeMenuInferenceWait = 0f;
            ApplyHandReachPose(targetPosition, true);
            ApplyHandPoseForStep(step, true);
            if (!RagInferenceSceneController.IsInferenceSceneActive())
                AddReward(-0.03f);
            Debug.Log($"🖐 [{agentId}] Assisted menu press for {step.stepId} (inference proximity assist {assistSeconds:F2}s).");
            return true;
        }

        if (pressRequested && fingerClose)
        {
            activeMenuPressLatched = true;
            activeMenuInferenceWait = 0f;
            AddReward(IsMenuOpenedForStep(step) ? 0.12f : 0.06f);
            Debug.Log($"🖐 [{agentId}] ONNX menu press accepted for {step.stepId}; fingerDistance={fingerDistance:F2}m");
            return true;
        }

        if (pressRequested && !fingerClose)
            AddReward(-0.02f);
        RequestDecisionOrQueue("Waiting for ONNX menu finger press");
        return false;
    }

    void SyncInferenceWalkPresentation()
    {
        if (!inferenceOnnxControlsLocomotion || agentRole != AgentRole.Physical)
            return;

        if (_humanWalkAnim == null)
            _humanWalkAnim = GetComponent<HumanWalkAnimation>() ?? GetComponentInChildren<HumanWalkAnimation>(true);
        if (_humanWalkAnim == null)
            return;

        if (!_inferenceWalkPosInitialized)
        {
            _lastInferenceWalkPos = transform.position;
            _inferenceWalkPosInitialized = true;
            return;
        }

        float moved = Vector3.Distance(transform.position, _lastInferenceWalkPos);
        _lastInferenceWalkPos = transform.position;

        bool wantsMove = currentMoveAction != 0 || moved > 0.004f;
        if (wantsMove)
            _humanWalkAnim.StartWalking();
        else
            _humanWalkAnim.StopWalking();
    }

    bool IsIndexFingerCloseTo(Vector3 targetPosition, out float distance)
    {
        distance = float.MaxValue;
        if (handRotationManager == null)
            handRotationManager = HandRotationManager.EnsureOnAgent(gameObject);
        if (handRotationManager == null || handRotationManager.IndexFingerTip == null)
            return false;

        distance = Vector3.Distance(handRotationManager.IndexFingerTip.position, targetPosition);
        return distance <= MenuInferenceFingerSelectDistance;
    }

    void ApplyHandReachPose(Vector3 targetPosition, bool press)
    {
        if (agentRole != AgentRole.Physical) return;
        KleinFrameExecutor klein = GetComponent<KleinFrameExecutor>();
        if (klein != null && klein.IsHoldingPose) return;
        if (handRotationManager == null)
            handRotationManager = HandRotationManager.EnsureOnAgent(gameObject);
        handRotationManager?.ApplyRightArmReachPose(targetPosition, press);
    }

    void ResetHandReachPose()
    {
        if (agentRole != AgentRole.Physical) return;
        KleinFrameExecutor klein = GetComponent<KleinFrameExecutor>();
        if (klein != null && klein.IsHoldingPose) return;
        if (handRotationManager == null)
            handRotationManager = HandRotationManager.EnsureOnAgent(gameObject);
        handRotationManager?.ResetRightArmReachPose();
    }

    void ApplyHandPoseForStep(ActionSequenceStep step, bool execute)
    {
        if (agentRole != AgentRole.Physical) return;
        KleinFrameExecutor klein = GetComponent<KleinFrameExecutor>();
        if (klein != null && klein.IsHoldingPose) return;
        if (handRotationManager == null)
            handRotationManager = HandRotationManager.EnsureOnAgent(gameObject);
        if (handRotationManager == null) return;
        if (handRotationManager.UsesMixamoRig) return;
        handRotationManager.ApplyFingerRotation(
            HandActionLibrary.IndexFinger,
            HandActionLibrary.GetRotationsForStep(step, execute));
    }

    void EvaluatePhysicalStepArrival(ActionSequenceStep currentStep, string targetId, Vector3 rawTarget)
    {
        CognitiveProcessManager.EnsureExists();
        float requiredDwell = CognitiveProcessManager.ComputeStationDwellSeconds(currentStep);
        if (IsInPlacePhysicalStep(currentStep))
            requiredDwell = Mathf.Clamp(requiredDwell, 0.35f, 8f);

        if (IsInPlacePhysicalStep(currentStep))
        {
            if (rb != null)
                rb.linearVelocity = Vector3.zero;
            timeAtTarget += Time.fixedDeltaTime;
            if (timeAtTarget >= requiredDwell)
                CompletePhysicalStep(currentStep, targetId, transform.position);
            return;
        }

        float arrivalDistance = GetPhysicalArrivalDistance(currentStep);
        if (inferenceOnnxControlsLocomotion && RagMenuController.IsMenuStep(currentStep))
            arrivalDistance = IsMenuOpenedForStep(currentStep) ? 1.25f : 2.15f;
        Vector3 bodyTarget = rawTarget;
        if (RagMenuController.IsMenuStep(currentStep))
            bodyTarget.y = transform.position.y;
        float distToCorrectTarget = Vector3.Distance(transform.position, bodyTarget);
        float leaveDistance = arrivalDistance + (RagInferenceSceneController.IsInferenceSceneActive()
            ? MenuInferenceArrivalHysteresis
            : 0.15f);

        if (distToCorrectTarget > leaveDistance)
        {
            timeAtTarget = 0f;
            activeMenuPressLatched = false;
            activeMenuInferenceWait = 0f;
            if (RagMenuController.IsMenuStep(currentStep))
                ResetHandReachPose();
            if (RagMenuController.IsMenuStep(currentStep) && currentToolAction == 2)
                AddReward(-0.02f);
            return;
        }

        if (rb != null)
            rb.linearVelocity = Vector3.zero;
        timeAtTarget += Time.fixedDeltaTime;

        if (RagMenuController.IsMenuStep(currentStep))
        {
            bool pressRequested = IsInteractionExecuteRequested;
            ApplyHandReachPose(rawTarget, pressRequested);
            ApplyHandPoseForStep(currentStep, pressRequested);

            if (!activeMenuPressLatched)
            {
                if (!TryAcceptInferenceMenuPress(currentStep, rawTarget))
                    return;
            }

            float menuDwell = IsMenuOpenedForStep(currentStep)
                ? MenuInferencePressDwellSeconds
                : MenuInferenceOpenDwellSeconds;

            if (timeAtTarget < menuDwell)
                return;

            if (!IsMenuOpenedForStep(currentStep))
            {
                activeMenuStepId = currentStep.stepId;
                activeMenuOpened = true;
                activeMenuPressLatched = false;
                activeMenuInferenceWait = 0f;
                timeAtTarget = 0f;
                activePhysicalStepId = null;
                // Show pressed feedback on the button object before revealing the menu
                RagMenuController.EnsureInScene()?.ShowButtonPressedFeedback(currentStep.targetObjectId, zoneIndex);
                RagMenuController.EnsureInScene()?.ShowMenuForStep(currentStep, zoneIndex);
                RagMenuController.EnsureInScene()?.ReassertVisibleMenus();
                ApplyHandPoseForStep(currentStep, false);
                ResetHandReachPose();
                RequestDecisionOrQueue("Menu opened, selecting option");
                Debug.Log($"🧾 [{agentId}] Menu opened for {currentStep.stepId}; next target is {RagMenuController.GetSelectedOptionTargetId(currentStep)}");
                return;
            }
        }

        if (currentStep.actionType == "learn" && skillSystem != null && skillSystem.IsDataReady())
            HandleLearnActionStep(currentStep, requiredDwell, timeAlreadyAdvancedByCaller: true);
        else if (timeAtTarget >= requiredDwell)
            CompletePhysicalStep(currentStep, targetId, rawTarget);
    }

    static float GetPhysicalArrivalDistance(ActionSequenceStep step)
    {
        if (RagMenuController.IsMenuStep(step))
            return 0.85f; // agent must walk up close to physically press button/option
        return 2.8f;
    }

    private void ExecutePhysicalScriptedStep()
    {
        if (!TryGetActivePhysicalStepTarget(out ActionSequenceStep currentStep, out string targetId, out Vector3 rawTarget))
            return;

        float arrivalDistance = GetPhysicalArrivalDistance(currentStep);
        float distToCorrectTarget = Vector3.Distance(transform.position, rawTarget);

        // ── DEFLECT PHASE: push away from wrong obstacle ─────────────────
        if (physicalDeflectRemaining > 0f)
        {
            physicalDeflectRemaining -= Time.fixedDeltaTime;
            float moveSpeed = Mathf.Clamp(physicalMovementSpeed * 1.5f, 3f, 7f);
            Vector3 deflectPos = transform.position + physicalDeflectDir * moveSpeed * Time.fixedDeltaTime;
            deflectPos.y = 1f;
            transform.position = deflectPos;
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            physicalStuckTimer   = 0f;
            physicalStuckLastPos = transform.position;
            return;
        }

        // ── WRONG-TOOL PROXIMITY CHECK: deflect if agent touches wrong tool ──
        // Only trigger when the agent is NOT already close to the correct target
        if (distToCorrectTarget > arrivalDistance)
        {
            string nearbyTool = GetNearestZoneTool(arrivalDistance + 0.5f);
            if (!string.IsNullOrEmpty(nearbyTool))
            {
                // Strip zone suffix to compare base IDs  e.g. "tool_002_zone0" → "tool_002"
                string nearbyBase = StripZoneSuffix(nearbyTool);
                string correctBase = StripZoneSuffix(targetId);
                if (!string.Equals(nearbyBase, correctBase, System.StringComparison.OrdinalIgnoreCase))
                {
                    // Touching wrong tool — compute deflect direction AWAY from it + random angle
                    GameObject wrongGO = GameObject.Find(nearbyTool);
                    Vector3 awayDir = wrongGO != null
                        ? (transform.position - wrongGO.transform.position)
                        : -transform.forward;
                    awayDir.y = 0f;
                    if (awayDir.sqrMagnitude < 0.01f) awayDir = transform.right;
                    awayDir.Normalize();

                    // Blend: half away from wrong tool, half toward correct target
                    Vector3 toCorrect = (rawTarget - transform.position); toCorrect.y = 0f; toCorrect.Normalize();
                    Vector3 deflect = (awayDir + toCorrect).normalized;

                    // Add a random perpendicular component to avoid re-hitting the same tool
                    float randomAngle = UnityEngine.Random.Range(-60f, 60f);
                    deflect = Quaternion.AngleAxis(randomAngle, Vector3.up) * deflect;

                    physicalDeflectDir       = deflect;
                    physicalDeflectRemaining = PHYSICAL_DEFLECT_SECS;
                    physicalStuckTimer       = 0f;
                    physicalStuckLastPos     = transform.position;
                    Debug.Log($"[{agentId}] Wrong tool {nearbyBase} (wanted {correctBase}) → deflecting {randomAngle:F0}° for {PHYSICAL_DEFLECT_SECS}s");
                    return;
                }
            }
        }

        // ── STUCK DETECTION: no real progress toward correct target ──────
        float progressTowardTarget = physicalStuckLastPos != Vector3.zero
            ? Vector3.Distance(physicalStuckLastPos, rawTarget) - distToCorrectTarget
            : 0f;
        bool makingProgress = progressTowardTarget > 0.02f;

        if (!makingProgress && distToCorrectTarget > arrivalDistance)
        {
            physicalStuckTimer += Time.fixedDeltaTime;
            if (physicalStuckTimer >= PHYSICAL_STUCK_TIMEOUT)
            {
                // Pick a random deflect direction pointing roughly at the target
                Vector3 toTarget = (rawTarget - transform.position); toTarget.y = 0f; toTarget.Normalize();
                float angle = UnityEngine.Random.Range(-90f, 90f);
                physicalDeflectDir       = Quaternion.AngleAxis(angle, Vector3.up) * toTarget;
                physicalDeflectRemaining = PHYSICAL_DEFLECT_SECS;
                physicalStuckTimer       = 0f;
                physicalStuckLastPos     = transform.position;
                Debug.LogWarning($"[{agentId}] Stuck moving to {targetId} — deflecting {angle:F0}° to find path");
                return;
            }
        }
        else
        {
            physicalStuckTimer   = 0f;
            physicalStuckLastPos = transform.position;
        }

        // ── MOVE toward correct target ────────────────────────────────────
        if (distToCorrectTarget > arrivalDistance)
        {
            float effectiveDuration = Mathf.Max(1.5f, currentStep.expectedDuration);
            float desiredSpeed = (distToCorrectTarget / effectiveDuration) * physicalMovementSpeedMultiplier;
            float moveSpeed = Mathf.Clamp(desiredSpeed, physicalMovementSpeed, 6f);

            Vector3 nextPos = Vector3.MoveTowards(transform.position, rawTarget, moveSpeed * Time.fixedDeltaTime);
            nextPos.y = 1f;
            transform.position = nextPos;
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }

            Vector3 dir = rawTarget - transform.position; dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 8f * Time.fixedDeltaTime);
            timeAtTarget = 0f;
        }
        else
            EvaluatePhysicalStepArrival(currentStep, targetId, rawTarget);
    }

    /// <summary>Returns the name of the nearest zone tool within <paramref name="radius"/> units, or null.</summary>
    private string GetNearestZoneTool(float radius)
    {
        string nearest = null;
        float bestDist = radius;
        Collider[] hits = Physics.OverlapSphere(transform.position, radius);
        foreach (var col in hits)
        {
            if (col == null || col.gameObject == gameObject) continue;
            string n = col.gameObject.name;
            // Match any zone tool or workbench (e.g. "tool_001_zone0", "workbench_001_zone2")
            if ((n.Contains("tool_") || n.Contains("workbench_")) && n.Contains("_zone"))
            {
                float d = Vector3.Distance(transform.position, col.transform.position);
                if (d < bestDist) { bestDist = d; nearest = n; }
            }
        }
        return nearest;
    }

    /// <summary>Strips "_zoneN" suffix, e.g. "tool_001_zone0" → "tool_001".</summary>
    private static string StripZoneSuffix(string id)
    {
        if (string.IsNullOrEmpty(id)) return id;
        int idx = id.IndexOf("_zone", System.StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? id.Substring(0, idx) : id;
    }

    /// <summary>
    /// Looks up the world-space position of a physical action target directly in the scene,
    /// bypassing the AgentSequenceManager cache which may have cognitive-station positions in it.
    /// </summary>
    private Vector3? ResolvePhysicalTargetPosition(string targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId)) return null;

        if (RagMenuController.IsMenuOptionId(targetId))
        {
            string runtimeName = RagMenuController.GetRuntimeOptionObjectName(targetId, zoneIndex);
            GameObject option = GameObject.Find(runtimeName);
            if (option != null)
                return option.transform.position;
        }

        if (SceneGenerator.Instance != null)
        {
            Vector3 scenePos = SceneGenerator.Instance.GetTargetPositionById(targetId, zoneIndex);
            if (scenePos != Vector3.zero)
                return scenePos;
        }

        // 1. Zone-suffixed exact match (e.g. tool_001_zone0)
        if (zoneIndex >= 0)
        {
            string zoneKey = $"{targetId}_zone{zoneIndex}";
            GameObject zgo = GameObject.Find(zoneKey);
            if (zgo != null) return zgo.transform.position;
        }

        // 2. Exact name match
        GameObject exactGO = GameObject.Find(targetId);
        if (exactGO != null) return exactGO.transform.position;

        // 3. Sequence manager cache (no full-scene GameObject scan — was O(n) all objects per call).
        return sequenceManager != null ? sequenceManager.GetTargetPositionById(targetId, zoneIndex) : null;
    }

    /// <summary>
    /// When <see cref="RagSequenceAgentMover"/> is disabled (e.g. RAG ML bootstrap), physical completions
    /// must still notify <see cref="CognitivePhaseOrchestrator"/> or HUD step counts stay at 0 for P.
    /// </summary>
    void NotifyCognitivePhaseOrchestratorStepCompleted(string stepId)
    {
        if (string.IsNullOrEmpty(stepId) || zoneIndex < 0) return;
        CognitivePhaseOrchestrator orch = CognitivePhaseOrchestrator.GetOrCreateForZone(zoneIndex);
        if (orch == null) return;
        if (!orch.IsInitialized)
            orch.EnsureInitializedFromSceneAgents();
        orch.NotifyStepCompleted(stepId);
    }

    /// <summary>
    /// Inference Option 1: mental <see cref="RagSequenceAgentMover"/> owns DAG steps — stop parallel cognitive FixedUpdate.
    /// </summary>
    public void YieldCognitiveSequenceToRagMover()
    {
        if (agentRole != AgentRole.Mental) return;
        cognitivePhaseActive = false;
        activeCognitiveStepId = null;
        activeCognitiveMemoryStepId = null;
        activeCognitiveRoute.Clear();
        activeCognitiveRouteIndex = 0;
        timeAtTarget = 0f;
        if (actionSequenceData != null)
            mySequence = actionSequenceData;
    }

    void RecordPhysicalStepProducedPayload(ActionSequenceStep step)
    {
        if (step == null || string.IsNullOrWhiteSpace(step.producesPayload) || zoneIndex < 0) return;

        ZoneDeclarativeMemory mem = ZoneDeclarativeMemory.ForZone(zoneIndex);
        if (mem == null)
        {
            ZoneDeclarativeMemory.RebuildRegistryFromScene();
            mem = ZoneDeclarativeMemory.ForZone(zoneIndex);
        }
        if (mem == null) return;

        if (ProductionMemoryRuleEngine.IsProductionMemoryStep(step)
            && mem.TryGetPayload(step.producesPayload, out _))
            return;

        string value = !string.IsNullOrWhiteSpace(step.description)
            ? step.description
            : (!string.IsNullOrWhiteSpace(step.currentCognitiveState)
                ? $"{step.currentCognitiveState} complete"
                : $"{step.stepId} complete");
        mem.RecordProducedPayload(step.stepId, step.producesPayload, value);
    }

    /// <summary>
    /// RAG scenes use <see cref="RagSequenceAgentMover"/>'s HUD reward model (+1 skill gauge per step),
    /// not ML pathfinding micro-rewards or variable JSON correct_step_reward on the skill profile.
    /// </summary>
    bool UsesRagHudRewardSystem()
    {
        return ragSpawnPreserveActive || _ragOrchestratorPhysicalMode;
    }

    void ApplyRagHudStepReward(ActionSequenceStep step)
    {
        if (step == null) return;
        if (skillSystem == null)
            skillSystem = FindObjectOfType<SkillBasedActionSystem>();
        if (skillSystem == null)
        {
            Debug.LogWarning($"[BSGMLAgent] {agentId}: SkillBasedActionSystem missing — RAG step reward lost for {step.stepId}");
            return;
        }

        RagStepRewardBridge.ApplyStandardStepReward(agentId, agentId, step, skillSystem);
    }

    void ApplyRagCognitiveStepSuccess(ActionSequenceStep step)
    {
        if (!UsesRagHudRewardSystem() || step == null) return;
        if (skillSystem == null)
            skillSystem = FindObjectOfType<SkillBasedActionSystem>();
        if (skillSystem == null)
        {
            Debug.LogWarning($"[BSGMLAgent] {agentId}: SkillBasedActionSystem missing — cognitive HUD reward lost for {step.stepId}");
            return;
        }

        string physicalRewardId = ZoneAgentIds.TryResolvePhysicalAgentId(zoneIndex);
        if (string.IsNullOrEmpty(physicalRewardId))
            physicalRewardId = skillSystem.GetPhysicalAgentIdForZone(zoneIndex);
        if (string.IsNullOrEmpty(physicalRewardId))
        {
            Debug.LogWarning($"[BSGMLAgent] {agentId}: No P-agent for zone {zoneIndex} — cognitive HUD reward lost for {step.stepId}");
            return;
        }

        RagStepRewardBridge.ApplyStandardStepReward(physicalRewardId, agentId, step, skillSystem);
        RagStepRewardBridge.ApplyCognitiveZoneMlReward(zoneIndex, step.stepId);
    }

    void PlayRagStepCompletionFeedback()
    {
        if (_ragStepFlashRoutine != null)
            StopCoroutine(_ragStepFlashRoutine);
        _ragStepFlashRoutine = StartCoroutine(CoRagStepCompletionFeedback());
    }

    IEnumerator CoRagStepCompletionFeedback()
    {
        CognitiveProcessManager.EnsureExists();
        float sec = CognitiveProcessManager.Instance != null
            ? CognitiveProcessManager.Instance.rewardWhiteFlashSeconds
            : 2f;

        Renderer[] rends = GetComponentsInChildren<Renderer>(true);
        if (rends == null || rends.Length == 0)
        {
            _ragStepFlashRoutine = null;
            yield break;
        }

        Color[] saved = new Color[rends.Length];
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] != null && rends[i].material != null && !IsBareCapsuleRenderer(rends[i], transform))
                saved[i] = rends[i].material.color;
        }

        foreach (Renderer r in rends)
        {
            if (r == null || r.material == null || IsBareCapsuleRenderer(r, transform)) continue;
            r.material.color = Color.white;
        }

        yield return new WaitForSeconds(sec);

        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] != null && rends[i].material != null && !IsBareCapsuleRenderer(rends[i], transform))
                rends[i].material.color = saved[i];
        }

        _ragStepFlashRoutine = null;
    }

    /// <summary>
    /// Unified step-complete reward: RAG HUD (+1 skill gauge) vs legacy ML/persona pathfinding rewards.
    /// </summary>
    void ApplyStepCompletionReward(ActionSequenceStep step)
    {
        if (step == null) return;

        if (UsesRagHudRewardSystem())
        {
            ApplyRagHudStepReward(step);
            PlayRagStepCompletionFeedback();
            return;
        }

        float stepReward = step.correct_step_reward > 0f
            ? step.correct_step_reward
            : (sequenceManager != null ? sequenceManager.GetCurrentStepReward(agentId) : 0f);
        if (stepReward > 0f)
        {
            AddReward(stepReward);
            episodeTotalReward += stepReward;
            UpdateAgentSkillLevelFromReward(stepReward);
        }

        TriggerCognitiveCompletionFlash(step.targetObjectId ?? string.Empty, transform.position);
    }

    private void CompletePhysicalStep(ActionSequenceStep currentStep, string targetId, Vector3 targetPos)
    {
        if (RagMenuController.IsMenuStep(currentStep))
        {
            // Store selected option in declarative memory for downstream cognitive steps
            string selectedId = RagMenuController.GetSelectedOptionTargetId(currentStep);
            if (!string.IsNullOrWhiteSpace(selectedId) && zoneMemory != null)
            {
                zoneMemory.SetData("selected_menu_option", selectedId);
                zoneMemory.SetData("menu_step_id", currentStep.stepId);
                Debug.Log($"📋 [{agentId}] Stored menu selection: {selectedId} → ZoneDeclarativeMemory zone {zoneIndex}");
            }
            RagMenuController.EnsureInScene()?.MarkOptionSelected(selectedId, zoneIndex);
            RagMenuController.EnsureInScene()?.HideAllMenus();
            ApplyHandPoseForStep(currentStep, false);
            ResetHandReachPose();
            activeMenuStepId = null;
            activeMenuOpened = false;
            activeMenuPressLatched = false;
            activeMenuInferenceWait = 0f;
        }

        RecordPhysicalStepProducedPayload(currentStep);
        mySequence.MarkStepCompleted();
        NotifyCognitivePhaseOrchestratorStepCompleted(currentStep.stepId);

        ApplyStepCompletionReward(currentStep);

        if (MLTrainingResultsWriter.Instance != null && !UsesRagHudRewardSystem())
            MLTrainingResultsWriter.Instance.OnStepCompleted(agentId);

        // Notify HUD / PersonaCognitiveControlSystem so the step indicator updates
        if (PersonaCognitiveControlSystem.Instance != null)
        {
            PersonaCognitiveControlSystem.Instance.NotifyCognitiveStepCompleted(
                agentId, currentStep.stepId, targetId);
            PersonaCognitiveControlSystem.Instance.NotifyStepTransition(agentId, currentStep.stepId);
        }

        Debug.Log($"✅ [{agentId}] Physical step DONE: {currentStep.stepId} → {targetId}");

        if (inferenceOnnxControlsLocomotion)
        {
            ClearMlNavigationTarget();
            if (_humanWalkAnim == null)
                _humanWalkAnim = GetComponent<HumanWalkAnimation>() ?? GetComponentInChildren<HumanWalkAnimation>(true);
            _humanWalkAnim?.StopWalking();
        }

        if (_ragOrchestratorPhysicalMode)
        {
            activePhysicalStepId = null;
            timeAtTarget = 0f;
            FinishOrchestratorPhysicalStep();
            return;
        }

        bool wasLast = !mySequence.HasNextStep();
        mySequence.MoveToNextStep();
        activePhysicalStepId = null;
        timeAtTarget = 0f;
        if (wasLast) CheckAndRewardSequenceCompletion();
    }
    // ── End physical scripted movement ────────────────────────────────────────

    private void ResolveDynamicCognitiveTarget(ActionSequenceStep cognitiveStep, ref string targetId, ref string targetName)
    {
        if (cognitiveStep == null) return;
        string state = (cognitiveStep.currentCognitiveState ?? string.Empty).ToLowerInvariant();

        // ── Dynamic target from cognitive process (Phase 4: Physical Execution) ──────────
        // If this is the PhysicalExecute step and ZoneDeclarativeMemory has a resolved target,
        // use that instead of the static JSON actionSequence value.
        bool isPhysicalExecute = state.Contains("physicalexecute") || state.Contains("physical_execute")
                              || (cognitiveStep.actionType ?? "").ToLowerInvariant().Contains("physical");
        if (isPhysicalExecute && zoneMemory != null && !string.IsNullOrWhiteSpace(zoneMemory.resolvedTargetId))
        {
            string cogTarget = zoneMemory.resolvedTargetId;
            // Append zone suffix if not already present
            if (!cogTarget.Contains("_zone"))
                cogTarget = $"{cogTarget}_zone{zoneIndex}";
            targetId = cogTarget;
            Debug.Log($"🎯 [{agentId}] PhysicalExecute → COGNITIVE target: {targetId}  (fallback suppressed)");
            return;
        }
        // Also check AgentCognitiveMemory (written by MentalAgentController merge step)
        if (isPhysicalExecute && cognitiveMemory != null
            && cognitiveMemory.TryGet("resolvedTargetId", out string memTarget)
            && !string.IsNullOrWhiteSpace(memTarget))
        {
            if (!memTarget.Contains("_zone")) memTarget = $"{memTarget}_zone{zoneIndex}";
            targetId = memTarget;
            Debug.Log($"🎯 [{agentId}] PhysicalExecute → COGNITIVE target (from AgentCognitiveMemory): {targetId}");
            return;
        }
        // ── End dynamic resolution ──────────────────────────────────────────────────────

        // For environment scan, always inspect the specific agent's upcoming real-action target.
        if (state.Contains("environment"))
        {
            // Hard guard by canonical agent identity (prevents parser/key mismatch from routing everyone to tool_001).
            if (TryGetCanonicalEnvironmentTarget(agentId, out string canonicalTargetId, out string canonicalTargetName))
            {
                targetId = canonicalTargetId;
                targetName = canonicalTargetName;
                return;
            }

            ActionSequenceStep nextAction = actionSequenceData != null ? actionSequenceData.GetCurrentStep() : null;
            if (nextAction == null)
            {
                actionSequenceData = GetActionSequenceWithFallback();
                nextAction = actionSequenceData != null ? actionSequenceData.GetCurrentStep() : null;
            }
            if (nextAction != null && !string.IsNullOrWhiteSpace(nextAction.targetObjectId))
            {
                targetId = nextAction.targetObjectId;
                if (!string.IsNullOrWhiteSpace(nextAction.targetObjectName))
                {
                    targetName = nextAction.targetObjectName;
                }
                return;
            }

            // Last-resort deterministic mapping by canonical agent identity.
            string canonical = ResolveCanonicalAgentId(agentId);
            if (string.Equals(canonical, "SIMPLE_Technician_01", StringComparison.OrdinalIgnoreCase)) { targetId = "tool_001"; targetName = "Industrial Motor Unit"; return; }
            if (string.Equals(canonical, "SIMPLE_Technician_02", StringComparison.OrdinalIgnoreCase)) { targetId = "tool_002"; targetName = "Secure Toolbox Alpha"; return; }
            if (string.Equals(canonical, "SIMPLE_Supervisor_01", StringComparison.OrdinalIgnoreCase)) { targetId = "tool_003"; targetName = "Hydraulic Lift Station"; return; }
            if (string.Equals(canonical, "SIMPLE_Supervisor_02", StringComparison.OrdinalIgnoreCase)) { targetId = "tool_004"; targetName = "Safety Inspection Station"; return; }
        }

        // For visual/retrieval/production reasoning passes, use current real-action target if JSON has a generic tool fallback.
        bool genericToolFallback = string.Equals(targetId, "tool_001", StringComparison.OrdinalIgnoreCase);
        if (genericToolFallback &&
            (state.Contains("visual") || state.Contains("retrieval") || state.Contains("production")))
        {
            ActionSequenceStep nextAction = actionSequenceData != null ? actionSequenceData.GetCurrentStep() : null;
            if (nextAction == null)
            {
                actionSequenceData = GetActionSequenceWithFallback();
                nextAction = actionSequenceData != null ? actionSequenceData.GetCurrentStep() : null;
            }
            if (nextAction != null && !string.IsNullOrWhiteSpace(nextAction.targetObjectId))
            {
                targetId = nextAction.targetObjectId;
                if (!string.IsNullOrWhiteSpace(nextAction.targetObjectName))
                {
                    targetName = nextAction.targetObjectName;
                }
            }
        }
    }

    private static bool TryGetCanonicalEnvironmentTarget(string rawAgentId, out string targetId, out string targetName)
    {
        targetId = null;
        targetName = null;
        string canonical = ResolveCanonicalAgentId(rawAgentId);

        if (string.Equals(canonical, "SIMPLE_Technician_01", StringComparison.OrdinalIgnoreCase))
        {
            targetId = "tool_001";
            targetName = "Industrial Motor Unit";
            return true;
        }
        if (string.Equals(canonical, "SIMPLE_Technician_02", StringComparison.OrdinalIgnoreCase))
        {
            targetId = "tool_002";
            targetName = "Secure Toolbox Alpha";
            return true;
        }
        if (string.Equals(canonical, "SIMPLE_Supervisor_01", StringComparison.OrdinalIgnoreCase))
        {
            targetId = "tool_003";
            targetName = "Hydraulic Lift Station";
            return true;
        }
        if (string.Equals(canonical, "SIMPLE_Supervisor_02", StringComparison.OrdinalIgnoreCase))
        {
            targetId = "tool_004";
            targetName = "Safety Inspection Station";
            return true;
        }

        return false;
    }

    private bool TryBuildCognitiveRoute(ActionSequenceStep currentStep, out List<Vector3> waypoints)
    {
        waypoints = new List<Vector3>();
        if (sequenceManager == null || currentStep == null) return false;
        if (!string.Equals(currentStep.actionType, "move", StringComparison.OrdinalIgnoreCase)) return false;

        string[] routeIds =
        {
            "cognitive_008", // Goal
            "cognitive_009", // Imaginal
            "cognitive_010", // Retrieval
            "cognitive_003", // Declarative
            "cognitive_002", // Production
            "cognitive_005"  // Motor
        };

        foreach (string id in routeIds)
        {
            Vector3? p = sequenceManager.GetTargetPositionById(id, zoneIndex);
            if (p.HasValue)
            {
                Vector3 wp = p.Value;
                wp.y = 1f;
                waypoints.Add(wp);
            }
        }

        // Route is only useful if it has more than one node.
        if (waypoints.Count <= 1) return false;
        return true;
    }

    private void UpdateCognitiveThreadLine(Vector3 targetPos)
    {
        if (RagInferenceSceneController.IsInferenceSceneActive())
        {
            ClearCognitiveThreadLine();
            return;
        }

        if (cognitiveThreadLine == null)
        {
            GameObject lineObj = new GameObject($"{agentId}_CognitiveThread");
            lineObj.transform.SetParent(transform, false);
            lineObj.SetActive(true);
            cognitiveThreadLine = lineObj.AddComponent<LineRenderer>();
            cognitiveThreadLine.positionCount = 2;
            cognitiveThreadLine.startWidth = 0.06f;
            cognitiveThreadLine.endWidth = 0.04f;
            cognitiveThreadLine.useWorldSpace = true;
            cognitiveThreadLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            cognitiveThreadLine.receiveShadows = false;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            Material mat = shader != null ? new Material(shader) : null;
            if (mat != null)
            {
                mat.color = new Color(1f, 0.95f, 0.2f, 0.9f);
                cognitiveThreadLine.material = mat;
            }
            cognitiveThreadLine.startColor = new Color(1f, 0.95f, 0.2f, 0.95f);
            cognitiveThreadLine.endColor = new Color(1f, 0.95f, 0.2f, 0.65f);
        }

        Vector3 from = transform.position + Vector3.up * 0.7f;
        Vector3 to = targetPos + Vector3.up * 0.6f;
        if (!cognitiveThreadLine.gameObject.activeSelf)
            cognitiveThreadLine.gameObject.SetActive(true);
        cognitiveThreadLine.enabled = true;
        if (!LineRendererSafe.CanDraw(cognitiveThreadLine))
            return;
        LineRendererSafe.TrySetPosition(cognitiveThreadLine, 0, from);
        LineRendererSafe.TrySetPosition(cognitiveThreadLine, 1, to);
    }

    private void ClearCognitiveThreadLine()
    {
        if (cognitiveThreadLine != null)
        {
            cognitiveThreadLine.enabled = false;
            cognitiveThreadLine.gameObject.SetActive(false);
        }
    }

    private AgentSequenceData GetActionSequenceWithFallback()
    {
        if (sequenceManager == null) return null;
        string lookupId = ZoneAgentIds.NormalizeProfileAgentId(agentId, zoneIndex);
        AgentSequenceData seq = sequenceManager.GetSequence(lookupId);
        if (seq != null && seq.actionSequence != null && seq.actionSequence.Count > 0) return seq;

        // RAG P-agents (P1, P2, …) often have steps only under physicalAgents[], not agentProfiles.
        seq = sequenceManager.GetPhysicalSequence(lookupId);
        if (seq != null && seq.actionSequence != null && seq.actionSequence.Count > 0)
        {
            Debug.Log($"⚙️ [{agentId}] Using physicalAgents steps for {lookupId} ({seq.actionSequence.Count} steps)");
            return seq;
        }

        if (zoneIndex >= 0)
        {
            string zonePhysical = ZoneAgentIds.TryResolvePhysicalAgentId(zoneIndex);
            if (!string.IsNullOrEmpty(zonePhysical) && !string.Equals(zonePhysical, lookupId, StringComparison.OrdinalIgnoreCase))
            {
                seq = sequenceManager.GetPhysicalSequence(zonePhysical);
                if (seq != null && seq.actionSequence != null && seq.actionSequence.Count > 0)
                {
                    Debug.Log($"⚙️ [{agentId}] Using zone {zoneIndex} physicalAgents steps ({zonePhysical}, {seq.actionSequence.Count} steps)");
                    return seq;
                }
                seq = sequenceManager.GetSequence(zonePhysical);
                if (seq != null && seq.actionSequence != null && seq.actionSequence.Count > 0)
                    return seq;
            }
        }

        string canonical = ResolveCanonicalAgentId(lookupId);
        if (!string.IsNullOrEmpty(canonical) && !string.Equals(canonical, lookupId, StringComparison.OrdinalIgnoreCase))
        {
            seq = sequenceManager.GetSequence(canonical);
            if (seq != null && seq.actionSequence != null && seq.actionSequence.Count > 0)
            {
                Debug.Log($"🔁 [{agentId}] Using canonical action sequence: {canonical}");
                return seq;
            }

            seq = sequenceManager.GetPhysicalSequence(canonical);
            if (seq != null && seq.actionSequence != null && seq.actionSequence.Count > 0)
            {
                Debug.Log($"🔁 [{agentId}] Using canonical physicalAgents steps: {canonical}");
                return seq;
            }
        }

        return seq;
    }

    private AgentSequenceData GetCognitiveSequenceWithFallback()
    {
        if (sequenceManager == null) return null;
        string lookupId = ZoneAgentIds.NormalizeProfileAgentId(agentId, zoneIndex);
        AgentSequenceData seq = sequenceManager.GetCognitiveSequence(lookupId);
        if (seq != null && seq.actionSequence != null && seq.actionSequence.Count > 0) return seq;

        if (zoneIndex >= 0)
        {
            string zonePhysical = ZoneAgentIds.TryResolvePhysicalAgentId(zoneIndex);
            if (!string.IsNullOrEmpty(zonePhysical))
            {
                seq = sequenceManager.GetCognitiveSequence(zonePhysical);
                if (seq != null && seq.actionSequence != null && seq.actionSequence.Count > 0)
                {
                    Debug.Log($"🧠 [{agentId}] Using zone {zoneIndex} cognitive sequence ({zonePhysical})");
                    return seq;
                }
            }
        }

        string canonical = ResolveCanonicalAgentId(lookupId);
        if (!string.IsNullOrEmpty(canonical) && !string.Equals(canonical, lookupId, StringComparison.OrdinalIgnoreCase))
        {
            seq = sequenceManager.GetCognitiveSequence(canonical);
            if (seq != null && seq.actionSequence != null && seq.actionSequence.Count > 0)
            {
                Debug.Log($"🔁 [{agentId}] Using canonical cognitive sequence: {canonical}");
                return seq;
            }
        }

        return seq;
    }

    private static string ResolveCanonicalAgentId(string rawId)
    {
        if (string.IsNullOrWhiteSpace(rawId)) return rawId;
        string lower = rawId.ToLowerInvariant().Replace(" ", "");

        bool isTech = lower.Contains("technician");
        bool isSup = lower.Contains("supervisor");
        bool has01 = lower.Contains("_01") || lower.Contains("01");
        bool has02 = lower.Contains("_02") || lower.Contains("02");

        // Prefer explicit 02 checks before 01 to avoid "(1)" clone suffix ambiguity.
        if (isTech && has02) return "SIMPLE_Technician_02";
        if (isTech && has01) return "SIMPLE_Technician_01";
        if (isSup && has02) return "SIMPLE_Supervisor_02";
        if (isSup && has01) return "SIMPLE_Supervisor_01";
        return rawId;
    }

    private bool IsCognitiveSequencePending()
    {
        // When MentalAgentSpawner owns this zone's cognitive process, defer to the
        // authoritative cognitiveReady flag on ZoneDeclarativeMemory rather than
        // iterating individual step completions (which may not all be marked yet).
        if (zoneIndex >= 0)
        {
            if (zoneMemory == null)
                zoneMemory = ZoneDeclarativeMemory.ForZone(zoneIndex);
            if (MentalAgentSpawner.ForZone(zoneIndex) != null
                && zoneMemory != null && zoneMemory.cognitiveReady)
                return false;
        }

        AgentSequenceData cognitive = cognitiveSequenceData;
        if ((cognitive == null || cognitive.actionSequence == null || cognitive.actionSequence.Count == 0) && sequenceManager != null)
        {
            cognitive = GetCognitiveSequenceWithFallback();
            cognitiveSequenceData = cognitive;
        }

        if (cognitive == null || cognitive.actionSequence == null || cognitive.actionSequence.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < cognitive.actionSequence.Count; i++)
        {
            if (!cognitive.actionSequence[i].isStepCompleted)
            {
                return true;
            }
        }

        return false;
    }
    
    /// <summary>
    /// PHASE 4: Calculate pathfinding rewards based on movement direction toward/away from target
    /// CRITICAL: Enforces strict sequence order - only rewards movement toward current step's target
    /// </summary>
    void ApplyPathfindingExtrinsic(float amount, bool updateSkillGauge = true)
    {
        if (UsesRagHudRewardSystem())
        {
            if (amount < 0f)
            {
                RagMlExtrinsicRewardHub.ApplyIncorrectActionPenalty(agentId, amount);
                episodeTotalReward += amount;
                if (updateSkillGauge)
                    UpdateAgentSkillLevelFromReward(amount);
            }
            return;
        }

        AddReward(amount);
        episodeTotalReward += amount;
        if (updateSkillGauge)
            UpdateAgentSkillLevelFromReward(amount);
    }

    private void CalculatePathfindingRewards(Vector3 moveDirection, float distanceBeforeMove)
    {
        // Hard rule: while cognitive sequence is still pending, do not apply operational rewards or penalties.
        if (IsCognitiveSequencePending())
        {
            return;
        }

        if (sequenceManager == null || mySequence == null) return;
        
        ActionSequenceStep currentStep = mySequence.GetCurrentStep();
        if (currentStep == null) return;
        
        // CRITICAL: Check if agent is at wrong tool (sequence violation)
        string currentTargetId = currentStep.targetObjectId;
        string nearbyToolId = GetNearbyToolId();
        
        // For "learn" steps: navigate to the tool, dwell, then process skill learning.
        if (currentStep.actionType == "learn")
        {
            float learnDist = sequenceManager.GetDistanceToTarget(agentId, transform.position, zoneIndex);
            if (learnDist <= 3.0f)
            {
                if (!isAtTarget)
                {
                    isAtTarget = true;
                    timeReachedTarget = Time.time;
                }
                else if (Time.time - timeReachedTarget >= TARGET_HOLD_TIME)
                {
                    isAtTarget = false;
                    timeReachedTarget = 0f;
                    CognitiveProcessManager.EnsureExists();
                    float rdLearn = CognitiveProcessManager.ComputeStationDwellSeconds(currentStep);
                    timeAtTarget = Mathf.Max(timeAtTarget, rdLearn);
                    HandleLearnActionStep(currentStep, rdLearn, timeAlreadyAdvancedByCaller: true);
                    if (mySequence.HasNextStep())
                    {
                        mySequence.MoveToNextStep();
                        activePhysicalStepId = null; // triggers SyncPhysicalStepTarget on next frame
                    }
                    else
                    {
                        CheckAndRewardSequenceCompletion();
                    }
                }
            }
            else
            {
                isAtTarget = false;
            }
            return;
        }

        // Menu steps are managed by EvaluatePhysicalStepArrivalOnly in FixedUpdate (sub-flow:
        // approach button → finger press → show menu → approach option → select → complete).
        if (RagMenuController.IsMenuStep(currentStep)) return;

        // Menu steps are authored as "act", but the policy still needs locomotion shaping
        // to approach the mouse button first, then the selected menu option.
        if (currentStep.actionType != "move" && !RagMenuController.IsMenuStep(currentStep)) return;
        
        // CRITICAL: Calculate distance FIRST to determine if agent is moving toward correct target
        float currentDistance = sequenceManager.GetDistanceToTarget(agentId, transform.position, zoneIndex);
        
        // Only apply wrong tool penalty if:
        // 1. Agent is near a wrong tool
        // 2. Agent is NOT moving toward correct target (distance not decreasing)
        // 3. Agent is NOT already at the wrong tool intentionally (for future steps)
        bool isMovingTowardCorrectTarget = distanceBeforeMove < float.MaxValue && 
                                           currentDistance < distanceBeforeMove - 0.1f; // Allow 0.1 tolerance
        
        if (!string.IsNullOrEmpty(nearbyToolId) && nearbyToolId != currentTargetId && !isMovingTowardCorrectTarget)
        {
            // Agent is at wrong tool AND not moving toward correct target - apply penalty
            float wrongToolPenalty = -5.0f;
            ApplyPathfindingExtrinsic(wrongToolPenalty);
            
            if (enableDetailedLogs && episodeSteps % 10 == 0)
            {
                Debug.LogWarning($"⚠️ [{agentId}] SEQUENCE VIOLATION! At wrong tool {nearbyToolId}, should be at {currentTargetId} (Step {currentStep.stepOrder}). Penalty: {wrongToolPenalty:F2}");
            }
        }
        
        // CRITICAL: Check if agent has reached the CORRECT target (current step's target only)
        // (currentDistance already calculated above)
        bool reachedTarget = sequenceManager.HasReachedTarget(agentId, transform.position, 3.0f, zoneIndex);
        
        // Debug logging to help identify why steps aren't completing
        // CRITICAL: More frequent logging for ML-driven movement debugging
        if (currentStep.actionType == "move")
        {
            if (enableDetailedLogs && episodeSteps % 20 == 0)
        {
                Debug.Log($"🔍 [{agentId}] Move step {currentStep.stepOrder} check: distance={currentDistance:F2}m, threshold=3.0m, reachedTarget={reachedTarget}, isAtTarget={isAtTarget}, nearbyTool={nearbyToolId ?? "none"}, currentTarget={currentTargetId}, timeHeld={(isAtTarget ? (Time.time - timeReachedTarget).ToString("F2") : "0.00")}s");
            }
            
            // CRITICAL: Always log when very close to target (helps debug ML oscillation)
            if (currentDistance <= 4.0f && currentDistance > 3.0f && episodeSteps % 10 == 0)
            {
                Debug.Log($"🎯 [{agentId}] VERY CLOSE to target {currentTargetId}: {currentDistance:F2}m (need <3.0m). ML action: move={currentMoveAction}, rotate={currentRotateAction}");
            }
        }
        
        // Verify that the reached target is actually the current step's target
        // IMPORTANT: Only invalidate reachedTarget if we detect a nearby tool AND it's wrong
        // If nearbyToolId is empty, trust the distance-based check (within 3.0 units)
        if (reachedTarget)
        {
            if (!string.IsNullOrEmpty(nearbyToolId))
            {
                // Proximity detector found a tool - verify it matches
                if (nearbyToolId != currentTargetId)
                {
                    // Agent reached wrong target - don't mark as reached
                    reachedTarget = false;
                    float wrongTargetPenalty = -3.0f;
                    ApplyPathfindingExtrinsic(wrongTargetPenalty);
                    
                    if (enableDetailedLogs)
                    {
                        Debug.LogWarning($"⚠️ [{agentId}] Reached WRONG target {nearbyToolId}, should be at {currentTargetId}! Penalty: {wrongTargetPenalty:F2}");
                    }
                }
                // If nearbyToolId matches currentTargetId, reachedTarget stays true - proceed to completion
                else if (PersonaCognitiveControlSystem.Instance != null)
                {
                    PersonaCognitiveControlSystem.Instance.NotifyToolDiscovered(agentId, nearbyToolId);
                }
            }
            // If nearbyToolId is empty but reachedTarget is true (within 3.0 units), trust the distance check
            // This handles cases where proximity detector might not detect the tool but agent is close enough
        }
        
        // CRITICAL FIX: For ML-driven movement, require agent to HOLD at target for brief time
        // This prevents oscillation from preventing step completion
        if (reachedTarget)
        {
            if (!isAtTarget)
        {
                // Just reached target - start tracking hold time
                isAtTarget = true;
                timeReachedTarget = Time.time;
                if (!UsesRagHudRewardSystem())
                {
                    float targetReachedReward = 4.0f; // From pathfinding_rewards.target_reached_reward
                    AddReward(targetReachedReward);
                    episodeTotalReward += targetReachedReward;
                    UpdateAgentSkillLevelFromReward(targetReachedReward);
                    Debug.Log($"🎯 [{agentId}] REACHED TARGET {currentStep.targetObjectId}! Holding for {TARGET_HOLD_TIME}s before completing step... Reward: +{targetReachedReward:F2}");
                }
                else
                {
                    Debug.Log($"🎯 [{agentId}] REACHED TARGET {currentStep.targetObjectId}! Holding for {TARGET_HOLD_TIME}s before completing step...");
                }
            }
            else
            {
                // Already at target - check if hold time requirement met
                float timeHeld = Time.time - timeReachedTarget;
                
                if (timeHeld >= TARGET_HOLD_TIME)
                {
                    // Hold time requirement met - mark step complete
                    Debug.Log($"✅ [{agentId}] HELD at target {currentStep.targetObjectId} for {timeHeld:F2}s - COMPLETING STEP {currentStep.stepOrder}!");
            
            // Mark step complete and move to next step
            mySequence.MarkStepCompleted();
            NotifyCognitivePhaseOrchestratorStepCompleted(currentStep.stepId);
            ApplyStepCompletionReward(currentStep);
            
            // Check if sequence is now complete (all steps finished)
            bool wasLastStep = !mySequence.HasNextStep();
            
            // Update training results
            if (MLTrainingResultsWriter.Instance != null)
            {
                MLTrainingResultsWriter.Instance.OnStepCompleted(agentId);
            }

            if (_ragOrchestratorPhysicalMode)
            {
                isAtTarget = false;
                timeReachedTarget = 0f;
                timeAtTarget = 0f;
                FinishOrchestratorPhysicalStep();
            }
            else
            {
            // Move to next step
            mySequence.MoveToNextStep();
            if (PersonaCognitiveControlSystem.Instance != null)
            {
                PersonaCognitiveControlSystem.Instance.NotifyStepTransition(agentId);
            }
                    
                    // Reset target tracking for next step
                    isAtTarget = false;
                    timeReachedTarget = 0f;
                    timeAtTarget = 0f;
            
            // CRITICAL: Check if all steps are completed
            if (wasLastStep)
            {
                CheckAndRewardSequenceCompletion();
                    }
            }
                }
                else
                {
                    // Still holding - log progress occasionally
                    if (enableDetailedLogs && episodeSteps % 20 == 0)
                    {
                        Debug.Log($"⏳ [{agentId}] Holding at target {currentStep.targetObjectId}... {timeHeld:F2}s / {TARGET_HOLD_TIME}s");
                    }
                }
            }
        }
        else if (!reachedTarget)
        {
            // Not at target - reset tracking if we were previously at target
            if (isAtTarget)
            {
                // Agent moved away from target - reset (ML model might be oscillating)
                float timeHeld = Time.time - timeReachedTarget;
                if (enableDetailedLogs && timeHeld > 0.1f) // Only log if we held for a bit
                {
                    Debug.LogWarning($"⚠️ [{agentId}] Moved away from target {currentStep.targetObjectId} after holding {timeHeld:F2}s (ML oscillation?)");
                }
            }
            isAtTarget = false;
            timeReachedTarget = 0f;
            timeAtTarget = 0f;
            
            // Calculate if movement is toward or away from target
            // CRITICAL: Always check movement direction if we have valid distance data
            if (moveDirection != Vector3.zero && distanceBeforeMove < float.MaxValue && currentDistance < float.MaxValue)
            {
                // Check if distance decreased (moving toward) or increased (moving away)
                // Use 0.05 threshold to account for small fluctuations
                float distanceChange = distanceBeforeMove - currentDistance;
                bool movingToward = distanceChange > 0.05f; // Moving toward if distance decreased by at least 0.05
                
                // CRITICAL: Check if agent is getting closer to wrong tool (sequence violation)
                // Compare distances to current target vs other tools
                float distToCurrentTarget = currentDistance;
                float closestWrongToolDistance = float.MaxValue;
                string closestWrongTool = null;
                
                // Check all tools to see if agent is moving toward wrong one
                string[] allToolIds = { "tool_001", "tool_002", "tool_003", "tool_004", "workbench_001" };
                foreach (string toolId in allToolIds)
                {
                    if (toolId != currentTargetId) // Skip current target
                    {
                        float distToTool = GetDistanceToTool(toolId);
                        if (distToTool < closestWrongToolDistance)
                        {
                            closestWrongToolDistance = distToTool;
                            closestWrongTool = toolId;
                        }
                    }
                }
                
                // If agent is closer to wrong tool than current target, apply penalty
                if (closestWrongTool != null && closestWrongToolDistance < distToCurrentTarget - 1.0f)
                {
                    float wrongToolDirectionPenalty = -2.0f;
                    ApplyPathfindingExtrinsic(wrongToolDirectionPenalty);
                    
                    if (enableDetailedLogs && episodeSteps % 10 == 0)
                    {
                        Debug.LogWarning($"⚠️ [{agentId}] Moving toward WRONG tool {closestWrongTool} (dist: {closestWrongToolDistance:F2}) instead of {currentTargetId} (dist: {distToCurrentTarget:F2}). Penalty: {wrongToolDirectionPenalty:F2}");
                    }
                }
                
                if (movingToward) // Moving toward CORRECT target
                {
                    // CRITICAL: Always reward if moving toward correct target, regardless of wrong tool distance
                    // This ensures agents get rewarded for correct behavior even if exploring
                    float stepReward = currentStep.correct_step_reward > 0f ? currentStep.correct_step_reward : sequenceManager.GetCurrentStepReward(agentId);
                    if (!UsesRagHudRewardSystem())
                        ApplyPathfindingExtrinsic(stepReward);
                    
                    if (enableDetailedLogs && episodeSteps % 10 == 0) // Log every 10 steps to avoid spam
                    {
                        Debug.Log($"✅ [{agentId}] Moving TOWARD correct target {currentStep.targetObjectId} (Step {currentStep.stepOrder}, dist: {distanceBeforeMove:F2}→{currentDistance:F2}, change: -{distanceChange:F3}). Reward: +{stepReward:F2}");
                    }
                    
                    // Additional check: warn if also moving toward wrong tool (but still reward correct movement)
                    if (closestWrongTool != null && closestWrongToolDistance < distToCurrentTarget - 0.5f)
                    {
                        if (enableDetailedLogs && episodeSteps % 10 == 0)
                        {
                            Debug.LogWarning($"⚠️ [{agentId}] Also near wrong tool {closestWrongTool}, but moving toward correct target - reward still given");
                        }
                    }
                }
                else if (!movingToward && distanceChange < -0.05f) // Moving away from target (distance increased)
                {
                    // Penalty for wrong direction
                    float wrongDirectionPenalty = -1.0f;
                    ApplyPathfindingExtrinsic(wrongDirectionPenalty);
                    
                    if (enableDetailedLogs && episodeSteps % 10 == 0)
                    {
                        Debug.Log($"❌ [{agentId}] Moving AWAY from target {currentStep.targetObjectId} (dist: {distanceBeforeMove:F2}→{currentDistance:F2}). Penalty: {wrongDirectionPenalty:F2}");
                    }
                }
            }
        }
        
        // PHASE 5: Handle "learn" action steps - check if agent should learn at target
        if (currentStep != null && currentStep.actionType == "learn" && reachedTarget)
        {
            CognitiveProcessManager.EnsureExists();
            float rdLearn = CognitiveProcessManager.ComputeStationDwellSeconds(currentStep);
            HandleLearnActionStep(currentStep, rdLearn, timeAlreadyAdvancedByCaller: false);
        }
    }
    
    /// <summary>
    /// PHASE 5: Handle "learn" action steps - hold agent at target, check skill matching, complete step
    /// </summary>
    /// <param name="timeAlreadyAdvancedByCaller">
    /// True when <see cref="ExecutePhysicalScriptedStep"/> already incremented <see cref="timeAtTarget"/> this FixedUpdate.
    /// False for RL / alternate callers that invoke this path directly.
    /// </param>
    private void HandleLearnActionStep(ActionSequenceStep currentStep, float requiredDwellSeconds, bool timeAlreadyAdvancedByCaller)
    {
        // Hard rule: while cognitive sequence is still pending, do not apply operational learn rewards/penalties.
        if (IsCognitiveSequencePending())
        {
            return;
        }

        if (skillSystem == null || !skillSystem.IsDataReady()) return;
        
        // Get target tool ID (used throughout the method)
        string targetToolId = currentStep.targetObjectId;

        if (!timeAlreadyAdvancedByCaller)
            timeAtTarget += Time.fixedDeltaTime;

        if (timeAtTarget >= requiredDwellSeconds)
        {
            // Time requirement met - now check skill matching
            var toolState = skillSystem.GetToolState(targetToolId);
            
            if (toolState != null && toolState.requiredSkills != null && currentStep.requiredSkills != null)
            {
                // Compare requiredSkills from step with tool's requiredSkills by onetSkillCode
                bool allSkillsMatch = true;
                List<string> learnedSkillCodes = new List<string>();
                
                foreach (var stepSkill in currentStep.requiredSkills)
                {
                    // Find matching skill in tool's requiredSkills by onetSkillCode
                    bool skillFound = false;
                    foreach (var toolSkill in toolState.requiredSkills)
                    {
                        if (toolSkill.onetSkillCode == stepSkill.onetSkillCode)
                        {
                            skillFound = true;
                            learnedSkillCodes.Add(stepSkill.onetSkillCode);
                            break;
                        }
                    }
                    
                    if (!skillFound)
                    {
                        allSkillsMatch = false;
                        break;
                    }
                }
                
                if (allSkillsMatch && learnedSkillCodes.Count > 0)
                {
                    // All required skills match - mark step as completed
                    mySequence.MarkStepCompleted();
                    NotifyCognitivePhaseOrchestratorStepCompleted(currentStep.stepId);
                    
                    // PHASE 6: Update learnSkills and availableSkills
                    UpdateAgentLearnedSkills(learnedSkillCodes, toolState.requiredSkills);
                    ApplyStepCompletionReward(currentStep);

                    Debug.Log($"🎓 [{agentId}] LEARNED SKILLS at {targetToolId}: {string.Join(", ", learnedSkillCodes)}. Step completed!");
                    
                    // Update training results
                    if (MLTrainingResultsWriter.Instance != null)
                    {
                        MLTrainingResultsWriter.Instance.OnStepCompleted(agentId);
                        MLTrainingResultsWriter.Instance.OnSkillLearned(agentId);
                    }
                    
                    // Check if this was the last step before moving
                    bool wasLastStep = !mySequence.HasNextStep();
                    
                    // Move to next step
                    mySequence.MoveToNextStep();
                    if (PersonaCognitiveControlSystem.Instance != null)
                    {
                        PersonaCognitiveControlSystem.Instance.NotifyStepTransition(agentId);
                    }
                    timeAtTarget = 0f; // Reset for next step
                    
                    // CRITICAL: Check if all steps are completed
                    if (wasLastStep)
                    {
                        CheckAndRewardSequenceCompletion();
                    }
                }
                else
                {
                    // Skills don't match yet - agent needs to wait longer or skills aren't available
                    if (enableDetailedLogs && episodeSteps % 30 == 0)
                    {
                        Debug.Log($"⏳ [{agentId}] Waiting at {targetToolId} - Skills not yet matched. Time: {timeAtTarget:F1}/{requiredDwellSeconds:F1}s");
                    }
                }
            }
        }
        else
        {
            // Still learning - hold agent at target position
            if (enableDetailedLogs && episodeSteps % 30 == 0)
            {
                Debug.Log($"📚 [{agentId}] Learning at {targetToolId}... Time: {timeAtTarget:F1}/{requiredDwellSeconds:F1}s");
            }
        }
    }
    
    /// <summary>
    /// PHASE 6: Update agent's learnSkills and availableSkills when learning new skills
    /// </summary>
    private void UpdateAgentLearnedSkills(List<string> learnedSkillCodes, SkillRequirement[] toolRequiredSkills)
    {
        if (skillSystem == null || !skillSystem.IsDataReady()) return;
        
        // Get agent profile from skill system
        var agent = skillSystem.GetAgentProfile(NormalizeToSkillId(agentId));
        if (agent == null) return;
        
        // Create list of learned skills to add
        List<AvailableSkill> newLearnedSkills = new List<AvailableSkill>();
        float totalReward = 0f;
        
        foreach (string skillCode in learnedSkillCodes)
        {
            // Find the skill requirement in tool's requiredSkills
            SkillRequirement toolSkill = null;
            foreach (var reqSkill in toolRequiredSkills)
            {
                if (reqSkill.onetSkillCode == skillCode)
                {
                    toolSkill = reqSkill;
                    break;
                }
            }
            
            if (toolSkill != null)
            {
                // Create new AvailableSkill
                AvailableSkill learnedSkill = new AvailableSkill();
                learnedSkill.onetSkillCode = toolSkill.onetSkillCode;
                learnedSkill.skillName = toolSkill.skillName;
                learnedSkill.category = toolSkill.category;
                learnedSkill.availableLevel = toolSkill.requiredLevel; // Agent now has this level
                learnedSkill.isProficient = true;
                
                newLearnedSkills.Add(learnedSkill);
                totalReward += toolSkill.reward; // Accumulate reward from skill
                
                Debug.Log($"📚 [{agentId}] Learned skill: {learnedSkill.skillName} ({learnedSkill.onetSkillCode}) - Reward: +{toolSkill.reward:F2}");
            }
        }
        
        // Update agent's availableSkills array
        if (agent.availableSkills == null)
        {
            agent.availableSkills = new AvailableSkill[0];
        }
        
        List<AvailableSkill> updatedSkills = new List<AvailableSkill>(agent.availableSkills);
        foreach (var newSkill in newLearnedSkills)
        {
            // Check if skill already exists
            bool exists = false;
            for (int i = 0; i < updatedSkills.Count; i++)
            {
                if (updatedSkills[i].onetSkillCode == newSkill.onetSkillCode)
                {
                    // Update existing skill
                    updatedSkills[i] = newSkill;
                    exists = true;
                    break;
                }
            }
            
            if (!exists)
            {
                updatedSkills.Add(newSkill);
            }
        }
        
        agent.availableSkills = updatedSkills.ToArray();
        
        // Update agent's skillLevel with accumulated rewards
        float skillBefore = agent.skillLevel;
        agent.skillLevel += totalReward;
        agent.skillLevel = Mathf.Clamp(agent.skillLevel, 0f, agent.desireLevel); // Don't exceed desireLevel
        
        Debug.Log($"📈 [{agentId}] Skill level updated: {skillBefore:F2} → {agent.skillLevel:F2} (+{totalReward:F2})");
        
        // Update learnSkills in current step (for tracking)
        ActionSequenceStep currentStep = mySequence?.GetCurrentStep();
        if (currentStep != null)
        {
            currentStep.learnSkills = newLearnedSkills.ToArray();
        }
    }
    
    /// <summary>
    /// Check if all steps in action sequence are completed and reward completion
    /// </summary>
    private void CheckAndRewardSequenceCompletion()
    {
        if (mySequence == null || mySequence.actionSequence == null) return;

        // Single-step orchestrator container is not the 20-step operational list.
        if (_ragOrchestratorPhysicalMode && mySequence == _orchestratorStepContainer)
        {
            if (!string.IsNullOrEmpty(_orchestratorActiveStepId))
            {
                ActionSequenceStep orchStep = mySequence.GetCurrentStep();
                if (orchStep != null && orchStep.isStepCompleted)
                {
                    NotifyCognitivePhaseOrchestratorStepCompleted(orchStep.stepId);
                    FinishOrchestratorPhysicalStep();
                }
            }
            return;
        }

        // Count completed steps
        int completedSteps = 0;
        int totalSteps = mySequence.actionSequence.Count;
        
        foreach (var step in mySequence.actionSequence)
        {
            if (step.isStepCompleted)
            {
                completedSteps++;
            }
        }
        
        // Check if all steps are completed
        if (completedSteps >= totalSteps && totalSteps > 0)
        {
            // Cognitive sequence completes first, then switch to normal action sequence.
            if (cognitivePhaseActive)
            {
                cognitivePhaseActive = false;
                activeCognitiveStepId = null;
                activeCognitiveRoute.Clear();
                activeCognitiveRouteIndex = 0;
                ClearCognitiveThreadLine();
                completedCognitiveHighlightTargetId = null;
                hasCompletedCognitiveHighlightTargetPos = false;
                completedCognitiveHighlightTargetPos = Vector3.zero;
                cognitiveCompletionFlashRemaining = 0f;
                if (agentProximity != null)
                {
                    agentProximity.SetExternalColorOverride(false);
                }
                RestoreDefaultAgentColors();
                mySequence = actionSequenceData;
                if (mySequence != null)
                {
                    mySequence.currentStepIndex = 0;
                    isAtTarget = false;
                    timeAtTarget = 0f;
                    timeReachedTarget = 0f;
                    Debug.Log($"🧠➡️🏃 [{agentId}] Cognitive process complete. Starting operational actionSequence.");
                }
                if (PersonaCognitiveControlSystem.Instance != null)
                {
                    PersonaCognitiveControlSystem.Instance.NotifyCognitiveSequenceCompleted(agentId);
                }
                return;
            }

            // Physical operational sequence already finalized — avoid duplicate bonuses / movement.
            if (physicalOperationalSequenceFinished)
                return;

            physicalOperationalSequenceFinished = true;
            Debug.Log($"⏹ [{agentId}] Operational action sequence complete — physical RL/heuristic phase ended.");

            if (!UsesRagHudRewardSystem())
            {
                float sequenceCompletionBonus = 50.0f;
                AddReward(sequenceCompletionBonus);
                episodeTotalReward += sequenceCompletionBonus;
                UpdateAgentSkillLevelFromReward(sequenceCompletionBonus);
            }

            if (skillSystem != null && skillSystem.IsDataReady())
            {
                var agent = skillSystem.GetAgentProfile(NormalizeToSkillId(agentId));
                if (agent != null)
                {
                    agent.isCompleted = true;
                    if (!UsesRagHudRewardSystem())
                        Debug.Log($"🎉 [{agentId}] ✅ SEQUENCE COMPLETE! All {totalSteps} steps finished! Bonus: +50");
                    else
                        Debug.Log($"🎉 [{agentId}] ✅ SEQUENCE COMPLETE (RAG — per-step +1 only, no sequence bonus). Steps: {totalSteps}");
                }
            }

            if (PersonaCognitiveControlSystem.Instance != null)
            {
                PersonaCognitiveControlSystem.Instance.NotifySequenceCompleted(agentId);
            }
            
            // Generate final results when sequence is complete
            if (MLTrainingResultsWriter.Instance != null)
            {
                MLTrainingResultsWriter.Instance.GenerateFinalGameResults();
            }
        }
        else
        {
            Debug.Log($"📊 [{agentId}] Progress: {completedSteps}/{totalSteps} steps completed ({100f * completedSteps / totalSteps:F1}%)");
        }
    }

    /// <summary>True when <c>mlagents-learn</c> (or compatible) has an active Academy communicator.</summary>
    bool IsMlTrainerCommunicatorConnected()
    {
        if (Time.unscaledTime - _mlTrainerProbeTime < MlTrainerProbeInterval)
            return _mlTrainerCommunicatorConnected;

        _mlTrainerProbeTime = Time.unscaledTime;
        _mlTrainerCommunicatorConnected = false;

        try
        {
            Academy academy = Academy.Instance;
            if (academy == null)
                return false;

            FieldInfo communicatorField = academy.GetType().GetField(
                "m_Communicator", BindingFlags.NonPublic | BindingFlags.Instance);
            object communicator = communicatorField?.GetValue(academy);
            if (communicator == null)
                return false;

            PropertyInfo isConnectedProp = communicator.GetType().GetProperty("IsConnected");
            if (isConnectedProp != null && isConnectedProp.GetValue(communicator) is bool b)
                _mlTrainerCommunicatorConnected = b;
        }
        catch
        {
            _mlTrainerCommunicatorConnected = false;
        }

        return _mlTrainerCommunicatorConnected;
    }

    /// <summary>
    /// After cognitive unlock: scripted <see cref="ExecutePhysicalScriptedStep"/> when the trainer is not
    /// driving actions; otherwise use neural policy output in <see cref="OnActionReceived"/>.
    /// </summary>
    bool UseScriptedPhysicalLocomotion()
    {
        // Inference + ONNX testing: policy drives movement; scripted only when ONNX locomotion flag is off.
        if (inferenceOnnxControlsLocomotion)
            return false;

        if (_ragOrchestratorPhysicalMode && physicalUnlocked && !string.IsNullOrEmpty(_orchestratorActiveStepId))
            return true;

        if (!physicalUnlocked || physicalOperationalSequenceFinished || actionSequenceData == null)
            return false;
        if (cognitivePhaseActive || isDoingObservationScan)
            return false;

        BehaviorParameters bp = GetComponent<BehaviorParameters>();
        if (bp == null)
            return true;

        switch (bp.BehaviorType)
        {
            case BehaviorType.HeuristicOnly:
                return true;
            case BehaviorType.InferenceOnly:
                return false;
            case BehaviorType.Default:
            default:
                return !IsMlTrainerCommunicatorConnected();
        }
    }

    private bool IsCognitiveGateBlocked()
    {
        // Once the physical phase is active, the gate must never block RL movement.
        if (physicalUnlocked) return false;
        if (!string.IsNullOrEmpty(_orchestratorActiveStepId)) return false;
        return PersonaCognitiveControlSystem.IsActionBlockedForAgent(agentId);
    }

    private void WriteIdleHeuristic(ActionSegment<int> discreteActions)
    {
        if (discreteActions.Length > 0) discreteActions[0] = 0;
        if (discreteActions.Length > 1) discreteActions[1] = 1;
        if (discreteActions.Length > 2) discreteActions[2] = 0;
    }

    private void ForceIdle()
    {
        if (rb == null)
        {
            return;
        }

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    /// <summary>
    /// Called by MentalAgentController for each completed cognitive station visit.
    /// Updates both the ML-Agents reward signal AND the custom score tracking so
    /// AgentSkillGaugeUI and StepEfficiencyIndicator reflect the cognitive reward immediately.
    /// </summary>
    /// <summary>
    /// Goal Buffer stack failure: applies negative ML reward and P-agent skill delta even while cognitive sequence is pending.
    /// </summary>
    public void AddCognitiveStackPenalty(float positiveMagnitude)
    {
        float p = -Mathf.Abs(positiveMagnitude);
        AddReward(p);
        episodeTotalReward += p;
        UpdateAgentSkillLevelFromReward(p, true);
    }

    /// <summary>
    /// Extrinsic ML-Agents reward + mirror <see cref="episodeTotalReward"/> without HUD duplication.
    /// Use <see cref="RagStepRewardBridge"/> for full HUD+ML standard step (+1).
    /// </summary>
    public void ApplyMlTrainingStepReward(float amount)
    {
        AddReward(amount);
        episodeTotalReward += amount;
    }

    public void AddCognitiveStepReward(float amount)
    {
        if (amount <= 0f) return;
        float k = amount;
        if (ragSpawnPreserveActive || _ragOrchestratorPhysicalMode)
            k = 1f;
        AddReward(k);
        episodeTotalReward += k;
        UpdateAgentSkillLevelFromReward(k);
    }
    
    /// <summary>
    /// Update agent's skill level in SkillBasedActionSystem when rewards are applied
    /// This ensures status board and skill gauge reflect reward changes immediately
    /// </summary>
    /// <summary>
    /// Normalizes any agent name variant (e.g. "Technician_01_zone0", "SIMPLE_Technician_01") to
    /// the canonical SIMPLE_ key used by SkillBasedActionSystem.
    /// </summary>
    private string NormalizeToSkillId(string id)
    {
        if (id.Contains("Technician_01"))  return "SIMPLE_Technician_01";
        if (id.Contains("Technician_02"))  return "SIMPLE_Technician_02";
        if (id.Contains("Supervisor_01"))  return "SIMPLE_Supervisor_01";
        if (id.Contains("Supervisor_02"))  return "SIMPLE_Supervisor_02";
        return id; // fallback: pass through unchanged
    }

    private void UpdateAgentSkillLevelFromReward(float reward, bool allowNegativeDuringCognitive = false)
    {
        if (reward < 0f && IsCognitiveSequencePending() && !allowNegativeDuringCognitive)
            return;

        if (skillSystem == null || !skillSystem.IsDataReady()) return;
        
        // Normalise the agentId to the canonical SIMPLE_* key so the lookup
        // succeeds regardless of how the GameObject is named (e.g. zone-suffix).
        string skillId = NormalizeToSkillId(agentId);

        // Get agent profile from skill system
        var agent = skillSystem.GetAgentProfile(skillId);
        if (agent == null) return;
        
        // Update skill level based on reward (positive rewards increase, penalties decrease)
        float skillBefore = agent.skillLevel;
        agent.skillLevel += reward;
        agent.skillLevel = Mathf.Clamp(agent.skillLevel, 0f, agent.desireLevel); // Don't exceed desireLevel or go negative
        
        // Log significant changes (more than 0.1 points)
        if (Mathf.Abs(reward) > 0.1f)
        {
            Debug.Log($"💰 [{agentId}] Skill level updated from reward: {skillBefore:F2} → {agent.skillLevel:F2} (reward: {reward:+.2f})");
        }
    }
    
    /// <summary>
    /// Process tool action decision from ML-Agents (no rewards/penalties)
    /// </summary>
    private void ProcessMLToolAction(string toolId, int mlActionDecision)
    {
        // ML Action: 1=Positive, 2=Negative/Neutral
        // Process action without rewards/penalties
        
        if (skillSystem == null || !skillSystem.IsDataReady())
        {
            Debug.LogWarning($"🤖 [ML-ACTION] {agentId}: Skill system not ready for tool action");
            return;
        }
        
        // Check what action SHOULD be based on skills (for validation)
        SkillMatchResult skillBasedResult = skillSystem.DetermineAction(agentId, toolId);
        
        string actionTaken = "";
        
        if (mlActionDecision == 1) // ML chose Positive Action
        {
            actionTaken = "Positive";
            
            // Apply skill progression without rewards
            if (skillBasedResult.actionType == ActionType.Positive)
            {
                float skillBefore = skillLevel;
                ApplySkillProgressionOnly(skillBasedResult);
                float skillAfter = skillSystem.GetAgentSkillLevel(agentId);
                positiveActions++;
                
                Debug.Log($"✅ [ML-ACTION] {agentId} → {toolId}: Positive action, Skill: {skillBefore:F1}→{skillAfter:F1}");
                
                // Log to training logger (without reward)
                if (trainingLogger != null)
                {
                    trainingLogger.LogMLAction(agentId, toolId, "Positive", 0f, skillBefore, skillAfter);
                }
            }
            else
            {
                RagMlExtrinsicRewardHub.ApplyIncorrectActionPenalty(agentId, -5f);
                Debug.Log($"❌ [ML-ACTION] {agentId} → {toolId}: INCORRECT Positive action (insufficient skill)");
            }
        }
        else if (mlActionDecision == 2) // ML chose Negative/Neutral Action
        {
            if (skillBasedResult.actionType == ActionType.Negative)
            {
                negativeActions++;
                Debug.Log($"✅ [ML-ACTION] {agentId} → {toolId}: Negative action (aware of limitation)");
                
                // Log to training logger (without reward)
                if (trainingLogger != null)
                {
                    trainingLogger.LogMLAction(agentId, toolId, "Negative", 0f, skillLevel, skillLevel);
                }
            }
            else if (skillBasedResult.actionType == ActionType.Neutral)
            {
                float skillBefore = skillLevel;
                ApplySkillProgressionOnly(skillBasedResult);
                float skillAfter = skillSystem.GetAgentSkillLevel(agentId);
                neutralActions++;
                
                Debug.Log($"✅ [ML-ACTION] {agentId} → {toolId}: Neutral action (already proficient)");
                
                // Log to training logger (without reward)
                if (trainingLogger != null)
                {
                    trainingLogger.LogMLAction(agentId, toolId, "Neutral", 0f, skillBefore, skillAfter);
                }
            }
            else
            {
                RagMlExtrinsicRewardHub.ApplyIncorrectActionPenalty(agentId, -3f);
                Debug.Log($"❌ [ML-ACTION] {agentId} → {toolId}: INCORRECT Negative action (could have learned)");
            }
        }
        
        // Update visual feedback
        if (agentProximity != null)
        {
            agentProximity.SetMLAction(actionTaken, skillBasedResult.actionColor);
        }
    }
    
    /// <summary>
    /// Apply skill progression only (no ML rewards)
    /// </summary>
    private void ApplySkillProgressionOnly(SkillMatchResult result)
    {
        // Update skill level in Unity system (for visual feedback only)
        if (skillSystem != null)
        {
            float skillBefore = skillLevel;
            skillSystem.ApplySkillProgression(agentId, result);
            float skillAfter = skillSystem.GetAgentSkillLevel(agentId);
            skillLevel = skillAfter; // Sync with ML-Agents
            
            // Log skill progression for training visibility
            Debug.Log($"📈 [ML-SKILL] {agentId}: {skillBefore:F1} → {skillAfter:F1} (Δ{skillAfter - skillBefore:+.1f})");
        }
    }
    
    /// <summary>
    /// Update custom statistics for ML-Agents terminal display
    /// Uses Academy's StatsRecorder to log custom metrics visible in training terminal
    /// </summary>
    private void UpdateMLAgentsStats()
    {
        var academy = Academy.Instance;
        if (academy == null || academy.StatsRecorder == null) return;
        
        var statsRecorder = academy.StatsRecorder;
        string behaviorPrefix = $"{behaviorName}/";
        
        // Skill Level Statistics (visible in ML-Agents terminal)
        statsRecorder.Add($"{behaviorPrefix}SkillLevel", skillLevel);
        statsRecorder.Add($"{behaviorPrefix}DesireLevel", desireLevel);
        statsRecorder.Add($"{behaviorPrefix}SkillPercentage", GetSkillPercentage() * 100f);
        
        // Episode Statistics
        statsRecorder.Add($"{behaviorPrefix}EpisodeReward", episodeTotalReward);
        statsRecorder.Add($"{behaviorPrefix}EpisodeSteps", episodeSteps);
        statsRecorder.Add($"{behaviorPrefix}MeanRewardPerStep", episodeSteps > 0 ? episodeTotalReward / episodeSteps : 0f);
        
        // Action Statistics
        statsRecorder.Add($"{behaviorPrefix}PositiveActions", positiveActions);
        statsRecorder.Add($"{behaviorPrefix}NegativeActions", negativeActions);
        statsRecorder.Add($"{behaviorPrefix}NeutralActions", neutralActions);
        
        // Action Ratios
        int totalActions = positiveActions + negativeActions + neutralActions;
        if (totalActions > 0)
        {
            statsRecorder.Add($"{behaviorPrefix}PositiveActionRate", (float)positiveActions / totalActions * 100f);
            statsRecorder.Add($"{behaviorPrefix}NegativeActionRate", (float)negativeActions / totalActions * 100f);
            statsRecorder.Add($"{behaviorPrefix}NeutralActionRate", (float)neutralActions / totalActions * 100f);
        }
        else
        {
            statsRecorder.Add($"{behaviorPrefix}PositiveActionRate", 0f);
            statsRecorder.Add($"{behaviorPrefix}NegativeActionRate", 0f);
            statsRecorder.Add($"{behaviorPrefix}NeutralActionRate", 0f);
        }
        
        // Tool Proximity Status
        string nearbyTool = GetNearbyToolId();
        statsRecorder.Add($"{behaviorPrefix}NearbyTool", !string.IsNullOrEmpty(nearbyTool) ? 1f : 0f);
    }
    
    /// <summary>
    /// Continuously update Unity Console with training statistics
    /// This runs separately from ML-Agents terminal stats for real-time Unity monitoring
    /// </summary>
    private void UpdateUnityConsoleLogs()
    {
        if (!enableDetailedLogs) return;
        
        string nearbyTool = GetNearbyToolId();
        string toolStatus = !string.IsNullOrEmpty(nearbyTool) ? $"Near: {nearbyTool}" : "No tool nearby";
        
        int totalActions = positiveActions + negativeActions + neutralActions;
        float positiveRate = totalActions > 0 ? (float)positiveActions / totalActions * 100f : 0f;
        float meanReward = episodeSteps > 0 ? episodeTotalReward / episodeSteps : 0f;
        
        // Continuous update log - shows current state every 2 seconds
        Debug.Log($"[ML-TRAINING] {agentId} | " +
                  $"Skill: {skillLevel:F1}/{desireLevel:F1} ({GetSkillPercentage()*100:F1}%) | " +
                  $"Reward: {episodeTotalReward:+.2f} (Mean: {meanReward:+.4f}/step) | " +
                  $"Steps: {episodeSteps} | " +
                  $"Actions: +{positiveActions} ({positiveRate:F1}%)/-{negativeActions}/={neutralActions} | " +
                  $"{toolStatus}");
    }
    
    /// <summary>
    /// Log episode summary when episode ends (called before episode reset)
    /// </summary>
    private void LogEpisodeSummary()
    {
        if (episodeSteps == 0) return; // Skip if no steps taken
        
        Debug.Log($"📊 [ML-EPISODE-END] {agentId}");
        Debug.Log($"   Total Reward: {episodeTotalReward:F2}");
        Debug.Log($"   Steps: {episodeSteps}");
        Debug.Log($"   Actions: Positive={positiveActions}, Negative={negativeActions}, Neutral={neutralActions}");
        Debug.Log($"   Skill Progress: {skillLevel:F1}/{desireLevel:F1} ({GetSkillPercentage()*100:F1}%)");
        Debug.Log($"   Mean Reward/Step: {(episodeSteps > 0 ? episodeTotalReward / episodeSteps : 0f):F4}");
        
        // Log to training logger
        if (trainingLogger != null)
        {
            trainingLogger.LogEpisodeEnd(agentId, episodeSteps, episodeTotalReward, skillLevel);
        }
        
        // CRITICAL: Generate final results on episode end (training might be stopping)
        if (MLTrainingResultsWriter.Instance != null)
        {
            MLTrainingResultsWriter.Instance.OnEpisodeEnd(agentId, episodeTotalReward, episodeSteps);
            
            // Also trigger final game results generation
            Debug.Log($"📊 [{agentId}] Episode ended - Triggering final game results generation...");
            MLTrainingResultsWriter.Instance.GenerateFinalGameResults();
        }
    }
    
    /// <summary>RAG ML observations — episode start position.</summary>
    public Vector3 StartPositionForObservation => startPosition;

    /// <summary>1 if agent id heuristic implies skill tag, else 0.</summary>
    public float ObservationSkillBit(string tag) => HasSkill(tag) ? 1f : 0f;

    /// <summary>Whether ML-Agents policy is active (for RAG observation vector).</summary>
    public bool IsMlAgentActive => mlAgentActive;

    public int CurrentToolAction => currentToolAction;
    public bool IsInteractionExecuteRequested => currentToolAction == 2;

    public float ObservationInteractionSubtype()
    {
        ActionSequenceStep step = mySequence != null ? mySequence.GetCurrentStep() : null;
        if (step == null && _ragOrchestratorPhysicalMode && !string.IsNullOrEmpty(_orchestratorActiveStepId))
            step = FindPhysicalStepById(_orchestratorActiveStepId);
        if (RagMenuController.IsMenuStep(step)) return 1f;
        if (step != null && string.Equals(step.actionType, "act", StringComparison.OrdinalIgnoreCase)) return 0.5f;
        return 0f;
    }

    public float ObservationHasSelectedMenuOption()
    {
        ActionSequenceStep step = mySequence != null ? mySequence.GetCurrentStep() : null;
        if (step == null && _ragOrchestratorPhysicalMode && !string.IsNullOrEmpty(_orchestratorActiveStepId))
            step = FindPhysicalStepById(_orchestratorActiveStepId);
        return !string.IsNullOrWhiteSpace(RagMenuController.GetSelectedOptionTargetId(step)) ? 1f : 0f;
    }

    public float ObservationFingerTargetDistance()
    {
        if (agentRole != AgentRole.Physical) return 1f;
        if (handRotationManager == null)
            handRotationManager = HandRotationManager.EnsureOnAgent(gameObject);
        if (handRotationManager == null || handRotationManager.IndexFingerTip == null) return 1f;
        if (!TryGetRagPhysicalObservationTarget(out Vector3 targetPos, out _)) return 1f;
        return Mathf.Clamp01(Vector3.Distance(handRotationManager.IndexFingerTip.position, targetPos) / 2.5f);
    }

    // Helper methods
    private bool HasSkill(string skill)
    {
        if (agentId == null) return false;
        
        string lowerAgentId = agentId.ToLower();
        
        switch (skill.ToLower())
        {
            case "repair":
                return lowerAgentId.Contains("technician");
            case "inspect":
                return lowerAgentId.Contains("supervisor") || lowerAgentId.Contains("inspector");
            case "safety":
                return lowerAgentId.Contains("supervisor") || lowerAgentId.Contains("inspector");
            default:
                return false;
        }
    }
    
    private float GetDistanceToTool(string toolName)
    {
        GameObject tool = GameObject.Find(toolName);
        if (tool != null)
        {
            return Vector3.Distance(transform.position, tool.transform.position);
        }
        return 100f; // Return large distance if tool not found
    }
    
    /// <summary>
    /// Public method to add rewards (called from other systems) - DISABLED: No rewards based on proximity
    /// </summary>
    public void AddTaskReward(float reward)
    {
        // Rewards disabled - proximity-based rewards removed
        // AddReward(reward);
    }
    
    /// <summary>
    /// Update agent properties from JSON data
    /// </summary>
    public void UpdateFromJSON(float skill, float desire)
    {
        this.skillLevel = skill;
        this.desireLevel = desire;
    }
}

