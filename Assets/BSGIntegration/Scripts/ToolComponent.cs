using UnityEngine;
using UnityEngine.UI;

public class ToolComponent : MonoBehaviour
{
    [Header("Tool Information")]
    public string toolId;
    public bool isAvailable = true;
    public bool isActive = false;
    public string currentUser = null;
    
    [Header("Tool Properties")]
    public string temperature = "cold";
    public string power = "off";
    public bool locked = false;
    
    [Header("UI Elements")]
    [Tooltip("Creates world-space info panel above tool. Disable for clean scene view.")]
    public bool enableWorldInfoPanel = false;
    public GameObject toolInfoPanel;
    public Text toolNameText;
    public Text toolStatusText;
    public Text toolPropertiesText;
    
    [Header("Visual Feedback")]
    public Material availableMaterial;
    public Material unavailableMaterial;
    public Material activeMaterial;
    public Material lockedMaterial;
    
    private Renderer toolRenderer;
    private ToolState currentState;
    
    void Awake()
    {
        toolRenderer = GetComponent<Renderer>();
        if (toolRenderer == null)
        {
            toolRenderer = GetComponentInChildren<Renderer>();
        }
    }
    
    public void Initialize(string id, ToolState state)
    {
        toolId = id;
        currentState = state;
        
        // Apply state from JSON
        if (state != null)
        {
            isAvailable = state.isAvailable;
            isActive = state.isActive;
            currentUser = state.currentUser;
            
            if (state.properties != null)
            {
                temperature = state.properties.temperature ?? "unknown";
                power = state.properties.power ?? "off";
                locked = state.properties.locked;
            }
        }
        
        UpdateVisuals();
        if (enableWorldInfoPanel)
        {
            CreateToolInfoPanel();
        }
        else if (toolInfoPanel != null)
        {
            Destroy(toolInfoPanel);
            toolInfoPanel = null;
        }
    }
    
    void UpdateVisuals()
    {
        if (toolRenderer == null) return;
        
        Material targetMaterial = null;
        
        if (!isAvailable)
        {
            targetMaterial = unavailableMaterial;
        }
        else if (locked)
        {
            targetMaterial = lockedMaterial;
        }
        else if (isActive)
        {
            targetMaterial = activeMaterial;
        }
        else
        {
            targetMaterial = availableMaterial;
        }
        
        // Apply material if available, otherwise use color coding
        if (targetMaterial != null)
        {
            toolRenderer.material = targetMaterial;
        }
        else
        {
            // Fallback color coding
            Color toolColor = GetToolColor();
            toolRenderer.material.color = toolColor;
        }
    }
    
    Color GetToolColor()
    {
        if (!isAvailable) return Color.gray;
        if (locked) return Color.yellow;
        if (isActive) return Color.blue;
        if (power == "on") return Color.green;
        if (power == "off") return Color.red;
        return Color.white;
    }
    
    void CreateToolInfoPanel()
    {
        if (toolInfoPanel != null) return;
        
        // Create a simple info panel above the tool
        GameObject panel = new GameObject("ToolInfoPanel");
        panel.transform.SetParent(transform);
        panel.transform.localPosition = Vector3.up * 2.5f;
        
        // Add Canvas for UI
        Canvas canvas = panel.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        
        // Add CanvasScaler
        CanvasScaler scaler = panel.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        
        // Add GraphicRaycaster
        panel.AddComponent<GraphicRaycaster>();
        
        // Create background panel
        GameObject background = new GameObject("Background");
        background.transform.SetParent(panel.transform);
        
        Image bgImage = background.AddComponent<Image>();
        bgImage.color = new Color(0, 0, 0, 0.8f);
        bgImage.rectTransform.sizeDelta = new Vector2(200, 120);
        
        // Create tool name text
        GameObject nameGO = new GameObject("ToolName");
        nameGO.transform.SetParent(panel.transform);
        
        toolNameText = nameGO.AddComponent<Text>();
        toolNameText.text = $"Tool: {toolId}";
        toolNameText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        toolNameText.fontSize = 14;
        toolNameText.color = Color.white;
        toolNameText.alignment = TextAnchor.MiddleCenter;
        toolNameText.rectTransform.anchoredPosition = new Vector2(0, 40);
        toolNameText.rectTransform.sizeDelta = new Vector2(180, 20);
        
        // Create status text
        GameObject statusGO = new GameObject("ToolStatus");
        statusGO.transform.SetParent(panel.transform);
        
        toolStatusText = statusGO.AddComponent<Text>();
        toolStatusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        toolStatusText.fontSize = 12;
        toolStatusText.color = Color.white;
        toolStatusText.alignment = TextAnchor.MiddleCenter;
        toolStatusText.rectTransform.anchoredPosition = new Vector2(0, 15);
        toolStatusText.rectTransform.sizeDelta = new Vector2(180, 20);
        
        // Create properties text
        GameObject propsGO = new GameObject("ToolProperties");
        propsGO.transform.SetParent(panel.transform);
        
        toolPropertiesText = propsGO.AddComponent<Text>();
        toolPropertiesText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        toolPropertiesText.fontSize = 10;
        toolPropertiesText.color = Color.white;
        toolPropertiesText.alignment = TextAnchor.MiddleCenter;
        toolPropertiesText.rectTransform.anchoredPosition = new Vector2(0, -10);
        toolPropertiesText.rectTransform.sizeDelta = new Vector2(180, 40);
        
        toolInfoPanel = panel;
        UpdateToolInfo();
    }
    
    void UpdateToolInfo()
    {
        if (toolNameText != null)
        {
            toolNameText.text = $"Tool: {toolId}";
        }
        
        if (toolStatusText != null)
        {
            string status = isAvailable ? "Available" : "Unavailable";
            if (isActive) status = "Active";
            if (locked) status = "Locked";
            toolStatusText.text = $"Status: {status}";
        }
        
        if (toolPropertiesText != null)
        {
            string properties = "";
            if (!string.IsNullOrEmpty(temperature)) properties += $"Temp: {temperature}\n";
            if (!string.IsNullOrEmpty(power)) properties += $"Power: {power}\n";
            if (locked) properties += "Locked: Yes";
            
            toolPropertiesText.text = properties;
        }
    }
    
    // Public methods for external control
    public void SetAvailable(bool available)
    {
        isAvailable = available;
        UpdateVisuals();
        UpdateToolInfo();
    }
    
    public void SetActive(bool active)
    {
        isActive = active;
        UpdateVisuals();
        UpdateToolInfo();
    }
    
    public void SetLocked(bool lockState)
    {
        locked = lockState;
        UpdateVisuals();
        UpdateToolInfo();
    }
    
    public void SetPower(string powerState)
    {
        power = powerState;
        UpdateVisuals();
        UpdateToolInfo();
    }
    
    public void SetTemperature(string temp)
    {
        temperature = temp;
        UpdateToolInfo();
    }
    
    public void SetUser(string user)
    {
        currentUser = user;
        UpdateToolInfo();
    }
    
    // Interaction method
    public void Interact()
    {
        Debug.Log($"Interacting with tool: {toolId}");
        
        // Toggle power if tool is available and not locked
        if (isAvailable && !locked)
        {
            if (power == "off")
            {
                SetPower("on");
            }
            else
            {
                SetPower("off");
            }
        }
    }
    
    void OnMouseDown()
    {
        Interact();
    }
}
