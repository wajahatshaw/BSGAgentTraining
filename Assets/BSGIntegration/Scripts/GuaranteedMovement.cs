using UnityEngine;

/// <summary>
/// GUARANTEED MOVEMENT - This script WILL make entities move visibly.
/// Attach this to any GameObject and it will force all technicians/supervisors to move.
/// </summary>
public class GuaranteedMovement : MonoBehaviour
{
    [Header("GUARANTEED MOVEMENT")]
    [Tooltip("Start immediately when scene begins")]
    public bool startImmediately = true;
    
    [Tooltip("Movement speed (high for visibility)")]
    public float movementSpeed = 15f;
    
    [Tooltip("Show debug information")]
    public bool showDebug = true;
    
    void Start()
    {
        if (startImmediately)
        {
            Invoke(nameof(StartGuaranteedMovement), 0.2f);
        }
    }
    
    void StartGuaranteedMovement()
    {
        if (showDebug)
            Debug.Log("🎯 GUARANTEED MOVEMENT - Starting visible movement system!");
        
        // Find all entities
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        int movedEntities = 0;
        
        foreach (GameObject obj in allObjects)
        {
            if (ShouldMoveThisObject(obj))
            {
                SetupGuaranteedMovement(obj);
                movedEntities++;
            }
        }
        
        if (showDebug)
            Debug.Log($"🎉 GUARANTEED MOVEMENT - Set up movement for {movedEntities} entities!");
    }
    
    bool ShouldMoveThisObject(GameObject obj)
    {
        string name = obj.name.ToLower();
        return name.Contains("technician") || 
               name.Contains("supervisor") || 
               name.Contains("simple_technician") ||
               name.Contains("simple_supervisor");
    }
    
    void SetupGuaranteedMovement(GameObject obj)
    {
        // Remove ALL existing movement components
        Component[] allComponents = obj.GetComponents<Component>();
        foreach (Component comp in allComponents)
        {
            if (comp != this && comp != obj.transform && 
                (comp.GetType().Name.Contains("Movement") || 
                 comp.GetType().Name.Contains("Controller") ||
                 comp.GetType().Name.Contains("SimpleMovement")))
            {
                if (Application.isPlaying)
                    Destroy(comp);
                else
                    DestroyImmediate(comp);
            }
        }
        
        // Add our guaranteed movement component
        SimpleGuaranteedMovement movement = obj.GetComponent<SimpleGuaranteedMovement>();
        if (movement == null)
        {
            movement = obj.AddComponent<SimpleGuaranteedMovement>();
        }
        
        // Configure movement
        movement.speed = movementSpeed;
        movement.showDebug = showDebug;
        
        // Setup physics
        SetupPhysicsForMovement(obj);
        
        if (showDebug)
            Debug.Log($"✅ GUARANTEED MOVEMENT - Set up {obj.name} with speed {movementSpeed}");
    }
    
    void SetupPhysicsForMovement(GameObject obj)
    {
        // Rigidbody
        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = obj.AddComponent<Rigidbody>();
        }
        
        // Configure for guaranteed movement
        rb.useGravity = false;
        rb.mass = 1f;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        
        // Collider
        Collider col = obj.GetComponent<Collider>();
        if (col == null)
        {
            col = obj.AddComponent<BoxCollider>();
        }
        col.isTrigger = false;
    }
    
    void Update()
    {
        // Press G to restart movement
        if (Input.GetKeyDown(KeyCode.G))
        {
            if (showDebug)
                Debug.Log("🎮 G key pressed - Restarting guaranteed movement!");
            StartGuaranteedMovement();
        }
    }
    
    [ContextMenu("Start Guaranteed Movement")]
    public void StartGuaranteedMovementNow()
    {
        StartGuaranteedMovement();
    }
}

/// <summary>
/// Simple movement component that GUARANTEES visible movement.
/// </summary>
public class SimpleGuaranteedMovement : MonoBehaviour
{
    [Header("Simple Guaranteed Movement")]
    public float speed = 0.12f;
    public bool showDebug = true;
    
    private Rigidbody rb;
    private Vector3 direction;
    private float nextDirectionChange;
    private Vector3 startPos;
    
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        startPos = transform.position;
        
        // Set initial direction
        SetRandomDirection();
        nextDirectionChange = Time.time + 2f;
        
        if (showDebug)
            Debug.Log($"🚀 SimpleGuaranteedMovement started on {name} with speed {speed}");
    }
    
    void Update()
    {
        // Change direction every 2 seconds
        if (Time.time >= nextDirectionChange)
        {
            SetRandomDirection();
            nextDirectionChange = Time.time + 2f + Random.Range(-0.5f, 0.5f);
        }
        
        // Apply movement
        ApplyMovement();
        
        // Check boundaries
        CheckBoundaries();
        
        // Debug every 2 seconds
        if (showDebug && Time.frameCount % 120 == 0)
        {
            Debug.Log($"🏃 {name} moving at {rb.linearVelocity.magnitude:F1} speed");
        }
    }
    
    void SetRandomDirection()
    {
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)).normalized;
        
        if (showDebug)
            Debug.Log($"🔄 {name} new direction: {direction}");
    }
    
    void ApplyMovement()
    {
        if (rb == null) return;
        
        // Force high-speed movement
        Vector3 velocity = direction * speed;
        velocity.y = 0; // Keep on ground
        
        rb.linearVelocity = velocity;
        
        // FORCE GROUND LEVEL POSITIONING
        Vector3 pos = transform.position;
        pos.y = 1f; // Force to ground level
        transform.position = pos;
        
        // Double-check that we're actually moving
        if (rb.linearVelocity.magnitude < speed * 0.8f)
        {
            rb.linearVelocity = direction * speed;
        }
    }
    
    void CheckBoundaries()
    {
        Vector3 pos = transform.position;
        
        // FORCE GROUND LEVEL AND BOUNDARY CONSTRAINTS
        pos.y = 1f; // Force to ground level
        
        // Keep within ground plane boundaries
        pos.x = Mathf.Clamp(pos.x, -10f, 10f); // X boundaries
        pos.z = Mathf.Clamp(pos.z, -10f, 10f); // Z boundaries
        
        transform.position = pos;
        float distance = Vector3.Distance(new Vector3(pos.x, 0, pos.z), new Vector3(startPos.x, 0, startPos.z));
        
        if (distance > 12f)
        {
            // Turn back toward center
            Vector3 toCenter = (startPos - pos).normalized;
            direction = new Vector3(toCenter.x, 0, toCenter.z).normalized;
            
            if (showDebug)
                Debug.Log($"🔄 {name} turning back to center");
        }
    }
    
    void OnCollisionEnter(Collision collision)
    {
        // Bounce off walls
        if (collision.gameObject.name.ToLower().Contains("wall"))
        {
            Vector3 reflection = Vector3.Reflect(direction, collision.contacts[0].normal);
            direction = new Vector3(reflection.x, 0, reflection.z).normalized;
            
            if (showDebug)
                Debug.Log($"🏐 {name} bounced off wall");
        }
    }
}
