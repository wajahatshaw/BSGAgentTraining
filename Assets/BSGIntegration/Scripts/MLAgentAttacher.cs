using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using Unity.MLAgents.SideChannels;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Static utility to attach ML-Agents components to runtime-created agents
/// </summary>
public static class MLAgentAttacher
{
    /// <summary>
    /// Attach ML-Agents components to a runtime-created agent
    /// </summary>
    /// <param name="ragSpawnWorldPosition">When set, <see cref="BSGMLAgent"/> keeps this spawn for Initialize/episode resets instead of legacy grid positions.</param>
    public static void AttachMLAgentComponents(GameObject agent, string behaviorName, string agentId, Vector3? ragSpawnWorldPosition = null)
    {
        if (agent == null)
        {
            Debug.LogError("❌ Cannot attach ML-Agents to null GameObject");
            return;
        }
        
        Debug.Log($"🔗 Attaching ML-Agents to {agent.name} with behavior: {behaviorName}");
        
        // 1. Add or get Behavior Parameters
        var behaviorParams = agent.GetComponent<BehaviorParameters>();
        if (behaviorParams == null)
        {
            behaviorParams = agent.AddComponent<BehaviorParameters>();
        }
        
        ConfigureBehaviorParameters(behaviorParams, behaviorName, agentId);
        
        // CRITICAL: Force correct action space immediately after configuration
        ForceCorrectActionSpace(behaviorParams, agentId);
        
        // CRITICAL: Double-check Behavior Type is not InferenceOnly
        // This prevents the "Can't use Behavior Type InferenceOnly without a model" error
        System.Type bpType = behaviorParams.GetType();
        var behaviorTypeProperty = bpType.GetProperty("BehaviorType");
        if (behaviorTypeProperty != null)
        {
            int currentType = (int)behaviorTypeProperty.GetValue(behaviorParams);
            if (currentType == 1 && behaviorParams.Model == null)
            {
                int correctType = 0;
                Debug.LogWarning($"⚠️ [{agentId}] InferenceOnly without model — using Default (0) until ONNX is assigned.");
                behaviorTypeProperty.SetValue(behaviorParams, correctType);
            }
        }
        
        // 2. Add Decision Requester
        var decisionRequester = agent.GetComponent<DecisionRequester>();
        if (decisionRequester == null)
        {
            decisionRequester = agent.AddComponent<DecisionRequester>();
        }
        
        ConfigureDecisionRequester(decisionRequester);
        
        // 3. Add or get custom BSG ML-Agent script
        var mlAgent = agent.GetComponent<BSGMLAgent>();
        if (mlAgent == null)
        {
            mlAgent = agent.AddComponent<BSGMLAgent>();
        }
        
        mlAgent.agentId = ZoneAgentIds.NormalizeProfileAgentId(agentId, mlAgent.zoneIndex);
        mlAgent.behaviorName = behaviorName;
        if (ragSpawnWorldPosition.HasValue)
            mlAgent.SetRagSpawnPreserve(ragSpawnWorldPosition.Value);

        if (IsPhysicalAgent(agentId, behaviorName))
            HandRotationManager.EnsureOnAgent(agent);
        
        Debug.Log($"✅ ML-Agents attached to {agent.name} (ID: {agentId}, Behavior: {behaviorName})");
    }

    static bool IsPhysicalAgent(string agentId, string behaviorName)
    {
        if (!string.IsNullOrWhiteSpace(agentId) && agentId.Trim().StartsWith("P", System.StringComparison.OrdinalIgnoreCase))
            return true;
        return !string.IsNullOrWhiteSpace(behaviorName)
               && behaviorName.IndexOf("PhysicalAgent", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// YAML behavior name for RAG physical workers: optional <see cref="AgentProfile.mlBehaviorName"/>, else PhysicalAgentZone{zoneIndex}.
    /// </summary>
    public static string ResolveMlBehaviorNameForRagPhysical(AgentProfile profile, int zoneIndex, string _)
    {
        if (profile != null && !string.IsNullOrWhiteSpace(profile.mlBehaviorName))
            return profile.mlBehaviorName.Trim();
        int z = Mathf.Clamp(zoneIndex, 0, 3);
        return "PhysicalAgentZone" + z;
    }

    /// <summary>YAML behavior for zone cognitive brain (M_A). Optional profile.mlCognitiveBehaviorName.</summary>
    public static string ResolveMlBehaviorNameForRagCognitive(AgentProfile profile, int zoneIndex)
    {
        if (profile != null && !string.IsNullOrWhiteSpace(profile.mlCognitiveBehaviorName))
            return profile.mlCognitiveBehaviorName.Trim();
        int z = Mathf.Clamp(zoneIndex, 0, 3);
        return "CognitiveAgentZone" + z;
    }
    
    /// <summary>
    /// EMERGENCY: Force correct action space immediately
    /// This ensures Unity sends the right action space info to ML-Agents server
    /// </summary>
    private static void ForceCorrectActionSpace(BehaviorParameters bp, string agentId)
    {
        try
        {
            Debug.Log($"🚨 FORCING ACTION SPACE: {agentId}");
            
            // Create correct ActionSpec: Discrete [3,3,3]
            var correctActionSpec = Unity.MLAgents.Actuators.ActionSpec.MakeDiscrete(3, 3, 3);
            
            // Use reflection to force set the ActionSpec
            var brainParamsField = typeof(BehaviorParameters).GetField("m_BrainParameters", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (brainParamsField != null)
            {
                var brainParams = brainParamsField.GetValue(bp);
                var brainParamsType = brainParams.GetType();
                
                // Force set ActionSpec
                var actionSpecField = brainParamsType.GetField("m_ActionSpec",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (actionSpecField != null)
                {
                    actionSpecField.SetValue(brainParams, correctActionSpec);
                    Debug.Log($"✅ FORCED ActionSpec: {agentId} -> Discrete [3,3,3]");
                }
                
                // Force set VectorObservationSize
                var vectorObsSizeField = brainParamsType.GetField("m_VectorObservationSize",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (vectorObsSizeField != null)
                {
                    vectorObsSizeField.SetValue(brainParams, 30);
                    Debug.Log($"✅ FORCED VectorObservationSize: {agentId} -> 30");
                }
            }
            
            // Verify the fix
            Debug.Log($"🔍 VERIFICATION: {agentId}");
            Debug.Log($"  ActionSpec: {bp.BrainParameters.ActionSpec}");
            Debug.Log($"  NumDiscreteActions: {bp.BrainParameters.ActionSpec.NumDiscreteActions}");
            if (bp.BrainParameters.ActionSpec.BranchSizes != null)
            {
                Debug.Log($"  BranchSizes: [{string.Join(", ", bp.BrainParameters.ActionSpec.BranchSizes)}]");
            }
            Debug.Log($"  VectorObservationSize: {bp.BrainParameters.VectorObservationSize}");
            
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ FAILED to force action space for {agentId}: {e.Message}");
        }
    }
    
    private static void ConfigureBehaviorParameters(BehaviorParameters bp, string behaviorName, string agentId)
    {
        bp.BehaviorName = behaviorName;
        bp.TeamId = 0;

        // Inference scene: RagMlBrainDeployer assigns Model + InferenceOnly after attach (not here).
        if (RagInferenceSceneController.IsInferenceSceneActive())
        {
            ForceCorrectActionSpace(bp, agentId);
            return;
        }

            // CRITICAL: Use Default (0) for ML-driven training where Python model controls movement
        // This allows the ML model to learn and make decisions via Python training server
        // Heuristic() method is used as fallback when no action is received from server
        try
        {
            int behaviorType = 0; // Default (0) = ML model controls movement during training
            
            // Use reflection to set Behavior Type
            System.Type bpType = bp.GetType();
            var behaviorTypeProperty = bpType.GetProperty("BehaviorType");
            if (behaviorTypeProperty != null)
            {
                behaviorTypeProperty.SetValue(bp, behaviorType);
                Debug.Log($"✅ [{agentId}] Set Behavior Type to Default (0) - ML model will control movement");
            }
            else
            {
                // Try field if property doesn't exist
                var behaviorTypeField = bpType.GetField("m_BehaviorType",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (behaviorTypeField != null)
                {
                    behaviorTypeField.SetValue(bp, behaviorType);
                    Debug.Log($"✅ [{agentId}] Set Behavior Type to Default (0) via field - ML model will control movement");
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"⚠️ Could not set Behavior Type via reflection: {e.Message}");
        }
        
        // Configure Behavior Parameters at runtime using SerializedObject
        // This works because we're modifying the serialized data directly
        try
        {
#if UNITY_EDITOR
            SerializedObject serializedBP = new SerializedObject(bp);
            
            // CRITICAL: Use Default (0) for ML-driven training where Python model controls movement
            // The ML model learns from observations and rewards to make movement decisions
            // Heuristic() method is used as fallback when no action is received
            int behaviorType = 0; // Default (0) = ML model controls movement during training
            
            SerializedProperty behaviorTypeProp = serializedBP.FindProperty("m_BehaviorType");
            if (behaviorTypeProp != null)
            {
                behaviorTypeProp.enumValueIndex = behaviorType; // 0 = Default
                Debug.Log($"✅ [{behaviorName}] Editor: Set Behavior Type to Default (0) - ML model will control movement");
            }
            
            // Set Vector Observation Space
            SerializedProperty vectorObsSizeProp = serializedBP.FindProperty("m_BrainParameters.m_VectorObservationSize");
            if (vectorObsSizeProp != null)
            {
                vectorObsSizeProp.intValue = 30; // Must match BSGMLAgent.CollectObservations (30 floats)
            }
            
            SerializedProperty numStackedProp = serializedBP.FindProperty("m_BrainParameters.m_NumStackedVectorObservations");
            if (numStackedProp != null)
            {
                numStackedProp.intValue = 1;
            }
            
            // Set Action Space - Discrete
            SerializedProperty actionSpaceTypeProp = serializedBP.FindProperty("m_BrainParameters.m_VectorActionSpaceType");
            if (actionSpaceTypeProp != null)
            {
                actionSpaceTypeProp.intValue = 0; // 0 = Discrete, 1 = Continuous
            }
            
            // Set Discrete Branch Sizes
            SerializedProperty branchSizesProp = serializedBP.FindProperty("m_BrainParameters.m_VectorActionSize");
            if (branchSizesProp != null)
            {
                branchSizesProp.arraySize = 3; // NEW: 3 branches (move, rotate, tool_action)
                branchSizesProp.GetArrayElementAtIndex(0).intValue = 3; // Move: stop, forward, backward
                branchSizesProp.GetArrayElementAtIndex(1).intValue = 3; // Rotate: left, straight, right
                branchSizesProp.GetArrayElementAtIndex(2).intValue = 3; // Tool: none, positive, negative/neutral
            }
            
            // Apply the changes
            serializedBP.ApplyModifiedProperties();
            
            Debug.Log($"✅ Configured Behavior Parameters for {behaviorName}: Type=Default (0 - ML-driven), VectorObs=30, Discrete Actions=[3,3,3]");
#else
            // Runtime build - use reflection to set properties
            ConfigureBehaviorParametersRuntime(bp);
#endif
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Failed to configure Behavior Parameters: {e.Message}");
            Debug.LogWarning($"⚠️ You may need to manually configure in Inspector");
        }
    }
    
#if !UNITY_EDITOR
    private static void ConfigureBehaviorParametersRuntime(BehaviorParameters bp)
    {
        // Runtime build configuration using reflection
        try
        {
            System.Type bpType = bp.GetType();
            
            // CRITICAL: Use Default (0) for ML-driven training where Python model controls movement
            // The ML model learns from observations and rewards to make movement decisions
            int behaviorType = 0; // Default (0) = ML model controls movement during training
            
            var behaviorTypeProperty = bpType.GetProperty("BehaviorType");
            if (behaviorTypeProperty != null)
            {
                behaviorTypeProperty.SetValue(bp, behaviorType);
                Debug.Log($"✅ [{bp.BehaviorName}] Runtime: Set Behavior Type to Default (0) - ML model will control movement");
            }
            else
            {
                // Try field if property doesn't exist
                var behaviorTypeField = bpType.GetField("m_BehaviorType",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (behaviorTypeField != null)
                {
                    behaviorTypeField.SetValue(bp, behaviorType);
                    Debug.Log($"✅ [{bp.BehaviorName}] Runtime: Set Behavior Type to Default (0) via field - ML model will control movement");
                }
            }
            
            var brainParamsField = bpType.GetField("m_BrainParameters", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (brainParamsField != null)
            {
                object brainParams = brainParamsField.GetValue(bp);
                System.Type brainParamsType = brainParams.GetType();
                
                // Set Vector Observation Size — must match BSGMLAgent.CollectObservations (30)
                var vectorObsSizeField = brainParamsType.GetField("m_VectorObservationSize",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (vectorObsSizeField != null)
                {
                    vectorObsSizeField.SetValue(brainParams, 30);
                }
                
                // Set Num Stacked
                var numStackedField = brainParamsType.GetField("m_NumStackedVectorObservations",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (numStackedField != null)
                {
                    numStackedField.SetValue(brainParams, 1);
                }
                
                // Set Action Space Type (0 = Discrete)
                var actionSpaceTypeField = brainParamsType.GetField("m_VectorActionSpaceType",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (actionSpaceTypeField != null)
                {
                    actionSpaceTypeField.SetValue(brainParams, 0);
                }
                
                // Set Branch Sizes - ENHANCED: 3 branches (move, rotate, tool_action)
                var branchSizesField = brainParamsType.GetField("m_VectorActionSize",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (branchSizesField != null)
                {
                    int[] branchSizes = new int[] { 3, 3, 3 }; // NEW: 3 branches instead of 2
                    branchSizesField.SetValue(brainParams, branchSizes);
                }
                
                Debug.Log($"✅ Configured Behavior Parameters (Runtime) for {bp.BehaviorName}: Type=Default (0 - ML-driven), VectorObs=30, Actions=[3,3,3]");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Runtime configuration failed: {e.Message}");
        }
    }
#endif
    
    private static void ConfigureDecisionRequester(DecisionRequester dr)
    {
        // CRITICAL: Set Decision Period to control how often ML model makes decisions
        // Lower = more frequent decisions (faster learning but more CPU)
        // Higher = less frequent decisions (slower learning but less CPU)
        dr.DecisionPeriod = 5; // ML model makes decisions every 5 FixedUpdate steps
        dr.TakeActionsBetweenDecisions = true; // Repeat last action between decisions for smooth movement
        
        Debug.Log($"✅ ConfigureDecisionRequester: DecisionPeriod={dr.DecisionPeriod}, TakeActionsBetweenDecisions={dr.TakeActionsBetweenDecisions}");
    }
    
    /// <summary>
    /// Check if ML-Agents training server is connected
    /// When connected, Default (0) mode allows the ML model to control agent movement
    /// Heuristic() is used as fallback only when model doesn't provide actions
    /// </summary>
    private static bool IsMLAgentsTrainingServerConnected()
    {
        try
        {
            // Check if Academy instance exists and if communicator is active
            var academy = Unity.MLAgents.Academy.Instance;
            if (academy != null)
            {
                // Use reflection to check if communicator is connected
                System.Type academyType = academy.GetType();
                var communicatorField = academyType.GetField("m_Communicator",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (communicatorField != null)
                {
                    var communicator = communicatorField.GetValue(academy);
                    if (communicator != null)
                    {
                        // Check if communicator is active/connected
                        System.Type commType = communicator.GetType();
                        var isConnectedProp = commType.GetProperty("IsConnected");
                        if (isConnectedProp != null)
                        {
                            bool isConnected = (bool)isConnectedProp.GetValue(communicator);
                            return isConnected;
                        }
                    }
                }
            }
            
            // Fallback: Assume training server is NOT connected (use HeuristicOnly)
            // This is safer - if we can't detect, use heuristic for immediate movement
            return false;
        }
        catch (System.Exception e)
        {
            // If we can't detect, default to HeuristicOnly (safer for standalone mode)
            Debug.LogWarning($"⚠️ Could not detect ML-Agents training server status: {e.Message}. Defaulting to HeuristicOnly.");
            return false;
        }
    }
    
    /// <summary>
    /// Determine behavior name based on agent ID from JSON
    /// </summary>
    public static string GetBehaviorNameForAgent(string agentId)
    {
        if (string.IsNullOrEmpty(agentId))
            return "PhysicalAgentZone0";

        // Legacy replica prefab IDs → same quadrant keys as RAG physical workers (YAML PhysicalAgentZone0–3).
        if (agentId.Contains("SIMPLE_Technician_01"))
            return "PhysicalAgentZone0";
        if (agentId.Contains("SIMPLE_Technician_02"))
            return "PhysicalAgentZone1";
        if (agentId.Contains("SIMPLE_Supervisor_01"))
            return "PhysicalAgentZone2";
        if (agentId.Contains("SIMPLE_Supervisor_02"))
            return "PhysicalAgentZone3";

        if (agentId.Contains("Technician_02") || agentId.Contains("Technician02"))
            return "PhysicalAgentZone1";
        if (agentId.Contains("Supervisor_01") || agentId.Contains("Supervisor01"))
            return "PhysicalAgentZone2";
        if (agentId.Contains("Supervisor_02") || agentId.Contains("Supervisor02"))
            return "PhysicalAgentZone3";
        if (agentId.Contains("Technician"))
            return "PhysicalAgentZone0";
        if (agentId.Contains("Supervisor"))
            return "PhysicalAgentZone2";

        Debug.LogWarning($"⚠️ Unknown agent ID for behavior mapping: {agentId} — using PhysicalAgentZone0");
        return "PhysicalAgentZone0";
    }
}

