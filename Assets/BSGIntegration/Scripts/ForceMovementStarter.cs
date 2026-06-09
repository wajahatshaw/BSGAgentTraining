using UnityEngine;

public class ForceMovementStarter : MonoBehaviour
{
    [Header("FORCE MOVEMENT STARTER")]
    [Tooltip("Click this to force start movement on all supervisors")]
    public bool startMovementNow = false;
    
    [Header("Movement Settings")]
    public float supervisorSpeed = 6f; // Increased from 0.06f
    public float technicianSpeed = 8f; // Increased from 0.08f
    public bool enableDebugLogs = true;
    
    [Header("Status")]
    public bool movementStarted = false;
    
    void Start()
    {
        if (enableDebugLogs)
            Debug.Log("🎯 ForceMovementStarter ready! Set 'startMovementNow' to true to force movement.");
    }
    
    void Update()
    {
        if (startMovementNow && !movementStarted)
        {
            ForceStartAllMovement();
            startMovementNow = false;
            movementStarted = true;
        }
        
        // Note: Input disabled due to Input System conflict
        // Use context menu or inspector button instead
    }
    
    [ContextMenu("Force Start Movement")]
    public void ForceStartAllMovement()
    {
        Debug.Log("🚀 FORCE STARTING MOVEMENT FOR ALL ENTITIES!");
        
        // Find all supervisors and technicians
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        int entitiesFixed = 0;
        
        foreach (GameObject obj in allObjects)
        {
            if (obj.name.Contains("Supervisor") || obj.name.Contains("Technician"))
            {
                bool isTechnician = obj.name.Contains("Technician");
                ForceAddMovementToEntity(obj, isTechnician);
                entitiesFixed++;
            }
        }
        
        Debug.Log($"✅ Movement forced on {entitiesFixed} entities!");
        movementStarted = true;
    }
    
    void RestartAllMovement()
    {
        Debug.Log("🔄 RESTARTING ALL MOVEMENT!");
        
        SimpleMovement[] allMovements = FindObjectsOfType<SimpleMovement>();
        Debug.Log($"🔍 Found {allMovements.Length} entities with SimpleMovement");
        
        foreach (SimpleMovement movement in allMovements)
        {
            if (movement != null)
            {
                Rigidbody rb = movement.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    // Force new random direction and velocity
                    Vector3 randomDirection = new Vector3(
                        Random.Range(-1f, 1f), 
                        0, 
                        Random.Range(-1f, 1f)
                    ).normalized;
                    
                    rb.linearVelocity = randomDirection * movement.speed;
                    Debug.Log($"🚀 Restarted {movement.name} with velocity: {rb.linearVelocity}");
                }
            }
        }
        
        Debug.Log($"✅ Restarted {allMovements.Length} entities!");
    }
    
    void ForceAddMovementToEntity(GameObject entity, bool isTechnician)
    {
        string entityName = entity.name;
        Debug.Log($"🚀 Adding movement to {entityName}");
        
        // Ensure rigidbody with enhanced settings
        Rigidbody rb = entity.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = entity.AddComponent<Rigidbody>();
            Debug.Log($"📦 Added Rigidbody to {entityName}");
        }
        
        // Configure rigidbody for movement
        rb.useGravity = false; // No gravity for better control
        rb.mass = 1f;
        rb.linearDamping = 0.1f; // Small damping for smooth movement
        rb.angularDamping = 0.1f; // Small damping for smooth rotation
        rb.freezeRotation = false; // Allow rotation for natural movement
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.isKinematic = false; // Ensure non-kinematic
        
        // Ensure collider exists
        Collider col = entity.GetComponent<Collider>();
        if (col == null)
        {
            col = entity.AddComponent<BoxCollider>();
            Debug.Log($"📦 Added BoxCollider to {entityName}");
        }
        
        // Remove existing movement components
        SimpleMovement[] existingMovements = entity.GetComponents<SimpleMovement>();
        for (int i = 0; i < existingMovements.Length; i++)
        {
            DestroyImmediate(existingMovements[i]);
        }
        
        // Add fresh movement component
        SimpleMovement movement = entity.AddComponent<SimpleMovement>();
        movement.speed = isTechnician ? technicianSpeed : supervisorSpeed;
        movement.isTechnician = isTechnician;
        
        // Apply immediate movement
        Vector3 randomDirection = new Vector3(
            Random.Range(-1f, 1f),
            0,
            Random.Range(-1f, 1f)
        ).normalized;
        
        rb.linearVelocity = randomDirection * movement.speed; // Use full speed
        
        Debug.Log($"✅ Movement added to {entityName} - Speed: {movement.speed}, Initial velocity: {rb.linearVelocity}");
    }
}

// Simple movement component for entities
public class SimpleMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    public float speed = 8f; // Increased from 0.08f
    public bool isTechnician = false;
    
    [Header("Random Movement")]
    public float directionChangeTime = 2f;
    public float randomIntensity = 0.05f;
    
    private Rigidbody rb;
    private Vector3 currentDirection;
    private float nextDirectionChange;
    
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        
        if (rb == null)
        {
            Debug.LogError($"❌ No Rigidbody on {name}!");
            return;
        }
        
        // Set random movement parameters
        directionChangeTime = isTechnician ? Random.Range(1f, 2f) : Random.Range(1.5f, 2.5f);
        randomIntensity = isTechnician ? 0.05f : 0.04f; // Much lower random intensity
        
        GenerateNewDirection();
        nextDirectionChange = Time.time + directionChangeTime;
        
        Debug.Log($"🚀 SimpleMovement started on {name} - Speed: {speed}");
    }
    
    void Update()
    {
        if (rb == null) return;
        
        // Change direction periodically
        if (Time.time >= nextDirectionChange)
        {
            GenerateNewDirection();
            nextDirectionChange = Time.time + directionChangeTime + Random.Range(-0.5f, 0.5f);
        }
        
        // Apply movement
        ApplyMovement();
        
        // Check boundaries and bounce
        CheckBounds();
        
        // Debug every 2 seconds
        if (Time.frameCount % 120 == 0)
        {
            Debug.Log($"🏃 {name} moving at velocity: {rb.linearVelocity.magnitude:F1}, position: {transform.position}");
        }
    }
    
    void GenerateNewDirection()
    {
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        currentDirection = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
        
        // Add some random variation
        Vector3 randomVariation = new Vector3(
            Random.Range(-randomIntensity, randomIntensity),
            0,
            Random.Range(-randomIntensity, randomIntensity)
        ) * 0.3f;
        
        currentDirection = (currentDirection + randomVariation).normalized;
    }
    
    void ApplyMovement()
    {
        // Apply horizontal movement while keeping Y at 0 (ground level)
        Vector3 targetVelocity = currentDirection * speed;
        targetVelocity.y = 0; // Keep on ground level
        
        rb.linearVelocity = targetVelocity;
        
        // FORCE GROUND LEVEL POSITIONING
        Vector3 pos = transform.position;
        pos.y = 1f; // Force to ground level
        transform.position = pos;
        
        // Debug movement every 3 seconds
        if (Time.frameCount % 180 == 0)
        {
            Debug.Log($"🏃 {name} moving at {rb.linearVelocity.magnitude:F1} m/s, position: {transform.position}");
        }
    }
    
    void CheckBounds()
    {
        Vector3 pos = transform.position;
        Vector3 vel = rb.linearVelocity;
        
        // FORCE GROUND LEVEL AND BOUNDARY CONSTRAINTS
        pos.y = 1f; // Force to ground level
        
        // Keep within ground plane boundaries
        pos.x = Mathf.Clamp(pos.x, -10f, 10f); // X boundaries
        pos.z = Mathf.Clamp(pos.z, -10f, 10f); // Z boundaries
        
        transform.position = pos;
        bool bounced = false;
        
        // X boundaries (bounce off walls)
        if (pos.x < -11f && vel.x < 0)
        {
            vel.x = Mathf.Abs(vel.x);
            currentDirection.x = Mathf.Abs(currentDirection.x);
            bounced = true;
        }
        else if (pos.x > 11f && vel.x > 0)
        {
            vel.x = -Mathf.Abs(vel.x);
            currentDirection.x = -Mathf.Abs(currentDirection.x);
            bounced = true;
        }
        
        // Z boundaries (bounce off walls)
        if (pos.z < -11f && vel.z < 0)
        {
            vel.z = Mathf.Abs(vel.z);
            currentDirection.z = Mathf.Abs(currentDirection.z);
            bounced = true;
        }
        else if (pos.z > 11f && vel.z > 0)
        {
            vel.z = -Mathf.Abs(vel.z);
            currentDirection.z = -Mathf.Abs(currentDirection.z);
            bounced = true;
        }
        
        if (bounced)
        {
            rb.linearVelocity = vel;
            Debug.Log($"🏐 {name} bounced! New velocity: {vel}");
        }
    }
    
    void OnCollisionEnter(Collision collision)
    {
        // Bounce off walls and other objects
        if (collision.gameObject.name.Contains("Wall") || collision.gameObject.tag == "Wall")
        {
            Vector3 reflection = Vector3.Reflect(currentDirection, collision.contacts[0].normal);
            currentDirection = new Vector3(reflection.x, 0, reflection.z).normalized;
            
            Debug.Log($"🏐 {name} hit {collision.gameObject.name}, new direction: {currentDirection}");
        }
    }
}
