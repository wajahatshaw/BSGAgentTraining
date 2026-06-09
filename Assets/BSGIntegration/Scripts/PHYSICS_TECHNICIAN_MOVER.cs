using UnityEngine;

/// <summary>
/// PHYSICS TECHNICIAN MOVER - Uses the EXACT SAME physics movement as the working factory scene
/// This creates a technician that moves using Rigidbody physics like ColoredMovement and ContinuousMovement
/// </summary>
public class PHYSICS_TECHNICIAN_MOVER : MonoBehaviour
{
    [Header("PHYSICS-BASED TECHNICIAN")]
    [Tooltip("Create technician immediately")]
    public bool CREATE_NOW = true;
    
    [Tooltip("Movement speed (physics-based)")]
    public float SPEED = 6f;
    
    [Tooltip("Technician size")]
    public float TECHNICIAN_SIZE = 1f;
    
    private GameObject technician;
    private Rigidbody technicianRigidbody;
    private Vector3 moveDirection;
    private float nextDirectionChange;
    private float nextRandomChange;
    
    void Start()
    {
        Debug.Log("🔧 PHYSICS_TECHNICIAN_MOVER START() CALLED!");
        
        if (CREATE_NOW)
        {
            CreatePhysicsTechnician();
        }
    }
    
    void CreatePhysicsTechnician()
    {
        Debug.Log("🔧 CREATING PHYSICS-BASED TECHNICIAN!");
        
        // Destroy any existing technician first
        if (technician != null)
        {
            DestroyImmediate(technician);
        }
        
        // Create technician as a cube (like factory scene)
        technician = GameObject.CreatePrimitive(PrimitiveType.Cube);
        technician.name = "PHYSICS_TECHNICIAN";
        
        // Position high so it falls (like factory scene entities)
        technician.transform.position = new Vector3(2f, 5f, 2f);
        technician.transform.localScale = Vector3.one * TECHNICIAN_SIZE;
        
        // Make it BRIGHT BLUE (technician color)
        Renderer renderer = technician.GetComponent<Renderer>();
        Material material = new Material(Shader.Find("Unlit/Color"));
        material.color = new Color(0.2f, 0.4f, 1f, 1f); // Bright blue like factory technicians
        renderer.material = material;
        
        // ADD RIGIDBODY - EXACT SAME SETUP AS FACTORY SCENE!
        technicianRigidbody = technician.AddComponent<Rigidbody>();
        technicianRigidbody.useGravity = true;
        technicianRigidbody.mass = 2f; // Technician mass from factory scene
        technicianRigidbody.linearDamping = 0.1f;
        technicianRigidbody.angularDamping = 8f;
        technicianRigidbody.freezeRotation = true;
        technicianRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        technicianRigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
        
        // Ensure collider exists (should already exist from CreatePrimitive)
        Collider col = technician.GetComponent<Collider>();
        if (col == null)
        {
            col = technician.AddComponent<BoxCollider>();
        }
        
        // Set initial movement direction and timing
        GenerateNewDirection();
        nextRandomChange = Time.time + Random.Range(0.2f, 0.5f);
        
        // FORCE IMMEDIATE MOVEMENT using physics
        Vector3 initialVelocity = new Vector3(Random.Range(-3f, 3f), technicianRigidbody.linearVelocity.y, Random.Range(-3f, 3f));
        technicianRigidbody.linearVelocity = initialVelocity;
        
        Debug.Log($"✅ CREATED PHYSICS TECHNICIAN at {technician.transform.position}");
        Debug.Log($"🚀 RIGIDBODY SETUP: Mass={technicianRigidbody.mass}, Gravity={technicianRigidbody.useGravity}, InitialVel={initialVelocity}");
    }
    
    void GenerateNewDirection()
    {
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        moveDirection = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
        nextDirectionChange = Time.time + Random.Range(1.5f, 2.5f); // Technician timing from factory
        
        Debug.Log($"🔄 {technician.name} new direction: {moveDirection}");
    }
    
    void AddRandomMovement()
    {
        // Add small random variations to movement (like ColoredMovement)
        Vector3 randomOffset = new Vector3(
            Random.Range(-0.5f, 0.5f),
            0f,
            Random.Range(-0.5f, 0.5f)
        );
        
        moveDirection = (moveDirection + randomOffset).normalized;
        nextRandomChange = Time.time + Random.Range(0.2f, 0.5f);
        
        Debug.Log($"🎲 {technician.name} random variation applied");
    }
    
    void Update()
    {
        // PHYSICS-BASED MOVEMENT - EXACTLY LIKE ColoredMovement!
        if (technician != null && technicianRigidbody != null)
        {
            // Change direction periodically
            if (Time.time > nextDirectionChange)
            {
                GenerateNewDirection();
            }
            
            // Add random movement variation
            if (Time.time > nextRandomChange)
            {
                AddRandomMovement();
            }
            
            // Apply movement while preserving gravity (EXACT COPY from ColoredMovement)
            Vector3 targetVelocity = moveDirection * SPEED;
            targetVelocity.y = technicianRigidbody.linearVelocity.y; // Keep gravity
            technicianRigidbody.linearVelocity = targetVelocity;
            
            // Check boundaries and bounce
            CheckBounds();
            
            // Debug movement every second
            if (Time.frameCount % 60 == 0)
            {
                Debug.Log($"🔧 PHYSICS TECHNICIAN: Pos={technician.transform.position}, Vel={technicianRigidbody.linearVelocity}, Dir={moveDirection}");
            }
        }
        
        // Make sure technician exists
        if (technician == null && CREATE_NOW)
        {
            Debug.Log("⚠️ TECHNICIAN MISSING - RECREATING!");
            CreatePhysicsTechnician();
        }
    }
    
    void CheckBounds()
    {
        if (technicianRigidbody == null) return;
        
        Vector3 pos = technician.transform.position;
        Vector3 vel = technicianRigidbody.linearVelocity;
        bool bounced = false;
        
        // X boundaries (EXACT COPY from ColoredMovement)
        if (pos.x < -9f && vel.x < 0)
        {
            vel.x = Mathf.Abs(vel.x);
            moveDirection.x = Mathf.Abs(moveDirection.x);
            bounced = true;
        }
        else if (pos.x > 9f && vel.x > 0)
        {
            vel.x = -Mathf.Abs(vel.x);
            moveDirection.x = -Mathf.Abs(moveDirection.x);
            bounced = true;
        }
        
        // Z boundaries (EXACT COPY from ColoredMovement)
        if (pos.z < -9f && vel.z < 0)
        {
            vel.z = Mathf.Abs(vel.z);
            moveDirection.z = Mathf.Abs(moveDirection.z);
            bounced = true;
        }
        else if (pos.z > 9f && vel.z > 0)
        {
            vel.z = -Mathf.Abs(vel.z);
            moveDirection.z = -Mathf.Abs(moveDirection.z);
            bounced = true;
        }
        
        if (bounced)
        {
            technicianRigidbody.linearVelocity = vel;
            Debug.Log($"🏀 {technician.name} bounced off wall!");
        }
    }
    
    [ContextMenu("Create Physics Technician")]
    public void CreatePhysicsTechnicianNow()
    {
        CreatePhysicsTechnician();
    }
    
    [ContextMenu("Stop Movement")]
    public void StopMovement()
    {
        if (technicianRigidbody != null)
        {
            technicianRigidbody.linearVelocity = Vector3.zero;
            Debug.Log("⏹️ STOPPED TECHNICIAN PHYSICS MOVEMENT");
        }
    }
    
    [ContextMenu("Start Movement")]
    public void StartMovement()
    {
        if (technician != null && technicianRigidbody != null)
        {
            GenerateNewDirection();
            nextRandomChange = Time.time + Random.Range(0.2f, 0.5f);
            
            // Force immediate movement
            Vector3 initialVelocity = new Vector3(Random.Range(-3f, 3f), technicianRigidbody.linearVelocity.y, Random.Range(-3f, 3f));
            technicianRigidbody.linearVelocity = initialVelocity;
            
            Debug.Log("▶️ STARTED TECHNICIAN PHYSICS MOVEMENT");
        }
    }
}
