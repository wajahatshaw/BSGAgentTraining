using UnityEngine;

/// <summary>
/// SINGLE MOVEMENT CONTROLLER - One script to rule them all
/// This script will handle movement without conflicts from other scripts
/// </summary>
public class SingleMovementController : MonoBehaviour
{
    [Header("SINGLE MOVEMENT CONTROLLER")]
    [Tooltip("Start movement immediately")]
    public bool startImmediately = true;
    
    [Tooltip("Movement speed")]
    public float speed = 8f;
    
    [Tooltip("Show debug logs")]
    public bool showDebug = true;
    
    private bool hasStarted = false;
    
    void Start()
    {
        if (startImmediately)
        {
            // Start movement after a short delay
            Invoke(nameof(StartMovement), 2f);
        }
    }
    
    void StartMovement()
    {
        if (hasStarted) return;
        
        Debug.Log("🎯 SINGLE MOVEMENT CONTROLLER - Starting movement!");
        
        // Find and move specific entities
        MoveEntityByName("SIMPLE_Technician_01");
        MoveEntityByName("SIMPLE_Technician_02");
        MoveEntityByName("SIMPLE_Supervisor_01");
        MoveEntityByName("SIMPLE_Supervisor_02");
        
        Debug.Log("✅ SINGLE MOVEMENT CONTROLLER - Movement started!");
        hasStarted = true;
    }
    
    void MoveEntityByName(string entityName)
    {
        GameObject entity = GameObject.Find(entityName);
        if (entity == null)
        {
            Debug.LogWarning($"⚠️ Entity {entityName} not found!");
            return;
        }
        
        Debug.Log($"🚀 Moving {entityName}...");
        
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
        
        // Add simple movement component
        SimpleEntityMovement movement = entity.AddComponent<SimpleEntityMovement>();
        movement.speed = speed;
        movement.showDebug = showDebug;
        
        Debug.Log($"✅ Added SimpleEntityMovement to {entityName}!");
    }
    
    [ContextMenu("Start Movement Now")]
    public void StartMovementNow()
    {
        hasStarted = false;
        StartMovement();
    }
}

/// <summary>
/// Simple entity movement - guaranteed to work
/// </summary>
public class SimpleEntityMovement : MonoBehaviour
{
    public float speed = 8f;
    public bool showDebug = true;
    
    private Rigidbody rb;
    private Vector3 direction;
    private float nextDirectionChange;
    private Vector3 startPosition;
    
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        startPosition = transform.position;
        
        // DISABLED: Random movement removed - agents stay at initial positions
        // Stop any movement immediately
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        
        Debug.Log($"🚫 {name} SIMPLE MOVEMENT DISABLED - agent will stay at initial position");
        
        // Original movement code commented out:
        // SetRandomDirection();
        // nextDirectionChange = Time.time + 2f;
        // Vector3 initialVelocity = direction * speed;
        // rb.linearVelocity = initialVelocity;
    }
    
    void Update()
    {
        // DISABLED: Random movement removed - agents stay at initial positions
        // Stop any movement that might have been applied
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        
        // Original movement code commented out:
        // if (rb == null) return;
        // if (Time.time >= nextDirectionChange)
        // {
        //     SetRandomDirection();
        // }
        // ApplyMovement();
        // CheckBoundaries();
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
