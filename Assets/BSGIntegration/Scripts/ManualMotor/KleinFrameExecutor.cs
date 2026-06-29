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

    // Motor-inspired press feedback on the contacted station (correct Klein target only).
    ButtonPressContact _pressContact;
    bool _pressFxEngaged;

    [Tooltip("How close (m) the fingertip must get to the target object's top surface to count as a physical press. Tight (~3cm) so a finger still hovering in the air does NOT register as pressed — only a real touch does.")]
    public float pressContactDistance = 0.03f;
    [Tooltip("The step does NOT complete until the fingertip physically presses the target. This is only a safety cap (seconds) so a genuinely unreachable target can't deadlock the sim.")]
    public float pressContactTimeout = 10f;
    Vector3 _contactPoint;
    bool _pressContacted;
    Transform _pressTargetRoot;   // the station whose TOP the fingertip must touch (correct Klein target)
    float _minTipGap;             // closest fingertip-to-target gap achieved this press (diagnostics)

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

    /// <summary>Arm (or clear) the physical press feedback on the contacted station. Set by the mover
    /// for the correct Klein target so the button visibly depresses/highlights when the finger lands.</summary>
    public void SetPressFeedbackTarget(GameObject station, GameObject presser)
    {
        if (_pressContact != null && (station == null || _pressContact.gameObject != station))
        {
            _pressContact.Release();
            _pressFxEngaged = false;
        }
        _pressContact = station != null ? ButtonPressContact.EnsureOn(station) : null;
        _pressTargetRoot = station != null ? station.transform : null;
    }

    void EngagePressFx()
    {
        if (_pressContact == null || _pressFxEngaged)
            return;
        _pressContact.Press();
        _pressFxEngaged = true;
    }

    void ReleasePressFx()
    {
        if (_pressContact == null)
            return;
        _pressContact.Release();
        _pressFxEngaged = false;
    }

    public bool TryExecuteForStep(ActionSequenceStep step, string agentId, Vector3 sceneWorldTarget, Action onComplete)
    {
        if (step == null)
            return false;

        if (_hand == null)
            _hand = HandRotationManager.EnsureOnAgent(gameObject);

        Cancel();
        _hand?.RefreshRigWire();

        LastResolve = KleinFrameResolver.Resolve(step, agentId, zoneIndex);
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
            Debug.Log($"[KleinFrameExecutor] {step.stepId} → {ActiveFrame.kleinFrameId} ({ActiveFrame.manualCommand} @ {ActiveFrame.targetObject}) " +
                      $"approach={ActiveFrame.rigPose?.approachAngleDeg}° force={ActiveFrame.rigPose?.contactForceN}N  [read live from mannualBuffer2.json]");

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
        _pressContacted = false;
    }

    void ReleaseHeldPose()
    {
        _poseHoldActive = false;
        _holdFrame = null;
        ReleasePressFx();
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
        _contactPoint = sceneWorldTarget;
        _pressContacted = false;
        _minTipGap = float.MaxValue;
        float elapsed = 0f;

        // Ramp the reach/press in. (Contact is only judged once fully pressed, below, so the resting
        // hand can't false-trigger while the finger is still on its way to the surface.)
        while (elapsed < duration)
        {
            float phase = Mathf.Clamp01(elapsed / duration);
            float press = Mathf.SmoothStep(0f, 1f, phase);
            ApplyMotorPose(frame, sceneWorldTarget, press, force);
            elapsed += Time.deltaTime;
            yield return null;
        }

        // Hold fully pressed and KEEP TRYING until the fingertip PHYSICALLY reaches the surface. The
        // step does not complete until then: only a real contact turns the button green, records the
        // unpressed→pressed state, and lets the step finish. The timeout is only a safety cap so a
        // genuinely unreachable target can't deadlock the sim.
        BeginHoldPose(frame, sceneWorldTarget, 1f, force);
        float waited = 0f;
        while (!_pressContacted && waited < pressContactTimeout)
        {
            CheckPressContact();
            waited += Time.deltaTime;
            yield return null;
        }

        if (_pressContacted)
        {
            if (frame != null)
                RecordMotorFrame(frame, _activeStepId);
            if (logVerbose)
                Debug.Log($"[KleinPress] {_activeStepId} fingertip HIT the target top (gap {_minTipGap:F2}m) → state '{frame?.stateBefore}'→'{frame?.stateAfter}' recorded; button green.");
        }
        else
        {
            // Safety cap hit without a real press — surface it loudly with the closest gap reached so
            // the target can be enlarged / pressContactDistance raised (the step was NOT pressed).
            ReleasePressFx();
            Vector3 tipPos = _hand != null && _hand.IndexFingerTip != null ? _hand.IndexFingerTip.position : Vector3.zero;
            Debug.LogError($"[KleinPress] {_activeStepId} fingertip did NOT reach the target top within the {pressContactTimeout:F0}s cap " +
                           $"(closest gap {_minTipGap:F2}m > {pressContactDistance:F2}m; tip at {tipPos}). Press NOT registered — enlarge the target or raise pressContactDistance.");
        }

        SignalMotorComplete();
    }

    /// <summary>True once the right index fingertip is physically pressing the TARGET's top surface:
    /// within <see cref="pressContactDistance"/> of the object's visible bounds AND in its upper half
    /// (so the resting hand near the base can't false-trigger). Falls back to a point-distance test
    /// when there is no target object. Latches and turns the button green on the first real contact.</summary>
    bool CheckPressContact()
    {
        if (_pressContacted)
            return true;

        Transform tip = _hand != null ? _hand.IndexFingerTip : null;
        if (tip == null)
            return false;
        Vector3 p = tip.position;

        float gap;
        bool onTop;
        if (_pressTargetRoot != null && EnvironmentSolidCollider.TryGetVisibleBounds(_pressTargetRoot, out Bounds b))
        {
            gap = Mathf.Sqrt(b.SqrDistance(p));   // 0 when the fingertip is inside the object's box
            onTop = p.y >= b.center.y;            // only the top half counts
        }
        else
        {
            gap = Vector3.Distance(p, _contactPoint);
            onTop = true;
        }

        if (gap < _minTipGap)
            _minTipGap = gap;

        if (gap <= pressContactDistance && onTop)
        {
            _pressContacted = true;
            EngagePressFx();   // button turns green at the moment of real physical contact
            return true;
        }
        return false;
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
            if (press >= 0.5f)
                EngagePressFx();   // depress at the peak, then release as the click rises back up
            else
                ReleasePressFx();
            elapsed += Time.deltaTime;
            yield return null;
        }

        ReleasePressFx();
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

    public float GetRequiredDwellSeconds(ActionSequenceStep step, string agentId)
    {
        KleinFrameResolveResult resolve = KleinFrameResolver.Peek(step, agentId, zoneIndex);
        if (!resolve.resolved)
            return 0.35f;

        if (resolve.isReachOnly)
            return Mathf.Max(0.35f, step != null && step.expectedDuration > 0f ? step.expectedDuration * 0.5f : 0.35f);

        if (resolve.frame != null)
            return Mathf.Max(0.35f, resolve.frame.durationMs / 1000f);

        return 0.35f;
    }
}
