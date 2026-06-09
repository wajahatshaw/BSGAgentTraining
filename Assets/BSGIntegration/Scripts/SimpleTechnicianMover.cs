using UnityEngine;

public class SimpleTechnicianMover : MonoBehaviour
{
    [Header("Technician Info")]
    public string technicianName;
    public Vector3 originalPosition;
    
    [Header("Movement")]
    public float moveSpeed = 3f;
    public bool isSelected = false;
    
    private GameObject statusIndicator;
    private GameObject selectionRing;
    
    void Start()
    {
        originalPosition = transform.position;
        statusIndicator = transform.Find("StatusIndicator")?.gameObject;
        
        Debug.Log($"🎯 {technicianName} sphere technician ready!");
    }
    
    void Update()
    {
        HandleInput();
        HandleMovement();
        
        // Simple floating animation when not selected
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
        
        // Right click to deselect
        if (Input.GetMouseButtonDown(1))
        {
            DeselectTechnician();
        }
    }
    
    void HandleMovement()
    {
        if (!isSelected) return;
        
        Vector3 movement = Vector3.zero;
        
        // WASD movement
        if (Input.GetKey(KeyCode.W))
        {
            movement += Vector3.forward;
        }
        if (Input.GetKey(KeyCode.S))
        {
            movement += Vector3.back;
        }
        if (Input.GetKey(KeyCode.A))
        {
            movement += Vector3.left;
        }
        if (Input.GetKey(KeyCode.D))
        {
            movement += Vector3.right;
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
            transform.Translate(movement * moveSpeed * Time.deltaTime, Space.World);
            UpdateStatusColor(Color.blue); // Blue when moving
            
            // Add rotation while moving
            transform.Rotate(0, 90f * Time.deltaTime, 0);
        }
        else
        {
            UpdateStatusColor(Color.yellow); // Yellow when selected but not moving
        }
    }
    
    void SelectTechnician()
    {
        // Deselect all other technicians
        SimpleTechnicianMover[] allTechnicians = FindObjectsOfType<SimpleTechnicianMover>();
        foreach (SimpleTechnicianMover tech in allTechnicians)
        {
            tech.DeselectTechnician();
        }
        
        // Select this technician
        isSelected = true;
        CreateSelectionRing();
        UpdateStatusColor(Color.yellow);
        
        Debug.Log($"✅ Selected sphere technician: {technicianName}");
        Debug.Log($"Use WASD to move, R to return to position");
    }
    
    void DeselectTechnician()
    {
        isSelected = false;
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
        selectionRing.transform.localPosition = new Vector3(0, -0.8f, 0);
        selectionRing.transform.localScale = new Vector3(3f, 0.1f, 3f);
        selectionRing.GetComponent<Renderer>().material.color = Color.yellow;
        
        // Remove collider to avoid interference
        Destroy(selectionRing.GetComponent<Collider>());
        
        // Add pulsing animation
        SelectionRingPulse pulse = selectionRing.AddComponent<SelectionRingPulse>();
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
        // Simple floating animation for sphere technicians
        float floatHeight = Mathf.Sin(Time.time * 2f) * 0.1f;
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
        
        Debug.Log($"🏠 {technicianName} returned to original position");
    }
}

// Simple selection ring animation
public class SelectionRingPulse : MonoBehaviour
{
    void Update()
    {
        // Pulsing scale animation
        float pulse = 1f + Mathf.Sin(Time.time * 4f) * 0.2f;
        transform.localScale = new Vector3(3f * pulse, 0.1f, 3f * pulse);
        
        // Rotation animation
        transform.Rotate(0, 120f * Time.deltaTime, 0);
    }
}
