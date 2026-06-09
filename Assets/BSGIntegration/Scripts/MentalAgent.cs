using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mental Agent (M-agent).
/// Performs the cognitive action sequence by physically moving to each
/// cognitive station in the zone, pausing briefly to "process" it,
/// then writing the output to ZoneDeclarativeMemory.
/// Multiple M-agents per zone model parallel cognitive processing.
/// </summary>
public class MentalAgent : MonoBehaviour
{
    [Header("Identity")]
    public int    zoneIndex   = -1;
    public int    agentIndex  = 0;   // 0..N-1 within the zone
    public string agentLabel  = "M";

    [Header("Movement")]
    public float moveSpeed         = 5f;
    public float arrivalDistance   = 1.8f;
    public float stationDwellTime  = 0.6f;   // seconds spent "thinking" at each station

    [Header("State")]
    public bool  isRunning         = false;
    public int   currentStepIndex  = 0;
    public string currentStepId    = string.Empty;

    // ── Internal ──────────────────────────────────────────────────────────
    private ZoneDeclarativeMemory declarativeMemory;
    private List<CognitiveStepEntry> steps = new List<CognitiveStepEntry>();
    private Rigidbody rb;
    private AgentCognitiveMemory localMemory;

    // Simple data class for each step this agent handles
    public class CognitiveStepEntry
    {
        public string stepId;
        public int    stepOrder;
        public string cognitiveState;
        public string targetObjectId;   // zone-suffixed station name (e.g. cognitive_001_zone2)
        public string outputKey;
        public string outputValue;
        /// <summary>Minimum dwell from JSON expectedDuration; combined with stationDwellTime.</summary>
        public float  expectedDuration;
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.useGravity    = false;
        rb.freezeRotation = true;
        rb.constraints   = RigidbodyConstraints.FreezeRotationX
                         | RigidbodyConstraints.FreezeRotationY
                         | RigidbodyConstraints.FreezeRotationZ
                         | RigidbodyConstraints.FreezePositionY;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        localMemory = GetComponent<AgentCognitiveMemory>();
        if (localMemory == null) localMemory = gameObject.AddComponent<AgentCognitiveMemory>();
    }

    void Start()
    {
        declarativeMemory = ZoneDeclarativeMemory.ForZone(zoneIndex);
        if (declarativeMemory == null)
        {
            Debug.LogWarning($"[MentalAgent Z{zoneIndex}#{agentIndex}] No ZoneDeclarativeMemory found — will retry.");
            StartCoroutine(RetryFindMemoryThenRun());
            return;
        }
        StartCoroutine(RunCognitiveSequence());
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>Assign the cognitive steps this M-agent is responsible for.</summary>
    public void AssignSteps(List<CognitiveStepEntry> assignedSteps)
    {
        steps = assignedSteps ?? new List<CognitiveStepEntry>();
    }

    // ── Coroutines ────────────────────────────────────────────────────────

    private IEnumerator RetryFindMemoryThenRun()
    {
        float waited = 0f;
        while (declarativeMemory == null && waited < 10f)
        {
            yield return new WaitForSeconds(0.5f);
            waited += 0.5f;
            declarativeMemory = ZoneDeclarativeMemory.ForZone(zoneIndex);
        }

        if (declarativeMemory != null)
            yield return RunCognitiveSequence();
        else
            Debug.LogError($"[MentalAgent Z{zoneIndex}#{agentIndex}] Could not find ZoneDeclarativeMemory after 10s.");
    }

    private IEnumerator RunCognitiveSequence()
    {
        isRunning = true;
        // Small stagger so M-agents don't all start simultaneously
        yield return new WaitForSeconds(agentIndex * 0.3f);

        for (int i = 0; i < steps.Count; i++)
        {
            currentStepIndex = i;
            CognitiveStepEntry step = steps[i];
            currentStepId = step.stepId;

            // Flash colour to "thinking" state
            SetColor(new Color(0.3f, 0.9f, 1f));  // cyan thinking

            // Find target station
            Vector3 targetPos = FindStationPosition(step.targetObjectId);
            bool hasTarget = targetPos != Vector3.zero;

            if (hasTarget)
            {
                // Move toward station
                yield return MoveToPosition(targetPos);
            }
            else
            {
                // Branch/dispatch/no-target step — just pause
                yield return new WaitForSeconds(stationDwellTime);
            }

            // Dwell at station — at least JSON expectedDuration (see CognitiveProcessManager on M-controller path)
            SetColor(new Color(1f, 0.9f, 0.2f));  // yellow processing
            float dwell = Mathf.Max(stationDwellTime, step.expectedDuration > 0f ? step.expectedDuration : 0f);
            if (dwell <= 0f) dwell = stationDwellTime;
            yield return new WaitForSeconds(dwell);

            // Write output to declarative memory
            if (!string.IsNullOrWhiteSpace(step.outputKey))
            {
                declarativeMemory.RecordCognitiveStep(step.stepId, step.outputKey, step.outputValue);
                localMemory.Store(step.outputKey, step.outputValue, step.targetObjectId);
            }

            declarativeMemory.MarkCognitiveStepComplete(step.stepId);

            // Flash green briefly
            SetColor(new Color(0.2f, 1f, 0.4f));
            yield return new WaitForSeconds(0.2f);
        }

        // All steps done — return to home position and idle
        SetColor(new Color(0.4f, 0.6f, 0.9f));  // cool blue idle
        isRunning = false;
        Debug.Log($"[MentalAgent Z{zoneIndex}#{agentIndex}] Cognitive sequence complete.");
    }

    private IEnumerator MoveToPosition(Vector3 target)
    {
        target.y = transform.position.y;  // keep Y fixed

        float timeout = 12f;
        float elapsed = 0f;

        while (Vector3.Distance(transform.position, target) > arrivalDistance && elapsed < timeout)
        {
            Vector3 dir = (target - transform.position).normalized;
            rb.MovePosition(transform.position + dir * moveSpeed * Time.fixedDeltaTime);
            elapsed += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }
    }

    private Vector3 FindStationPosition(string stationId)
    {
        if (string.IsNullOrWhiteSpace(stationId)) return Vector3.zero;

        // Try zone-suffixed name first
        string zoneSuffixed = stationId.Contains("_zone") ? stationId : $"{stationId}_zone{zoneIndex}";
        GameObject go = GameObject.Find(zoneSuffixed);
        if (go != null) return go.transform.position;

        // Fallback: plain name
        go = GameObject.Find(stationId);
        return go != null ? go.transform.position : Vector3.zero;
    }

    private void SetColor(Color c)
    {
        Renderer r = GetComponent<Renderer>();
        if (r != null && r.material != null)
            r.material.color = c;
    }
}
