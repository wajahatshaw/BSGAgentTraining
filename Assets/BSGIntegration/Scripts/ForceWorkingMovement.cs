using UnityEngine;

public class ForceWorkingMovement : MonoBehaviour
{
    [Header("FORCE WORKING MOVEMENT SYSTEM")]
    public bool forceStart = true;
    
    void Start()
    {
        if (forceStart)
        {
            Debug.Log("🔥 FORCE WORKING MOVEMENT STARTING NOW!");
            
            // Disable ALL other scripts first
            DisableAllOtherScripts();
            
            // Clear everything
            ClearAllObjects();
            
            // Create working system
            CreateWorkingMovementSystem();
            
            Debug.Log("✅ FORCE WORKING MOVEMENT COMPLETE!");
        }
    }
    
    void DisableAllOtherScripts()
    {
        // Find all MonoBehaviour scripts and disable them
        MonoBehaviour[] allScripts = FindObjectsOfType<MonoBehaviour>();
        
        foreach (MonoBehaviour script in allScripts)
        {
            if (script != this && script.enabled)
            {
                string scriptName = script.GetType().Name;
                if (scriptName.Contains("JSON") || 
                    scriptName.Contains("Scene") || 
                    scriptName.Contains("Generator") ||
                    scriptName.Contains("Setup") ||
                    scriptName.Contains("Standalone"))
                {
                    Debug.Log($"🔇 Disabling: {scriptName}");
                    script.enabled = false;
                }
            }
        }
    }
    
    void ClearAllObjects()
    {
        // Destroy all existing objects
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            if (obj != this.gameObject && obj.name != "Main Camera")
            {
                if (obj.name.Contains("Technician") || 
                    obj.name.Contains("Supervisor") ||
                    obj.name.Contains("Wall") ||
                    obj.name.Contains("Floor") ||
                    obj.name.Contains("Canvas") ||
                    obj.name.Contains("Generated") ||
                    obj.name.Contains("Physics") ||
                    obj.name.Contains("Movement"))
                {
                    Debug.Log($"🗑️ Destroying: {obj.name}");
                    DestroyImmediate(obj);
                }
            }
        }
    }
    
    void CreateWorkingMovementSystem()
    {
        Debug.Log("🏗️ Creating working movement system...");
        
        // Create floor
        CreateFloor();
        
        // Create walls
        CreateWalls();
        
        // Create moving entities
        CreateMovingEntities();
        
        // Setup camera
        SetupCamera();
        
        Debug.Log("🎯 All entities should be moving now!");
    }
    
    void CreateFloor()
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "WORKING_Floor";
        floor.transform.position = Vector3.zero;
        floor.transform.localScale = new Vector3(2, 1, 2);
        
        // Green floor material with texture-like appearance
        Renderer floorRenderer = floor.GetComponent<Renderer>();
        Material floorMat = new Material(Shader.Find("Standard"));
        floorMat.color = new Color(0.2f, 0.6f, 0.2f, 1f); // Forest green
        floorMat.SetFloat("_Metallic", 0.1f);
        floorMat.SetFloat("_Glossiness", 0.3f);
        floorRenderer.material = floorMat;
        
        Debug.Log("✅ Green floor created");
    }
    
    void CreateWalls()
    {
        // Create visible colored boundary walls
        CreateWall("WORKING_North_Wall", new Vector3(0, 1, 10), new Vector3(22, 2, 1), new Color(0.8f, 0.2f, 0.2f)); // Red
        CreateWall("WORKING_South_Wall", new Vector3(0, 1, -10), new Vector3(22, 2, 1), new Color(0.2f, 0.2f, 0.8f)); // Blue
        CreateWall("WORKING_East_Wall", new Vector3(10, 1, 0), new Vector3(1, 2, 22), new Color(0.8f, 0.8f, 0.2f)); // Yellow
        CreateWall("WORKING_West_Wall", new Vector3(-10, 1, 0), new Vector3(1, 2, 22), new Color(0.8f, 0.2f, 0.8f)); // Magenta
        
        Debug.Log("✅ Colored walls created");
    }
    
    void CreateWall(string name, Vector3 position, Vector3 scale, Color wallColor)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.position = position;
        wall.transform.localScale = scale;
        try
        {
            wall.tag = "Wall";
        }
        catch (System.Exception)
        {
            Debug.Log($"ℹ️ Wall tag not defined, skipping tag assignment for {name}");
        }
        
        // Make walls visible with different colors
        Renderer wallRenderer = wall.GetComponent<Renderer>();
        Material wallMat = new Material(Shader.Find("Standard"));
        wallMat.color = wallColor;
        wallMat.SetFloat("_Metallic", 0.3f);
        wallMat.SetFloat("_Glossiness", 0.7f);
        wallRenderer.material = wallMat;
        
        // Add kinematic rigidbody
        Rigidbody rb = wall.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        
        Debug.Log($"✅ Created {name} with color {wallColor}");
    }
    
    void CreateMovingEntities()
    {
        Debug.Log("👥 Creating moving entities...");
        
        // Create 10 blue technicians
        for (int i = 0; i < 10; i++)
        {
            CreateMovingEntity("Technician", i, true);
        }
        
        // Create 5 red supervisors
        for (int i = 0; i < 5; i++)
        {
            CreateMovingEntity("Supervisor", i, false);
        }
        
        Debug.Log("✅ All moving entities created!");
    }
    
    void CreateMovingEntity(string type, int index, bool isTechnician)
    {
        // Create shape
        GameObject entity = GameObject.CreatePrimitive(isTechnician ? PrimitiveType.Cube : PrimitiveType.Sphere);
        entity.name = $"WORKING_{type}_{index:D2}";
        
        // Random position
        Vector3 pos = new Vector3(
            Random.Range(-8f, 8f),
            3f, // High so it falls
            Random.Range(-8f, 8f)
        );
        entity.transform.position = pos;
        
        // Add rigidbody with proper gravity settings
        Rigidbody rb = entity.AddComponent<Rigidbody>();
        rb.useGravity = true;
        rb.mass = isTechnician ? 1.5f : 1f; // Technicians heavier
        rb.linearDamping = 0.2f; // Less damping for more fluid movement
        rb.freezeRotation = true; // Prevent tumbling
        
        // Add movement script
        WorkingMovement movement = entity.AddComponent<WorkingMovement>();
        movement.speed = isTechnician ? 4f : 3f;
        movement.isTechnician = isTechnician;
        
        // Set color with variations
        Renderer renderer = entity.GetComponent<Renderer>();
        Material mat = new Material(Shader.Find("Standard"));
        
        if (isTechnician)
        {
            // Blue variations for technicians
            mat.color = new Color(
                Random.Range(0f, 0.3f),      // Low red
                Random.Range(0.2f, 0.5f),    // Some green
                Random.Range(0.7f, 1f),      // High blue
                1f
            );
        }
        else
        {
            // Red variations for supervisors
            mat.color = new Color(
                Random.Range(0.7f, 1f),      // High red
                Random.Range(0f, 0.3f),      // Low green
                Random.Range(0f, 0.3f),      // Low blue
                1f
            );
        }
        
        mat.SetFloat("_Metallic", 0.2f);
        mat.SetFloat("_Glossiness", 0.5f);
        renderer.material = mat;
        
        Debug.Log($"✅ Created {type} at {pos}");
    }
    
    void SetupCamera()
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(0, 15, -10);
            cam.transform.rotation = Quaternion.Euler(45, 0, 0);
        }
        
        Debug.Log("📷 Camera positioned");
    }
}

// Enhanced working movement component with continuous random motion
public class WorkingMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    public float speed = 0.05f;
    public bool isTechnician = true;
    public float directionChangeInterval = 2f;
    
    [Header("Continuous Random Motion")]
    public bool enableRandomMotion = true;
    public float randomMotionIntensity = 0.03f;
    public float randomMotionInterval = 0.3f;
    
    private Rigidbody rb;
    private Vector3 baseDirection;
    private Vector3 randomOffset;
    private float nextDirectionChange;
    private float nextRandomMotion;
    
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        GenerateNewDirection();
        
        // Set different intervals for technicians vs supervisors
        directionChangeInterval = isTechnician ? Random.Range(1.5f, 2.5f) : Random.Range(2f, 3f);
        randomMotionIntensity = isTechnician ? 0.04f : 0.03f; // Much lower random motion
        
        Debug.Log($"🚀 {name} movement started with speed {speed}, random motion enabled");
    }
    
    void Update()
    {
        // Change base direction periodically
        if (Time.time > nextDirectionChange)
        {
            GenerateNewDirection();
        }
        
        // Add continuous random motion
        if (enableRandomMotion && Time.time > nextRandomMotion)
        {
            GenerateRandomOffset();
        }
        
        // Combine base direction with random offset
        Vector3 finalDirection = (baseDirection + randomOffset).normalized;
        
        // Apply movement with gravity preservation
        Vector3 velocity = finalDirection * speed;
        velocity.y = rb.linearVelocity.y; // Keep gravity
        rb.linearVelocity = velocity;
        
        // FORCE GROUND LEVEL POSITIONING
        Vector3 pos = transform.position;
        pos.y = 1f; // Force to ground level
        transform.position = pos;
        
        // Bounce off boundaries
        CheckBounds();
    }
    
    void GenerateNewDirection()
    {
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        baseDirection = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
        nextDirectionChange = Time.time + directionChangeInterval + Random.Range(-0.5f, 0.5f);
        
        Debug.Log($"🔄 {name} new base direction: {baseDirection}");
    }
    
    void GenerateRandomOffset()
    {
        // Generate small random offset for continuous motion
        randomOffset = new Vector3(
            Random.Range(-randomMotionIntensity, randomMotionIntensity),
            0f, // No Y component
            Random.Range(-randomMotionIntensity, randomMotionIntensity)
        ) * 0.3f; // Scale down the offset
        
        nextRandomMotion = Time.time + randomMotionInterval + Random.Range(-0.1f, 0.1f);
        
        Debug.Log($"🎲 {name} random offset: {randomOffset}");
    }
    
    void CheckBounds()
    {
        Vector3 pos = transform.position;
        
        // FORCE GROUND LEVEL AND BOUNDARY CONSTRAINTS
        pos.y = 1f; // Force to ground level
        
        // Keep within ground plane boundaries
        pos.x = Mathf.Clamp(pos.x, -10f, 10f); // X boundaries
        pos.z = Mathf.Clamp(pos.z, -10f, 10f); // Z boundaries
        
        transform.position = pos;
        Vector3 vel = rb.linearVelocity;
        bool bounced = false;
        
        // X bounds
        if (pos.x < -9f && vel.x < 0)
        {
            vel.x = Mathf.Abs(vel.x);
            baseDirection.x = Mathf.Abs(baseDirection.x);
            bounced = true;
        }
        else if (pos.x > 9f && vel.x > 0)
        {
            vel.x = -Mathf.Abs(vel.x);
            baseDirection.x = -Mathf.Abs(baseDirection.x);
            bounced = true;
        }
        
        // Z bounds
        if (pos.z < -9f && vel.z < 0)
        {
            vel.z = Mathf.Abs(vel.z);
            baseDirection.z = Mathf.Abs(baseDirection.z);
            bounced = true;
        }
        else if (pos.z > 9f && vel.z > 0)
        {
            vel.z = -Mathf.Abs(vel.z);
            baseDirection.z = -Mathf.Abs(baseDirection.z);
            bounced = true;
        }
        
        if (bounced)
        {
            rb.linearVelocity = vel;
            Debug.Log($"🏀 {name} bounced!");
        }
    }
}
