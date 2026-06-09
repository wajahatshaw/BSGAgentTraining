using UnityEngine;

public class JSONTechnicianMover : MonoBehaviour
{
    [Header("Agent Info")]
    public string agentId;
    public string agentName;
    public Vector3 originalPosition;
    
    [Header("Movement")]
    public float moveSpeed = 4f;
    public bool isSelected = false;
    
    private GameObject statusIndicator;
    private GameObject selectionRing;
    
    void Start()
    {
        originalPosition = transform.position;
        statusIndicator = transform.Find("StatusIndicator")?.gameObject;
        
        Debug.Log($"🎯 JSON Technician ready: {agentName} ({agentId})");
    }
    
    void Update()
    {
        HandleInput();
        HandleMovement();
        
        // Floating animation when not selected
        if (!isSelected)
        {
            FloatingAnimation();
        }
    }
    
    void HandleInput()
    {
        // Mouse click to select
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            
            if (Physics.Raycast(ray, out hit))
            {
                if (hit.collider.gameObject == gameObject)
                {
                    SelectTechnician();
                }
            }
        }
        
        // Right click to deselect all
        if (Input.GetMouseButtonDown(1))
        {
            DeselectAllTechnicians();
        }
    }
    
    void HandleMovement()
    {
        if (!isSelected) return;
        
        Vector3 movement = Vector3.zero;
        bool isMoving = false;
        
        // WASD movement
        if (Input.GetKey(KeyCode.W))
        {
            movement += Vector3.forward;
            isMoving = true;
        }
        if (Input.GetKey(KeyCode.S))
        {
            movement += Vector3.back;
            isMoving = true;
        }
        if (Input.GetKey(KeyCode.A))
        {
            movement += Vector3.left;
            isMoving = true;
        }
        if (Input.GetKey(KeyCode.D))
        {
            movement += Vector3.right;
            isMoving = true;
        }
        
        // Note: Input disabled due to Input System conflict
        // Use context menu or inspector button instead
        
        // Apply movement
        if (movement != Vector3.zero)
        {
            // Move the technician
            transform.Translate(movement * moveSpeed * Time.deltaTime, Space.World);
            
            // Rotate while moving
            transform.Rotate(0, 120f * Time.deltaTime, 0);
            
            // Update status to blue (moving)
            UpdateStatusColor(Color.blue);
        }
        else
        {
            // Yellow when selected but not moving
            UpdateStatusColor(Color.yellow);
        }
    }
    
    void SelectTechnician()
    {
        // Deselect all other technicians
        JSONTechnicianMover[] allTechnicians = FindObjectsOfType<JSONTechnicianMover>();
        foreach (JSONTechnicianMover tech in allTechnicians)
        {
            tech.DeselectTechnician();
        }
        
        // Select this technician
        isSelected = true;
        CreateSelectionRing();
        UpdateStatusColor(Color.yellow);
        
        Debug.Log($"✅ Selected JSON technician: {agentName}");
        Debug.Log($"Agent ID: {agentId}");
        Debug.Log($"Use WASD to move, R to return to position");
        
        ShowTechnicianInfo();
    }
    
    void DeselectTechnician()
    {
        isSelected = false;
        DestroySelectionRing();
        UpdateStatusColor(Color.green);
    }
    
    void DeselectAllTechnicians()
    {
        JSONTechnicianMover[] allTechnicians = FindObjectsOfType<JSONTechnicianMover>();
        foreach (JSONTechnicianMover tech in allTechnicians)
        {
            tech.DeselectTechnician();
        }
        
        Debug.Log("❌ Deselected all technicians");
    }
    
    void CreateSelectionRing()
    {
        if (selectionRing != null) return;
        
        selectionRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        selectionRing.name = "SelectionRing";
        selectionRing.transform.SetParent(transform);
        selectionRing.transform.localPosition = new Vector3(0, -1f, 0);
        selectionRing.transform.localScale = new Vector3(4f, 0.1f, 4f);
        selectionRing.GetComponent<Renderer>().material.color = Color.yellow;
        
        // Remove collider to avoid interference
        Destroy(selectionRing.GetComponent<Collider>());
        
        // Add pulsing animation
        JSONSelectionRingAnimator animator = selectionRing.AddComponent<JSONSelectionRingAnimator>();
    }
    
    void DestroySelectionRing()
    {
        if (selectionRing != null)
        {
            Destroy(selectionRing);
            selectionRing = null;
        }
    }
    
    void UpdateStatusColor(Color color)
    {
        if (statusIndicator != null)
        {
            statusIndicator.GetComponent<Renderer>().material.color = color;
        }
    }
    
    void FloatingAnimation()
    {
        // Gentle floating animation
        float floatHeight = Mathf.Sin(Time.time * 1.5f) * 0.15f;
        transform.position = new Vector3(
            transform.position.x, 
            originalPosition.y + floatHeight, 
            transform.position.z
        );
    }
    
    void ReturnToOriginalPosition()
    {
        transform.position = originalPosition;
        transform.rotation = Quaternion.identity;
        UpdateStatusColor(Color.green);
        
        Debug.Log($"🏠 {agentName} returned to original position");
    }
    
    void ShowTechnicianInfo()
    {
        string info = $"=== {agentName.ToUpper()} INFO ===\n";
        info += $"Agent ID: {agentId}\n";
        info += $"Position: {transform.position}\n";
        info += $"Status: {(isSelected ? "Selected" : "Idle")}\n";
        info += $"\nControls:\n";
        info += $"WASD - Move around plan\n";
        info += $"R - Return to original position\n";
        info += $"Right Click - Deselect all\n";
        
        Debug.Log(info);
    }
    
    void OnTriggerEnter(Collider other)
    {
        if (other.name.StartsWith("Tool_"))
        {
            Debug.Log($"🔧 {agentName} approached {other.name}");
        }
    }
    
    void OnTriggerExit(Collider other)
    {
        if (other.name.StartsWith("Tool_"))
        {
            Debug.Log($"🚶 {agentName} left {other.name}");
        }
    }
}

// Selection ring animator for JSON technicians
public class JSONSelectionRingAnimator : MonoBehaviour
{
    void Update()
    {
        // Pulsing scale animation
        float pulse = 1f + Mathf.Sin(Time.time * 5f) * 0.15f;
        transform.localScale = new Vector3(4f * pulse, 0.1f, 4f * pulse);
        
        // Rotation animation
        transform.Rotate(0, 150f * Time.deltaTime, 0);
    }
}
