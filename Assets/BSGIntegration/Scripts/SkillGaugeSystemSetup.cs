using UnityEngine;

/// <summary>
/// Automatically sets up the complete skill gauge system
/// Adds AgentSkillGaugeUI and AgentCompletionManager to the scene
/// Place this script on any GameObject in your scene
/// </summary>
public class SkillGaugeSystemSetup : MonoBehaviour
{
    [Header("Setup Settings")]
    public bool setupOnStart = true;
    
    [Header("Component References (Auto-created)")]
    public AgentSkillGaugeUI gaugeUI;
    public AgentCompletionManager completionManager;
    public SkillBasedActionSystem skillSystem;
    
    void Start()
    {
        if (setupOnStart)
        {
            SetupSkillGaugeSystem();
        }
    }
    
    [ContextMenu("Setup Skill Gauge System")]
    public void SetupSkillGaugeSystem()
    {
        Debug.Log("🎯 ===================================");
        Debug.Log("🎯 SKILL GAUGE SYSTEM SETUP STARTING");
        Debug.Log("🎯 ===================================");
        
        // Step 1: Find or ensure SkillBasedActionSystem exists
        skillSystem = FindObjectOfType<SkillBasedActionSystem>();
        if (skillSystem == null)
        {
            Debug.LogWarning("⚠️ SkillBasedActionSystem not found - looking for SceneUILoader...");
            SceneUILoader loader = FindObjectOfType<SceneUILoader>();
            if (loader != null)
            {
                skillSystem = loader.gameObject.AddComponent<SkillBasedActionSystem>();
                Debug.Log("✅ Added SkillBasedActionSystem to SceneUILoader");
            }
            else
            {
                GameObject skillSystemGO = new GameObject("SkillBasedActionSystem");
                skillSystem = skillSystemGO.AddComponent<SkillBasedActionSystem>();
                Debug.Log("✅ Created SkillBasedActionSystem GameObject");
            }
        }
        else
        {
            Debug.Log("✅ SkillBasedActionSystem found");
        }
        
        // Step 2: Add AgentCompletionManager if not exists
        completionManager = FindObjectOfType<AgentCompletionManager>();
        if (completionManager == null)
        {
            // Add to this GameObject
            completionManager = gameObject.AddComponent<AgentCompletionManager>();
            Debug.Log("✅ Added AgentCompletionManager to scene");
        }
        else
        {
            Debug.Log("✅ AgentCompletionManager already exists");
        }
        
        // Step 3: Add AgentSkillGaugeUI if not exists
        gaugeUI = FindObjectOfType<AgentSkillGaugeUI>();
        if (gaugeUI == null)
        {
            // Add to this GameObject
            gaugeUI = gameObject.AddComponent<AgentSkillGaugeUI>();
            Debug.Log("✅ Added AgentSkillGaugeUI to scene");
        }
        else
        {
            Debug.Log("✅ AgentSkillGaugeUI already exists");
        }
        
        Debug.Log("🎯 ===================================");
        Debug.Log("🎯 SKILL GAUGE SYSTEM SETUP COMPLETE");
        Debug.Log("🎯 ===================================");
        Debug.Log($"📊 Components Ready:");
        Debug.Log($"   - SkillBasedActionSystem: {skillSystem != null}");
        Debug.Log($"   - AgentCompletionManager: {completionManager != null}");
        Debug.Log($"   - AgentSkillGaugeUI: {gaugeUI != null}");
    }
    
    [ContextMenu("Force Recreate Gauges")]
    public void ForceRecreateGauges()
    {
        // Remove existing gauge UI
        if (gaugeUI != null)
        {
            DestroyImmediate(gaugeUI);
        }
        
        // Add new one
        gaugeUI = gameObject.AddComponent<AgentSkillGaugeUI>();
        
        Debug.Log("✅ Forced recreation of skill gauges");
    }
    
    void OnValidate()
    {
        // Auto-find components when values change in inspector
        if (skillSystem == null) skillSystem = FindObjectOfType<SkillBasedActionSystem>();
        if (completionManager == null) completionManager = FindObjectOfType<AgentCompletionManager>();
        if (gaugeUI == null) gaugeUI = FindObjectOfType<AgentSkillGaugeUI>();
    }
}

