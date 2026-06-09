using UnityEngine;

public class FixedColorMovement : MonoBehaviour
{
    [Header("FIXED COLOR MOVEMENT SYSTEM")]
    public bool startOnPlay = true;
    
    void Start()
    {
        if (startOnPlay)
        {
            Debug.Log("🎨 FIXED COLOR MOVEMENT STARTING!");
            
            // Clear everything first
            ClearAllObjects();
            
            // Create working system with proper colors
            CreateColoredSystem();
            
            Debug.Log("✅ FIXED COLOR SYSTEM COMPLETE!");
        }
    }
    
    void ClearAllObjects()
    {
        // Destroy all existing objects except camera and this controller
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            if (obj != this.gameObject && 
                obj.name != "Main Camera" && 
                obj.name != "JSONSceneController" &&
                !obj.name.Contains("Camera"))
            {
                if (obj.name.Contains("WORKING") || 
                    obj.name.Contains("Technician") ||
                    obj.name.Contains("Supervisor") ||
                    obj.name.Contains("Wall") ||
                    obj.name.Contains("Floor"))
                {
                    Debug.Log($"🗑️ Destroying: {obj.name}");
                    DestroyImmediate(obj);
                }
            }
        }
    }
    
    void CreateColoredSystem()
    {
        Debug.Log("🎨 Creating system with proper colors...");
        
        // Create colored floor
        CreateColoredFloor();
        
        // Create colored walls
        CreateColoredWalls();
        
        // Create moving entities with proper colors
        CreateMovingEntitiesWithColors();
        
        // Setup camera
        SetupCamera();
        
        Debug.Log("🎯 All entities created with proper colors!");
    }
    
    void CreateColoredFloor()
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "COLORED_Floor";
        floor.transform.position = Vector3.zero;
        floor.transform.localScale = new Vector3(2, 1, 2);
        
        // Create bright green floor material
        Renderer floorRenderer = floor.GetComponent<Renderer>();
        Material floorMat = CreateColorMaterial(Color.green, "FloorMaterial");
        floorRenderer.material = floorMat;
        
        Debug.Log("✅ Green floor created with proper material");
    }
    
    void CreateColoredWalls()
    {
        // Create walls with distinct colors
        CreateColoredWall("COLORED_North_Wall", new Vector3(0, 1, 10), new Vector3(22, 2, 1), Color.red);
        CreateColoredWall("COLORED_South_Wall", new Vector3(0, 1, -10), new Vector3(22, 2, 1), Color.blue);
        CreateColoredWall("COLORED_East_Wall", new Vector3(10, 1, 0), new Vector3(1, 2, 22), Color.yellow);
        CreateColoredWall("COLORED_West_Wall", new Vector3(-10, 1, 0), new Vector3(1, 2, 22), Color.magenta);
        
        Debug.Log("✅ All colored walls created");
    }
    
    void CreateColoredWall(string name, Vector3 position, Vector3 scale, Color color)
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
        
        // Apply color material
        Renderer wallRenderer = wall.GetComponent<Renderer>();
        Material wallMat = CreateColorMaterial(color, $"{name}_Material");
        wallRenderer.material = wallMat;
        
        // Add kinematic rigidbody for collision
        Rigidbody rb = wall.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        
        Debug.Log($"✅ Created {name} with {color} color");
    }
    
    Material CreateColorMaterial(Color color, string materialName)
    {
        // Create material using Unlit/Color shader (always works)
        Material mat = new Material(Shader.Find("Unlit/Color"));
        if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
        {
            // Fallback to Legacy/Diffuse if Unlit/Color fails
            mat = new Material(Shader.Find("Legacy Shaders/Diffuse"));
        }
        
        mat.name = materialName;
        mat.color = color;
        
        Debug.Log($"✅ Created material {materialName} with shader {mat.shader.name}");
        return mat;
    }
    
    void CreateMovingEntitiesWithColors()
    {
        Debug.Log("👥 Creating 15 moving entities with proper colors...");
        
        // Create 10 blue technician cubes
        for (int i = 0; i < 10; i++)
        {
            CreateMovingEntity($"COLORED_Technician_{i:D2}", true, i);
        }
        
        // Create 5 red supervisor spheres
        for (int i = 0; i < 5; i++)
        {
            CreateMovingEntity($"COLORED_Supervisor_{i:D2}", false, i);
        }
        
        Debug.Log("✅ All 15 moving entities created!");
    }
    
    void CreateMovingEntity(string entityName, bool isTechnician, int index)
    {
        // Create appropriate shape
        GameObject entity = GameObject.CreatePrimitive(isTechnician ? PrimitiveType.Cube : PrimitiveType.Sphere);
        entity.name = entityName;
        
        // Random spawn position (high so they fall)
        Vector3 spawnPos = new Vector3(
            Random.Range(-8f, 8f),
            Random.Range(3f, 5f), // High spawn for gravity
            Random.Range(-8f, 8f)
        );
        entity.transform.position = spawnPos;
        
        // Add rigidbody for physics
        Rigidbody rb = entity.AddComponent<Rigidbody>();
        rb.useGravity = true;
        rb.mass = isTechnician ? 1.5f : 1f;
        rb.linearDamping = 0.1f;
        rb.freezeRotation = true;
        
        // Apply proper color
        Renderer entityRenderer = entity.GetComponent<Renderer>();
        Color entityColor = isTechnician ? 
            new Color(Random.Range(0.2f, 0.4f), Random.Range(0.3f, 0.5f), Random.Range(0.8f, 1f), 1f) : // Blue variations
            new Color(Random.Range(0.8f, 1f), Random.Range(0.1f, 0.3f), Random.Range(0.1f, 0.3f), 1f);   // Red variations
        
        Material entityMat = CreateColorMaterial(entityColor, $"{entityName}_Material");
        entityRenderer.material = entityMat;
        
        // Add movement component
        ColoredMovement movement = entity.AddComponent<ColoredMovement>();
        movement.speed = isTechnician ? 5f : 3f;
        movement.isTechnician = isTechnician;
        
        Debug.Log($"✅ Created {entityName} at {spawnPos} with color {entityColor}");
    }
    
    void SetupCamera()
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(0, 15, -12);
            cam.transform.rotation = Quaternion.Euler(45, 0, 0);
            cam.backgroundColor = new Color(0.2f, 0.3f, 0.4f); // Dark blue background
        }
        
        Debug.Log("📷 Camera positioned for colored scene");
    }
}

// Simple movement component that definitely works
public class ColoredMovement : MonoBehaviour
{
    public float speed = 0.05f;
    public bool isTechnician = true;
    
    private Rigidbody rb;
    private Vector3 moveDirection;
    private float nextDirectionChange;
    private float nextRandomChange;
    
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        GenerateNewDirection();
        nextRandomChange = Time.time + Random.Range(0.2f, 0.5f);
        
        Debug.Log($"🚀 {name} movement started - Speed: {speed}");
    }
    
    void Update()
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
        
        // Apply movement while preserving gravity
        Vector3 targetVelocity = moveDirection * speed;
        targetVelocity.y = rb.linearVelocity.y; // Keep gravity
        rb.linearVelocity = targetVelocity;
        
        // FORCE GROUND LEVEL POSITIONING
        Vector3 pos = transform.position;
        pos.y = 1f; // Force to ground level
        transform.position = pos;
        
        // Check boundaries and bounce
        CheckBounds();
    }
    
    void GenerateNewDirection()
    {
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        moveDirection = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
        nextDirectionChange = Time.time + Random.Range(2f, 4f);
        
        Debug.Log($"🔄 {name} new direction: {moveDirection}");
    }
    
    void AddRandomMovement()
    {
        // Add small random variations to movement
        Vector3 randomOffset = new Vector3(
            Random.Range(-0.5f, 0.5f),
            0f,
            Random.Range(-0.5f, 0.5f)
        );
        
        moveDirection = (moveDirection + randomOffset).normalized;
        nextRandomChange = Time.time + Random.Range(0.2f, 0.5f);
        
        Debug.Log($"🎲 {name} random variation applied");
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
        
        // X boundaries
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
        
        // Z boundaries
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
            rb.linearVelocity = vel;
            Debug.Log($"🏀 {name} bounced off wall!");
        }
    }
}
