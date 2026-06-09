using UnityEngine;

/// <summary>
/// GUARANTEED WORKING MOVEMENT - No input dependencies, pure physics movement
/// This will definitely make entities move regardless of any other issues
/// </summary>
public class GuaranteedWorkingMovement : MonoBehaviour
{
    [Header("GUARANTEED WORKING MOVEMENT")]
    [Tooltip("Start movement immediately")]
    public bool startImmediately = true;
    
    [Tooltip("Movement speed")]
    public float speed = 10f;
    
    [Tooltip("Show debug logs")]
    public bool showDebug = true;
    
    private Rigidbody rb;
    private Vector3 direction;
    private float nextDirectionChange;
    private Vector3 startPosition;
    
    void Start()
    {
        if (startImmediately)
        {
            StartMovement();
        }
    }
    
    void StartMovement()
    {
        Debug.Log($"🚀 GUARANTEED MOVEMENT STARTING on {name}");
        
        // Get or create Rigidbody
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            Debug.Log($"📦 Added Rigidbody to {name}");
        }
        
        // Configure Rigidbody for guaranteed movement
        rb.useGravity = false;
        rb.mass = 1f;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.isKinematic = false;
        
        // Ensure Collider exists
        Collider col = GetComponent<Collider>();
        if (col == null)
        {
            col = gameObject.AddComponent<BoxCollider>();
            Debug.Log($"📦 Added BoxCollider to {name}");
        }
        col.isTrigger = false;
        
        // Set initial position and direction
        startPosition = transform.position;
        SetRandomDirection();
        nextDirectionChange = Time.time + 2f;
        
        // FORCE IMMEDIATE MOVEMENT
        Vector3 initialVelocity = direction * speed;
        rb.linearVelocity = initialVelocity;
        
        Debug.Log($"✅ {name} GUARANTEED MOVEMENT STARTED - Speed: {speed}, Velocity: {rb.linearVelocity}");
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
    
    [ContextMenu("Force Start Movement")]
    public void ForceStartMovement()
    {
        StartMovement();
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
