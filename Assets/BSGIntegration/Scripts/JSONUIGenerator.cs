using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class JSONUIGenerator : MonoBehaviour
{
    [Header("UI Generation")]
    public bool generateUIOnStart = true;
    public Canvas targetCanvas;
    
    [Header("UI Layout")]
    public Vector2 workerPanelPosition = new Vector2(-400, 200);
    public Vector2 taskPanelPosition = new Vector2(400, 200);
    public Vector2 detailsPanelPosition = new Vector2(0, -200);
    public Vector2 progressPanelPosition = new Vector2(0, 100);
    
    [Header("Panel Sizes")]
    public Vector2 panelSize = new Vector2(300, 200);
    public Vector2 smallPanelSize = new Vector2(200, 150);
    
    [Header("Generated UI")]
    public GameObject workerPanel;
    public GameObject taskPanel;
    public GameObject detailsPanel;
    public GameObject progressPanel;
    
    private SceneUILoader sceneLoader;
    private SceneData sceneData;
    
    void Start()
    {
        if (generateUIOnStart)
        {
            GenerateUI();
        }
    }
    
    public void GenerateUI()
    {
        // Get scene loader reference
        sceneLoader = GetComponent<SceneUILoader>();
        if (sceneLoader == null)
        {
            Debug.LogError("SceneUILoader not found! Cannot generate UI without JSON data.");
            return;
        }
        
        // Get the scene data
        sceneData = sceneLoader.GetSceneData();
        if (sceneData == null)
        {
            Debug.LogError("No scene data available! Make sure JSON is loaded first.");
            return;
        }
        
        // Find or create canvas
        if (targetCanvas == null)
        {
            targetCanvas = FindObjectOfType<Canvas>();
            if (targetCanvas == null)
            {
                CreateCanvas();
            }
        }
        
        // Generate UI panels
        GenerateWorkerPanel();
        GenerateTaskPanel();
        GenerateDetailsPanel();
        GenerateProgressPanel();
        
        Debug.Log("JSON UI panels generated successfully!");
    }
    
    void CreateCanvas()
    {
        GameObject canvasGO = new GameObject("JSON_Generated_Canvas");
        targetCanvas = canvasGO.AddComponent<Canvas>();
        targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        targetCanvas.sortingOrder = 100; // Ensure it's on top
        
        // Add CanvasScaler
        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        
        // Add GraphicRaycaster
        canvasGO.AddComponent<GraphicRaycaster>();
        
        Debug.Log("Created new Canvas for JSON UI");
    }
    
    void GenerateWorkerPanel()
    {
        if (workerPanel != null) return;
        
        // Create worker panel
        workerPanel = CreateUIPanel("JSON_Worker_Panel", workerPanelPosition, smallPanelSize);
        
        // Add title
        CreateUIText(workerPanel, "Worker_Title", "WORKER", new Vector2(0, 60), 18, Color.white, true);
        
        // Add worker name (will be updated by SceneUILoader)
        CreateUIText(workerPanel, "Worker_Name", "Loading...", new Vector2(0, 20), 16, Color.yellow);
        
        // Add status
        CreateUIText(workerPanel, "Worker_Status", "Status: Available", new Vector2(0, -10), 14, Color.white);
        
        // Add skills summary
        CreateUIText(workerPanel, "Worker_Skills", "Skills: Loading...", new Vector2(0, -40), 12, new Color(0.7f, 0.7f, 0.7f));
        
        // Connect to SceneUILoader
        if (sceneLoader != null)
        {
            sceneLoader.workerTitleText = workerPanel.transform.Find("Worker_Name").GetComponent<Text>();
        }
    }
    
    void GenerateTaskPanel()
    {
        if (taskPanel != null) return;
        
        // Create task panel
        taskPanel = CreateUIPanel("JSON_Task_Panel", taskPanelPosition, smallPanelSize);
        
        // Add title
        CreateUIText(taskPanel, "Task_Title", "CURRENT TASK", new Vector2(0, 60), 18, Color.white, true);
        
        // Add task ID
        CreateUIText(taskPanel, "Task_ID", "Loading...", new Vector2(0, 20), 16, new Color(1f, 0.6f, 0f));
        
        // Add action description
        CreateUIText(taskPanel, "Task_Action", "Action: Loading...", new Vector2(0, -10), 14, Color.white);
        
        // Add tool info
        CreateUIText(taskPanel, "Task_Tool", "Tool: Loading...", new Vector2(0, -40), 12, new Color(0.7f, 0.7f, 0.7f));
        
        // Connect to SceneUILoader
        if (sceneLoader != null)
        {
            sceneLoader.currentTaskText = taskPanel.transform.Find("Task_ID").GetComponent<Text>();
        }
    }
    
    void GenerateDetailsPanel()
    {
        if (detailsPanel != null) return;
        
        // Create details panel
        detailsPanel = CreateUIPanel("JSON_Details_Panel", detailsPanelPosition, panelSize);
        
        // Add title
        CreateUIText(detailsPanel, "Details_Title", "TASK DETAILS", new Vector2(0, 80), 18, Color.white, true);
        
        // Add skills section
        CreateUIText(detailsPanel, "Skills_Title", "Required Skills:", new Vector2(0, 40), 16, Color.cyan, true);
        CreateUIText(detailsPanel, "Skills_List", "Loading skills...", new Vector2(0, 10), 12, Color.white);
        
        // Add duration
        CreateUIText(detailsPanel, "Duration_Label", "Duration:", new Vector2(-100, -20), 14, Color.white);
        CreateUIText(detailsPanel, "Duration_Value", "Loading...", new Vector2(50, -20), 14, Color.yellow);
        
        // Add action type
        CreateUIText(detailsPanel, "Type_Label", "Type:", new Vector2(-100, -50), 14, Color.white);
        CreateUIText(detailsPanel, "Type_Value", "Loading...", new Vector2(50, -50), 14, Color.yellow);
        
        // Add preconditions
        CreateUIText(detailsPanel, "Preconditions_Title", "Preconditions:", new Vector2(0, -80), 14, Color.cyan, true);
        CreateUIText(detailsPanel, "Preconditions_List", "Loading...", new Vector2(0, -110), 12, Color.white);
        
        // Connect to SceneUILoader
        if (sceneLoader != null)
        {
            sceneLoader.taskDetailsText = detailsPanel.transform.Find("Skills_List").GetComponent<Text>();
        }
    }
    
    void GenerateProgressPanel()
    {
        if (progressPanel != null) return;
        
        // Create progress panel
        progressPanel = CreateUIPanel("JSON_Progress_Panel", progressPanelPosition, smallPanelSize);
        
        // Add title
        CreateUIText(progressPanel, "Progress_Title", "WORKFLOW PROGRESS", new Vector2(0, 60), 18, Color.white, true);
        
        // Add step counter
        CreateUIText(progressPanel, "Step_Counter", "Step 1 of 2", new Vector2(0, 20), 16, Color.green);
        
        // Add navigation hints
        CreateUIText(progressPanel, "Nav_Hint1", "← Previous", new Vector2(-80, -20), 14, new Color(0.4f, 0.7f, 1f));
        CreateUIText(progressPanel, "Nav_Hint2", "Next →", new Vector2(80, -20), 14, new Color(0.4f, 0.7f, 1f));
        
        // Add execute button hint
        CreateUIText(progressPanel, "Execute_Hint", "Press SPACE to execute", new Vector2(0, -50), 12, new Color(1f, 0.6f, 0f));
        
        // Connect to SceneUILoader
        if (sceneLoader != null)
        {
            sceneLoader.stepProgressText = progressPanel.transform.Find("Step_Counter").GetComponent<Text>();
        }
    }
    
    GameObject CreateUIPanel(string name, Vector2 position, Vector2 size)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(targetCanvas.transform, false);
        
        // Add Image component for background
        Image bgImage = panel.AddComponent<Image>();
        bgImage.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        
        // Set RectTransform
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        
        // Add border
        AddBorder(panel);
        
        return panel;
    }
    
    void AddBorder(GameObject panel)
    {
        // Create border outline
        GameObject border = new GameObject("Border");
        border.transform.SetParent(panel.transform, false);
        
        Image borderImage = border.AddComponent<Image>();
        borderImage.color = Color.cyan;
        
        RectTransform borderRect = border.GetComponent<RectTransform>();
        borderRect.anchorMin = Vector2.zero;
        borderRect.anchorMax = Vector2.one;
        borderRect.sizeDelta = new Vector2(4, 4);
        borderRect.anchoredPosition = Vector2.zero;
    }
    
    void CreateUIText(GameObject parent, string name, string text, Vector2 position, int fontSize, Color color, bool bold = false)
    {
        GameObject textGO = new GameObject(name);
        textGO.transform.SetParent(parent.transform, false);
        
        Text textComponent = textGO.AddComponent<Text>();
        textComponent.text = text;
        textComponent.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        textComponent.fontSize = fontSize;
        textComponent.color = color;
        textComponent.alignment = TextAnchor.MiddleCenter;
        
        if (bold)
        {
            textComponent.fontStyle = FontStyle.Bold;
        }
        
        RectTransform rect = textComponent.GetComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(280, fontSize + 10);
    }
    
    // Public methods for external control
    public void RegenerateUI()
    {
        // Clear existing panels
        if (workerPanel != null) DestroyImmediate(workerPanel);
        if (taskPanel != null) DestroyImmediate(taskPanel);
        if (detailsPanel != null) DestroyImmediate(detailsPanel);
        if (progressPanel != null) DestroyImmediate(progressPanel);
        
        // Reset references
        workerPanel = null;
        taskPanel = null;
        detailsPanel = null;
        progressPanel = null;
        
        // Generate new UI
        GenerateUI();
    }
    
    public void UpdateWorkerInfo(string workerName, string status, string skills)
    {
        if (workerPanel != null)
        {
            Transform nameTransform = workerPanel.transform.Find("Worker_Name");
            Transform statusTransform = workerPanel.transform.Find("Worker_Status");
            Transform skillsTransform = workerPanel.transform.Find("Worker_Skills");
            
            if (nameTransform != null) nameTransform.GetComponent<Text>().text = workerName;
            if (statusTransform != null) statusTransform.GetComponent<Text>().text = $"Status: {status}";
            if (skillsTransform != null) skillsTransform.GetComponent<Text>().text = $"Skills: {skills}";
        }
    }
    
    public void UpdateTaskInfo(string taskId, string action, string tool)
    {
        if (taskPanel != null)
        {
            Transform idTransform = taskPanel.transform.Find("Task_ID");
            Transform actionTransform = taskPanel.transform.Find("Task_Action");
            Transform toolTransform = taskPanel.transform.Find("Task_Tool");
            
            if (idTransform != null) idTransform.GetComponent<Text>().text = taskId;
            if (actionTransform != null) actionTransform.GetComponent<Text>().text = $"Action: {action}";
            if (toolTransform != null) toolTransform.GetComponent<Text>().text = $"Tool: {tool}";
        }
    }
}
