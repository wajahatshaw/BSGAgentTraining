using UnityEngine;
using Unity.MLAgents.Policies;

/// <summary>
/// Emergency fix to ensure all ML-Agents have correct action space configuration
/// This fixes the "Not enough discrete actions" error
/// </summary>
public class MLAgentsActionSpaceFixer : MonoBehaviour
{
    [Header("Action Space Fix")]
    public bool autoFixOnStart = true;
    public bool enableDebugLogs = true;
    
    void Start()
    {
        if (autoFixOnStart)
        {
            Invoke(nameof(FixAllAgentActionSpaces), 1f); // Wait 1 second for agents to be created
        }
    }
    
    [ContextMenu("Fix All Agent Action Spaces")]
    public void FixAllAgentActionSpaces()
    {
        if (enableDebugLogs)
            Debug.Log("🔧 MLAgentsActionSpaceFixer: Starting action space fix...");
        
        // Find all BehaviorParameters in the scene
        BehaviorParameters[] allBehaviorParams = FindObjectsOfType<BehaviorParameters>();
        
        if (allBehaviorParams.Length == 0)
        {
            Debug.LogError("❌ No BehaviorParameters found! ML-Agents may not be attached to agents.");
            return;
        }
        
        Debug.Log($"🔧 Found {allBehaviorParams.Length} BehaviorParameters to fix");
        
        foreach (var bp in allBehaviorParams)
        {
            FixSingleAgentActionSpace(bp);
        }
        
        Debug.Log("✅ MLAgentsActionSpaceFixer: All agents fixed!");
    }
    
    void FixSingleAgentActionSpace(BehaviorParameters bp)
    {
        string agentName = bp.gameObject.name;
        
        if (enableDebugLogs)
        {
            Debug.Log($"🔧 Fixing {agentName}:");
            Debug.Log($"  Current VectorObservationSize: {bp.BrainParameters.VectorObservationSize}");
            Debug.Log($"  Current ActionSpec: {bp.BrainParameters.ActionSpec}");
        }
        
        // Fix Vector Observation Size (BSGMLAgent uses 30 floats in RAG training)
        int expectedObsSize = 30;
        if (bp.BrainParameters.VectorObservationSize != expectedObsSize)
        {
            // Use reflection to set the vector observation size
            try
            {
                var brainParamsField = typeof(BehaviorParameters).GetField("m_BrainParameters", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (brainParamsField != null)
                {
                    var brainParams = brainParamsField.GetValue(bp);
                    var brainParamsType = brainParams.GetType();
                    
                    var vectorObsSizeField = brainParamsType.GetField("m_VectorObservationSize",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    if (vectorObsSizeField != null)
                    {
                        vectorObsSizeField.SetValue(brainParams, expectedObsSize);
                        Debug.Log($"  ✅ Fixed VectorObservationSize: {expectedObsSize}");
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"  ❌ Failed to fix VectorObservationSize: {e.Message}");
            }
        }
        
        // Fix Action Space (should be discrete [3,3,3])
        try
        {
            var brainParamsField = typeof(BehaviorParameters).GetField("m_BrainParameters", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (brainParamsField != null)
            {
                var brainParams = brainParamsField.GetValue(bp);
                var brainParamsType = brainParams.GetType();
                
                // Set ActionSpec to discrete [3,3,3]
                var actionSpecField = brainParamsType.GetField("m_ActionSpec",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (actionSpecField != null)
                {
                    // Create ActionSpec with discrete branches [3,3,3]
                    var actionSpecType = actionSpecField.FieldType;
                    var createDiscreteMethod = actionSpecType.GetMethod("MakeDiscrete", 
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    
                    if (createDiscreteMethod != null)
                    {
                        int[] branchSizes = new int[] { 3, 3, 3 }; // Move, Rotate, Tool
                        var newActionSpec = createDiscreteMethod.Invoke(null, new object[] { branchSizes });
                        actionSpecField.SetValue(brainParams, newActionSpec);
                        
                        Debug.Log($"  ✅ Fixed ActionSpec: Discrete [3,3,3]");
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"  ❌ Failed to fix ActionSpec: {e.Message}");
        }
        
        // Verify the fix
        if (enableDebugLogs)
        {
            Debug.Log($"  Final VectorObservationSize: {bp.BrainParameters.VectorObservationSize}");
            Debug.Log($"  Final ActionSpec: {bp.BrainParameters.ActionSpec}");
        }
    }
    
    [ContextMenu("Debug All Agent Configurations")]
    public void DebugAllAgentConfigurations()
    {
        BehaviorParameters[] allBehaviorParams = FindObjectsOfType<BehaviorParameters>();
        
        Debug.Log($"🔍 === AGENT CONFIGURATION DEBUG ({allBehaviorParams.Length} agents) ===");
        
        foreach (var bp in allBehaviorParams)
        {
            Debug.Log($"🤖 Agent: {bp.gameObject.name}");
            Debug.Log($"  BehaviorName: {bp.BehaviorName}");
            Debug.Log($"  BehaviorType: {bp.BehaviorType}");
            Debug.Log($"  VectorObservationSize: {bp.BrainParameters.VectorObservationSize}");
            Debug.Log($"  ActionSpec: {bp.BrainParameters.ActionSpec}");
            Debug.Log($"  NumContinuousActions: {bp.BrainParameters.ActionSpec.NumContinuousActions}");
            Debug.Log($"  NumDiscreteActions: {bp.BrainParameters.ActionSpec.NumDiscreteActions}");
            
            if (bp.BrainParameters.ActionSpec.BranchSizes != null)
            {
                Debug.Log($"  BranchSizes: [{string.Join(", ", bp.BrainParameters.ActionSpec.BranchSizes)}]");
            }
            else
            {
                Debug.LogError($"  ❌ BranchSizes is NULL!");
            }
        }
        
        Debug.Log("🔍 === END DEBUG ===");
    }
}
