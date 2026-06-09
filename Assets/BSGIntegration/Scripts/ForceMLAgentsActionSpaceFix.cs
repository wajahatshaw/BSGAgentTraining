using UnityEngine;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Actuators;

/// <summary>
/// EMERGENCY FIX: Force all ML-Agents to have correct action space
/// This script runs immediately and fixes the action space mismatch
/// </summary>
public class ForceMLAgentsActionSpaceFix : MonoBehaviour
{
    void Awake()
    {
        // Run immediately when scene loads
        Debug.Log("🚨 EMERGENCY: ForceMLAgentsActionSpaceFix starting...");
        
        // Wait a frame for all components to be created
        StartCoroutine(FixActionSpacesDelayed());
    }
    
    System.Collections.IEnumerator FixActionSpacesDelayed()
    {
        // Wait for agents to be created
        yield return new WaitForSeconds(2f);
        
        Debug.Log("🔧 EMERGENCY: Fixing all ML-Agents action spaces NOW...");
        
        // Find all BehaviorParameters
        BehaviorParameters[] allBehaviorParams = FindObjectsOfType<BehaviorParameters>();
        Debug.Log($"🔧 Found {allBehaviorParams.Length} BehaviorParameters to emergency fix");
        
        foreach (var bp in allBehaviorParams)
        {
            EmergencyFixActionSpace(bp);
        }
        
        Debug.Log("✅ EMERGENCY: All action spaces fixed!");
    }
    
    void EmergencyFixActionSpace(BehaviorParameters bp)
    {
        string agentName = bp.gameObject.name;
        Debug.Log($"🚨 EMERGENCY FIXING: {agentName}");
        
        try
        {
            // Create new ActionSpec with discrete branches [3,3,3]
            var actionSpec = ActionSpec.MakeDiscrete(3, 3, 3);
            
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
                    actionSpecField.SetValue(brainParams, actionSpec);
                    Debug.Log($"✅ EMERGENCY: {agentName} ActionSpec set to Discrete [3,3,3]");
                }
                
                // Force set VectorObservationSize to 30 (BSGMLAgent default for RAG training)
                var vectorObsSizeField = brainParamsType.GetField("m_VectorObservationSize",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (vectorObsSizeField != null)
                {
                    int obsSize = 30;
                    vectorObsSizeField.SetValue(brainParams, obsSize);
                    Debug.Log($"✅ EMERGENCY: {agentName} VectorObservationSize set to {obsSize}");
                }
            }
            
            // Verify the fix
            Debug.Log($"🔍 EMERGENCY VERIFY: {agentName}");
            Debug.Log($"  ActionSpec: {bp.BrainParameters.ActionSpec}");
            Debug.Log($"  NumDiscreteActions: {bp.BrainParameters.ActionSpec.NumDiscreteActions}");
            Debug.Log($"  BranchSizes: [{string.Join(", ", bp.BrainParameters.ActionSpec.BranchSizes)}]");
            Debug.Log($"  VectorObservationSize: {bp.BrainParameters.VectorObservationSize}");
            
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ EMERGENCY FAILED: {agentName} - {e.Message}");
        }
    }
}
