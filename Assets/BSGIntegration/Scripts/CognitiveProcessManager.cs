using UnityEngine;

/// <summary>
/// Resolves per-step dwell time from JSON (<see cref="ActionSequenceStep.expectedDuration"/>)
/// for both cognitive and operational (physical) RAG steps, and defines completion feedback
/// (white flash duration) when a step finishes and rewards apply.
/// </summary>
public class CognitiveProcessManager : MonoBehaviour
{
    public static CognitiveProcessManager Instance { get; private set; }

    [Header("Dwell")]
    [Tooltip("Minimum seconds to wait at a station when JSON specifies expectedDuration (e.g. 0.1 is honored, not replaced by 2s).")]
    public float minimumDwellSeconds = 0.05f;

    [Tooltip("Used when JSON has no expectedDuration or no matching sequence step.")]
    public float fallbackDwellSeconds = 2.5f;

    [Header("Completion (reward)")]
    [Tooltip("Agent body + cognitive station turn white for this long after the step is committed and reward is passed to the P-agent.")]
    public float rewardWhiteFlashSeconds = 2f;

    [Header("Processing phase (during dwell — before reward)")]
    public Color processingBodyTint = new Color(0.35f, 0.85f, 0.95f, 1f);

    [Tooltip("If true, station stays normal color during dwell; white only during reward flash.")]
    public bool whiteStationOnlyOnSuccess = true;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    /// <summary>Ensures a manager exists (DontDestroyOnLoad singleton).</summary>
    public static void EnsureExists()
    {
        if (Instance != null) return;
        GameObject go = new GameObject("CognitiveProcessManager");
        DontDestroyOnLoad(go);
        go.AddComponent<CognitiveProcessManager>();
    }

    /// <summary>
    /// Seconds to wait at the station after arrival: JSON expectedDuration when present,
    /// else explicit override, else controller default / fallback.
    /// </summary>
    public float ResolveDwellSeconds(ActionSequenceStep step, float dwellOverride, float controllerDefault)
    {
        if (dwellOverride > 0f)
            return Mathf.Max(minimumDwellSeconds, dwellOverride);
        if (step != null && step.expectedDuration > 0f)
            return Mathf.Max(minimumDwellSeconds, step.expectedDuration);
        float fb = controllerDefault > 0f ? controllerDefault : fallbackDwellSeconds;
        return Mathf.Max(minimumDwellSeconds, fb);
    }

    public float ResolveLayerDwell(float layerExpectedDuration)
    {
        if (layerExpectedDuration > 0f)
            return Mathf.Max(minimumDwellSeconds, layerExpectedDuration);
        return Mathf.Max(minimumDwellSeconds, fallbackDwellSeconds);
    }

    /// <summary>
    /// Finds the JSON step for this visit: exact stepId match first, else same targetObjectId (without zone suffix).
    /// </summary>
    public ActionSequenceStep FindStepForStation(string pAgentId, string zoneSuffixedStationId, string stepId)
    {
        if (string.IsNullOrEmpty(pAgentId) || AgentSequenceManager.Instance == null) return null;

        AgentSequenceData cogSeq = AgentSequenceManager.Instance.GetCognitiveSequence(pAgentId);
        if (cogSeq?.actionSequence == null) return null;

        if (!string.IsNullOrEmpty(stepId))
        {
            foreach (ActionSequenceStep s in cogSeq.actionSequence)
            {
                if (s != null && string.Equals(s.stepId, stepId, System.StringComparison.OrdinalIgnoreCase))
                    return s;
            }
        }

        string baseId = StripZoneSuffix(zoneSuffixedStationId);
        foreach (ActionSequenceStep s in cogSeq.actionSequence)
        {
            if (s == null || string.IsNullOrEmpty(s.targetObjectId)) continue;
            if (string.Equals(s.targetObjectId, baseId, System.StringComparison.OrdinalIgnoreCase))
                return s;
        }

        return null;
    }

    public static string StripZoneSuffix(string zoneSuffixedStationId)
    {
        if (string.IsNullOrEmpty(zoneSuffixedStationId)) return string.Empty;
        int idx = zoneSuffixedStationId.IndexOf("_zone", System.StringComparison.OrdinalIgnoreCase);
        if (idx <= 0) return zoneSuffixedStationId;
        return zoneSuffixedStationId.Substring(0, idx);
    }

    /// <summary>
    /// Shared dwell after the agent reaches the step target (cognitive station or RAG environment object).
    /// Uses expectedDuration, else endTimeSec−startTimeSec, else <see cref="fallbackDwellSeconds"/>.
    /// </summary>
    public static float ComputeStationDwellSeconds(ActionSequenceStep step)
    {
        EnsureExists();
        float ed = 0f;
        if (step != null)
        {
            ed = step.expectedDuration;
            if (float.IsNaN(ed) || float.IsInfinity(ed) || ed <= 0f)
            {
                float derived = step.endTimeSec - step.startTimeSec;
                if (derived > 0f && !float.IsNaN(derived))
                    ed = derived;
            }
        }

        float minS = Instance != null ? Instance.minimumDwellSeconds : 0.05f;
        float fb = Instance != null ? Instance.fallbackDwellSeconds : 2.5f;
        if (ed <= 0f)
            ed = fb;
        return Mathf.Max(minS, ed);
    }
}
