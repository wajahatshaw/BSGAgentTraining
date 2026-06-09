using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Professional UI system displaying skill level gauges for each agent
/// Shows percentage of skillLevel vs desireLevel with modern design.
/// For RAG multi-zone scenes, enable <see cref="showOnlyPhysicalRagAgents"/> to list only
/// physical agents (P*) per zone and update as <see cref="SkillBasedActionSystem"/> rewards land.
/// </summary>
public class AgentSkillGaugeUI : MonoBehaviour
{
    [Header("RAG / zone physical agents")]
    [Tooltip("When true, only agents whose id starts with P (physical) are shown, ordered by zoneIndex. ReplicaSceneSetup RAG path sets this to true at runtime.")]
    public bool showOnlyPhysicalRagAgents = false;
    [Tooltip("When >= 0, only physical agents in this zone index are shown (-1 = all zones). JSONWorkflowSceneML / multiplayer embed use zone 0.")]
    public int showOnlyZoneIndex = -1;

    [Header("UI Settings")]
    public Canvas targetCanvas;
    public float gaugeWidth = 270f;
    public float gaugeHeight = 34f;
    public float spacing = 8f;
    public float topPadding = 20f;
    public float leftPadding = -20f; // Aligned with TrainingSpeedIndicator (X = 20) for left alignment
    public bool enableConsoleLogs = false;

    [Header("Zone 0 Step Indicator")]
    [Tooltip("Show completed Mental/Physical step counts for Zone 0 under the skill gauges.")]
    public bool showZone0StepIndicator = true;
    [Tooltip("Multiplayer Photon P1: only show M/P step counts (no P* skill gauge rows).")]
    public bool stepIndicatorOnlyMode;
    public float stepIndicatorHeight = 34f;
    
    [Header("Colors")]
    public Color backgroundColor = new Color(0.05f, 0.05f, 0.05f, 0.95f);
    public Color borderColor = new Color(0.4f, 0.4f, 0.4f, 1f);
    public Color fillColorLow = new Color(0.9f, 0.3f, 0.3f, 1f);  // Red for low progress
    public Color fillColorMid = new Color(1f, 0.8f, 0.2f, 1f);    // Yellow for mid progress
    public Color fillColorHigh = new Color(0.3f, 0.9f, 0.3f, 1f); // Green for high progress
    public Color completedColor = new Color(0.2f, 0.7f, 1f, 1f);  // Blue for completed
    public Color textColor = new Color(1f, 1f, 1f, 1f);  // Pure white for better contrast
    public Color agentNameColor = new Color(1f, 1f, 0.9f, 1f);  // Slightly off-white for agent names
    
    private SkillBasedActionSystem skillSystem;
    /// <summary>Set when <see cref="RebuildGaugesNow"/> runs before <see cref="Start"/> — skips delayed CreateGauges invoke.</summary>
    private bool gaugesBuiltEarly;
    private bool subscribedCompletionEvent;
    private Dictionary<string, GaugeElements> agentGauges = new Dictionary<string, GaugeElements>();
    private Dictionary<string, float> lastLoggedSkillLevel = new Dictionary<string, float>(); // Track last logged value to avoid spam
    private float logThrottleTime = 1f; // Log gauge updates every 1 second max
    private float lastGaugeLogTime = 0f;
    private GameObject zone0StepIndicatorPanel;
    private Text zone0StepIndicatorText;
    CognitivePhaseOrchestrator _zone0OrchestratorHud;
    
    // Struct to hold all UI elements for one agent's gauge
    private class GaugeElements
    {
        public GameObject containerPanel;
        public Text agentNameText;
        public Image fillImage;
        public Text percentageText;
        public Text skillLevelText;
    }
    
    void Start()
    {
        RagTrainingHudVisibility.Initialize(defaultVisible: false);
        RagTrainingHudVisibility.Changed += OnUserHudVisibilityChanged;

        EnsureCanvasAndSkillSystemReady();

        if (skillSystem == null)
        {
            Debug.LogError("❌ AgentSkillGaugeUI: SkillBasedActionSystem not found!");
            return;
        }

        TrySubscribeCompletionEvent();

        // Wait for data unless RAG path already called RebuildGaugesNow()
        if (!gaugesBuiltEarly)
            Invoke(nameof(CreateGauges), 1f);
        
        // Start updating gauges continuously (every 0.5 seconds)
        InvokeRepeating(nameof(UpdateAllGauges), 2f, 0.5f); // 2 updates/sec is plenty for a skill gauge

        SubscribeZone0OrchestratorHud();
        ApplyUserHudVisibility();
    }

    void OnUserHudVisibilityChanged(bool visible) => ApplyUserHudVisibility();
    
    void SubscribeZone0OrchestratorHud()
    {
        if (!showZone0StepIndicator) return;
        if (_zone0OrchestratorHud != null) return;
        _zone0OrchestratorHud = CognitivePhaseOrchestrator.GetOrCreateForZone(0);
        if (_zone0OrchestratorHud == null) return;
        _zone0OrchestratorHud.OnStepCompleted += HandleZone0OrchestratorHudRefresh;
        _zone0OrchestratorHud.OnBarrierReached += HandleZone0OrchestratorBarrierHudRefresh;
    }

    void HandleZone0OrchestratorHudRefresh(string stepId) => UpdateZone0StepIndicator();

    void HandleZone0OrchestratorBarrierHudRefresh(string barrierStepId, string closes, string opens) => UpdateZone0StepIndicator();

    void UnsubscribeZone0OrchestratorHud()
    {
        if (_zone0OrchestratorHud == null) return;
        _zone0OrchestratorHud.OnStepCompleted -= HandleZone0OrchestratorHudRefresh;
        _zone0OrchestratorHud.OnBarrierReached -= HandleZone0OrchestratorBarrierHudRefresh;
        _zone0OrchestratorHud = null;
    }
    
    void OnDestroy()
    {
        RagTrainingHudVisibility.Changed -= OnUserHudVisibilityChanged;
        UnsubscribeZone0OrchestratorHud();
        if (skillSystem != null && subscribedCompletionEvent)
        {
            skillSystem.OnAgentCompleted -= OnAgentCompleted;
            subscribedCompletionEvent = false;
        }
    }

    /// <summary>Shows or hides skill gauge rows and the Zone 0 step indicator per user HUD toggle.</summary>
    public void ApplyUserHudVisibility()
    {
        bool show = RagTrainingHudVisibility.IsVisible;

        if (targetCanvas != null)
            targetCanvas.gameObject.SetActive(true);

        foreach (var kv in agentGauges)
        {
            if (kv.Value?.containerPanel != null)
                kv.Value.containerPanel.SetActive(show);
        }

        if (zone0StepIndicatorPanel != null)
            zone0StepIndicatorPanel.SetActive(show);
    }

    /// <summary>
    /// Must run before any gauge GameObjects are parented (e.g. <see cref="RebuildGaugesNow"/> from ReplicaSceneSetup runs before <see cref="Start"/>).
    /// </summary>
    void EnsureCanvasAndSkillSystemReady()
    {
        if (leftPadding != 20f)
        {
            Debug.Log($"🔧 AgentSkillGaugeUI: Correcting leftPadding from {leftPadding} to 20 for alignment");
            leftPadding = 20f;
        }

        // Never use FindObjectOfType<Canvas>() — ReplicaSceneSetup's loading overlay is usually the
        // first canvas; parenting gauges there destroys them when DestroyLoadingOverlay runs.
        if (targetCanvas == null)
        {
            GameObject existing = GameObject.Find("AgentSkillGaugeCanvas");
            if (existing != null)
                targetCanvas = existing.GetComponent<Canvas>();
            if (targetCanvas == null)
                CreateCanvas();
        }

        if (skillSystem == null)
            skillSystem = FindObjectOfType<SkillBasedActionSystem>();
    }

    void TrySubscribeCompletionEvent()
    {
        if (subscribedCompletionEvent || skillSystem == null) return;
        skillSystem.OnAgentCompleted += OnAgentCompleted;
        subscribedCompletionEvent = true;
    }
    
    void CreateCanvas()
    {
        GameObject canvasGO = new GameObject("AgentSkillGaugeCanvas");
        targetCanvas = canvasGO.AddComponent<Canvas>();
        targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        targetCanvas.sortingOrder = 2000; // Above loading overlay and most HUD
        
        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        // Use ScaleWithScreenSize with match=0 to keep side panels anchored
        // Match=0 means scale based on width, keeping left/right anchored elements in place
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0f; // Match width (0) to keep horizontal positioning stable
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        
        canvasGO.AddComponent<GraphicRaycaster>();
        
        // Ensure canvas is active and visible
        canvasGO.SetActive(true);
        targetCanvas.enabled = true;
        
        Debug.Log("✅ Created AgentSkillGaugeCanvas (enabled and visible)");
    }
    
    void CreateGauges()
    {
        EnsureCanvasAndSkillSystemReady();
        TrySubscribeCompletionEvent();

        if (targetCanvas == null)
        {
            Debug.LogError("❌ AgentSkillGaugeUI: No Canvas — cannot create gauges.");
            return;
        }

        if (skillSystem == null)
        {
            Debug.LogError("❌ AgentSkillGaugeUI: Cannot create gauges - SkillBasedActionSystem is null");
            return;
        }

        var agentProfiles = skillSystem.GetAllAgentProfiles();
        if (agentProfiles == null || agentProfiles.Count == 0)
        {
            Debug.LogWarning("⚠️ AgentSkillGaugeUI: No agent profiles found, retrying in 1 second...");
            Invoke(nameof(CreateGauges), 1f);
            return;
        }

        if (showOnlyPhysicalRagAgents && !stepIndicatorOnlyMode)
        {
            var physicalOrdered = GetOrderedPhysicalProfiles();
            if (physicalOrdered.Count == 0)
            {
                Debug.LogWarning("⚠️ AgentSkillGaugeUI: No physical (P*) agents yet — retrying in 1 second (RAG may still be loading)...");
                Invoke(nameof(CreateGauges), 1f);
                return;
            }

            ClearAllGauges();
            Debug.Log($"📊 Creating skill gauges for {physicalOrdered.Count} physical zone agent(s)");
            for (int i = 0; i < physicalOrdered.Count; i++)
                CreateGaugeForAgent(physicalOrdered[i].profile, physicalOrdered[i].resolvedId, i);
            EnsureZone0StepIndicator();
        }
        else if (stepIndicatorOnlyMode)
        {
            ClearAllGauges();
            EnsureZone0StepIndicator();
        }
        else
        {
            Debug.Log($"📊 Creating skill gauges for {agentProfiles.Count} agents");
            int index = 0;
            foreach (var agentPair in agentProfiles)
            {
                CreateGaugeForAgent(agentPair.Value, agentPair.Key, index);
                index++;
            }
        }

        Debug.Log($"✅ Created {agentGauges.Count} skill gauges");

        ApplyUserHudVisibility();
        Invoke(nameof(RefreshAllGauges), 0.1f);
    }

    struct PhysicalEntry
    {
        public string resolvedId;
        public AgentProfile profile;
    }

    private List<PhysicalEntry> _cachedPhysicalProfiles = null;
    private int _cachedProfileCount = -1;
    private int _cachedZoneFilter = int.MinValue;

    /// <summary>Physical RAG agents (P1…Pn), sorted by zone then id. Cached until profile count changes.</summary>
    List<PhysicalEntry> GetOrderedPhysicalProfiles()
    {
        if (skillSystem == null) return _cachedPhysicalProfiles ?? (_cachedPhysicalProfiles = new List<PhysicalEntry>());

        var allProfiles = skillSystem.GetAllAgentProfiles();
        if (allProfiles == null) return _cachedPhysicalProfiles ?? (_cachedPhysicalProfiles = new List<PhysicalEntry>());

        // Rebuild only when the number of profiles or zone filter changes (agents added mid-session)
        if (_cachedPhysicalProfiles != null && allProfiles.Count == _cachedProfileCount && _cachedZoneFilter == showOnlyZoneIndex)
            return _cachedPhysicalProfiles;

        _cachedProfileCount = allProfiles.Count;
        _cachedZoneFilter = showOnlyZoneIndex;
        _cachedPhysicalProfiles = new List<PhysicalEntry>();

        foreach (var kvp in allProfiles)
        {
            AgentProfile ap = kvp.Value;
            if (ap == null) continue;
            string resolvedId = !string.IsNullOrEmpty(ap.agentId) ? ap.agentId : kvp.Key;
            if (!IsPhysicalRagAgent(ap, kvp.Key)) continue;
            if (showOnlyZoneIndex >= 0 && ap.zoneIndex != showOnlyZoneIndex) continue;
            _cachedPhysicalProfiles.Add(new PhysicalEntry { resolvedId = resolvedId, profile = ap });
        }

        _cachedPhysicalProfiles.Sort((a, b) =>
        {
            int z = a.profile.zoneIndex.CompareTo(b.profile.zoneIndex);
            return z != 0 ? z : string.Compare(a.resolvedId, b.resolvedId, StringComparison.OrdinalIgnoreCase);
        });

        return _cachedPhysicalProfiles;
    }

    static bool IsPhysicalRagAgent(AgentProfile ap, string dictKey)
    {
        string id = !string.IsNullOrEmpty(ap.agentId) ? ap.agentId : dictKey;
        if (id.Length >= 2 && id.StartsWith("P", StringComparison.OrdinalIgnoreCase) && char.IsDigit(id[1]))
            return true;
        if (!string.IsNullOrEmpty(ap.role) && ap.role.Trim().Equals("Physical", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    void ClearAllGauges()
    {
        foreach (var kv in agentGauges)
        {
            if (kv.Value?.containerPanel != null)
                Destroy(kv.Value.containerPanel);
        }
        agentGauges.Clear();
    }
    
    void CreateGaugeForAgent(AgentProfile agent, string gaugeKey, int index)
    {
        if (agent == null || string.IsNullOrEmpty(gaugeKey)) return;

        // Create container panel
        GameObject containerGO = new GameObject($"Gauge_{gaugeKey}");
        containerGO.transform.SetParent(targetCanvas.transform, false);
        
        RectTransform containerRect = containerGO.AddComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0, 1);
        containerRect.anchorMax = new Vector2(0, 1);
        containerRect.pivot = new Vector2(0, 1);
        
        // CRITICAL: Align with TrainingSpeedIndicator X position (20)
        // TrainingSpeedIndicator: anchor (0,0) bottom-left, position (20, 20) = 20px from left
        // Gauges: anchor (0,1) top-left, so X=20 aligns the left edges perfectly
        float xPos = leftPadding; // Should be 20 to match TrainingSpeedIndicator
        float yPos = -(topPadding + (gaugeHeight + spacing) * index);
        containerRect.anchoredPosition = new Vector2(xPos, yPos);
        containerRect.sizeDelta = new Vector2(gaugeWidth, gaugeHeight);
        
        // Background
        Image bgImage = containerGO.AddComponent<Image>();
        bgImage.color = backgroundColor;
        
        // Border - Removed outline to eliminate visual offset that causes misalignment
        // If border is needed, use Image border instead
        // Outline outline = containerGO.AddComponent<Outline>();
        // outline.effectColor = borderColor;
        // outline.effectDistance = new Vector2(1, -1);
        
        // Agent Name Text (left side)
        GameObject nameTextGO = new GameObject("AgentName");
        nameTextGO.transform.SetParent(containerGO.transform, false);
        
        RectTransform nameRect = nameTextGO.AddComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0, 0);
        nameRect.anchorMax = new Vector2(0.32f, 1);
        // Match TrainingSpeedIndicator and EpisodeCounterTimer text padding: 10-12px
        nameRect.offsetMin = new Vector2(10, 0); // 10px left padding to match other components' text
        nameRect.offsetMax = new Vector2(0, 0);
        
        Text nameText = nameTextGO.AddComponent<Text>();
        nameText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        nameText.fontSize = 13;
        nameText.fontStyle = FontStyle.Bold;
        nameText.color = agentNameColor;
        nameText.alignment = TextAnchor.MiddleLeft;
        nameText.text = GetAgentDisplayName(agent, gaugeKey);
        
        // Add text shadow for better visibility
        Shadow nameShadow = nameTextGO.AddComponent<Shadow>();
        nameShadow.effectColor = new Color(0, 0, 0, 0.8f);
        nameShadow.effectDistance = new Vector2(2, -2);
        
        // Progress Bar Container (center)
        GameObject barContainerGO = new GameObject("ProgressBarContainer");
        barContainerGO.transform.SetParent(containerGO.transform, false);
        
        RectTransform barContainerRect = barContainerGO.AddComponent<RectTransform>();
        barContainerRect.anchorMin = new Vector2(0.33f, 0.14f);
        barContainerRect.anchorMax = new Vector2(0.64f, 0.86f);
        barContainerRect.offsetMin = Vector2.zero;
        barContainerRect.offsetMax = Vector2.zero;
        
        // Bar Background
        Image barBgImage = barContainerGO.AddComponent<Image>();
        barBgImage.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        
        // Fill Image (progress)
        GameObject fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(barContainerGO.transform, false);
        
        RectTransform fillRect = fillGO.AddComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0, 0);
        fillRect.anchorMax = new Vector2(0, 1);
        fillRect.pivot = new Vector2(0, 0.5f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        
        Image fillImage = fillGO.AddComponent<Image>();
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImage.color = fillColorLow;
        
        // Percentage Text (on the bar)
        GameObject percentTextGO = new GameObject("PercentageText");
        percentTextGO.transform.SetParent(barContainerGO.transform, false);
        
        RectTransform percentRect = percentTextGO.AddComponent<RectTransform>();
        percentRect.anchorMin = Vector2.zero;
        percentRect.anchorMax = Vector2.one;
        percentRect.offsetMin = Vector2.zero;
        percentRect.offsetMax = Vector2.zero;
        
        Text percentText = percentTextGO.AddComponent<Text>();
        percentText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        percentText.fontSize = 12;
        percentText.fontStyle = FontStyle.Bold;
        percentText.color = new Color(1f, 1f, 1f, 1f);
        percentText.alignment = TextAnchor.MiddleCenter;
        percentText.text = "0%";
        
        // Add shadow to percentage text for better readability
        Shadow percentShadow = percentTextGO.AddComponent<Shadow>();
        percentShadow.effectColor = new Color(0, 0, 0, 0.9f);
        percentShadow.effectDistance = new Vector2(2, -2);
        
        // Skill Level Text (right side)
        GameObject skillTextGO = new GameObject("SkillLevelText");
        skillTextGO.transform.SetParent(containerGO.transform, false);
        
        RectTransform skillRect = skillTextGO.AddComponent<RectTransform>();
        skillRect.anchorMin = new Vector2(0.65f, 0);
        skillRect.anchorMax = new Vector2(1, 1);
        skillRect.offsetMin = new Vector2(8, 0);
        skillRect.offsetMax = new Vector2(-15, 0);
        
        Text skillText = skillTextGO.AddComponent<Text>();
        skillText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        skillText.fontSize = 11;
        skillText.fontStyle = FontStyle.Normal;
        skillText.color = textColor;
        skillText.alignment = TextAnchor.MiddleRight;
        float initialCap = GetDesireCap(agent);
        skillText.text = $"0/{initialCap:F0}";
        
        // Add shadow to skill level text for better readability
        Shadow skillShadow = skillTextGO.AddComponent<Shadow>();
        skillShadow.effectColor = new Color(0, 0, 0, 0.8f);
        skillShadow.effectDistance = new Vector2(1, -1);
        
        // Store references
        GaugeElements elements = new GaugeElements
        {
            containerPanel = containerGO,
            agentNameText = nameText,
            fillImage = fillImage,
            percentageText = percentText,
            skillLevelText = skillText
        };
        
        agentGauges[gaugeKey] = elements;

        UpdateGauge(gaugeKey);

        Debug.Log($"✅ Created gauge for {gaugeKey}");
    }

    static float GetDesireCap(AgentProfile agent)
    {
        if (agent == null) return 100f;
        if (agent.desireLevel > 0f) return agent.desireLevel;
        if (agent.agentDesireLevel > 0f) return agent.agentDesireLevel;
        return 100f;
    }
    
    void UpdateAllGauges()
    {
        if (skillSystem == null) return;

        if (showOnlyPhysicalRagAgents)
            EnsurePhysicalGaugesExist();

        var agentProfiles = skillSystem.GetAllAgentProfiles();
        if (agentProfiles == null) return;

        UpdateGaugePositions();

        if (showOnlyPhysicalRagAgents)
        {
            foreach (var entry in GetOrderedPhysicalProfiles())
                UpdateGauge(entry.resolvedId);
        }
        else
        {
            foreach (var agentPair in agentProfiles)
                UpdateGauge(agentPair.Key);
        }

        UpdateZone0StepIndicator();
    }

    /// <summary>
    /// When RAG profiles load after startup, create any missing physical-agent rows.
    /// </summary>
    void EnsurePhysicalGaugesExist()
    {
        if (skillSystem == null) return;

        var ordered = GetOrderedPhysicalProfiles();
        bool added = false;
        for (int i = 0; i < ordered.Count; i++)
        {
            string id = ordered[i].resolvedId;
            if (agentGauges.ContainsKey(id)) continue;
            CreateGaugeForAgent(ordered[i].profile, id, i);
            added = true;
        }

        if (added)
            UpdateGaugePositions();

        EnsureZone0StepIndicator();
        ApplyUserHudVisibility();
    }
    
    void UpdateGaugePositions()
    {
        if (skillSystem == null) return;

        IEnumerable<string> orderedKeys;
        if (showOnlyPhysicalRagAgents)
            orderedKeys = GetOrderedPhysicalProfiles().Select(e => e.resolvedId);
        else
        {
            var agentProfiles = skillSystem.GetAllAgentProfiles();
            if (agentProfiles == null) return;
            orderedKeys = agentProfiles.OrderBy(kvp =>
            {
                string id = kvp.Key;
                if (id.Contains("Technician_01") || id.Contains("SIMPLE_Technician_01")) return 0;
                if (id.Contains("Technician_02") || id.Contains("SIMPLE_Technician_02")) return 1;
                if (id.Contains("Supervisor_01") || id.Contains("SIMPLE_Supervisor_01")) return 2;
                if (id.Contains("Supervisor_02") || id.Contains("SIMPLE_Supervisor_02")) return 3;
                return 99;
            }).Select(kvp => kvp.Key);
        }

        int index = 0;
        foreach (string agentKey in orderedKeys)
        {
            if (!agentGauges.ContainsKey(agentKey)) { index++; continue; }

            GaugeElements elements = agentGauges[agentKey];
            if (elements?.containerPanel == null) { index++; continue; }

            RectTransform containerRect = elements.containerPanel.GetComponent<RectTransform>();
            if (containerRect != null)
            {
                float xPos = leftPadding;
                float yPos = -(topPadding + (gaugeHeight + spacing) * index);
                containerRect.anchoredPosition = new Vector2(xPos, yPos);

                if (Mathf.Abs(containerRect.anchoredPosition.x - xPos) > 0.1f)
                    containerRect.anchoredPosition = new Vector2(xPos, containerRect.anchoredPosition.y);
            }
            index++;
        }

        UpdateZone0StepIndicatorPosition(index);
    }

    void EnsureZone0StepIndicator()
    {
        if (!showZone0StepIndicator || targetCanvas == null) return;
        if (zone0StepIndicatorPanel != null && zone0StepIndicatorText != null) return;

        zone0StepIndicatorPanel = new GameObject("Zone0StepCompletionIndicator");
        zone0StepIndicatorPanel.transform.SetParent(targetCanvas.transform, false);

        RectTransform rect = zone0StepIndicatorPanel.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.sizeDelta = new Vector2(gaugeWidth, stepIndicatorHeight);

        Image bg = zone0StepIndicatorPanel.AddComponent<Image>();
        bg.color = new Color(0.04f, 0.04f, 0.04f, 0.92f);

        GameObject textGo = new GameObject("StepCountsText");
        textGo.transform.SetParent(zone0StepIndicatorPanel.transform, false);

        RectTransform textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10f, 0f);
        textRect.offsetMax = new Vector2(-10f, 0f);

        zone0StepIndicatorText = textGo.AddComponent<Text>();
        zone0StepIndicatorText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        zone0StepIndicatorText.fontSize = 13;
        zone0StepIndicatorText.fontStyle = FontStyle.Bold;
        zone0StepIndicatorText.color = textColor;
        zone0StepIndicatorText.alignment = TextAnchor.MiddleLeft;

        Shadow shadow = textGo.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.85f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);

        UpdateZone0StepIndicatorPosition(agentGauges.Count);
        UpdateZone0StepIndicator();
    }

    void UpdateZone0StepIndicatorPosition(int gaugeRowCount)
    {
        if (!showZone0StepIndicator || zone0StepIndicatorPanel == null) return;

        RectTransform rect = zone0StepIndicatorPanel.GetComponent<RectTransform>();
        if (rect == null) return;

        float xPos = leftPadding;
        int rows = stepIndicatorOnlyMode ? 0 : gaugeRowCount;
        float yPos = -(topPadding + (gaugeHeight + spacing) * rows + (rows > 0 ? spacing : 0f));
        if (stepIndicatorOnlyMode)
            yPos = -topPadding;
        rect.anchoredPosition = new Vector2(xPos, yPos);
        rect.sizeDelta = new Vector2(gaugeWidth, stepIndicatorHeight);
    }

    void UpdateZone0StepIndicator()
    {
        if (!showZone0StepIndicator) return;
        SubscribeZone0OrchestratorHud();
        EnsureZone0StepIndicator();
        if (zone0StepIndicatorText == null) return;

        CognitivePhaseOrchestrator orch = CognitivePhaseOrchestrator.GetOrCreateForZone(0);
        if (orch == null)
        {
            zone0StepIndicatorText.text = "Zone 0 Steps  M: 0  P: 0";
            return;
        }

        orch.GetCompletedStepCounts(out int mentalDone, out int physicalDone);
        orch.GetTotalStepCounts(out int mentalTotal, out int physicalTotal);

        string mental = mentalTotal > 0 ? $"{mentalDone}/{mentalTotal}" : $"{mentalDone}";
        string physical = physicalTotal > 0 ? $"{physicalDone}/{physicalTotal}" : $"{physicalDone}";
        zone0StepIndicatorText.text = $"Zone 0 Steps  M: {mental}   P: {physical}";
    }
    
    void UpdateGauge(string agentId)
    {
        if (!agentGauges.ContainsKey(agentId)) return;
        if (skillSystem == null) return;
        
        AgentProfile agent = skillSystem.GetAgentProfile(agentId);
        if (agent == null) return;

        GaugeElements elements = agentGauges[agentId];

        float cap = GetDesireCap(agent);
        if (cap <= 0f) cap = 100f;
        float percentage = (agent.skillLevel / cap) * 100f;
        percentage = Mathf.Clamp(percentage, 0f, 100f);
        
        // Update fill amount (0 to 1)
        float fillAmount = percentage / 100f;
        elements.fillImage.fillAmount = fillAmount;
        
        // Update fill color based on progress
        if (agent.isCompleted)
        {
            elements.fillImage.color = completedColor;
        }
        else if (percentage >= 75f)
        {
            elements.fillImage.color = fillColorHigh;
        }
        else if (percentage >= 40f)
        {
            elements.fillImage.color = fillColorMid;
        }
        else
        {
            elements.fillImage.color = fillColorLow;
        }
        
        // Whole numbers only (per-step rewards are +1)
        string completedMark = agent.isCompleted ? " ✓" : "";
        elements.percentageText.text = $"{Mathf.RoundToInt(percentage)}%{completedMark}";
        elements.skillLevelText.text = $"{Mathf.RoundToInt(agent.skillLevel)}/{Mathf.RoundToInt(cap)}";
        
        // If completed, add visual feedback
        if (agent.isCompleted)
        {
            elements.agentNameText.color = completedColor;
            elements.skillLevelText.color = completedColor;
        }
        
        // Always log when skill changes — key visibility for debugging
        if (!lastLoggedSkillLevel.ContainsKey(agentId) || Mathf.Abs(agent.skillLevel - lastLoggedSkillLevel.GetValueOrDefault(agentId)) > 0.05f)
        {
            lastLoggedSkillLevel[agentId] = agent.skillLevel;
            string displayName = GetAgentDisplayName(agent, agentId);
            string completedStatus = agent.isCompleted ? " [COMPLETED ✓]" : "";
            Debug.Log($"[SKILL-GAUGE] 📊 {displayName}: {Mathf.RoundToInt(agent.skillLevel)}/{Mathf.RoundToInt(cap)} ({Mathf.RoundToInt(percentage)}%){completedStatus}");
        }
    }
    
    void OnAgentCompleted(string agentId)
    {
        Debug.Log($"🎉 AgentSkillGaugeUI: Agent {agentId} completed! Updating gauge...");
        UpdateGauge(agentId);
        
        // Add celebration effect (optional - flash the gauge)
        if (agentGauges.ContainsKey(agentId))
        {
            StartCoroutine(FlashGauge(agentId));
        }
    }
    
    System.Collections.IEnumerator FlashGauge(string agentId)
    {
        if (!agentGauges.ContainsKey(agentId)) yield break;
        
        GaugeElements elements = agentGauges[agentId];
        Color originalBg = elements.containerPanel.GetComponent<Image>().color;
        
        // Flash 3 times
        for (int i = 0; i < 3; i++)
        {
            elements.containerPanel.GetComponent<Image>().color = new Color(0.3f, 0.8f, 1f, 0.9f);
            yield return new WaitForSeconds(0.2f);
            elements.containerPanel.GetComponent<Image>().color = originalBg;
            yield return new WaitForSeconds(0.2f);
        }
    }
    
    string GetAgentDisplayName(AgentProfile profile, string agentId)
    {
        if (profile != null && !string.IsNullOrEmpty(profile.role))
        {
            string r = profile.role.Trim();
            if (!r.Equals("Physical", StringComparison.OrdinalIgnoreCase) &&
                !r.Equals("Mental", StringComparison.OrdinalIgnoreCase))
            {
                const int maxLen = 40;
                if (r.Length > maxLen)
                    r = r.Substring(0, maxLen - 1) + "…";
                return r;
            }
        }

        if (string.IsNullOrEmpty(agentId)) return "Unknown";

        if (agentId.Contains("SIMPLE_"))
        {
            string[] parts = agentId.Split('_');
            if (parts.Length >= 3)
            {
                string role = parts[1];
                string number = parts[2];
                if (role.Length > 0)
                    role = char.ToUpper(role[0]) + role.Substring(1).ToLower();
                return $"{role} {number}";
            }
        }

        if (profile != null && agentId.StartsWith("P", StringComparison.OrdinalIgnoreCase))
            return $"Zone {profile.zoneIndex} · {agentId}";

        return agentId;
    }
    
    /// <summary>
    /// Call after RAG/scene data is loaded so physical-agent rows are built immediately.
    /// </summary>
    public void RebuildGaugesNow()
    {
        CancelInvoke(nameof(CreateGauges));
        _cachedPhysicalProfiles = null;
        _cachedProfileCount = -1;
        _cachedZoneFilter = int.MinValue;
        gaugesBuiltEarly = true;
        EnsureCanvasAndSkillSystemReady();
        TrySubscribeCompletionEvent();
        CreateGauges();
    }

    [ContextMenu("Refresh All Gauges")]
    public void RefreshAllGauges()
    {
        Debug.Log("🔄 Refreshing all skill gauges with updated styling...");
        
        if (agentGauges == null || agentGauges.Count == 0)
        {
            Debug.LogWarning("⚠️ No gauges to refresh");
            return;
        }
        
        foreach (var gaugePair in agentGauges)
        {
            GaugeElements elements = gaugePair.Value;
            
            // Update background color
            Image bgImage = elements.containerPanel.GetComponent<Image>();
            if (bgImage != null)
            {
                bgImage.color = backgroundColor;
            }
            
            // Update outline
            Outline outline = elements.containerPanel.GetComponent<Outline>();
            if (outline != null)
            {
                outline.effectColor = borderColor;
                outline.effectDistance = new Vector2(3, -3);
            }
            
            // Update text colors and shadows
            UpdateTextStyling(elements.agentNameText, agentNameColor);
            UpdateTextStyling(elements.percentageText, Color.white);
            UpdateTextStyling(elements.skillLevelText, textColor);
        }
        
        Debug.Log("✅ All gauges refreshed with new styling");
    }
    
    void UpdateTextStyling(Text textComponent, Color textColor)
    {
        if (textComponent == null) return;
        
        textComponent.color = textColor;
        
        // Update shadow if it exists
        Shadow shadow = textComponent.GetComponent<Shadow>();
        if (shadow != null)
        {
            shadow.effectColor = new Color(0, 0, 0, 0.8f);
            shadow.effectDistance = new Vector2(2, -2);
        }
    }
}

