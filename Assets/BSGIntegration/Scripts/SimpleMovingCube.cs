using UnityEngine;

/// <summary>
/// Creates a simple moving cube that moves around on the plane.
/// This is the simplest possible moving entity - guaranteed to work.
/// </summary>
public class SimpleMovingCube : MonoBehaviour
{
    [Header("SIMPLE MOVING CUBE")]
    [Tooltip("Create cube immediately")]
    public bool createImmediately = true;
    
    [Tooltip("Cube color")]
    public Color cubeColor = Color.red;
    
    [Tooltip("Movement speed")]
    public float speed = 8f;
    
    void Start()
    {
        if (createImmediately)
        {
            CreateMovingCube();
        }
    }
    
    void CreateMovingCube()
    {
        Debug.Log("🎯 CREATING SIMPLE MOVING CUBE!");
        
        // Create cube
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "SimpleMovingCube";
        
        // Position ON the green plane (touching ground)
        cube.transform.position = new Vector3(0, 0.5f, 0);
        cube.transform.localScale = Vector3.one * 0.8f;
        
        // Color the cube
        Renderer renderer = cube.GetComponent<Renderer>();
        Material material = new Material(Shader.Find("Unlit/Color"));
        material.color = cubeColor;
        renderer.material = material;
        
        // Add movement component
        CubeMovement movement = cube.AddComponent<CubeMovement>();
        movement.speed = speed;
        
        // FORCE START MOVEMENT IMMEDIATELY
        movement.ForceStartMovement();
        
        Debug.Log("✅ Created SimpleMovingCube with GUARANTEED movement!");
    }
    
    void Update()
    {
        // No input needed - cube is created automatically on Start
    }
}

/// <summary>
/// Simple movement for the cube - just moves in random directions.
/// GUARANTEED TO MOVE!
/// </summary>
public class CubeMovement : MonoBehaviour
{
    public float speed = 8f;
    
    private Vector3 direction;
    private float nextDirectionChange;
    private bool isMoving = false;
    
    void Start()
    {
        // Set random direction
        SetRandomDirection();
        nextDirectionChange = Time.time + 1.5f;
        
        Debug.Log($"🚀 CubeMovement started on {name}");
        
        // Start moving immediately
        StartMoving();
    }
    
    public void ForceStartMovement()
    {
        StartMoving();
        Debug.Log($"🔥 FORCED MOVEMENT START on {name}");
    }
    
    void StartMoving()
    {
        isMoving = true;
        SetRandomDirection();
        Debug.Log($"▶️ MOVEMENT STARTED on {name} with direction {direction} and speed {speed}");
    }
    
    void Update()
    {
        // ONLY MOVE IF isMoving IS TRUE
        if (!isMoving) return;
        
        // Change direction every 1.5 seconds
        if (Time.time >= nextDirectionChange)
        {
            SetRandomDirection();
            nextDirectionChange = Time.time + 1.5f;
        }
        
        // FORCE MOVE THE CUBE EVERY FRAME
        MoveCube();
        
        // Keep on plane
        KeepOnPlane();
        
        // Debug EVERY FRAME to see movement
        if (Time.frameCount % 30 == 0) // Every 0.5 seconds
        {
            Debug.Log($"🏃 {name} MOVING at position: {transform.position} with direction: {direction} and speed: {speed}");
        }
    }
    
    void SetRandomDirection()
    {
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)).normalized;
        
        Debug.Log($"🔄 {name} new direction: {direction}");
    }
    
    void MoveCube()
    {
        // FORCE MOVE using transform (no physics conflicts)
        Vector3 movement = direction * speed * Time.deltaTime;
        Vector3 newPosition = transform.position + movement;
        
        // FORCE the position change
        transform.position = newPosition;
        
        // Debug movement every frame for first 5 seconds
        if (Time.time < 5f)
        {
            Debug.Log($"🔥 FORCING MOVEMENT: Old pos: {transform.position - movement}, New pos: {newPosition}, Movement: {movement}");
        }
    }
    
    void KeepOnPlane()
    {
        Vector3 pos = transform.position;
        
        // Keep Y at 0.5 (touching ground)
        pos.y = 0.5f;
        
        // Bounce off boundaries
        if (pos.x > 10f || pos.x < -10f)
        {
            direction.x = -direction.x;
            pos.x = Mathf.Clamp(pos.x, -10f, 10f);
        }
        
        if (pos.z > 10f || pos.z < -10f)
        {
            direction.z = -direction.z;
            pos.z = Mathf.Clamp(pos.z, -10f, 10f);
        }
        
        transform.position = pos;
    }
}
