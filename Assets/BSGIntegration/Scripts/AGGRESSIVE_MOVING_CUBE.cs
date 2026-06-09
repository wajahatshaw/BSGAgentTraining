using UnityEngine;

/// <summary>
/// AGGRESSIVE MOVING CUBE - Uses RIGIDBODY PHYSICS like the working factory scene!
/// This uses the EXACT SAME movement pattern as ColoredMovement and ContinuousMovement
/// </summary>
public class AGGRESSIVE_MOVING_CUBE : MonoBehaviour
{
    [Header("PHYSICS-BASED AGGRESSIVE CUBE")]
    [Tooltip("Create and move cube immediately")]
    public bool CREATE_NOW = true;
    
    [Tooltip("Movement speed (physics-based)")]
    public float SPEED = 8f;
    
    [Tooltip("Cube size")]
    public float CUBE_SIZE = 1.5f;
    
    private GameObject movingCube;
    private Rigidbody cubeRigidbody;
    private Vector3 moveDirection;
    private float nextDirectionChange;
    private float nextRandomChange;
    
    void Start()
    {
        Debug.Log("🔥 AGGRESSIVE_MOVING_CUBE START() CALLED!");
        
        if (CREATE_NOW)
        {
            CreateAggressiveMovingCube();
        }
    }
    
    void CreateAggressiveMovingCube()
    {
        Debug.Log("🔥 CREATING PHYSICS-BASED AGGRESSIVE MOVING CUBE!");
        
        // Destroy any existing cube first
        if (movingCube != null)
        {
            DestroyImmediate(movingCube);
        }
        
        // Create new cube
        movingCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        movingCube.name = "PHYSICS_AGGRESSIVE_CUBE";
        
        // Position high so it falls (like factory scene entities)
        movingCube.transform.position = new Vector3(0, 5f, 0);
        movingCube.transform.localScale = Vector3.one * CUBE_SIZE;
        
        // Make it BRIGHT CYAN so it's VERY visible
        Renderer renderer = movingCube.GetComponent<Renderer>();
        Material material = new Material(Shader.Find("Unlit/Color"));
        material.color = Color.cyan;
        renderer.material = material;
        
        // ADD RIGIDBODY - THIS IS THE KEY!
        cubeRigidbody = movingCube.AddComponent<Rigidbody>();
        cubeRigidbody.useGravity = true;
        cubeRigidbody.mass = 2f;
        cubeRigidbody.linearDamping = 0.1f;
        cubeRigidbody.angularDamping = 8f;
        cubeRigidbody.freezeRotation = true;
        cubeRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        cubeRigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
        
        // Ensure collider exists (should already exist from CreatePrimitive)
        Collider col = movingCube.GetComponent<Collider>();
        if (col == null)
        {
            col = movingCube.AddComponent<BoxCollider>();
        }
        
        // Set initial movement direction and timing
        GenerateNewDirection();
        nextRandomChange = Time.time + Random.Range(0.2f, 0.5f);
        
        // FORCE IMMEDIATE MOVEMENT using physics
        Vector3 initialVelocity = new Vector3(Random.Range(-3f, 3f), cubeRigidbody.linearVelocity.y, Random.Range(-3f, 3f));
        cubeRigidbody.linearVelocity = initialVelocity;
        
        Debug.Log($"✅ CREATED PHYSICS CUBE at {movingCube.transform.position}");
        Debug.Log($"🚀 RIGIDBODY SETUP: Mass={cubeRigidbody.mass}, Gravity={cubeRigidbody.useGravity}, InitialVel={initialVelocity}");
    }
    
    void GenerateNewDirection()
    {
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        moveDirection = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
        nextDirectionChange = Time.time + Random.Range(2f, 4f);
        
        Debug.Log($"🔄 {movingCube.name} new direction: {moveDirection}");
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
        
        Debug.Log($"🎲 {movingCube.name} random variation applied");
    }
    
    void Update()
    {
        // PHYSICS-BASED MOVEMENT - EXACTLY LIKE ColoredMovement!
        if (movingCube != null && cubeRigidbody != null)
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
            targetVelocity.y = cubeRigidbody.linearVelocity.y; // Keep gravity
            cubeRigidbody.linearVelocity = targetVelocity;
            
            // Check boundaries and bounce
            CheckBounds();
            
            // Debug movement every second
            if (Time.frameCount % 60 == 0)
            {
                Debug.Log($"🏃 PHYSICS CUBE: Pos={movingCube.transform.position}, Vel={cubeRigidbody.linearVelocity}, Dir={moveDirection}");
            }
        }
        
        // Make sure cube exists
        if (movingCube == null && CREATE_NOW)
        {
            Debug.Log("⚠️ CUBE MISSING - RECREATING!");
            CreateAggressiveMovingCube();
        }
    }
    
    void CheckBounds()
    {
        if (cubeRigidbody == null) return;
        
        Vector3 pos = movingCube.transform.position;
        Vector3 vel = cubeRigidbody.linearVelocity;
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
            cubeRigidbody.linearVelocity = vel;
            Debug.Log($"🏀 {movingCube.name} bounced off wall!");
        }
    }
    
    [ContextMenu("Create Aggressive Moving Cube")]
    public void CreateAggressiveMovingCubeNow()
    {
        CreateAggressiveMovingCube();
    }
    
    [ContextMenu("Stop Movement")]
    public void StopMovement()
    {
        if (cubeRigidbody != null)
        {
            cubeRigidbody.linearVelocity = Vector3.zero;
            Debug.Log("⏹️ STOPPED PHYSICS MOVEMENT");
        }
    }
    
    [ContextMenu("Start Movement")]
    public void StartMovement()
    {
        if (movingCube != null && cubeRigidbody != null)
        {
            GenerateNewDirection();
            nextRandomChange = Time.time + Random.Range(0.2f, 0.5f);
            
            // Force immediate movement
            Vector3 initialVelocity = new Vector3(Random.Range(-3f, 3f), cubeRigidbody.linearVelocity.y, Random.Range(-3f, 3f));
            cubeRigidbody.linearVelocity = initialVelocity;
            
            Debug.Log("▶️ STARTED PHYSICS MOVEMENT");
        }
    }
}
