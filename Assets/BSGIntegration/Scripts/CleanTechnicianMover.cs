using UnityEngine;

public class CleanTechnicianMover : MonoBehaviour
{
    [Header("Clean Technician")]
    public string technicianName;
    public Vector3 originalPosition;
    public float moveSpeed = 3f;
    public bool isSelected = false;
    
    private GameObject statusIndicator;
    private GameObject selectionRing;
    
    void Start()
    {
        originalPosition = transform.position;
        statusIndicator = transform.Find("StatusIndicator")?.gameObject;
        
        Debug.Log($"🎯 Clean technician ready: {technicianName}");
    }
    
    void Update()
    {
        HandleMouseInput();
        HandleKeyboardMovement();
        
        // Clean floating animation when not selected
        if (!isSelected)
        {
            CleanFloatingAnimation();
        }
    }
    
    void HandleMouseInput()
    {
        // Mouse click selection
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            
            if (Physics.Raycast(ray, out hit))
            {
                if (hit.collider.gameObject == gameObject)
                {
                    SelectCleanTechnician();
                }
            }
        }
        
        // Right click to deselect
        if (Input.GetMouseButtonDown(1))
        {
            DeselectAllCleanTechnicians();
        }
    }
    
    void HandleKeyboardMovement()
    {
        if (!isSelected) return;
        
        Vector3 movement = Vector3.zero;
        bool isMoving = false;
        
        // WASD movement
        if (Input.GetKey("w"))
        {
            movement += Vector3.forward;
            isMoving = true;
        }
        if (Input.GetKey("s"))
        {
            movement += Vector3.back;
            isMoving = true;
        }
        if (Input.GetKey("a"))
        {
            movement += Vector3.left;
            isMoving = true;
        }
        if (Input.GetKey("d"))
        {
            movement += Vector3.right;
            isMoving = true;
        }
        
        // Return to original position
        if (Input.GetKeyDown("r"))
        {
            ReturnToOriginalPosition();
            return;
        }
        
        // Apply clean movement
        if (movement != Vector3.zero)
        {
            transform.Translate(movement * moveSpeed * Time.deltaTime, Space.World);
            transform.Rotate(0, 90f * Time.deltaTime, 0);
            UpdateCleanStatusColor(new Color(0.2f, 0.6f, 0.9f, 1f)); // Blue when moving
        }
        else
        {
            UpdateCleanStatusColor(new Color(0.9f, 0.7f, 0.2f, 1f)); // Yellow when selected
        }
    }
    
    void SelectCleanTechnician()
    {
        // Deselect all other clean technicians
        CleanTechnicianMover[] allTechnicians = FindObjectsOfType<CleanTechnicianMover>();
        foreach (CleanTechnicianMover tech in allTechnicians)
        {
            tech.DeselectCleanTechnician();
        }
        
        // Select this clean technician
        isSelected = true;
        CreateCleanSelectionRing();
        UpdateCleanStatusColor(new Color(0.9f, 0.7f, 0.2f, 1f)); // Yellow
        
        Debug.Log($"✅ Selected clean technician: {technicianName}");
        Debug.Log($"Use WASD to move, R to return to position");
    }
    
    void DeselectCleanTechnician()
    {
        isSelected = false;
        DestroyCleanSelectionRing();
        UpdateCleanStatusColor(new Color(0.2f, 0.8f, 0.2f, 1f)); // Green
    }
    
    void DeselectAllCleanTechnicians()
    {
        CleanTechnicianMover[] allTechnicians = FindObjectsOfType<CleanTechnicianMover>();
        foreach (CleanTechnicianMover tech in allTechnicians)
        {
            tech.DeselectCleanTechnician();
        }
        
        Debug.Log("❌ Deselected all clean technicians");
    }
    
    void CreateCleanSelectionRing()
    {
        if (selectionRing != null) return;
        
        selectionRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        selectionRing.name = "CleanSelectionRing";
        selectionRing.transform.SetParent(transform);
        selectionRing.transform.localPosition = new Vector3(0, -1f, 0);
        selectionRing.transform.localScale = new Vector3(4f, 0.1f, 4f);
        
        // Clean selection ring color (no metallic properties)
        selectionRing.GetComponent<Renderer>().material.color = new Color(0.9f, 0.7f, 0.2f, 0.8f);
        
        // Remove collider to avoid interference
        Destroy(selectionRing.GetComponent<Collider>());
        
        // Add clean pulsing animation
        CleanSelectionRingAnimator animator = selectionRing.AddComponent<CleanSelectionRingAnimator>();
    }
    
    void DestroyCleanSelectionRing()
    {
        if (selectionRing != null)
        {
            Destroy(selectionRing);
            selectionRing = null;
        }
    }
    
    void UpdateCleanStatusColor(Color color)
    {
        if (statusIndicator != null)
        {
            // Simple color change (no metallic properties)
            statusIndicator.GetComponent<Renderer>().material.color = color;
        }
    }
    
    void CleanFloatingAnimation()
    {
        // Clean floating animation
        float floatHeight = Mathf.Sin(Time.time * 1.2f) * 0.1f;
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
        UpdateCleanStatusColor(new Color(0.2f, 0.8f, 0.2f, 1f)); // Green
        
        Debug.Log($"🏠 {technicianName} returned to original position");
    }
}

// Clean selection ring animator
public class CleanSelectionRingAnimator : MonoBehaviour
{
    void Update()
    {
        // Clean pulsing animation
        float pulse = 1f + Mathf.Sin(Time.time * 3f) * 0.1f;
        transform.localScale = new Vector3(4f * pulse, 0.1f, 4f * pulse);
        
        // Clean rotation
        transform.Rotate(0, 60f * Time.deltaTime, 0);
    }
}
