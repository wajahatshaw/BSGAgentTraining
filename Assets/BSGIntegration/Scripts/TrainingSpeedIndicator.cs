using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Training Speed Indicator - Shows step completion speed (steps per minute/hour)
/// Displays real-time training speed from reinforcement learning model
/// Tracks speed for each step individually - resets when agents move to next step
/// 
/// DATA SOURCE: Gets data from MLTrainingResultsWriter which tracks step progress from
/// the ML-Agents reinforcement learning training model (BSGMLAgent.cs).
/// Updates in real-time as agents complete steps during training.
/// </summary>
public class TrainingSpeedIndicator : MonoBehaviour
{
    [Header("UI References")]
    public Canvas speedCanvas;
    public GameObject speedPanel;
    
    [Header("Panel Settings")]
    public Vector2 panelPosition = new Vector2(20, 20); // Bottom-left corner (positive offset from anchor)
    public Vector2 panelSize = new Vector2(280, 80);
    public float updateInterval = 1f; // Update every second
    
    [Header("Visual Settings")]
    public Color backgroundColor = new Color(0, 0, 0, 0.85f);
    public Color headerColor = new Color(0.2f, 0.6f, 0.2f, 1f);
    public Color textColor = Color.white;
    public Color speedHighColor = new Color(0.3f, 0.9f, 0.3f, 1f); // Green for good speed
    public Color speedMidColor = new Color(1f, 0.8f, 0.2f, 1f);   // Yellow for moderate speed
    public Color speedLowColor = new Color(0.9f, 0.3f, 0.3f, 1f);  // Red for slow speed
    
    private MLTrainingResultsWriter resultsWriter; // Primary data source
    private Text speedText;
    private Text headerText;
    private float lastUpdateTime = 0f;
    
    // Step-based tracking
    private int currentStepNumber = 1; // Track which step we're measuring speed for
    private float stepStartTime = 0f; // When current step tracking started
    private int lastStepCompletions = 0; // Last count of completions for current step
    private bool stepTrackingStarted = false;
    
    void Start()
    {
        Debug.Log("🚀 TrainingSpeedIndicator: Starting initialization...");
        Debug.Log("📊 TrainingSpeedIndicator: Tracking STEP completion speed (resets per step)");
        
        // Find results writer - primary data source
        resultsWriter = MLTrainingResultsWriter.Instance;
        
        if (resultsWriter == null)
        {
            Debug.LogWarning("⚠️ TrainingSpeedIndicator: MLTrainingResultsWriter not found yet, will retry...");
            Invoke("RetryInitialize", 2f);
            return;
        }
        
        Debug.Log("✅ TrainingSpeedIndicator: Found MLTrainingResultsWriter");
        
        // Create the speed panel UI
        CreateSpeedPanel();
        
        // Initial update with a small delay
        StartCoroutine(InitialUpdateAfterDelay());
    }
    
    void RetryInitialize()
    {
        resultsWriter = MLTrainingResultsWriter.Instance;
        
        if (resultsWriter != null)
        {
            Debug.Log("✅ TrainingSpeedIndicator: Successfully found MLTrainingResultsWriter on retry");
            CreateSpeedPanel();
            StartCoroutine(InitialUpdateAfterDelay());
        }
        else
        {
            Debug.LogWarning("⚠️ TrainingSpeedIndicator: Still no MLTrainingResultsWriter, will keep retrying...");
            Invoke("RetryInitialize", 2f);
        }
    }
    
    void Update()
    {
        // Update speed display at regular intervals
        if (Time.time - lastUpdateTime >= updateInterval)
        {
            UpdateSpeedDisplay();
            lastUpdateTime = Time.time;
        }
    }
    
    void CreateSpeedPanel()
    {
        // Create main canvas if not assigned
        if (speedCanvas == null)
        {
            GameObject canvasGO = new GameObject("TrainingSpeedCanvas");
            speedCanvas = canvasGO.AddComponent<Canvas>();
            speedCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            speedCanvas.sortingOrder = 100;
            
            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0f;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            
            canvasGO.AddComponent<GraphicRaycaster>();
            canvasGO.SetActive(false); // Hidden — panel logic preserved but not displayed
            speedCanvas.enabled = true;
        }
        
        // Create main panel
        if (speedPanel == null)
        {
            speedPanel = new GameObject("SpeedPanel");
            speedPanel.transform.SetParent(speedCanvas.transform, false);
            
            Image panelImage = speedPanel.AddComponent<Image>();
            panelImage.color = backgroundColor;
            
            RectTransform panelRect = speedPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0, 0);
            panelRect.anchorMax = new Vector2(0, 0);
            panelRect.pivot = new Vector2(0, 0);
            panelRect.anchoredPosition = panelPosition;
            panelRect.sizeDelta = panelSize;
            
            speedPanel.SetActive(true);
        }
        
        CreateHeader();
        CreateSpeedText();
        
        if (speedPanel != null)
        {
            speedPanel.SetActive(true);
        }
    }
    
    void CreateHeader()
    {
        GameObject headerBG = new GameObject("HeaderBackground");
        headerBG.transform.SetParent(speedPanel.transform, false);
        headerBG.transform.SetAsLastSibling();
        
        Image headerImage = headerBG.AddComponent<Image>();
        headerImage.color = headerColor;
        
        RectTransform headerRect = headerBG.GetComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0, 1);
        headerRect.anchorMax = new Vector2(1, 1);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.anchoredPosition = new Vector2(0, 0);
        headerRect.offsetMin = new Vector2(0, -30);
        headerRect.offsetMax = new Vector2(0, 0);
        
        GameObject headerTextGO = new GameObject("HeaderText");
        headerTextGO.transform.SetParent(headerBG.transform, false);
        
        headerText = headerTextGO.AddComponent<Text>();
        headerText.text = "TRAINING SPEED";
        headerText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        headerText.fontSize = 16;
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
    
    void CreateSpeedText()
    {
        GameObject speedTextGO = new GameObject("SpeedText");
        speedTextGO.transform.SetParent(speedPanel.transform, false);
        
        speedText = speedTextGO.AddComponent<Text>();
        speedText.text = "Calculating...";
        speedText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        speedText.fontSize = 16;
        speedText.color = textColor;
        speedText.fontStyle = FontStyle.Bold;
        speedText.alignment = TextAnchor.MiddleCenter;
        speedText.horizontalOverflow = HorizontalWrapMode.Wrap;
        speedText.verticalOverflow = VerticalWrapMode.Overflow;
        speedText.lineSpacing = 1.0f;
        
        RectTransform textRect = speedText.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0, 0);
        textRect.anchorMax = new Vector2(1, 1);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.offsetMin = new Vector2(10, 4);
        textRect.offsetMax = new Vector2(-10, -36);
    }
    
    private System.Collections.IEnumerator InitialUpdateAfterDelay()
    {
        yield return null;
        yield return new WaitForSeconds(0.5f);
        UpdateSpeedDisplay();
    }
    
    /// <summary>
    /// Check if ML-Agents training server is actually connected
    /// Uses multiple methods for reliable detection
    /// </summary>
    private bool IsMLAgentsServerConnected()
    {
        // Method 1: Check Academy communicator (primary method)
        try
        {
            var academy = Unity.MLAgents.Academy.Instance;
            if (academy != null)
            {
                var communicatorField = typeof(Unity.MLAgents.Academy).GetField("m_Communicator",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (communicatorField != null)
                {
                    var communicator = communicatorField.GetValue(academy);
                    if (communicator != null)
                    {
                        var isConnectedProperty = communicator.GetType().GetProperty("IsConnected");
                        if (isConnectedProperty != null)
                        {
                            bool connected = (bool)isConnectedProperty.GetValue(communicator);
                            if (connected) return true; // If connected, return immediately
                        }
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"⚠️ TrainingSpeedIndicator: Academy check failed: {e.Message}");
        }
        
        // Do not treat local Sentis/ONNX inference (InferenceOnly) as a Python training server.
        if (RagInferenceSceneController.IsInferenceSceneActive())
            return false;

        // Method 2 (training scenes only): agents with episodeSteps often means Python is connected.
        try
        {
            BSGMLAgent[] agents = FindObjectsOfType<BSGMLAgent>();
            if (agents != null && agents.Length > 0)
            {
                int agentsReceivingActions = 0;
                foreach (var agent in agents)
                {
                    if (agent != null && agent.episodeSteps > 0)
                    {
                        agentsReceivingActions++;
                    }
                }

                if (agentsReceivingActions > 0)
                    return true;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"⚠️ TrainingSpeedIndicator: Agent check failed: {e.Message}");
        }
        
        // Method 3: Check if we have training results with active agents
        // This is a fallback - if agents are working, server is likely connected
        if (resultsWriter != null)
        {
            var trainingResults = resultsWriter.GetTrainingResults();
            if (trainingResults != null && trainingResults.agentResults != null && trainingResults.agentResults.Count > 0)
            {
                // Check if any agent has step progress (indicates active training)
                foreach (var agentResult in trainingResults.agentResults.Values)
                {
                    if (agentResult.stepProgress != null && agentResult.stepProgress.Count > 0)
                    {
                        // If any step is completed or in progress, server is likely connected
                        return true;
                    }
                }
            }
        }
        
        return false;
    }
    
    void UpdateSpeedDisplay()
    {
        if (resultsWriter == null)
        {
            resultsWriter = MLTrainingResultsWriter.Instance;
            if (resultsWriter == null)
            {
                if (speedText != null)
                {
                    speedText.text = "Waiting for training data...";
                    speedText.color = new Color(textColor.r, textColor.g, textColor.b, 0.7f);
                }
                return;
            }
        }
        
        if (speedPanel == null || speedText == null)
        {
            CreateSpeedPanel();
            return;
        }
        
        if (!speedPanel.activeSelf)
        {
            speedPanel.SetActive(true);
        }
        
        // Check if ML-Agents server is actually connected
        bool serverConnected = IsMLAgentsServerConnected();
        
        // Get training results
        var trainingResults = resultsWriter.GetTrainingResults();
        if (trainingResults == null || trainingResults.agentResults == null || trainingResults.agentResults.Count == 0)
        {
            if (serverConnected)
            {
                speedText.text = "Server connected\nWaiting for agents...";
            }
            else
            {
                speedText.text = "Server not connected\nWaiting for training...";
            }
            speedText.color = new Color(textColor.r, textColor.g, textColor.b, 0.7f);
            
            // Debug log connection status (only log occasionally to avoid spam)
            if (Time.time % 5f < updateInterval)
            {
                Debug.Log($"📊 TrainingSpeedIndicator: Server connected = {serverConnected}, Training results = {(trainingResults != null ? "exists" : "null")}");
            }
            return;
        }
        
        // Find the current step number being worked on by agents
        // Get the minimum current step index across all agents (the step they're all working on)
        int minCurrentStep = int.MaxValue;
        int maxStepOrder = 0;
        int totalStepCompletions = 0;
        
        foreach (var agentResult in trainingResults.agentResults.Values)
        {
            if (agentResult.stepProgress != null && agentResult.stepProgress.Count > 0)
            {
                // Find the first incomplete step (current step)
                var incompleteStep = agentResult.stepProgress.FirstOrDefault(s => !s.isCompleted);
                if (incompleteStep != null)
                {
                    minCurrentStep = Mathf.Min(minCurrentStep, incompleteStep.stepOrder);
                }
                
                // Also track max step order and count completions
                foreach (var step in agentResult.stepProgress)
                {
                    maxStepOrder = Mathf.Max(maxStepOrder, step.stepOrder);
                    if (step.isCompleted)
                    {
                        totalStepCompletions++;
                    }
                }
            }
        }
        
        // If all steps are completed, use the max step order + 1
        if (minCurrentStep == int.MaxValue && maxStepOrder > 0)
        {
            minCurrentStep = maxStepOrder + 1;
        }
        
        // If no step data yet, default to step 1
        if (minCurrentStep == int.MaxValue)
        {
            minCurrentStep = 1;
        }
        
        // Check if we've moved to a new step - if so, reset tracking
        if (minCurrentStep != currentStepNumber)
        {
            currentStepNumber = minCurrentStep;
            stepStartTime = Time.time;
            lastStepCompletions = 0;
            stepTrackingStarted = false;
            Debug.Log($"🔄 TrainingSpeedIndicator: Moved to Step {currentStepNumber} - Resetting speed tracking");
        }
        
        // Count completions for current step across all agents
        int currentStepCompletions = 0;
        foreach (var agentResult in trainingResults.agentResults.Values)
        {
            if (agentResult.stepProgress != null)
            {
                var step = agentResult.stepProgress.FirstOrDefault(s => s.stepOrder == currentStepNumber);
                if (step != null && step.isCompleted)
                {
                    currentStepCompletions++;
                }
            }
        }
        
        // Start tracking when first completion happens
        if (currentStepCompletions > 0 && !stepTrackingStarted)
        {
            stepTrackingStarted = true;
            stepStartTime = Time.time;
            lastStepCompletions = 0;
            Debug.Log($"✅ TrainingSpeedIndicator: Step {currentStepNumber} tracking started - First completion detected");
        }
        
        // Calculate speed
        string speedDisplay = "";
        Color speedColor = textColor;
        
        if (stepTrackingStarted && currentStepCompletions > 0)
        {
            float elapsedTime = Time.time - stepStartTime;
            
            if (elapsedTime > 0)
            {
                // Calculate step completions per minute
                float stepsPerMinute = (currentStepCompletions / elapsedTime) * 60f;
                
                // If speed is very high (>60 steps/min), show as steps per hour
                if (stepsPerMinute >= 60f)
                {
                    float stepsPerHour = stepsPerMinute * 60f;
                    speedDisplay = $"Speed: {stepsPerHour:F1} steps/hour";
                    
                    if (stepsPerHour > 3600f)
                        speedColor = speedHighColor;
                    else if (stepsPerHour >= 1800f)
                        speedColor = speedMidColor;
                    else
                        speedColor = speedLowColor;
                }
                else
                {
                    speedDisplay = $"Speed: {stepsPerMinute:F1} steps/min";
                    
                    if (stepsPerMinute > 30f)
                        speedColor = speedHighColor;
                    else if (stepsPerMinute >= 15f)
                        speedColor = speedMidColor;
                    else
                        speedColor = speedLowColor;
                }
                
                // Show step number and completions
                speedDisplay += $"\nStep {currentStepNumber}: {currentStepCompletions}/{trainingResults.agentResults.Count}";
            }
        }
        else if (serverConnected)
        {
            speedDisplay = $"Step {currentStepNumber}\nWaiting for completions...";
            speedColor = new Color(textColor.r, textColor.g, textColor.b, 0.7f);
        }
        else
        {
            speedDisplay = "Server not connected\nWaiting for training...";
            speedColor = new Color(textColor.r, textColor.g, textColor.b, 0.7f);
        }
        
        // Update display
        if (speedText != null)
        {
            speedText.text = speedDisplay;
            speedText.color = speedColor;
        }
        
        lastStepCompletions = currentStepCompletions;
    }
}
