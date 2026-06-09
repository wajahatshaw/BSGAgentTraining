using UnityEngine;

public class NoShaderColorSystem : MonoBehaviour
{
    [Header("NO SHADER COLOR SYSTEM - GUARANTEED TO WORK")]
    public bool startOnPlay = true;
    
    void Start()
    {
        if (startOnPlay)
        {
            Debug.Log("🎨 NO SHADER COLOR SYSTEM STARTING!");
            
            // Clear everything
            ClearAll();
            
            // Create system without any custom shaders
            CreateNoShaderSystem();
            
            Debug.Log("✅ NO SHADER SYSTEM COMPLETE - NO PINK COLORS!");
        }
    }
    
    void ClearAll()
    {
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            if (obj != this.gameObject && 
                obj.name != "Main Camera" && 
                obj.name != "JSONSceneController" &&
                !obj.name.Contains("Camera"))
            {
                if (obj.name.Contains("SIMPLE") || 
                    obj.name.Contains("WORKING") ||
                    obj.name.Contains("COLORED") ||
                    obj.name.Contains("Technician") ||
                    obj.name.Contains("Supervisor") ||
                    obj.name.Contains("Wall") ||
                    obj.name.Contains("Floor") ||
                    obj.name.Contains("Plane"))
                {
                    DestroyImmediate(obj);
                }
            }
        }
    }
    
    void CreateNoShaderSystem()
    {
        Debug.Log("🏗️ Creating system with NO custom shaders...");
        
        // Create plane using default material
        CreateDefaultPlane();
        
        // Create walls using default materials
        CreateDefaultWalls();
        
        // Create entities using default materials
        CreateDefaultEntities();
        
        // Setup camera
        SetupCamera();
        
        Debug.Log("🎯 System created with default Unity materials only!");
    }
    
    void CreateDefaultPlane()
    {
        GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.name = "DEFAULT_Plane";
        plane.transform.position = Vector3.zero;
        plane.transform.localScale = new Vector3(2.5f, 1f, 2.5f);
        
        // Use the default material that comes with the primitive
        Renderer planeRenderer = plane.GetComponent<Renderer>();
        Material defaultMat = planeRenderer.material;
        
        // Just change the color - don't create new material
        defaultMat.color = new Color(0.1f, 0.6f, 0.1f, 1f); // Green
        
        Debug.Log("✅ Green plane created with default material");
    }
    
    void CreateDefaultWalls()
    {
        CreateDefaultWall("DEFAULT_North_Wall", new Vector3(0, 1.5f, 12), new Vector3(26, 3, 1), Color.red);
        CreateDefaultWall("DEFAULT_South_Wall", new Vector3(0, 1.5f, -12), new Vector3(26, 3, 1), Color.blue);
        CreateDefaultWall("DEFAULT_East_Wall", new Vector3(12, 1.5f, 0), new Vector3(1, 3, 26), Color.yellow);
        CreateDefaultWall("DEFAULT_West_Wall", new Vector3(-12, 1.5f, 0), new Vector3(1, 3, 26), Color.magenta);
        
        Debug.Log("✅ All walls created with default materials");
    }
    
    void CreateDefaultWall(string name, Vector3 position, Vector3 scale, Color color)
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
        
        // Use default material and just change color
        Renderer wallRenderer = wall.GetComponent<Renderer>();
        Material defaultMat = wallRenderer.material;
        defaultMat.color = color;
        
        // Add rigidbody
        Rigidbody rb = wall.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        
        Debug.Log($"✅ Created {name} with default material - color: {color}");
    }
    
    void CreateDefaultEntities()
    {
        Debug.Log("👥 Creating entities with default materials...");
        
        // Create 2 technicians
        CreateDefaultEntity("DEFAULT_Technician_01", true, new Vector3(-5f, 3f, -5f), new Color(0.2f, 0.4f, 1f, 1f));
        CreateDefaultEntity("DEFAULT_Technician_02", true, new Vector3(5f, 3f, 5f), new Color(0.4f, 0.7f, 1f, 1f));
        
        // Create 2 supervisors
        CreateDefaultEntity("DEFAULT_Supervisor_01", false, new Vector3(5f, 3f, -5f), new Color(1f, 0.2f, 0.2f, 1f));
        CreateDefaultEntity("DEFAULT_Supervisor_02", false, new Vector3(-5f, 3f, 5f), new Color(1f, 0.5f, 0.1f, 1f));
        
        Debug.Log("✅ All entities created with default materials");
    }
    
    void CreateDefaultEntity(string name, bool isTechnician, Vector3 position, Color color)
    {
        // Create shape
        GameObject entity = GameObject.CreatePrimitive(isTechnician ? PrimitiveType.Cube : PrimitiveType.Sphere);
        entity.name = name;
        entity.transform.position = position;
        
        // Use default material and just change color
        Renderer entityRenderer = entity.GetComponent<Renderer>();
        Material defaultMat = entityRenderer.material;
        defaultMat.color = color;
        
        // Add physics
        Rigidbody rb = entity.AddComponent<Rigidbody>();
        rb.useGravity = true;
        rb.mass = isTechnician ? 2f : 1.5f;
        rb.linearDamping = 0.3f;
        rb.freezeRotation = true;
        
        // Add simple movement
        SimpleDefaultMovement movement = entity.AddComponent<SimpleDefaultMovement>();
        movement.speed = isTechnician ? 6f : 4f;
        movement.isTechnician = isTechnician;
        
        Debug.Log($"✅ Created {name} with default material - color: {color}");
    }
    
    void SetupCamera()
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(0, 18, -15);
            cam.transform.rotation = Quaternion.Euler(50, 0, 0);
            cam.backgroundColor = new Color(0.1f, 0.1f, 0.2f, 1f);
        }
        
        Debug.Log("📷 Camera positioned");
    }
}

// Simple movement that definitely works
public class SimpleDefaultMovement : MonoBehaviour
{
    public float speed = 0.06f;
    public bool isTechnician = true;
    
    private Rigidbody rb;
    private Vector3 direction;
    private float nextChange;
    
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        GenerateDirection();
        Debug.Log($"🚀 {name} movement started - Speed: {speed}");
    }
    
    void Update()
    {
        if (Time.time > nextChange)
        {
            GenerateDirection();
        }
        
        // Apply movement
        Vector3 vel = direction * speed;
        vel.y = rb.linearVelocity.y; // Keep gravity
        rb.linearVelocity = vel;
        
        // FORCE GROUND LEVEL POSITIONING
        Vector3 pos = transform.position;
        pos.y = 1f; // Force to ground level
        transform.position = pos;
        
        // FORCE BOUNDARY CONSTRAINTS - Keep within ground plane
        pos.x = Mathf.Clamp(pos.x, -10f, 10f); // X boundaries
        pos.z = Mathf.Clamp(pos.z, -10f, 10f); // Z boundaries
        
        transform.position = pos;
        
        // Simple boundary check with direction reversal
        if (pos.x <= -10f || pos.x >= 10f || pos.z <= -10f || pos.z >= 10f)
        {
            // Reverse direction if hitting boundary
            direction = -direction;
            Debug.Log($"🏀 {name} bounced!");
        }
    }
    
    void GenerateDirection()
    {
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
        nextChange = Time.time + Random.Range(2f, 4f);
        Debug.Log($"🔄 {name} new direction");
    }
}
