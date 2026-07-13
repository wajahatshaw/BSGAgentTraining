using System;
using UnityEngine;

/// <summary>
/// World-space billboard UI attached to the Production Memory station (cognitive_003).
/// Shows the command type and description whenever Production Memory fires in Phase 2
/// or Phase 3 (not during Phase 1 imagination which is internal planning only).
///
/// Command types are derived from the step's <c>productionMemoryConnections</c> field:
///   - connections containing "visual_buffer" / "visual_location_buffer"  → LOOK  (blue)
///   - connections containing "manual_buffer"                              → ACT   (orange)
///   - connections containing "goal_buffer" / "declarative_module"        → RETRIEVE (green)
///
/// The component self-registers with <see cref="CognitivePhaseOrchestrator"/> events; no
/// external wiring is needed.  Add via <see cref="GetOrCreateForStation"/>.
///
/// Does NOT interact with GoalBufferStationPresenter or ImaginalThoughtBubble.
/// </summary>
[DefaultExecutionOrder(55)]
public class ProductionMemoryCommandUI : MonoBehaviour
{
    [Header("Layout")]
    [Tooltip("Height above the station pivot where the UI panel appears.")]
    public float uiHeight = 1.5f;

    [Header("Timing")]
    [Tooltip("How long the panel stays visible after the PM step completes.")]
    public float displayDuration = 3.0f;
    [Tooltip("Fade-out window at end of display (as a fraction of displayDuration).")]
    [Range(0f, 1f)] public float fadeFraction = 0.35f;

    [Header("Ring pulse")]
    public float pulseFrequency = 1.8f;   // Hz
    public float pulseAmplitude = 0.18f;  // ring scale swing ±
    public float ringRadius     = 0.38f;

    // ── Internal state ────────────────────────────────────────────────────────
    private GameObject _uiRoot;
    private TextMesh   _commandTypeLabel;
    private TextMesh   _descriptionLabel;
    private LineRenderer _pulseRing;

    private bool   _visible;
    private float  _displayTimer;
    private bool   _pulsing;
    private string _triggeredByStepId;

    // ── Public factory ────────────────────────────────────────────────────────
    /// <summary>
    /// Ensures a <see cref="ProductionMemoryCommandUI"/> is attached to <paramref name="stationGo"/>
    /// and returns it.  Safe to call multiple times; returns the existing component if present.
    /// </summary>
    public static ProductionMemoryCommandUI GetOrCreateForStation(GameObject stationGo)
    {
        if (stationGo == null) return null;
        var ui = stationGo.GetComponent<ProductionMemoryCommandUI>();
        if (ui == null) ui = stationGo.AddComponent<ProductionMemoryCommandUI>();
        return ui;
    }

    // ── Unity lifecycle ───────────────────────────────────────────────────────
    void Awake()
    {
        BuildUI();
        SetVisible(false);
    }

    void OnEnable()
    {
        var orch = CognitivePhaseOrchestrator.Instance;
        if (orch != null)
        {
            orch.OnCognitiveStepDispatched += OnCognitiveStepDispatched;
            orch.OnStepCompleted           += OnStepCompleted;
        }
    }

    void OnDisable()
    {
        var orch = CognitivePhaseOrchestrator.Instance;
        if (orch != null)
        {
            orch.OnCognitiveStepDispatched -= OnCognitiveStepDispatched;
            orch.OnStepCompleted           -= OnStepCompleted;
        }
    }

    void LateUpdate()
    {
        if (!_visible) return;

        // Billboard — face camera
        Camera cam = Camera.main;
        if (cam != null && _uiRoot != null)
        {
            _uiRoot.transform.rotation = Quaternion.LookRotation(
                _uiRoot.transform.position - cam.transform.position,
                Vector3.up);
        }

        // Pulse ring scale animation
        if (_pulsing && _pulseRing != null)
        {
            float s = 1f + pulseAmplitude * Mathf.Sin(Time.time * pulseFrequency * Mathf.PI * 2f);
            _pulseRing.transform.localScale = Vector3.one * s;
        }

        // Auto-fade / hide countdown
        if (_displayTimer > 0f)
        {
            _displayTimer -= Time.deltaTime;

            // Apply alpha fade in the final fadeFraction window
            float fullTime = displayDuration;
            float fadeStart = fullTime * (1f - fadeFraction);
            float elapsed = fullTime - _displayTimer;
            if (elapsed >= fadeStart)
            {
                float t = Mathf.Clamp01((elapsed - fadeStart) / (fullTime * fadeFraction + 0.001f));
                float alpha = 1f - t;
                ApplyAlpha(alpha);
            }

            if (_displayTimer <= 0f)
                SetVisible(false);
        }
    }

    // ── Orchestrator event handlers ───────────────────────────────────────────

    void OnCognitiveStepDispatched(string stepId)
    {
        var orch = CognitivePhaseOrchestrator.Instance;
        if (orch == null) return;

        // Only show during Phase 2 and Phase 3 — not during Phase 1 imagination
        if (orch.CurrentPhase == CognitivePhaseOrchestrator.PHASE_IMAGINE) return;

        var step = orch.GetStep(stepId);
        if (step == null) return;

        if (!IsProductionMemoryStep(step)) return;

        _triggeredByStepId = stepId;
        ShowCommand(step);
    }

    void OnStepCompleted(string stepId)
    {
        if (!string.Equals(stepId, _triggeredByStepId, StringComparison.Ordinal)) return;

        // Stop pulsing; begin short fade-out
        _pulsing = false;
        _displayTimer = Mathf.Min(_displayTimer, displayDuration * fadeFraction);
    }

    // ── Display logic ─────────────────────────────────────────────────────────

    void ShowCommand(ActionSequenceStep step)
    {
        ClassifyCommand(step, out string label, out Color color);

        if (_commandTypeLabel != null)
        {
            _commandTypeLabel.text  = label;
            _commandTypeLabel.color = color;
        }

        if (_descriptionLabel != null)
        {
            string desc = step.description ?? "";
            if (desc.Length > 55) desc = desc.Substring(0, 52) + "...";
            _descriptionLabel.text = desc;
        }

        if (_pulseRing != null)
        {
            _pulseRing.startColor = color;
            _pulseRing.endColor   = color;
        }

        _displayTimer = displayDuration;
        _pulsing = true;
        SetVisible(true);
        ApplyAlpha(1f);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static bool IsProductionMemoryStep(ActionSequenceStep step)
    {
        return string.Equals(step.currentCognitiveState, "ProductionMemory",
                             StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines the human-readable command label and accent color from
    /// <c>productionMemoryConnections</c>.
    /// </summary>
    public static void ClassifyCommand(ActionSequenceStep step, out string label, out Color color)
    {
        label = "COMMAND";
        color = Color.white;

        var conns = step.productionMemoryConnections;
        if (conns == null || conns.Length == 0) return;

        bool hasVisual   = false;
        bool hasManual   = false;
        bool hasRetrieve = false;

        foreach (string c in conns)
        {
            if (string.Equals(c, "visual_buffer",         StringComparison.Ordinal) ||
                string.Equals(c, "visual_location_buffer", StringComparison.Ordinal))
                hasVisual = true;

            if (string.Equals(c, "manual_buffer", StringComparison.Ordinal))
                hasManual = true;

            if (string.Equals(c, "goal_buffer",         StringComparison.Ordinal) ||
                string.Equals(c, "declarative_module",   StringComparison.Ordinal) ||
                string.Equals(c, "retrieval_buffer",     StringComparison.Ordinal))
                hasRetrieve = true;
        }

        // Priority: manual (motor) > visual (attention) > retrieval (memory)
        if (hasManual && hasVisual)      { label = "LOOK + ACT"; color = new Color(0.90f, 0.49f, 0.13f); }
        else if (hasManual)              { label = "ACT";         color = new Color(0.90f, 0.49f, 0.13f); }
        else if (hasVisual)              { label = "LOOK";        color = new Color(0.16f, 0.50f, 0.73f); }
        else if (hasRetrieve)            { label = "RETRIEVE";    color = new Color(0.15f, 0.68f, 0.38f); }
    }

    void SetVisible(bool v)
    {
        _visible = v;
        if (_uiRoot != null) _uiRoot.SetActive(v);
        if (!v)
        {
            _pulsing = false;
            _displayTimer = 0f;
            _triggeredByStepId = null;
        }
    }

    void ApplyAlpha(float alpha)
    {
        if (_commandTypeLabel != null)
        {
            Color c = _commandTypeLabel.color; c.a = alpha;
            _commandTypeLabel.color = c;
        }
        if (_descriptionLabel != null)
        {
            Color c = _descriptionLabel.color; c.a = alpha;
            _descriptionLabel.color = c;
        }
        if (_pulseRing != null)
        {
            Color cs = _pulseRing.startColor; cs.a = alpha; _pulseRing.startColor = cs;
            Color ce = _pulseRing.endColor;   ce.a = alpha; _pulseRing.endColor   = ce;
        }
    }

    // ── Procedural UI construction ────────────────────────────────────────────

    void BuildUI()
    {
        _uiRoot = new GameObject("PMCommandUI_Root");
        _uiRoot.transform.SetParent(transform, false);
        _uiRoot.transform.localPosition = Vector3.up * uiHeight;

        // ── Command type label (large, bold) ──────────────────────────────────
        GameObject typeGo = new GameObject("PMLabel_Type");
        typeGo.transform.SetParent(_uiRoot.transform, false);
        typeGo.transform.localPosition = new Vector3(0f, 0.22f, 0f);

        _commandTypeLabel = typeGo.AddComponent<TextMesh>();
        _commandTypeLabel.fontSize      = 28;
        _commandTypeLabel.fontStyle     = FontStyle.Bold;
        _commandTypeLabel.anchor        = TextAnchor.MiddleCenter;
        _commandTypeLabel.alignment     = TextAlignment.Center;
        _commandTypeLabel.characterSize = 0.07f;
        _commandTypeLabel.color         = Color.white;
        _commandTypeLabel.text          = "";

        // ── Description label (small, regular) ───────────────────────────────
        GameObject descGo = new GameObject("PMLabel_Desc");
        descGo.transform.SetParent(_uiRoot.transform, false);
        descGo.transform.localPosition = new Vector3(0f, 0.08f, 0f);

        _descriptionLabel = descGo.AddComponent<TextMesh>();
        _descriptionLabel.fontSize      = 16;
        _descriptionLabel.fontStyle     = FontStyle.Normal;
        _descriptionLabel.anchor        = TextAnchor.MiddleCenter;
        _descriptionLabel.alignment     = TextAlignment.Center;
        _descriptionLabel.characterSize = 0.046f;
        _descriptionLabel.color         = new Color(0.88f, 0.88f, 0.88f);
        _descriptionLabel.text          = "";

        // ── Pulse ring (LineRenderer circle) ─────────────────────────────────
        GameObject ringGo = new GameObject("PMPulseRing");
        ringGo.transform.SetParent(_uiRoot.transform, false);
        ringGo.transform.localPosition = new Vector3(0f, 0.15f, 0f);

        _pulseRing = ringGo.AddComponent<LineRenderer>();
        _pulseRing.loop            = true;
        _pulseRing.useWorldSpace   = false;
        _pulseRing.startWidth      = 0.025f;
        _pulseRing.endWidth        = 0.025f;
        _pulseRing.positionCount   = 40;

        Shader spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader != null)
            _pulseRing.material = new Material(spriteShader);

        _pulseRing.startColor = Color.white;
        _pulseRing.endColor   = Color.white;

        int n = _pulseRing.positionCount;
        for (int i = 0; i < n; i++)
        {
            float angle = (float)i / n * Mathf.PI * 2f;
            _pulseRing.SetPosition(i, new Vector3(
                Mathf.Cos(angle) * ringRadius,
                Mathf.Sin(angle) * ringRadius,
                0f));
        }
    }
}
