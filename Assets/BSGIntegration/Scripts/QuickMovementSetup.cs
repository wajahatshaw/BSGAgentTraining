using UnityEngine;

public class QuickMovementSetup : MonoBehaviour
{
    [Header("Quick Random Movement & Bounceback Setup")]
    public bool setupOnStart = true;
    
    [Header("Entity Settings")]
    public int technicianCount = 10;
    public int supervisorCount = 5;
    public float technicianSpeed = 4f;
    public float supervisorSpeed = 3f;
    
    void Start()
    {
        if (setupOnStart)
        {
            Debug.Log("🚀 QUICK MOVEMENT SETUP STARTING...");
            SetupMovementSystem();
        }
    }
    
    void SetupMovementSystem()
    {
        // Clear any existing objects
        ClearExistingEntities();
        
        // Create the movement area
        CreateMovementArea();
        
        // Spawn moving entities
        SpawnMovingEntities();
        
        // Setup camera
        SetupCamera();
        
        Debug.Log("✅ RANDOM MOVEMENT & BOUNCEBACK SYSTEM READY!");
        Debug.Log($"👥 {technicianCount} blue technicians + {supervisorCount} red supervisors moving randomly");
        Debug.Log("🏀 Entities will bounce off walls and change direction randomly");
        
        // Remove this setup script
        Destroy(this);
    }
    
    void ClearExistingEntities()
    {
        // Find and remove existing generated entities
        GameObject[] existingEntities = GameObject.FindGameObjectsWithTag("MovingEntity");
        foreach (GameObject entity in existingEntities)
        {
            DestroyImmediate(entity);
        }
        
        // Also clear by name patterns
        string[] prefixes = {"Technician_", "Supervisor_", "Wall_", "Physics_"};
        foreach (string prefix in prefixes)
        {
            GameObject[] objects = FindObjectsOfType<GameObject>();
            foreach (GameObject obj in objects)
            {
                if (obj.name.StartsWith(prefix))
                {
                    DestroyImmediate(obj);
                }
            }
        }
        
        Debug.Log("🧹 Cleared existing entities");
    }
    
    void CreateMovementArea()
    {
        // Create floor
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Movement_Floor";
        floor.transform.position = Vector3.zero;
        floor.transform.localScale = new Vector3(2.5f, 1f, 2.5f); // 25x25 area
        
        // Floor material
        Renderer floorRenderer = floor.GetComponent<Renderer>();
        Material floorMaterial = new Material(Shader.Find("Standard"));
        floorMaterial.color = new Color(0.3f, 0.3f, 0.3f, 1f);
        floorRenderer.material = floorMaterial;
        
        // Create invisible walls for bouncing
        CreateBoundaryWall("North_Boundary", new Vector3(0, 1, 12), new Vector3(26, 2, 1));
        CreateBoundaryWall("South_Boundary", new Vector3(0, 1, -12), new Vector3(26, 2, 1));
        CreateBoundaryWall("East_Boundary", new Vector3(12, 1, 0), new Vector3(1, 2, 26));
        CreateBoundaryWall("West_Boundary", new Vector3(-12, 1, 0), new Vector3(1, 2, 26));
        
        Debug.Log("🏗️ Created movement area with boundaries");
    }
    
    void CreateBoundaryWall(string wallName, Vector3 position, Vector3 scale)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = wallName;
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
        
        // Make wall invisible but keep collider
        Renderer wallRenderer = wall.GetComponent<Renderer>();
        wallRenderer.enabled = false; // Invisible walls
        
        // Add kinematic rigidbody
        Rigidbody wallRb = wall.AddComponent<Rigidbody>();
        wallRb.isKinematic = true;
        
        Debug.Log($"✅ Created boundary: {wallName}");
    }
    
    void SpawnMovingEntities()
    {
        Debug.Log($"👥 Spawning {technicianCount + supervisorCount} moving entities...");
        
        // Spawn blue technicians (cubes)
        for (int i = 0; i < technicianCount; i++)
        {
            SpawnMovingEntity("Technician", i, PrimitiveType.Cube, Color.blue, technicianSpeed);
        }
        
        // Spawn red supervisors (spheres)
        for (int i = 0; i < supervisorCount; i++)
        {
            SpawnMovingEntity("Supervisor", i, PrimitiveType.Sphere, Color.red, supervisorSpeed);
        }
        
        Debug.Log("✅ All moving entities spawned!");
    }
    
    void SpawnMovingEntity(string entityType, int index, PrimitiveType shape, Color baseColor, float speed)
    {
        // Create entity
        GameObject entity = GameObject.CreatePrimitive(shape);
        entity.name = $"{entityType}_{index:D2}";
        entity.tag = "MovingEntity";
        
        // Random spawn position (avoid walls) - spawn higher so gravity works
        Vector3 spawnPos = new Vector3(
            Random.Range(-8f, 8f),
            2f, // Spawn higher so entities fall to ground
            Random.Range(-8f, 8f)
        );
        entity.transform.position = spawnPos;
        
        // Add and configure RandomMovingEntity component
        RandomMovingEntity movementScript = entity.AddComponent<RandomMovingEntity>();
        movementScript.moveSpeed = speed;
        movementScript.directionChangeInterval = Random.Range(1.5f, 3f); // Vary timing
        
        // Set color with more variation
        Color entityColor = GetColorVariation(baseColor);
        movementScript.entityColor = entityColor;
        
        // Set movement bounds
        movementScript.movementBoundsMin = new Vector3(-11f, 0f, -11f);
        movementScript.movementBoundsMax = new Vector3(11f, 0f, 11f);
        
        // Force color application after component is added
        movementScript.SetEntityColor(entityColor);
        
        // Set entity type
        if (entityType == "Technician")
        {
            movementScript.entityType = RandomMovingEntity.EntityType.Technician;
        }
        else
        {
            movementScript.entityType = RandomMovingEntity.EntityType.Supervisor;
        }
        
        Debug.Log($"✅ Spawned {entityType} at {spawnPos} with speed {speed}");
    }
    
    Color GetColorVariation(Color baseColor)
    {
        float variation = 0.3f;
        Color newColor = new Color(
            Mathf.Clamp01(baseColor.r + Random.Range(-variation, variation)),
            Mathf.Clamp01(baseColor.g + Random.Range(-variation, variation)),
            Mathf.Clamp01(baseColor.b + Random.Range(-variation, variation)),
            1f
        );
        
        // Ensure colors are distinct and visible
        if (baseColor == Color.blue)
        {
            newColor.b = Mathf.Clamp01(0.7f + Random.Range(-0.2f, 0.3f)); // Keep blue dominant
            newColor.r = Random.Range(0f, 0.3f); // Minimal red
            newColor.g = Random.Range(0f, 0.4f); // Some green for variation
        }
        else if (baseColor == Color.red)
        {
            newColor.r = Mathf.Clamp01(0.7f + Random.Range(-0.2f, 0.3f)); // Keep red dominant
            newColor.g = Random.Range(0f, 0.3f); // Minimal green
            newColor.b = Random.Range(0f, 0.3f); // Minimal blue
        }
        
        return newColor;
    }
    
    void SetupCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            GameObject cameraObj = new GameObject("Movement_Camera");
            mainCamera = cameraObj.AddComponent<Camera>();
            cameraObj.tag = "MainCamera";
        }
        
        // Position camera to see all the movement
        mainCamera.transform.position = new Vector3(0, 18, -12);
        mainCamera.transform.rotation = Quaternion.Euler(50, 0, 0);
        
        Debug.Log("📷 Camera positioned to view movement area");
    }
}
