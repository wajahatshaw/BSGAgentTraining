using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Policies;

/// <summary>
/// Debug script to check ML-Agents setup and diagnose movement issues
/// </summary>
public class MLAgentsDebugger : MonoBehaviour
{
    [Header("Debug Settings")]
    public bool enableDebugLogs = true;
    public float debugInterval = 3f; // Check every 3 seconds
    public float startupGraceSeconds = 8f; // Avoid false negatives during startup gating
    
    private float lastDebugTime = 0f;
    
    void Start()
    {
        if (enableDebugLogs)
        {
            Debug.Log("🔍 MLAgentsDebugger: Starting ML-Agents diagnostics...");
            InvokeRepeating(nameof(RunDiagnostics), 1f, debugInterval);
        }
    }
    
    void RunDiagnostics()
    {
        if (!enableDebugLogs) return;
        
        Debug.Log("🔍 === ML-AGENTS DIAGNOSTICS ===");
        
        // 1. Check Academy status
        CheckAcademyStatus();
        
        // 2. Find all agents and check their setup
        CheckAllAgents();
        
        // 3. Check Python server connection
        CheckPythonConnection();
        
        Debug.Log("🔍 === END DIAGNOSTICS ===");
    }
    
    void CheckAcademyStatus()
    {
        var academy = Academy.Instance;
        if (academy != null)
        {
            Debug.Log($"✅ Academy: Initialized");
            
            // Check communicator status using reflection
            try
            {
                System.Type academyType = academy.GetType();
                var communicatorField = academyType.GetField("m_Communicator",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (communicatorField != null)
                {
                    var communicator = communicatorField.GetValue(academy);
                    if (communicator != null)
                    {
                        System.Type commType = communicator.GetType();
                        var isConnectedProp = commType.GetProperty("IsConnected");
                        if (isConnectedProp != null)
                        {
                            bool isConnected = (bool)isConnectedProp.GetValue(communicator);
                            Debug.Log($"📡 Communicator: {(isConnected ? "CONNECTED" : "NOT CONNECTED")}");
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"⚠️ Could not check communicator status: {e.Message}");
            }
        }
        else
        {
            Debug.LogError("❌ Academy: NOT INITIALIZED");
        }
    }
    
    void CheckAllAgents()
    {
        // Find all BSGMLAgent components
        BSGMLAgent[] allAgents = FindObjectsOfType<BSGMLAgent>();
        Debug.Log($"🤖 Found {allAgents.Length} BSGMLAgent components");
        
        if (allAgents.Length == 0)
        {
            Debug.LogError("❌ NO BSGMLAgent components found! Agents may not be created or ML-Agents not attached.");
            return;
        }
        
        foreach (var agent in allAgents)
        {
            CheckSingleAgent(agent);
        }
    }
    
    void CheckSingleAgent(BSGMLAgent agent)
    {
        string agentId = agent.agentId;
        GameObject agentGO = agent.gameObject;
        
        Debug.Log($"🔍 Checking agent: {agentId}");
        
        // 1. Check BehaviorParameters
        var behaviorParams = agentGO.GetComponent<BehaviorParameters>();
        if (behaviorParams != null)
        {
            Debug.Log($"  ✅ BehaviorParameters: Name={behaviorParams.BehaviorName}, Type={behaviorParams.BehaviorType}");
            
            if (behaviorParams.BehaviorType != BehaviorType.Default)
            {
                Debug.LogError($"  ❌ WRONG BehaviorType! Expected: Default (0), Got: {behaviorParams.BehaviorType}");
            }
            
            Debug.Log($"  📊 VectorObservationSize: {behaviorParams.BrainParameters.VectorObservationSize}");
            Debug.Log($"  🎮 DiscreteActionBranches: [{string.Join(", ", behaviorParams.BrainParameters.ActionSpec.BranchSizes)}]");
        }
        else
        {
            Debug.LogError($"  ❌ BehaviorParameters: MISSING");
        }
        
        // 2. Check DecisionRequester
        var decisionRequester = agentGO.GetComponent<DecisionRequester>();
        if (decisionRequester != null)
        {
            Debug.Log($"  ✅ DecisionRequester: Period={decisionRequester.DecisionPeriod}, TakeActionsBetween={decisionRequester.TakeActionsBetweenDecisions}");
        }
        else
        {
            Debug.LogError($"  ❌ DecisionRequester: MISSING");
        }
        
        // 3. Check Rigidbody
        var rb = agentGO.GetComponent<Rigidbody>();
        if (rb != null)
        {
            Debug.Log($"  🏃 Rigidbody: useGravity={rb.useGravity}, velocity={rb.linearVelocity.magnitude:F2}");
            if (rb.useGravity)
            {
                Debug.LogWarning($"  ⚠️ useGravity is TRUE - this may cause jumping!");
            }
        }
        else
        {
            Debug.LogError($"  ❌ Rigidbody: MISSING");
        }
        
        // 4. Check position
        Debug.Log($"  📍 Position: {agentGO.transform.position}");
        
        // 5. Check if agent is receiving actions
        Debug.Log($"  🎯 Episode Steps: {agent.episodeSteps} (0 = no actions received)");
        if (agent.episodeSteps == 0)
        {
            if (IsAgentInCognitiveScriptedPhase(agent))
            {
                Debug.Log("  ⏸️ Agent is in cognitive scripted phase; RL actions are intentionally not driving movement yet.");
            }
            else
            {
                Debug.LogWarning($"  ⚠️ Agent has not received any ML actions yet!");
            }
        }
    }
    
    void CheckPythonConnection()
    {
        // During startup, agents may intentionally be idle before first ML decision.
        if (Time.timeSinceLevelLoad < startupGraceSeconds)
        {
            Debug.Log($"⏳ ML check in grace period ({Time.timeSinceLevelLoad:F1}s/{startupGraceSeconds:F1}s). Skipping connection failure check.");
            return;
        }

        // Cognitive controller intentionally blocks movement/decisions until init completes.
        if (PersonaCognitiveControlSystem.Instance != null &&
            !PersonaCognitiveControlSystem.Instance.IsGateOpen)
        {
            Debug.Log("⏸️ Cognitive startup gate is closed; agents are intentionally idle. Skipping ML action warning.");
            return;
        }

        // Look for signs that Python server is connected
        BSGMLAgent[] agents = FindObjectsOfType<BSGMLAgent>();
        int agentsReceivingActions = 0;
        int enabledDecisionRequesters = 0;
        bool anyAgentInCognitiveScriptedPhase = false;
        
        foreach (var agent in agents)
        {
            var requester = agent.GetComponent<DecisionRequester>();
            if (requester != null && requester.enabled)
            {
                enabledDecisionRequesters++;
            }

            if (IsAgentInCognitiveScriptedPhase(agent))
            {
                anyAgentInCognitiveScriptedPhase = true;
            }

            if (agent.episodeSteps > 0)
            {
                agentsReceivingActions++;
            }
        }

        if (anyAgentInCognitiveScriptedPhase)
        {
            Debug.Log("⏸️ Cognitive action sequence is running in scripted mode; skipping RL-action connectivity error.");
            return;
        }

        if (enabledDecisionRequesters == 0 && agents.Length > 0)
        {
            Debug.Log("⏸️ All DecisionRequesters are disabled; agents will not receive ML actions yet.");
            return;
        }
        
        if (agentsReceivingActions == 0)
        {
            // Keep this as warning to avoid triggering Unity Error Pause during expected transient states.
            Debug.LogWarning("⚠️ NO agents are receiving ML actions right now. This may be expected during startup/scripted transitions.");
            Debug.LogWarning("⚠️ Check: 1) Python server running? 2) Unity scene playing? 3) Agents connected in Python terminal?");
        }
        else if (agentsReceivingActions < agents.Length)
        {
            Debug.LogWarning($"⚠️ Only {agentsReceivingActions}/{agents.Length} agents receiving ML actions.");
        }
        else
        {
            Debug.Log($"✅ All {agentsReceivingActions} agents are receiving ML actions from Python server!");
        }
    }
    
    [ContextMenu("Run Manual Diagnostics")]
    public void RunManualDiagnostics()
    {
        RunDiagnostics();
    }
    
    [ContextMenu("List All GameObjects with ML Components")]
    public void ListMLComponents()
    {
        Debug.Log("🔍 === ALL ML-AGENTS COMPONENTS ===");
        
        // Find all BehaviorParameters
        var allBehaviorParams = FindObjectsOfType<BehaviorParameters>();
        Debug.Log($"Found {allBehaviorParams.Length} BehaviorParameters:");
        foreach (var bp in allBehaviorParams)
        {
            Debug.Log($"  - {bp.gameObject.name}: {bp.BehaviorName} (Type: {bp.BehaviorType})");
        }
        
        // Find all DecisionRequesters
        var allDecisionRequesters = FindObjectsOfType<DecisionRequester>();
        Debug.Log($"Found {allDecisionRequesters.Length} DecisionRequesters:");
        foreach (var dr in allDecisionRequesters)
        {
            Debug.Log($"  - {dr.gameObject.name}: Period={dr.DecisionPeriod}");
        }
        
        // Find all BSGMLAgents
        var allBSGAgents = FindObjectsOfType<BSGMLAgent>();
        Debug.Log($"Found {allBSGAgents.Length} BSGMLAgents:");
        foreach (var agent in allBSGAgents)
        {
            Debug.Log($"  - {agent.gameObject.name}: ID={agent.agentId}, Behavior={agent.behaviorName}");
        }
    }

    private bool IsAgentInCognitiveScriptedPhase(BSGMLAgent agent)
    {
        if (agent == null || AgentSequenceManager.Instance == null)
        {
            return false;
        }

        AgentSequenceData cognitive = AgentSequenceManager.Instance.GetCognitiveSequence(agent.agentId);
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
}
