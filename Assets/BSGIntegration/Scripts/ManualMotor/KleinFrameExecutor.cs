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

    [Tooltip("How close (m) the fingertip must get to the target object's top surface to count as a genuine press (button turns green). Tight (~3cm) so a finger still hovering in the air does NOT green — only a real touch does.")]
    public float pressContactDistance = 0.03f;
    [Tooltip("Once the fingertip stops getting closer to the target for this long (seconds), stop waiting for contact — the step may still advance but the button stays un-pressed.")]
    public float pressSettleSeconds = 0.35f;
    [Tooltip("Hard safety cap (seconds) on a single press attempt so a genuinely unreachable target can't hold the coroutine open.")]
    public float pressContactTimeout = 2.5f;
    Vector3 _contactPoint;
    bool _pressContacted;
    Transform _pressTargetRoot;   // the station whose TOP the fingertip must touch (correct Klein target)
    float _minTipGap;             // closest fingertip-to-target gap achieved this press (diagnostics)

    [Tooltip("Extra reach aggression from RagSequenceAgentMover retry attempts (0..1).")]
    public float PressReachRetryBoost;

    public bool IsExecuting => _activeRoutine != null && !_completed;
    public bool IsHoldingPose => _poseHoldActive;
    public bool PressContactAchieved => _pressContacted;
    /// <summary>Closest gap (m) between fingertip pad and meronym top this press attempt.</summary>
    public float ClosestTipGap => _minTipGap;
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
        // Do NOT pre-load mannualBuffer2.json here — the Klein frames come from the RAG (LoadFromRag in
        // BootstrapFromRagText / EnsureKleinFramesFromRag). The file is only a last-resort fallback.
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
        // Klein frames come ONLY from the RAG (sceneStateLog/physicalAgents). No mannualBuffer2.json
        // fallback — if the RAG has no physical Klein data, the motor simply stays idle.
        if (!ManualBufferCatalog.LoadFromRag(ragText))
            Debug.LogWarning("[KleinFrameExecutor] RAG had no physical Klein frames (sceneStateLog/physicalAgents) — motor idle. mannualBuffer2.json is NOT used.");
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

        if (ActiveFrame.IsPendingFullBody || (ActiveFrame.manualCommand ?? "").StartsWith("RESTING", StringComparison.OrdinalIgnoreCase))
            _activeRoutine = StartCoroutine(CoResting(ActiveFrame));
        else if ((ActiveFrame.manualCommand ?? "").StartsWith("PRESSING", StringComparison.OrdinalIgnoreCase))
            _activeRoutine = StartCoroutine(CoPressing(ActiveFrame, sceneWorldTarget));
        else if ((ActiveFrame.manualCommand ?? "").StartsWith("DEPRESSING", StringComparison.OrdinalIgnoreCase))
            _activeRoutine = StartCoroutine(CoDepressing(ActiveFrame, sceneWorldTarget));
        else
            _activeRoutine = StartCoroutine(CoPressing(ActiveFrame, sceneWorldTarget));

        if (logVerbose)
            Debug.Log($"[KleinFrameExecutor] {step.stepId} → {ActiveFrame.kleinFrameId} ({ActiveFrame.manualCommand} @ {ActiveFrame.targetObject}) " +
                      $"approach={ActiveFrame.rigPose?.approachAngleDeg}° force={ActiveFrame.rigPose?.contactForceN}N  " +
                      $"[{(ManualBufferCatalog.LoadedFromRag ? "read live from RAG sceneStateLog" : "read live from mannualBuffer2.json")}]");

        return true;
    }

    public bool TryExecuteRestingFrame(Action onComplete = null)
    {
        if (!ManualBufferCatalog.IsLoaded)   // RAG-only; no mannualBuffer2.json fallback
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

        // Hold fully pressed and wait for a genuine fingertip touch. No time-based or relaxed-distance
        // green — only CheckPressContact() may EngagePressFx().
        BeginHoldPose(frame, sceneWorldTarget, 1f, force);
        yield return CoWaitForPressContact(frame);
        SignalMotorComplete();
    }

    IEnumerator CoWaitForPressContact(KleinFrame frame)
    {
        float waited = 0f;
        float settleTimer = 0f;
        float lastGap = float.MaxValue;
        float contactTimeout = pressContactTimeout + PressReachRetryBoost * 1.8f;
        while (!_pressContacted && waited < contactTimeout)
        {
            CheckPressContact();
            if (_hand != null && _poseHoldActive && _holdFrame != null)
                ApplyMotorPose(_holdFrame, _holdTarget, 1f, _holdForce);

            if (_minTipGap < lastGap - 0.002f)
            {
                lastGap = _minTipGap;
                settleTimer = 0f;
            }
            else
            {
                settleTimer += Time.deltaTime;
            }
            if (settleTimer >= pressSettleSeconds)
                break;

            waited += Time.deltaTime;
            yield return null;
        }

        if (_pressContacted)
        {
            if (frame != null)
                RecordMotorFrame(frame, _activeStepId);
            if (logVerbose)
                Debug.Log($"[KleinPress] {_activeStepId} fingertip HIT the target top (gap {_minTipGap:F3}m) → state '{frame?.stateBefore}'→'{frame?.stateAfter}' recorded; button green.");
        }
        else
        {
            ReleasePressFx();
            Vector3 tipPos = _hand != null && _hand.IndexFingerTip != null ? _hand.IndexFingerTip.position : Vector3.zero;
            Debug.LogWarning($"[KleinPress] {_activeStepId} no physical contact (closest gap {_minTipGap:F3}m vs required {pressContactDistance:F3}m; tip at {tipPos}). " +
                             "No state transition recorded; button stays un-pressed.");
        }
    }

    /// <summary>True once the right index fingertip is physically pressing the meronym TOP surface.
    /// Uses top-surface gap, downward raycast, and trigger overlap — not time or parent proximity.</summary>
    bool CheckPressContact()
    {
        if (_pressContacted)
            return true;

        Transform tip = _hand != null ? _hand.IndexFingerTip : null;
        if (tip == null)
            return false;
        Vector3 p = _hand.GetEffectiveFingerTipWorld();

        float gap = float.MaxValue;
        bool onTop = false;
        float contactTol = pressContactDistance;

        if (_pressTargetRoot != null && EnvironmentSolidCollider.TryGetVisibleBounds(_pressTargetRoot, out Bounds b))
        {
            gap = EnvironmentSolidCollider.GetTopSurfaceGap(b, p);
            float topY = b.max.y;
            // Fingertip must be at or slightly into the top face — hovering above does not count.
            onTop = p.y <= topY + 0.003f && p.y >= topY - Mathf.Max(0.004f, b.size.y * 0.65f);

            // Cylinder meronyms (scroll_wheel): accept cap-disk proximity in XZ when the tip is on the top cap.
            if (!onTop && b.size.y < b.size.x * 0.85f)
            {
                float dx = p.x - b.center.x;
                float dz = p.z - b.center.z;
                float capR = Mathf.Max(b.extents.x, b.extents.z) * 0.9f;
                if (dx * dx + dz * dz <= capR * capR && p.y <= topY + 0.005f && p.y >= topY - 0.018f)
                    onTop = true;
            }

            if (TryFingerOverlapsMeronym(p, 0.014f))
            {
                onTop = true;
                gap = Mathf.Min(gap, 0f);
            }

            if (TryFingerRayHitsMeronymTop(p, topY, 0.09f))
            {
                onTop = true;
                gap = Mathf.Min(gap, Vector3.Distance(p, new Vector3(p.x, topY, p.z)));
            }
        }
        else
        {
            // No renderer bounds on the meronym — never treat "near the IK goal" as a physical touch.
            gap = Vector3.Distance(p, _contactPoint);
            onTop = p.y <= _contactPoint.y + 0.003f && p.y >= _contactPoint.y - 0.015f;
        }

        if (gap < _minTipGap)
            _minTipGap = gap;

        if (gap <= contactTol && onTop)
        {
            _pressContacted = true;
            EngagePressFx();
            if (logVerbose)
                Debug.Log($"[KleinPress] {_activeStepId} verified meronym contact (gap {gap:F4}m, tip {p}).");
            return true;
        }
        return false;
    }

    bool TryFingerOverlapsMeronym(Vector3 tipPos, float radius)
    {
        if (_pressTargetRoot == null)
            return false;

        Collider[] hits = Physics.OverlapSphere(tipPos, radius, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i];
            if (c == null)
                continue;
            if (c.transform == _pressTargetRoot || c.transform.IsChildOf(_pressTargetRoot))
                return true;
        }
        return false;
    }

    bool TryFingerRayHitsMeronymTop(Vector3 tipPos, float topY, float maxDown)
    {
        if (_pressTargetRoot == null)
            return false;

        Vector3 origin = tipPos + Vector3.up * 0.015f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxDown + 0.02f, ~0, QueryTriggerInteraction.Collide))
        {
            if (hit.transform == _pressTargetRoot || hit.transform.IsChildOf(_pressTargetRoot))
                return hit.point.y >= topY - 0.006f;
        }
        return false;
    }

    IEnumerator CoDepressing(KleinFrame frame, Vector3 sceneWorldTarget)
    {
        float duration = Mathf.Max(0.08f, frame.durationMs / 1000f);
        float force = frame.hasForceNewtons ? frame.forceNewtons : frame.rigPose?.contactForceN ?? 0.3f;
        _contactPoint = sceneWorldTarget;
        _pressContacted = false;
        _minTipGap = float.MaxValue;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            float press = t < 0.45f
                ? Mathf.SmoothStep(0f, 1f, t / 0.45f)
                : Mathf.SmoothStep(1f, 0f, (t - 0.45f) / 0.55f);
            ApplyMotorPose(frame, sceneWorldTarget, press, force);
            CheckPressContact();
            if (!_pressContacted && press < 0.5f)
                ReleasePressFx();
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (!_pressContacted)
        {
            BeginHoldPose(frame, sceneWorldTarget, 1f, force);
            yield return CoWaitForPressContact(frame);
        }
        else if (frame != null)
        {
            RecordMotorFrame(frame, _activeStepId);
        }

        BeginHoldPose(null, sceneWorldTarget, 0f, 0f);
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
        {
            float motor = resolve.frame.durationMs / 1000f;
            string cmd = resolve.frame.manualCommand ?? string.Empty;
            if (string.Equals(cmd, "PRESSING", StringComparison.OrdinalIgnoreCase)
                || string.Equals(cmd, "DEPRESSING", StringComparison.OrdinalIgnoreCase))
            {
                motor += pressContactTimeout;
            }
            return Mathf.Max(0.35f, motor);
        }

        return 0.35f;
    }
}
