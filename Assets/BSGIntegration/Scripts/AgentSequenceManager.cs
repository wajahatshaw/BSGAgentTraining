using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Manages agent action sequences loaded from JSON
/// Handles step tracking, target finding, and sequence progression
/// </summary>
[System.Serializable]
public class GoalBufferStackLayer
{
    public string position;
    public string type;
    public string value;
    public bool isActive;
    public bool isCompleted;
    public int reward;
    public int penalty;
    public int retryAttempt;
    public float desireLevel;
    public float expectedDuration;
}

[System.Serializable]
public class GoalBufferContract
{
    public GoalBufferStackLayer[] stack;
    public string[] connections;
    /// <summary>RAG <c>initial_state.bottom</c> (text stack — no JSON schema change).</summary>
    public string initialStateBottom;
    /// <summary>RAG <c>initial_state.middle</c>.</summary>
    public string initialStateMiddle;
    /// <summary>RAG <c>initial_state.top</c>.</summary>
    public string initialStateTop;

    public bool HasStackLayers() => stack != null && stack.Length > 0;

    public bool HasInitialStateTripleContent() =>
        !string.IsNullOrWhiteSpace(initialStateBottom)
        || !string.IsNullOrWhiteSpace(initialStateMiddle)
        || !string.IsNullOrWhiteSpace(initialStateTop);

    /// <summary>Triple-ball UX: bottom/middle/top strings without legacy <c>stack</c> array.</summary>
    public bool IsTripleModeContract() => !HasStackLayers() && HasInitialStateTripleContent();
}

/// <summary>Single option entry inside a <see cref="MenuContract"/>.</summary>
[System.Serializable]
public class MenuContractOption
{
    public string id;
    public string label;
}

/// <summary>
/// Contract block for physical steps with <c>actionSubType == "menu_open"</c>.
/// Mirrors <c>GoalBufferContract</c> style: stored inside the step's JSON so all
/// sub-action metadata lives in one place alongside the goal-buffer contract.
/// </summary>
[System.Serializable]
public class MenuContract
{
    /// <summary>Available menu options (id + display label).</summary>
    public MenuContractOption[] options;
    /// <summary>Object ID of the option the agent must select to complete the step.</summary>
    public string selectedOptionId;
    /// <summary>Visual style hint for the runtime menu panel (default: "flat_panel").</summary>
    public string panelStyle;
    /// <summary>Optional: explicit button target if different from the step's <c>targetObjectId</c>.</summary>
    public string buttonTargetId;
    /// <summary>Initial sub-phase label shown in training HUD (e.g. "approach_button").</summary>
    public string initialPhase;
}

[System.Serializable]
public class ActionSequenceStep
{
    public string stepId;
    public int stepOrder;
    public string actionVerb;        // "examine", "click", "type", "verify", "navigate"
    public string actionType; // "move", "act", "learn"
    public string currentCognitiveState;
    public string preposition;
    public string targetObjectId;
    public string targetObjectName;
    /// <summary>Physical-agent step: meronym / scene object name (RAG <c>target</c>).</summary>
    public string physicalTarget;
    /// <summary>Physical-agent step: root sceneEntities[] id containing <see cref="physicalTarget"/> meronym (RAG <c>target_id</c>).</summary>
    public string physicalTargetId;
    /// <summary>Optional RAG subtype for physical/manual actions, e.g. menu_open.</summary>
    public string actionSubType;
    /// <summary>Menu group shown by this physical action, if any.</summary>
    public string menuId;
    /// <summary>Candidate menu option target object IDs for a menu action.</summary>
    public string[] menuOptions;
    /// <summary>Correct/default menu option object ID to select after the menu opens.</summary>
    public string selectedMenuOptionObjectId;
    public string description;
    public float expectedDuration;
    public float startTimeSec;
    public float endTimeSec;
    public SkillRequirement[] requiredSkills;
    public AvailableSkill[] learnSkills;
    public float correct_step_reward;
    /// <summary>JSON mental-agent payload key (e.g. intention, retrieval_schema).</summary>
    public string producesPayload;
    /// <summary>e.g. st_0 for opening_sequence cognitive substeps.</summary>
    public string subTaskId;
    public bool isActivated;
    public bool isStepCompleted;
    /// <summary>Populated for Goal Buffer cognitive steps from goalBufferContract in JSON.</summary>
    public GoalBufferContract goalBufferContract;
    /// <summary>Resolved Goal Buffer desire for this step using shared precedence rules.</summary>
    public float resolvedGoalBufferDesireLevel;
    public string resolvedGoalBufferDesireSource;
    /// <summary>Populated for physical menu steps (actionSubType == "menu_open") from menuContract in JSON.</summary>
    public MenuContract menuContract;

    // ── Three-phase execution fields ─────────────────────────────────────────
    /// <summary>Step IDs that must complete before this step is eligible to run. Execution gate for the DAG.</summary>
    public string[] dependsOn;
    /// <summary>When true this step closes the current subtask and gates the next one.</summary>
    public bool isBarrier;
    /// <summary>Payload tokens this step requires from prior steps.</summary>
    public string[] consumesPayload;
    /// <summary>Step IDs that may execute simultaneously with this one.</summary>
    public string[] canRunInParallelWith;
    /// <summary>Parallel group ID from <c>parallelSchedule.parallelGroups</c>.</summary>
    public string parallelGroupId;
    /// <summary>"M" = mental/cognitive agent, "P" = physical agent.</summary>
    public string agentRole;

    // ── Module/buffer contract connections ────────────────────────────────────
    /// <summary>Buffer names Production Memory is commanding in this step (e.g. "visual_buffer", "manual_buffer").</summary>
    public string[] productionMemoryConnections;
    /// <summary>Buffer names the Visual Module connects to.</summary>
    public string[] visualModuleConnections;
    /// <summary>Buffer names the Manual Module connects to.</summary>
    public string[] manualModuleConnections;
    /// <summary>Imaginal Buffer state before this step executes.</summary>
    public string imaginalStateBefore;
    /// <summary>Imaginal Buffer state after this step completes.</summary>
    public string imaginalStateAfter;
    /// <summary>RAG-authored thought to show while visiting the Imaginal Buffer.</summary>
    public string imaginalThoughtText;
    /// <summary>Buffer names the Imaginal Buffer connects to.</summary>
    public string[] imaginalBufferConnections;
    /// <summary>Raw JSON contracts keyed by contract object name (e.g. productionMemoryContract).</summary>
    public Dictionary<string, string> contractJsonByName;
    public string productionMemoryContractJson;
    public string visualModuleContractJson;
    public string manualModuleContractJson;
    public string imaginalBufferContractJson;
    public string intentionalModuleContractJson;
    public string retrievalBufferContractJson;
    public string declarativeModuleContractJson;

    public string GetContractJson(string contractName)
    {
        if (string.IsNullOrEmpty(contractName) || contractJsonByName == null) return "";
        return contractJsonByName.TryGetValue(contractName, out string json) ? json : "";
    }
}

[System.Serializable]
public class AgentSequenceData
{
    public string agentId;
    public List<ActionSequenceStep> actionSequence;
    public int currentStepIndex;
    
    public AgentSequenceData(string id)
    {
        agentId = id;
        actionSequence = new List<ActionSequenceStep>();
        currentStepIndex = 0;
    }
    
    public ActionSequenceStep GetCurrentStep()
    {
        if (currentStepIndex >= 0 && currentStepIndex < actionSequence.Count)
        {
            return actionSequence[currentStepIndex];
        }
        return null;
    }
    
    public bool HasNextStep()
    {
        return currentStepIndex < actionSequence.Count - 1;
    }
    
    public void MoveToNextStep()
    {
        if (HasNextStep())
        {
            currentStepIndex++;
            Debug.Log($"🔄 [{agentId}] Moved to step {currentStepIndex + 1}/{actionSequence.Count}: {GetCurrentStep()?.stepId}");
        }
        else
        {
            Debug.Log($"✅ [{agentId}] Sequence completed! All {actionSequence.Count} steps finished.");
        }
    }
    
    public void MarkStepCompleted()
    {
        if (currentStepIndex >= 0 && currentStepIndex < actionSequence.Count)
        {
            actionSequence[currentStepIndex].isActivated = true;
            actionSequence[currentStepIndex].isStepCompleted = true;
            Debug.Log($"✅ [{agentId}] Step {currentStepIndex + 1} ({actionSequence[currentStepIndex].stepId}) marked as completed");
        }
    }

    /// <summary>Attempt recorded but step failed — no reward; advance to next step.</summary>
    public void MarkStepAttemptedFailed()
    {
        if (currentStepIndex >= 0 && currentStepIndex < actionSequence.Count)
        {
            actionSequence[currentStepIndex].isActivated = true;
            actionSequence[currentStepIndex].isStepCompleted = false;
            Debug.LogWarning($"⚠️ [{agentId}] Step {currentStepIndex + 1} ({actionSequence[currentStepIndex].stepId}) marked failed (activated, not completed)");
        }
        MoveToNextStep();
    }
}

public class AgentSequenceManager : MonoBehaviour
{
    private static AgentSequenceManager _instance;
    public static AgentSequenceManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<AgentSequenceManager>();
                if (_instance == null && Application.isPlaying && !PlayModeQuitGuard.IsQuitting)
                {
                    GameObject go = new GameObject("AgentSequenceManager");
                    _instance = go.AddComponent<AgentSequenceManager>();
                    // CRITICAL: Load sequences immediately when instance is created
                    _instance.EnsureSequencesLoaded();
                }
            }
            else if (_instance.agentSequences.Count == 0)
            {
                // Sequences not loaded yet - load them now
                _instance.EnsureSequencesLoaded();
            }
            return _instance;
        }
    }
    
    private Dictionary<string, AgentSequenceData> agentSequences = new Dictionary<string, AgentSequenceData>();
    private Dictionary<string, AgentSequenceData> cognitiveAgentSequences = new Dictionary<string, AgentSequenceData>();
    private Dictionary<string, AgentSequenceData> physicalAgentSequences = new Dictionary<string, AgentSequenceData>();
    private Dictionary<string, Vector3> toolPositions = new Dictionary<string, Vector3>();
    private SceneUILoader sceneLoader;
    
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

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }
    
    void Start()
    {
        EnsureSequencesLoaded();
    }

    /// <summary>Called by SceneUILoader after it successfully populates EffectivePipelineJson.</summary>
    public void ReloadFromLoader(SceneUILoader loader)
    {
        sceneLoader = loader;
        agentSequences.Clear();
        cognitiveAgentSequences.Clear();
        physicalAgentSequences.Clear();
        LoadSequencesFromJSON();
        LoadToolPositions();
    }
    
    /// <summary>
    /// Ensure sequences are loaded (called from Start or when first accessed)
    /// </summary>
    void EnsureSequencesLoaded()
    {
        if (agentSequences.Count > 0) return; // Already loaded
        
        sceneLoader = FindObjectOfType<SceneUILoader>();
        LoadSequencesFromJSON();
        LoadToolPositions();
    }
    
    /// <summary>
    /// Load agent sequences from JSON file
    /// </summary>
    void LoadSequencesFromJSON()
    {
        try
        {
            if (sceneLoader == null)
                sceneLoader = FindObjectOfType<SceneUILoader>();

            string json;
            if (sceneLoader != null && !string.IsNullOrEmpty(sceneLoader.EffectivePipelineJson))
            {
                json = sceneLoader.EffectivePipelineJson;
                Debug.Log($"📄 AgentSequenceManager: Using SceneUILoader.EffectivePipelineJson ({json.Length} chars)");
            }
            else
            {
                string configuredJsonFile = ResolveRuntimeJsonFileName();
                string jsonPath = System.IO.Path.Combine(Application.dataPath, "JsonFile", configuredJsonFile);
                Debug.Log($"🔍 AgentSequenceManager: Trying JSON path: {jsonPath}");

                if (!System.IO.File.Exists(jsonPath))
                {
                    jsonPath = System.IO.Path.Combine(Application.streamingAssetsPath, configuredJsonFile);
                    Debug.Log($"🔍 AgentSequenceManager: Fallback JSON path: {jsonPath}");
                }

                if (!System.IO.File.Exists(jsonPath))
                {
                    Debug.LogError($"❌ AgentSequenceManager: JSON file not found at {jsonPath}");
                    return;
                }

                Debug.Log($"✅ AgentSequenceManager: Found JSON file at {jsonPath}");
                json = System.IO.File.ReadAllText(jsonPath);
                Debug.Log($"📄 AgentSequenceManager: JSON file size: {json.Length} characters");
            }
            
            // Parse using SimpleJSON or manual parsing
            ParseAgentSequences(json);
            ParsePhysicalAgentStepsManual(json);
            
            Debug.Log($"✅ AgentSequenceManager: Loaded {agentSequences.Count} agent sequences");
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ AgentSequenceManager: Error loading sequences: {e.Message}");
            Debug.LogError($"❌ AgentSequenceManager: Stack trace: {e.StackTrace}");
        }
    }

    public string GetRuntimeJsonFileName()
    {
        return ResolveRuntimeJsonFileName();
    }

    private string ResolveRuntimeJsonFileName()
    {
        if (sceneLoader == null)
        {
            sceneLoader = FindObjectOfType<SceneUILoader>();
        }

        if (sceneLoader != null && !string.IsNullOrWhiteSpace(sceneLoader.jsonFileName))
        {
            return sceneLoader.jsonFileName;
        }

        ReplicaSceneSetup replicaSetup = FindObjectOfType<ReplicaSceneSetup>();
        if (replicaSetup != null && !string.IsNullOrWhiteSpace(replicaSetup.jsonFileName))
        {
            return replicaSetup.jsonFileName;
        }

        return "basicUI_ml2.json";
    }
    
    void ParseAgentSequences(string json)
    {
        Debug.Log("🔍 AgentSequenceManager: Starting to parse agent sequences...");
        
        try
        {
            // Extract agentProfiles section manually
            int startIdx = json.IndexOf("\"agentProfiles\":");
            if (startIdx == -1) 
            {
                Debug.LogError("❌ AgentSequenceManager: Could not find 'agentProfiles' in JSON");
                return;
            }
            
            Debug.Log($"✅ AgentSequenceManager: Found 'agentProfiles' at index {startIdx}");

            List<string> agentIds = SceneUILoader.ParseAgentProfilesStatic(json).Keys.ToList();
            if (agentIds.Count == 0)
            {
                // Legacy fallback set.
                agentIds.AddRange(new[]
                {
                    "SIMPLE_Technician_01",
                    "SIMPLE_Technician_02",
                    "SIMPLE_Supervisor_01",
                    "SIMPLE_Supervisor_02"
                });
            }

            bool skipReplicaStepVerify = sceneLoader != null && sceneLoader.LoadedFromRagSidecarMerge;
            foreach (string id in agentIds)
            {
                ParseAgentSequenceManual(id, json);

                if (skipReplicaStepVerify)
                    continue;

                if (string.Equals(id, "SIMPLE_Technician_01", StringComparison.Ordinal))
                    VerifySequenceCorrectness(id, "tech01", "tool_001");
                else if (string.Equals(id, "SIMPLE_Technician_02", StringComparison.Ordinal))
                    VerifySequenceCorrectness(id, "tech02", "tool_002");
                else if (string.Equals(id, "SIMPLE_Supervisor_01", StringComparison.Ordinal))
                    VerifySequenceCorrectness(id, "sup01", "tool_003");
                else if (string.Equals(id, "SIMPLE_Supervisor_02", StringComparison.Ordinal))
                    VerifySequenceCorrectness(id, "sup02", "tool_004");
            }
            
            Debug.Log($"✅ AgentSequenceManager: Finished parsing. Total sequences loaded: {agentSequences.Count}");
            
            // Verify unique sequences for each agent
            VerifyUniqueSequences();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ AgentSequenceManager: Exception during JSON parsing: {e.Message}");
            Debug.LogError($"❌ Stack trace: {e.StackTrace}");
        }
    }
    
    /// <summary>
    /// Verify that a parsed sequence is correct for the agent
    /// </summary>
    void VerifySequenceCorrectness(string agentId, string expectedStepIdPattern, string expectedFirstTarget)
    {
        if (!agentSequences.ContainsKey(agentId))
        {
            Debug.LogError($"❌ AgentSequenceManager: Sequence for {agentId} was not loaded!");
            return;
        }
        
        var sequence = agentSequences[agentId];
        if (sequence.actionSequence == null || sequence.actionSequence.Count == 0)
        {
            Debug.LogError($"❌ AgentSequenceManager: Sequence for {agentId} has no steps!");
            return;
        }
        
        var firstStep = sequence.actionSequence[0];
        bool stepIdCorrect = firstStep.stepId != null && firstStep.stepId.Contains(expectedStepIdPattern);
        bool targetCorrect = firstStep.targetObjectId == expectedFirstTarget;
        
        if (!stepIdCorrect || !targetCorrect)
        {
            Debug.LogError($"❌ AgentSequenceManager: WRONG SEQUENCE for {agentId}!");
            Debug.LogError($"   Expected stepId pattern: {expectedStepIdPattern}, Got: {firstStep.stepId} (correct: {stepIdCorrect})");
            Debug.LogError($"   Expected first target: {expectedFirstTarget}, Got: {firstStep.targetObjectId} (correct: {targetCorrect})");
        }
        else
        {
            Debug.Log($"✅ AgentSequenceManager: Verified correct sequence for {agentId} - stepId: {firstStep.stepId}, target: {firstStep.targetObjectId}");
        }
    }
    
    /// <summary>
    /// Verify that each agent has a unique sequence with different first targets
    /// </summary>
    void VerifyUniqueSequences()
    {
        Debug.Log("🔍 AgentSequenceManager: Verifying unique sequences for each agent...");
        
        foreach (var kvp in agentSequences)
        {
            string agentId = kvp.Key;
            AgentSequenceData sequence = kvp.Value;
            
            if (sequence.actionSequence.Count > 0)
            {
                var firstStep = sequence.actionSequence[0];
                Debug.Log($"✅ {agentId}: {sequence.actionSequence.Count} steps, First target: {firstStep.targetObjectId} ({firstStep.targetObjectName})");
            }
            else
            {
                if (cognitiveAgentSequences.TryGetValue(agentId, out AgentSequenceData cogSequence) &&
                    cogSequence != null &&
                    cogSequence.actionSequence != null &&
                    cogSequence.actionSequence.Count > 0)
                {
                    Debug.Log($"ℹ️ {agentId}: No physical actionSequence; cognitiveActionSequence has {cogSequence.actionSequence.Count} steps.");
                }
                else
                {
                    Debug.LogError($"❌ {agentId}: No steps in sequence!");
                }
            }
        }
        
        // Check for duplicate first targets (should not happen)
        var firstTargets = new Dictionary<string, string>();
        foreach (var kvp in agentSequences)
        {
            if (kvp.Value.actionSequence.Count > 0)
            {
                string firstTarget = kvp.Value.actionSequence[0].targetObjectId;
                if (firstTargets.ContainsKey(firstTarget))
                {
                    Debug.LogWarning($"⚠️ DUPLICATE FIRST TARGET: {kvp.Key} and {firstTargets[firstTarget]} both target {firstTarget}");
                }
                else
                {
                    firstTargets[firstTarget] = kvp.Key;
                }
            }
        }
        
        Debug.Log($"✅ AgentSequenceManager: Sequence verification complete. {firstTargets.Count} unique first targets found.");
    }
    
    void ParseAgentSequenceManual(string agentId, string json)
    {
        Debug.Log($"🔍 AgentSequenceManager: Parsing sequence for {agentId} using manual parsing...");
        
        // CRITICAL FIX: Find the agent's section more specifically
        // Look for agent ID followed by colon and opening brace (indicates start of agent object)
        // This avoids matching agent IDs in arrays like "assignedTeam"
        string agentSearchKey = $"\"{agentId}\"";
        int agentStart = -1;
        int searchStart = 0;
        
        // Search for agent ID that's followed by a colon (indicating it's a key, not a value in an array)
        while (true)
        {
            int foundIndex = json.IndexOf(agentSearchKey, searchStart);
            if (foundIndex == -1)
                break;
            
            // Check if this is followed by a colon (key) or comma/quote (value in array)
            int afterKey = foundIndex + agentSearchKey.Length;
            if (afterKey < json.Length)
            {
                // Skip whitespace
                while (afterKey < json.Length && char.IsWhiteSpace(json[afterKey]))
                    afterKey++;
                
                // If followed by colon, this is the agent profile key
                if (afterKey < json.Length && json[afterKey] == ':')
                {
                    agentStart = foundIndex;
                    break;
                }
            }
            
            searchStart = foundIndex + 1;
        }
        
        if (agentStart == -1)
        {
            Debug.LogError($"❌ AgentSequenceManager: Could not find agent '{agentId}' in JSON (searched for key pattern)");
            return;
        }
        
        Debug.Log($"✅ AgentSequenceManager: Found agent '{agentId}' at index {agentStart} (verified as key, not array value)");
        
        // Robust section bound: derive this exact agent object range.
        int agentSectionEnd = json.Length;
        int keyEndQuote = json.IndexOf('"', agentStart + 1);
        int colonAfterKey = keyEndQuote >= 0 ? json.IndexOf(':', keyEndQuote + 1) : -1;
        int objectStart = colonAfterKey >= 0 ? json.IndexOf('{', colonAfterKey + 1) : -1;
        if (objectStart >= 0)
        {
            int objectEnd = FindMatchingBrace(json, objectStart);
            if (objectEnd >= 0)
            {
                agentSectionEnd = objectEnd + 1;
            }
        }
        
        Debug.Log($"✅ AgentSequenceManager: Agent '{agentId}' section boundaries: {agentStart} to {agentSectionEnd} (length: {agentSectionEnd - agentStart})");
        
        // CRITICAL: Determine expected stepId pattern for this agent
        string expectedStepIdPattern = "";
        string expectedFirstTarget = "";
        if (agentId == "SIMPLE_Technician_01")
        {
            expectedStepIdPattern = "tech01";
            expectedFirstTarget = "tool_001";
        }
        else if (agentId == "SIMPLE_Technician_02")
        {
            expectedStepIdPattern = "tech02";
            expectedFirstTarget = "tool_002";
        }
        else if (agentId == "SIMPLE_Supervisor_01")
        {
            expectedStepIdPattern = "sup01";
            expectedFirstTarget = "tool_003";
        }
        else if (agentId == "SIMPLE_Supervisor_02")
        {
            expectedStepIdPattern = "sup02";
            expectedFirstTarget = "tool_004";
        }
        
        // STRATEGY: For legacy SIMPLE agents, search for expected stepId pattern first.
        // For dynamic RAG agents (e.g. M1/P1), directly try actionSequence field.
        bool hasLegacyPattern = !string.IsNullOrEmpty(expectedStepIdPattern);
        string stepIdSearchPattern = hasLegacyPattern ? $"\"stepId\":\"{expectedStepIdPattern}_step_001\"" : "";
        int stepIdIndex = hasLegacyPattern ? json.IndexOf(stepIdSearchPattern, agentStart) : -1;
        
        string arrayJson = "";
        
        if (stepIdIndex != -1 && stepIdIndex < agentSectionEnd)
        {
            // Found the correct stepId - work backwards to find the array start
            Debug.Log($"✅ AgentSequenceManager: Found correct stepId pattern for {agentId} at index {stepIdIndex}");
            
            // Find the array start before this stepId (should be the opening bracket of actionSequence array)
            int arrayStart = json.LastIndexOf("[", stepIdIndex);
            if (arrayStart != -1 && arrayStart >= agentStart)
            {
                // Verify this is the actionSequence array (should have "actionSequence" before the bracket)
                int actionSeqCheck = json.LastIndexOf("\"actionSequence\":", arrayStart);
                if (actionSeqCheck != -1 && actionSeqCheck >= agentStart)
                {
                    int arrayEnd = FindMatchingBracket(json, arrayStart);
                    if (arrayEnd != -1 && arrayEnd < agentSectionEnd)
                    {
                        arrayJson = json.Substring(arrayStart, arrayEnd - arrayStart + 1);
                        Debug.Log($"✅ AgentSequenceManager: Extracted actionSequence array for '{agentId}' using stepId pattern ({arrayJson.Length} chars)");
                    }
                }
            }
        }
        
        // Fallback: If stepId search failed (or not applicable), use direct actionSequence search.
        if (string.IsNullOrEmpty(arrayJson))
        {
            if (hasLegacyPattern)
                Debug.LogWarning($"⚠️ AgentSequenceManager: Could not find stepId pattern for {agentId}, using fallback method");
            
            // Find the actionSequence array for this agent (within this agent's section only)
            int actionSeqStart = json.IndexOf("\"actionSequence\":", agentStart);
            if (actionSeqStart == -1 || actionSeqStart >= agentSectionEnd)
            {
                Debug.Log($"ℹ️ AgentSequenceManager: No actionSequence found for '{agentId}' (cognitive-only agent is allowed).");
                agentSequences[agentId] = new AgentSequenceData(agentId);
                ParseCognitiveSequenceManual(agentId, json, agentStart, agentSectionEnd);
                return;
            }
            
            Debug.Log($"✅ AgentSequenceManager: Found 'actionSequence' for '{agentId}' at index {actionSeqStart} (within section boundaries)");
            
            // Find the start of the array
            int arrayStart = json.IndexOf("[", actionSeqStart);
            if (arrayStart == -1 || arrayStart >= agentSectionEnd)
            {
                Debug.Log($"ℹ️ AgentSequenceManager: actionSequence is present but empty for '{agentId}' (cognitive-only agent is allowed).");
                agentSequences[agentId] = new AgentSequenceData(agentId);
                ParseCognitiveSequenceManual(agentId, json, agentStart, agentSectionEnd);
                return;
            }
            
            // Find the end of the array by counting brackets (but limit to agent section)
            int arrayEnd = FindMatchingBracket(json, arrayStart);
            if (arrayEnd == -1 || arrayEnd >= agentSectionEnd)
            {
                Debug.Log($"ℹ️ AgentSequenceManager: actionSequence parsing not possible for '{agentId}', falling back to cognitive sequence only.");
                agentSequences[agentId] = new AgentSequenceData(agentId);
                ParseCognitiveSequenceManual(agentId, json, agentStart, agentSectionEnd);
                return;
            }
            
            arrayJson = json.Substring(arrayStart, arrayEnd - arrayStart + 1);
            Debug.Log($"✅ AgentSequenceManager: Extracted actionSequence array for '{agentId}' using fallback method ({arrayJson.Length} chars)");
        }
        
        // CRITICAL: Verify the extracted array contains the expected stepId pattern
        if (hasLegacyPattern && !arrayJson.Contains($"\"stepId\":\"{expectedStepIdPattern}"))
        {
            // Try to find what stepId is actually in this array
            int stepIdStart = arrayJson.IndexOf("\"stepId\":\"");
            if (stepIdStart != -1)
            {
                int stepIdValueStart = stepIdStart + 10; // Length of "stepId":"
                int stepIdValueEnd = arrayJson.IndexOf("\"", stepIdValueStart);
                if (stepIdValueEnd != -1)
                {
                    string actualStepId = arrayJson.Substring(stepIdValueStart, stepIdValueEnd - stepIdValueStart);
                    Debug.LogError($"❌ AgentSequenceManager: WRONG ARRAY EXTRACTED for {agentId}!");
                    Debug.LogError($"   Expected stepId pattern: {expectedStepIdPattern}, Found stepId: {actualStepId}");
                    Debug.LogError($"   This means we extracted the wrong agent's actionSequence array!");
                    return; // Don't parse wrong data
                }
            }
        }
        
        // CRITICAL: Verify the first step's stepId matches expected pattern
        string firstStepPreview = arrayJson.Substring(0, Math.Min(500, arrayJson.Length));
        Debug.Log($"✅ AgentSequenceManager: Final array for '{agentId}' ({arrayJson.Length} chars)");
        Debug.Log($"   First 500 chars: {firstStepPreview}");
        
        AgentSequenceData sequenceData = new AgentSequenceData(agentId);
        
        // Parse each step object in the array
        ParseStepsFromArrayImproved(sequenceData, arrayJson);
        
        // CRITICAL: Verify first step matches expected agent
        // Note: expectedStepIdPattern was already declared earlier in this function
        if (sequenceData.actionSequence != null && sequenceData.actionSequence.Count > 0)
        {
            var firstStep = sequenceData.actionSequence[0];
            
            if (!string.IsNullOrEmpty(expectedStepIdPattern) && firstStep.stepId != null)
            {
                if (!firstStep.stepId.Contains(expectedStepIdPattern))
                {
                    Debug.LogError($"❌ AgentSequenceManager: WRONG SEQUENCE! Agent {agentId} has stepId {firstStep.stepId}, expected pattern '{expectedStepIdPattern}'");
                    Debug.LogError($"   First step target: {firstStep.targetObjectId}");
                }
                else
                {
                    Debug.Log($"✅ AgentSequenceManager: Verified correct sequence for {agentId} - first step: {firstStep.stepId}, target: {firstStep.targetObjectId}");
                }
            }
        }
        
        agentSequences[agentId] = sequenceData;
        Debug.Log($"✅ AgentSequenceManager: Successfully loaded {sequenceData.actionSequence.Count} steps for {agentId}");
        
        // Log the first step to verify unique sequences
        if (sequenceData.actionSequence.Count > 0)
        {
            var firstStep = sequenceData.actionSequence[0];
            Debug.Log($"🎯 AgentSequenceManager: {agentId} first target: {firstStep.targetObjectId} ({firstStep.targetObjectName})");
        }

        ParseCognitiveSequenceManual(agentId, json, agentStart, agentSectionEnd);
    }

    void ParseCognitiveSequenceManual(string agentId, string json, int agentStart, int agentSectionEnd)
    {
        int cognitiveSeqStart = json.IndexOf("\"cognitiveActionSequence\":", agentStart);
        if (cognitiveSeqStart == -1 || cognitiveSeqStart >= agentSectionEnd)
        {
            return;
        }

        int arrayStart = json.IndexOf("[", cognitiveSeqStart);
        if (arrayStart == -1 || arrayStart >= agentSectionEnd)
        {
            return;
        }

        int arrayEnd = FindMatchingBracket(json, arrayStart);
        if (arrayEnd == -1 || arrayEnd >= agentSectionEnd)
        {
            return;
        }

        string arrayJson = json.Substring(arrayStart, arrayEnd - arrayStart + 1);
        AgentSequenceData cognitiveData = new AgentSequenceData(agentId);
        ParseStepsFromArrayImproved(cognitiveData, arrayJson);

        if (cognitiveData.actionSequence.Count > 0)
        {
            cognitiveAgentSequences[agentId] = cognitiveData;
            Debug.Log($"🧠 AgentSequenceManager: Loaded cognitiveActionSequence for {agentId}: {cognitiveData.actionSequence.Count} steps");

            // RAG can define mental agents as cognitive-only. Mirror those steps into runtime
            // actionSequence when empty so existing movement/progression flows can advance.
            if (agentSequences.TryGetValue(agentId, out AgentSequenceData runtimeSequence) &&
                (runtimeSequence.actionSequence == null || runtimeSequence.actionSequence.Count == 0))
            {
                if (agentId.StartsWith("M", StringComparison.OrdinalIgnoreCase) ||
                    agentId.IndexOf("mental", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    runtimeSequence.actionSequence = new List<ActionSequenceStep>(cognitiveData.actionSequence);
                    Debug.Log($"🧠 AgentSequenceManager: Mirrored cognitiveActionSequence into runtime actionSequence for {agentId}.");
                }
            }
        }
    }

    /// <summary>
    /// Parses the <c>physicalAgents[].steps</c> section of the JSON (distinct from agentProfiles actionSequence).
    /// Stores results in <see cref="physicalAgentSequences"/> keyed by agentId (e.g. "P1").
    /// These steps carry full dependency data: <c>dependsOn</c>, <c>isBarrier</c>, <c>canRunInParallelWith</c>.
    /// </summary>
    void ParsePhysicalAgentStepsManual(string json)
    {
        try
        {
            int physStart = json.IndexOf("\"physicalAgents\":");
            if (physStart >= 0)
            {
                ParsePhysicalAgentsArray(json, physStart);
                return;
            }

            int legacyStart = IndexOfSingularPhysicalAgentKey(json);
            if (legacyStart >= 0)
            {
                ParseSingularPhysicalAgent(json, legacyStart);
                return;
            }

            Debug.Log("ℹ️ AgentSequenceManager: No physicalAgents / physicalAgent section found in JSON.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ AgentSequenceManager: Error parsing physicalAgents steps: {e.Message}");
        }
    }

    static int IndexOfSingularPhysicalAgentKey(string json)
    {
        const string tok = "\"physicalAgent\"";
        int pos = 0;
        while (pos < json.Length)
        {
            int idx = json.IndexOf(tok, pos, StringComparison.Ordinal);
            if (idx < 0) return -1;
            int after = idx + tok.Length;
            while (after < json.Length && char.IsWhiteSpace(json[after])) after++;
            if (after < json.Length && json[after] == ':')
                return idx;
            pos = idx + tok.Length;
        }
        return -1;
    }

    void ParseSingularPhysicalAgent(string json, int keyIdx)
    {
        int objStart = json.IndexOf("{", keyIdx);
        if (objStart < 0) return;

        int objEnd = FindMatchingBrace(json, objStart);
        if (objEnd < 0) return;

        string agentJson = json.Substring(objStart, objEnd - objStart + 1);
        string agentId = ExtractStringValue(agentJson, "agentId");
        if (string.IsNullOrEmpty(agentId)) agentId = "P1";
        ParsePhysicalAgentStepsObject(agentJson, agentId);
    }

    void ParsePhysicalAgentsArray(string json, int physStart)
    {
            int arrayStart = json.IndexOf("[", physStart);
            if (arrayStart == -1) return;

            int arrayEnd = FindMatchingBracket(json, arrayStart);
            if (arrayEnd == -1) return;

            string physArrayJson = json.Substring(arrayStart, arrayEnd - arrayStart + 1);
            int pos = 1;
            while (pos < physArrayJson.Length - 1)
            {
                while (pos < physArrayJson.Length && (physArrayJson[pos] == ' ' || physArrayJson[pos] == '\n' || physArrayJson[pos] == '\r' || physArrayJson[pos] == '\t' || physArrayJson[pos] == ',')) pos++;
                if (pos >= physArrayJson.Length - 1) break;

                int agentStart = physArrayJson.IndexOf("{", pos);
                if (agentStart == -1) break;

                int agentEnd = FindMatchingBrace(physArrayJson, agentStart);
                if (agentEnd == -1) break;

                string agentJson = physArrayJson.Substring(agentStart, agentEnd - agentStart + 1);
                string agentId = ExtractStringValue(agentJson, "agentId");
                if (string.IsNullOrEmpty(agentId)) agentId = "PhysicalAgent";
                ParsePhysicalAgentStepsObject(agentJson, agentId);
                pos = agentEnd + 1;
            }
    }

    void ParsePhysicalAgentStepsObject(string agentJson, string agentId)
    {
                int stepsStart = agentJson.IndexOf("\"steps\":");
                if (stepsStart == -1) return;

                int stepsArrStart = agentJson.IndexOf("[", stepsStart);
                if (stepsArrStart == -1) return;

                int stepsArrEnd = FindMatchingBracket(agentJson, stepsArrStart);
                if (stepsArrEnd == -1) return;

                string stepsArrayJson = agentJson.Substring(stepsArrStart, stepsArrEnd - stepsArrStart + 1);
                AgentSequenceData physData = new AgentSequenceData(agentId);
                ParseStepsFromArrayImproved(physData, stepsArrayJson);

                if (physData.actionSequence.Count > 0)
                {
                    foreach (var step in physData.actionSequence)
                        if (string.IsNullOrEmpty(step.agentRole)) step.agentRole = "P";

                    physicalAgentSequences[agentId] = physData;
                    Debug.Log($"⚙️ AgentSequenceManager: Loaded physical agent steps for {agentId}: {physData.actionSequence.Count} steps");
                }
    }
    
    /// <summary>
    /// Find matching bracket for array parsing
    /// </summary>
    int FindMatchingBracket(string json, int start)
    {
        int bracketCount = 0;
        bool inString = false;
        
        for (int i = start; i < json.Length; i++)
        {
            char c = json[i];
            if (c == '"' && (i == 0 || json[i-1] != '\\')) inString = !inString;
            if (!inString)
            {
                if (c == '[') bracketCount++;
                if (c == ']') 
                {
                    bracketCount--;
                    if (bracketCount == 0) return i;
                }
            }
        }
        return -1;
    }
    
    /// <summary>
    /// Improved step parsing from array JSON
    /// </summary>
    void ParseStepsFromArrayImproved(AgentSequenceData sequenceData, string arrayJson)
    {
        Debug.Log($"🔍 AgentSequenceManager: Parsing steps from array for {sequenceData.agentId}...");
        
        int stepIndex = 0;
        int currentPos = 1; // Skip the opening '['
        
        while (currentPos < arrayJson.Length - 1) // Stop before closing ']'
        {
            // Skip whitespace and commas
            while (currentPos < arrayJson.Length && (arrayJson[currentPos] == ' ' || arrayJson[currentPos] == '\n' || arrayJson[currentPos] == '\r' || arrayJson[currentPos] == '\t' || arrayJson[currentPos] == ','))
            {
                currentPos++;
            }
            
            if (currentPos >= arrayJson.Length - 1) break;
            
            // Find step object
            int stepStart = arrayJson.IndexOf("{", currentPos);
            if (stepStart == -1) break;
            
            int stepEnd = FindMatchingBrace(arrayJson, stepStart);
            if (stepEnd == -1) 
            {
                Debug.LogError($"❌ AgentSequenceManager: Could not find matching brace for step {stepIndex} in {sequenceData.agentId}");
                break;
            }
            
            string stepJson = arrayJson.Substring(stepStart, stepEnd - stepStart + 1);
            
            // CRITICAL: Log first step JSON to verify we're parsing the right data
            if (stepIndex == 0)
            {
                string stepPreview = stepJson.Length > 300 ? stepJson.Substring(0, 300) + "..." : stepJson;
                Debug.Log($"🔍 AgentSequenceManager: Parsing FIRST step {stepIndex} for {sequenceData.agentId} ({stepJson.Length} chars)");
                Debug.Log($"   Step JSON preview: {stepPreview}");
            }
            else
            {
                Debug.Log($"🔍 AgentSequenceManager: Parsing step {stepIndex} for {sequenceData.agentId} ({stepJson.Length} chars)");
            }
            
            ActionSequenceStep step = ParseStepManual(stepJson);
            
            if (step != null)
            {
                sequenceData.actionSequence.Add(step);
                // CRITICAL: Log first step details to verify correct parsing
                if (stepIndex == 0)
                {
                    Debug.Log($"🎯 AgentSequenceManager: FIRST STEP for {sequenceData.agentId}: stepId={step.stepId}, targetObjectId={step.targetObjectId}, targetObjectName={step.targetObjectName ?? "null"}");
                }
                Debug.Log($"✅ AgentSequenceManager: Added step {stepIndex} ({step.stepId}) -> {step.targetObjectId} for {sequenceData.agentId}");
            }
            else
            {
                Debug.LogError($"❌ AgentSequenceManager: Failed to parse step {stepIndex} for {sequenceData.agentId}");
            }
            
            currentPos = stepEnd + 1;
            stepIndex++;
        }
        
        Debug.Log($"✅ AgentSequenceManager: Finished parsing steps for {sequenceData.agentId}. Total steps: {sequenceData.actionSequence.Count}");
    }
    
    /// <summary>
    /// Parse individual step from JSON string
    /// </summary>
    ActionSequenceStep ParseStepManual(string stepJson)
    {
        ActionSequenceStep step = new ActionSequenceStep();
        
        try
        {
            // Extract basic properties
            step.stepId = ExtractStringValue(stepJson, "stepId");
            step.stepOrder = ExtractIntValue(stepJson, "stepOrder");
            step.actionType = ExtractStringValue(stepJson, "actionType");
            step.actionVerb = ExtractStringValue(stepJson, "actionVerb");
            step.currentCognitiveState = ExtractStringValue(stepJson, "currentCognitiveState");
            step.preposition = ExtractStringValue(stepJson, "preposition");
            step.targetObjectId = ExtractStringValue(stepJson, "targetObjectId");
            step.targetObjectName = ExtractStringValue(stepJson, "targetObjectName");
            step.physicalTarget = ExtractStringValue(stepJson, "target");
            step.physicalTargetId = ExtractStringValue(stepJson, "target_id");
            step.actionSubType = ExtractStringValue(stepJson, "actionSubType");
            step.menuId = ExtractStringValue(stepJson, "menuId");
            step.menuOptions = ExtractStringArray(stepJson, "menuOptions");
            step.selectedMenuOptionObjectId = ExtractStringValue(stepJson, "selectedMenuOptionObjectId");
            step.description = ExtractStringValue(stepJson, "description");
            step.expectedDuration = ExtractFloatValue(stepJson, "expectedDuration");
            step.startTimeSec     = ExtractFloatValue(stepJson, "startTimeSec");
            step.endTimeSec       = ExtractFloatValue(stepJson, "endTimeSec");
            step.correct_step_reward = ExtractFloatValue(stepJson, "correct_step_reward");
            step.producesPayload = ExtractStringValue(stepJson, "producesPayload");
            step.subTaskId = ExtractStringValue(stepJson, "subTaskId");
            step.isActivated = ExtractBoolValue(stepJson, "isActivated");
            step.isStepCompleted = ExtractBoolValue(stepJson, "isStepCompleted");
            step.agentRole = ExtractStringValue(stepJson, "agentRole");

            // Three-phase DAG fields
            step.isBarrier = ExtractBoolValue(stepJson, "isBarrier");
            step.parallelGroupId = ExtractStringValue(stepJson, "parallelGroupId");
            step.dependsOn = ExtractStringArray(stepJson, "dependsOn");
            step.consumesPayload = ExtractStringArray(stepJson, "consumesPayload");
            step.canRunInParallelWith = ExtractStringArray(stepJson, "canRunInParallelWith");

            // Module/buffer contract connections
            step.productionMemoryConnections = ExtractContractConnections(stepJson, "productionMemoryContract");
            step.visualModuleConnections     = ExtractContractConnections(stepJson, "visualModuleContract");
            step.manualModuleConnections     = ExtractContractConnections(stepJson, "manualModuleContract");
            step.imaginalBufferConnections   = ExtractContractConnections(stepJson, "imaginalBufferContract");
            ExtractImaginalBufferStates(stepJson, out step.imaginalStateBefore, out step.imaginalStateAfter);
            step.imaginalThoughtText = ExtractImaginalThoughtText(stepJson);
            ParseRawContracts(stepJson, step);

            // Use temporary Lists during parsing
            List<SkillRequirement> requiredSkillsList = new List<SkillRequirement>();
            
            // Parse required skills if present
            ParseRequiredSkillsToList(stepJson, requiredSkillsList);
            
            // Convert Lists to arrays for assignment
            step.requiredSkills = requiredSkillsList.ToArray();
            step.learnSkills = new AvailableSkill[0]; // Empty array for now, will be populated during learning
            
            ParseGoalBufferContract(stepJson, step);
            ParseMenuContract(stepJson, step);

            return step;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Error parsing step: {e.Message}");
            return null;
        }
    }

    /// <summary>Optional; only Goal Buffer cognitive steps in JSON include goalBufferContract — all other steps leave goalBufferContract null.</summary>
    void ParseGoalBufferContract(string stepJson, ActionSequenceStep step)
    {
        if (GoalBufferContractParser.TryParse(stepJson, out GoalBufferContract c))
        {
            step.goalBufferContract = c;
            GoalBufferDesireUtility.TryResolveDesire(c, out step.resolvedGoalBufferDesireLevel, out step.resolvedGoalBufferDesireSource);
        }
    }

    /// <summary>Optional; physical menu steps include menuContract — other steps leave it null.</summary>
    void ParseMenuContract(string stepJson, ActionSequenceStep step)
    {
        int keyIdx = stepJson.IndexOf("\"menuContract\":", StringComparison.Ordinal);
        if (keyIdx == -1) return;

        int objStart = stepJson.IndexOf('{', keyIdx);
        if (objStart == -1) return;
        int objEnd = FindMatchingBrace(stepJson, objStart);
        if (objEnd == -1) return;

        string contractJson = stepJson.Substring(objStart, objEnd - objStart + 1);

        var contract = new MenuContract
        {
            selectedOptionId = ExtractStringValue(contractJson, "selectedOptionId"),
            panelStyle       = ExtractStringValue(contractJson, "panelStyle"),
            buttonTargetId   = ExtractStringValue(contractJson, "buttonTargetId"),
            initialPhase     = ExtractStringValue(contractJson, "initialPhase"),
        };

        // Parse options array
        int optKey = contractJson.IndexOf("\"options\":", StringComparison.Ordinal);
        if (optKey >= 0)
        {
            int arrStart = contractJson.IndexOf('[', optKey);
            if (arrStart >= 0)
            {
                int arrEnd = FindMatchingBracket(contractJson, arrStart);
                if (arrEnd > arrStart)
                {
                    string arrJson = contractJson.Substring(arrStart, arrEnd - arrStart + 1);
                    var opts = new List<MenuContractOption>();
                    int pos = 1;
                    while (pos < arrJson.Length - 1)
                    {
                        while (pos < arrJson.Length && (arrJson[pos] == ' ' || arrJson[pos] == '\n' || arrJson[pos] == '\r' || arrJson[pos] == '\t' || arrJson[pos] == ',')) pos++;
                        if (pos >= arrJson.Length - 1) break;
                        int itemStart = arrJson.IndexOf('{', pos);
                        if (itemStart == -1) break;
                        int itemEnd = FindMatchingBrace(arrJson, itemStart);
                        if (itemEnd == -1) break;
                        string itemJson = arrJson.Substring(itemStart, itemEnd - itemStart + 1);
                        opts.Add(new MenuContractOption
                        {
                            id    = ExtractStringValue(itemJson, "id"),
                            label = ExtractStringValue(itemJson, "label"),
                        });
                        pos = itemEnd + 1;
                    }
                    contract.options = opts.ToArray();
                }
            }
        }

        step.menuContract = contract;

        if ((step.menuOptions == null || step.menuOptions.Length == 0)
            && contract.options != null && contract.options.Length > 0)
        {
            var optionIds = new List<string>();
            foreach (var option in contract.options)
                if (option != null && !string.IsNullOrWhiteSpace(option.id))
                    optionIds.Add(option.id);
            step.menuOptions = optionIds.ToArray();
        }

        if (string.IsNullOrWhiteSpace(step.selectedMenuOptionObjectId)
            && !string.IsNullOrWhiteSpace(contract.selectedOptionId))
            step.selectedMenuOptionObjectId = contract.selectedOptionId;
    }
    
    /// <summary>
    /// Extract string value from JSON
    /// </summary>
    string ExtractStringValue(string json, string key)
    {
        string searchKey = $"\"{key}\":";
        int startIdx = json.IndexOf(searchKey);
        if (startIdx == -1) return "";
        
        startIdx += searchKey.Length;
        
        // Skip whitespace
        while (startIdx < json.Length && char.IsWhiteSpace(json[startIdx])) startIdx++;
        
        if (startIdx >= json.Length || json[startIdx] != '"') return "";
        
        startIdx++; // Skip opening quote
        int endIdx = json.IndexOf("\"", startIdx);
        if (endIdx == -1) return "";
        
        return json.Substring(startIdx, endIdx - startIdx);
    }
    
    /// <summary>
    /// Extract integer value from JSON
    /// </summary>
    int ExtractIntValue(string json, string key)
    {
        string searchKey = $"\"{key}\":";
        int startIdx = json.IndexOf(searchKey);
        if (startIdx == -1) return 0;
        
        startIdx += searchKey.Length;
        
        // Skip whitespace
        while (startIdx < json.Length && (json[startIdx] == ' ' || json[startIdx] == '\t')) startIdx++;
        
        // Find the end of the number
        int endIdx = startIdx;
        while (endIdx < json.Length && (char.IsDigit(json[endIdx]) || json[endIdx] == '-')) endIdx++;
        
        if (endIdx == startIdx) return 0;
        
        string numberStr = json.Substring(startIdx, endIdx - startIdx);
        if (int.TryParse(numberStr, out int result))
        {
            return result;
        }
        return 0;
    }
    
    /// <summary>
    /// Extract float value from JSON
    /// </summary>
    float ExtractFloatValue(string json, string key)
    {
        string searchKey = $"\"{key}\":";
        int startIdx = json.IndexOf(searchKey);
        if (startIdx == -1) return 0f;

        startIdx += searchKey.Length;

        while (startIdx < json.Length && (json[startIdx] == ' ' || json[startIdx] == '\t')) startIdx++;

        int endIdx = startIdx;
        // Accept leading minus sign
        if (endIdx < json.Length && json[endIdx] == '-') endIdx++;
        while (endIdx < json.Length && (char.IsDigit(json[endIdx]) || json[endIdx] == '.')) endIdx++;

        if (endIdx == startIdx) return 0f;

        string numberStr = json.Substring(startIdx, endIdx - startIdx);
        return float.TryParse(numberStr, System.Globalization.NumberStyles.Float,
                              System.Globalization.CultureInfo.InvariantCulture, out float result)
            ? result : 0f;
    }
    
    /// <summary>
    /// Extract boolean value from JSON
    /// </summary>
    bool ExtractBoolValue(string json, string key)
    {
        string searchKey = $"\"{key}\":";
        int startIdx = json.IndexOf(searchKey);
        if (startIdx == -1) return false;
        
        startIdx += searchKey.Length;
        
        // Skip whitespace
        while (startIdx < json.Length && (json[startIdx] == ' ' || json[startIdx] == '\t')) startIdx++;
        
        if (startIdx + 4 < json.Length && json.Substring(startIdx, 4) == "true")
        {
            return true;
        }
        return false;
    }

    /// <summary>Parses a JSON array of strings for the given key. Returns empty array if not found.</summary>
    string[] ExtractStringArray(string json, string key)
    {
        string searchKey = $"\"{key}\":";
        int startIdx = json.IndexOf(searchKey);
        if (startIdx == -1) return new string[0];

        startIdx += searchKey.Length;
        while (startIdx < json.Length && char.IsWhiteSpace(json[startIdx])) startIdx++;

        if (startIdx >= json.Length || json[startIdx] != '[') return new string[0];

        int endIdx = FindMatchingBracket(json, startIdx);
        if (endIdx == -1) return new string[0];

        string content = json.Substring(startIdx + 1, endIdx - startIdx - 1);
        var results = new List<string>();
        int pos = 0;
        while (pos < content.Length)
        {
            while (pos < content.Length && (content[pos] == ' ' || content[pos] == '\n' || content[pos] == '\r' || content[pos] == '\t' || content[pos] == ',')) pos++;
            if (pos >= content.Length) break;
            if (content[pos] == '"')
            {
                pos++;
                int end = content.IndexOf('"', pos);
                if (end == -1) break;
                string val = content.Substring(pos, end - pos);
                if (!string.IsNullOrEmpty(val)) results.Add(val);
                pos = end + 1;
            }
            else break;
        }
        return results.ToArray();
    }

    /// <summary>
    /// Extracts the <c>connections</c> string array from a named contract object in the step JSON.
    /// e.g. contractName="productionMemoryContract" → returns connections array.
    /// </summary>
    string[] ExtractContractConnections(string json, string contractName)
    {
        string contractJson = ExtractObjectJson(json, contractName);
        if (string.IsNullOrEmpty(contractJson)) return new string[0];
        return ExtractStringArray(contractJson, "connections");
    }

    string ExtractObjectJson(string json, string objectName)
    {
        string searchKey = $"\"{objectName}\":";
        int startIdx = json.IndexOf(searchKey);
        if (startIdx == -1) return "";

        int braceStart = json.IndexOf('{', startIdx + searchKey.Length);
        if (braceStart == -1) return "";

        int braceEnd = FindMatchingBrace(json, braceStart);
        if (braceEnd == -1) return "";

        return json.Substring(braceStart, braceEnd - braceStart + 1);
    }

    void ParseRawContracts(string stepJson, ActionSequenceStep step)
    {
        step.contractJsonByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string[] contractNames =
        {
            "productionMemoryContract",
            "goalBufferContract",
            "imaginalBufferContract",
            "visualModuleContract",
            "manualModuleContract",
            "intentionalModuleContract",
            "retrievalBufferContract",
            "declarativeModuleContract",
            "temporalBufferContract"
        };

        foreach (string name in contractNames)
        {
            string contractJson = ExtractObjectJson(stepJson, name);
            if (string.IsNullOrEmpty(contractJson)) continue;
            step.contractJsonByName[name] = contractJson;
        }

        step.productionMemoryContractJson = step.GetContractJson("productionMemoryContract");
        step.visualModuleContractJson     = step.GetContractJson("visualModuleContract");
        step.manualModuleContractJson     = step.GetContractJson("manualModuleContract");
        step.imaginalBufferContractJson   = step.GetContractJson("imaginalBufferContract");
        step.intentionalModuleContractJson = step.GetContractJson("intentionalModuleContract");
        step.retrievalBufferContractJson  = step.GetContractJson("retrievalBufferContract");
        step.declarativeModuleContractJson = step.GetContractJson("declarativeModuleContract");
    }

    /// <summary>
    /// Extracts <c>state_before</c> and <c>state_after</c> from <c>imaginalBufferContract.initial_state</c>.
    /// </summary>
    void ExtractImaginalBufferStates(string json, out string stateBefore, out string stateAfter)
    {
        stateBefore = "";
        stateAfter  = "";
        string searchKey = "\"imaginalBufferContract\":";
        int startIdx = json.IndexOf(searchKey);
        if (startIdx == -1) return;

        int braceStart = json.IndexOf('{', startIdx + searchKey.Length);
        if (braceStart == -1) return;

        int braceEnd = FindMatchingBrace(json, braceStart);
        if (braceEnd == -1) return;

        string contractJson = json.Substring(braceStart, braceEnd - braceStart + 1);

        // Drill into initial_state object
        int isStart = contractJson.IndexOf("\"initial_state\":");
        if (isStart == -1) return;
        int isBrace = contractJson.IndexOf('{', isStart);
        if (isBrace == -1) return;
        int isEnd = FindMatchingBrace(contractJson, isBrace);
        if (isEnd == -1) return;

        string initialStateJson = contractJson.Substring(isBrace, isEnd - isBrace + 1);
        stateBefore = ExtractStringValue(initialStateJson, "state_before");
        stateAfter  = ExtractStringValue(initialStateJson, "state_after");
    }

    string ExtractImaginalThoughtText(string json)
    {
        string contractJson = ExtractObjectJson(json, "imaginalBufferContract");
        if (string.IsNullOrEmpty(contractJson)) return "";

        string[] phaseKeys = { "initial_state", "execution_state", "transition_state" };
        for (int i = 0; i < phaseKeys.Length; i++)
        {
            string phaseJson = ExtractObjectJson(contractJson, phaseKeys[i]);
            if (string.IsNullOrEmpty(phaseJson)) continue;

            string thought = ExtractStringValue(phaseJson, "imagination_statement");
            if (!string.IsNullOrWhiteSpace(thought))
                return thought;
        }

        return ExtractStringValue(contractJson, "imagination_statement");
    }
    
    /// <summary>
    /// Parse required skills from step JSON into a List
    /// </summary>
    void ParseRequiredSkillsToList(string stepJson, List<SkillRequirement> skillsList)
    {
        // Find requiredSkills array
        int skillsStart = stepJson.IndexOf("\"requiredSkills\":");
        if (skillsStart == -1) return;
        
        int arrayStart = stepJson.IndexOf("[", skillsStart);
        if (arrayStart == -1) return;
        
        int arrayEnd = FindMatchingBracket(stepJson, arrayStart);
        if (arrayEnd == -1) return;
        
        string skillsArrayJson = stepJson.Substring(arrayStart, arrayEnd - arrayStart + 1);
        
        // Parse each skill object or simple string in the array.
        int currentPos = 1; // Skip opening '['
        while (currentPos < skillsArrayJson.Length - 1)
        {
            // Skip whitespace and commas
            while (currentPos < skillsArrayJson.Length && (skillsArrayJson[currentPos] == ' ' || skillsArrayJson[currentPos] == '\n' || skillsArrayJson[currentPos] == '\r' || skillsArrayJson[currentPos] == '\t' || skillsArrayJson[currentPos] == ','))
            {
                currentPos++;
            }
            
            if (currentPos >= skillsArrayJson.Length - 1) break;
            
            if (skillsArrayJson[currentPos] == '"')
            {
                int skillEnd = skillsArrayJson.IndexOf('"', currentPos + 1);
                if (skillEnd == -1) break;
                string skillName = skillsArrayJson.Substring(currentPos + 1, skillEnd - currentPos - 1);
                if (!string.IsNullOrWhiteSpace(skillName))
                {
                    skillsList.Add(new SkillRequirement
                    {
                        onetSkillCode = skillName.Trim().ToLowerInvariant().Replace(" ", "_"),
                        skillName = skillName.Trim(),
                        category = "cognitive",
                        requiredLevel = 1f,
                        isCritical = false
                    });
                }
                currentPos = skillEnd + 1;
                continue;
            }

            int skillStart = skillsArrayJson.IndexOf("{", currentPos);
            if (skillStart == -1) break;

            int skillEndObj = FindMatchingBrace(skillsArrayJson, skillStart);
            if (skillEndObj == -1) break;

            string skillJson = skillsArrayJson.Substring(skillStart, skillEndObj - skillStart + 1);

            SkillRequirement skill = new SkillRequirement();
            skill.onetSkillCode = ExtractStringValue(skillJson, "onetSkillCode");
            skill.skillName = ExtractStringValue(skillJson, "skillName");
            skill.category = ExtractStringValue(skillJson, "category");
            skill.requiredLevel = ExtractFloatValue(skillJson, "requiredLevel");
            skill.isCritical = ExtractBoolValue(skillJson, "isCritical");

            if (string.IsNullOrWhiteSpace(skill.skillName))
                skill.skillName = skill.onetSkillCode;
            if (string.IsNullOrWhiteSpace(skill.onetSkillCode) && !string.IsNullOrWhiteSpace(skill.skillName))
                skill.onetSkillCode = skill.skillName.Trim().ToLowerInvariant().Replace(" ", "_");
            if (string.IsNullOrWhiteSpace(skill.category))
                skill.category = "cognitive";

            skillsList.Add(skill);

            currentPos = skillEndObj + 1;
        }
    }
    
    void ParseAgentSequence(string agentId, string json)
    {
        Debug.Log($"🔍 AgentSequenceManager: Parsing sequence for {agentId}...");
        AgentSequenceData sequenceData = new AgentSequenceData(agentId);
        
        // Find actionSequence array for this agent
        string searchKey = $"\"{agentId}\"";
        int agentStart = json.IndexOf(searchKey);
        if (agentStart == -1) 
        {
            Debug.LogError($"❌ AgentSequenceManager: Could not find agent '{agentId}' in JSON");
            return;
        }
        
        Debug.Log($"✅ AgentSequenceManager: Found agent '{agentId}' at index {agentStart}");
        
        int actionSeqStart = json.IndexOf("\"actionSequence\":", agentStart);
        if (actionSeqStart == -1) 
        {
            Debug.LogError($"❌ AgentSequenceManager: Could not find 'actionSequence' for agent '{agentId}'");
            return;
        }
        
        Debug.Log($"✅ AgentSequenceManager: Found 'actionSequence' for '{agentId}' at index {actionSeqStart}");
        
        // Extract array content
        int arrayStart = json.IndexOf("[", actionSeqStart);
        int arrayEnd = json.IndexOf("]", arrayStart);
        int bracketCount = 0;
        bool inString = false;
        
        for (int i = arrayStart; i < json.Length && i <= arrayEnd; i++)
        {
            char c = json[i];
            if (c == '"' && (i == 0 || json[i-1] != '\\')) inString = !inString;
            if (!inString)
            {
                if (c == '[') bracketCount++;
                if (c == ']') bracketCount--;
                if (bracketCount == 0 && c == ']')
                {
                    arrayEnd = i;
                    break;
                }
            }
        }
        
        string arrayJson = json.Substring(arrayStart, arrayEnd - arrayStart + 1);
        Debug.Log($"📄 AgentSequenceManager: Extracted actionSequence array for '{agentId}' ({arrayJson.Length} chars)");
        
        // Parse steps using SimpleJSON or manual parsing
        // For now, use a simpler approach - parse step by step
        ParseStepsFromArray(sequenceData, arrayJson);
        
        agentSequences[agentId] = sequenceData;
        Debug.Log($"✅ Loaded sequence for {agentId}: {sequenceData.actionSequence.Count} steps");
    }
    
    void ParseStepsFromArray(AgentSequenceData sequenceData, string arrayJson)
    {
        Debug.Log($"🔍 AgentSequenceManager: Parsing steps from array for {sequenceData.agentId}...");
        Debug.Log($"📄 Array JSON preview: {arrayJson.Substring(0, Math.Min(200, arrayJson.Length))}...");
        
        // Use SimpleJSON if available, otherwise manual parsing
        // For manual parsing, find each step object
        int stepIndex = 0;
        int currentPos = 0;
        
        while (true)
        {
            int stepStart = arrayJson.IndexOf("{", currentPos);
            if (stepStart == -1) 
            {
                Debug.Log($"✅ AgentSequenceManager: No more steps found for {sequenceData.agentId}. Total parsed: {stepIndex}");
                break;
            }
            
            int stepEnd = FindMatchingBrace(arrayJson, stepStart);
            if (stepEnd == -1) 
            {
                Debug.LogError($"❌ AgentSequenceManager: Could not find matching brace for step {stepIndex} in {sequenceData.agentId}");
                break;
            }
            
            string stepJson = arrayJson.Substring(stepStart, stepEnd - stepStart + 1);
            Debug.Log($"🔍 AgentSequenceManager: Parsing step {stepIndex} for {sequenceData.agentId} ({stepJson.Length} chars)");
            
            ActionSequenceStep step = ParseStep(stepJson);
            
            if (step != null)
            {
                sequenceData.actionSequence.Add(step);
                Debug.Log($"✅ AgentSequenceManager: Added step {stepIndex} ({step.stepId}) for {sequenceData.agentId}");
            }
            else
            {
                Debug.LogError($"❌ AgentSequenceManager: Failed to parse step {stepIndex} for {sequenceData.agentId}");
            }
            
            currentPos = stepEnd + 1;
            stepIndex++;
        }
        
        Debug.Log($"✅ AgentSequenceManager: Finished parsing steps for {sequenceData.agentId}. Total steps: {sequenceData.actionSequence.Count}");
    }
    
    int FindMatchingBrace(string json, int start)
    {
        int braceCount = 0;
        bool inString = false;
        
        for (int i = start; i < json.Length; i++)
        {
            char c = json[i];
            if (c == '"' && (i == 0 || json[i-1] != '\\')) inString = !inString;
            if (!inString)
            {
                if (c == '{') braceCount++;
                if (c == '}') 
                {
                    braceCount--;
                    if (braceCount == 0) return i;
                }
            }
        }
        return -1;
    }
    
    ActionSequenceStep ParseStep(string stepJson)
    {
        // Use the improved ParseStepManual method instead
        return ParseStepManual(stepJson);
    }
    
    /// <summary>
    /// Load tool positions from SceneUILoader or initialStates
    /// </summary>
    void LoadToolPositions()
    {
        // Try to get positions from SceneUILoader
        if (sceneLoader != null && sceneLoader.sceneData != null)
        {
            var initialStates = sceneLoader.sceneData.initialStates;
            if (initialStates != null)
            {
                foreach (var kvp in initialStates)
                {
                    if (kvp.Value != null && kvp.Value.position != null)
                    {
                        Vector3 pos = new Vector3(
                            kvp.Value.position.x,
                            kvp.Value.position.y,
                            kvp.Value.position.z
                        );
                        toolPositions[kvp.Key] = pos;
                        Debug.Log($"📍 Loaded position for {kvp.Key}: {pos}");
                    }
                }
            }
        }
        
        // Fallback: Use hardcoded positions from JSON structure
        if (toolPositions.Count == 0)
        {
            toolPositions["tool_001"] = new Vector3(-4f, 0.5f, -5f);
            toolPositions["tool_002"] = new Vector3(0f, 0.5f, -5f);
            toolPositions["tool_003"] = new Vector3(4f, 0.5f, -5f);
            toolPositions["tool_004"] = new Vector3(-2f, 0.5f, 3f);
            toolPositions["workbench_001"] = new Vector3(2f, 0.8f, 3f);
            Debug.Log("📍 Using fallback tool positions");
        }
    }
    
    /// <summary>
    /// Get sequence data for an agent
    /// </summary>
    public AgentSequenceData GetSequence(string agentId)
    {
        if (agentSequences.ContainsKey(agentId))
        {
            return agentSequences[agentId];
        }
        return null;
    }

    public AgentSequenceData GetCognitiveSequence(string agentId)
    {
        if (cognitiveAgentSequences.ContainsKey(agentId))
        {
            return cognitiveAgentSequences[agentId];
        }

        return null;
    }

    /// <summary>
    /// Returns the full physical-agent step list parsed from <c>physicalAgents[].steps</c> in the JSON.
    /// These steps carry three-phase execution fields: <c>dependsOn</c>, <c>isBarrier</c>, etc.
    /// </summary>
    public AgentSequenceData GetPhysicalSequence(string agentId)
    {
        if (physicalAgentSequences.ContainsKey(agentId))
            return physicalAgentSequences[agentId];
        return null;
    }

    /// <summary>
    /// Returns all physical sequences keyed by agentId. Useful when the caller doesn't know
    /// the exact physical agent ID (e.g. the orchestrator iterates them during initialization).
    /// </summary>
    public Dictionary<string, AgentSequenceData> GetAllPhysicalSequences()
    {
        return physicalAgentSequences;
    }
    
    /// <summary>
    /// Get current target position for an agent.
    /// When <paramref name="zoneIndex"/> &gt;= 0, resolves <c>{baseTargetId}_zone{zoneIndex}</c> (same as physical agents).
    /// </summary>
    public Vector3? GetCurrentTargetPosition(string agentId, int zoneIndex = -1)
    {
        AgentSequenceData sequence = GetSequence(agentId);
        if (sequence == null) return null;
        
        ActionSequenceStep currentStep = sequence.GetCurrentStep();
        if (currentStep == null) return null;
        
        string targetId = PhysicalStepTargetResolver.IsPhysicalStep(currentStep)
            ? PhysicalStepTargetResolver.ResolveObjectId(currentStep, zoneIndex)
            : currentStep.targetObjectId;
        if (string.IsNullOrEmpty(targetId)) return null;

        if (zoneIndex >= 0)
            return GetTargetPositionById(targetId, zoneIndex);

        if (toolPositions.ContainsKey(targetId))
            return toolPositions[targetId];

        return null;
    }
    
    /// <summary>
    /// Get current step's correct_step_reward
    /// </summary>
    public float GetCurrentStepReward(string agentId)
    {
        AgentSequenceData sequence = GetSequence(agentId);
        if (sequence == null) return 0f;
        
        ActionSequenceStep currentStep = sequence.GetCurrentStep();
        if (currentStep == null) return 0f;
        
        return currentStep.correct_step_reward;
    }
    
    /// <summary>
    /// Check if agent has reached current target
    /// </summary>
    public bool HasReachedTarget(string agentId, Vector3 agentPosition, float threshold = 3.0f, int zoneIndex = -1)
    {
        Vector3? targetPos = GetCurrentTargetPosition(agentId, zoneIndex);
        if (!targetPos.HasValue) return false;
        
        float distance = Vector3.Distance(agentPosition, targetPos.Value);
        return distance <= threshold;
    }
    
    /// <summary>
    /// Calculate direction vector from agent to target (normalized)
    /// </summary>
    public Vector3 GetDirectionToTarget(string agentId, Vector3 agentPosition, int zoneIndex = -1)
    {
        Vector3? targetPos = GetCurrentTargetPosition(agentId, zoneIndex);
        if (!targetPos.HasValue) return Vector3.zero;
        
        Vector3 direction = (targetPos.Value - agentPosition);
        direction.y = 0; // Keep on horizontal plane
        return direction.normalized;
    }
    
    /// <summary>
    /// Get distance to current target (zone-aware when <paramref name="zoneIndex"/> &gt;= 0).
    /// </summary>
    public float GetDistanceToTarget(string agentId, Vector3 agentPosition, int zoneIndex = -1)
    {
        Vector3? targetPos = GetCurrentTargetPosition(agentId, zoneIndex);
        if (!targetPos.HasValue) return float.MaxValue;
        
        Vector3 direction = targetPos.Value - agentPosition;
        direction.y = 0;
        return direction.magnitude;
    }

    /// <summary>
    /// Force-register a world position for a target ID, overwriting any stale cached value.
    /// Called by BSGMLAgent when the physical step begins so RL observations always point
    /// to the correct scene object (bypassing cognitive-station pollution).
    /// </summary>
    public void RegisterToolPosition(string targetId, Vector3 position)
    {
        if (!string.IsNullOrWhiteSpace(targetId))
            toolPositions[targetId] = position;
    }

    public Vector3? GetTargetPositionById(string targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId))
        {
            return null;
        }

        if (toolPositions.TryGetValue(targetId, out Vector3 pos))
        {
            return pos;
        }

        // Runtime fallback: resolve directly from scene object names and cache.
        GameObject exact = GameObject.Find(targetId);
        if (exact != null)
        {
            Vector3 exactPos = exact.transform.position;
            toolPositions[targetId] = exactPos;
            return exactPos;
        }

        GameObject[] all = FindObjectsOfType<GameObject>();
        foreach (GameObject go in all)
        {
            if (go == null || string.IsNullOrEmpty(go.name)) continue;
            if (go.name.Equals(targetId, StringComparison.OrdinalIgnoreCase) ||
                go.name.IndexOf(targetId, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Vector3 resolved = go.transform.position;
                toolPositions[targetId] = resolved;
                return resolved;
            }
        }

        return null;
    }

    /// <summary>
    /// Zone-aware overload: looks for "{baseTargetId}_zone{zoneIndex}" first so each agent
    /// resolves tools within its own quadrant. Strips an existing "_zoneN" suffix from
    /// <paramref name="targetId"/> before building the key so IDs like "tool_001_zone0" still resolve.
    /// Falls back to the base targetId if no zone-specific object exists.
    /// </summary>
    public Vector3? GetTargetPositionById(string targetId, int zoneIndex)
    {
        if (string.IsNullOrWhiteSpace(targetId)) return null;
        if (zoneIndex < 0) return GetTargetPositionById(targetId);

        string baseId = StripZoneSuffixFromTargetId(targetId);
        string zoneKey = $"{baseId}_zone{zoneIndex}";

        // Check cache first
        if (toolPositions.TryGetValue(zoneKey, out Vector3 cachedPos)) return cachedPos;

        // Direct scene lookup
        GameObject zoneObj = GameObject.Find(zoneKey);
        if (zoneObj != null)
        {
            toolPositions[zoneKey] = zoneObj.transform.position;
            return zoneObj.transform.position;
        }

        // Fallback to base targetId
        return GetTargetPositionById(baseId);
    }

    static string StripZoneSuffixFromTargetId(string id)
    {
        if (string.IsNullOrEmpty(id)) return id;
        int idx = id.IndexOf("_zone", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? id.Substring(0, idx) : id;
    }
}

/// <summary>
/// True while the player is quitting or the Editor is exiting Play Mode. Used to avoid allocating
/// new GameObjects from lazy singletons during teardown (Unity: "Did you spawn new GameObjects from OnDestroy?").
/// </summary>
internal static class PlayModeQuitGuard
{
    public static bool IsQuitting { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Register()
    {
        IsQuitting = false;
        Application.quitting += () => IsQuitting = true;
#if UNITY_EDITOR
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
    }

#if UNITY_EDITOR
    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode)
            IsQuitting = true;
        else if (state == PlayModeStateChange.EnteredPlayMode)
            IsQuitting = false;
    }
#endif
}

