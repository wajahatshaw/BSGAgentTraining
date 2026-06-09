using System.Collections;
using UnityEngine;

/// <summary>
/// Bridges Ronald-Johnson TaskManager milestones to BSG CognitivePhaseOrchestrator RAG steps.
/// Add to ML training / hybrid scenes only; no-op when orchestrator or TaskManager is absent.
/// </summary>
public class TaskRagBridge : MonoBehaviour
{
    [Header("Zone")]
    [SerializeField] private int zoneIndex = 0;

    [Header("RAG step mapping (basicUI_ml2.json)")]
    [Tooltip("Optional: completes st_2 physical barrier when the human accepts the motor task.")]
    [SerializeField] private string stepOnTaskAccept = "";
    [Tooltip("Completes st_2 physical band barrier after motor interaction.")]
    [SerializeField] private string stepOnMotorInteract = "t01_phy_s59";
    [Tooltip("Completes st_3 cognitive barrier after motor quiz.")]
    [SerializeField] private string stepOnMotorQuiz = "t01_cog_s63";

    [Header("ML bot")]
    [SerializeField] private bool autoAcceptTasks;
    [SerializeField] private float autoAcceptDelaySeconds = 1f;

    [Header("Score")]
    [SerializeField] private bool mapRagRewardsToSkillScore = true;
    [SerializeField] private int scorePerRagStep = 25;

    CognitivePhaseOrchestrator _orchestrator;
    BPT_MotorTask _motorTask;
    bool _subscribed;

    void OnEnable()
    {
        if (RagPhysicalAgentLocalMode.ShouldUseRagPhysicalAgentMode())
        {
            enabled = false;
            return;
        }

        Subscribe();
        if (autoAcceptTasks)
            StartCoroutine(AutoAcceptWhenTaskPopupVisible());
    }

    void OnDisable() => Unsubscribe();

    void Subscribe()
    {
        if (_subscribed) return;
        _subscribed = true;
        EventManager.OnChapterStartEvent += OnChapterStart;
        EventManager.AC_OnTaskAccpet += OnTaskAccepted;
        EventManager.AC_OnMotorTaskInteracted += OnMotorInteracted;
        EventManager.AC_OnMotorQuizAnswered += OnMotorQuizAnswered;
        EventManager.OnChapterEndEvent += OnChapterEnd;
    }

    void Unsubscribe()
    {
        if (!_subscribed) return;
        _subscribed = false;
        EventManager.OnChapterStartEvent -= OnChapterStart;
        EventManager.AC_OnTaskAccpet -= OnTaskAccepted;
        EventManager.AC_OnMotorTaskInteracted -= OnMotorInteracted;
        EventManager.AC_OnMotorQuizAnswered -= OnMotorQuizAnswered;
        EventManager.OnChapterEndEvent -= OnChapterEnd;
        if (_orchestrator != null)
            _orchestrator.OnAllStepsCompleted -= OnAllRagStepsCompleted;
    }

    void OnChapterStart()
    {
        EnsureOrchestrator();
        _motorTask = FindObjectOfType<BPT_MotorTask>();
    }

    void OnChapterEnd()
    {
        if (_orchestrator != null)
            _orchestrator.OnAllStepsCompleted -= OnAllRagStepsCompleted;
    }

    void OnTaskAccepted(int taskIndex)
    {
        EnsureOrchestrator();
        if (_orchestrator == null) return;

        _orchestrator.EnsureInitializedFromSceneAgents();
        NotifyRagStep(stepOnTaskAccept);
    }

    void OnMotorInteracted() => NotifyRagStep(stepOnMotorInteract);

    void OnMotorQuizAnswered(int answerIndex)
    {
        NotifyRagStep(stepOnMotorQuiz);
        if (_motorTask != null && !_motorTask.b_TaskDone)
            _motorTask.TaskComplete();
    }

    void NotifyRagStep(string stepId)
    {
        if (string.IsNullOrEmpty(stepId) || _orchestrator == null) return;
        if (_orchestrator.IsStepCompleted(stepId)) return;

        RagOrchestratorNetworkSync.CompleteExternalStep(zoneIndex, stepId);

        if (mapRagRewardsToSkillScore && SkillScoreManager.Instance != null)
            SkillScoreManager.Instance.AddScore(scorePerRagStep);
    }

    void EnsureOrchestrator()
    {
        if (_orchestrator != null) return;
        _orchestrator = CognitivePhaseOrchestrator.GetOrCreateForZone(zoneIndex);
        if (_orchestrator == null) return;
        _orchestrator.OnAllStepsCompleted -= OnAllRagStepsCompleted;
        _orchestrator.OnAllStepsCompleted += OnAllRagStepsCompleted;
    }

    void OnAllRagStepsCompleted(int completedZone)
    {
        if (completedZone != zoneIndex) return;
        Debug.Log("[TaskRagBridge] RAG zone complete — TaskManager chapter may proceed.");
    }

    IEnumerator AutoAcceptWhenTaskPopupVisible()
    {
        yield return new WaitForSeconds(autoAcceptDelaySeconds);
        if (!enabled || !autoAcceptTasks) yield break;
        AcceptCurrentTaskFromBridge();
    }

    /// <summary>Called from UI or ML bot to accept the current task popup.</summary>
    public void AcceptCurrentTaskFromBridge()
    {
        if (UIManager.Instance != null)
            UIManager.Instance.TaskAccept();
    }
}
