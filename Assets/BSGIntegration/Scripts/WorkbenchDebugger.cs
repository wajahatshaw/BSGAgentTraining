using UnityEngine;

/// <summary>
/// Debug script to troubleshoot workbench_001 negative action issue
/// Add this to any GameObject in the scene to debug the problem
/// </summary>
public class WorkbenchDebugger : MonoBehaviour
{
    [Header("Debug Settings")]
    public string testAgentId = "SIMPLE_Technician_01";
    public string testToolId = "workbench_001";
    
    private SkillBasedActionSystem skillSystem;
    
    void Start()
    {
        skillSystem = FindObjectOfType<SkillBasedActionSystem>();
        
        if (skillSystem == null)
        {
            Debug.LogError("❌ WorkbenchDebugger: SkillBasedActionSystem not found!");
        }
        
        Debug.Log("🔍 WorkbenchDebugger initialized");
    }
    
    [ContextMenu("Debug Workbench_001 Issue")]
    public void DebugWorkbenchIssue()
    {
        if (skillSystem == null)
        {
            Debug.LogError("❌ Cannot debug - SkillBasedActionSystem not found!");
            return;
        }
        
        Debug.Log("🔍 ===== WORKBENCH_001 DEBUG START =====");
        
        // Check if data is loaded
        var agentProfiles = skillSystem.GetAllAgentProfiles();
        if (agentProfiles == null)
        {
            Debug.LogError("❌ No agent profiles loaded!");
            return;
        }
        
        // Check if test agent exists
        if (!agentProfiles.ContainsKey(testAgentId))
        {
            Debug.LogError($"❌ Agent {testAgentId} not found in profiles!");
            Debug.Log($"Available agents: {string.Join(", ", agentProfiles.Keys)}");
            return;
        }
        
        // Check agent data
        var agent = agentProfiles[testAgentId];
        Debug.Log($"🔍 AGENT DATA: {testAgentId}");
        Debug.Log($"   - SkillLevel: {agent.skillLevel}");
        Debug.Log($"   - DesireLevel: {agent.desireLevel}");
        Debug.Log($"   - AvailableSkills: {agent.availableSkills?.Length ?? 0}");
        
        // Check tool data
        var toolStates = skillSystem.GetAllToolStates();
        if (toolStates == null)
        {
            Debug.LogError("❌ No tool states loaded!");
            return;
        }
        
        if (!toolStates.ContainsKey(testToolId))
        {
            Debug.LogError($"❌ Tool {testToolId} not found in tool states!");
            Debug.Log($"Available tools: {string.Join(", ", toolStates.Keys)}");
            return;
        }
        
        var tool = toolStates[testToolId];
        Debug.Log($"🔍 TOOL DATA: {testToolId}");
        Debug.Log($"   - ObjectId: {tool.objectId}");
        Debug.Log($"   - Name: {tool.name}");
        Debug.Log($"   - RequiredSkills: {tool.requiredSkills?.Length ?? 0}");
        
        if (tool.requiredSkills != null)
        {
            for (int i = 0; i < tool.requiredSkills.Length; i++)
            {
                var skill = tool.requiredSkills[i];
                Debug.Log($"   - Skill {i}: {skill.skillName}");
                Debug.Log($"     * OnetSkillCode: {skill.onetSkillCode}");
                Debug.Log($"     * RequiredLevel: {skill.requiredLevel}");
                Debug.Log($"     * Reward: {skill.reward}");
                Debug.Log($"     * IsCritical: {skill.isCritical}");
            }
        }
        
        // Test skill determination
        Debug.Log("🔍 TESTING SKILL DETERMINATION...");
        var result = skillSystem.DetermineAction(testAgentId, testToolId);
        
        Debug.Log($"🔍 RESULT: ActionType = {result.actionType}");
        Debug.Log($"🔍 RESULT: Reason = {result.reason}");
        if (result.matchedSkill != null)
        {
            Debug.Log($"🔍 RESULT: MatchedSkill = {result.matchedSkill.skillName}");
        }
        else
        {
            Debug.Log($"🔍 RESULT: MatchedSkill = null");
        }
        
        // Analyze why result is negative
        Debug.Log("🔍 ANALYZING RESULT...");
        if (result.actionType == ActionType.Negative)
        {
            Debug.LogWarning("⚠️ NEGATIVE ACTION DETECTED!");
            
            // Check each skill requirement
            if (tool.requiredSkills != null)
            {
                foreach (var skill in tool.requiredSkills)
                {
                    bool canLearn = agent.skillLevel >= skill.requiredLevel;
                    bool alreadyHas = skillSystem.HasAgentSkill(testAgentId, skill.onetSkillCode);
                    
                    Debug.Log($"🔍 SKILL ANALYSIS: {skill.skillName}");
                    Debug.Log($"   - CanLearn: {canLearn} (skillLevel {agent.skillLevel} >= requiredLevel {skill.requiredLevel})");
                    Debug.Log($"   - AlreadyHas: {alreadyHas}");
                    
                    if (canLearn && !alreadyHas)
                    {
                        Debug.LogWarning($"⚠️ ISSUE: Agent should be able to learn {skill.skillName} but got negative action!");
                    }
                }
            }
        }
        else if (result.actionType == ActionType.Neutral)
        {
            Debug.Log("⚪ NEUTRAL ACTION DETECTED!");
            
            // Check if agent really knows ALL learnable skills
            if (tool.requiredSkills != null)
            {
                int learnableCount = 0;
                int alreadyKnownCount = 0;
                
                foreach (var skill in tool.requiredSkills)
                {
                    bool canLearn = agent.skillLevel >= skill.requiredLevel;
                    bool alreadyHas = skillSystem.HasAgentSkill(testAgentId, skill.onetSkillCode);
                    
                    if (canLearn)
                    {
                        learnableCount++;
                        if (alreadyHas)
                        {
                            alreadyKnownCount++;
                        }
                    }
                    
                    Debug.Log($"🔍 SKILL ANALYSIS: {skill.skillName}");
                    Debug.Log($"   - CanLearn: {canLearn}, AlreadyHas: {alreadyHas}");
                }
                
                Debug.Log($"🔍 NEUTRAL ANALYSIS: {learnableCount} learnable skills, {alreadyKnownCount} already known");
                
                if (learnableCount > alreadyKnownCount)
                {
                    Debug.LogWarning($"⚠️ ISSUE: Agent should get POSITIVE action! {learnableCount - alreadyKnownCount} skills still learnable!");
                }
                else
                {
                    Debug.Log("✅ NEUTRAL action is correct - agent knows all learnable skills");
                }
            }
        }
        else if (result.actionType == ActionType.Positive)
        {
            Debug.Log("✅ POSITIVE ACTION - This is correct!");
        }
        else if (result.actionType == ActionType.Neutral)
        {
            Debug.Log("⚪ NEUTRAL ACTION - Agent already knows the skill");
        }
        
        Debug.Log("🔍 ===== WORKBENCH_001 DEBUG END =====");
    }
    
    [ContextMenu("Force Test Action")]
    public void ForceTestAction()
    {
        if (skillSystem == null)
        {
            Debug.LogError("❌ Cannot test - SkillBasedActionSystem not found!");
            return;
        }
        
        Debug.Log($"🧪 Force testing action: {testAgentId} -> {testToolId}");
        
        var result = skillSystem.DetermineAction(testAgentId, testToolId);
        skillSystem.ApplySkillProgression(testAgentId, result);
        
        Debug.Log($"🧪 Action applied: {result.actionType} - {result.reason}");
    }
    
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.W))
        {
            DebugWorkbenchIssue();
        }
        
        if (Input.GetKeyDown(KeyCode.T))
        {
            ForceTestAction();
        }
    }
    
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 300, 400, 150));
        GUILayout.Label("Workbench Debugger", GUI.skin.box);
        GUILayout.Label($"Test Agent: {testAgentId}");
        GUILayout.Label($"Test Tool: {testToolId}");
        
        if (GUILayout.Button("Debug Workbench Issue"))
        {
            DebugWorkbenchIssue();
        }
        
        if (GUILayout.Button("Force Test Action"))
        {
            ForceTestAction();
        }
        
        GUILayout.Label("Keyboard shortcuts:");
        GUILayout.Label("W - Debug Workbench Issue");
        GUILayout.Label("T - Force Test Action");
        
        GUILayout.EndArea();
    }
}
