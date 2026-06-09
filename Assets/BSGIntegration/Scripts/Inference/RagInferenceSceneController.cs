using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

/// <summary>
/// ONNX inference scene (Phase 5). No Python. Hides mental/cognitive visuals; physical agents use exported ONNX.
/// </summary>
[DefaultExecutionOrder(-60)]
public class RagInferenceSceneController : MonoBehaviour
{
    public static bool IsInferenceSceneActive()
    {
        var ctrl = Object.FindObjectOfType<RagInferenceSceneController>();
        return ctrl != null && ctrl.isActiveAndEnabled;
    }

    [Header("ReplicaSceneSetup overrides (applied in Awake)")]
    public bool patchReplicaSceneSetup = true;
    public string jsonFileName = "basicUI_ml2.json";

    [Header("ONNX deployer")]
    public RagMlBrainDeployer brainDeployer;

    [Header("Visuals (guideline: hide renderers, keep GameObjects active)")]
    [FormerlySerializedAs("hideCognitiveBoxRenderers")]
    public bool hideCognitiveAndMentalVisuals = true;
    public bool disableTrainingSpeedCanvas = true;

    [Header("Timing")]
    [Min(0.25f)]
    public float pollIntervalSeconds = 0.5f;
    [Min(1f)]
    public float maxWaitForAgentsSeconds = 90f;

    [Header("Cognitive flow (Option 1)")]
    [Tooltip("Mental: invisible RagSequenceAgentMover (sequence + step signals). Physical unlock after leader cognitive (not st_0 barrier).")]
    public bool useInvisibleCognitiveScriptedWalk = true;

    [Header("Physical locomotion")]
    [Tooltip("When true (default), RagSequenceAgentMover walks P-agents like the training scene; ONNX drives tool/finger press only.")]
    public bool useScriptedPhysicalLocomotion = true;

    [Header("Debug")]
    public bool skipCognitiveOnnxWarmup;

    bool _applied;

    public static bool UseInvisibleCognitiveScriptedWalk()
    {
        var ctrl = Object.FindObjectOfType<RagInferenceSceneController>();
        return ctrl != null && ctrl.isActiveAndEnabled && ctrl.useInvisibleCognitiveScriptedWalk;
    }

    /// <summary>Option 1: do not unlock physical on early st_0 barrier — wait for zone leader cognitive sequence.</summary>
    public static bool DeferPhysicalUntilLeaderCognitiveDone()
    {
        return UseInvisibleCognitiveScriptedWalk();
    }

    public static bool UseScriptedPhysicalLocomotion()
    {
        var ctrl = Object.FindObjectOfType<RagInferenceSceneController>();
        return ctrl == null || !ctrl.isActiveAndEnabled || ctrl.useScriptedPhysicalLocomotion;
    }

    void Awake()
    {
        MLTrainingLogger.SuppressPeriodicSummary = true;

        foreach (var speedUi in Object.FindObjectsOfType<TrainingSpeedIndicator>(true))
        {
            if (speedUi != null)
                speedUi.enabled = false;
        }

        if (!patchReplicaSceneSetup) return;

        ReplicaSceneSetup setup = FindObjectOfType<ReplicaSceneSetup>();
        if (setup == null) return;

        setup.enableMlTrainingInRagMode = false;
        setup.enableMlTrainingForCognitiveAgents = false;
        setup.trainZonesMask = 0;
        setup.ragOnlyMode = true;
        if (!string.IsNullOrEmpty(jsonFileName))
            setup.jsonFileName = jsonFileName;

        Debug.Log("[RagInferenceScene] Training OFF — inference-only (no mlagents-learn).");
    }

    void OnDestroy()
    {
        MLTrainingLogger.SuppressPeriodicSummary = false;
    }

    void Start()
    {
        if (disableTrainingSpeedCanvas)
        {
            var canvas = GameObject.Find("TrainingSpeedCanvas");
            if (canvas != null) canvas.SetActive(false);
        }

        brainDeployer = RagInferenceMLBootstrap.EnsureDeployerInScene();
        if (brainDeployer != null)
            brainDeployer.applyOnStart = false;

        StartCoroutine(WaitForAgentsThenApplyInference());
    }

    /// <summary>Called when scene generation finishes (optional hook from SceneGenerator).</summary>
    public void OnSceneGenerated()
    {
        if (!_applied)
            StartCoroutine(ApplyInferencePipeline());
    }

    IEnumerator WaitForAgentsThenApplyInference()
    {
        float deadline = Time.unscaledTime + maxWaitForAgentsSeconds;
        while (Time.unscaledTime < deadline)
        {
            if (FindAgentsRoot() != null && Object.FindObjectsOfType<RagSequenceAgentMover>(true).Length >= 4)
                break;
            yield return new WaitForSeconds(pollIntervalSeconds);
        }

        yield return ApplyInferencePipeline();
    }

    IEnumerator ApplyInferencePipeline()
    {
        if (_applied) yield break;
        _applied = true;

        if (hideCognitiveAndMentalVisuals)
            RagInferenceVisuals.ApplyFullInferenceHide();

        DisableTrainingOnlySystems();

        brainDeployer = RagInferenceMLBootstrap.EnsureDeployerInScene();
        RagInferenceMLBootstrap.AttachInferenceAgents(brainDeployer, useScriptedPhysicalLocomotion);

        string physMode = useScriptedPhysicalLocomotion
            ? "scripted mover + ONNX interaction"
            : "ONNX locomotion";

        if (useInvisibleCognitiveScriptedWalk)
        {
            EnsureOrchestratorsInitialized();
            Debug.Log($"[RagInferenceScene] Option 1: invisible mental mover; physical {physMode} after leader completes cognitive sequence.");
        }
        else
        {
            FastForwardAllZonesCognitive();

            if (!skipCognitiveOnnxWarmup)
            {
                var coord = FindObjectOfType<RagCognitiveInferenceCoordinator>();
                if (coord == null)
                {
                    var go = new GameObject("RagCognitiveInferenceCoordinator");
                    coord = go.AddComponent<RagCognitiveInferenceCoordinator>();
                }
                yield return new WaitForSeconds(0.5f);
            }
        }

        if (hideCognitiveAndMentalVisuals)
            RagInferenceVisuals.ApplyFullInferenceHide();

        VerifyOnnxModelsAttached();
        if (hideCognitiveAndMentalVisuals)
            StartCoroutine(ReapplyHideForLateSpawns());

        yield return new WaitForSeconds(1f);
        RagInferenceMLBootstrap.ReassertOnnxBrains(brainDeployer);
        VerifyOnnxModelsAttached();

        string cogMode = useInvisibleCognitiveScriptedWalk
            ? "mental mover (invisible DAG) + ONNX"
            : "cognitive fast-forward";
        Debug.Log($"[RagInferenceScene] Inference ready in '{SceneManager.GetActiveScene().name}'. Physical: {physMode}; cognitive: {cogMode}.");
        yield return null;
    }

    IEnumerator ReapplyHideForLateSpawns()
    {
        for (int i = 0; i < 6; i++)
        {
            yield return new WaitForSeconds(0.5f);
            RagInferenceVisuals.ApplyFullInferenceHide();
        }
    }

    static GameObject FindAgentsRoot() => GameObject.Find("JSON_Generated_Agents");

    static void DisableTrainingOnlySystems()
    {
        if (UseInvisibleCognitiveScriptedWalk())
            return;

        foreach (var rt in Object.FindObjectsOfType<TemporalCognitionRuntime>(true))
        {
            if (rt != null)
                rt.enabled = false;
        }
    }

    static void EnsureOrchestratorsInitialized()
    {
        for (int z = 0; z < 4; z++)
        {
            CognitivePhaseOrchestrator orch = CognitivePhaseOrchestrator.GetOrCreateForZone(z);
            if (orch != null && !orch.IsInitialized && !orch.EnsureInitializedFromSceneAgents())
                Debug.LogWarning($"[RagInferenceScene] Zone {z}: orchestrator not initialized.");
        }
    }

    static void FastForwardAllZonesCognitive()
    {
        for (int z = 0; z < 4; z++)
        {
            CognitivePhaseOrchestrator orch = CognitivePhaseOrchestrator.GetOrCreateForZone(z);
            if (orch != null && !orch.IsInitialized && !orch.EnsureInitializedFromSceneAgents())
                Debug.LogWarning($"[RagInferenceScene] Zone {z}: orchestrator not initialized — physical DAG may not dispatch.");

            orch?.CompleteCognitivePhaseForInference();

            ZoneDeclarativeMemory mem = ZoneDeclarativeMemory.ForZone(z);
            if (mem != null)
                mem.cognitiveReady = true;

            foreach (var m in Object.FindObjectsOfType<RagSequenceAgentMover>(true))
            {
                if (m != null && m.isMentalAgent && m.zoneIndex == z)
                    m.SetCognitivePhaseCompleteForInference(true);
            }
        }

        UnlockPhysicalAgentsForInference();
    }

    static void UnlockPhysicalAgentsForInference()
    {
        foreach (var ml in Object.FindObjectsOfType<BSGMLAgent>(true))
        {
            if (ml == null || ml.agentRole != BSGMLAgent.AgentRole.Physical) continue;

            ml.waitForCognitiveReady = false;
            ml.physicalUnlocked = true;
            ml.ragSpawnPreserveActive = true;

            ZoneDeclarativeMemory mem = ZoneDeclarativeMemory.ForZone(ml.zoneIndex);
            if (mem != null)
                mem.cognitiveReady = true;

            var dr = ml.GetComponent<Unity.MLAgents.DecisionRequester>();
            if (dr != null)
                dr.enabled = true;
            ml.RequestDecision();
        }
    }

    static void VerifyOnnxModelsAttached()
    {
        foreach (var ml in Object.FindObjectsOfType<BSGMLAgent>(true))
        {
            if (ml == null || ml.zoneIndex < 0) continue;
            var bp = ml.GetComponent<Unity.MLAgents.Policies.BehaviorParameters>();
            if (bp == null) continue;

            string role = ml.agentRole == BSGMLAgent.AgentRole.Mental ? "Cognitive" : "Physical";
            if (bp.Model == null)
            {
                Debug.LogError($"[RagInferenceScene] {role} {ml.agentId} zone {ml.zoneIndex}: ModelAsset is None on BehaviorParameters — assign zone model on RagMlBrainDeployer in scene.");
                continue;
            }

            if (bp.BehaviorType != Unity.MLAgents.Policies.BehaviorType.InferenceOnly)
                Debug.LogWarning($"[RagInferenceScene] {role} {ml.agentId}: BehaviorType={bp.BehaviorType} (expected InferenceOnly). Re-run bootstrap or check MLAgentAttacher.");

            int obs = bp.BrainParameters.VectorObservationSize;
            if (obs != 30)
                Debug.LogWarning($"[RagInferenceScene] {role} {ml.agentId}: VectorObservationSize={obs} (expected 30).");

            bool onnxMove = ml.inferenceOnnxControlsLocomotion;
            Debug.Log($"[RagInferenceScene] {role} {ml.agentId} zone {ml.zoneIndex} → '{bp.BehaviorName}' ONNX attached ({bp.Model.name}) InferenceOnly obs={obs} locomotion={(onnxMove ? "ONNX" : "scripted/mover")}.");
        }
    }
}
