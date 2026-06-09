using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Distance to Target Visual Meter - Shows progress bar for each agent's distance to their current target
/// Displays real-time distance progress with color coding: Red (far) → Yellow (close) → Green (at target)
/// 
/// DATA SOURCE: P-agent positions from <see cref="BSGMLAgent"/>. In replica scenes with
/// <see cref="MentalAgentSpawner"/>, distances and target labels stay hidden until
/// <see cref="ZoneDeclarativeMemory.cognitiveReady"/> — same gate as physical movement.
/// After cognitive completes, the first operational target label prefers
/// <see cref="ZoneDeclarativeMemory.resolvedTargetId"/> (declarative / M-agent output), not raw JSON alone.
/// Distances use <see cref="AgentSequenceManager"/> positions (seeded by the same sync as the P-agent).
/// </summary>
public class DistanceToTargetMeter : MonoBehaviour
{
    [Header("UI References")]
    public Canvas meterCanvas;
    public GameObject meterPanel;
    public Transform meterListParent;
    
    [Header("Panel Settings")]
    // Position below EpisodeCounterTimer: EpisodeCounterTimer is at (20, -278) with size (240, 110)
    // So EpisodeCounterTimer ends at Y = -278 - 110 = -388
    // Add spacing of 10px, so DistanceToTargetMeter starts at Y = -388 - 10 = -398
    // Align X position with EpisodeCounterTimer and TrainingSpeedIndicator (20)
    public Vector2 panelPosition = new Vector2(16, -398); // Top-left; tweak Y if stacked UI overlaps
    public Vector2 panelSize = new Vector2(200, 220);    // Compact panel for 4 agents
    public float updateInterval = 0.2f; // Update 5 times per second for smooth progress
    
    [Header("Cognitive phase (replica)")]
    [Tooltip("Shown while ZoneDeclarativeMemory.cognitiveReady is false (M-agents still running).")]
    public string cognitiveLoadingMessage = "Cognitive processing…";
    [Tooltip("Optional progress suffix; {0}=completed steps, {1}=total.")]
    public string cognitiveProgressFormat = "{0}/{1} steps";
    public Color cognitiveLoadingTextColor = new Color(1f, 0.88f, 0.25f, 1f);
    
    [Header("Visual Settings")]
    public Color backgroundColor = new Color(0, 0, 0, 0.85f);
    public Color headerColor = new Color(0.2f, 0.6f, 0.2f, 1f);
    public Color textColor = Color.white;
    public Color progressFarColor = new Color(0.9f, 0.3f, 0.3f, 1f); // Red for far (>70%)
    public Color progressCloseColor = new Color(1f, 0.8f, 0.2f, 1f);   // Yellow for close (30-70%)
    public Color progressAtTargetColor = new Color(0.3f, 0.9f, 0.3f, 1f); // Green for at target (<30%)
    public Color progressBarBackgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    
    private AgentSequenceManager sequenceManager;
    private Dictionary<string, GameObject> agentMeterPanels = new Dictionary<string, GameObject>();
    private Dictionary<string, BSGMLAgent> agentComponents = new Dictionary<string, BSGMLAgent>();
    private float lastUpdateTime = 0f;
    private const float MAX_DISTANCE = 22f; // Normalize progress bar (typical in-zone reach)
    
    void Start()
    {
        Debug.Log("🚀 DistanceToTargetMeter: Starting initialization...");
        
        // Find sequence manager
        sequenceManager = AgentSequenceManager.Instance;
        if (sequenceManager == null)
        {
            Debug.LogWarning("⚠️ DistanceToTargetMeter: AgentSequenceManager not found yet, will retry...");
            Invoke("RetryInitialize", 2f);
            return;
        }
        
        Debug.Log("✅ DistanceToTargetMeter: Found AgentSequenceManager");
        
        ZoneDeclarativeMemory.RebuildRegistryFromScene();
        
        // Find all BSGMLAgent components
        FindAgentComponents();
        
        // Create the meter panel UI
        CreateMeterPanel();
        
        // Initial update with a small delay
        StartCoroutine(InitialUpdateAfterDelay());
    }
    
    void RetryInitialize()
    {
        sequenceManager = AgentSequenceManager.Instance;
        if (sequenceManager != null)
        {
            Debug.Log("✅ DistanceToTargetMeter: Successfully found AgentSequenceManager on retry");
            FindAgentComponents();
            CreateMeterPanel();
            StartCoroutine(InitialUpdateAfterDelay());
        }
        else
        {
            Debug.LogWarning("⚠️ DistanceToTargetMeter: Still no AgentSequenceManager, will keep retrying...");
            Invoke("RetryInitialize", 2f);
        }
    }
    
    void FindAgentComponents()
    {
        agentComponents.Clear();
        BSGMLAgent[] agents = FindObjectsOfType<BSGMLAgent>();
        foreach (var agent in agents)
        {
            if (agent != null && !string.IsNullOrEmpty(agent.agentId))
            {
                agentComponents[agent.agentId] = agent;
                string displayName = GetAgentDisplayName(agent.agentId);
                Debug.Log($"✅ DistanceToTargetMeter: Found agent - agentId: {agent.agentId}, displayName: {displayName}");
            }
        }
        
        // Log summary
        if (Time.frameCount % 180 == 0) // Log every 3 seconds
        {
            Debug.Log($"📋 DistanceToTargetMeter: Found {agentComponents.Count} agents total");
            foreach (var kvp in agentComponents)
            {
                Debug.Log($"   - {kvp.Key} → display: {GetAgentDisplayName(kvp.Key)}");
            }
        }
    }
    
    void Update()
    {
        // Update distance display at regular intervals
        if (Time.time - lastUpdateTime >= updateInterval)
        {
            UpdateDistanceDisplay();
            lastUpdateTime = Time.time;
        }
    }
    
    void CreateMeterPanel()
    {
        // Create main canvas if not assigned
        if (meterCanvas == null)
        {
            GameObject canvasGO = new GameObject("DistanceMeterCanvas");
            meterCanvas = canvasGO.AddComponent<Canvas>();
            meterCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            meterCanvas.sortingOrder = 95; // Below StepEfficiencyIndicator but above other UI
            
            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0f;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            
            canvasGO.AddComponent<GraphicRaycaster>();
            canvasGO.SetActive(true);
            meterCanvas.enabled = true;
            
            Debug.Log("✅ DistanceToTargetMeter: Created and activated DistanceMeterCanvas");
        }
        
        // Create main panel
        if (meterPanel == null)
        {
            meterPanel = new GameObject("DistanceMeterPanel");
            meterPanel.transform.SetParent(meterCanvas.transform, false);
            
            Image panelImage = meterPanel.AddComponent<Image>();
            panelImage.color = backgroundColor;
            
            RectTransform panelRect = meterPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0, 1); // Top-left anchor (aligned with EpisodeCounterTimer)
            panelRect.anchorMax = new Vector2(0, 1);
            panelRect.pivot = new Vector2(0, 1); // Pivot at top-left
            panelRect.anchoredPosition = panelPosition; // Positive X, negative Y from top-left
            panelRect.sizeDelta = panelSize;
            
            meterPanel.SetActive(true);
            
            Debug.Log($"✅ DistanceToTargetMeter: Panel positioned at {panelPosition}, size {panelSize}");
        }
        
        CreateHeader();
        CreateMeterListParent();
        
        if (meterPanel != null)
        {
            meterPanel.SetActive(true);
        }
        
        Debug.Log("✅ DistanceToTargetMeter: Meter panel created successfully");
    }
    
    void CreateHeader()
    {
        GameObject headerBG = new GameObject("HeaderBackground");
        headerBG.transform.SetParent(meterPanel.transform, false);
        headerBG.transform.SetAsLastSibling();
        
        Image headerImage = headerBG.AddComponent<Image>();
        headerImage.color = headerColor;
        
        RectTransform headerRect = headerBG.GetComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0, 1);
        headerRect.anchorMax = new Vector2(1, 1);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.anchoredPosition = new Vector2(0, 0);
        headerRect.offsetMin = new Vector2(0, -20);
        headerRect.offsetMax = new Vector2(0, 0);
        
        GameObject headerTextGO = new GameObject("HeaderText");
        headerTextGO.transform.SetParent(headerBG.transform, false);
        
        Text headerText = headerTextGO.AddComponent<Text>();
        headerText.text = "DISTANCE TO TARGET";
        headerText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        headerText.fontSize = 10;
        headerText.color = textColor;
        headerText.fontStyle = FontStyle.Bold;
        headerText.alignment = TextAnchor.MiddleCenter;
        
        RectTransform textRect = headerText.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
    }
    
    void CreateMeterListParent()
    {
        if (meterListParent == null)
        {
            GameObject listParent = new GameObject("MeterListParent");
            listParent.transform.SetParent(meterPanel.transform, false);
            
            RectTransform listRect = listParent.AddComponent<RectTransform>();
            listRect.anchorMin = new Vector2(0, 0);
            listRect.anchorMax = new Vector2(1, 1);
            listRect.offsetMin = new Vector2(10, 10);
            listRect.offsetMax = new Vector2(-6, -22);
            
            listParent.transform.SetSiblingIndex(0);
            
            ScrollRect scrollRect = listParent.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            
            GameObject viewport = new GameObject("Viewport");
            viewport.transform.SetParent(listParent.transform, false);
            viewport.transform.SetAsFirstSibling();
            
            RectTransform viewportRect = viewport.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.sizeDelta = Vector2.zero;
            
            Image viewportImage = viewport.AddComponent<Image>();
            viewportImage.color = new Color(0, 0, 0, 0);
            Mask viewportMask = viewport.AddComponent<Mask>();
            viewportMask.showMaskGraphic = false;
            
            GameObject content = new GameObject("Content");
            content.transform.SetParent(listParent.transform, false);
            content.transform.SetSiblingIndex(1);
            
            RectTransform contentRect = content.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(0, 0);
            
            VerticalLayoutGroup layoutGroup = content.AddComponent<VerticalLayoutGroup>();
            layoutGroup.spacing = 4f;
            layoutGroup.padding = new RectOffset(3, 3, 2, 3);
            layoutGroup.childControlHeight = false;
            layoutGroup.childControlWidth = true;
            layoutGroup.childForceExpandWidth = true;
            
            ContentSizeFitter sizeFitter = content.AddComponent<ContentSizeFitter>();
            sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            
            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            
            meterListParent = content.transform;
            
            RectMask2D rectMask = listParent.AddComponent<RectMask2D>();
        }
    }
    
    private System.Collections.IEnumerator InitialUpdateAfterDelay()
    {
        yield return null;
        yield return new WaitForSeconds(0.5f);
        UpdateDistanceDisplay();
    }
    
    void UpdateDistanceDisplay()
    {
        if (sequenceManager == null)
        {
            sequenceManager = AgentSequenceManager.Instance;
            if (sequenceManager == null)
            {
                return;
            }
        }

        if (Time.frameCount % 120 == 0)
            ZoneDeclarativeMemory.RebuildRegistryFromScene();
        
        // Refresh agent components periodically
        if (Time.time % 2f < updateInterval)
        {
            FindAgentComponents();
        }
        
        if (meterPanel == null || meterListParent == null)
        {
            CreateMeterPanel();
            return;
        }
        
        if (!meterPanel.activeSelf)
        {
            meterPanel.SetActive(true);
        }
        
        // Get all agents with sequences
        // CRITICAL FIX: Directly map each agent by display name to their sequence
        // This is the most reliable method - no fallbacks that could cause confusion
        var allSequences = new Dictionary<string, AgentSequenceData>();
        var agentIdToSequenceKey = new Dictionary<string, string>(); // Map component agentId to sequence key
        
        // Get all 4 sequences directly
        var tech01Seq = sequenceManager.GetSequence("SIMPLE_Technician_01");
        var tech02Seq = sequenceManager.GetSequence("SIMPLE_Technician_02");
        var sup01Seq = sequenceManager.GetSequence("SIMPLE_Supervisor_01");
        var sup02Seq = sequenceManager.GetSequence("SIMPLE_Supervisor_02");
        
        // CRITICAL: Log first targets for verification - this will show if sequences are different
        if (Time.frameCount % 180 == 0) // Log every 3 seconds
        {
            if (tech01Seq != null && tech01Seq.actionSequence != null && tech01Seq.actionSequence.Count > 0)
                Debug.Log($"🔍 DistanceToTargetMeter: SIMPLE_Technician_01 first target: {tech01Seq.actionSequence[0].targetObjectId}");
            if (tech02Seq != null && tech02Seq.actionSequence != null && tech02Seq.actionSequence.Count > 0)
                Debug.Log($"🔍 DistanceToTargetMeter: SIMPLE_Technician_02 first target: {tech02Seq.actionSequence[0].targetObjectId}");
            if (sup01Seq != null && sup01Seq.actionSequence != null && sup01Seq.actionSequence.Count > 0)
                Debug.Log($"🔍 DistanceToTargetMeter: SIMPLE_Supervisor_01 first target: {sup01Seq.actionSequence[0].targetObjectId}");
            if (sup02Seq != null && sup02Seq.actionSequence != null && sup02Seq.actionSequence.Count > 0)
                Debug.Log($"🔍 DistanceToTargetMeter: SIMPLE_Supervisor_02 first target: {sup02Seq.actionSequence[0].targetObjectId}");
            
        // CRITICAL: Verify sequences are different and have correct agentIds
        // If parsing is wrong, sequences might be mixed up
        if (tech01Seq != null && tech02Seq != null && 
            tech01Seq.actionSequence != null && tech02Seq.actionSequence != null &&
            tech01Seq.actionSequence.Count > 0 && tech02Seq.actionSequence.Count > 0)
        {
            string tech01Target = tech01Seq.actionSequence[0].targetObjectId;
            string tech02Target = tech02Seq.actionSequence[0].targetObjectId;
            string tech01StepId = tech01Seq.actionSequence[0].stepId;
            string tech02StepId = tech02Seq.actionSequence[0].stepId;
            
            Debug.Log($"🔍 DistanceToTargetMeter: Tech01 sequence - agentId: {tech01Seq.agentId}, stepId: {tech01StepId}, target: {tech01Target}");
            Debug.Log($"🔍 DistanceToTargetMeter: Tech02 sequence - agentId: {tech02Seq.agentId}, stepId: {tech02StepId}, target: {tech02Target}");
            
            // Check if sequences are swapped or wrong
            if (tech01Seq.agentId != "SIMPLE_Technician_01")
            {
                Debug.LogError($"❌ DistanceToTargetMeter: Tech01Seq has wrong agentId! Expected SIMPLE_Technician_01, got {tech01Seq.agentId}");
            }
            if (tech02Seq.agentId != "SIMPLE_Technician_02")
            {
                Debug.LogError($"❌ DistanceToTargetMeter: Tech02Seq has wrong agentId! Expected SIMPLE_Technician_02, got {tech02Seq.agentId}");
            }
            
            if (tech01Target == tech02Target)
            {
                Debug.LogError($"❌ DistanceToTargetMeter: ERROR! Tech01 and Tech02 have SAME first target: {tech01Target}");
                Debug.LogError($"   Tech01 stepId: {tech01StepId}, Tech02 stepId: {tech02StepId}");
                Debug.LogError($"   This indicates a parsing error in AgentSequenceManager - sequences are mixed up!");
            }
        }
        }
        
        foreach (var agentId in agentComponents.Keys)
        {
            string displayName = GetAgentDisplayName(agentId);
            AgentSequenceData sequence = null;
            string sequenceKey = null;
            
            // DIRECT MAPPING: Map by display name only (most reliable)
            if (displayName == "technician001")
            {
                sequence = tech01Seq;
                sequenceKey = "SIMPLE_Technician_01";
            }
            else if (displayName == "technician002")
            {
                sequence = tech02Seq;
                sequenceKey = "SIMPLE_Technician_02";
            }
            else if (displayName == "supervisor001")
            {
                sequence = sup01Seq;
                sequenceKey = "SIMPLE_Supervisor_01";
            }
            else if (displayName == "supervisor002")
            {
                sequence = sup02Seq;
                sequenceKey = "SIMPLE_Supervisor_02";
            }
            
            if (sequence != null && !string.IsNullOrEmpty(sequenceKey))
            {
            // CRITICAL: Verify sequence matches - this is the key check
            if (sequence.agentId != sequenceKey)
            {
                Debug.LogError($"❌ DistanceToTargetMeter: Sequence ID mismatch! Agent {agentId} (display: {displayName}) expected sequence {sequenceKey}, but got sequence for {sequence.agentId}");
                // Try to get the correct sequence
                sequence = sequenceManager.GetSequence(sequence.agentId);
                if (sequence != null && sequence.agentId == sequenceKey)
                {
                    Debug.Log($"✅ DistanceToTargetMeter: Corrected sequence for {agentId}");
                }
                else
                {
                    Debug.LogError($"❌ DistanceToTargetMeter: Could not get correct sequence for {agentId}. Expected {sequenceKey}");
                    continue; // Skip this agent if sequence is wrong
                }
            }
            
            // CRITICAL: Verify the stepId matches the expected agent
            // Each agent should have a unique stepId pattern (tech01_step_001, tech02_step_001, etc.)
            if (sequence.actionSequence != null && sequence.actionSequence.Count > 0)
            {
                var firstStep = sequence.actionSequence[0];
                string firstTarget = firstStep != null ? firstStep.targetObjectId : "null";
                string stepId = firstStep != null ? firstStep.stepId : "null";
                
                // Verify stepId matches expected agent
                bool stepIdMatches = false;
                if (sequenceKey == "SIMPLE_Technician_01" && stepId.Contains("tech01"))
                    stepIdMatches = true;
                else if (sequenceKey == "SIMPLE_Technician_02" && stepId.Contains("tech02"))
                    stepIdMatches = true;
                else if (sequenceKey == "SIMPLE_Supervisor_01" && stepId.Contains("sup01"))
                    stepIdMatches = true;
                else if (sequenceKey == "SIMPLE_Supervisor_02" && stepId.Contains("sup02"))
                    stepIdMatches = true;
                
                if (!stepIdMatches)
                {
                    Debug.LogError($"❌ DistanceToTargetMeter: StepId mismatch! Agent {agentId} (display: {displayName}) expected stepId containing agent pattern, but got {stepId}");
                    Debug.LogError($"   This indicates the sequence is WRONG - AgentSequenceManager parsed the wrong sequence!");
                }
                
                if (Time.frameCount % 180 == 0)
                    Debug.Log($"✅ DistanceToTargetMeter: MAPPED {agentId} ({displayName}) → {sequenceKey}, step: {stepId}, target: {firstTarget}, ok={stepIdMatches}");
            }
            else
            {
                Debug.LogWarning($"⚠️ DistanceToTargetMeter: Sequence {sequenceKey} has no steps!");
            }
                
                allSequences[agentId] = sequence;
                agentIdToSequenceKey[agentId] = sequenceKey;
            }
            else
            {
                Debug.LogWarning($"⚠️ DistanceToTargetMeter: No sequence found for agentId: {agentId} (display: {displayName}), tried sequenceKey: {sequenceKey}");
            }
        }
        
        // Sort by zone index first, then role (tech01, tech02, sup01, sup02)
        var sortedAgents = allSequences.OrderBy(kvp =>
        {
            if (agentComponents.TryGetValue(kvp.Key, out BSGMLAgent za) && za != null && za.zoneIndex >= 0)
                return za.zoneIndex;
            return 99;
        }).ThenBy(kvp =>
        {
            string id = kvp.Key;
            if (id.Contains("Technician_01") || id.Contains("SIMPLE_Technician_01")) return 0;
            if (id.Contains("Technician_02") || id.Contains("SIMPLE_Technician_02")) return 1;
            if (id.Contains("Supervisor_01") || id.Contains("SIMPLE_Supervisor_01")) return 2;
            if (id.Contains("Supervisor_02") || id.Contains("SIMPLE_Supervisor_02")) return 3;
            return 99;
        }).ToList();
        
        foreach (var agentKvp in sortedAgents)
        {
            string agentId = agentKvp.Key;
            AgentSequenceData sequence = agentKvp.Value;
            
            // Declare displayName once at the start of the loop
            string displayName = GetAgentDisplayName(agentId);
            
            if (!agentComponents.ContainsKey(agentId))
            {
                // Try to find agent by matching display name
                BSGMLAgent foundAgent = null;
                foreach (var kvp in agentComponents)
                {
                    if (GetAgentDisplayName(kvp.Key) == displayName)
                    {
                        foundAgent = kvp.Value;
                        agentId = kvp.Key; // Use the actual agentId from component
                        displayName = GetAgentDisplayName(agentId); // Update display name with new agentId
                        break;
                    }
                }
                if (foundAgent == null)
                    continue;
            }
            
            BSGMLAgent agent = agentComponents.ContainsKey(agentId) ? agentComponents[agentId] : null;
            if (agent == null)
                continue;
            
            if (sequence.actionSequence == null || sequence.actionSequence.Count == 0)
            {
                Debug.LogWarning($"⚠️ DistanceToTargetMeter: No steps in sequence for agent {agentId}");
                continue;
            }

            var firstStep = sequence.actionSequence[0];
            if (firstStep == null || string.IsNullOrEmpty(firstStep.targetObjectId))
            {
                Debug.LogWarning($"⚠️ DistanceToTargetMeter: First step is null or has no targetObjectId for agent {agentId}");
                continue;
            }

            ActionSequenceStep currentStep = sequence.GetCurrentStep();
            if (currentStep == null)
                currentStep = firstStep;

            string rawTargetId = currentStep.targetObjectId;
            if (string.IsNullOrEmpty(rawTargetId))
                rawTargetId = firstStep.targetObjectId;

            int zoneIdx = agent.zoneIndex;
            ZoneDeclarativeMemory zMem = zoneIdx >= 0 ? ZoneDeclarativeMemory.ForZone(zoneIdx) : null;
            bool cognitiveGate = ShouldGateOnCognitivePhase(agent, zMem);
            bool cognitiveLoading = cognitiveGate && zMem != null && !zMem.cognitiveReady;

            string sequenceKeyForLog = agentIdToSequenceKey.ContainsKey(agentId) ? agentIdToSequenceKey[agentId] : agentId;
            
            // CRITICAL: Use the correct sequence key for distance calculation
            string sequenceKey = agentIdToSequenceKey.ContainsKey(agentId) ? agentIdToSequenceKey[agentId] : agentId;
            
            // Log every update to see what's happening (throttled to avoid spam)
            if (Time.frameCount % 60 == 0)
            {
                Debug.Log($"📊 DistanceToTargetMeter: {displayName} Z{zoneIdx} loading={cognitiveLoading} step={sequence.currentStepIndex}");
            }
            
            // VERIFY: Check if this sequence actually belongs to this agent
            // The sequence.agentId should match the sequenceKey
            if (sequence.agentId != sequenceKeyForLog)
            {
                Debug.LogWarning($"⚠️ DistanceToTargetMeter: Sequence mismatch! Agent {agentId} using sequence for {sequence.agentId}, but expected {sequenceKeyForLog}");
            }
            
            // CRITICAL VERIFICATION: Log the actual first step target for each agent
            if (Time.frameCount % 120 == 0)
            {
                Debug.Log($"🔍 DistanceToTargetMeter: {displayName} Z{zoneIdx} gate={cognitiveGate} ready={zMem == null || zMem.cognitiveReady} seq={sequenceKeyForLog}");
            }
            
            Vector3 agentPosition = agent.transform.position;
            int zi = zoneIdx >= 0 ? zoneIdx : -1;

            float distance = float.MaxValue;
            string targetLabel = "—";
            if (!cognitiveLoading)
            {
                targetLabel = BuildReplicaTargetLabel(zMem, sequence, rawTargetId, zoneIdx);
                distance = sequenceManager.GetDistanceToTarget(sequenceKey, agentPosition, zi);
            }

            if (!agentMeterPanels.ContainsKey(agentId))
            {
                Debug.Log($"🆕 DistanceToTargetMeter: Creating panel for {agentId} ({displayName} Z{zoneIdx})");
                CreateAgentMeterPanel(agentId, displayName, targetLabel, zoneIdx);
            }

            string loadingLine = BuildCognitiveLoadingLine(zMem);
            UpdateAgentMeterPanel(agentId, displayName, zoneIdx, cognitiveLoading, loadingLine, distance, targetLabel);
        }
        
        // Remove panels for agents that no longer exist
        var agentsToRemove = agentMeterPanels.Keys.Where(id => !allSequences.ContainsKey(id)).ToList();
        foreach (var agentId in agentsToRemove)
        {
            if (agentMeterPanels.ContainsKey(agentId))
            {
                Destroy(agentMeterPanels[agentId]);
                agentMeterPanels.Remove(agentId);
            }
        }
    }
    
    void CreateAgentMeterPanel(string agentId, string displayName, string targetLabel, int zoneIdx)
    {
        if (meterListParent == null)
            return;
        
        GameObject panel = new GameObject($"DistanceMeter_{agentId}");
        panel.transform.SetParent(meterListParent, false);
        
        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.15f, 0.15f, 0.15f, 0.8f);
        
        const float rowH = 46f;
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0, 1);
        panelRect.anchorMax = new Vector2(1, 1);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.sizeDelta = new Vector2(0, rowH);
        
        LayoutElement layoutElement = panel.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = rowH;
        layoutElement.flexibleWidth = 1;
        
        GameObject nameGO = new GameObject("AgentName");
        nameGO.transform.SetParent(panel.transform, false);
        
        Text nameText = nameGO.AddComponent<Text>();
        nameText.text = FormatAgentHeaderLine(displayName, zoneIdx);
        nameText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        nameText.fontSize = 9;
        nameText.color = textColor;
        nameText.fontStyle = FontStyle.Bold;
        nameText.alignment = TextAnchor.MiddleLeft;
        
        RectTransform nameRect = nameText.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0, 0.68f);
        nameRect.anchorMax = new Vector2(1, 1);
        nameRect.offsetMin = new Vector2(6, 0);
        nameRect.offsetMax = new Vector2(-6, 0);
        
        GameObject targetGO = new GameObject("TargetText");
        targetGO.transform.SetParent(panel.transform, false);
        
        Text targetText = targetGO.AddComponent<Text>();
        targetText.text = targetLabel == "—" ? "—" : $"→ {targetLabel}";
        targetText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        targetText.fontSize = 8;
        targetText.color = new Color(textColor.r, textColor.g, textColor.b, 0.85f);
        targetText.alignment = TextAnchor.MiddleLeft;
        
        RectTransform targetRect = targetText.GetComponent<RectTransform>();
        targetRect.anchorMin = new Vector2(0, 0.42f);
        targetRect.anchorMax = new Vector2(0.62f, 0.68f);
        targetRect.offsetMin = new Vector2(6, 0);
        targetRect.offsetMax = new Vector2(-2, 0);
        
        GameObject distanceGO = new GameObject("DistanceText");
        distanceGO.transform.SetParent(panel.transform, false);
        
        Text distanceText = distanceGO.AddComponent<Text>();
        distanceText.text = "0.0m";
        distanceText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        distanceText.fontSize = 8;
        distanceText.color = textColor;
        distanceText.fontStyle = FontStyle.Bold;
        distanceText.alignment = TextAnchor.MiddleRight;
        
        RectTransform distanceRect = distanceText.GetComponent<RectTransform>();
        distanceRect.anchorMin = new Vector2(0.58f, 0.42f);
        distanceRect.anchorMax = new Vector2(1, 0.68f);
        distanceRect.offsetMin = new Vector2(2, 0);
        distanceRect.offsetMax = new Vector2(-6, 0);
        
        GameObject progressContainer = new GameObject("ProgressContainer");
        progressContainer.transform.SetParent(panel.transform, false);
        
        RectTransform progressContainerRect = progressContainer.AddComponent<RectTransform>();
        progressContainerRect.anchorMin = new Vector2(0, 0);
        progressContainerRect.anchorMax = new Vector2(1, 0.4f);
        progressContainerRect.offsetMin = new Vector2(6, 4);
        progressContainerRect.offsetMax = new Vector2(-6, -2);
        
        // Background bar
        GameObject bgBar = new GameObject("BackgroundBar");
        bgBar.transform.SetParent(progressContainer.transform, false);
        
        Image bgImage = bgBar.AddComponent<Image>();
        bgImage.color = progressBarBackgroundColor;
        
        RectTransform bgRect = bgImage.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.sizeDelta = Vector2.zero;
        
        // Progress fill bar
        GameObject fillBar = new GameObject("FillBar");
        fillBar.transform.SetParent(progressContainer.transform, false);
        
        Image fillImage = fillBar.AddComponent<Image>();
        fillImage.color = progressFarColor;
        
        RectTransform fillRect = fillImage.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0, 0);
        fillRect.anchorMax = new Vector2(0, 1);
        fillRect.pivot = new Vector2(0, 0.5f);
        fillRect.sizeDelta = new Vector2(0, 0);
        fillRect.anchoredPosition = Vector2.zero;
        
        // Store references
        DistanceMeterPanelData panelData = panel.AddComponent<DistanceMeterPanelData>();
        panelData.Initialize(nameText, targetText, distanceText, fillImage, fillRect);
        
        panel.SetActive(true);
        
        int siblingIndex = GetAgentSiblingIndex(agentId);
        panel.transform.SetSiblingIndex(siblingIndex);
        
        agentMeterPanels[agentId] = panel;
    }
    
    void UpdateAgentMeterPanel(
        string agentId,
        string displayName,
        int zoneIdx,
        bool cognitiveLoading,
        string cognitiveLoadingLine,
        float distance,
        string targetLabel)
    {
        if (!agentMeterPanels.ContainsKey(agentId))
            return;
        
        GameObject panel = agentMeterPanels[agentId];
        if (panel == null)
            return;
        
        DistanceMeterPanelData panelData = panel.GetComponent<DistanceMeterPanelData>();
        if (panelData == null)
            return;
        
        if (panelData.agentNameText != null)
        {
            string header = FormatAgentHeaderLine(displayName, zoneIdx);
            if (panelData.agentNameText.text != header)
                panelData.agentNameText.text = header;
        }

        if (panelData.targetText != null)
        {
            string targetLine;
            Color lineColor;
            if (cognitiveLoading)
            {
                targetLine = string.IsNullOrEmpty(cognitiveLoadingLine)
                    ? cognitiveLoadingMessage
                    : cognitiveLoadingLine;
                lineColor = cognitiveLoadingTextColor;
            }
            else
            {
                targetLine = targetLabel == "—" ? "—" : $"→ {targetLabel}";
                lineColor = new Color(textColor.r, textColor.g, textColor.b, 0.85f);
            }

            if (panelData.targetText.text != targetLine)
            {
                if (Time.frameCount % 120 == 0)
                    Debug.Log($"🔄 DistanceToTargetMeter: {agentId} → '{targetLine}'");
                panelData.targetText.text = targetLine;
            }
            if (panelData.targetText.color != lineColor)
                panelData.targetText.color = lineColor;
        }
        else
            Debug.LogWarning($"⚠️ DistanceToTargetMeter: panelData.targetText is null for agent {agentId}");
        
        if (panelData.distanceText != null)
        {
            if (cognitiveLoading)
            {
                panelData.distanceText.text = "—";
                panelData.distanceText.color = cognitiveLoadingTextColor;
            }
            else
            {
                panelData.distanceText.color = textColor;
                if (distance >= float.MaxValue - 1f)
                    panelData.distanceText.text = "N/A";
                else
                    panelData.distanceText.text = $"{distance:F1}m";
            }
        }
        
        float progress = 0f;
        Color progressColor = progressBarBackgroundColor;
        
        if (!cognitiveLoading && distance < float.MaxValue - 1f)
        {
            progress = Mathf.Clamp01(1f - (distance / MAX_DISTANCE));
            
            if (progress >= 0.7f)
                progressColor = progressAtTargetColor;
            else if (progress >= 0.3f)
                progressColor = progressCloseColor;
            else
                progressColor = progressFarColor;
        }
        
        if (panelData.fillImage != null && panelData.fillRect != null)
        {
            panelData.fillImage.color = cognitiveLoading ? progressBarBackgroundColor : progressColor;
            
            RectTransform containerRect = panelData.fillRect.parent.GetComponent<RectTransform>();
            if (containerRect != null)
            {
                float barWidth = cognitiveLoading ? 0f : containerRect.rect.width * progress;
                panelData.fillRect.sizeDelta = new Vector2(barWidth, 0);
            }
        }
    }

    /// <summary>Replica scenes: hide JSON-derived distance until M-agent protocol finishes.</summary>
    static bool ShouldGateOnCognitivePhase(BSGMLAgent agent, ZoneDeclarativeMemory zMem)
    {
        if (agent == null || agent.zoneIndex < 0) return false;
        if (!agent.waitForCognitiveReady) return false;
        if (zMem == null) return false;
        return MentalAgentSpawner.ForZone(agent.zoneIndex) != null;
    }

    string BuildCognitiveLoadingLine(ZoneDeclarativeMemory mem)
    {
        if (mem == null) return cognitiveLoadingMessage;
        try
        {
            string prog = string.Format(cognitiveProgressFormat, mem.CompletedSteps, mem.TotalSteps);
            return $"{cognitiveLoadingMessage} ({prog})";
        }
        catch (FormatException)
        {
            return cognitiveLoadingMessage;
        }
    }

    /// <summary>Prefer declarative outputs on the first physical step; later steps use the sequence target.</summary>
    static string BuildReplicaTargetLabel(ZoneDeclarativeMemory mem, AgentSequenceData sequence, string rawStepTarget, int zoneIdx)
    {
        if (sequence == null) return FormatZoneTargetLabel(rawStepTarget, zoneIdx);

        bool firstPhysicalStep = sequence.currentStepIndex <= 0;
        if (mem != null && firstPhysicalStep)
        {
            if (!string.IsNullOrWhiteSpace(mem.resolvedTargetId))
                return mem.resolvedTargetId.Trim();
            if (mem.TryGet("motor_target", out string mt) && !string.IsNullOrWhiteSpace(mt))
                return mt.Trim();
            if (mem.TryGet("resolved_target", out string rt) && !string.IsNullOrWhiteSpace(rt))
                return rt.Trim();
        }

        return FormatZoneTargetLabel(rawStepTarget, zoneIdx);
    }

    /// <summary>Scene object id for UI: adds _zoneN when the agent has a zone and JSON used a base id.</summary>
    static string FormatZoneTargetLabel(string rawTargetId, int zoneIndex)
    {
        if (string.IsNullOrEmpty(rawTargetId)) return "—";
        if (zoneIndex < 0) return rawTargetId;
        if (rawTargetId.IndexOf("_zone", StringComparison.OrdinalIgnoreCase) >= 0)
            return rawTargetId;
        return $"{rawTargetId}_zone{zoneIndex}";
    }

    static string FormatAgentHeaderLine(string displayName, int zoneIdx)
    {
        if (string.IsNullOrEmpty(displayName)) displayName = "?";
        if (zoneIdx >= 0) return $"{displayName}  ·  Zone {zoneIdx}";
        return displayName;
    }
    
    string GetAgentDisplayName(string agentId)
    {
        if (string.IsNullOrEmpty(agentId))
            return "Unknown";
        
        if (agentId.Contains("SIMPLE_"))
        {
            string[] parts = agentId.Split('_');
            if (parts.Length >= 3)
            {
                string role = parts[1].ToLower();
                string number = parts[2];
                
                if (number.Length == 2)
                {
                    number = "0" + number;
                }
                
                return $"{role}{number}";
            }
        }
        
        return agentId;
    }
    
    int GetAgentSiblingIndex(string agentId)
    {
        if (agentId.Contains("Technician_01") || agentId.Contains("SIMPLE_Technician_01")) return 0;
        if (agentId.Contains("Technician_02") || agentId.Contains("SIMPLE_Technician_02")) return 1;
        if (agentId.Contains("Supervisor_01") || agentId.Contains("SIMPLE_Supervisor_01")) return 2;
        if (agentId.Contains("Supervisor_02") || agentId.Contains("SIMPLE_Supervisor_02")) return 3;
        return 99;
    }
    
    /// <summary>
    /// Convert display name (e.g., "technician001") back to SIMPLE_ format (e.g., "SIMPLE_Technician_01")
    /// </summary>
    string ConvertDisplayNameToSimpleFormat(string displayName)
    {
        if (string.IsNullOrEmpty(displayName))
            return null;
        
        displayName = displayName.ToLower();
        
        if (displayName.StartsWith("technician"))
        {
            string number = displayName.Replace("technician", "");
            if (number == "001" || number == "01")
                return "SIMPLE_Technician_01";
            else if (number == "002" || number == "02")
                return "SIMPLE_Technician_02";
        }
        else if (displayName.StartsWith("supervisor"))
        {
            string number = displayName.Replace("supervisor", "");
            if (number == "001" || number == "01")
                return "SIMPLE_Supervisor_01";
            else if (number == "002" || number == "02")
                return "SIMPLE_Supervisor_02";
        }
        
        return null;
    }
}

/// <summary>
/// Helper class to store references to UI elements for each distance meter panel
/// </summary>
public class DistanceMeterPanelData : MonoBehaviour
{
    public Text agentNameText;
    public Text targetText;
    public Text distanceText;
    public Image fillImage;
    public RectTransform fillRect;
    
    public void Initialize(Text name, Text target, Text distance, Image fill, RectTransform fillRectTransform)
    {
        agentNameText = name;
        targetText = target;
        distanceText = distance;
        fillImage = fill;
        fillRect = fillRectTransform;
    }
}

