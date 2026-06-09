using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Episode Counter & Timer Display - Shows current episode number, elapsed training time, and total steps
/// Displays real-time training progress metrics
/// 
/// DATA SOURCE:
/// - Episode count: MLTrainingLogger.GetTotalEpisodes() (total episodes across all agents)
/// - Training time: Time.time (elapsed time since training started)
/// - Total steps: Sum of all BSGMLAgent.episodeSteps or MLTrainingResultsWriter
/// 
/// POSITION: Top-left corner (opposite of StepEfficiencyIndicator which is top-right)
/// DESIGN: Small, compact panel to minimize screen space
/// </summary>
public class EpisodeCounterTimer : MonoBehaviour
{
    [Header("UI References")]
    public Canvas episodeCanvas;
    public GameObject episodePanel;
    
    [Header("Panel Settings")]
    // Align with TrainingSpeedIndicator (X = 20) and gauge component
    // TrainingSpeedIndicator is at (20, 20) bottom-left
    // Align all components to X = 20 for left alignment
    // Position: X = 20 (aligned with TrainingSpeedIndicator), Y = below 4 gauges (50 + (45+12)*4 = 278)
    public Vector2 panelPosition = new Vector2(20, -278); // Top-left corner, aligned with TrainingSpeedIndicator X position
    public Vector2 panelSize = new Vector2(240, 110); // Small, compact size
    public float updateInterval = 1f; // Update every second
    
    [Header("Visual Settings")]
    public Color backgroundColor = new Color(0, 0, 0, 0.85f);
    public Color headerColor = new Color(0.2f, 0.6f, 0.2f, 1f);
    public Color textColor = Color.white;
    
    private MLTrainingLogger trainingLogger;
    private MLTrainingResultsWriter resultsWriter;
    private Text episodeText;
    private Text timeText;
    private Text stepsText;
    private Text headerText;
    private GameObject contentContainer; // Container for content with padding
    
    private float trainingStartTime = 0f;
    private bool trainingStarted = false;
    private float lastUpdateTime = 0f;
    
    void Start()
    {
        Debug.Log("🚀 EpisodeCounterTimer: Starting initialization...");
        
        // Find data sources
        trainingLogger = MLTrainingLogger.Instance;
        resultsWriter = MLTrainingResultsWriter.Instance;
        
        // Create the UI panel
        CreateEpisodePanel();
        
        // Initialize training start time
        trainingStartTime = Time.time;
        trainingStarted = true;
        
        Debug.Log("✅ EpisodeCounterTimer: Initialized");
    }
    
    void Update()
    {
        // Update display at regular intervals
        if (Time.time - lastUpdateTime >= updateInterval)
        {
            UpdateDisplay();
            lastUpdateTime = Time.time;
        }
    }
    
    void CreateEpisodePanel()
    {
        // Create main canvas if not assigned
        if (episodeCanvas == null)
        {
            GameObject canvasGO = new GameObject("EpisodeCounterCanvas");
            episodeCanvas = canvasGO.AddComponent<Canvas>();
            episodeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            episodeCanvas.sortingOrder = 105; // Below StepEfficiencyIndicator (110) but above most UI
            
            // Add CanvasScaler
            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0f;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            
            // Add GraphicRaycaster
            canvasGO.AddComponent<GraphicRaycaster>();
            
            canvasGO.SetActive(false); // Hidden — panel logic preserved but not displayed
            episodeCanvas.enabled = true;
            
            Debug.Log("✅ EpisodeCounterTimer: Created EpisodeCounterCanvas (hidden)");
        }
        
        // Create main panel
        if (episodePanel == null)
        {
            episodePanel = new GameObject("EpisodePanel");
            episodePanel.transform.SetParent(episodeCanvas.transform, false);
            
            // Add Image component for background
            Image panelImage = episodePanel.AddComponent<Image>();
            panelImage.color = backgroundColor;
            
            // Set position and size - TOP-LEFT CORNER
            RectTransform panelRect = episodePanel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0, 1); // Top-left anchor
            panelRect.anchorMax = new Vector2(0, 1); // Top-left anchor
            panelRect.pivot = new Vector2(0, 1); // Pivot at top-left
            panelRect.anchoredPosition = panelPosition; // Positive X, negative Y from top-left corner
            panelRect.sizeDelta = panelSize;
            
            // Ensure panel is active
            episodePanel.SetActive(true);
            
            Debug.Log($"✅ EpisodeCounterTimer: Panel positioned at {panelPosition}, size {panelSize}");
        }
        
        // Create header
        CreateHeader();
        
        // Create content container with padding
        CreateContentContainer();
        
        // Create content text elements
        CreateContentTexts();
    }
    
    void CreateHeader()
    {
        // Create header background (like DistanceToTargetMeter)
        GameObject headerBG = new GameObject("HeaderBackground");
        headerBG.transform.SetParent(episodePanel.transform, false);
        headerBG.transform.SetAsLastSibling();
        
        Image headerImage = headerBG.AddComponent<Image>();
        headerImage.color = headerColor;
        
        RectTransform headerRect = headerBG.GetComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0, 1);
        headerRect.anchorMax = new Vector2(1, 1);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.anchoredPosition = new Vector2(0, 0);
        headerRect.offsetMin = new Vector2(0, -30); // 30px height
        headerRect.offsetMax = new Vector2(0, 0);
        
        // Create header text
        GameObject headerTextGO = new GameObject("HeaderText");
        headerTextGO.transform.SetParent(headerBG.transform, false);
        
        Text header = headerTextGO.AddComponent<Text>();
        header.text = "EPISODE & TIMER";
        header.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        header.fontSize = 16;
        header.fontStyle = FontStyle.Bold;
        header.color = textColor; // Use textColor for better contrast on header background
        header.alignment = TextAnchor.MiddleCenter;
        
        RectTransform textRect = headerTextGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        
        headerText = header;
    }
    
    void CreateContentContainer()
    {
        // Create content container with padding (like DistanceToTargetMeter)
        contentContainer = new GameObject("ContentContainer");
        contentContainer.transform.SetParent(episodePanel.transform, false);
        contentContainer.transform.SetSiblingIndex(0); // Place before header
        
        RectTransform containerRect = contentContainer.AddComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0, 0);
        containerRect.anchorMax = new Vector2(1, 1);
        containerRect.offsetMin = new Vector2(10, 10); // Left and bottom padding
        containerRect.offsetMax = new Vector2(-10,10); // Right and top padding (40 = header height + spacing)
    }
    
    void CreateContentTexts()
    {
        float lineHeight = 24f; // Slightly taller for better readability
        float headerHeight = 30f; // Match header height
        float topPadding = 8f; // Padding from header (like DistanceToTargetMeter)
        float bottomPadding = 10f; // Bottom padding (like DistanceToTargetMeter)
        // Calculate Y positions from top of panel (negative values for anchoredPosition)
        float startY = -(headerHeight + topPadding); // Negative because anchored to top
        float spacing = 6f; // Spacing between lines
        
        // Episode text
        episodeText = CreateTextLine("EpisodeText", "Episode: 0", startY, lineHeight);
        startY -= (lineHeight + spacing);
        
        // Time text
        timeText = CreateTextLine("TimeText", "Training Time: 00:00:00", startY, lineHeight);
        startY -= (lineHeight + spacing);
        
        // Steps text
        stepsText = CreateTextLine("StepsText", "Total Steps: 0", startY, lineHeight);
    }
    
    Text CreateTextLine(string name, string initialText, float yPos, float height)
    {
        GameObject textGO = new GameObject(name);
        // Parent to content container if it exists, otherwise to panel
        textGO.transform.SetParent(contentContainer != null ? contentContainer.transform : episodePanel.transform, false);
        
        Text text = textGO.AddComponent<Text>();
        text.text = initialText;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 12;
        text.color = textColor;
        text.alignment = TextAnchor.MiddleLeft;
        
        RectTransform textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0, 1);
        textRect.anchorMax = new Vector2(1, 1);
        textRect.pivot = new Vector2(0, 1);
        // Position from top of panel: yPos is already negative (going down from top)
        textRect.anchoredPosition = new Vector2(0, yPos);
        textRect.sizeDelta = new Vector2(0, height);
        // Use offsetMin/offsetMax for proper padding (left, right, and bottom)
        float bottomPadding = 10f; // Bottom padding like DistanceToTargetMeter
        textRect.offsetMin = new Vector2(12, yPos - height); // Left: 12px, Bottom: yPos - height
        textRect.offsetMax = new Vector2(-12, yPos); // Right: -12px, Top: yPos
        
        return text;
    }
    
    void UpdateDisplay()
    {
        // Update episode count
        int totalEpisodes = 0;
        if (trainingLogger != null)
        {
            totalEpisodes = trainingLogger.GetTotalEpisodes();
        }
        else
        {
            // Fallback: count from MLTrainingResultsWriter
            if (resultsWriter != null)
            {
                var results = resultsWriter.GetTrainingResults();
                if (results != null && results.agentResults != null)
                {
                    foreach (var agentResult in results.agentResults.Values)
                    {
                        totalEpisodes += agentResult.episodes;
                    }
                }
            }
        }
        
        if (episodeText != null)
        {
            episodeText.text = $"Episode: {totalEpisodes:N0}";
        }
        
        // Update training time
        if (trainingStarted && timeText != null)
        {
            float elapsedTime = Time.time - trainingStartTime;
            int hours = Mathf.FloorToInt(elapsedTime / 3600f);
            int minutes = Mathf.FloorToInt((elapsedTime % 3600f) / 60f);
            int seconds = Mathf.FloorToInt(elapsedTime % 60f);
            
            timeText.text = $"Training Time: {hours:D2}:{minutes:D2}:{seconds:D2}";
        }
        
        // Update total steps - calculate cumulative steps across all episodes
        // Total Steps = Sum of step completions across all episodes for all agents
        int totalSteps = 0;
        
        // Method 1: Use MLTrainingLogger if available (tracks cumulative steps)
        if (trainingLogger != null)
        {
            var allStats = trainingLogger.GetAllAgentStats();
            if (allStats != null)
            {
                foreach (var stats in allStats.Values)
                {
                    // totalSteps field in AgentTrainingStats tracks cumulative steps
                    totalSteps += stats.totalSteps;
                }
            }
        }
        
        // Method 2: Fallback - calculate from MLTrainingResultsWriter
        if (totalSteps == 0 && resultsWriter != null)
        {
            var results = resultsWriter.GetTrainingResults();
            if (results != null && results.agentResults != null)
            {
                foreach (var agentResult in results.agentResults.Values)
                {
                    // Calculate cumulative: episodes * average steps per episode
                    // For now, use episodes * completedSteps as approximation
                    // (This assumes each episode completes similar number of steps)
                    if (agentResult.episodes > 0)
                    {
                        // Estimate: episodes * average completed steps
                        // Use completedSteps as current episode progress indicator
                        int avgStepsPerEpisode = agentResult.completedSteps; // Current episode progress
                        totalSteps += agentResult.episodes * Mathf.Max(avgStepsPerEpisode, 1);
                    }
                    else
                    {
                        // First episode - use current completed steps
                        totalSteps += agentResult.completedSteps;
                    }
                }
            }
        }
        
        // Method 3: Final fallback - sum current episode steps from agents
        if (totalSteps == 0)
        {
            BSGMLAgent[] agents = FindObjectsOfType<BSGMLAgent>();
            if (agents != null)
            {
                foreach (var agent in agents)
                {
                    totalSteps += agent.episodeSteps;
                }
            }
        }
        
        if (stepsText != null)
        {
            stepsText.text = $"Total Steps: {totalSteps:N0}";
        }
    }
}

