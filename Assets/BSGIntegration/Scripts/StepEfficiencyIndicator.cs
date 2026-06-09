using System;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Step Efficiency Indicator - Visual comparison of expected vs actual step completion times
/// Shows efficiency percentage, color coding, and mini progress bars
/// 
/// DATA SOURCE: Gets data from MLTrainingResultsWriter which collects statistics from
/// the ML-Agents reinforcement learning training model (BSGMLAgent.cs).
/// Updates in real-time as agents complete steps during training.
/// </summary>
public class StepEfficiencyIndicator : MonoBehaviour
{
    [Header("UI References")]
    public Canvas efficiencyCanvas;
    public GameObject efficiencyPanel;
    public Transform efficiencyListParent;
    
    [Header("Panel Settings")]
    public Vector2 panelPosition = new Vector2(0, -2); // Top-center, just below screen edge
    public Vector2 panelSize = new Vector2(1400, 195);  // Tall enough for 2 step rows + header
    public float updateInterval = 1f;
    
    [Header("Visual Settings")]
    public Color backgroundColor = new Color(0, 0, 0, 0.85f);
    public Color headerColor = new Color(0.2f, 0.6f, 0.2f, 1f);
    public Color textColor = Color.white;
    public Color efficiencyHighColor = new Color(0.3f, 0.9f, 0.3f, 1f); // Green >110%
    public Color efficiencyMidColor = new Color(1f, 0.8f, 0.2f, 1f);   // Yellow 90-110%
    public Color efficiencyLowColor = new Color(0.9f, 0.3f, 0.3f, 1f);  // Red <90%
    public Color progressBarBackgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    
    private MLTrainingResultsWriter resultsWriter;
    private Dictionary<string, GameObject> agentEfficiencyPanels = new Dictionary<string, GameObject>();
    private float lastUpdateTime = 0f;
    private GameObject noDataMessage = null;
    private GameObject headerBackground = null; // Track header to prevent duplicates
    
    void Start()
    {
        Debug.Log("🚀 StepEfficiencyIndicator: Starting initialization...");
        Debug.Log("📊 StepEfficiencyIndicator: Data source: MLTrainingResultsWriter (Reinforcement Learning Training Model)");
        
        // Find results writer - this gets data from reinforcement training model
        resultsWriter = MLTrainingResultsWriter.Instance;
        if (resultsWriter == null)
        {
            Debug.LogWarning("⚠️ StepEfficiencyIndicator: MLTrainingResultsWriter not found yet, will retry...");
            Debug.LogWarning("⚠️ MLTrainingResultsWriter tracks data from ML-Agents reinforcement training");
            // Retry after a delay
            Invoke("RetryInitialize", 2f);
            return;
        }
        
        Debug.Log("✅ StepEfficiencyIndicator: Found MLTrainingResultsWriter");
        Debug.Log("✅ StepEfficiencyIndicator: Connected to reinforcement training data source");
        
        // Create the efficiency panel UI
        CreateEfficiencyPanel();
        
        // Initial update with a small delay
        StartCoroutine(InitialUpdateAfterDelay());
    }
    
    void RetryInitialize()
    {
        resultsWriter = MLTrainingResultsWriter.Instance;
        if (resultsWriter != null)
        {
            Debug.Log("✅ StepEfficiencyIndicator: Successfully found MLTrainingResultsWriter on retry");
            CreateEfficiencyPanel();
            StartCoroutine(InitialUpdateAfterDelay());
        }
        else
        {
            Debug.LogWarning("⚠️ StepEfficiencyIndicator: Still no MLTrainingResultsWriter, will keep retrying...");
            Invoke("RetryInitialize", 2f);
        }
    }
    
    void Update()
    {
        // Update efficiency display at regular intervals
        if (Time.time - lastUpdateTime >= updateInterval)
        {
            UpdateEfficiencyDisplay();
            lastUpdateTime = Time.time;
        }
    }
    
    void CreateEfficiencyPanel()
    {
        // Create main canvas if not assigned
        if (efficiencyCanvas == null)
        {
            GameObject canvasGO = new GameObject("StepEfficiencyCanvas");
            efficiencyCanvas = canvasGO.AddComponent<Canvas>();
            efficiencyCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            efficiencyCanvas.sortingOrder = 110; // Much higher to ensure it's on top of everything
            
            // Add CanvasScaler - Use ScaleWithScreenSize with match=0 to keep side panels anchored
            // Match=0 means scale based on width, keeping left/right anchored elements in place
            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0f; // Match width (0) to keep horizontal positioning stable
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            
            // Add GraphicRaycaster
            canvasGO.AddComponent<GraphicRaycaster>();
            
            // CRITICAL: Ensure canvas is active and visible
            canvasGO.SetActive(true);
            efficiencyCanvas.enabled = true;
            
            Debug.Log("✅ StepEfficiencyIndicator: Created and activated StepEfficiencyCanvas");
        }
        
        // Create main panel
        if (efficiencyPanel == null)
        {
            efficiencyPanel = new GameObject("EfficiencyPanel");
            efficiencyPanel.transform.SetParent(efficiencyCanvas.transform, false);
            
            // Add Image component for background
            Image panelImage = efficiencyPanel.AddComponent<Image>();
            panelImage.color = backgroundColor;
            
            // Set position and size - TOP-CENTER
            RectTransform panelRect = efficiencyPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 1f); // Top-center anchor
            panelRect.anchorMax = new Vector2(0.5f, 1f); // Top-center anchor
            panelRect.pivot     = new Vector2(0.5f, 1f); // Pivot at top-center
            panelRect.anchoredPosition = panelPosition;
            panelRect.sizeDelta = panelSize;
            
            // Add Mask component to clip content within panel bounds
            Mask mask = efficiencyPanel.AddComponent<Mask>();
            mask.showMaskGraphic = false; // Don't show mask graphic, just clip
            
            // Ensure panel is active
            efficiencyPanel.SetActive(true);
            
            Debug.Log($"✅ StepEfficiencyIndicator: Panel positioned at {panelPosition}, size {panelSize}");
        }
        
        // Create header
        CreateHeader();
        
        // Create efficiency list parent
        if (efficiencyListParent == null)
        {
            GameObject listParent = new GameObject("EfficiencyListParent");
            listParent.transform.SetParent(efficiencyPanel.transform, false);
            
            RectTransform listRect = listParent.AddComponent<RectTransform>();
            listRect.anchorMin = new Vector2(0, 0);
            listRect.anchorMax = new Vector2(1, 1);
            // Header is 26px tall, leave 4px gap below it
            listRect.offsetMin = new Vector2(8, 6);
            listRect.offsetMax = new Vector2(-8, -30);
            
            // Ensure list parent is below header in hierarchy (renders behind header)
            listParent.transform.SetSiblingIndex(0);
            
            // Horizontal layout: tech01 -> tech02 -> supervisor01 -> supervisor02
            HorizontalLayoutGroup layoutGroup = listParent.AddComponent<HorizontalLayoutGroup>();
            layoutGroup.spacing = 10f;
            layoutGroup.padding = new RectOffset(6, 6, 6, 6);
            layoutGroup.childControlHeight = true;
            layoutGroup.childControlWidth = true;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.childForceExpandWidth = false;

            efficiencyListParent = listParent.transform;
        }
        
        // Ensure panel is active and visible
        if (efficiencyPanel != null)
        {
            efficiencyPanel.SetActive(true);
        }
        
        Debug.Log("✅ StepEfficiencyIndicator: Efficiency panel created successfully and activated");
    }
    
    void CreateHeader()
    {
        // Prevent duplicate headers
        if (headerBackground != null)
        {
            return;
        }
        
        // Create header background
        GameObject headerBG = new GameObject("HeaderBackground");
        headerBG.transform.SetParent(efficiencyPanel.transform, false);
        
        // CRITICAL: Set sibling index to last to ensure header renders on top
        headerBG.transform.SetAsLastSibling();
        
        headerBackground = headerBG; // Store reference
        
        Image headerImage = headerBG.AddComponent<Image>();
        headerImage.color = headerColor;
        
        RectTransform headerRect = headerBG.GetComponent<RectTransform>();
        // Align header with content area - match the horizontal padding of listParent (10px on each side)
        headerRect.anchorMin = new Vector2(0, 1);
        headerRect.anchorMax = new Vector2(1, 1);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.anchoredPosition = new Vector2(0, 0);
        headerRect.offsetMin = new Vector2(0, -26); // 26px tall header
        headerRect.offsetMax = new Vector2(0, 0);

        // Header text
        GameObject headerText = new GameObject("HeaderText");
        headerText.transform.SetParent(headerBG.transform, false);

        Text text = headerText.AddComponent<Text>();
        text.text = "STEP EFFICIENCY";
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 13;
        text.color = textColor;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.resizeTextForBestFit = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        RectTransform textRect = headerText.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
    }
    
    /// <summary>
    /// Coroutine to update efficiency display after a delay
    /// </summary>
    private System.Collections.IEnumerator InitialUpdateAfterDelay()
    {
        yield return null;
        yield return new WaitForSeconds(0.5f);
        UpdateEfficiencyDisplay();
    }
    
    void UpdateEfficiencyDisplay()
    {
        if (resultsWriter == null)
        {
            // Try to find it again
            resultsWriter = MLTrainingResultsWriter.Instance;
            if (resultsWriter == null)
            {
                return; // Still not available, skip this update
            }
        }
        
        if (efficiencyPanel == null || efficiencyListParent == null)
        {
            Debug.LogWarning("⚠️ StepEfficiencyIndicator: UI not ready, recreating...");
            CreateEfficiencyPanel();
            return;
        }
        
        // Ensure panel is visible
        if (!efficiencyPanel.activeSelf)
        {
            efficiencyPanel.SetActive(true);
            Debug.Log("✅ StepEfficiencyIndicator: Reactivated efficiency panel");
        }
        
        // Get training results from reinforcement learning model
        // MLTrainingResultsWriter collects data from ML-Agents training (BSGMLAgent.cs)
        var trainingResults = resultsWriter.GetTrainingResults();
        if (trainingResults == null || trainingResults.agentResults == null)
        {
            // Show "No data" message if no results yet
            if (agentEfficiencyPanels.Count == 0)
            {
                ShowNoDataMessage();
            }
            return;
        }
        
        // Hide "No data" message if we have results
        HideNoDataMessage();
        
        // Create or update agent efficiency panels
        int agentCount = trainingResults.agentResults.Count;
        if (agentCount > 0)
        {
            Debug.Log($"📊 StepEfficiencyIndicator: Updating display for {agentCount} agents");
            
            // Debug: Log all agent IDs to identify missing agents
            string allAgentIds = string.Join(", ", trainingResults.agentResults.Keys);
            Debug.Log($"📋 StepEfficiencyIndicator: Available agents: {allAgentIds}");
        }
        
        // Sort agents: legacy SIMPLE_* order, then other agent IDs (e.g. RAG P1/M1) last.
        var sortedAgents = trainingResults.agentResults.OrderBy(kvp => 
        {
            string id = kvp.Key;
            // Order: Technician_01 (0), Technician_02 (1), Supervisor_01 (2), Supervisor_02 (3)
            if (id.Contains("Technician_01") || id.Contains("SIMPLE_Technician_01")) return 0;
            if (id.Contains("Technician_02") || id.Contains("SIMPLE_Technician_02")) return 1;
            if (id.Contains("Supervisor_01") || id.Contains("SIMPLE_Supervisor_01")) return 2;
            if (id.Contains("Supervisor_02") || id.Contains("SIMPLE_Supervisor_02")) return 3;
            return 99; // Other agents go last
        }).ToList();
        
        foreach (var agentResult in sortedAgents)
        {
            string agentId = agentResult.Key;
            string displayName = GetAgentDisplayName(agentId);
            
            // Debug: Log Supervisor 2 specifically
            if (agentId.Contains("Supervisor_02") || agentId.Contains("SIMPLE_Supervisor_02"))
            {
                Debug.Log($"🔍 StepEfficiencyIndicator: Found Supervisor 2 - ID: {agentId}, Display: {displayName}");
                Debug.Log($"🔍 StepEfficiencyIndicator: Panel exists in dict: {agentEfficiencyPanels.ContainsKey(agentId)}");
            }
            
            // Debug: Log Technician 1 specifically
            if (agentId.Contains("Technician_01") || agentId.Contains("SIMPLE_Technician_01"))
            {
                Debug.Log($"🔍 StepEfficiencyIndicator: Found Technician 1 - ID: {agentId}, Display: {displayName}");
                Debug.Log($"🔍 StepEfficiencyIndicator: Panel exists in dict: {agentEfficiencyPanels.ContainsKey(agentId)}");
            }
            
            // Create panel if it doesn't exist
            if (!agentEfficiencyPanels.ContainsKey(agentId))
            {
                CreateAgentEfficiencyPanel(agentId, displayName);
                Debug.Log($"✅ StepEfficiencyIndicator: Created efficiency panel for {displayName} (ID: {agentId})");
                
                // Additional debug for Supervisor 2
                if (agentId.Contains("Supervisor_02") || agentId.Contains("SIMPLE_Supervisor_02"))
                {
                    if (agentEfficiencyPanels.ContainsKey(agentId))
                    {
                        GameObject panel = agentEfficiencyPanels[agentId];
                        Debug.Log($"🔍 StepEfficiencyIndicator: Supervisor 2 panel created - Active: {panel.activeSelf}, Parent: {panel.transform.parent?.name}");
                    }
                    else
                    {
                        Debug.LogError($"❌ StepEfficiencyIndicator: Supervisor 2 panel NOT in dictionary after creation!");
                    }
                }
                
                // Additional debug for Technician 1
                if (agentId.Contains("Technician_01") || agentId.Contains("SIMPLE_Technician_01"))
                {
                    if (agentEfficiencyPanels.ContainsKey(agentId))
                    {
                        GameObject panel = agentEfficiencyPanels[agentId];
                        Debug.Log($"🔍 StepEfficiencyIndicator: Technician 1 panel created - Active: {panel.activeSelf}, Parent: {panel.transform.parent?.name}, SiblingIndex: {panel.transform.GetSiblingIndex()}");
                    }
                    else
                    {
                        Debug.LogError($"❌ StepEfficiencyIndicator: Technician 1 panel NOT in dictionary after creation!");
                    }
                }
            }
            
            // Update existing panel
            UpdateAgentEfficiencyPanel(agentId, agentResult.Value);
        }
        
        // Debug: Check if Supervisor 2 is missing
        bool hasSupervisor2 = trainingResults.agentResults.Keys.Any(id => 
            id.Contains("Supervisor_02") || id.Contains("SIMPLE_Supervisor_02"));
        if (!hasSupervisor2)
        {
            Debug.LogWarning($"⚠️ StepEfficiencyIndicator: Supervisor 2 data not found in training results!");
            Debug.LogWarning($"⚠️ This may indicate Supervisor 2 hasn't started training yet or data isn't being collected.");
        }
        else
        {
            // Supervisor 2 exists - ensure it's visible
            string supervisor2Id = trainingResults.agentResults.Keys.FirstOrDefault(id => 
                id.Contains("Supervisor_02") || id.Contains("SIMPLE_Supervisor_02"));
            
            if (!string.IsNullOrEmpty(supervisor2Id) && agentEfficiencyPanels.ContainsKey(supervisor2Id))
            {
                GameObject supervisor2Panel = agentEfficiencyPanels[supervisor2Id];
                if (supervisor2Panel != null)
                {
                    // Ensure panel is active and visible
                    supervisor2Panel.SetActive(true);
                    
                    // Force layout rebuild to ensure proper sizing
                    RectTransform panelRect = supervisor2Panel.GetComponent<RectTransform>();
                    if (panelRect != null)
                    {
                        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
                        
                        // Force content area to update
                        if (efficiencyListParent != null)
                        {
                            RectTransform contentRect = efficiencyListParent.GetComponent<RectTransform>();
                            if (contentRect != null)
                            {
                                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
                            }
                        }
                        
                        // Ensure ScrollRect shows all content
                        ScrollRect scrollRect = efficiencyListParent?.parent?.GetComponent<ScrollRect>();
                        if (scrollRect != null)
                        {
                            // Scroll to show the last item (Supervisor 2 if it's last)
                            Canvas.ForceUpdateCanvases();
                            scrollRect.verticalNormalizedPosition = 0f; // Scroll to bottom
                        }
                    }
                }
            }
        }
        
        // Debug: Check if Technician 1 is missing
        bool hasTechnician1 = trainingResults.agentResults.Keys.Any(id => 
            id.Contains("Technician_01") || id.Contains("SIMPLE_Technician_01"));
        if (!hasTechnician1)
        {
            Debug.LogWarning($"⚠️ StepEfficiencyIndicator: Technician 1 data not found in training results!");
            Debug.LogWarning($"⚠️ This may indicate Technician 1 hasn't started training yet or data isn't being collected.");
        }
        else
        {
            // Technician 1 exists - ensure it's visible
            string technician1Id = trainingResults.agentResults.Keys.FirstOrDefault(id => 
                id.Contains("Technician_01") || id.Contains("SIMPLE_Technician_01"));
            
            if (!string.IsNullOrEmpty(technician1Id) && agentEfficiencyPanels.ContainsKey(technician1Id))
            {
                GameObject technician1Panel = agentEfficiencyPanels[technician1Id];
                if (technician1Panel != null)
                {
                    // Ensure panel is active and visible
                    technician1Panel.SetActive(true);
                    
                    // Force layout rebuild to ensure proper sizing
                    RectTransform panelRect = technician1Panel.GetComponent<RectTransform>();
                    if (panelRect != null)
                    {
                        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
                        
                        // Move to top of list to ensure visibility (Technician 1 should be first)
                        technician1Panel.transform.SetAsFirstSibling();
                        
                        // Force content area to update
                        if (efficiencyListParent != null)
                        {
                            RectTransform contentRect = efficiencyListParent.GetComponent<RectTransform>();
                            if (contentRect != null)
                            {
                                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
                            }
                        }
                        
                        // Scroll to top to show Technician 1
                        ScrollRect scrollRect = efficiencyListParent?.parent?.GetComponent<ScrollRect>();
                        if (scrollRect != null)
                        {
                            Canvas.ForceUpdateCanvases();
                            scrollRect.verticalNormalizedPosition = 1f; // Scroll to top
                        }
                        
                        Debug.Log($"✅ StepEfficiencyIndicator: Technician 1 panel ensured visible - Active: {technician1Panel.activeSelf}, SiblingIndex: {technician1Panel.transform.GetSiblingIndex()}");
                    }
                }
            }
        }
        
        // Final check: Log all created panels to verify all 4 agents are present
        Debug.Log($"📋 StepEfficiencyIndicator: Total panels created: {agentEfficiencyPanels.Count}");
        foreach (var kvp in agentEfficiencyPanels)
        {
            string displayName = GetAgentDisplayName(kvp.Key);
            Debug.Log($"  - {displayName} (ID: {kvp.Key}): Active={kvp.Value.activeSelf}, SiblingIndex={kvp.Value.transform.GetSiblingIndex()}");
        }
        
        // Remove panels for agents that no longer exist
        var agentsToRemove = agentEfficiencyPanels.Keys.Where(id => !trainingResults.agentResults.ContainsKey(id)).ToList();
        foreach (var agentId in agentsToRemove)
        {
            if (agentEfficiencyPanels.ContainsKey(agentId))
            {
                Destroy(agentEfficiencyPanels[agentId]);
                agentEfficiencyPanels.Remove(agentId);
            }
        }
    }
    
    void CreateAgentEfficiencyPanel(string agentId, string displayName)
    {
        if (efficiencyListParent == null)
        {
            Debug.LogError("StepEfficiencyIndicator: efficiencyListParent is null");
            return;
        }
        
        GameObject panel = new GameObject($"AgentEfficiency_{agentId}");
        panel.transform.SetParent(efficiencyListParent, false);
        
        // Add background
        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.15f, 0.15f, 0.15f, 0.8f);
        
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0, 0.5f);
        panelRect.anchorMax = new Vector2(0, 0.5f);
        panelRect.pivot = new Vector2(0f, 0.5f);
        panelRect.sizeDelta = new Vector2(330, 158);
        panelRect.anchoredPosition = Vector2.zero;

        UnityEngine.UI.LayoutElement layoutElement = panel.AddComponent<UnityEngine.UI.LayoutElement>();
        layoutElement.preferredHeight = 158;
        layoutElement.preferredWidth  = 330;
        layoutElement.flexibleWidth   = 0;
        layoutElement.ignoreLayout    = false;
        
        // Force layout update on parent first, then on this panel to ensure width is calculated correctly
        if (efficiencyListParent != null)
        {
            RectTransform parentRect = efficiencyListParent.GetComponent<RectTransform>();
            if (parentRect != null)
            {
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
            }
        }
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
        
        // Create agent name text
        GameObject nameGO = new GameObject("AgentName");
        nameGO.transform.SetParent(panel.transform, false);
        
        Text nameText = nameGO.AddComponent<Text>();
        nameText.text = displayName;
        nameText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        nameText.fontSize = 13;
        nameText.color = textColor;
        nameText.fontStyle = FontStyle.Bold;
        nameText.alignment = TextAnchor.MiddleLeft;
        nameText.resizeTextForBestFit = false;
        nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
        nameText.verticalOverflow = VerticalWrapMode.Overflow;

        RectTransform nameRect = nameText.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0, 0.78f);
        nameRect.anchorMax = new Vector2(1, 1f);
        nameRect.offsetMin = new Vector2(6, 0);
        nameRect.offsetMax = new Vector2(-6, 0);
        
        // Create overall efficiency text
        GameObject overallGO = new GameObject("OverallEfficiency");
        overallGO.transform.SetParent(panel.transform, false);
        
        Text overallText = overallGO.AddComponent<Text>();
        overallText.text = "Overall: Calculating...";
        overallText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        overallText.fontSize = 12;
        overallText.color = textColor;
        overallText.fontStyle = FontStyle.Bold;
        overallText.alignment = TextAnchor.MiddleLeft;
        overallText.resizeTextForBestFit = false;
        overallText.horizontalOverflow = HorizontalWrapMode.Overflow;
        overallText.verticalOverflow = VerticalWrapMode.Overflow;

        RectTransform overallRect = overallText.GetComponent<RectTransform>();
        overallRect.anchorMin = new Vector2(0, 0.55f);
        overallRect.anchorMax = new Vector2(1, 0.78f);
        overallRect.offsetMin = new Vector2(6, 0);
        overallRect.offsetMax = new Vector2(-6, 0);
        
        // Create steps container
        GameObject stepsContainer = new GameObject("StepsContainer");
        stepsContainer.transform.SetParent(panel.transform, false);
        
        RectTransform stepsRect = stepsContainer.AddComponent<RectTransform>();
        stepsRect.anchorMin = new Vector2(0, 0);
        stepsRect.anchorMax = new Vector2(1, 0.50f);
        stepsRect.offsetMin = new Vector2(6, 3);
        stepsRect.offsetMax = new Vector2(-6, -2);

        VerticalLayoutGroup stepsLayout = stepsContainer.AddComponent<VerticalLayoutGroup>();
        stepsLayout.spacing = 2f;
        stepsLayout.childControlHeight = false;
        stepsLayout.childControlWidth = true;
        stepsLayout.childForceExpandWidth = true;
        
        // Ensure steps container clips content within bounds
        RectMask2D stepsMask = stepsContainer.AddComponent<RectMask2D>();
        
        // Store references for updating
        AgentEfficiencyPanelData panelData = panel.AddComponent<AgentEfficiencyPanelData>();
        panelData.Initialize(nameText, overallText, stepsContainer.transform);
        
        // Ensure panel is active and visible
        panel.SetActive(true);
        
        // Set sibling index based on required ordering:
        // tech01 -> tech02 -> supervisor01 -> supervisor02
        int siblingIndex = GetAgentSiblingIndex(agentId);
        panel.transform.SetSiblingIndex(siblingIndex);
        
        agentEfficiencyPanels[agentId] = panel;
        
        // Additional debug info for Supervisor 2
        if (agentId.Contains("Supervisor_02") || agentId.Contains("SIMPLE_Supervisor_02"))
        {
            RectTransform rect = panel.GetComponent<RectTransform>();
            // Force another layout update to ensure width is calculated
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            
            // Check if panel is within viewport bounds
            RectTransform contentRect = efficiencyListParent.GetComponent<RectTransform>();
            RectTransform viewportRect = null;
            ScrollRect scrollRect = efficiencyListParent.parent?.GetComponent<ScrollRect>();
            if (scrollRect != null && scrollRect.viewport != null)
            {
                viewportRect = scrollRect.viewport;
            }
            
            // Calculate world positions
            Vector3[] panelCorners = new Vector3[4];
            rect.GetWorldCorners(panelCorners);
            
            string viewportInfo = "N/A";
            if (viewportRect != null)
            {
                Vector3[] viewportCorners = new Vector3[4];
                viewportRect.GetWorldCorners(viewportCorners);
                viewportInfo = $"Viewport: {viewportRect.rect.size}, WorldPos: {viewportRect.position}";
            }
            
            Debug.Log($"🔍 StepEfficiencyIndicator: Supervisor 2 panel details - " +
                     $"Active: {panel.activeSelf}, " +
                     $"Parent: {panel.transform.parent?.name}, " +
                     $"Position: {rect.anchoredPosition}, " +
                     $"SizeDelta: {rect.sizeDelta}, " +
                     $"RectSize: {rect.rect.size}, " +
                     $"WorldPos: {rect.position}, " +
                     $"SiblingIndex: {panel.transform.GetSiblingIndex()}, " +
                     $"TotalChildren: {panel.transform.parent?.childCount}, " +
                     $"{viewportInfo}, " +
                     $"ContentSize: {contentRect?.rect.size}, " +
                     $"ScrollRect: {(scrollRect != null ? "Found" : "Missing")}");
            
            // Check if panel might be clipped
            if (viewportRect != null)
            {
                Rect panelRectWorld = rect.rect;
                Rect viewportRectWorld = viewportRect.rect;
                bool isVisible = panelRectWorld.Overlaps(viewportRectWorld);
                Debug.Log($"🔍 StepEfficiencyIndicator: Supervisor 2 visibility check - " +
                         $"PanelWorldRect: {panelRectWorld}, " +
                         $"ViewportWorldRect: {viewportRectWorld}, " +
                         $"Overlaps: {isVisible}");
            }
        }
        
        Debug.Log($"StepEfficiencyIndicator: Created efficiency panel for {displayName} (Active: {panel.activeSelf}, Parent: {panel.transform.parent?.name})");
    }
    
    void UpdateAgentEfficiencyPanel(string agentId, AgentTrainingResults agentResult)
    {
        if (!agentEfficiencyPanels.ContainsKey(agentId))
            return;
            
        GameObject panel = agentEfficiencyPanels[agentId];
        if (panel == null)
            return;
            
        AgentEfficiencyPanelData panelData = panel.GetComponent<AgentEfficiencyPanelData>();
        
        if (panelData == null)
        {
            Debug.LogWarning($"StepEfficiencyIndicator: PanelData is null for agent {agentId}");
            return;
        }
        
        // Calculate overall efficiency
        float overallEfficiency = CalculateOverallEfficiency(agentResult);
        Color overallColor = GetEfficiencyColor(overallEfficiency);
        
        // Show more detailed stats
        int completedSteps = agentResult.completedSteps;
        int totalSteps = agentResult.totalSteps;
        string overallText = "";
        bool cognitivePending = TryGetCognitiveSequenceStatus(agentId, out int cognitiveCompletedSteps, out int cognitiveTotalSteps, out bool cognitiveSequenceCompleted) &&
                                !cognitiveSequenceCompleted;
        bool observationPipelineLocksPhysical = TryGetPersonaObservationPipelineGate(
            agentId, out string observationStatusLine, out string observationDetailLine);

        if (cognitivePending)
        {
            overallText = $"Cognitive: {cognitiveCompletedSteps}/{cognitiveTotalSteps} steps | Evaluating...";
            overallColor = efficiencyMidColor;
        }
        else if (observationPipelineLocksPhysical)
        {
            overallText = observationStatusLine;
            overallColor = efficiencyMidColor;
        }
        else if (completedSteps > 0)
        {
            overallText = $"Overall: {overallEfficiency:F1}% | {completedSteps}/{totalSteps} steps";
        }
        else
        {
            overallText = $"Overall: {completedSteps}/{totalSteps} steps | Starting...";
            overallColor = new Color(textColor.r, textColor.g, textColor.b, 0.7f);
        }
        
        panelData.overallEfficiencyText.text = overallText;
        panelData.overallEfficiencyText.color = overallColor;
        
        // Update step efficiency displays
        UpdateStepEfficiencyDisplays(panelData, agentResult, cognitivePending, cognitiveCompletedSteps, cognitiveTotalSteps,
            observationPipelineLocksPhysical, observationDetailLine);
    }
    
    float CalculateOverallEfficiency(AgentTrainingResults agentResult)
    {
        if (agentResult.stepProgress == null || agentResult.stepProgress.Count == 0)
            return 0f;
        
        float totalEfficiency = 0f;
        int completedCount = 0;
        
        foreach (var step in agentResult.stepProgress)
        {
            if (step.isCompleted && step.expectedDuration > 0)
            {
                float efficiency = 100f; // Default to 100% (on-time)
                
                // If we have actual completion time (different from expected), calculate efficiency
                if (step.completionTime > 0 && Mathf.Abs(step.completionTime - step.expectedDuration) > 0.01f)
                {
                    // Efficiency = (expectedDuration / completionTime) × 100
                    // >100% means faster than expected, <100% means slower
                    efficiency = (step.expectedDuration / step.completionTime) * 100f;
                }
                // If completionTime equals expectedDuration, assume 100% efficiency (on-time)
                // This is the current behavior since actual time tracking isn't fully implemented yet
                
                totalEfficiency += efficiency;
                completedCount++;
            }
        }
        
        return completedCount > 0 ? totalEfficiency / completedCount : 0f;
    }
    
    Color GetEfficiencyColor(float efficiency)
    {
        if (efficiency > 110f)
            return efficiencyHighColor; // Green - much faster than expected
        else if (efficiency >= 90f)
            return efficiencyMidColor; // Yellow - within acceptable range
        else
            return efficiencyLowColor; // Red - slower than expected
    }
    
    void UpdateStepEfficiencyDisplays(AgentEfficiencyPanelData panelData, AgentTrainingResults agentResult, bool cognitivePending, int cognitiveCompletedSteps, int cognitiveTotalSteps,
        bool observationPipelineLocksPhysical, string observationDetailLine)
    {
        if (agentResult.stepProgress == null)
            return;
        
        // Clear existing step displays
        foreach (Transform child in panelData.stepsContainer)
        {
            Destroy(child.gameObject);
        }

        if (cognitivePending)
        {
            CreateCognitiveLoaderDisplay(panelData.stepsContainer, cognitiveCompletedSteps, cognitiveTotalSteps);
            return;
        }

        if (observationPipelineLocksPhysical)
        {
            CreateObservationPipelineDisplay(panelData.stepsContainer, observationDetailLine);
            return;
        }
        
        // Sort steps by order
        var sortedSteps = agentResult.stepProgress.OrderBy(s => s.stepOrder).ToList();
        
        // Debug: Log step data for first step of each agent to verify uniqueness
        // CRITICAL: Verify each agent has unique targetObjectId
        if (sortedSteps.Count > 0)
        {
            var firstStep = sortedSteps[0];
            Debug.Log($"📋 StepEfficiencyIndicator: Agent {agentResult.agentId} - Step 1: actionType={firstStep.actionType}, targetObjectId={firstStep.targetObjectId ?? "NULL"}, preposition={firstStep.preposition ?? "NULL"}, description={firstStep.description ?? "null"}");
            
            // CRITICAL: Always verify and fix targetObjectId from sequence manager
            // This ensures each agent shows their unique target even if step data is cached incorrectly
            var sequenceManager = AgentSequenceManager.Instance;
            if (sequenceManager != null)
            {
                var sequence = sequenceManager.GetSequence(agentResult.agentId);
                if (sequence != null && sequence.actionSequence != null && sequence.actionSequence.Count > 0)
                {
                    // Get the actual step from sequence (source of truth)
                    var actualStep = sequence.actionSequence.FirstOrDefault(s => s.stepOrder == firstStep.stepOrder);
                    if (actualStep == null && sequence.actionSequence.Count > 0)
                    {
                        // Fallback to first step if stepOrder doesn't match
                        actualStep = sequence.actionSequence[0];
                    }
                    
                    if (actualStep != null && !string.IsNullOrEmpty(actualStep.targetObjectId))
                    {
                        // Always use the actual targetObjectId from sequence (ensures uniqueness)
                        if (firstStep.targetObjectId != actualStep.targetObjectId)
                        {
                            Debug.Log($"🔧 StepEfficiencyIndicator: Fixing targetObjectId for {agentResult.agentId} - Was: {firstStep.targetObjectId}, Should be: {actualStep.targetObjectId}");
                            firstStep.targetObjectId = actualStep.targetObjectId;
                            firstStep.preposition = actualStep.preposition ?? "to";
                            firstStep.actionType = actualStep.actionType ?? "move";
                        }
                    }
                }
            }
        }
        
        // Find the first incomplete step (current step)
        int firstIncompleteIndex = sortedSteps.FindIndex(s => !s.isCompleted);
        
        // Show at most 2 entries: current step (newest) on top, then immediately preceding completed step.
        // If all steps are done, show the last two completed steps newest-first.
        var toShow = new System.Collections.Generic.List<int>();
        if (firstIncompleteIndex >= 0)
        {
            toShow.Add(firstIncompleteIndex);           // current / newest on top
            if (firstIncompleteIndex > 0)
                toShow.Add(firstIncompleteIndex - 1);   // last completed below it
        }
        else if (sortedSteps.Count > 0)
        {
            toShow.Add(sortedSteps.Count - 1);          // most-recently completed on top
            if (sortedSteps.Count > 1)
                toShow.Add(sortedSteps.Count - 2);
        }

        foreach (int idx in toShow)
        {
            var step = sortedSteps[idx];
            
            // Verify and fix targetObjectId for each step before displaying
            var sequenceManager = AgentSequenceManager.Instance;
            if (sequenceManager != null)
            {
                var sequence = sequenceManager.GetSequence(agentResult.agentId);
                if (sequence != null && sequence.actionSequence != null && sequence.actionSequence.Count > step.stepOrder - 1)
                {
                    var actualStep = sequence.actionSequence[step.stepOrder - 1];
                    if (actualStep != null && !string.IsNullOrEmpty(actualStep.targetObjectId))
                    {
                        if (step.targetObjectId != actualStep.targetObjectId)
                        {
                            step.targetObjectId = actualStep.targetObjectId;
                            step.preposition    = actualStep.preposition ?? "to";
                            step.actionType     = actualStep.actionType  ?? "move";
                        }
                    }
                }
            }
            
            CreateStepEfficiencyDisplay(panelData.stepsContainer, step);
        }
    }

    private bool TryGetCognitiveSequenceStatus(string agentId, out int completedSteps, out int totalSteps, out bool sequenceCompleted)
    {
        completedSteps = 0;
        totalSteps = 0;
        sequenceCompleted = true;

        AgentSequenceManager sequenceManager = AgentSequenceManager.Instance;
        if (sequenceManager == null || string.IsNullOrWhiteSpace(agentId))
        {
            return false;
        }

        AgentSequenceData cognitiveSequence = sequenceManager.GetCognitiveSequence(agentId);
        if (cognitiveSequence == null || cognitiveSequence.actionSequence == null || cognitiveSequence.actionSequence.Count == 0)
        {
            return false;
        }

        totalSteps = cognitiveSequence.actionSequence.Count;
        completedSteps = cognitiveSequence.actionSequence.Count(step => step.isStepCompleted);
        sequenceCompleted = completedSteps >= totalSteps && totalSteps > 0;
        return true;
    }

    /// <summary>
    /// Persona flow: JSON cognitive steps can read "complete" before P's <see cref="PhysicalObservationLoop"/>
    /// and second-pass merge finish. Until <see cref="MentalAgentSpawner.phase"/> is
    /// <c>CognitiveProcessComplete</c>, hide ML physical step rows (they use pre-assigned tool IDs, not scan output).
    /// </summary>
    static int ZoneIndexFromPersonaPhysicalAgentId(string agentId)
    {
        if (string.IsNullOrEmpty(agentId)) return -1;
        if (agentId.IndexOf("Technician_01", StringComparison.OrdinalIgnoreCase) >= 0) return 0;
        if (agentId.IndexOf("Technician_02", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
        if (agentId.IndexOf("Supervisor_01", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
        if (agentId.IndexOf("Supervisor_02", StringComparison.OrdinalIgnoreCase) >= 0) return 3;
        return -1;
    }

    static PhysicalObservationLoop FindPhysicalObservationLoopForZone(int zoneIndex)
    {
        string pId = zoneIndex switch
        {
            0 => "SIMPLE_Technician_01",
            1 => "SIMPLE_Technician_02",
            2 => "SIMPLE_Supervisor_01",
            3 => "SIMPLE_Supervisor_02",
            _ => null
        };
        if (string.IsNullOrEmpty(pId)) return null;
        GameObject go = GameObject.Find(pId);
        return go != null ? go.GetComponent<PhysicalObservationLoop>() : null;
    }

    /// <returns>True if this agent's zone is in the persona pipeline and physical ML steps should stay hidden.</returns>
    bool TryGetPersonaObservationPipelineGate(string agentId, out string statusLine, out string detailLine)
    {
        statusLine = null;
        detailLine = null;
        int z = ZoneIndexFromPersonaPhysicalAgentId(agentId);
        if (z < 0) return false;

        MentalAgentSpawner spawner = MentalAgentSpawner.ForZone(z);
        if (spawner == null) return false;

        if (string.Equals(spawner.phase, "CognitiveProcessComplete", StringComparison.Ordinal))
            return false;

        PhysicalObservationLoop obs = FindPhysicalObservationLoopForZone(z);
        bool scanning = obs != null && obs.isObserving;
        bool scanDone = obs != null && obs.observationDone;

        switch (spawner.phase)
        {
            case "Idle":
                statusLine = "Physical steps (locked) | Waiting for cognitive start…";
                detailLine = "ML tool list hidden until environment scan and merge finish (then it reflects the full pipeline).";
                return true;
            case "FirstPass":
            case "BranchBC_Parallel":
                statusLine = "Physical steps (locked) | Cognitive first pass…";
                detailLine = "Mental agents at stations. After this pass, P runs the environment scan; then second pass + merge.";
                return true;
            case "FirstPassComplete_AwaitingPScan":
                statusLine = scanning
                    ? "Physical steps (locked) | Environment scan running…"
                    : "Physical steps (locked) | Starting environment scan…";
                detailLine = scanning && obs != null && !string.IsNullOrEmpty(obs.currentTarget)
                    ? $"P scanning → {obs.currentTarget}"
                    : "Scan data is written to declarative memory as P visits each tool; ML steps stay hidden until merge completes.";
                return true;
            case "SecondPass":
                statusLine = "Physical steps (locked) | Integrating scan (2nd pass)…";
                detailLine = scanDone
                    ? "Cognitive second pass is consuming observation data from memory."
                    : "Waiting for scan completion before integration.";
                return true;
            case "Merge":
                statusLine = "Physical steps (locked) | Merging branches…";
                detailLine = "Final cognitive merge; after this, physical RL steps unlock.";
                return true;
            default:
                statusLine = $"Physical steps (locked) | {spawner.phase}";
                detailLine = "Observation pipeline not finished — pre-baked tool rows stay hidden.";
                return true;
        }
    }

    void CreateObservationPipelineDisplay(Transform parent, string detailLine)
    {
        GameObject row = new GameObject("ObservationPipelineWait");
        row.transform.SetParent(parent, false);
        RectTransform rowRect = row.AddComponent<RectTransform>();
        rowRect.sizeDelta = new Vector2(0, 44);

        Text t = row.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = 10;
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.UpperLeft;
        t.resizeTextForBestFit = false;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.color = new Color(textColor.r, textColor.g, textColor.b, 0.88f);
        t.text = string.IsNullOrEmpty(detailLine)
            ? "Waiting for environment observation and cognitive merge before showing tool steps."
            : detailLine;

        RectTransform tr = t.GetComponent<RectTransform>();
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(4f, 2f);
        tr.offsetMax = new Vector2(-4f, -2f);
    }

    private void CreateCognitiveLoaderDisplay(Transform parent, int completedSteps, int totalSteps)
    {
        GameObject row = new GameObject("CognitiveLoader");
        row.transform.SetParent(parent, false);
        RectTransform rowRect = row.AddComponent<RectTransform>();
        rowRect.sizeDelta = new Vector2(0, 22);

        Text loaderText = row.AddComponent<Text>();
        loaderText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        loaderText.fontSize = 10;
        loaderText.fontStyle = FontStyle.Bold;
        loaderText.alignment = TextAnchor.MiddleLeft;
        loaderText.resizeTextForBestFit = false;
        loaderText.horizontalOverflow = HorizontalWrapMode.Overflow;
        loaderText.verticalOverflow = VerticalWrapMode.Overflow;
        loaderText.color = new Color(textColor.r, textColor.g, textColor.b, 0.85f);
        loaderText.text = $"Cognitive process evaluating... {completedSteps}/{totalSteps} completed";

        RectTransform textRect = loaderText.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
    }
    
    void CreateStepEfficiencyDisplay(Transform parent, StepProgress step)
    {
        GameObject stepPanel = new GameObject($"Step_{step.stepOrder}");
        stepPanel.transform.SetParent(parent, false);
        
        RectTransform stepRect = stepPanel.AddComponent<RectTransform>();
        stepRect.sizeDelta = new Vector2(0, 28);
        
        // Step info text (left side)
        GameObject stepInfoGO = new GameObject("StepInfo");
        stepInfoGO.transform.SetParent(stepPanel.transform, false);
        
        Text stepInfoText = stepInfoGO.AddComponent<Text>();
        string stepStatus = step.isCompleted ? "✓" : "→"; // Arrow for current step
        
        // CRITICAL FIX: Always build description from targetObjectId to ensure agent-specific display
        // Even if description field exists, prioritize targetObjectId to show correct unique targets
        string stepDesc = "";
        
        // Always use targetObjectId as primary source (ensures uniqueness per agent)
        if (!string.IsNullOrEmpty(step.targetObjectId))
        {
            // Generate description from actionType, preposition, and targetObjectId
            // Format: "move to tool_002" or "learn from tool_003"
            if (!string.IsNullOrEmpty(step.actionType))
            {
                string action = step.actionType.ToLower();
                string preposition = !string.IsNullOrEmpty(step.preposition) ? step.preposition : 
                                    (action == "move" ? "to" : "from");
                stepDesc = $"{action} {preposition} {step.targetObjectId}";
            }
            else
            {
                // Fallback: just show target if no actionType
                stepDesc = step.targetObjectId;
            }
        }
        else if (!string.IsNullOrEmpty(step.description))
        {
            // Only use description if targetObjectId is missing
            stepDesc = step.description;
        }
        else
        {
            // Final fallback
            stepDesc = $"Step {step.stepOrder}";
        }
        
        // Truncate if too long
        if (stepDesc.Length > 25)
        {
            stepDesc = stepDesc.Substring(0, 25) + "...";
        }
        
        stepInfoText.text = $"{stepStatus} Step {step.stepOrder}: {stepDesc}";
        stepInfoText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        stepInfoText.fontSize = 12;
        stepInfoText.color = step.isCompleted ? textColor : new Color(textColor.r, textColor.g, textColor.b, 0.8f);
        stepInfoText.fontStyle = FontStyle.Bold;
        stepInfoText.alignment = TextAnchor.MiddleLeft;
        stepInfoText.resizeTextForBestFit = false;
        stepInfoText.horizontalOverflow = HorizontalWrapMode.Overflow;
        stepInfoText.verticalOverflow = VerticalWrapMode.Overflow;
        
        RectTransform stepInfoRect = stepInfoText.GetComponent<RectTransform>();
        stepInfoRect.anchorMin = new Vector2(0, 0);
        stepInfoRect.anchorMax = new Vector2(0.6f, 1);
        stepInfoRect.offsetMin = Vector2.zero;
        stepInfoRect.offsetMax = Vector2.zero;
        
        // Efficiency text (right side)
        GameObject efficiencyGO = new GameObject("Efficiency");
        efficiencyGO.transform.SetParent(stepPanel.transform, false);
        
        Text efficiencyText = efficiencyGO.AddComponent<Text>();
        float efficiency = 0f;
        Color efficiencyColor = textColor;
        string efficiencyDisplay = "";
        
        if (step.isCompleted && step.expectedDuration > 0)
        {
            efficiency = 100f; // Default to 100% (on-time)
            
            // If we have actual completion time (different from expected), calculate efficiency
            if (step.completionTime > 0 && Mathf.Abs(step.completionTime - step.expectedDuration) > 0.01f)
            {
                efficiency = (step.expectedDuration / step.completionTime) * 100f;
            }
            // If completionTime equals expectedDuration, show 100% (on-time)
            
            efficiencyColor = GetEfficiencyColor(efficiency);
            efficiencyDisplay = $"{efficiency:F1}%";
        }
        else
        {
            // Show useful stats even for pending steps
            if (step.expectedDuration > 0)
            {
                efficiencyDisplay = $"Exp: {step.expectedDuration:F1}s";
                efficiencyColor = new Color(textColor.r, textColor.g, textColor.b, 0.7f);
            }
            else
            {
                efficiencyDisplay = "Pending";
                efficiencyColor = new Color(textColor.r, textColor.g, textColor.b, 0.5f);
            }
        }
        
        efficiencyText.text = efficiencyDisplay;
        
        efficiencyText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        efficiencyText.fontSize = 12;
        efficiencyText.color = efficiencyColor;
        efficiencyText.fontStyle = FontStyle.Bold;
        efficiencyText.alignment = TextAnchor.MiddleRight;
        efficiencyText.resizeTextForBestFit = false;
        efficiencyText.horizontalOverflow = HorizontalWrapMode.Overflow;
        efficiencyText.verticalOverflow = VerticalWrapMode.Overflow;
        
        RectTransform efficiencyRect = efficiencyText.GetComponent<RectTransform>();
        efficiencyRect.anchorMin = new Vector2(0.6f, 0);
        efficiencyRect.anchorMax = new Vector2(1, 1);
        efficiencyRect.offsetMin = new Vector2(5, 0);
        efficiencyRect.offsetMax = new Vector2(-5, 0);
        
        // Progress bar showing expected vs actual time
        if (step.isCompleted && step.expectedDuration > 0)
        {
            CreateTimeComparisonBar(stepPanel, step);
        }
    }
    
    void CreateTimeComparisonBar(GameObject parent, StepProgress step)
    {
        GameObject barContainer = new GameObject("TimeBar");
        barContainer.transform.SetParent(parent.transform, false);
        
        RectTransform barRect = barContainer.AddComponent<RectTransform>();
        barRect.anchorMin = new Vector2(0, 0);
        barRect.anchorMax = new Vector2(1, 0.3f);
        barRect.offsetMin = new Vector2(5, 2);
        barRect.offsetMax = new Vector2(-5, -2);
        
        // Background bar (expected duration)
        GameObject bgBar = new GameObject("ExpectedBar");
        bgBar.transform.SetParent(barContainer.transform, false);
        
        Image bgImage = bgBar.AddComponent<Image>();
        bgImage.color = progressBarBackgroundColor;
        
        RectTransform bgRect = bgBar.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.sizeDelta = Vector2.zero;
        
        // Actual time bar (overlay)
        GameObject actualBar = new GameObject("ActualBar");
        actualBar.transform.SetParent(barContainer.transform, false);
        
        Image actualImage = actualBar.AddComponent<Image>();
        float efficiency = (step.expectedDuration / step.completionTime) * 100f;
        actualImage.color = GetEfficiencyColor(efficiency);
        
        RectTransform actualRect = actualBar.GetComponent<RectTransform>();
        actualRect.anchorMin = Vector2.zero;
        actualRect.anchorMax = new Vector2(0, 1);
        actualRect.pivot = new Vector2(0, 0.5f);
        
        // Calculate width: if completionTime < expectedDuration, bar is shorter (faster)
        // If completionTime > expectedDuration, bar extends beyond (slower)
        float maxTime = Mathf.Max(step.expectedDuration, step.completionTime);
        float actualWidth = (step.completionTime / maxTime);
        actualRect.sizeDelta = new Vector2(actualWidth * barRect.rect.width, 0);
        actualRect.anchoredPosition = Vector2.zero;
        
        // Expected duration marker (vertical line)
        GameObject expectedMarker = new GameObject("ExpectedMarker");
        expectedMarker.transform.SetParent(barContainer.transform, false);
        
        Image markerImage = expectedMarker.AddComponent<Image>();
        markerImage.color = new Color(1f, 1f, 1f, 0.8f);
        
        RectTransform markerRect = expectedMarker.GetComponent<RectTransform>();
        float expectedRatio = step.expectedDuration / maxTime;
        markerRect.anchorMin = new Vector2(expectedRatio, 0);
        markerRect.anchorMax = new Vector2(expectedRatio, 1);
        markerRect.sizeDelta = new Vector2(2, 0);
    }
    
    string GetAgentDisplayName(string agentId)
    {
        if (string.IsNullOrEmpty(agentId))
            return "Unknown";
            
        // Convert SIMPLE_Supervisor_01 -> supervisor001
        // Convert SIMPLE_Technician_01 -> technician001
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
        // Return sibling index to ensure proper ordering:
        // 0 = Technician 1 (first)
        // 1 = Technician 2
        // 2 = Supervisor 1
        // 3 = Supervisor 2
        if (agentId.Contains("Technician_01") || agentId.Contains("SIMPLE_Technician_01")) return 0;
        if (agentId.Contains("Technician_02") || agentId.Contains("SIMPLE_Technician_02")) return 1;
        if (agentId.Contains("Supervisor_01") || agentId.Contains("SIMPLE_Supervisor_01")) return 2;
        if (agentId.Contains("Supervisor_02") || agentId.Contains("SIMPLE_Supervisor_02")) return 3;
        return 99; // Other agents go last
    }
    
    void ShowNoDataMessage()
    {
        if (noDataMessage != null || efficiencyListParent == null) return;
        
        noDataMessage = new GameObject("NoDataMessage");
        noDataMessage.transform.SetParent(efficiencyListParent, false);
        
        RectTransform rect = noDataMessage.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(0, 50);
        
        Text text = noDataMessage.AddComponent<Text>();
        text.text = "Waiting for training data...";
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 14;
        text.color = new Color(textColor.r, textColor.g, textColor.b, 0.7f);
        text.alignment = TextAnchor.MiddleCenter;
        text.resizeTextForBestFit = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        
        RectTransform textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        
        Debug.Log("📊 StepEfficiencyIndicator: Showing 'No data' message");
    }
    
    void HideNoDataMessage()
    {
        if (noDataMessage != null)
        {
            Destroy(noDataMessage);
            noDataMessage = null;
        }
    }
}

/// <summary>
/// Helper class to store references to UI elements for each agent efficiency panel
/// </summary>
public class AgentEfficiencyPanelData : MonoBehaviour
{
    public Text agentNameText;
    public Text overallEfficiencyText;
    public Transform stepsContainer;
    
    public void Initialize(Text name, Text overall, Transform steps)
    {
        agentNameText = name;
        overallEfficiencyText = overall;
        stepsContainer = steps;
    }
}

