using UnityEngine;

/// <summary>
/// NON-CONFLICTING MOVEMENT FIX - Works WITH your existing scripts
/// This script adds movement without disabling your scene generation or UI
/// </summary>
public class NonConflictingMovementFix : MonoBehaviour
{
    [Header("NON-CONFLICTING MOVEMENT FIX")]
    [Tooltip("Start fixing immediately")]
    public bool fixNow = true;
    
    [Tooltip("Movement speed")]
    public float speed = 15f;
    
    [Tooltip("Check every few seconds for new entities")]
    public bool continuousCheck = true;
    
    private float lastCheckTime;
    
    void Start()
    {
        if (fixNow)
        {
            Invoke(nameof(AddMovementToExistingEntities), 2f); // Wait for scene generation
        }
    }
    
    void Update()
    {
        // Continuously check for new entities if enabled
        if (continuousCheck && Time.time - lastCheckTime > 3f)
        {
            AddMovementToExistingEntities();
            lastCheckTime = Time.time;
        }
        
        // Note: Input disabled due to Input System conflict
        // Use context menu or inspector button instead
    }
    
    void AddMovementToExistingEntities()
    {
        Debug.Log("🔧 Adding movement to existing entities (non-conflicting)...");
        
        // Find all entities
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        int processedCount = 0;
        int totalObjects = allObjects.Length;
        
        Debug.Log($"🔍 Found {totalObjects} total objects in scene");
        
        foreach (GameObject obj in allObjects)
        {
            if (IsEntity(obj))
            {
                Debug.Log($"🎯 Found entity: {obj.name} (Renderer: {obj.GetComponent<Renderer>() != null})");
                AddMovementToEntity(obj);
                processedCount++;
            }
        }
        
        Debug.Log($"✅ Added movement to {processedCount} entities out of {totalObjects} total objects!");
    }
    
    void AddMovementToEntity(GameObject entity)
    {
        string entityName = entity.name;
        
        // Check if entity already has our movement component
        NonConflictingMovement existingMovement = entity.GetComponent<NonConflictingMovement>();
        if (existingMovement != null)
        {
            // Already has our movement, just update speed
            existingMovement.speed = speed;
            return;
        }
        
        Debug.Log($"🔧 Adding non-conflicting movement to {entityName}...");
        
        // Ensure Rigidbody exists
        Rigidbody rb = entity.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = entity.AddComponent<Rigidbody>();
            Debug.Log($"📦 Added Rigidbody to {entityName}");
        }
        
        // Configure Rigidbody for movement (compatible with other systems)
        rb.useGravity = false; // No gravity conflicts
        rb.mass = 1f;
        rb.linearDamping = 0.1f; // Small damping for smoother movement
        rb.angularDamping = 0.1f; // Small damping for smoother rotation
        rb.freezeRotation = false; // Allow rotation for more natural movement
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        
        // Ensure the rigidbody is not kinematic
        rb.isKinematic = false;
        
        // Ensure Collider exists
        Collider col = entity.GetComponent<Collider>();
        if (col == null)
        {
            col = entity.AddComponent<BoxCollider>();
            Debug.Log($"📦 Added BoxCollider to {entityName}");
        }
        col.isTrigger = false;
        
        // Add our non-conflicting movement component
        NonConflictingMovement movement = entity.AddComponent<NonConflictingMovement>();
        movement.speed = speed;
        
        Debug.Log($"✅ Added NonConflictingMovement to {entityName}!");
    }
    
    bool IsEntity(GameObject obj)
    {
        // Skip if it's a UI element, camera, or light
        if (obj.GetComponent<Canvas>() != null || 
            obj.GetComponent<Camera>() != null || 
            obj.GetComponent<Light>() != null ||
            obj.name.Contains("EventSystem") ||
            obj.name.Contains("Canvas") ||
            obj.name.Contains("Camera"))
        {
            return false;
        }
        
        string name = obj.name.ToLower();
        
        // Check for specific entity names
        bool hasEntityName = name.Contains("technician") || 
                           name.Contains("supervisor") || 
                           name.Contains("simple_technician") ||
                           name.Contains("simple_supervisor") ||
                           name.Contains("agent") ||
                           name.Contains("sphere") ||
                           name.Contains("cube") ||
                           name.Contains("capsule") ||
                           name.Contains("cylinder");
        
        // Check for colored objects (likely entities)
        bool hasColor = false;
        Renderer renderer = obj.GetComponent<Renderer>();
        if (renderer != null && renderer.material != null)
        {
            Color objColor = renderer.material.color;
            // Check if it's not white, black, or grey (likely an entity)
            hasColor = !IsGreyColor(objColor);
        }
        
        // Check for primitive shapes that could be entities
        bool isPrimitive = obj.name.Contains("Primitive") || 
                          obj.GetComponent<MeshFilter>() != null;
        
        return hasEntityName || hasColor || isPrimitive;
    }
    
    bool IsGreyColor(Color color)
    {
        // Check if color is white, black, or grey
        float greyThreshold = 0.1f;
        return (Mathf.Abs(color.r - color.g) < greyThreshold && 
                Mathf.Abs(color.g - color.b) < greyThreshold && 
                Mathf.Abs(color.r - color.b) < greyThreshold);
    }
    
    [ContextMenu("Add Movement to Entities")]
    public void AddMovementNow()
    {
        AddMovementToExistingEntities();
    }
    
    void ForceAllEntitiesToMove()
    {
        Debug.Log("🚀 FORCING all entities to start moving...");
        
        NonConflictingMovement[] allMovements = FindObjectsOfType<NonConflictingMovement>();
        Debug.Log($"🔍 Found {allMovements.Length} entities with movement components");
        
        foreach (NonConflictingMovement movement in allMovements)
        {
            if (movement != null)
            {
                // Force them to start moving
                Rigidbody rb = movement.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    // Set a random direction and force movement
                    Vector3 randomDirection = new Vector3(
                        Random.Range(-1f, 1f), 
                        0, 
                        Random.Range(-1f, 1f)
                    ).normalized;
                    
                    rb.linearVelocity = randomDirection * movement.speed;
                    Debug.Log($"🚀 Forced {movement.name} to move with velocity: {rb.linearVelocity}");
                }
            }
        }
        
        Debug.Log($"✅ Forced {allMovements.Length} entities to start moving!");
    }
}

/// <summary>
/// Non-conflicting movement component that works alongside other systems
/// </summary>
public class NonConflictingMovement : MonoBehaviour
{
    public float speed = 15f;
    
    private Rigidbody rb;
    private Vector3 direction;
    private float nextDirectionChange;
    private Vector3 startPosition;
    
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        startPosition = transform.position;
        
        // Set initial direction
        SetRandomDirection();
        nextDirectionChange = Time.time + 2f;
        
        // Apply immediate movement
        StartMoving();
        
        Debug.Log($"🚀 {name} non-conflicting movement started with speed {speed}");
    }
    
    void StartMoving()
    {
        if (rb == null) return;
        
        // Force immediate movement
        Vector3 initialVelocity = direction * speed;
        initialVelocity.y = 0; // Keep on ground level
        rb.linearVelocity = initialVelocity;
        
        Debug.Log($"🏃 {name} started moving with velocity: {rb.linearVelocity}");
    }
    
    void Update()
    {
        // Change direction periodically
        if (Time.time >= nextDirectionChange)
        {
            SetRandomDirection();
        }
        
        // Apply movement
        ApplyMovement();
        
        // Check boundaries
        CheckBoundaries();
        
        // Debug every 2 seconds
        if (Time.frameCount % 120 == 0)
        {
            Debug.Log($"🏃 {name} moving at {rb.linearVelocity.magnitude:F1} speed");
        }
    }
    
    void SetRandomDirection()
    {
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)).normalized;
        nextDirectionChange = Time.time + 2f + Random.Range(-0.5f, 0.5f);
        
        Debug.Log($"🔄 {name} new direction: {direction}");
    }
    
    void ApplyMovement()
    {
        if (rb == null) return;
        
        // Apply horizontal movement while keeping Y at 0 (ground level)
        Vector3 velocity = direction * speed;
        velocity.y = 0; // Keep on ground level
        
        rb.linearVelocity = velocity;
        
        // Ensure we're actually moving - if not, force movement
        if (rb.linearVelocity.magnitude < speed * 0.5f)
        {
            Debug.Log($"⚠️ {name} not moving properly, forcing movement");
            rb.linearVelocity = direction * speed;
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
        }
        
        // Debug movement every 3 seconds
        if (Time.frameCount % 180 == 0)
        {
            Debug.Log($"🏃 {name} velocity: {rb.linearVelocity.magnitude:F1} m/s, direction: {direction}");
        }
    }
    
    void CheckBoundaries()
    {
        Vector3 pos = transform.position;
        float distance = Vector3.Distance(new Vector3(pos.x, 0, pos.z), new Vector3(startPosition.x, 0, startPosition.z));
        
        if (distance > 12f)
        {
            // Turn back toward center
            Vector3 toCenter = (startPosition - pos).normalized;
            direction = new Vector3(toCenter.x, 0, toCenter.z).normalized;
            
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
            
            Debug.Log($"🏐 {name} bounced off wall");
        }
    }
}
