using UnityEngine;

/// <summary>
/// Creates a completely NEW entity with guaranteed random motion.
/// This will work 100% because we create everything from scratch.
/// </summary>
public class CreateNewMovingEntity : MonoBehaviour
{
    [Header("CREATE NEW MOVING ENTITY")]
    [Tooltip("Create moving entity immediately")]
    public bool createOnStart = true;
    
    [Tooltip("Number of moving entities to create")]
    public int numberOfEntities = 3;
    
    [Tooltip("Movement speed")]
    public float movementSpeed = 6f;
    
    [Tooltip("Entity color")]
    public Color entityColor = Color.blue;
    
    [Tooltip("Show debug information")]
    public bool showDebug = true;
    
    void Start()
    {
        if (createOnStart)
        {
            Invoke(nameof(CreateMovingEntities), 0.5f);
        }
    }
    
    void CreateMovingEntities()
    {
        if (showDebug)
            Debug.Log("🚀 CREATING NEW MOVING ENTITIES!");
        
        for (int i = 0; i < numberOfEntities; i++)
        {
            CreateSingleMovingEntity(i);
        }
        
        if (showDebug)
            Debug.Log($"✅ Created {numberOfEntities} new moving entities!");
    }
    
    void CreateSingleMovingEntity(int index)
    {
        // Create new GameObject
        GameObject newEntity = GameObject.CreatePrimitive(PrimitiveType.Cube);
        newEntity.name = $"NewMovingEntity_{index + 1}";
        
        // Position on plane surface (Y = 1 to be above the green plane)
        Vector3 startPosition = new Vector3(
            Random.Range(-8f, 8f),  // Random X position
            1f,                     // Above the plane
            Random.Range(-8f, 8f)   // Random Z position
        );
        newEntity.transform.position = startPosition;
        
        // Make it visible with color
        Renderer renderer = newEntity.GetComponent<Renderer>();
        Material material = new Material(Shader.Find("Standard"));
        material.color = entityColor;
        renderer.material = material;
        
        // Add Rigidbody for physics
        Rigidbody rb = newEntity.AddComponent<Rigidbody>();
        rb.useGravity = false; // Don't fall through plane
        rb.mass = 1f;
        rb.linearDamping = 0.5f;
        rb.angularDamping = 5f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        
        // Add our guaranteed movement component
        GuaranteedEntityMovement movement = newEntity.AddComponent<GuaranteedEntityMovement>();
        movement.speed = movementSpeed;
        movement.showDebug = showDebug;
        movement.startPosition = startPosition;
        
        if (showDebug)
            Debug.Log($"✅ Created {newEntity.name} at position {startPosition}");
    }
    
    [ContextMenu("Create Moving Entities Now")]
    public void CreateMovingEntitiesNow()
    {
        CreateMovingEntities();
    }
    
    void Update()
    {
        // No input needed - entities are created automatically on Start
    }
}

/// <summary>
/// Guaranteed movement component for newly created entities.
/// This WILL work because it's on a fresh entity with no conflicts.
/// </summary>
public class GuaranteedEntityMovement : MonoBehaviour
{
    [Header("Guaranteed Entity Movement")]
    public float speed = 6f;
    public bool showDebug = true;
    public Vector3 startPosition;
    
    private Rigidbody rb;
    private Vector3 currentDirection;
    private float nextDirectionChange;
    private float maxDistance = 10f;
    
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        startPosition = transform.position;
        
        // Set initial random direction
        SetRandomDirection();
        nextDirectionChange = Time.time + 2f;
        
        if (showDebug)
            Debug.Log($"🚀 GuaranteedEntityMovement started on {name}");
        
        // Start moving immediately
        StartMoving();
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
        
        // Debug info
        if (showDebug && Time.frameCount % 120 == 0) // Every 2 seconds
        {
            Debug.Log($"🏃 {name} moving at speed {rb.linearVelocity.magnitude:F1}, position: {transform.position}");
        }
    }
    
    void SetRandomDirection()
    {
        // Random direction on XZ plane
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        currentDirection = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)).normalized;
        
        if (showDebug)
            Debug.Log($"🔄 {name} new direction: {currentDirection}");
    }
    
    void ApplyMovement()
    {
        if (rb == null) return;
        
        // Apply horizontal movement
        Vector3 targetVelocity = currentDirection * speed;
        targetVelocity.y = 0; // No vertical movement
        
        rb.linearVelocity = targetVelocity;
        
        // Ensure we're actually moving
        if (rb.linearVelocity.magnitude < speed * 0.8f)
        {
            rb.linearVelocity = currentDirection * speed;
        }
        
        // FORCE GROUND LEVEL POSITIONING
        Vector3 pos = transform.position;
        pos.y = 1f; // Force to ground level
        transform.position = pos;
    }
    
    void CheckBoundaries()
    {
        Vector3 currentPos = transform.position;
        
        // Check distance from start position
        float distanceFromStart = Vector3.Distance(
            new Vector3(currentPos.x, 0, currentPos.z), 
            new Vector3(startPosition.x, 0, startPosition.z)
        );
        
        if (distanceFromStart > maxDistance)
        {
            // Turn back toward start
            Vector3 directionToStart = (startPosition - currentPos).normalized;
            currentDirection = new Vector3(directionToStart.x, 0, directionToStart.z).normalized;
            
            if (showDebug)
                Debug.Log($"🔄 {name} turning back to start position");
        }
        
        // Keep on plane surface and within boundaries
        Vector3 correctedPos = currentPos;
        correctedPos.y = 1f; // Force to ground level
        
        // Keep within ground plane boundaries
        correctedPos.x = Mathf.Clamp(correctedPos.x, -10f, 10f); // X boundaries
        correctedPos.z = Mathf.Clamp(correctedPos.z, -10f, 10f); // Z boundaries
        
        transform.position = correctedPos;
    }
    
    void StartMoving()
    {
        // Give initial velocity to start moving immediately
        rb.linearVelocity = currentDirection * (speed * 0.02f); // Start at 2% speed
        
        if (showDebug)
            Debug.Log($"▶️ {name} started moving with velocity: {rb.linearVelocity}");
    }
    
    void OnCollisionEnter(Collision collision)
    {
        // Bounce off walls
        if (collision.gameObject.name.ToLower().Contains("wall") ||
            collision.gameObject.name.ToLower().Contains("border"))
        {
            Vector3 reflection = Vector3.Reflect(currentDirection, collision.contacts[0].normal);
            currentDirection = new Vector3(reflection.x, 0, reflection.z).normalized;
            
            if (showDebug)
                Debug.Log($"🏐 {name} bounced off {collision.gameObject.name}");
        }
    }
}
