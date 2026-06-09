using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

public enum ActionType
{
    Positive,   // Learning new skill
    Negative,   // Skill penalty
    Neutral     // Already proficient
}

[System.Serializable]
public class SkillMatchResult
{
    public ActionType actionType;
    public SkillRequirement matchedSkill;
    public string reason;
    public Color actionColor;
}

public class SkillBasedActionSystem : MonoBehaviour
{
    [Header("Skill System Settings")]
    // Note: skillPenaltyAmount removed - no penalties applied
    
    private SceneData sceneData;
    private Dictionary<string, AgentProfile> agentProfiles;
    private Dictionary<string, ToolState> toolStates;
    
    // Event to notify when agent completes training
    public delegate void AgentCompletedHandler(string agentId);
    public event AgentCompletedHandler OnAgentCompleted;
    
    void Start()
    {
        Debug.Log($"🎓 SkillBasedActionSystem: Starting data loading process...");
        
        // DON'T create fallback data immediately - wait for real data first
        // CreateFallbackData();
        
        // Try to load real data immediately
        StartCoroutine(LoadDataAfterDelay());
    }
    
    void Awake()
    {
        // Ensure data is available even before Start() is called
        if (agentProfiles == null || toolStates == null)
        {
            Debug.Log($"🎓 SkillBasedActionSystem: Awake() - Creating emergency fallback data");
            CreateFallbackData();
        }
    }
    
    /// <summary>
    /// Coroutine to load data after SceneUILoader has initialized
    /// </summary>
    private SceneUILoader _cachedLoader;

    private System.Collections.IEnumerator LoadDataAfterDelay()
    {
        yield return new WaitForSeconds(0.5f);

        // Cache SceneUILoader once — FindObjectOfType is O(n) over all MonoBehaviours
        if (_cachedLoader == null)
            _cachedLoader = FindObjectOfType<SceneUILoader>();

        int attempts = 0;
        while (attempts < 25)
        {
            if (_cachedLoader == null)
                _cachedLoader = FindObjectOfType<SceneUILoader>();

            if (_cachedLoader != null && _cachedLoader.sceneData != null)
            {
                agentProfiles = _cachedLoader.sceneData.agentProfiles;
                toolStates    = _cachedLoader.sceneData.initialStates;

                if (agentProfiles != null && agentProfiles.Count > 0)
                {
                    bool hasSimple = agentProfiles.ContainsKey("SIMPLE_Supervisor_01") &&
                                     agentProfiles.ContainsKey("SIMPLE_Technician_01");
                    bool hasRag = false;
                    foreach (var k in agentProfiles.Keys)
                    {
                        if (k.Length >= 2 &&
                            (k[0] == 'P' || k[0] == 'p' || k[0] == 'M' || k[0] == 'm') &&
                            char.IsDigit(k[1]))
                        { hasRag = true; break; }
                    }

                    if (hasSimple || hasRag)
                    {
                        Debug.Log($"[SkillSystem] ✅ Profiles loaded ({attempts + 1} attempt) — " +
                                  $"{agentProfiles.Count} agents ({(hasRag ? "RAG" : "SIMPLE")})");
                        sceneData = _cachedLoader.sceneData;
                        yield break;
                    }
                }
            }

            attempts++;
            yield return new WaitForSeconds(0.25f);
        }

        Debug.LogWarning("[SkillSystem] Profiles not found after max attempts — using fallback.");
        CreateFallbackData();
    }

    public void LoadData()
    {
        if (_cachedLoader == null)
            _cachedLoader = FindObjectOfType<SceneUILoader>();
        if (_cachedLoader == null) return;

        sceneData = _cachedLoader.GetSceneData();
        if (sceneData == null) return;

        agentProfiles = sceneData.agentProfiles;
        toolStates    = sceneData.initialStates;
    }
    
    /// <summary>
    /// Create fallback data when JSON loading fails
    /// </summary>
    private void CreateFallbackData()
    {
        Debug.Log($"🎓 SkillBasedActionSystem: Creating fallback data...");
        
        try
        {
            // Create fallback agent profiles
            agentProfiles = new Dictionary<string, AgentProfile>();
            
            // Create fallback tool states
            toolStates = new Dictionary<string, ToolState>();
        
        // Add correct agents with proper skill levels (fallback data) - 0-100 scale
        agentProfiles["SIMPLE_Technician_01"] = new AgentProfile
        {
            agentId = "SIMPLE_Technician_01",
            name = "Alex Rodriguez - Senior Technician",
            role = "Lead Maintenance Technician",
            skillLevel = 0f, // Start from zero and build through runtime rewards/penalties
            desireLevel = 100f,
            isCompleted = false,
            availableSkills = new AvailableSkill[0]
        };
        
        agentProfiles["SIMPLE_Technician_02"] = new AgentProfile
        {
            agentId = "SIMPLE_Technician_02",
            name = "Sarah Johnson - Junior Technician",
            role = "Maintenance Technician",
            skillLevel = 0f, // Start from zero and build through runtime rewards/penalties
            desireLevel = 80f,
            isCompleted = false,
            availableSkills = new AvailableSkill[0]
        };
        
        agentProfiles["SIMPLE_Supervisor_01"] = new AgentProfile
        {
            agentId = "SIMPLE_Supervisor_01",
            name = "Maria Santos - Operations Supervisor",
            role = "Maintenance Supervisor",
            skillLevel = 0f, // Start from zero and build through runtime rewards/penalties
            desireLevel = 90f,
            isCompleted = false,
            availableSkills = new AvailableSkill[0]
        };
        
        agentProfiles["SIMPLE_Supervisor_02"] = new AgentProfile
        {
            agentId = "SIMPLE_Supervisor_02",
            name = "David Kim - Safety Inspector",
            role = "Safety and Compliance Inspector",
            skillLevel = 0f, // Start from zero and build through runtime rewards/penalties
            desireLevel = 100f,
            isCompleted = false,
            availableSkills = new AvailableSkill[0]
        };
        
        // Add correct tools with proper requirements (fallback data)
        toolStates["tool_001"] = new ToolState
        {
            objectId = "tool_001",
            name = "Industrial Motor Unit",
            type = "Tool",
            requiredSkills = new SkillRequirement[]
            {
                new SkillRequirement
                {
                    onetSkillCode = "2.C.3.a",
                    skillName = "Repairing",
                    category = "Repairing",
                    requiredLevel = 5f, // 0-100 scale
                    isCritical = true,
                    reward = 20f
                }
            }
        };
        
        toolStates["tool_002"] = new ToolState
        {
            objectId = "tool_002",
            name = "Secure Toolbox",
            type = "Tool",
            requiredSkills = new SkillRequirement[]
            {
                new SkillRequirement
                {
                    onetSkillCode = "2.C.3.b",
                    skillName = "Equipment Maintenance",
                    category = "EquipmentMaintenance",
                    requiredLevel = 20f, // 0-100 scale
                    isCritical = true,
                    reward = 30f
                }
            }
        };
        
        toolStates["tool_003"] = new ToolState
        {
            objectId = "tool_003",
            name = "Diagnostic Station",
            type = "Tool",
            requiredSkills = new SkillRequirement[]
            {
                new SkillRequirement
                {
                    onetSkillCode = "2.C.3.c",
                    skillName = "Systems Analysis",
                    category = "SystemsAnalysis",
                    requiredLevel = 0f, // 0-100 scale
                    isCritical = true,
                    reward = 30f
                }
            }
        };
        
        toolStates["workbench_001"] = new ToolState
        {
            objectId = "workbench_001",
            name = "Assembly Workbench",
            type = "Workbench",
            requiredSkills = new SkillRequirement[]
            {
                new SkillRequirement
                {
                    onetSkillCode = "2.C.3.d",
                    skillName = "Quality Control",
                    category = "QualityControl",
                    requiredLevel = 30f, // 0-100 scale
                    isCritical = true,
                    reward = 40f
                }
            }
        };
        
        Debug.Log($"🎓 SkillBasedActionSystem: Created fallback data - {agentProfiles.Count} agents, {toolStates.Count} tools");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"🎓 SkillBasedActionSystem: Error creating fallback data: {ex.Message}");
            Debug.LogError($"🎓 SkillBasedActionSystem: Stack trace: {ex.StackTrace}");
            
            // Create minimal fallback data
            agentProfiles = new Dictionary<string, AgentProfile>();
            toolStates = new Dictionary<string, ToolState>();
            
            // Add at least one basic agent
            agentProfiles["Agent001"] = new AgentProfile
            {
                agentId = "Agent001",
                name = "Agent 001",
                role = "Worker",
                skillLevel = 0f,
                desireLevel = 100f,
                isCompleted = false,
                availableSkills = new AvailableSkill[0]
            };
            
            // Add at least one basic tool
            toolStates["Tool_001"] = new ToolState
            {
                objectId = "Tool_001",
                name = "Basic Tool",
                type = "Tool",
                requiredSkills = new SkillRequirement[]
                {
                    new SkillRequirement
                    {
                        onetSkillCode = "2.C.3.a",
                        skillName = "Basic Skill",
                        category = "Basic",
                        requiredLevel = 1.0f, // Easy skill for technicians to learn
                        isCritical = true,
                        reward = 0.2f
                    }
                }
            };
            
            Debug.Log($"🎓 SkillBasedActionSystem: Created minimal fallback data - {agentProfiles.Count} agents, {toolStates.Count} tools");
        }
    }
    
    /// <summary>
    /// Check if the skill system data is ready
    /// </summary>
    public bool IsDataReady()
    {
        return agentProfiles != null && toolStates != null;
    }
    
    /// <summary>
    /// Force ensure data exists - can be called from anywhere
    /// </summary>
    public static void ForceEnsureData()
    {
        SkillBasedActionSystem instance = FindObjectOfType<SkillBasedActionSystem>();
        if (instance != null)
        {
            if (instance.agentProfiles == null || instance.toolStates == null)
            {
                Debug.Log($"🎓 SkillBasedActionSystem: ForceEnsureData - Creating emergency data");
                instance.CreateFallbackData();
            }
        }
    }
    
    /// <summary>
    /// Main method to determine action type based on agent skills and tool requirements
    /// </summary>
    public SkillMatchResult DetermineAction(string agentId, string toolId)
    {
        if (agentProfiles == null || !agentProfiles.ContainsKey(agentId) ||
            toolStates == null  || !toolStates.ContainsKey(toolId))
            return CreateNegativeResult("Agent or tool not found");

        AgentProfile agent = agentProfiles[agentId];
        ToolState tool = toolStates[toolId];

        if (tool.requiredSkills == null || tool.requiredSkills.Length == 0)
            return CreateNegativeResult("Tool has no required skills");

        List<SkillRequirement> learnableSkills    = new List<SkillRequirement>();
        List<SkillRequirement> alreadyKnownSkills = new List<SkillRequirement>();

        foreach (var requiredSkill in tool.requiredSkills)
        {
            if (agent.skillLevel >= requiredSkill.requiredLevel)
            {
                if (!HasSkillInAvailableSkills(agent, requiredSkill.onetSkillCode))
                    learnableSkills.Add(requiredSkill);
                else
                    alreadyKnownSkills.Add(requiredSkill);
            }
        }

        if (learnableSkills.Count > 0)
            return CreatePositiveResult(SelectSkillFromLearnableSkills(learnableSkills, agent), "Learning new skill");

        if (alreadyKnownSkills.Count > 0)
            return CreateNeutralResult(alreadyKnownSkills[0], "Already proficient in all learnable skills");

        return CreateNegativeResult("Insufficient skill level");
    }

    private SkillRequirement SelectSkillFromLearnableSkills(List<SkillRequirement> learnableSkills, AgentProfile agent)
    {
        return learnableSkills[0];
        // Strategy 2: Select skill with highest reward (most beneficial)
        // return learnableSkills.OrderByDescending(s => s.reward).First();
        
        // Strategy 3: Select skill with lowest requiredLevel (easiest to learn)
        // return learnableSkills.OrderBy(s => s.requiredLevel).First();
        
        // Strategy 4: Random selection for variety
        // int randomIndex = Random.Range(0, learnableSkills.Count);
        // return learnableSkills[randomIndex];
        
        // Strategy 5: Select critical skills first
        // var criticalSkills = learnableSkills.Where(s => s.isCritical).ToList();
        // if (criticalSkills.Count > 0)
        //     return criticalSkills[Random.Range(0, criticalSkills.Count)];
        // return learnableSkills[Random.Range(0, learnableSkills.Count)];
    }
    
    /// <summary>
    /// Apply the skill progression based on action result
    /// </summary>
    public void ApplySkillProgression(string agentId, SkillMatchResult result)
    {
        Debug.Log($"🎓 APPLY SKILL PROGRESSION CALLED: {agentId} - ActionType: {result.actionType}");
        
        if (!agentProfiles.ContainsKey(agentId))
        {
            Debug.LogWarning($"🎓 SkillBasedActionSystem: Agent {agentId} not found for skill progression");
            return;
        }
        
        AgentProfile agent = agentProfiles[agentId];
        
        // Don't apply progression if agent already completed
        if (agent.isCompleted)
        {
            Debug.Log($"🎓 AGENT ALREADY COMPLETED: {agentId} - No further skill progression");
            return;
        }
        
        float previousSkillLevel = agent.skillLevel;
        int previousSkillsCount = GetAgentAvailableSkillsCount(agentId);
        float previousPercentage = (agent.skillLevel / agent.desireLevel) * 100f;
        
        Debug.Log($"🎓 BEFORE PROGRESSION: {agentId} - SkillLevel: {previousSkillLevel:F1}/{agent.desireLevel:F1} ({previousPercentage:F1}%), SkillsCount: {previousSkillsCount}");
        
        switch (result.actionType)
        {
            case ActionType.Positive:
                // Increase skill level and add to available skills (no reward-based progression)
                // Use a fixed small increment instead of reward value
                float skillIncrement = 1.0f; // Fixed small increment
                agent.skillLevel += skillIncrement;
                
                // Cap at desireLevel (don't exceed target)
                if (agent.skillLevel > agent.desireLevel)
                {
                    agent.skillLevel = agent.desireLevel;
                    Debug.Log($"🎓 SKILL LEVEL CAPPED: {agentId} reached desireLevel limit of {agent.desireLevel:F1}");
                }
                
                AddSkillToAvailableSkills(agent, result.matchedSkill);
                Debug.Log($"🎓 POSITIVE PROGRESSION: {agentId} learned {result.matchedSkill.skillName}");
                Debug.Log($"🎓 SKILL LEVEL: {previousSkillLevel:F1} -> {agent.skillLevel:F1} (+{skillIncrement:F1})");
                Debug.Log($"🎓 AVAILABLE SKILLS: {previousSkillsCount} -> {GetAgentAvailableSkillsCount(agentId)}");
                break;
                
            case ActionType.Negative:
                // No penalty - just log the action
                Debug.Log($"🎓 NEGATIVE ACTION: {agentId} - Skill level unchanged (no penalty applied)");
                Debug.Log($"🎓 SKILL LEVEL: {agent.skillLevel:F1} (unchanged)");
                Debug.Log($"🎓 AVAILABLE SKILLS: {previousSkillsCount} (unchanged)");
                break;
                
            case ActionType.Neutral:
                // No change to skill level or available skills
                Debug.Log($"🎓 NEUTRAL PROGRESSION: {agentId} already proficient in {result.matchedSkill.skillName}");
                Debug.Log($"🎓 SKILL LEVEL: {agent.skillLevel:F1} (unchanged)");
                Debug.Log($"🎓 AVAILABLE SKILLS: {previousSkillsCount} (unchanged)");
                break;
        }
        
        // Check if agent has reached completion (100% of desireLevel)
        float currentPercentage = (agent.skillLevel / agent.desireLevel) * 100f;
        if (agent.skillLevel >= agent.desireLevel && !agent.isCompleted)
        {
            agent.isCompleted = true;
            Debug.Log($"🎉 ========================================");
            Debug.Log($"🎉 AGENT COMPLETED TRAINING: {agentId}");
            Debug.Log($"🎉 Final SkillLevel: {agent.skillLevel:F1}/{agent.desireLevel:F1} (100%)");
            Debug.Log($"🎉 ========================================");
            
            // Trigger completion event to stop movement
            OnAgentCompleted?.Invoke(agentId);
        }
        
        // Log final state
        Debug.Log($"🎓 FINAL STATE: {agentId} skillLevel={agent.skillLevel:F1}/{agent.desireLevel:F1} ({currentPercentage:F1}%), availableSkills={GetAgentAvailableSkillsCount(agentId)}, isCompleted={agent.isCompleted}");
    }
    
    /// <summary>
    /// Get tool state for ML-Agents observations
    /// </summary>
    public AgentProfile GetAgentProfile(string agentId)
    {
        if (agentProfiles == null || string.IsNullOrWhiteSpace(agentId))
            return null;
        if (agentProfiles.ContainsKey(agentId))
            return agentProfiles[agentId];
        string key = ResolveAgentProfileDictionaryKey(agentId);
        return key != null ? agentProfiles[key] : null;
    }
    
    public ToolState GetToolState(string toolId)
    {
        if (toolStates != null && toolStates.ContainsKey(toolId))
        {
            return toolStates[toolId];
        }
        return null;
    }
    
    /// <summary>
    /// Check if agent has a specific skill in their available skills
    /// </summary>
    private bool HasSkillInAvailableSkills(AgentProfile agent, string skillCode)
    {
        if (agent.availableSkills == null) 
        {
            Debug.Log($"🎓 HAS SKILL CHECK: {agent.agentId} has no availableSkills array - returning false");
            return false;
        }
        
        bool hasSkill = agent.availableSkills.Any(skill => skill.onetSkillCode == skillCode);
        Debug.Log($"🎓 HAS SKILL CHECK: {agent.agentId} looking for skill '{skillCode}' in {agent.availableSkills.Length} available skills - result: {hasSkill}");
        
        if (agent.availableSkills.Length > 0)
        {
            Debug.Log($"🎓 AVAILABLE SKILLS: {string.Join(", ", agent.availableSkills.Select(s => s.onetSkillCode))}");
        }
        
        return hasSkill;
    }
    
    /// <summary>
    /// Add a new skill to agent's available skills
    /// </summary>
    private void AddSkillToAvailableSkills(AgentProfile agent, SkillRequirement skillRequirement)
    {
        if (agent.availableSkills == null)
        {
            agent.availableSkills = new AvailableSkill[0];
        }
        
        // Check if skill already exists
        if (HasSkillInAvailableSkills(agent, skillRequirement.onetSkillCode))
        {
            return; // Skill already exists
        }
        
        // Create new available skill
        AvailableSkill newSkill = new AvailableSkill
        {
            onetSkillCode = skillRequirement.onetSkillCode,
            skillName = skillRequirement.skillName,
            category = skillRequirement.category,
            availableLevel = agent.skillLevel,
            isProficient = true
        };
        
        // Add to array
        var skillList = agent.availableSkills.ToList();
        skillList.Add(newSkill);
        agent.availableSkills = skillList.ToArray();
    }
    
    /// <summary>
    /// Create positive action result
    /// </summary>
    private SkillMatchResult CreatePositiveResult(SkillRequirement skill, string reason)
    {
        return new SkillMatchResult
        {
            actionType = ActionType.Positive,
            matchedSkill = skill,
            reason = reason,
            actionColor = Color.green
        };
    }
    
    /// <summary>
    /// Create negative action result
    /// </summary>
    private SkillMatchResult CreateNegativeResult(string reason)
    {
        return new SkillMatchResult
        {
            actionType = ActionType.Negative,
            matchedSkill = null,
            reason = reason,
            actionColor = Color.black
        };
    }
    
    /// <summary>
    /// Create neutral action result
    /// </summary>
    private SkillMatchResult CreateNeutralResult(SkillRequirement skill, string reason)
    {
        return new SkillMatchResult
        {
            actionType = ActionType.Neutral,
            matchedSkill = skill,
            reason = reason,
            actionColor = Color.gray
        };
    }
    
    /// <summary>
    /// Get agent's current skill level
    /// </summary>
    public float GetAgentSkillLevel(string agentId)
    {
        // Safety check - ensure data exists
        if (agentProfiles == null)
        {
            Debug.LogWarning($"🎓 SkillBasedActionSystem: agentProfiles is null, creating emergency data");
            CreateFallbackData();
            
            // If still null after emergency creation, return default
            if (agentProfiles == null)
            {
                Debug.LogError($"🎓 SkillBasedActionSystem: Emergency data creation failed, returning default skill level");
                return 1.0f;
            }
        }
        
        if (agentProfiles.ContainsKey(agentId))
        {
            float skillLevel = agentProfiles[agentId].skillLevel;
            Debug.Log($"🎓 GET SKILL LEVEL: {agentId} -> {skillLevel:F2}");
            return skillLevel;
        }
        
        Debug.LogWarning($"🎓 SkillBasedActionSystem: Agent {agentId} not found in profiles, returning default skill level 1.0");
        return 1.0f; // Default skill level as defined in JSON
    }
    
    /// <summary>
    /// Get agent's available skills count
    /// </summary>
    public int GetAgentAvailableSkillsCount(string agentId)
    {
        // Safety check - ensure data exists
        if (agentProfiles == null)
        {
            Debug.LogWarning($"🎓 SkillBasedActionSystem: agentProfiles is null, creating emergency data");
            CreateFallbackData();
            
            // If still null after emergency creation, return default
            if (agentProfiles == null)
            {
                Debug.LogError($"🎓 SkillBasedActionSystem: Emergency data creation failed, returning 0");
                return 0;
            }
        }
        
        if (agentProfiles.ContainsKey(agentId) && agentProfiles[agentId].availableSkills != null)
        {
            return agentProfiles[agentId].availableSkills.Length;
        }
        
        Debug.LogWarning($"🎓 SkillBasedActionSystem: Agent {agentId} not found or no available skills, returning 0");
        return 0;
    }
    
    /// <summary>
    /// Get agent's available skills as a formatted string
    /// </summary>
    public string GetAgentAvailableSkillsString(string agentId)
    {
        if (!agentProfiles.ContainsKey(agentId) || agentProfiles[agentId].availableSkills == null)
        {
            return "No skills learned";
        }
        
        if (agentProfiles[agentId].availableSkills.Length == 0)
        {
            return "No skills learned";
        }
        
        var skillNames = agentProfiles[agentId].availableSkills.Select(skill => skill.skillName).ToArray();
        return string.Join(", ", skillNames);
    }
    
    /// <summary>
    /// Check if agent has learned a specific skill
    /// </summary>
    public bool HasAgentLearnedSkill(string agentId, string skillCode)
    {
        if (!agentProfiles.ContainsKey(agentId) || agentProfiles[agentId].availableSkills == null)
        {
            return false;
        }
        
        return agentProfiles[agentId].availableSkills.Any(skill => skill.onetSkillCode == skillCode);
    }
    
    /// <summary>
    /// Get detailed agent skill report
    /// </summary>
    public string GetAgentSkillReport(string agentId)
    {
        if (!agentProfiles.ContainsKey(agentId))
        {
            return $"Agent {agentId} not found";
        }
        
        AgentProfile agent = agentProfiles[agentId];
        string report = $"=== AGENT SKILL REPORT: {agentId} ===\n";
        report += $"Current Skill Level: {agent.skillLevel:F2}\n";
        report += $"Available Skills Count: {GetAgentAvailableSkillsCount(agentId)}\n";
        report += $"Learned Skills: {GetAgentAvailableSkillsString(agentId)}\n";
        
        return report;
    }
    
    /// <summary>
    /// Get all agent profiles for external systems
    /// </summary>
    public Dictionary<string, AgentProfile> GetAllAgentProfiles()
    {
        // Safety check - ensure data exists
        if (agentProfiles == null)
        {
            Debug.LogWarning($"🎓 SkillBasedActionSystem: agentProfiles is null, creating emergency data");
            CreateFallbackData();
            
            // If still null after emergency creation, return empty dictionary
            if (agentProfiles == null)
            {
                Debug.LogError($"🎓 SkillBasedActionSystem: Emergency data creation failed, returning empty dictionary");
                return new Dictionary<string, AgentProfile>();
            }
        }
        
        return agentProfiles;
    }
    
    /// <summary>
    /// Get all tool states for external systems
    /// </summary>
    public Dictionary<string, ToolState> GetAllToolStates()
    {
        // Safety check - ensure data exists
        if (toolStates == null)
        {
            Debug.LogWarning($"🎓 SkillBasedActionSystem: toolStates is null, creating emergency data");
            CreateFallbackData();
            
            // If still null after emergency creation, return empty dictionary
            if (toolStates == null)
            {
                Debug.LogError($"🎓 SkillBasedActionSystem: Emergency data creation failed, returning empty dictionary");
                return new Dictionary<string, ToolState>();
            }
        }
        
        return toolStates;
    }
    
    /// <summary>
    /// Check if agent has a specific skill
    /// </summary>
    public bool HasAgentSkill(string agentId, string onetSkillCode)
    {
        if (!agentProfiles.ContainsKey(agentId))
        {
            return false;
        }
        
        AgentProfile agent = agentProfiles[agentId];
        return HasSkillInAvailableSkills(agent, onetSkillCode);
    }
    
    /// <summary>
    /// Public method to trigger agent completion event (for testing purposes)
    /// </summary>
    public void TriggerAgentCompletionEvent(string agentId)
    {
        Debug.Log($"🧪 Triggering completion event for {agentId}");
        OnAgentCompleted?.Invoke(agentId);
    }

    /// <summary>
    /// RAG per-zone: add <paramref name="reward"/> to a physical agent profile (same running total for cognitive + operational phases).
    /// </summary>
    /// <summary>Matches dictionary keys where JSON key and <see cref="AgentProfile.agentId"/> differ.</summary>
    string ResolveAgentProfileDictionaryKey(string agentId)
    {
        if (agentProfiles == null || string.IsNullOrWhiteSpace(agentId)) return null;
        if (agentProfiles.ContainsKey(agentId)) return agentId;
        foreach (var kvp in agentProfiles)
        {
            if (kvp.Value == null) continue;
            if (!string.IsNullOrEmpty(kvp.Value.agentId) &&
                string.Equals(kvp.Value.agentId, agentId, StringComparison.OrdinalIgnoreCase))
                return kvp.Key;
            if (string.Equals(kvp.Key, agentId, StringComparison.OrdinalIgnoreCase))
                return kvp.Key;
        }
        return null;
    }

    public void ApplyCorrectStepReward(string agentId, float reward)
    {
        if (reward <= 0f || string.IsNullOrWhiteSpace(agentId)) return;

        // Re-load from scene if profiles are still the SIMPLE_* fallback while RAG data is now ready
        if (agentProfiles != null && agentProfiles.Count > 0)
        {
            bool hasFallbackOnly = agentProfiles.ContainsKey("SIMPLE_Technician_01");
            bool hasRequestedId = ResolveAgentProfileDictionaryKey(agentId) != null;
            if (hasFallbackOnly && !hasRequestedId)
            {
                Debug.LogWarning($"[SkillBasedActionSystem] ApplyCorrectStepReward: profiles are SIMPLE_* fallback but reward is for '{agentId}' — reloading from SceneUILoader.");
                LoadData();
            }
        }

        if (agentProfiles == null)
        {
            Debug.LogWarning($"[SkillBasedActionSystem] ApplyCorrectStepReward: agentProfiles is null — reward for '{agentId}' lost.");
            return;
        }

        string key = ResolveAgentProfileDictionaryKey(agentId);
        if (string.IsNullOrEmpty(key))
        {
            Debug.LogWarning($"[SkillBasedActionSystem] ApplyCorrectStepReward: agent '{agentId}' not found. " +
                             $"Known agents: [{string.Join(", ", agentProfiles.Keys)}]");
            return;
        }

        AgentProfile agent = agentProfiles[key];
        if (agent.isCompleted)
        {
            Debug.Log($"[SkillBasedActionSystem] {agentId} already completed — reward skipped.");
            return;
        }

        float before = agent.skillLevel;
        float cap = agent.desireLevel > 0f ? agent.desireLevel : float.MaxValue;
        agent.skillLevel += reward;
        if (agent.skillLevel > cap) agent.skillLevel = cap;

        Debug.Log($"[SKILL-REWARD] ✅ {agentId} skillLevel: {before:F0} → {agent.skillLevel:F0}  (+{reward:F0})  cap={cap:F0}");

        if (agent.desireLevel > 0f && agent.skillLevel >= agent.desireLevel && !agent.isCompleted)
        {
            agent.isCompleted = true;
            Debug.Log($"🎉 [SkillBasedActionSystem] Agent {agentId} reached desire cap ({agent.desireLevel:F0}) — COMPLETED");
            OnAgentCompleted?.Invoke(agentId);
        }
    }

    public void MarkAgentCompleted(string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId)) return;
        EnsureProfilesAvailableFor(agentId);

        string key = ResolveAgentProfileDictionaryKey(agentId);
        if (string.IsNullOrEmpty(key))
        {
            Debug.LogWarning($"[SkillBasedActionSystem] MarkAgentCompleted: agent '{agentId}' not found.");
            return;
        }

        AgentProfile agent = agentProfiles[key];
        if (agent == null) return;
        if (agent.isCompleted) return;

        agent.isCompleted = true;
        if (agent.desireLevel > 0f && agent.skillLevel < agent.desireLevel)
            agent.skillLevel = agent.desireLevel;

        Debug.Log($"🎉 [SkillBasedActionSystem] Agent {agentId} marked COMPLETED by sequence completion.");
        OnAgentCompleted?.Invoke(agentId);
    }

    public void MarkZoneAgentsCompleted(int zoneIndex)
    {
        if (agentProfiles == null)
            LoadData();
        if (agentProfiles == null) return;

        List<string> zoneAgentIds = new List<string>();
        foreach (var kvp in agentProfiles)
        {
            AgentProfile ap = kvp.Value;
            if (ap == null || ap.zoneIndex != zoneIndex) continue;
            string id = !string.IsNullOrWhiteSpace(ap.agentId) ? ap.agentId : kvp.Key;
            zoneAgentIds.Add(id);
        }

        foreach (string id in zoneAgentIds)
            MarkAgentCompleted(id);
    }

    void EnsureProfilesAvailableFor(string agentId)
    {
        if (agentProfiles == null)
        {
            LoadData();
            return;
        }

        bool hasFallbackOnly = agentProfiles.ContainsKey("SIMPLE_Technician_01");
        bool hasRequestedId = ResolveAgentProfileDictionaryKey(agentId) != null;
        if (hasFallbackOnly && !hasRequestedId)
            LoadData();
    }

    /// <summary>
    /// Resolve the physical agent id (P*) for a zone index from loaded profiles.
    /// </summary>
    public string GetPhysicalAgentIdForZone(int zoneIndex)
    {
        if (agentProfiles == null) return null;
        foreach (var kvp in agentProfiles)
        {
            AgentProfile ap = kvp.Value;
            if (ap == null || ap.zoneIndex != zoneIndex) continue;
            string id = !string.IsNullOrEmpty(ap.agentId) ? ap.agentId : kvp.Key;
            if (id.Length > 0 && id.StartsWith("P", StringComparison.OrdinalIgnoreCase))
                return id;
        }
        foreach (var kvp in agentProfiles)
        {
            AgentProfile ap = kvp.Value;
            if (ap == null || ap.zoneIndex != zoneIndex) continue;
            string role = ap.role ?? string.Empty;
            if (role.IndexOf("physical", StringComparison.OrdinalIgnoreCase) >= 0)
                return !string.IsNullOrEmpty(ap.agentId) ? ap.agentId : kvp.Key;
        }
        return null;
    }
}
