using UnityEngine;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Writes ML-Agents training progress back to JSON file
/// Updates basicUi_ml.json with agent progress: isStepCompleted, learnSkills, availableSkills, skillLevel
/// </summary>
public class MLTrainingJSONWriter : MonoBehaviour
{
    private static MLTrainingJSONWriter _instance;
    public static MLTrainingJSONWriter Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<MLTrainingJSONWriter>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("MLTrainingJSONWriter");
                    _instance = go.AddComponent<MLTrainingJSONWriter>();
                }
            }
            return _instance;
        }
    }
    
    [Header("Output Settings")]
    public string inputJsonFile = "basicUi_ml.json";
    public string outputJsonFile = "basicUi_ml_output.json"; // Separate output file
    public bool autoSave = true; // Auto-save when agents progress
    public float autoSaveInterval = 10f; // Save every 10 seconds
    
    private AgentSequenceManager sequenceManager;
    private SkillBasedActionSystem skillSystem;
    private float lastAutoSave = 0f;
    
    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }
    
    void Start()
    {
        sequenceManager = AgentSequenceManager.Instance;
        skillSystem = FindObjectOfType<SkillBasedActionSystem>();
    }
    
    void Update()
    {
        if (autoSave && Time.time - lastAutoSave >= autoSaveInterval)
        {
            SaveProgressToJSON();
            lastAutoSave = Time.time;
        }
    }
    
    /// <summary>
    /// Save current agent progress to JSON output file
    /// </summary>
    public void SaveProgressToJSON()
    {
        try
        {
            string inputPath = Path.Combine(Application.dataPath, "JsonFile", inputJsonFile);
            if (!File.Exists(inputPath))
            {
                Debug.LogWarning($"⚠️ MLTrainingJSONWriter: Input file not found: {inputPath}");
                return;
            }
            
            string jsonContent = File.ReadAllText(inputPath);
            
            // Update JSON with current agent progress
            string updatedJson = UpdateJSONWithProgress(jsonContent);
            
            // Write to output file
            string outputPath = Path.Combine(Application.dataPath, "JsonFile", outputJsonFile);
            File.WriteAllText(outputPath, updatedJson);
            
            Debug.Log($"✅ MLTrainingJSONWriter: Saved progress to {outputJsonFile}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ MLTrainingJSONWriter: Error saving JSON: {e.Message}");
        }
    }
    
    /// <summary>
    /// Update JSON content with current agent progress
    /// </summary>
    private string UpdateJSONWithProgress(string jsonContent)
    {
        StringBuilder sb = new StringBuilder(jsonContent);
        
        if (sequenceManager != null && skillSystem != null)
        {
            // Update each agent's progress
            foreach (var kvp in skillSystem.GetAllAgentProfiles())
            {
                string agentId = kvp.Key;
                var agent = kvp.Value;
                var sequence = sequenceManager.GetSequence(agentId);
                
                if (sequence != null)
                {
                    // Update agent skillLevel, availableSkills in agentProfiles section
                    UpdateAgentProfile(sb, agentId, agent);
                    
                    // Update actionSequence steps (isStepCompleted, learnSkills)
                    UpdateActionSequence(sb, agentId, sequence);
                }
            }
        }
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Update agent profile section with current skillLevel and availableSkills
    /// </summary>
    private void UpdateAgentProfile(StringBuilder sb, string agentId, AgentProfile agent)
    {
        // Find agent profile section
        string searchKey = $"\"{agentId}\"";
        int agentStart = sb.ToString().IndexOf(searchKey);
        if (agentStart == -1) return;
        
        // Update skillLevel
        UpdateJsonField(sb, agentStart, "\"skillLevel\"", agent.skillLevel);
        
        // Update isCompleted
        UpdateJsonField(sb, agentStart, "\"isCompleted\"", agent.isCompleted ? "true" : "false");
        
        // Update availableSkills array
        UpdateAvailableSkillsArray(sb, agentStart, agent.availableSkills);
    }
    
    /// <summary>
    /// Update actionSequence steps with isStepCompleted and learnSkills
    /// </summary>
    private void UpdateActionSequence(StringBuilder sb, string agentId, AgentSequenceData sequence)
    {
        // Find actionSequence section for this agent
        string searchKey = $"\"{agentId}\"";
        int agentStart = sb.ToString().IndexOf(searchKey);
        if (agentStart == -1) return;
        
        int actionSeqStart = sb.ToString().IndexOf("\"actionSequence\":", agentStart);
        if (actionSeqStart == -1) return;
        
        // Update each step
        for (int i = 0; i < sequence.actionSequence.Count; i++)
        {
            var step = sequence.actionSequence[i];
            
            // Find step by stepId
            string stepSearchKey = $"\"{step.stepId}\"";
            int stepStart = sb.ToString().IndexOf(stepSearchKey, actionSeqStart);
            if (stepStart == -1) continue;
            
            // Update isStepCompleted
            UpdateJsonField(sb, stepStart, "\"isStepCompleted\"", step.isStepCompleted ? "true" : "false");
            
            // Update learnSkills array if step is completed
            if (step.isStepCompleted && step.learnSkills != null && step.learnSkills.Length > 0)
            {
                UpdateLearnSkillsArray(sb, stepStart, step.learnSkills);
            }
        }
    }
    
    /// <summary>
    /// Update a JSON field value
    /// </summary>
    private void UpdateJsonField(StringBuilder sb, int startPos, string fieldName, object value)
    {
        string json = sb.ToString();
        int fieldIndex = json.IndexOf(fieldName, startPos);
        if (fieldIndex == -1) return;
        
        int valueStart = json.IndexOf(":", fieldIndex) + 1;
        while (valueStart < json.Length && (json[valueStart] == ' ' || json[valueStart] == '\t')) valueStart++;
        
        int valueEnd = valueStart;
        
        // Find end of value (number, boolean, or string)
        if (json[valueStart] == '"')
        {
            valueEnd = json.IndexOf("\"", valueStart + 1) + 1;
        }
        else if (char.IsDigit(json[valueStart]) || json[valueStart] == '-')
        {
            while (valueEnd < json.Length && (char.IsDigit(json[valueEnd]) || json[valueEnd] == '.' || json[valueEnd] == '-')) valueEnd++;
        }
        else if (json.Substring(valueStart, 4) == "true" || json.Substring(valueStart, 4) == "True")
        {
            valueEnd = valueStart + 4;
        }
        else if (json.Substring(valueStart, 5) == "false" || json.Substring(valueStart, 5) == "False")
        {
            valueEnd = valueStart + 5;
        }
        
        // Replace value
        string valueStr = value is bool ? value.ToString().ToLower() : value.ToString();
        sb.Remove(valueStart, valueEnd - valueStart);
        sb.Insert(valueStart, valueStr);
    }
    
    /// <summary>
    /// Update availableSkills array in JSON
    /// </summary>
    private void UpdateAvailableSkillsArray(StringBuilder sb, int agentStart, AvailableSkill[] skills)
    {
        string json = sb.ToString();
        int skillsStart = json.IndexOf("\"availableSkills\":", agentStart);
        if (skillsStart == -1) return;
        
        int arrayStart = json.IndexOf("[", skillsStart);
        int arrayEnd = json.IndexOf("]", arrayStart);
        
        if (arrayStart == -1 || arrayEnd == -1) return;
        
        // Build new array string
        StringBuilder newArray = new StringBuilder("[");
        if (skills != null && skills.Length > 0)
        {
            for (int i = 0; i < skills.Length; i++)
            {
                if (i > 0) newArray.Append(", ");
                newArray.Append($"{{\"onetSkillCode\":\"{skills[i].onetSkillCode}\",\"skillName\":\"{skills[i].skillName}\",\"category\":\"{skills[i].category}\",\"availableLevel\":{skills[i].availableLevel},\"isProficient\":{skills[i].isProficient.ToString().ToLower()}}}");
            }
        }
        newArray.Append("]");
        
        // Replace array
        sb.Remove(arrayStart, arrayEnd - arrayStart + 1);
        sb.Insert(arrayStart, newArray.ToString());
    }
    
    /// <summary>
    /// Update learnSkills array in JSON step
    /// </summary>
    private void UpdateLearnSkillsArray(StringBuilder sb, int stepStart, AvailableSkill[] skills)
    {
        string json = sb.ToString();
        int skillsStart = json.IndexOf("\"learnSkills\":", stepStart);
        if (skillsStart == -1) return;
        
        int arrayStart = json.IndexOf("[", skillsStart);
        int arrayEnd = json.IndexOf("]", arrayStart);
        
        if (arrayStart == -1 || arrayEnd == -1) return;
        
        // Build new array string
        StringBuilder newArray = new StringBuilder("[");
        if (skills != null && skills.Length > 0)
        {
            for (int i = 0; i < skills.Length; i++)
            {
                if (i > 0) newArray.Append(", ");
                newArray.Append($"{{\"onetSkillCode\":\"{skills[i].onetSkillCode}\",\"skillName\":\"{skills[i].skillName}\",\"category\":\"{skills[i].category}\",\"availableLevel\":{skills[i].availableLevel},\"isProficient\":{skills[i].isProficient.ToString().ToLower()}}}");
            }
        }
        newArray.Append("]");
        
        // Replace array
        sb.Remove(arrayStart, arrayEnd - arrayStart + 1);
        sb.Insert(arrayStart, newArray.ToString());
    }
    
    /// <summary>
    /// Manually trigger save (called when step completed or skill learned)
    /// </summary>
    public void SaveOnProgress()
    {
        SaveProgressToJSON();
    }
    
    [ContextMenu("Save Progress Now")]
    public void SaveProgressNow()
    {
        SaveProgressToJSON();
    }
}

