using UnityEngine;

/// <summary>
/// Forces immediate, continuous, high-speed movement on all entities.
/// This script ensures movement is ALWAYS visible and working.
/// </summary>
public class ForceVisibleMovement : MonoBehaviour
{
    [Header("FORCE VISIBLE MOVEMENT")]
    [Tooltip("Start movement immediately when scene begins")]
    public bool startImmediately = true;
    
    [Tooltip("High speed for visible movement")]
    public float highSpeed = 12f;
    
    [Tooltip("Direction change interval (seconds)")]
    public float directionChangeTime = 2f;
    
    [Tooltip("Show debug information")]
    public bool showDebugInfo = true;
    
    [Tooltip("Force movement every frame")]
    public bool forceEveryFrame = true;
    
    void Start()
    {
        if (startImmediately)
        {
            Invoke(nameof(ForceStartAllMovement), 0.1f);
        }
    }
    
    void Update()
    {
        // Force movement every frame if enabled
        if (forceEveryFrame)
        {
            ForceMovementOnAllEntities();
        }
        
        // Press M to manually force movement
        if (Input.GetKeyDown(KeyCode.M))
        {
            ForceStartAllMovement();
        }
    }
    
    void ForceStartAllMovement()
    {
        if (showDebugInfo)
            Debug.Log("🚀 FORCE VISIBLE MOVEMENT - Starting aggressive movement!");
        
        // Find all potential entities
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        int entitiesProcessed = 0;
        
        foreach (GameObject obj in allObjects)
        {
            if (IsEntity(obj))
            {
                ProcessEntity(obj);
                entitiesProcessed++;
            }
        }
        
        if (showDebugInfo)
            Debug.Log($"🎉 Processed {entitiesProcessed} entities for movement!");
    }
    
    bool IsEntity(GameObject obj)
    {
        string name = obj.name.ToLower();
        return name.Contains("technician") || 
               name.Contains("supervisor") || 
               name.Contains("agent") ||
               name.Contains("simple_technician") ||
               name.Contains("simple_supervisor");
    }
    
    void ProcessEntity(GameObject obj)
    {
        // Remove existing movement components
        Component[] existingComponents = obj.GetComponents<Component>();
        foreach (Component comp in existingComponents)
        {
            if (comp.GetType().Name.Contains("Movement") && comp != this)
            {
                DestroyImmediate(comp);
            }
        }
        
        // Add our aggressive movement component
        AggressiveMovement movement = obj.GetComponent<AggressiveMovement>();
        if (movement == null)
        {
            movement = obj.AddComponent<AggressiveMovement>();
        }
        
        // Configure movement
        movement.speed = highSpeed;
        movement.directionChangeTime = directionChangeTime;
        movement.showDebugInfo = showDebugInfo;
        
        // Ensure physics components
        EnsurePhysicsComponents(obj);
        
        if (showDebugInfo)
            Debug.Log($"✅ Added aggressive movement to {obj.name} with speed {highSpeed}");
    }
    
    void EnsurePhysicsComponents(GameObject obj)
    {
        // Rigidbody
        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = obj.AddComponent<Rigidbody>();
        }
        
        // Configure for high-speed movement
        rb.useGravity = false; // No gravity for consistent movement
        rb.mass = 1f;
        rb.linearDamping = 0f; // No damping for high speed
        rb.angularDamping = 0f;
        rb.freezeRotation = true; // Prevent tumbling
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        
        // Collider
        Collider col = obj.GetComponent<Collider>();
        if (col == null)
        {
            col = obj.AddComponent<BoxCollider>();
        }
        
        // Make sure collider is not a trigger
        col.isTrigger = false;
    }
    
    void ForceMovementOnAllEntities()
    {
        // Find all entities with AggressiveMovement and force them to move
        AggressiveMovement[] movements = FindObjectsOfType<AggressiveMovement>();
        foreach (AggressiveMovement movement in movements)
        {
            if (movement != null)
            {
                movement.ForceMovement();
            }
        }
    }
    
    [ContextMenu("Force Movement Now")]
    public void ForceMovementNow()
    {
        ForceStartAllMovement();
    }
}

/// <summary>
/// Aggressive movement component that ensures continuous, visible movement.
/// </summary>
public class AggressiveMovement : MonoBehaviour
{
    [Header("Aggressive Movement Settings")]
    public float speed = 12f;
    public float directionChangeTime = 2f;
    public bool showDebugInfo = true;
    
    private Rigidbody rb;
    private Vector3 currentDirection;
    private float nextDirectionChange;
    private Vector3 startPosition;
    private float boundaryDistance = 15f;
    
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        startPosition = transform.position;
        
        // Generate initial direction
        GenerateNewDirection();
        nextDirectionChange = Time.time + directionChangeTime;
        
        if (showDebugInfo)
            Debug.Log($"🚀 AggressiveMovement started on {name} with speed {speed}");
    }
    
    void Update()
    {
        // Change direction periodically
        if (Time.time >= nextDirectionChange)
        {
            GenerateNewDirection();
            nextDirectionChange = Time.time + directionChangeTime + Random.Range(-0.5f, 0.5f);
        }
        
        // Check boundaries
        CheckBoundaries();
        
        // Apply movement
        ApplyMovement();
        
        // Debug info
        if (showDebugInfo && Time.frameCount % 120 == 0) // Every 2 seconds
        {
            Debug.Log($"🏃 {name} moving at {rb.linearVelocity.magnitude:F1} speed, pos: {transform.position}");
        }
    }
    
    void GenerateNewDirection()
    {
        // Generate random direction on XZ plane
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        currentDirection = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)).normalized;
        
        if (showDebugInfo)
            Debug.Log($"🔄 {name} new direction: {currentDirection}");
    }
    
    void ApplyMovement()
    {
        if (rb == null) return;
        
        // Apply high-speed movement
        Vector3 targetVelocity = currentDirection * speed;
        targetVelocity.y = 0; // Keep on ground level
        
        // Force the velocity
        rb.linearVelocity = targetVelocity;
        
        // Ensure minimum speed
        if (rb.linearVelocity.magnitude < speed * 0.5f)
        {
            rb.linearVelocity = currentDirection * speed;
        }
    }
    
    void CheckBoundaries()
    {
        Vector3 currentPos = transform.position;
        Vector3 startPos = startPosition;
        
        // Check distance from start position
        float distanceFromStart = Vector3.Distance(new Vector3(currentPos.x, 0, currentPos.z), 
                                                  new Vector3(startPos.x, 0, startPos.z));
        
        if (distanceFromStart > boundaryDistance)
        {
            // Turn towards center
            Vector3 directionToCenter = (startPos - currentPos).normalized;
            currentDirection = new Vector3(directionToCenter.x, 0, directionToCenter.z).normalized;
            
            if (showDebugInfo)
                Debug.Log($"🔄 {name} hit boundary, turning toward center");
        }
    }
    
    void OnCollisionEnter(Collision collision)
    {
        // Bounce off walls and obstacles
        if (collision.gameObject.name.ToLower().Contains("wall") ||
            collision.gameObject.CompareTag("Wall"))
        {
            Vector3 reflection = Vector3.Reflect(currentDirection, collision.contacts[0].normal);
            currentDirection = new Vector3(reflection.x, 0, reflection.z).normalized;
            
            if (showDebugInfo)
                Debug.Log($"🏐 {name} bounced off: {collision.gameObject.name}");
        }
    }
    
    public void ForceMovement()
    {
        if (rb == null) return;
        
        // Force immediate movement
        Vector3 forceDirection = currentDirection * speed;
        forceDirection.y = 0;
        rb.linearVelocity = forceDirection;
        
        if (showDebugInfo)
            Debug.Log($"⚡ {name} movement forced!");
    }
}
