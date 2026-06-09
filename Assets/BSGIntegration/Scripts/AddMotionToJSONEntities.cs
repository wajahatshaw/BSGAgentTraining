using UnityEngine;

/// <summary>
/// Adds continuous random motion to existing technicians created from basicUi.json.
/// This script finds the JSON-created entities and adds movement without creating new ones.
/// </summary>
public class AddMotionToJSONEntities : MonoBehaviour
{
    [Header("ADD MOTION TO JSON ENTITIES")]
    [Tooltip("Start adding motion immediately when scene starts")]
    public bool startImmediately = true;
    
    [Tooltip("Movement speed for technicians")]
    public float technicianSpeed = 3f;
    
    [Tooltip("Movement speed for supervisors")]
    public float supervisorSpeed = 2f;
    
    [Tooltip("How often to change direction (seconds)")]
    public float directionChangeInterval = 2f;
    
    [Tooltip("Maximum distance to move from start position")]
    public float maxMoveDistance = 4f;
    
    [Tooltip("Show debug information")]
    public bool showDebug = true;
    
    void Start()
    {
        if (startImmediately)
        {
            // Wait a bit for JSON scene to be fully loaded
            Invoke(nameof(AddMotionToExistingEntities), 1f);
        }
    }
    
    void AddMotionToExistingEntities()
    {
        if (showDebug)
            Debug.Log("🎯 ADDING MOTION TO JSON ENTITIES - Finding existing technicians and supervisors!");
        
        // Find all GameObjects in the scene
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        int entitiesWithMotion = 0;
        
        foreach (GameObject obj in allObjects)
        {
            if (IsJSONCreatedEntity(obj))
            {
                AddContinuousMotion(obj);
                entitiesWithMotion++;
            }
        }
        
        if (showDebug)
            Debug.Log($"🎉 Added continuous motion to {entitiesWithMotion} JSON entities!");
        
        if (entitiesWithMotion == 0)
        {
            Debug.LogWarning("⚠️ No JSON entities found! Make sure the JSON scene has been generated first.");
        }
    }
    
    bool IsJSONCreatedEntity(GameObject obj)
    {
        string name = obj.name.ToLower();
        
        // Check for agent names from JSON (agent_technician_A, agent_supervisor_B, agent_inspector_C)
        return name.Contains("agent_technician") || 
               name.Contains("agent_supervisor") || 
               name.Contains("agent_inspector") ||
               name.Contains("technician") ||
               name.Contains("supervisor") ||
               name.Contains("alex rodriguez") ||
               name.Contains("maria santos") ||
               name.Contains("david kim");
    }
    
    void AddContinuousMotion(GameObject entity)
    {
        if (showDebug)
            Debug.Log($"🚀 Adding continuous motion to {entity.name}");
        
        // Add our motion component
        JSONEntityMotion motion = entity.GetComponent<JSONEntityMotion>();
        if (motion == null)
        {
            motion = entity.AddComponent<JSONEntityMotion>();
        }
        
        // Configure motion based on entity type
        bool isTechnician = entity.name.ToLower().Contains("technician");
        motion.speed = isTechnician ? technicianSpeed : supervisorSpeed;
        motion.directionChangeInterval = directionChangeInterval;
        motion.maxMoveDistance = maxMoveDistance;
        motion.showDebug = showDebug;
        
        // Ensure the entity has necessary components for motion
        EnsureMotionComponents(entity);
        
        if (showDebug)
            Debug.Log($"✅ Added motion to {entity.name} with speed {motion.speed}");
    }
    
    void EnsureMotionComponents(GameObject entity)
    {
        // Make sure entity has a Rigidbody for smooth motion
        Rigidbody rb = entity.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = entity.AddComponent<Rigidbody>();
        }
        
        // Configure Rigidbody for smooth motion
        rb.useGravity = false; // Keep entities floating at their level
        rb.mass = 1f;
        rb.linearDamping = 2f; // Some damping for smooth motion
        rb.angularDamping = 5f;
        rb.freezeRotation = true; // Prevent tumbling
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        
        // Ensure entity has a collider
        Collider col = entity.GetComponent<Collider>();
        if (col == null)
        {
            col = entity.AddComponent<CapsuleCollider>();
            CapsuleCollider capsule = col as CapsuleCollider;
            capsule.height = 2f;
            capsule.radius = 0.5f;
        }
    }
    
    [ContextMenu("Add Motion to JSON Entities Now")]
    public void AddMotionToJSONEntitiesNow()
    {
        AddMotionToExistingEntities();
    }
    
    void Update()
    {
        // Press J to add motion to JSON entities
        if (Input.GetKeyDown(KeyCode.J))
        {
            if (showDebug)
                Debug.Log("🎮 J key pressed - Adding motion to JSON entities!");
            AddMotionToExistingEntities();
        }
    }
}

/// <summary>
/// Motion component for JSON-created entities.
/// Provides continuous random movement while staying near the original position.
/// </summary>
public class JSONEntityMotion : MonoBehaviour
{
    [Header("JSON Entity Motion")]
    public float speed = 3f;
    public float directionChangeInterval = 2f;
    public float maxMoveDistance = 4f;
    public bool showDebug = true;
    
    private Rigidbody rb;
    private Vector3 startPosition;
    private Vector3 currentDirection;
    private float nextDirectionChange;
    private bool isMoving = false;
    
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        startPosition = transform.position;
        
        // Generate initial random direction
        GenerateRandomDirection();
        nextDirectionChange = Time.time + directionChangeInterval;
        isMoving = true;
        
        if (showDebug)
            Debug.Log($"🚀 JSONEntityMotion started on {name} at position {startPosition}");
    }
    
    void Update()
    {
        if (!isMoving || rb == null) return;
        
        // Check if it's time to change direction
        if (Time.time >= nextDirectionChange)
        {
            GenerateRandomDirection();
            nextDirectionChange = Time.time + directionChangeInterval + Random.Range(-0.5f, 0.5f);
        }
        
        // Apply movement
        ApplyMovement();
        
        // Check boundaries
        CheckBoundaries();
        
        // Debug info
        if (showDebug && Time.frameCount % 180 == 0) // Every 3 seconds
        {
            Debug.Log($"🏃 {name} moving at speed {rb.linearVelocity.magnitude:F1}, position: {transform.position}");
        }
    }
    
    void GenerateRandomDirection()
    {
        // Generate random direction on XZ plane (horizontal movement)
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        currentDirection = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)).normalized;
        
        if (showDebug)
            Debug.Log($"🔄 {name} new direction: {currentDirection}");
    }
    
    void ApplyMovement()
    {
        // Apply horizontal movement while preserving Y position
        Vector3 targetVelocity = currentDirection * speed;
        targetVelocity.y = 0; // No vertical movement
        
        rb.linearVelocity = targetVelocity;
    }
    
    void CheckBoundaries()
    {
        Vector3 currentPos = transform.position;
        
        // Calculate distance from start position (only XZ plane)
        float distanceFromStart = Vector3.Distance(
            new Vector3(currentPos.x, 0, currentPos.z), 
            new Vector3(startPosition.x, 0, startPosition.z)
        );
        
        // If too far from start position, turn back
        if (distanceFromStart > maxMoveDistance)
        {
            Vector3 directionToStart = (startPosition - currentPos).normalized;
            currentDirection = new Vector3(directionToStart.x, 0, directionToStart.z).normalized;
            
            if (showDebug)
                Debug.Log($"🔄 {name} turning back to start position");
        }
    }
    
    void OnCollisionEnter(Collision collision)
    {
        // Bounce off walls and obstacles
        if (collision.gameObject.name.ToLower().Contains("wall") ||
            collision.gameObject.name.ToLower().Contains("border"))
        {
            Vector3 reflection = Vector3.Reflect(currentDirection, collision.contacts[0].normal);
            currentDirection = new Vector3(reflection.x, 0, reflection.z).normalized;
            
            if (showDebug)
                Debug.Log($"🏐 {name} bounced off {collision.gameObject.name}");
        }
    }
    
    /// <summary>
    /// Start or resume movement
    /// </summary>
    public void StartMovement()
    {
        isMoving = true;
        GenerateRandomDirection();
        
        if (showDebug)
            Debug.Log($"▶️ {name} movement started");
    }
    
    /// <summary>
    /// Stop movement
    /// </summary>
    public void StopMovement()
    {
        isMoving = false;
        if (rb != null)
            rb.linearVelocity = Vector3.zero;
        
        if (showDebug)
            Debug.Log($"⏹️ {name} movement stopped");
    }
    
    /// <summary>
    /// Force immediate direction change
    /// </summary>
    public void ForceDirectionChange()
    {
        GenerateRandomDirection();
        nextDirectionChange = Time.time + directionChangeInterval;
    }
}
