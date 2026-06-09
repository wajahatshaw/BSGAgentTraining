using UnityEngine;

public class SimpleEntitySpawner : MonoBehaviour
{
    [Header("Spawning Settings")]
    public int technicianCount = 10;
    public int supervisorCount = 5;
    
    [Header("Movement Settings")]
    public float technicianSpeed = 3f;
    public float supervisorSpeed = 2f;
    public float directionChangeInterval = 2f;
    
    [Header("Area Settings")]
    public Vector3 spawnAreaMin = new Vector3(-8f, 0.5f, -8f);
    public Vector3 spawnAreaMax = new Vector3(8f, 0.5f, 8f);
    public Vector3 movementBoundsMin = new Vector3(-10f, 0f, -10f);
    public Vector3 movementBoundsMax = new Vector3(10f, 0f, 10f);
    
    [Header("Colors")]
    public Color technicianBaseColor = Color.blue;
    public Color supervisorBaseColor = Color.red;
    
    void Start()
    {
        Debug.Log("🚀 SIMPLE PHYSICS SPAWNER STARTING...");
        
        // Create walls first
        CreateWalls();
        
        // Create floor
        CreateFloor();
        
        // Spawn entities
        SpawnAllEntities();
        
        // Setup camera
        SetupCamera();
        
        Debug.Log("✅ SIMPLE PHYSICS SCENE COMPLETE!");
    }
    
    void CreateWalls()
    {
        Debug.Log("🏗️ Creating physics walls...");
        
        // Create walls with colliders
        CreateWall("North_Wall", new Vector3(0, 1, movementBoundsMax.z), new Vector3(25, 2, 1));
        CreateWall("South_Wall", new Vector3(0, 1, movementBoundsMin.z), new Vector3(25, 2, 1));
        CreateWall("East_Wall", new Vector3(movementBoundsMax.x, 1, 0), new Vector3(1, 2, 25));
        CreateWall("West_Wall", new Vector3(movementBoundsMin.x, 1, 0), new Vector3(1, 2, 25));
    }
    
    void CreateWall(string wallName, Vector3 position, Vector3 scale)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = wallName;
        wall.transform.position = position;
        wall.transform.localScale = scale;
        wall.tag = "Wall";
        
        // Make wall static
        Rigidbody wallRb = wall.GetComponent<Rigidbody>();
        if (wallRb == null)
        {
            wallRb = wall.AddComponent<Rigidbody>();
        }
        wallRb.isKinematic = true;
        
        // Set wall material
        Renderer wallRenderer = wall.GetComponent<Renderer>();
        Material wallMaterial = new Material(Shader.Find("Standard"));
        wallMaterial.color = new Color(0.7f, 0.8f, 1f, 0.8f);
        wallMaterial.SetFloat("_Mode", 3); // Transparent mode
        wallMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        wallMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        wallMaterial.SetInt("_ZWrite", 0);
        wallMaterial.DisableKeyword("_ALPHATEST_ON");
        wallMaterial.EnableKeyword("_ALPHABLEND_ON");
        wallMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        wallMaterial.renderQueue = 3000;
        wallRenderer.material = wallMaterial;
        
        Debug.Log($"✅ Created wall: {wallName}");
    }
    
    void CreateFloor()
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Physics_Floor";
        floor.transform.position = new Vector3(0, 0, 0);
        floor.transform.localScale = new Vector3(3, 1, 3); // 30x30 units
        
        // Set floor material
        Renderer floorRenderer = floor.GetComponent<Renderer>();
        Material floorMaterial = new Material(Shader.Find("Standard"));
        floorMaterial.color = new Color(0.5f, 0.5f, 0.5f, 1f);
        floorRenderer.material = floorMaterial;
        
        Debug.Log("✅ Created floor");
    }
    
    void SpawnAllEntities()
    {
        Debug.Log($"👥 Spawning {technicianCount} technicians and {supervisorCount} supervisors...");
        
        // Spawn technicians (blue cubes)
        for (int i = 0; i < technicianCount; i++)
        {
            SpawnEntity(RandomMovingEntity.EntityType.Technician, i);
        }
        
        // Spawn supervisors (red spheres)
        for (int i = 0; i < supervisorCount; i++)
        {
            SpawnEntity(RandomMovingEntity.EntityType.Supervisor, i);
        }
        
        Debug.Log("✅ All entities spawned!");
    }
    
    void SpawnEntity(RandomMovingEntity.EntityType entityType, int index)
    {
        // Create appropriate primitive
        GameObject entity;
        if (entityType == RandomMovingEntity.EntityType.Technician)
        {
            entity = GameObject.CreatePrimitive(PrimitiveType.Cube);
            entity.name = $"Technician_{index:D2}";
        }
        else
        {
            entity = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            entity.name = $"Supervisor_{index:D2}";
        }
        
        // Set random spawn position
        Vector3 spawnPosition = new Vector3(
            Random.Range(spawnAreaMin.x, spawnAreaMax.x),
            spawnAreaMin.y,
            Random.Range(spawnAreaMin.z, spawnAreaMax.z)
        );
        entity.transform.position = spawnPosition;
        
        // Add physics collider (already has one from primitive)
        Collider collider = entity.GetComponent<Collider>();
        collider.material = CreatePhysicsMaterial();
        
        // Add and configure RandomMovingEntity component
        RandomMovingEntity movementComponent = entity.AddComponent<RandomMovingEntity>();
        movementComponent.entityType = entityType;
        movementComponent.directionChangeInterval = directionChangeInterval;
        movementComponent.movementBoundsMin = movementBoundsMin;
        movementComponent.movementBoundsMax = movementBoundsMax;
        
        // Set entity-specific properties
        if (entityType == RandomMovingEntity.EntityType.Technician)
        {
            movementComponent.moveSpeed = technicianSpeed;
            movementComponent.entityColor = GetRandomVariation(technicianBaseColor);
        }
        else
        {
            movementComponent.moveSpeed = supervisorSpeed;
            movementComponent.entityColor = GetRandomVariation(supervisorBaseColor);
        }
        
        Debug.Log($"✅ Spawned {entityType} at {spawnPosition}");
    }
    
    PhysicsMaterial CreatePhysicsMaterial()
    {
        PhysicsMaterial physicsMat = new PhysicsMaterial("EntityPhysics");
        physicsMat.bounciness = 0.8f;
        physicsMat.staticFriction = 0.1f;
        physicsMat.dynamicFriction = 0.1f;
        physicsMat.bounceCombine = PhysicsMaterialCombine.Maximum;
        physicsMat.frictionCombine = PhysicsMaterialCombine.Minimum;
        return physicsMat;
    }
    
    Color GetRandomVariation(Color baseColor)
    {
        float variation = 0.3f;
        return new Color(
            Mathf.Clamp01(baseColor.r + Random.Range(-variation, variation)),
            Mathf.Clamp01(baseColor.g + Random.Range(-variation, variation)),
            Mathf.Clamp01(baseColor.b + Random.Range(-variation, variation)),
            1f
        );
    }
    
    void SetupCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            GameObject cameraObj = new GameObject("Physics_Main_Camera");
            mainCamera = cameraObj.AddComponent<Camera>();
            cameraObj.tag = "MainCamera";
        }
        
        // Position camera to see all the action
        mainCamera.transform.position = new Vector3(0, 20, -15);
        mainCamera.transform.rotation = Quaternion.Euler(45, 0, 0);
        
        Debug.Log("✅ Camera positioned for physics scene");
    }
}
