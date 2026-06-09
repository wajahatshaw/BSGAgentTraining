using UnityEngine;

public class TechnicianMover : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 3f;
    public float rotationSpeed = 180f;
    
    [Header("Info")]
    public string technicianName;
    public Vector3 originalPosition;
    
    [Header("Status")]
    public bool isSelected = false;
    public bool isMoving = false;
    
    private GameObject statusIndicator;
    private GameObject selectionRing;
    
    void Start()
    {
        originalPosition = transform.position;
        statusIndicator = transform.Find("StatusIndicator")?.gameObject;
        
        Debug.Log($"🎯 {technicianName} ready for interaction!");
        Debug.Log($"Click to select, WASD to move, R to return");
    }
    
    void Update()
    {
        HandleInput();
        HandleMovement();
        
        // Simple idle animation when not moving
        if (!isMoving && !isSelected)
        {
            IdleAnimation();
        }
    }
    
    void HandleInput()
    {
        // Mouse click selection
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            
            if (Physics.Raycast(ray, out hit))
            {
                if (hit.collider.gameObject == gameObject || hit.collider.transform.IsChildOf(transform))
                {
                    SelectTechnician();
                }
            }
        }
        
        // Deselect with right click
        if (Input.GetMouseButtonDown(1))
        {
            DeselectTechnician();
        }
    }
    
    void HandleMovement()
    {
        if (!isSelected) return;
        
        Vector3 movement = Vector3.zero;
        bool wasMoving = isMoving;
        
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
        
        // Return to original position
        if (Input.GetKeyDown(KeyCode.R))
        {
            ReturnToOriginalPosition();
            return;
        }
        
        // Apply movement
        if (movement != Vector3.zero)
        {
            // Move the technician
            transform.Translate(movement * moveSpeed * Time.deltaTime, Space.World);
            
            // Rotate to face movement direction
            if (movement != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(movement);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }
            
            UpdateStatusColor(Color.blue); // Blue when moving
        }
        else
        {
            isMoving = false;
            if (isSelected)
            {
                UpdateStatusColor(Color.yellow); // Yellow when selected but not moving
            }
        }
        
        // Log movement start/stop
        if (isMoving && !wasMoving)
        {
            Debug.Log($"🚶 {technicianName} started moving");
        }
        else if (!isMoving && wasMoving)
        {
            Debug.Log($"🛑 {technicianName} stopped moving");
        }
    }
    
    void SelectTechnician()
    {
        // Deselect all other technicians
        TechnicianMover[] allTechnicians = FindObjectsOfType<TechnicianMover>();
        foreach (TechnicianMover tech in allTechnicians)
        {
            tech.DeselectTechnician();
        }
        
        // Select this technician
        isSelected = true;
        CreateSelectionRing();
        UpdateStatusColor(Color.yellow);
        
        Debug.Log($"✅ Selected {technicianName}");
        Debug.Log($"Use WASD to move, R to return to position");
        
        ShowTechnicianInfo();
    }
    
    void DeselectTechnician()
    {
        isSelected = false;
        isMoving = false;
        DestroySelectionRing();
        UpdateStatusColor(Color.green);
        
        Debug.Log($"❌ Deselected {technicianName}");
    }
    
    void CreateSelectionRing()
    {
        if (selectionRing != null) return;
        
        selectionRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        selectionRing.name = "SelectionRing";
        selectionRing.transform.SetParent(transform);
        selectionRing.transform.localPosition = new Vector3(0, -1.5f, 0);
        selectionRing.transform.localScale = new Vector3(3f, 0.1f, 3f);
        selectionRing.GetComponent<Renderer>().material.color = Color.yellow;
        
        // Remove collider to avoid interference
        Destroy(selectionRing.GetComponent<Collider>());
        
        // Add pulsing animation
        SelectionRingAnimator animator = selectionRing.AddComponent<SelectionRingAnimator>();
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
    
    void IdleAnimation()
    {
        // Simple bobbing animation
        float bobHeight = Mathf.Sin(Time.time * 2f) * 0.05f;
        transform.position = new Vector3(transform.position.x, originalPosition.y + bobHeight, transform.position.z);
    }
    
    void ReturnToOriginalPosition()
    {
        transform.position = originalPosition;
        transform.rotation = Quaternion.identity;
        UpdateStatusColor(Color.green);
        
        Debug.Log($"🏠 {technicianName} returned to original position");
    }
    
    void ShowTechnicianInfo()
    {
        string info = $"=== {technicianName.ToUpper()} INFO ===\n";
        info += $"Position: {transform.position}\n";
        info += $"Status: {(isMoving ? "Moving" : isSelected ? "Selected" : "Idle")}\n";
        info += $"\nControls:\n";
        info += $"WASD - Move around\n";
        info += $"R - Return to original position\n";
        info += $"Right Click - Deselect\n";
        
        Debug.Log(info);
    }
    
    void OnTriggerEnter(Collider other)
    {
        if (other.name.StartsWith("Tool_"))
        {
            Debug.Log($"🔧 {technicianName} approached {other.name}");
        }
    }
    
    void OnTriggerExit(Collider other)
    {
        if (other.name.StartsWith("Tool_"))
        {
            Debug.Log($"🚶 {technicianName} left {other.name}");
        }
    }
}

// Simple selection ring animator
public class SelectionRingAnimator : MonoBehaviour
{
    void Update()
    {
        // Pulsing scale animation
        float pulse = 1f + Mathf.Sin(Time.time * 4f) * 0.1f;
        transform.localScale = new Vector3(3f * pulse, 0.1f, 3f * pulse);
        
        // Rotation animation
        transform.Rotate(0, 90f * Time.deltaTime, 0);
    }
}
