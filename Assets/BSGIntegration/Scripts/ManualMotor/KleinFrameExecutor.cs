using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Manual Module motor runtime for the designated physical player.
/// Executes Klein frames (RESTING / PRESSING / DEPRESSING) from mannualBuffer.json.
/// </summary>
public class KleinFrameExecutor : MonoBehaviour
{
    [SerializeField] int zoneIndex = 0;
    [SerializeField] bool logVerbose = true;

    HandRotationManager _hand;
    Coroutine _activeRoutine;
    string _activeStepId;
    bool _completed;
    Action _onComplete;

    bool _poseHoldActive;
    KleinFrame _holdFrame;
    Vector3 _holdTarget;
    float _holdPressAmount;
    float _holdForce;

    public bool IsExecuting => _activeRoutine != null && !_completed;
    public bool IsHoldingPose => _poseHoldActive;
    public KleinFrame ActiveFrame { get; private set; }
    public KleinFrameResolveResult LastResolve { get; private set; }

    public static KleinFrameExecutor EnsureOnAgent(GameObject agent, int zone = 0)
    {
        if (agent == null) return null;

        KleinFrameExecutor exec = agent.GetComponent<KleinFrameExecutor>();
        if (exec == null)
            exec = agent.AddComponent<KleinFrameExecutor>();

        exec.zoneIndex = zone;
        exec._hand = HandRotationManager.EnsureOnAgent(agent);
        exec._hand?.RefreshRigWire();
        BodyPartRegistry.TryLoad();
        ManualBufferCatalog.TryLoad();
        return exec;
    }

    public static bool IsAvailableOn(GameObject agent)
    {
        return agent != null && agent.GetComponent<KleinFrameExecutor>() != null && ManualBufferCatalog.IsLoaded;
    }

    public void BootstrapFromRagText(string ragText)
    {
        if (!string.IsNullOrWhiteSpace(ragText))
            SceneStateLogBridge.TryParseFromRagText(ragText);
        BodyPartRegistry.TryLoad();
        ManualBufferCatalog.TryLoad();
        BodyPartRegistryValidator.ValidateAtBootstrap(ragText);
        KleinFrameResolver.ResetSequenceCounters();
        _hand?.RefreshRigWire();
    }

    public bool TryExecuteForStep(ActionSequenceStep step, Vector3 sceneWorldTarget, Action onComplete)
    {
        if (step == null)
            return false;

        if (_hand == null)
            _hand = HandRotationManager.EnsureOnAgent(gameObject);

        Cancel();
        _hand?.RefreshRigWire();

        LastResolve = KleinFrameResolver.Resolve(step);
        if (!LastResolve.resolved)
        {
            if (logVerbose)
                Debug.LogWarning($"[KleinFrameExecutor] No Klein frame for {step.stepId} ({step.targetObjectName}) — fallback hand pose.");
            return false;
        }

        _activeStepId = step.stepId;
        _onComplete = onComplete;
        _completed = false;

        if (LastResolve.isReachOnly)
        {
            _activeRoutine = StartCoroutine(CoReachOnly(step, sceneWorldTarget));
            return true;
        }

        ActiveFrame = LastResolve.frame;
        if (ActiveFrame == null)
            return false;

        if (ActiveFrame.IsPendingFullBody || string.Equals(ActiveFrame.manualCommand, "RESTING", StringComparison.OrdinalIgnoreCase))
            _activeRoutine = StartCoroutine(CoResting(ActiveFrame));
        else if (string.Equals(ActiveFrame.manualCommand, "PRESSING", StringComparison.OrdinalIgnoreCase))
            _activeRoutine = StartCoroutine(CoPressing(ActiveFrame, sceneWorldTarget));
        else if (string.Equals(ActiveFrame.manualCommand, "DEPRESSING", StringComparison.OrdinalIgnoreCase))
            _activeRoutine = StartCoroutine(CoDepressing(ActiveFrame, sceneWorldTarget));
        else
            _activeRoutine = StartCoroutine(CoPressing(ActiveFrame, sceneWorldTarget));

        if (logVerbose)
            Debug.Log($"[KleinFrameExecutor] {step.stepId} → {ActiveFrame.kleinFrameId} ({ActiveFrame.manualCommand} @ {ActiveFrame.targetObject})");

        return true;
    }

    public bool TryExecuteRestingFrame(Action onComplete = null)
    {
        if (!ManualBufferCatalog.IsLoaded && !ManualBufferCatalog.TryLoad())
            return false;

        KleinFrame resting = ManualBufferCatalog.RestingFrame;
        if (resting == null)
            return false;

        Cancel();
        _onComplete = onComplete;
        _completed = false;
        ActiveFrame = resting;
        _activeRoutine = StartCoroutine(CoResting(resting));
        return true;
    }

    public void Cancel()
    {
        if (_activeRoutine != null)
        {
            StopCoroutine(_activeRoutine);
            _activeRoutine = null;
        }
        ReleaseHeldPose();
        _completed = false;
        _activeStepId = null;
        _onComplete = null;
        ActiveFrame = null;
    }

    void ReleaseHeldPose()
    {
        _poseHoldActive = false;
        _holdFrame = null;
        _hand?.ResetRightArmReachPose();
        _hand?.ResetFingerCurl();
    }

    IEnumerator CoResting(KleinFrame frame)
    {
        float duration = Mathf.Max(0.05f, frame.durationMs / 1000f);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        Complete(frame, _activeStepId);
    }

    IEnumerator CoReachOnly(ActionSequenceStep step, Vector3 sceneWorldTarget)
    {
        float duration = Mathf.Max(0.35f, step.expectedDuration > 0f ? step.expectedDuration * 0.5f : 0.35f);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float phase = Mathf.Clamp01(elapsed / duration);
            _hand?.SetManualReachPose(sceneWorldTarget, phase);
            elapsed += Time.deltaTime;
            yield return null;
        }
        BeginHoldPose(null, sceneWorldTarget, 1f, 0f);
        RecordReachOnly(step);
        SignalMotorComplete();
    }

    IEnumerator CoPressing(KleinFrame frame, Vector3 sceneWorldTarget)
    {
        float duration = Mathf.Max(0.08f, frame.durationMs / 1000f);
        float force = frame.hasForceNewtons ? frame.forceNewtons : frame.rigPose?.contactForceN ?? 0.25f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float phase = Mathf.Clamp01(elapsed / duration);
            float press = Mathf.SmoothStep(0f, 1f, phase);
            ApplyMotorPose(frame, sceneWorldTarget, press, force);
            elapsed += Time.deltaTime;
            yield return null;
        }

        BeginHoldPose(frame, sceneWorldTarget, 1f, force);
        if (frame != null)
            RecordMotorFrame(frame, _activeStepId);
        SignalMotorComplete();
    }

    IEnumerator CoDepressing(KleinFrame frame, Vector3 sceneWorldTarget)
    {
        float duration = Mathf.Max(0.08f, frame.durationMs / 1000f);
        float force = frame.hasForceNewtons ? frame.forceNewtons : frame.rigPose?.contactForceN ?? 0.3f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            float press = t < 0.45f
                ? Mathf.SmoothStep(0f, 1f, t / 0.45f)
                : Mathf.SmoothStep(1f, 0f, (t - 0.45f) / 0.55f);
            ApplyMotorPose(frame, sceneWorldTarget, press, force);
            elapsed += Time.deltaTime;
            yield return null;
        }

        BeginHoldPose(null, sceneWorldTarget, 0f, 0f);
        if (frame != null)
            RecordMotorFrame(frame, _activeStepId);
        SignalMotorComplete();
    }

    void BeginHoldPose(KleinFrame frame, Vector3 target, float pressAmount, float force)
    {
        _poseHoldActive = true;
        _holdFrame = frame;
        _holdTarget = target;
        _holdPressAmount = pressAmount;
        _holdForce = force;
        ApplyMotorPose(frame, target, pressAmount, force);
    }

    void LateUpdate()
    {
        if (!_poseHoldActive || _hand == null)
            return;

        ApplyMotorPose(_holdFrame, _holdTarget, _holdPressAmount, _holdForce);
    }

    void ApplyMotorPose(KleinFrame frame, Vector3 sceneWorldTarget, float pressPhase, float forceNewtons)
    {
        if (_hand == null) return;

        // Scene station position is authoritative — Klein JSON coordinates are UI-space metadata, not world XYZ.
        _hand.SetManualMotorPose(frame, sceneWorldTarget, pressPhase, forceNewtons);
    }

    void RecordReachOnly(ActionSequenceStep step)
    {
        ZoneDeclarativeMemory mem = ZoneDeclarativeMemory.ForZone(zoneIndex);
        if (mem == null)
        {
            ZoneDeclarativeMemory.RebuildRegistryFromScene();
            mem = ZoneDeclarativeMemory.ForZone(zoneIndex);
        }
        if (mem == null || step == null) return;

        mem.RecordMotorFrame("reach_only", "extended_0", "contacting_1", step.stepId);
    }

    void RecordMotorFrame(KleinFrame frame, string stepId)
    {
        if (frame == null) return;
        ZoneDeclarativeMemory mem = ZoneDeclarativeMemory.ForZone(zoneIndex);
        if (mem == null)
        {
            ZoneDeclarativeMemory.RebuildRegistryFromScene();
            mem = ZoneDeclarativeMemory.ForZone(zoneIndex);
        }
        mem?.RecordMotorFrame(frame.kleinFrameId, frame.stateBefore, frame.stateAfter, stepId);
    }

    void SignalMotorComplete()
    {
        _completed = true;
        _activeRoutine = null;
        Action cb = _onComplete;
        _onComplete = null;
        cb?.Invoke();
    }

    void Complete(KleinFrame frame, string stepId)
    {
        if (frame != null)
            RecordMotorFrame(frame, stepId);
        SignalMotorComplete();
    }

    public float GetRequiredDwellSeconds(ActionSequenceStep step)
    {
        KleinFrameResolveResult resolve = KleinFrameResolver.Peek(step);
        if (!resolve.resolved)
            return 0.35f;

        if (resolve.isReachOnly)
            return Mathf.Max(0.35f, step != null && step.expectedDuration > 0f ? step.expectedDuration * 0.5f : 0.35f);

        if (resolve.frame != null)
            return Mathf.Max(0.35f, resolve.frame.durationMs / 1000f);

        return 0.35f;
    }
}
