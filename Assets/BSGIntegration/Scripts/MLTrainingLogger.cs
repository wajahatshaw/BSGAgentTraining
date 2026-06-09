using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Comprehensive logging system for ML-Agents training progress
/// Logs to Unity Console and can be extended to log to file/terminal
/// </summary>
public class MLTrainingLogger : MonoBehaviour
{
    /// <summary>When true (inference scene), skip periodic "TRAINING SUMMARY" console spam.</summary>
    public static bool SuppressPeriodicSummary { get; set; }

    private static MLTrainingLogger instance;
    
    [Header("Logging Settings")]
    public bool enableDetailedLogs = true;
    public bool logToUnityConsole = true;
    public float logInterval = 5f; // Log summary every N seconds
    
    private Dictionary<string, AgentTrainingStats> agentStats = new Dictionary<string, AgentTrainingStats>();
    private float lastLogTime = 0f;
    
    public static MLTrainingLogger Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindObjectOfType<MLTrainingLogger>();
                if (instance == null)
                {
                    GameObject loggerObj = new GameObject("MLTrainingLogger");
                    instance = loggerObj.AddComponent<MLTrainingLogger>();
                }
            }
            return instance;
        }
    }
    
    void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }
    
    void Update()
    {
        if (SuppressPeriodicSummary)
            return;

        if (Time.time - lastLogTime >= logInterval)
        {
            LogTrainingSummary();
            lastLogTime = Time.time;
        }
    }
    
    /// <summary>
    /// Log action taken by ML-Agents
    /// </summary>
    public void LogMLAction(string agentId, string toolId, string actionType, float reward, float skillBefore, float skillAfter)
    {
        if (!enableDetailedLogs) return;
        
        EnsureAgentStats(agentId);
        agentStats[agentId].actions++;
        agentStats[agentId].totalReward += reward;
        
        if (actionType == "Positive")
        {
            agentStats[agentId].positiveActions++;
            agentStats[agentId].skillProgress += (skillAfter - skillBefore);
        }
        else if (actionType == "Negative")
        {
            agentStats[agentId].negativeActions++;
        }
        else if (actionType == "Neutral")
        {
            agentStats[agentId].neutralActions++;
        }
        
        if (logToUnityConsole)
        {
            Debug.Log($"🤖 [ML-ACTION] {agentId} → {toolId}: {actionType} | Reward: {reward:+.2f} | Skill: {skillBefore:F1}→{skillAfter:F1}");
        }
    }
    
    /// <summary>
    /// Log a completed RAG/cognitive/physical step (+1 standard reward path).
    /// </summary>
    public void LogStepCompleted(string agentId, string stepId, float reward)
    {
        if (!enableDetailedLogs) return;

        EnsureAgentStats(agentId);
        agentStats[agentId].actions++;
        agentStats[agentId].totalReward += reward;
        agentStats[agentId].positiveActions++;

        if (logToUnityConsole)
        {
            Debug.Log($"🔹 [ML-STEP] {agentId} completed '{stepId}' | reward +{reward:F2}");
        }
    }

    /// <summary>
    /// Log episode end
    /// </summary>
    public void LogEpisodeEnd(string agentId, int steps, float totalReward, float finalSkill)
    {
        EnsureAgentStats(agentId);
        agentStats[agentId].episodes++;
        agentStats[agentId].totalSteps += steps;
        
        if (logToUnityConsole)
        {
            float meanReward = steps > 0 ? totalReward / steps : 0f;
            Debug.Log($"📊 [ML-EPISODE] {agentId} Ended | Steps: {steps} | Total Reward: {totalReward:+.2f} | Mean: {meanReward:+.4f} | Final Skill: {finalSkill:F1}");
        }
    }
    
    /// <summary>
    /// Log comprehensive training summary
    /// </summary>
    public void LogTrainingSummary()
    {
        if (agentStats.Count == 0) return;
        
        if (logToUnityConsole)
        {
            Debug.Log("═══════════════════════════════════════════════════════════");
            Debug.Log("📈 ML-AGENTS TRAINING SUMMARY");
            Debug.Log("═══════════════════════════════════════════════════════════");
            
            foreach (var kvp in agentStats.OrderBy(x => x.Key))
            {
                var stats = kvp.Value;
                float avgRewardPerAction = stats.actions > 0 ? stats.totalReward / stats.actions : 0f;
                float avgStepsPerEpisode = stats.episodes > 0 ? (float)stats.totalSteps / stats.episodes : 0f;
                
                Debug.Log($"\n🤖 {kvp.Key}:");
                Debug.Log($"   Episodes: {stats.episodes} | Total Actions: {stats.actions}");
                Debug.Log($"   Actions: Positive={stats.positiveActions}, Negative={stats.negativeActions}, Neutral={stats.neutralActions}");
                Debug.Log($"   Total Reward: {stats.totalReward:+.2f} | Avg/Action: {avgRewardPerAction:+.4f}");
                Debug.Log($"   Skill Progress: +{stats.skillProgress:F1} points");
                Debug.Log($"   Avg Steps/Episode: {avgStepsPerEpisode:F1}");
            }
            
            Debug.Log("═══════════════════════════════════════════════════════════");
        }
    }
    
    /// <summary>
    /// Get training statistics for an agent
    /// </summary>
    public AgentTrainingStats GetAgentStats(string agentId)
    {
        EnsureAgentStats(agentId);
        return agentStats[agentId];
    }
    
    /// <summary>
    /// Get total episodes across all agents (for training speed calculation)
    /// </summary>
    public int GetTotalEpisodes()
    {
        int totalEpisodes = 0;
        foreach (var stats in agentStats.Values)
        {
            totalEpisodes += stats.episodes;
        }
        return totalEpisodes;
    }
    
    /// <summary>
    /// Get all agent statistics (for external components)
    /// </summary>
    public Dictionary<string, AgentTrainingStats> GetAllAgentStats()
    {
        return new Dictionary<string, AgentTrainingStats>(agentStats);
    }
    
    private void EnsureAgentStats(string agentId)
    {
        if (!agentStats.ContainsKey(agentId))
        {
            agentStats[agentId] = new AgentTrainingStats { agentId = agentId };
        }
    }
    
    /// <summary>
    /// Reset all statistics (call when starting new training session)
    /// </summary>
    public void ResetStats()
    {
        agentStats.Clear();
        Debug.Log("🔄 [ML-LOGGER] Training statistics reset");
    }
}

/// <summary>
/// Training statistics for a single agent
/// </summary>
[System.Serializable]
public class AgentTrainingStats
{
    public string agentId;
    public int episodes = 0;
    public int actions = 0;
    public int positiveActions = 0;
    public int negativeActions = 0;
    public int neutralActions = 0;
    public float totalReward = 0f;
    public float skillProgress = 0f;
    public int totalSteps = 0;
}

