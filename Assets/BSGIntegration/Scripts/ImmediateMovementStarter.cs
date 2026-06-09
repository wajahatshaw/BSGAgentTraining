using UnityEngine;

/// <summary>
/// IMMEDIATE MOVEMENT STARTER - Starts movement immediately without any input dependencies
/// This script will automatically start movement for all entities as soon as the scene loads
/// </summary>
public class ImmediateMovementStarter : MonoBehaviour
{
    [Header("IMMEDIATE MOVEMENT STARTER")]
    [Tooltip("Start movement immediately on scene load")]
    public bool startImmediately = true;
    
    [Tooltip("Movement speed")]
    public float speed = 10f;
    
    [Tooltip("Show debug logs")]
    public bool showDebug = true;
    
    private bool hasStarted = false;
    
    void Start()
    {
        if (startImmediately)
        {
            // Start movement after a short delay to ensure scene is loaded
            Invoke(nameof(StartAllMovement), 2f);
        }
    }
    
    void StartAllMovement()
    {
        if (hasStarted) return;
        
        Debug.Log("🚀 IMMEDIATE MOVEMENT STARTER - Starting movement for all entities!");
        
        // Find all entities
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        int startedCount = 0;
        
        foreach (GameObject obj in allObjects)
        {
            if (IsEntity(obj))
            {
                StartEntityMovement(obj);
                startedCount++;
            }
        }
        
        Debug.Log($"✅ IMMEDIATE MOVEMENT STARTER - Started movement for {startedCount} entities!");
        hasStarted = true;
    }
    
    void StartEntityMovement(GameObject entity)
    {
        string entityName = entity.name;
        Debug.Log($"🚀 Starting movement for {entityName}...");
        
        // Remove ALL existing movement components
        Component[] allComponents = entity.GetComponents<Component>();
        foreach (Component comp in allComponents)
        {
            if (comp != entity.transform && 
                comp.GetType().Name.Contains("Movement"))
            {
                Debug.Log($"🗑️ Removing: {comp.GetType().Name}");
                DestroyImmediate(comp);
            }
        }
        
        // Get or create Rigidbody
        Rigidbody rb = entity.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = entity.AddComponent<Rigidbody>();
            Debug.Log($"📦 Added Rigidbody to {entityName}");
        }
        
        // Configure Rigidbody for movement
        rb.useGravity = false;
        rb.mass = 1f;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.isKinematic = false;
        
        // Ensure Collider exists
        Collider col = entity.GetComponent<Collider>();
        if (col == null)
        {
            col = entity.AddComponent<BoxCollider>();
            Debug.Log($"📦 Added BoxCollider to {entityName}");
        }
        col.isTrigger = false;
        
        // Add immediate movement component
        ImmediateMovement movement = entity.AddComponent<ImmediateMovement>();
        movement.speed = speed;
        movement.showDebug = showDebug;
        
        Debug.Log($"✅ Added ImmediateMovement to {entityName}!");
    }
    
    bool IsEntity(GameObject obj)
    {
        // Skip if it's a UI element, camera, or light
        if (obj.GetComponent<Canvas>() != null || 
            obj.GetComponent<Camera>() != null || 
            obj.GetComponent<Light>() != null ||
            obj.name.Contains("EventSystem") ||
            obj.name.Contains("Canvas") ||
            obj.name.Contains("Camera") ||
            obj.name.Contains("Wall") ||
            obj.name.Contains("Border") ||
            obj.name.Contains("Surface") ||
            obj.name.Contains("Text") ||
            obj.name.Contains("Plan_"))
        {
            return false;
        }
        
        string name = obj.name.ToLower();
        
        // Check for specific entity names
        bool hasEntityName = name.Contains("technician") || 
                           name.Contains("supervisor") || 
                           name.Contains("simple_technician") ||
                           name.Contains("simple_supervisor") ||
                           name.Contains("agent");
        
        // Check for colored objects (likely entities)
        bool hasColor = false;
        Renderer renderer = obj.GetComponent<Renderer>();
        if (renderer != null && renderer.material != null)
        {
            Color objColor = renderer.material.color;
            // Check if it's not white, black, or grey (likely an entity)
            hasColor = !IsGreyColor(objColor);
        }
        
        return hasEntityName || hasColor;
    }
    
    bool IsGreyColor(Color color)
    {
        // Check if color is white, black, or grey
        float greyThreshold = 0.1f;
        return (Mathf.Abs(color.r - color.g) < greyThreshold && 
                Mathf.Abs(color.g - color.b) < greyThreshold && 
                Mathf.Abs(color.r - color.b) < greyThreshold);
    }
    
    [ContextMenu("Start All Movement")]
    public void StartAllMovementNow()
    {
        hasStarted = false;
        StartAllMovement();
    }
}

/// <summary>
/// Immediate movement component - pure physics movement with no input dependencies
/// </summary>
public class ImmediateMovement : MonoBehaviour
{
    public float speed = 10f;
    public bool showDebug = true;
    
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
        
        // FORCE IMMEDIATE MOVEMENT
        Vector3 initialVelocity = direction * speed;
        rb.linearVelocity = initialVelocity;
        
        Debug.Log($"🚀 {name} IMMEDIATE MOVEMENT STARTED - Speed: {speed}, Velocity: {rb.linearVelocity}");
    }
    
    void Update()
    {
        if (rb == null) return;
        
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
        if (showDebug && Time.frameCount % 120 == 0)
        {
            Debug.Log($"🏃 {name} moving at {rb.linearVelocity.magnitude:F1} m/s, position: {transform.position}");
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
        // Apply horizontal movement while keeping Y at 0 (ground level)
        Vector3 velocity = direction * speed;
        velocity.y = 0; // Keep on ground level
        
        rb.linearVelocity = velocity;
        
        // FORCE GROUND LEVEL POSITIONING
        Vector3 pos = transform.position;
        pos.y = 1f; // Force to ground level
        transform.position = pos;
        
        // Ensure we're actually moving
        if (rb.linearVelocity.magnitude < speed * 0.5f)
        {
            Debug.Log($"⚠️ {name} not moving properly, forcing movement");
            rb.linearVelocity = direction * speed;
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
        }
    }
    
    void CheckBoundaries()
    {
        Vector3 pos = transform.position;
        float distance = Vector3.Distance(new Vector3(pos.x, 0, pos.z), new Vector3(startPosition.x, 0, startPosition.z));
        
        if (distance > 15f)
        {
            // Turn back toward center
            Vector3 toCenter = (startPosition - pos).normalized;
            direction = new Vector3(toCenter.x, 0, toCenter.z).normalized;
            
            Debug.Log($"🔄 {name} turning back to center");
        }
        
        // Keep within reasonable bounds
        pos.x = Mathf.Clamp(pos.x, -20f, 20f);
        pos.z = Mathf.Clamp(pos.z, -20f, 20f);
        pos.y = 1f; // Force ground level
        transform.position = pos;
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
    
    [ContextMenu("Force New Direction")]
    public void ForceNewDirection()
    {
        SetRandomDirection();
        if (rb != null)
        {
            rb.linearVelocity = direction * speed;
            Debug.Log($"🚀 {name} forced new direction: {direction}");
        }
    }
}