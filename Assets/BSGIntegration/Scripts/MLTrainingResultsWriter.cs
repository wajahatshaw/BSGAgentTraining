using UnityEngine;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;

/// <summary>
/// Generates ML-Agents training results output files
/// Creates JSON files with agent progress, step completion, and learning statistics
/// </summary>
[System.Serializable]
public class MLTrainingResults
{
    public string timestamp;
    public string sceneId;
    public Dictionary<string, AgentTrainingResults> agentResults;
    
    public MLTrainingResults()
    {
        timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        agentResults = new Dictionary<string, AgentTrainingResults>();
    }
}

[System.Serializable]
public class AgentTrainingResults
{
    public string agentId;
    public float skillLevel;
    public float desireLevel;
    public bool isCompleted;
    public int completedSteps;
    public int totalSteps;
    public List<string> learnedSkills;
    public List<StepProgress> stepProgress;
    public float totalReward;
    public int episodes;
    public Dictionary<string, object> statistics;
    
    public AgentTrainingResults()
    {
        learnedSkills = new List<string>();
        stepProgress = new List<StepProgress>();
        statistics = new Dictionary<string, object>();
    }
}

[System.Serializable]
public class StepProgress
{
    public string stepId;
    public int stepOrder;
    public string actionType;
    public string preposition;
    public string targetObjectId;
    public string targetObjectName;
    public string description;
    public float expectedDuration;
    public bool isCompleted;
    public float completionTime;
    public List<string> skillsLearned;
    public List<string> requiredSkillCodes; // O*NET skill codes required for this step
    
    public StepProgress()
    {
        skillsLearned = new List<string>();
        requiredSkillCodes = new List<string>();
    }
}

public class MLTrainingResultsWriter : MonoBehaviour
{
    private static MLTrainingResultsWriter _instance;
    public static MLTrainingResultsWriter Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<MLTrainingResultsWriter>();
                if (_instance == null && Application.isPlaying && !PlayModeQuitGuard.IsQuitting)
                {
                    GameObject go = new GameObject("MLTrainingResultsWriter");
                    _instance = go.AddComponent<MLTrainingResultsWriter>();
                }
            }
            return _instance;
        }
    }
    
    [Header("Output Settings")]
    public string resultsFileName = "ml_agents_training_results.json";
    public bool autoSave = true;
    public float autoSaveInterval = 30f; // Save every 30 seconds
    [Tooltip("When true, disk JSON writes run only while mlagents-learn is connected. Keeps Unity-only play smooth.")]
    public bool diskWritesOnlyWhenTrainerConnected = true;

    [Tooltip("If set, final_game_results.json and ml_action_sequence_results.json go here when this folder exists under results/. Use when multiple run folders exist (e.g. match mlagents --run-id).")]
    public string preferredRunId = "";
    
    private MLTrainingResults trainingResults;
    private AgentSequenceManager sequenceManager;
    private SkillBasedActionSystem skillSystem;
    private float lastAutoSave = 0f;
    private float lastFinalResultsCheck = 0f;
    private const float FINAL_RESULTS_INTERVAL = 30f; // Generate final results every 30 seconds during training (reduced from 60s)
    private bool lastMLServerState = false;
    private bool hasGeneratedInitialResults = false;
    private float lastActionReceivedCheck = 0f; // Track when we last checked if agents are receiving actions
    private bool hadActiveAgents = false; // Track if agents were receiving actions previously
    /// <summary>Last run-id we logged — GetCurrentRunId() is not cached so switching --run-id updates exports.</summary>
    private string lastLoggedResolvedRunId;
    private float lastDisconnectCheck = 0f; // Track when we last checked for disconnect
    private const float DISCONNECT_CHECK_INTERVAL = 2f; // Check for disconnect every 2 seconds
    private bool _diskWriteQueued;
    private Coroutine _diskWriteCoroutine;
    private BSGMLAgent[] _cachedMlAgents = Array.Empty<BSGMLAgent>();
    private float _lastMlAgentCacheTime = -999f;
    private const float MlAgentCacheInterval = 5f;

    bool ShouldWriteHeavyResultsToDisk() =>
        !IsInferencePlayback()
        && (!diskWritesOnlyWhenTrainerConnected || IsMLAgentsServerConnected());

    static bool IsInferencePlayback() =>
        RagInferenceSceneController.IsInferenceSceneActive();

    void RefreshMlAgentCacheIfNeeded()
    {
        if (Time.unscaledTime - _lastMlAgentCacheTime < MlAgentCacheInterval) return;
        _lastMlAgentCacheTime = Time.unscaledTime;
        _cachedMlAgents = FindObjectsOfType<BSGMLAgent>();
    }
    
    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            // CRITICAL: Initialize immediately if created dynamically
            // This ensures Start() is called even if component is created at runtime
            InitializeComponent();
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }
    
    void Start()
    {
        InitializeComponent();
        
        // Generate initial final results file after a short delay to ensure all agents are initialized
        StartCoroutine(GenerateInitialFinalResults());
    }
    
    /// <summary>
    /// Initialize component data (called from both Awake and Start to ensure initialization)
    /// </summary>
    private void InitializeComponent()
    {
        // Only initialize if not already initialized
        if (trainingResults == null)
        {
            trainingResults = new MLTrainingResults();
            sequenceManager = FindObjectOfType<AgentSequenceManager>();
            if (sequenceManager == null && Application.isPlaying && !PlayModeQuitGuard.IsQuitting)
                sequenceManager = AgentSequenceManager.Instance;
            skillSystem = FindObjectOfType<SkillBasedActionSystem>();
            
            // Try to load scene ID
            var sceneLoader = FindObjectOfType<SceneUILoader>();
            if (sceneLoader != null && sceneLoader.sceneData != null)
            {
                trainingResults.sceneId = sceneLoader.sceneData.scene_id;
            }
            else
            {
                trainingResults.sceneId = "scene_warehouse_replica_001";
            }
            
            Debug.Log("✅ MLTrainingResultsWriter: Component initialized successfully");
        }
    }

    /// <summary>
    /// Re-resolve <see cref="AgentSequenceManager"/> / <see cref="SkillBasedActionSystem"/> before reads.
    /// Cached fields can point at destroyed objects after scene teardown or domain reload; <c>stepProgress</c> may
    /// still look valid from earlier <see cref="UpdateAgentResults"/> while <see cref="AgentSequenceManager.GetCognitiveSequence"/>
    /// would return null — producing empty <c>cognitiveStepProgress</c> in <c>final_game_results.json</c>.
    /// </summary>
    private void RefreshTrainingDependencies()
    {
        if (sequenceManager == null)
            sequenceManager = FindObjectOfType<AgentSequenceManager>();
        if (skillSystem == null)
            skillSystem = FindObjectOfType<SkillBasedActionSystem>();
    }

    /// <summary>Episode reward accumulated in the current ML-Agents episode.</summary>
    private float GetLiveEpisodeTotalReward(string agentId)
    {
        BSGMLAgent b = FindBSGMLAgent(agentId);
        return b != null ? b.EpisodeTotalReward : 0f;
    }

    /// <summary>Persona overlay disabled in Ronald-Johnson integration (PersonaSystem not included).</summary>
    private void ApplyPersonaTrainingOverlayIfAny(string agentId, AgentTrainingResults agentResult)
    {
    }

    /// <summary>Same id normalization as <see cref="BSGMLAgent"/> cognitive fallback (SIMPLE_* profile keys).</summary>
    private static string ResolveCanonicalAgentIdForCognitive(string rawId)
    {
        if (string.IsNullOrWhiteSpace(rawId)) return rawId;
        string lower = rawId.ToLowerInvariant().Replace(" ", "");
        bool isTech = lower.Contains("technician");
        bool isSup = lower.Contains("supervisor");
        bool has01 = lower.Contains("_01") || lower.Contains("01");
        bool has02 = lower.Contains("_02") || lower.Contains("02");
        if (isTech && has02) return "SIMPLE_Technician_02";
        if (isTech && has01) return "SIMPLE_Technician_01";
        if (isSup && has02) return "SIMPLE_Supervisor_02";
        if (isSup && has01) return "SIMPLE_Supervisor_01";
        return rawId;
    }

    /// <summary>Cognitive sequences are keyed like physical sequences; try raw id then canonical SIMPLE_* id.</summary>
    private AgentSequenceData ResolveCognitiveSequence(string agentId)
    {
        if (sequenceManager == null) return null;
        AgentSequenceData seq = sequenceManager.GetCognitiveSequence(agentId);
        if (seq != null && seq.actionSequence != null && seq.actionSequence.Count > 0)
            return seq;
        string canonical = ResolveCanonicalAgentIdForCognitive(agentId);
        if (!string.IsNullOrEmpty(canonical) && !string.Equals(canonical, agentId, StringComparison.OrdinalIgnoreCase))
        {
            seq = sequenceManager.GetCognitiveSequence(canonical);
            if (seq != null && seq.actionSequence != null && seq.actionSequence.Count > 0)
                return seq;
        }
        return null;
    }
    
    /// <summary>
    /// Resolve ML-Agents run-id for results/run_logs paths. Not cached: if you switch from --run-id=bsg_training_new
    /// to persona_training in the same Editor session, exports follow the active run (newest timers.json).
    /// </summary>
    private string GetCurrentRunId()
    {
        string id = ResolveRunIdUncached();
        if (id != lastLoggedResolvedRunId)
        {
            lastLoggedResolvedRunId = id;
            Debug.Log($"📂 MLTrainingResultsWriter: Resolved run-id for run_logs exports: {id}");
        }
        return id;
    }

    private string ResolveRunIdUncached()
    {
        string envRunId = System.Environment.GetEnvironmentVariable("MLAGENTS_RUN_ID");
        if (!string.IsNullOrEmpty(envRunId))
            return envRunId.Trim();

        try
        {
            string resultsBaseDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "results"));
            if (!string.IsNullOrWhiteSpace(preferredRunId) &&
                Directory.Exists(Path.Combine(resultsBaseDir, preferredRunId.Trim(), "run_logs")))
                return preferredRunId.Trim();
        }
        catch { }

        string fromTimers = TryDetectRunIdFromLatestTimersJson();
        if (!string.IsNullOrEmpty(fromTimers))
            return fromTimers;

        try
        {
            string resultsBaseDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "results"));
            if (Directory.Exists(resultsBaseDir))
            {
                string[] dirs = Directory.GetDirectories(resultsBaseDir);
                string mostRecentDir = null;
                DateTime mostRecentTime = DateTime.MinValue;
                foreach (string dir in dirs)
                {
                    try
                    {
                        if (!Directory.Exists(Path.Combine(dir, "run_logs")))
                            continue;
                        var dirInfo = new DirectoryInfo(dir);
                        if (dirInfo.LastWriteTime > mostRecentTime)
                        {
                            mostRecentTime = dirInfo.LastWriteTime;
                            mostRecentDir = dir;
                        }
                    }
                    catch { }
                }
                if (mostRecentDir != null)
                    return Path.GetFileName(mostRecentDir);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"⚠️ MLTrainingResultsWriter: Could not detect run-id from directory: {e.Message}");
        }

        return "bsg_training_new";
    }

    /// <summary>
    /// Picks the most recently active results/&lt;runId&gt;/run_logs (by max mtime of timers.json and training_status.json),
    /// then reads --run-id from that folder's timers.json, or falls back to the directory name (matches ONNX layout).
    /// </summary>
    /// <summary>Resolve the active results/&lt;runId&gt;/run_logs directory without needing an instance.
    /// Mirrors GetCurrentRunId's env-var → latest-timers detection, with a persona_training fallback.
    /// Used by side systems (e.g. Operating Paragraph export) that write into the same run_logs dir.</summary>
    public static string ResolveRunLogsDirStatic()
    {
        string envRunId = System.Environment.GetEnvironmentVariable("MLAGENTS_RUN_ID");
        string runId = !string.IsNullOrEmpty(envRunId) ? envRunId.Trim() : TryDetectRunIdFromLatestTimersJson();
        if (string.IsNullOrWhiteSpace(runId))
            runId = "persona_training";
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "results", runId, "run_logs"));
    }
    private static string TryDetectRunIdFromLatestTimersJson()
    {
        try
        {
            string resultsBaseDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "results"));
            if (!Directory.Exists(resultsBaseDir))
                return null;

            string bestRunRoot = null;
            DateTime bestUtc = DateTime.MinValue;

            foreach (string runRoot in Directory.GetDirectories(resultsBaseDir))
            {
                string runLogs = Path.Combine(runRoot, "run_logs");
                if (!Directory.Exists(runLogs))
                    continue;

                DateTime activityUtc = DateTime.MinValue;
                string timersPath = Path.Combine(runLogs, "timers.json");
                string statusPath = Path.Combine(runLogs, "training_status.json");
                if (File.Exists(timersPath))
                    activityUtc = File.GetLastWriteTimeUtc(timersPath);
                if (File.Exists(statusPath))
                {
                    DateTime st = File.GetLastWriteTimeUtc(statusPath);
                    if (st > activityUtc)
                        activityUtc = st;
                }
                if (activityUtc == DateTime.MinValue)
                    continue;

                if (activityUtc > bestUtc)
                {
                    bestUtc = activityUtc;
                    bestRunRoot = runRoot;
                }
            }

            if (bestRunRoot == null)
                return null;

            string timersInBest = Path.Combine(bestRunRoot, "run_logs", "timers.json");
            if (File.Exists(timersInBest))
            {
                string text = File.ReadAllText(timersInBest);
                const string needle = "--run-id=";
                int i = text.IndexOf(needle, StringComparison.Ordinal);
                if (i >= 0)
                {
                    i += needle.Length;
                    int j = i;
                    while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '_' || text[j] == '-'))
                        j++;
                    if (j > i)
                        return text.Substring(i, j - i);
                }
            }

            return Path.GetFileName(bestRunRoot);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Uses <see cref="AgentProfile.zoneIndex"/> from the loaded pipeline so RAG ids (P*, M*) map to the correct zone.</summary>
    private int ResolveZoneIndexForAgent(string agentId)
    {
        if (string.IsNullOrEmpty(agentId))
            return -1;
        RefreshTrainingDependencies();
        if (skillSystem != null)
        {
            AgentProfile profile = skillSystem.GetAgentProfile(agentId);
            if (profile != null && profile.zoneIndex >= 0)
                return profile.zoneIndex;
        }
        if (agentId.Contains("Technician_01"))
            return 0;
        if (agentId.Contains("Technician_02"))
            return 1;
        if (agentId.Contains("Supervisor_01"))
            return 2;
        if (agentId.Contains("Supervisor_02"))
            return 3;
        return -1;
    }

    /// <summary>Called after <see cref="SceneUILoader"/> parses new flat or RAG JSON — clears aggregated stats keyed by old agent ids.</summary>
    public void OnScenePipelineReloaded(SceneUILoader loader)
    {
        RefreshTrainingDependencies();
        trainingResults = new MLTrainingResults();
        if (loader != null && loader.sceneData != null && !string.IsNullOrWhiteSpace(loader.sceneData.scene_id))
            trainingResults.sceneId = loader.sceneData.scene_id;
        else
            trainingResults.sceneId = "scene_warehouse_replica_001";
        Debug.Log($"[MLTrainingResultsWriter] Reset training results for new pipeline (sceneId={trainingResults.sceneId}).");
    }

    /// <summary>Refresh scene id and remove agent rows no longer present in the current SkillBasedActionSystem profiles.</summary>
    private void EnsureResultsMatchCurrentScene()
    {
        if (trainingResults == null)
        {
            InitializeComponent();
            return;
        }
        RefreshTrainingDependencies();
        SceneUILoader loader = FindObjectOfType<SceneUILoader>();
        if (loader != null && loader.sceneData != null && !string.IsNullOrWhiteSpace(loader.sceneData.scene_id))
            trainingResults.sceneId = loader.sceneData.scene_id;
        if (skillSystem == null)
            return;
        List<string> staleKeys = trainingResults.agentResults.Keys
            .Where(k => skillSystem.GetAgentProfile(k) == null)
            .ToList();
        foreach (string k in staleKeys)
        {
            trainingResults.agentResults.Remove(k);
            Debug.Log($"[MLTrainingResultsWriter] Removed stale agent result '{k}' (not in current pipeline profiles).");
        }
    }
    
    /// <summary>
    /// Generate initial final results after agents are initialized
    /// </summary>
    private System.Collections.IEnumerator GenerateInitialFinalResults()
    {
        yield return new WaitForSeconds(3f);
        if (!ShouldWriteHeavyResultsToDisk())
            yield break;
        QueueDeferredDiskWrite(includeFinalResults: true);
        hasGeneratedInitialResults = true;
    }
    
    void Update()
    {
        if (trainingResults == null)
            InitializeComponent();

        if (!ShouldWriteHeavyResultsToDisk())
            return;
        
        if (autoSave && Time.time - lastAutoSave >= autoSaveInterval)
        {
            QueueDeferredDiskWrite(includeFinalResults: true);
            hasGeneratedInitialResults = true;
            lastAutoSave = Time.time;
        }
        
        // CRITICAL: Check if ML-Agents server is still connected (check every 2 seconds for fast detection)
        bool mlServerConnected = false;
        if (Time.time - lastDisconnectCheck >= DISCONNECT_CHECK_INTERVAL)
        {
            mlServerConnected = IsMLAgentsServerConnected();
            
            // If server was connected and now disconnected, generate final results IMMEDIATELY
            // This happens when training is interrupted (Ctrl+C) or ends naturally
            if (lastMLServerState && !mlServerConnected)
            {
                Debug.Log("🔄 MLTrainingResultsWriter: ML-Agents server disconnected - Generating final game results...");
                Debug.Log("🔄 This typically happens when training stops/interrupts (Ctrl+C) - similar to ONNX file generation");
                
                // CRITICAL: Generate immediately with synchronous write (don't wait for coroutine)
                GenerateFinalGameResultsSynchronous();
                
                // Also start coroutine as backup (in case Unity doesn't close immediately)
                if (this != null && gameObject.activeInHierarchy)
                {
                    StartCoroutine(EnsureFinalResultsWrittenAfterDisconnect());
                }
                
                hasGeneratedInitialResults = true;
                
                // Log file location for debugging
                string runId = GetCurrentRunId();
                string resultsPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "results", runId, "run_logs", "final_game_results.json"));
                Debug.Log($"📂 Final results file should be at: {resultsPath}");
            }
            
            lastMLServerState = mlServerConnected;
            lastDisconnectCheck = Time.time;
        }
        else
        {
            // Use cached state if we're not checking this frame
            mlServerConnected = lastMLServerState;
        }
        
        // ADDITIONAL CHECK: If server was connected and suddenly stops responding (timeout detection)
        // Check if agents stopped receiving actions but server still reports as connected
        if (mlServerConnected && Time.time - lastActionReceivedCheck >= 5f)
        {
            bool agentsReceivingActions = CheckAgentsReceivingActions();
            if (!agentsReceivingActions && hadActiveAgents)
            {
                Debug.LogWarning("⚠️ MLTrainingResultsWriter: Agents stopped receiving actions but server still connected - possible disconnect");
                Debug.Log("🔄 MLTrainingResultsWriter: Generating final game results (agents inactive)...");
                GenerateFinalGameResults();
                StartCoroutine(EnsureFinalResultsWrittenAfterDisconnect());
                hasGeneratedInitialResults = true;
            }
            lastActionReceivedCheck = Time.time;
            hadActiveAgents = agentsReceivingActions;
        }
        
        if (Time.time - lastFinalResultsCheck >= FINAL_RESULTS_INTERVAL)
        {
            QueueDeferredDiskWrite(includeFinalResults: true);
            hasGeneratedInitialResults = true;
            lastFinalResultsCheck = Time.time;
            CheckTrainingStatusAndGenerateIfNeeded();
        }
        
        if (!hasGeneratedInitialResults && Time.time >= 5f)
        {
            QueueDeferredDiskWrite(includeFinalResults: true);
            hasGeneratedInitialResults = true;
        }
    }

    void QueueDeferredDiskWrite(bool includeFinalResults)
    {
        if (!ShouldWriteHeavyResultsToDisk() || !isActiveAndEnabled)
            return;
        if (_diskWriteQueued)
            return;
        _diskWriteQueued = true;
        if (_diskWriteCoroutine != null)
            StopCoroutine(_diskWriteCoroutine);
        _diskWriteCoroutine = StartCoroutine(CoDeferredDiskWrite(includeFinalResults));
    }

    IEnumerator CoDeferredDiskWrite(bool includeFinalResults)
    {
        yield return null;
        try
        {
            SaveTrainingResults();
            if (includeFinalResults)
                GenerateFinalGameResults();
        }
        finally
        {
            _diskWriteQueued = false;
            _diskWriteCoroutine = null;
        }
    }
    
    /// <summary>
    /// Generate final results when component is destroyed
    /// CRITICAL: Use synchronous write since Unity might be closing
    /// </summary>
    void OnDestroy()
    {
        if (IsInferencePlayback())
        {
            if (_instance == this)
                _instance = null;
            return;
        }

        Debug.Log("🔄 MLTrainingResultsWriter: Component destroyed - Generating final game results...");
        
        // Use synchronous method since Unity might be closing (coroutines won't run)
        GenerateFinalGameResultsSynchronous();
        System.Threading.Thread.Sleep(150);
        
        // One more attempt
        GenerateFinalGameResultsSynchronous();

        if (_instance == this)
            _instance = null;
    }
    
    /// <summary>
    /// Check if training has ended by checking if training_status.json hasn't been updated recently
    /// This is a fallback mechanism in case disconnect detection doesn't work
    /// </summary>
    private void CheckTrainingStatusAndGenerateIfNeeded()
    {
        try
        {
            string runId = GetCurrentRunId();
            string trainingStatusPath = Path.Combine(Application.dataPath, "..", "results", runId, "run_logs", "training_status.json");
            trainingStatusPath = Path.GetFullPath(trainingStatusPath);
            
            if (File.Exists(trainingStatusPath))
            {
                var fileInfo = new FileInfo(trainingStatusPath);
                var timeSinceLastUpdate = DateTime.Now - fileInfo.LastWriteTime;
                
                // If training_status.json hasn't been updated in 60 seconds and we have agent data,
                // it likely means training has ended
                if (timeSinceLastUpdate.TotalSeconds > 60 && 
                    trainingResults != null && 
                    trainingResults.agentResults.Count > 0)
                {
                    Debug.Log($"🔄 MLTrainingResultsWriter: Training status file hasn't been updated in {timeSinceLastUpdate.TotalSeconds:F0}s - Training may have ended");
                    Debug.Log("🔄 MLTrainingResultsWriter: Generating final game results as safety measure...");
                    GenerateFinalGameResultsSynchronous();
                }
            }
        }
        catch (Exception e)
        {
            // Silently fail - this is just a safety check
            Debug.LogWarning($"⚠️ MLTrainingResultsWriter: Could not check training status: {e.Message}");
        }
    }
    
    /// <summary>
    /// Check if ML-Agents training server is connected
    /// </summary>
    private bool IsMLAgentsServerConnected()
    {
        try
        {
            var academy = Unity.MLAgents.Academy.Instance;
            if (academy == null) return false;
            
            // Use reflection to check communicator status
            var communicatorField = typeof(Unity.MLAgents.Academy).GetField("m_Communicator",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (communicatorField != null)
            {
                var communicator = communicatorField.GetValue(academy);
                if (communicator != null)
                {
                    var isConnectedProperty = communicator.GetType().GetProperty("IsConnected");
                    if (isConnectedProperty != null)
                    {
                        return (bool)isConnectedProperty.GetValue(communicator);
                    }
                }
            }
        }
        catch (Exception e)
        {
            // If reflection fails, assume not connected
            return false;
        }
        
        return false;
    }
    
    /// <summary>
    /// Generate final results when application quits or game ends
    /// CRITICAL: This is called when Unity closes, similar to when training stops (Ctrl+C)
    /// </summary>
    void OnApplicationQuit()
    {
        if (IsInferencePlayback())
            return;

        Debug.Log("🔄 MLTrainingResultsWriter: Application quitting - Generating final game results...");
        Debug.Log("🔄 This is triggered when Unity closes (similar to ONNX file generation when training stops)");
        
        // CRITICAL: Generate synchronously multiple times to ensure file is written
        // Use synchronous method that writes immediately without coroutines
        GenerateFinalGameResultsSynchronous();
        
        // Wait briefly to ensure file system buffers are flushed
        System.Threading.Thread.Sleep(200);
        
        // Generate one more time as final safety
        GenerateFinalGameResultsSynchronous();
        System.Threading.Thread.Sleep(100);
        
        // Log file location for verification
        string runId = GetCurrentRunId();
        string resultsPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "results", runId, "run_logs", "final_game_results.json"));
        bool fileExists = File.Exists(resultsPath);
        Debug.Log($"📂 OnApplicationQuit: Final results file exists: {fileExists} at {resultsPath}");
        
        if (!fileExists)
        {
            Debug.LogError("❌ CRITICAL: Final results file was NOT created on quit! Attempting emergency write...");
            // Emergency write - try one more time
            GenerateFinalGameResultsSynchronous();
            System.Threading.Thread.Sleep(300);
        }
    }
    
    /// <summary>
    /// Generate final results when training completes (call this when Python script stops)
    /// </summary>
    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            Debug.Log("🔄 MLTrainingResultsWriter: Application paused - Generating final game results...");
            GenerateFinalGameResults();
        }
    }
    
    void OnDisable()
    {
        if (IsInferencePlayback())
            return;

        Debug.Log("🔄 MLTrainingResultsWriter: Component disabled - Generating final game results...");
        
        // Use synchronous method since component might be disabled during Unity shutdown
        GenerateFinalGameResultsSynchronous();
        System.Threading.Thread.Sleep(100);
    }
    
    /// <summary>
    /// Manual trigger for final results generation (callable from inspector or code)
    /// </summary>
    [ContextMenu("Generate Final Game Results Now")]
    public void GenerateFinalResultsNow()
    {
        Debug.Log("🎮 MLTrainingResultsWriter: Manually generating final game results...");
        GenerateFinalGameResults();
    }
    
    /// <summary>
    /// Get current training results (for external components like StepEfficiencyIndicator)
    /// </summary>
    public MLTrainingResults GetTrainingResults()
    {
        if (trainingResults == null)
        {
            InitializeComponent();
        }
        return trainingResults;
    }
    
    /// <summary>
    /// Update agent results from current game state
    /// </summary>
    public void UpdateAgentResults(string agentId)
    {
        RefreshTrainingDependencies();
        if (skillSystem == null || sequenceManager == null) return;
        
        var agent = skillSystem.GetAgentProfile(agentId);
        var sequence = sequenceManager.GetSequence(agentId);
        var cognitiveSequence = ResolveCognitiveSequence(agentId);
        
        if (agent == null || sequence == null) return;
        
        AgentTrainingResults agentResult;
        if (trainingResults.agentResults.ContainsKey(agentId))
        {
            agentResult = trainingResults.agentResults[agentId];
        }
        else
        {
            agentResult = new AgentTrainingResults();
            agentResult.agentId = agentId;
            trainingResults.agentResults[agentId] = agentResult;
        }
        
        // Update agent statistics
        agentResult.skillLevel = agent.skillLevel;
        agentResult.desireLevel = agent.desireLevel;
        agentResult.isCompleted = agent.isCompleted;
        agentResult.totalSteps = sequence.actionSequence.Count;
        agentResult.completedSteps = sequence.actionSequence.Count(step => step.isStepCompleted);
        
        // Update learned skills
        agentResult.learnedSkills.Clear();
        if (agent.availableSkills != null)
        {
            foreach (var skill in agent.availableSkills)
            {
                agentResult.learnedSkills.Add($"{skill.skillName} ({skill.onetSkillCode})");
            }
        }
        ApplyPersonaTrainingOverlayIfAny(agentId, agentResult);
        
        // Update step progress - CRITICAL: Include ALL action sequence details
        agentResult.stepProgress.Clear();
        for (int i = 0; i < sequence.actionSequence.Count; i++)
        {
            var step = sequence.actionSequence[i];
            StepProgress stepProgress = new StepProgress();
            
            // Basic step information
            stepProgress.stepId = step.stepId;
            stepProgress.stepOrder = step.stepOrder;
            stepProgress.actionType = step.actionType;
            stepProgress.preposition = step.preposition ?? "";
            stepProgress.targetObjectId = step.targetObjectId;
            stepProgress.targetObjectName = step.targetObjectName ?? "";
            stepProgress.description = step.description ?? "";
            stepProgress.expectedDuration = step.expectedDuration;
            stepProgress.isCompleted = step.isStepCompleted;
            
            // Skills learned in this step
            if (step.learnSkills != null && step.learnSkills.Length > 0)
            {
                foreach (var skill in step.learnSkills)
                {
                    stepProgress.skillsLearned.Add(skill.skillName);
                }
            }
            
            // Required skills for this step (O*NET codes)
            if (step.requiredSkills != null && step.requiredSkills.Length > 0)
            {
                foreach (var reqSkill in step.requiredSkills)
                {
                    stepProgress.requiredSkillCodes.Add(reqSkill.onetSkillCode);
                }
            }
            
            if (step.isStepCompleted)
            {
                if (TryGetRecordedStepCompletionTime(agentId, step.stepId, out float recorded) && recorded >= 0f)
                    stepProgress.completionTime = recorded;
                else
                    stepProgress.completionTime = step.expectedDuration;
            }
            else
            {
                stepProgress.completionTime = 0f;
            }
            
            agentResult.stepProgress.Add(stepProgress);
        }
        
        // Update statistics
        agentResult.statistics["currentStepIndex"] = sequence.currentStepIndex;
        agentResult.statistics["hasNextStep"] = sequence.HasNextStep();
        if (cognitiveSequence != null)
        {
            agentResult.statistics["cognitiveCurrentStepIndex"] = cognitiveSequence.currentStepIndex;
            agentResult.statistics["cognitiveTotalSteps"] = cognitiveSequence.actionSequence.Count;
            agentResult.statistics["cognitiveCompletedSteps"] = cognitiveSequence.actionSequence.Count(step => step.isStepCompleted);
            agentResult.statistics["cognitiveSequenceCompleted"] = cognitiveSequence.actionSequence.Count > 0 &&
                                                                  cognitiveSequence.actionSequence.All(step => step.isStepCompleted);
        }
    }
    
    /// <summary>
    /// Save training results to JSON file
    /// </summary>
    public void SaveTrainingResults()
    {
        if (IsInferencePlayback())
            return;

        try
        {
            if (trainingResults == null)
                InitializeComponent();
            EnsureResultsMatchCurrentScene();

            // Update all agent results
            if (skillSystem != null)
            {
                var allAgents = skillSystem.GetAllAgentProfiles();
                foreach (var kvp in allAgents)
                {
                    UpdateAgentResults(kvp.Key);
                }
            }
            
            // Convert to JSON manually (JsonUtility doesn't handle dictionaries)
            string json = ConvertToJSON(trainingResults);
            
            // Save to results folder (ML-Agents standard location)
            string runId = GetCurrentRunId();
            string resultsPath = Path.Combine(Application.dataPath, "..", "results", runId, "run_logs", resultsFileName);
            
            // Ensure directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(resultsPath));
            
            File.WriteAllText(resultsPath, json);
            
            Debug.Log($"✅ MLTrainingResultsWriter: Saved training results to {resultsPath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ MLTrainingResultsWriter: Error saving results: {e.Message}");
        }
    }
    
    /// <summary>
    /// Convert MLTrainingResults to JSON string (handles dictionaries)
    /// </summary>
    private string ConvertToJSON(MLTrainingResults results)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"  \"timestamp\": \"{results.timestamp}\",\n");
        sb.Append($"  \"sceneId\": \"{results.sceneId}\",\n");
        sb.Append("  \"agentResults\": {\n");
        
        bool first = true;
        foreach (var kvp in results.agentResults.OrderBy(x => x.Key))
        {
            if (!first) sb.Append(",\n");
            first = false;
            
            sb.Append($"    \"{kvp.Key}\": ");
            sb.Append(ConvertAgentResultsToJSON(kvp.Value, "    "));
        }
        
        sb.Append("\n  }\n");
        sb.Append("}");
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Convert AgentTrainingResults to JSON string
    /// </summary>
    private string ConvertAgentResultsToJSON(AgentTrainingResults agentResult, string indent)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"{indent}  \"agentId\": \"{agentResult.agentId}\",\n");
        sb.Append($"{indent}  \"skillLevel\": {agentResult.skillLevel:F2},\n");
        sb.Append($"{indent}  \"desireLevel\": {agentResult.desireLevel:F2},\n");
        sb.Append($"{indent}  \"isCompleted\": {agentResult.isCompleted.ToString().ToLower()},\n");
        sb.Append($"{indent}  \"completedSteps\": {agentResult.completedSteps},\n");
        sb.Append($"{indent}  \"totalSteps\": {agentResult.totalSteps},\n");
        
        // Learned skills array
        sb.Append($"{indent}  \"learnedSkills\": [");
        for (int i = 0; i < agentResult.learnedSkills.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append($"\"{agentResult.learnedSkills[i]}\"");
        }
        sb.Append("],\n");
        
        // Step progress array
        sb.Append($"{indent}  \"stepProgress\": [\n");
        for (int i = 0; i < agentResult.stepProgress.Count; i++)
        {
            if (i > 0) sb.Append(",\n");
            sb.Append(ConvertStepProgressToJSON(agentResult.stepProgress[i], indent + "    "));
        }
        sb.Append($"\n{indent}  ],\n");
        
        sb.Append($"{indent}  \"totalReward\": {agentResult.totalReward:F2},\n");
        sb.Append($"{indent}  \"episodes\": {agentResult.episodes}\n");
        
        sb.Append($"{indent}}}");
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Convert StepProgress to JSON string - CRITICAL: Include ALL action sequence details
    /// </summary>
    private string ConvertStepProgressToJSON(StepProgress stepProgress, string indent)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append($"{indent}{{\n");
        sb.Append($"{indent}  \"stepId\": \"{stepProgress.stepId}\",\n");
        sb.Append($"{indent}  \"stepOrder\": {stepProgress.stepOrder},\n");
        sb.Append($"{indent}  \"actionType\": \"{stepProgress.actionType}\",\n");
        if (!string.IsNullOrEmpty(stepProgress.preposition))
        {
            sb.Append($"{indent}  \"preposition\": \"{stepProgress.preposition}\",\n");
        }
        sb.Append($"{indent}  \"targetObjectId\": \"{stepProgress.targetObjectId}\",\n");
        if (!string.IsNullOrEmpty(stepProgress.targetObjectName))
        {
            sb.Append($"{indent}  \"targetObjectName\": \"{stepProgress.targetObjectName}\",\n");
        }
        if (!string.IsNullOrEmpty(stepProgress.description))
        {
            sb.Append($"{indent}  \"description\": \"{stepProgress.description}\",\n");
        }
        sb.Append($"{indent}  \"expectedDuration\": {stepProgress.expectedDuration:F2},\n");
        sb.Append($"{indent}  \"isCompleted\": {stepProgress.isCompleted.ToString().ToLower()},\n");
        sb.Append($"{indent}  \"completionTime\": {stepProgress.completionTime:F2},\n");
        
        // Required skill codes (O*NET codes)
        sb.Append($"{indent}  \"requiredSkillCodes\": [");
        for (int i = 0; i < stepProgress.requiredSkillCodes.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append($"\"{stepProgress.requiredSkillCodes[i]}\"");
        }
        sb.Append("],\n");
        
        // Skills learned in this step
        sb.Append($"{indent}  \"skillsLearned\": [");
        for (int i = 0; i < stepProgress.skillsLearned.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append($"\"{stepProgress.skillsLearned[i]}\"");
        }
        sb.Append("]\n");
        sb.Append($"{indent}}}");
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Manually trigger save
    /// </summary>
    [ContextMenu("Save Training Results Now")]
    public void SaveResultsNow()
    {
        SaveTrainingResults();
    }
    
    /// <summary>
    /// Called when agent completes a step.
    /// Disk writes are throttled to at most once per second to avoid main-thread stalls.
    /// </summary>
    private float _lastStepSaveTime = -1f;
    private const float StepSaveInterval = 1f;

    static readonly Dictionary<string, float> s_recordedStepCompletionSec =
        new Dictionary<string, float>(64, StringComparer.OrdinalIgnoreCase);

    static string StepCompletionKey(string agentId, string stepId) =>
        string.IsNullOrEmpty(stepId) ? agentId : agentId + "|" + stepId;

    public static void RecordStepCompletionTime(string agentId, string stepId, float completionTimeSec)
    {
        if (string.IsNullOrEmpty(agentId) || completionTimeSec < 0f) return;
        s_recordedStepCompletionSec[StepCompletionKey(agentId, stepId)] = completionTimeSec;
    }

    public static bool TryGetRecordedStepCompletionTime(string agentId, string stepId, out float completionTimeSec)
    {
        return s_recordedStepCompletionSec.TryGetValue(StepCompletionKey(agentId, stepId), out completionTimeSec);
    }

    public void OnStepCompleted(string agentId)
    {
        OnStepCompleted(agentId, null, -1f);
    }

    public void OnStepCompleted(string agentId, string stepId, float completionTimeSec)
    {
        if (completionTimeSec >= 0f)
            RecordStepCompletionTime(agentId, stepId, completionTimeSec);

        UpdateAgentResults(agentId);

        if (!ShouldWriteHeavyResultsToDisk())
            return;

        float now = Time.unscaledTime;
        if (now - _lastStepSaveTime >= StepSaveInterval)
        {
            _lastStepSaveTime = now;
            QueueDeferredDiskWrite(includeFinalResults: false);
        }
    }
    
    /// <summary>
    /// Called when agent learns a skill
    /// </summary>
    public void OnSkillLearned(string agentId)
    {
        UpdateAgentResults(agentId);
        SaveTrainingResults();
        
        // CRITICAL: Generate final results on every skill learned (like training_status.json)
        GenerateFinalGameResults();
    }
    
    /// <summary>
    /// Called when episode ends
    /// </summary>
    public void OnEpisodeEnd(string agentId, float totalReward, int steps)
    {
        if (trainingResults == null)
        {
            InitializeComponent();
        }
        
        if (trainingResults != null)
        {
            AgentTrainingResults agentResult;
            if (trainingResults.agentResults.ContainsKey(agentId))
            {
                agentResult = trainingResults.agentResults[agentId];
            }
            else
            {
                agentResult = new AgentTrainingResults();
                agentResult.agentId = agentId;
                trainingResults.agentResults[agentId] = agentResult;
            }
            
            agentResult.episodes++;
            agentResult.totalReward += totalReward;
            
            UpdateAgentResults(agentId);
            SaveTrainingResults();
            
            Debug.Log($"📊 MLTrainingResultsWriter: Episode ended for {agentId} - Reward: {totalReward:F2}, Episodes: {agentResult.episodes}");

            // Keep final_game_results.json in sync with ONNX run folder when an episode completes
            GenerateFinalGameResultsSynchronous();
        }
    }
    
    /// <summary>
    /// Generate final game results JSON at end of training/game
    /// This should be called when training completes (similar to when ONNX files are generated)
    /// </summary>
    public void GenerateFinalGameResults()
    {
        if (IsInferencePlayback())
            return;

        try
        {
            // CRITICAL: Ensure component is initialized before generating
            if (trainingResults == null)
            {
                InitializeComponent();
            }
            RefreshTrainingDependencies();
            EnsureResultsMatchCurrentScene();

            // Update all agent results first (do not mutate totalReward with += EpisodeTotalReward here —
            // that ran every auto-save and inflated rewards; export adds current episode once below.)
            if (skillSystem != null)
            {
                var allAgents = skillSystem.GetAllAgentProfiles();
                foreach (var kvp in allAgents)
                    UpdateAgentResults(kvp.Key);
            }
            
            // Create comprehensive final results
            FinalGameResults finalResults = new FinalGameResults();
            finalResults.timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            finalResults.sceneId = trainingResults.sceneId;
            finalResults.mlAgentsRunId = GetCurrentRunId();
            finalResults.resultsRunLogsRelativePath = $"results/{finalResults.mlAgentsRunId}/run_logs/";
            finalResults.totalAgents = trainingResults.agentResults.Count;
            
            // Process each agent's results
            foreach (var kvp in trainingResults.agentResults)
            {
                string agentId = kvp.Key;
                AgentTrainingResults agentResult = kvp.Value;
                
                FinalAgentGameResult agentFinal = new FinalAgentGameResult();
                agentFinal.agentId = agentId;
                
                // Get behavior name from BSGMLAgent
                BSGMLAgent mlAgent = FindBSGMLAgent(agentId);
                if (mlAgent != null && !string.IsNullOrEmpty(mlAgent.behaviorName))
                    agentFinal.behaviorName = mlAgent.behaviorName;
                else
                    agentFinal.behaviorName = MLAgentAttacher.GetBehaviorNameForAgent(agentId);
                
                // Get role from skill system
                if (skillSystem != null)
                {
                    var agent = skillSystem.GetAgentProfile(agentId);
                    if (agent != null && agent.role != null)
                    {
                        agentFinal.role = agent.role;
                    }
                }
                
                agentFinal.skillLevel = agentResult.skillLevel;
                agentFinal.desireLevel = agentResult.desireLevel;
                agentFinal.skillPercentage = agentResult.desireLevel > 0 
                    ? (agentResult.skillLevel / agentResult.desireLevel) * 100f 
                    : 0f;
                agentFinal.isCompleted = agentResult.isCompleted;
                agentFinal.learnedSkillsCount = agentResult.learnedSkills.Count;
                agentFinal.learnedSkillsNames = new List<string>();
                
                // Extract skill names from learned skills list
                foreach (var skillStr in agentResult.learnedSkills)
                {
                    // Format: "Skill Name (ONET_CODE)"
                    int parenIndex = skillStr.IndexOf(" (");
                    if (parenIndex > 0)
                    {
                        agentFinal.learnedSkillsNames.Add(skillStr.Substring(0, parenIndex));
                    }
                    else
                    {
                        agentFinal.learnedSkillsNames.Add(skillStr);
                    }
                }
                
                agentFinal.episodes = agentResult.episodes;
                // Completed episodes (from OnEpisodeEnd) + reward accumulated so far in the active episode
                {
                    agentFinal.totalReward = agentResult.totalReward + GetLiveEpisodeTotalReward(agentId);
                }
                agentFinal.completedSteps = agentResult.completedSteps;
                agentFinal.totalSteps = agentResult.totalSteps;
                
                // Add step progress details - CRITICAL: Include ALL action sequence details
                agentFinal.stepProgress = new List<FinalStepProgress>();
                foreach (var step in agentResult.stepProgress)
                {
                    FinalStepProgress finalStep = new FinalStepProgress();
                    finalStep.stepId = step.stepId;
                    finalStep.stepOrder = step.stepOrder;
                    finalStep.actionType = step.actionType;
                    finalStep.preposition = step.preposition ?? "";
                    finalStep.targetObjectId = step.targetObjectId;
                    finalStep.targetObjectName = step.targetObjectName ?? "";
                    finalStep.description = step.description ?? "";
                    finalStep.expectedDuration = step.expectedDuration;
                    finalStep.isCompleted = step.isCompleted;
                    finalStep.completionTime = step.completionTime;
                    // Physical object state transition (before→after) from the operating-paragraph reference.
                    if (BSG.OperatingParagraph.OperatingParagraphCatalog.EnsureLoaded() &&
                        BSG.OperatingParagraph.OperatingParagraphCatalog.ExpectedByStepId.TryGetValue(step.stepId, out var opAction) && opAction != null)
                    {
                        // stateBefore is always known; stateAfter is only reached once the step COMPLETES
                        // successfully — leave it null until then so the result never claims an un-done outcome.
                        finalStep.stateBefore = opAction.state_before;
                        finalStep.stateAfter = step.isCompleted ? opAction.state_after : null;
                    }
                    finalStep.requiredSkillCodes = new List<string>(step.requiredSkillCodes);
                    finalStep.skillsLearned = new List<string>(step.skillsLearned);
                    agentFinal.stepProgress.Add(finalStep);
                }

                // Add cognitive sequence progress details.
                var cognitiveSequence = ResolveCognitiveSequence(agentId);
                if (cognitiveSequence != null && cognitiveSequence.actionSequence != null)
                {
                    agentFinal.cognitiveTotalSteps = cognitiveSequence.actionSequence.Count;
                    agentFinal.cognitiveCompletedSteps = cognitiveSequence.actionSequence.Count(s => s.isStepCompleted);
                    agentFinal.cognitiveSequenceCompleted = agentFinal.cognitiveTotalSteps > 0 &&
                                                           agentFinal.cognitiveCompletedSteps >= agentFinal.cognitiveTotalSteps;
                    string runtimeJsonPath = Path.Combine(
                        Application.dataPath,
                        "JsonFile",
                        sequenceManager != null ? sequenceManager.GetRuntimeJsonFileName() : "basicUi_ml.json");
                    foreach (var cStep in cognitiveSequence.actionSequence)
                    {
                        FinalStepProgress cognitiveFinalStep = new FinalStepProgress();
                        cognitiveFinalStep.stepId = cStep.stepId;
                        cognitiveFinalStep.stepOrder = cStep.stepOrder;
                        cognitiveFinalStep.actionType = cStep.actionType;
                        cognitiveFinalStep.preposition = cStep.preposition ?? "";
                        cognitiveFinalStep.targetObjectId = cStep.targetObjectId;
                        cognitiveFinalStep.targetObjectName = cStep.targetObjectName ?? "";
                        cognitiveFinalStep.description = cStep.description ?? "";
                        cognitiveFinalStep.currentCognitiveState = cStep.currentCognitiveState ?? "";
                        cognitiveFinalStep.expectedDuration = cStep.expectedDuration;
                        cognitiveFinalStep.isCompleted = cStep.isStepCompleted;
                        cognitiveFinalStep.completionTime = cStep.isStepCompleted ? cStep.expectedDuration : 0f;
                        if (cStep.requiredSkills != null)
                        {
                            foreach (var req in cStep.requiredSkills)
                            {
                                cognitiveFinalStep.requiredSkillCodes.Add(req.onetSkillCode);
                            }
                        }
                        if (cStep.learnSkills != null)
                        {
                            foreach (var skill in cStep.learnSkills)
                            {
                                cognitiveFinalStep.skillsLearned.Add(skill.skillName);
                            }
                        }

                        // goalBufferStack: only the Goal Buffer cognitive step has a stack in JSON; merge disk structure with runtime flags when needed.
                        GoalBufferContract structuralGb = null;
                        GoalBufferStackLayer[] runtimeStack = cStep.goalBufferContract?.stack;
                        if (runtimeStack != null && runtimeStack.Length > 0)
                            structuralGb = cStep.goalBufferContract;
                        else if (!string.IsNullOrEmpty(cStep.stepId))
                        {
                            GoalBufferContractParser.TryReadGoalBufferContractForStepFromDisk(
                                runtimeJsonPath, agentId, cStep.stepId, out GoalBufferContract fromDisk, out _);
                            structuralGb = fromDisk;
                        }

                        if (structuralGb?.stack != null && structuralGb.stack.Length > 0)
                        {
                            float resolvedStepDesire = cStep.resolvedGoalBufferDesireLevel;
                            string resolvedStepDesireSource = cStep.resolvedGoalBufferDesireSource ?? "";
                            if ((resolvedStepDesire <= 0f || string.IsNullOrWhiteSpace(resolvedStepDesireSource))
                                && GoalBufferDesireUtility.TryResolveDesire(structuralGb, out float fallbackStepDesire, out string fallbackStepSource))
                            {
                                resolvedStepDesire = fallbackStepDesire;
                                resolvedStepDesireSource = fallbackStepSource;
                            }
                            cognitiveFinalStep.goalBufferResolvedDesireLevel = resolvedStepDesire;
                            cognitiveFinalStep.goalBufferResolvedDesireSource = resolvedStepDesireSource;

                            int zoneIdx = ResolveZoneIndexForAgent(agentId);
                            ZoneDeclarativeMemory zoneMem = zoneIdx >= 0 ? ZoneDeclarativeMemory.ForZone(zoneIdx) : null;

                            for (int gi = 0; gi < structuralGb.stack.Length; gi++)
                            {
                                GoalBufferStackLayer d = structuralGb.stack[gi];
                                if (d == null) continue;
                                GoalBufferStackLayer m = (runtimeStack != null && gi < runtimeStack.Length)
                                    ? runtimeStack[gi]
                                    : null;

                                string position = !string.IsNullOrEmpty(m?.position) ? m.position : (d.position ?? "");
                                string type = !string.IsNullOrEmpty(m?.type) ? m.type : (d.type ?? "");
                                string rawValue = !string.IsNullOrEmpty(m?.value) ? m.value : (d.value ?? "");
                                bool isActive = m != null ? m.isActive : d.isActive;
                                bool isCompleted = m != null ? m.isCompleted : d.isCompleted;
                                int retryAttempt = m != null ? m.retryAttempt : d.retryAttempt;
                                float layerDesire = m != null && m.desireLevel > 0f ? m.desireLevel : d.desireLevel;

                                string preview = rawValue;
                                if (preview.Length > 240) preview = preview.Substring(0, 240) + "…";

                                string declPreview = "";
                                bool hasDecl = false;
                                if (zoneMem != null && !string.IsNullOrEmpty(type) &&
                                    zoneMem.TryGet(type, out string slotVal) && !string.IsNullOrWhiteSpace(slotVal))
                                {
                                    hasDecl = true;
                                    declPreview = slotVal;
                                    if (declPreview.Length > 240) declPreview = declPreview.Substring(0, 240) + "…";
                                }

                                cognitiveFinalStep.goalBufferStack.Add(new FinalGoalBufferStackLayerProgress
                                {
                                    position = position,
                                    type = type,
                                    desireLevel = layerDesire,
                                    isActive = isActive,
                                    isCompleted = isCompleted,
                                    retryAttempt = retryAttempt,
                                    storedInDeclarativeMemory = hasDecl || isCompleted,
                                    extractedValuePreview = preview,
                                    declarativeMemoryValuePreview = declPreview
                                });
                            }
                        }

                        agentFinal.cognitiveStepProgress.Add(cognitiveFinalStep);
                    }
                }
                else
                {
                    agentFinal.cognitiveTotalSteps = 0;
                    agentFinal.cognitiveCompletedSteps = 0;
                    agentFinal.cognitiveSequenceCompleted = false;
                }
                
                finalResults.agents.Add(agentFinal);
            }
            
            // Convert to JSON
            string json = ConvertFinalResultsToJSON(finalResults);
            
            // Save to results folder (same location as ONNX files)
            // Use dynamic run-id detection to ensure correct location
            string runId = GetCurrentRunId();
            string resultsPath = Path.Combine(Application.dataPath, "..", "results", runId, "run_logs", "final_game_results.json");
            
            // Normalize path to handle relative paths correctly
            resultsPath = Path.GetFullPath(resultsPath);
            
            // Ensure directory exists
            string directory = Path.GetDirectoryName(resultsPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                Debug.Log($"📁 MLTrainingResultsWriter: Created directory: {directory}");
            }
            
            // Write file with error handling and ensure it's flushed/synced
            try
            {
                // Do not gate on activeInHierarchy — during OnApplicationQuit / shutdown the hierarchy
                // can be inactive while ONNX exports already landed; we still must write run_logs JSON.

                // Use FileStream with flush to ensure file is written immediately
                using (FileStream fs = new FileStream(resultsPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    using (StreamWriter sw = new StreamWriter(fs, Encoding.UTF8))
                    {
                        sw.Write(json);
                        sw.Flush(); // Force flush to disk
                    }
                    // Only flush if stream is still valid
                    if (fs != null && fs.CanWrite)
                    {
                        fs.Flush(true); // Flush file system buffers to ensure data is written
                    }
                }
                Debug.Log($"💾 MLTrainingResultsWriter: Wrote {json.Length} characters to file (flushed to disk)");
            }
            catch (ObjectDisposedException)
            {
                // Silently ignore disposal errors during shutdown
                Debug.LogWarning("⚠️ MLTrainingResultsWriter: Object disposed during write (normal during shutdown)");
            }
            catch (Exception writeEx)
            {
                Debug.LogError($"❌ MLTrainingResultsWriter: Failed to write file: {writeEx.Message}");
                // Fallback to simple write if stream fails (only if not quitting)
                if (!Application.isPlaying || Application.isEditor)
                {
                    try
                    {
                        File.WriteAllText(resultsPath, json);
                        Debug.Log($"💾 MLTrainingResultsWriter: Fallback write successful");
                    }
                    catch (Exception fallbackEx)
                    {
                        // Don't log error if application is quitting
                        if (Application.isPlaying)
                        {
                            Debug.LogError($"❌ MLTrainingResultsWriter: Fallback write also failed: {fallbackEx.Message}");
                        }
                    }
                }
            }
            
            // Verify file was created
            if (File.Exists(resultsPath))
            {
                long fileSize = new FileInfo(resultsPath).Length;
                Debug.Log($"✅ MLTrainingResultsWriter: Generated final game results to {resultsPath}");
                Debug.Log($"📊 File size: {fileSize} bytes");
                Debug.Log($"📊 Final Results: {finalResults.totalAgents} agents, {finalResults.agents.Count(a => a.isCompleted)} completed");
                
                // Log absolute path for easy verification
                Debug.Log($"📂 Absolute path: {Path.GetFullPath(resultsPath)}");
            }
            else
            {
                Debug.LogError($"❌ MLTrainingResultsWriter: File was not created! Expected path: {resultsPath}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ MLTrainingResultsWriter: Error generating final game results: {e.Message}");
            Debug.LogError($"❌ Stack trace: {e.StackTrace}");
        }
        
        // CRITICAL: Also generate ML-specific action sequence results file
        GenerateMLActionSequenceResults();
    }
    
    /// <summary>
    /// Generate ML-specific action sequence results file
    /// Focuses on showing each agent's action sequence execution with ML performance metrics
    /// Generated when game ends (similar to ONNX files and final_game_results.json)
    /// </summary>
    public void GenerateMLActionSequenceResults()
    {
        try
        {
            // Ensure component is initialized
            if (trainingResults == null)
            {
                InitializeComponent();
            }
            
            if (sequenceManager == null || skillSystem == null)
            {
                sequenceManager = FindObjectOfType<AgentSequenceManager>();
                skillSystem = FindObjectOfType<SkillBasedActionSystem>();
            }
            
            if (sequenceManager == null || skillSystem == null)
            {
                Debug.LogWarning("⚠️ MLTrainingResultsWriter: Cannot generate ML action sequence results - missing dependencies");
                return;
            }
            
            // Create ML-specific results structure
            StringBuilder sb = new StringBuilder();
            string mlRunId = GetCurrentRunId();
            sb.Append("{\n");
            sb.Append($"    \"timestamp\": \"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}\",\n");
            sb.Append($"    \"sceneId\": \"{trainingResults.sceneId}\",\n");
            sb.Append($"    \"mlAgentsRunId\": \"{EscapeJsonString(mlRunId)}\",\n");
            sb.Append($"    \"type\": \"ML_Action_Sequence_Results\",\n");
            sb.Append($"    \"description\": \"Machine Learning action sequence execution results for each agent\",\n");
            sb.Append($"    \"totalAgents\": {trainingResults.agentResults.Count},\n");
            sb.Append("    \"agents\": [\n");
            
            int agentIndex = 0;
            foreach (var kvp in trainingResults.agentResults)
            {
                string agentId = kvp.Key;
                AgentTrainingResults agentResult = kvp.Value;
                
                if (agentIndex > 0) sb.Append(",\n");
                
                var sequence = sequenceManager.GetSequence(agentId);
                var agent = skillSystem.GetAgentProfile(agentId);
                BSGMLAgent mlAgent = FindBSGMLAgent(agentId);
                
                // Get behavior name
                string behaviorName = agentId;
                if (mlAgent != null && !string.IsNullOrEmpty(mlAgent.behaviorName))
                {
                    behaviorName = mlAgent.behaviorName;
                }
                else
                    behaviorName = MLAgentAttacher.GetBehaviorNameForAgent(agentId);
                
                sb.Append("        {\n");
                sb.Append($"            \"agentId\": \"{agentId}\",\n");
                sb.Append($"            \"behaviorName\": \"{behaviorName}\",\n");
                if (agent != null && !string.IsNullOrEmpty(agent.role))
                {
                    sb.Append($"            \"role\": \"{agent.role}\",\n");
                }
                
                // ML Performance Metrics
                sb.Append("            \"mlPerformance\": {\n");
                sb.Append($"                \"totalEpisodes\": {agentResult.episodes},\n");
                sb.Append($"                \"totalReward\": {agentResult.totalReward:F2},\n");
                sb.Append($"                \"averageRewardPerEpisode\": {(agentResult.episodes > 0 ? agentResult.totalReward / agentResult.episodes : 0f):F2},\n");
                sb.Append($"                \"completedSteps\": {agentResult.completedSteps},\n");
                sb.Append($"                \"totalSteps\": {agentResult.totalSteps},\n");
                sb.Append($"                \"completionPercentage\": {(agentResult.totalSteps > 0 ? (agentResult.completedSteps * 100f / agentResult.totalSteps) : 0f):F2},\n");
                sb.Append($"                \"sequenceCompleted\": {agentResult.isCompleted.ToString().ToLower()},\n");
                sb.Append($"                \"currentStepIndex\": {(sequence != null ? sequence.currentStepIndex : -1)}\n");
                sb.Append("            },\n");
                
                // Skills Summary
                sb.Append("            \"skillsSummary\": {\n");
                sb.Append($"                \"skillLevel\": {agentResult.skillLevel:F1},\n");
                sb.Append($"                \"desireLevel\": {agentResult.desireLevel:F1},\n");
                sb.Append($"                \"skillPercentage\": {(agentResult.desireLevel > 0 ? (agentResult.skillLevel / agentResult.desireLevel * 100f) : 0f):F2},\n");
                sb.Append($"                \"learnedSkillsCount\": {agentResult.learnedSkills.Count},\n");
                sb.Append("                \"learnedSkills\": [");
                for (int i = 0; i < agentResult.learnedSkills.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append($"\"{agentResult.learnedSkills[i]}\"");
                }
                sb.Append("]\n");
                sb.Append("            },\n");
                
                // Action Sequence Execution Details
                sb.Append("            \"actionSequence\": [\n");
                
                if (sequence != null && sequence.actionSequence != null)
                {
                    for (int stepIdx = 0; stepIdx < sequence.actionSequence.Count; stepIdx++)
                    {
                        var step = sequence.actionSequence[stepIdx];
                        StepProgress stepProgress = stepIdx < agentResult.stepProgress.Count 
                            ? agentResult.stepProgress[stepIdx] 
                            : null;
                        
                        if (stepIdx > 0) sb.Append(",\n");
                        
                        sb.Append("                {\n");
                        sb.Append($"                    \"stepOrder\": {step.stepOrder},\n");
                        sb.Append($"                    \"stepId\": \"{step.stepId}\",\n");
                        sb.Append($"                    \"actionType\": \"{step.actionType}\",\n");
                        sb.Append($"                    \"description\": \"{step.description ?? ""}\",\n");
                        sb.Append($"                    \"target\": {{\n");
                        sb.Append($"                        \"id\": \"{step.targetObjectId}\",\n");
                        sb.Append($"                        \"name\": \"{step.targetObjectName ?? ""}\",\n");
                        sb.Append($"                        \"preposition\": \"{step.preposition ?? ""}\"\n");
                        sb.Append($"                    }},\n");
                        sb.Append($"                    \"expectedDuration\": {step.expectedDuration:F2},\n");
                        
                        // Execution Status
                        bool isCompleted = stepProgress != null ? stepProgress.isCompleted : step.isStepCompleted;
                        sb.Append($"                    \"executionStatus\": {{\n");
                        sb.Append($"                        \"isCompleted\": {isCompleted.ToString().ToLower()},\n");
                        sb.Append($"                        \"completionTime\": {(stepProgress != null ? stepProgress.completionTime : 0f):F2},\n");
                        sb.Append($"                        \"timeEfficiency\": {(isCompleted && step.expectedDuration > 0 ? ((stepProgress != null ? stepProgress.completionTime : step.expectedDuration) / step.expectedDuration * 100f) : 0f):F2}\n");
                        sb.Append($"                    }},\n");
                        
                        // Skills Information
                        sb.Append($"                    \"skills\": {{\n");
                        sb.Append("                        \"required\": [");
                        if (step.requiredSkills != null && step.requiredSkills.Length > 0)
                        {
                            for (int i = 0; i < step.requiredSkills.Length; i++)
                            {
                                if (i > 0) sb.Append(", ");
                                sb.Append($"\"{step.requiredSkills[i].onetSkillCode}\"");
                            }
                        }
                        sb.Append("],\n");
                        
                        sb.Append("                        \"learned\": [");
                        List<string> learnedInStep = new List<string>();
                        if (step.learnSkills != null && step.learnSkills.Length > 0)
                        {
                            foreach (var skill in step.learnSkills)
                            {
                                learnedInStep.Add(skill.skillName);
                            }
                        }
                        else if (stepProgress != null && stepProgress.skillsLearned != null && stepProgress.skillsLearned.Count > 0)
                        {
                            learnedInStep.AddRange(stepProgress.skillsLearned);
                        }
                        
                        for (int i = 0; i < learnedInStep.Count; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            sb.Append($"\"{learnedInStep[i]}\"");
                        }
                        sb.Append("]\n");
                        sb.Append($"                    }}\n");
                        
                        sb.Append("                }");
                    }
                }
                else
                {
                    // Fallback to stepProgress if sequence not available
                    for (int stepIdx = 0; stepIdx < agentResult.stepProgress.Count; stepIdx++)
                    {
                        var stepProgress = agentResult.stepProgress[stepIdx];
                        if (stepIdx > 0) sb.Append(",\n");
                        
                        sb.Append("                {\n");
                        sb.Append($"                    \"stepOrder\": {stepProgress.stepOrder},\n");
                        sb.Append($"                    \"stepId\": \"{stepProgress.stepId}\",\n");
                        sb.Append($"                    \"actionType\": \"{stepProgress.actionType}\",\n");
                        sb.Append($"                    \"description\": \"{stepProgress.description ?? ""}\",\n");
                        sb.Append($"                    \"target\": {{\n");
                        sb.Append($"                        \"id\": \"{stepProgress.targetObjectId}\",\n");
                        sb.Append($"                        \"name\": \"{stepProgress.targetObjectName ?? ""}\",\n");
                        sb.Append($"                        \"preposition\": \"{stepProgress.preposition ?? ""}\"\n");
                        sb.Append($"                    }},\n");
                        sb.Append($"                    \"expectedDuration\": {stepProgress.expectedDuration:F2},\n");
                        sb.Append($"                    \"executionStatus\": {{\n");
                        sb.Append($"                        \"isCompleted\": {stepProgress.isCompleted.ToString().ToLower()},\n");
                        sb.Append($"                        \"completionTime\": {stepProgress.completionTime:F2}\n");
                        sb.Append($"                    }},\n");
                        sb.Append($"                    \"skills\": {{\n");
                        sb.Append("                        \"required\": [");
                        for (int i = 0; i < stepProgress.requiredSkillCodes.Count; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            sb.Append($"\"{stepProgress.requiredSkillCodes[i]}\"");
                        }
                        sb.Append("],\n");
                        sb.Append("                        \"learned\": [");
                        for (int i = 0; i < stepProgress.skillsLearned.Count; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            sb.Append($"\"{stepProgress.skillsLearned[i]}\"");
                        }
                        sb.Append("]\n");
                        sb.Append($"                    }}\n");
                        sb.Append("                }");
                    }
                }
                
                sb.Append("\n            ]\n");
                sb.Append("        }");
                
                agentIndex++;
            }
            
            sb.Append("\n    ]\n");
            sb.Append("}\n");
            
            string json = sb.ToString();
            
            // Save to results folder (same location as ONNX files)
            string runId = GetCurrentRunId();
            string resultsPath = Path.Combine(Application.dataPath, "..", "results", runId, "run_logs", "ml_action_sequence_results.json");
            resultsPath = Path.GetFullPath(resultsPath);
            
            // Ensure directory exists
            string directory = Path.GetDirectoryName(resultsPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            // Write file with flush
            try
            {
                using (FileStream fs = new FileStream(resultsPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    using (StreamWriter sw = new StreamWriter(fs, Encoding.UTF8))
                    {
                        sw.Write(json);
                        sw.Flush();
                    }
                    // Only flush if stream is still valid
                    if (fs != null && fs.CanWrite)
                    {
                        fs.Flush(true);
                    }
                }
                Debug.Log($"💾 MLTrainingResultsWriter: Generated ML action sequence results to {resultsPath}");
                
                if (File.Exists(resultsPath))
                {
                    long fileSize = new FileInfo(resultsPath).Length;
                    Debug.Log($"📊 ML Action Sequence Results file size: {fileSize} bytes");
                }
            }
            catch (ObjectDisposedException)
            {
                // Silently ignore disposal errors during shutdown
                Debug.LogWarning("⚠️ MLTrainingResultsWriter: Object disposed during ML action sequence write (normal during shutdown)");
            }
            catch (Exception writeEx)
            {
                Debug.LogError($"❌ MLTrainingResultsWriter: Failed to write ML action sequence results: {writeEx.Message}");
                // Fallback (only if not quitting)
                if (Application.isPlaying || Application.isEditor)
                {
                    try
                    {
                        File.WriteAllText(resultsPath, json);
                        Debug.Log($"💾 MLTrainingResultsWriter: Fallback write successful for ML action sequence results");
                    }
                    catch (Exception fallbackEx)
                    {
                        // Don't log error if application is quitting
                        if (Application.isPlaying)
                        {
                            Debug.LogError($"❌ MLTrainingResultsWriter: Fallback write also failed: {fallbackEx.Message}");
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ MLTrainingResultsWriter: Error generating ML action sequence results: {e.Message}");
            Debug.LogError($"❌ Stack trace: {e.StackTrace}");
        }
    }
    
    /// <summary>
    /// Find BSGMLAgent component by agentId
    /// </summary>
    private BSGMLAgent FindBSGMLAgent(string agentId)
    {
        if (string.IsNullOrEmpty(agentId)) return null;

        BSGMLAgent registered = RagStepRewardBridge.TryGetRegisteredPhysicalAgent(agentId);
        if (registered != null)
            return registered;

        RefreshMlAgentCacheIfNeeded();
        foreach (var agent in _cachedMlAgents)
        {
            if (agent != null && string.Equals(agent.agentId, agentId, StringComparison.OrdinalIgnoreCase))
                return agent;
        }
        return null;
    }
    
    /// <summary>
    /// Check if agents are currently receiving actions from ML-Agents server
    /// Returns true if any agent has active episodeSteps (receiving actions)
    /// </summary>
    private bool CheckAgentsReceivingActions()
    {
        RefreshMlAgentCacheIfNeeded();
        if (_cachedMlAgents == null || _cachedMlAgents.Length == 0) return false;
        
        foreach (var agent in _cachedMlAgents)
        {
            if (agent != null && agent.episodeSteps > 0)
                return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Coroutine to ensure final results are written after disconnect
    /// This runs multiple times after disconnect detection to ensure file is written
    /// Similar to how ONNX files are generated when training stops (Ctrl+C)
    /// </summary>
    private IEnumerator EnsureFinalResultsWrittenAfterDisconnect()
    {
        Debug.Log("🔄 MLTrainingResultsWriter: Starting coroutine to ensure final results are written after disconnect...");
        
        // Generate immediately (synchronous write)
        GenerateFinalGameResultsSynchronous();
        yield return new WaitForSeconds(0.1f);
        
        // Generate again after 0.1 seconds to ensure file is written (synchronous)
        GenerateFinalGameResultsSynchronous();
        yield return new WaitForSeconds(0.2f);
        
        // Generate one more time after 0.3 seconds as final safety (synchronous)
        GenerateFinalGameResultsSynchronous();
        Debug.Log("✅ MLTrainingResultsWriter: Completed final results generation after disconnect (similar to ONNX file generation)");
    }
    
    /// <summary>
    /// Synchronous version of GenerateFinalGameResults that writes immediately without waiting
    /// CRITICAL: Use this when Unity might be closing (OnApplicationQuit, OnDestroy, etc.)
    /// This ensures the file is written even if Unity closes quickly
    /// Also generates ML action sequence results synchronously
    /// </summary>
    private void GenerateFinalGameResultsSynchronous()
    {
        try
        {
            // Call the regular method but ensure synchronous write
            // This also calls GenerateMLActionSequenceResults() automatically
            GenerateFinalGameResults();
            
            // Force file system flush by checking if files exist (triggers flush)
            string runId = GetCurrentRunId();
            string resultsPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "results", runId, "run_logs", "final_game_results.json"));
            string mlResultsPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "results", runId, "run_logs", "ml_action_sequence_results.json"));
            
            if (File.Exists(resultsPath))
            {
                // File exists - verify it's readable (forces flush)
                try
                {
                    var fileInfo = new FileInfo(resultsPath);
                    long size = fileInfo.Length;
                    Debug.Log($"✅ Synchronous write confirmed: final_game_results.json size = {size} bytes");
                }
                catch { }
            }
            
            if (File.Exists(mlResultsPath))
            {
                // ML action sequence results file exists - verify it's readable
                try
                {
                    var fileInfo = new FileInfo(mlResultsPath);
                    long size = fileInfo.Length;
                    Debug.Log($"✅ Synchronous write confirmed: ml_action_sequence_results.json size = {size} bytes");
                }
                catch { }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ MLTrainingResultsWriter: Error in synchronous generation: {e.Message}");
            Debug.LogError($"❌ Stack trace: {e.StackTrace}");
        }
    }
    
    /// <summary>
    /// Convert FinalGameResults to JSON
    /// </summary>
    private static string EscapeJsonString(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    private string ConvertFinalResultsToJSON(FinalGameResults results)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"    \"timestamp\": \"{results.timestamp}\",\n");
        sb.Append($"    \"sceneId\": \"{results.sceneId}\",\n");
        sb.Append($"    \"mlAgentsRunId\": \"{EscapeJsonString(results.mlAgentsRunId ?? "")}\",\n");
        sb.Append($"    \"resultsRunLogsRelativePath\": \"{EscapeJsonString(results.resultsRunLogsRelativePath ?? "")}\",\n");
        sb.Append($"    \"totalAgents\": {results.totalAgents},\n");
        sb.Append("    \"agents\": [\n");
        
        for (int i = 0; i < results.agents.Count; i++)
        {
            var agent = results.agents[i];
            sb.Append("        {\n");
            sb.Append($"            \"agentId\": \"{agent.agentId}\",\n");
            sb.Append($"            \"behaviorName\": \"{agent.behaviorName}\",\n");
            if (!string.IsNullOrEmpty(agent.role))
            {
                sb.Append($"            \"role\": \"{agent.role}\",\n");
            }
            sb.Append($"            \"skillLevel\": {agent.skillLevel:F1},\n");
            sb.Append($"            \"desireLevel\": {agent.desireLevel:F1},\n");
            sb.Append($"            \"skillPercentage\": {agent.skillPercentage:F2},\n");
            sb.Append($"            \"isCompleted\": {agent.isCompleted.ToString().ToLower()},\n");
            sb.Append($"            \"learnedSkillsCount\": {agent.learnedSkillsCount},\n");
            sb.Append($"            \"learnedSkillsNames\": [");
            
            for (int j = 0; j < agent.learnedSkillsNames.Count; j++)
            {
                if (j > 0) sb.Append(", ");
                sb.Append($"\"{agent.learnedSkillsNames[j]}\"");
            }
            sb.Append("],\n");
            
            sb.Append($"            \"episodes\": {agent.episodes},\n");
            sb.Append($"            \"totalReward\": {agent.totalReward:F2},\n");
            sb.Append($"            \"completedSteps\": {agent.completedSteps},\n");
            sb.Append($"            \"totalSteps\": {agent.totalSteps},\n");
            sb.Append($"            \"cognitiveSequenceCompleted\": {agent.cognitiveSequenceCompleted.ToString().ToLower()},\n");
            sb.Append($"            \"cognitiveCompletedSteps\": {agent.cognitiveCompletedSteps},\n");
            sb.Append($"            \"cognitiveTotalSteps\": {agent.cognitiveTotalSteps},\n");
            
            // Add step progress - CRITICAL: Include ALL action sequence details
            sb.Append("            \"stepProgress\": [\n");
            for (int j = 0; j < agent.stepProgress.Count; j++)
            {
                var step = agent.stepProgress[j];
                sb.Append("                {\n");
                sb.Append($"                    \"stepId\": \"{step.stepId}\",\n");
                sb.Append($"                    \"stepOrder\": {step.stepOrder},\n");
                sb.Append($"                    \"actionType\": \"{step.actionType}\",\n");
                if (!string.IsNullOrEmpty(step.preposition))
                {
                    sb.Append($"                    \"preposition\": \"{step.preposition}\",\n");
                }
                sb.Append($"                    \"targetObjectId\": \"{step.targetObjectId}\",\n");
                if (!string.IsNullOrEmpty(step.targetObjectName))
                {
                    sb.Append($"                    \"targetObjectName\": \"{step.targetObjectName}\",\n");
                }
                if (!string.IsNullOrEmpty(step.description))
                {
                    sb.Append($"                    \"description\": \"{step.description}\",\n");
                }
                sb.Append($"                    \"expectedDuration\": {step.expectedDuration:F2},\n");
                sb.Append($"                    \"isCompleted\": {step.isCompleted.ToString().ToLower()},\n");
                sb.Append($"                    \"completionTime\": {step.completionTime:F2},\n");
                if (!string.IsNullOrEmpty(step.stateBefore))
                {
                    sb.Append($"                    \"stateBefore\": \"{EscapeJsonString(step.stateBefore)}\",\n");
                    // stateAfter is null until the physical step completes successfully (see finalStep assignment).
                    string after = string.IsNullOrEmpty(step.stateAfter) ? "null" : $"\"{EscapeJsonString(step.stateAfter)}\"";
                    sb.Append($"                    \"stateAfter\": {after},\n");
                }

                // Required skill codes (O*NET codes)
                sb.Append("                    \"requiredSkillCodes\": [");
                for (int k = 0; k < step.requiredSkillCodes.Count; k++)
                {
                    if (k > 0) sb.Append(", ");
                    sb.Append($"\"{step.requiredSkillCodes[k]}\"");
                }
                sb.Append("],\n");
                
                // Skills learned in this step
                sb.Append("                    \"skillsLearned\": [");
                for (int k = 0; k < step.skillsLearned.Count; k++)
                {
                    if (k > 0) sb.Append(", ");
                    sb.Append($"\"{step.skillsLearned[k]}\"");
                }
                sb.Append("]\n");
                sb.Append("                }");
                if (j < agent.stepProgress.Count - 1) sb.Append(",");
                sb.Append("\n");
            }
            sb.Append("            ],\n");
            sb.Append("            \"cognitiveStepProgress\": [\n");
            for (int j = 0; j < agent.cognitiveStepProgress.Count; j++)
            {
                var cStep = agent.cognitiveStepProgress[j];
                sb.Append("                {\n");
                sb.Append($"                    \"stepId\": \"{cStep.stepId}\",\n");
                sb.Append($"                    \"stepOrder\": {cStep.stepOrder},\n");
                sb.Append($"                    \"actionType\": \"{cStep.actionType}\",\n");
                if (!string.IsNullOrEmpty(cStep.preposition))
                {
                    sb.Append($"                    \"preposition\": \"{cStep.preposition}\",\n");
                }
                sb.Append($"                    \"targetObjectId\": \"{cStep.targetObjectId}\",\n");
                if (!string.IsNullOrEmpty(cStep.targetObjectName))
                {
                    sb.Append($"                    \"targetObjectName\": \"{cStep.targetObjectName}\",\n");
                }
                if (!string.IsNullOrEmpty(cStep.description))
                {
                    sb.Append($"                    \"description\": \"{cStep.description}\",\n");
                }
                sb.Append($"                    \"expectedDuration\": {cStep.expectedDuration:F2},\n");
                sb.Append($"                    \"isCompleted\": {cStep.isCompleted.ToString().ToLower()},\n");
                sb.Append($"                    \"completionTime\": {cStep.completionTime:F2},\n");
                if (!string.IsNullOrEmpty(cStep.currentCognitiveState))
                {
                    sb.Append($"                    \"currentCognitiveState\": \"{EscapeJsonString(cStep.currentCognitiveState)}\",\n");
                }
                sb.Append("                    \"requiredSkillCodes\": [");
                for (int k = 0; k < cStep.requiredSkillCodes.Count; k++)
                {
                    if (k > 0) sb.Append(", ");
                    sb.Append($"\"{cStep.requiredSkillCodes[k]}\"");
                }
                sb.Append("],\n");
                sb.Append("                    \"skillsLearned\": [");
                for (int k = 0; k < cStep.skillsLearned.Count; k++)
                {
                    if (k > 0) sb.Append(", ");
                    sb.Append($"\"{EscapeJsonString(cStep.skillsLearned[k])}\"");
                }
                sb.Append("]");
                if (cStep.goalBufferResolvedDesireLevel > 0f)
                {
                    sb.Append($",\n                    \"goalBufferResolvedDesireLevel\": {cStep.goalBufferResolvedDesireLevel:F1}");
                    if (!string.IsNullOrWhiteSpace(cStep.goalBufferResolvedDesireSource))
                        sb.Append($",\n                    \"goalBufferResolvedDesireSource\": \"{EscapeJsonString(cStep.goalBufferResolvedDesireSource)}\"");
                }
                if (cStep.goalBufferStack != null && cStep.goalBufferStack.Count > 0)
                {
                    sb.Append(",\n                    \"goalBufferStack\": [\n");
                    for (int k = 0; k < cStep.goalBufferStack.Count; k++)
                    {
                        var gb = cStep.goalBufferStack[k];
                        sb.Append("                        {\n");
                        sb.Append($"                            \"position\": \"{EscapeJsonString(gb.position)}\",\n");
                        sb.Append($"                            \"type\": \"{EscapeJsonString(gb.type)}\",\n");
                        sb.Append($"                            \"desireLevel\": {gb.desireLevel:F1},\n");
                        sb.Append($"                            \"isActive\": {gb.isActive.ToString().ToLower()},\n");
                        sb.Append($"                            \"isCompleted\": {gb.isCompleted.ToString().ToLower()},\n");
                        sb.Append($"                            \"retryAttempt\": {gb.retryAttempt},\n");
                        sb.Append($"                            \"storedInDeclarativeMemory\": {gb.storedInDeclarativeMemory.ToString().ToLower()},\n");
                        sb.Append($"                            \"extractedValuePreview\": \"{EscapeJsonString(gb.extractedValuePreview)}\",\n");
                        sb.Append($"                            \"declarativeMemoryValuePreview\": \"{EscapeJsonString(gb.declarativeMemoryValuePreview)}\"\n");
                        sb.Append("                        }");
                        if (k < cStep.goalBufferStack.Count - 1) sb.Append(",");
                        sb.Append("\n");
                    }
                    sb.Append("                    ]");
                }
                sb.Append("\n                }");
                if (j < agent.cognitiveStepProgress.Count - 1) sb.Append(",");
                sb.Append("\n");
            }
            sb.Append("            ]\n");
            
            sb.Append("        }");
            if (i < results.agents.Count - 1) sb.Append(",");
            sb.Append("\n");
        }
        
        sb.Append("    ],\n");

        // Operating Paragraph: predicted (RAG reference) + actual (built from completed physical steps) +
        // comparison. Computed for whatever completed so far, so BOTH an ideal (all-physical-complete) and a
        // mid-run/partial end are stored + compared here (every write path routes through this method).
        sb.Append("    \"operatingParagraph\": ");
        sb.Append(BSG.OperatingParagraph.OperatingParagraphRuntime.BuildFinalResultsBlockJson(0, "    ").TrimStart());
        sb.Append(",\n");

        // Identity Statement: predicted (RAG reference) + actual (built from completed physical steps) +
        // comparison. Same partial-friendly contract as operatingParagraph — stored on every game end.
        sb.Append("    \"identityStatement\": ");
        sb.Append(BSG.IdentityStatement.IdentityStatementRuntime.BuildFinalResultsBlockJson(0, "    ").TrimStart());
        sb.Append("\n");

        sb.Append("}\n");

        return sb.ToString();
    }
}

/// <summary>
/// Final game results structure
/// </summary>
[System.Serializable]
public class FinalGameResults
{
    public string timestamp;
    public string sceneId;
    /// <summary>Same as mlagents-learn --run-id; export file lives under results/&lt;this&gt;/run_logs/.</summary>
    public string mlAgentsRunId;
    /// <summary>Human-readable relative path from project root (forward slashes).</summary>
    public string resultsRunLogsRelativePath;
    public int totalAgents;
    public List<FinalAgentGameResult> agents;
    
    public FinalGameResults()
    {
        agents = new List<FinalAgentGameResult>();
    }
}

/// <summary>
/// Final agent game result
/// </summary>
[System.Serializable]
public class FinalAgentGameResult
{
    public string agentId;
    public string behaviorName;
    public string role;
    public float skillLevel;
    public float desireLevel;
    public float skillPercentage;
    public bool isCompleted;
    public int learnedSkillsCount;
    public List<string> learnedSkillsNames;
    public int episodes;
    public float totalReward;
    public int completedSteps;
    public int totalSteps;
    public List<FinalStepProgress> stepProgress;
    public bool cognitiveSequenceCompleted;
    public int cognitiveCompletedSteps;
    public int cognitiveTotalSteps;
    public List<FinalStepProgress> cognitiveStepProgress;
    
    public FinalAgentGameResult()
    {
        learnedSkillsNames = new List<string>();
        stepProgress = new List<FinalStepProgress>();
        cognitiveStepProgress = new List<FinalStepProgress>();
    }
}

/// <summary>
/// Final step progress
/// </summary>
[System.Serializable]
public class FinalGoalBufferStackLayerProgress
{
    public string position;
    public string type;
    public float desireLevel;
    public bool isActive;
    public bool isCompleted;
    public int retryAttempt;
    public bool storedInDeclarativeMemory;
    public string extractedValuePreview;
    /// <summary>Text read from zone declarative dataSlots for this layer type (e.g. smart_key_result).</summary>
    public string declarativeMemoryValuePreview;
}

[System.Serializable]
public class FinalStepProgress
{
    public string stepId;
    public int stepOrder;
    public string actionType;
    public string preposition;
    public string targetObjectId;
    public string targetObjectName;
    public string description;
    public string currentCognitiveState;
    public string stateBefore;   // physical step: object state before (from operating paragraph)
    public string stateAfter;    // physical step: object state after
    public float expectedDuration;
    public bool isCompleted;
    public float completionTime;
    public List<string> skillsLearned;
    public List<string> requiredSkillCodes; // O*NET skill codes required for this step
    public float goalBufferResolvedDesireLevel;
    public string goalBufferResolvedDesireSource;
    public List<FinalGoalBufferStackLayerProgress> goalBufferStack;
    
    public FinalStepProgress()
    {
        skillsLearned = new List<string>();
        requiredSkillCodes = new List<string>();
        goalBufferStack = new List<FinalGoalBufferStackLayerProgress>();
    }
}


