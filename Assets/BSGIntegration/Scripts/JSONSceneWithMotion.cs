using UnityEngine;

/// <summary>
/// Enhances the existing JSON scene with continuous motion for technicians.
/// This script works with the existing JSON scene generation and adds motion.
/// </summary>
public class JSONSceneWithMotion : MonoBehaviour
{
    [Header("JSON SCENE WITH MOTION")]
    [Tooltip("Add motion immediately after scene generation")]
    public bool addMotionOnStart = true;
    
    [Tooltip("Motion speed for all entities")]
    public float motionSpeed = 4f;
    
    [Tooltip("Show debug information")]
    public bool showDebug = true;
    
    void Start()
    {
        if (addMotionOnStart)
        {
            // Wait for JSON scene to be fully generated
            Invoke(nameof(EnhanceJSONSceneWithMotion), 1.5f);
        }
    }
    
    void EnhanceJSONSceneWithMotion()
    {
        if (showDebug)
            Debug.Log("🎯 JSON SCENE WITH MOTION - Enhancing existing scene with movement!");
        
        // Find all potential entities from the JSON
        FindAndEnhanceJSONEntities();
    }
    
    void FindAndEnhanceJSONEntities()
    {
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        int enhancedEntities = 0;
        
        foreach (GameObject obj in allObjects)
        {
            // Check if this is a JSON-created entity
            if (IsFromJSONScene(obj))
            {
                EnhanceEntityWithMotion(obj);
                enhancedEntities++;
            }
        }
        
        if (showDebug)
        {
            Debug.Log($"🎉 Enhanced {enhancedEntities} JSON entities with motion!");
            
            if (enhancedEntities == 0)
            {
                Debug.LogWarning("⚠️ No JSON entities found to enhance. Checking all objects...");
                ListAllSceneObjects();
            }
        }
    }
    
    bool IsFromJSONScene(GameObject obj)
    {
        string name = obj.name.ToLower();
        
        // Check for various possible names from JSON creation
        return name.Contains("agent") ||
               name.Contains("technician") ||
               name.Contains("supervisor") ||
               name.Contains("inspector") ||
               name.Contains("alex") ||
               name.Contains("maria") ||
               name.Contains("david") ||
               name.Contains("rodriguez") ||
               name.Contains("santos") ||
               name.Contains("kim");
    }
    
    void EnhanceEntityWithMotion(GameObject entity)
    {
        if (showDebug)
            Debug.Log($"🔧 Enhancing {entity.name} with motion");
        
        // Remove any existing motion components to avoid conflicts
        Component[] existingMotion = entity.GetComponents<Component>();
        foreach (Component comp in existingMotion)
        {
            if (comp.GetType().Name.Contains("Motion") || 
                comp.GetType().Name.Contains("Movement"))
            {
                if (comp != this)
                    DestroyImmediate(comp);
            }
        }
        
        // Add simple continuous motion
        SimpleContinuousMotion motion = entity.AddComponent<SimpleContinuousMotion>();
        motion.speed = motionSpeed;
        motion.showDebug = showDebug;
        
        // Ensure physics components
        SetupPhysicsForMotion(entity);
        
        if (showDebug)
            Debug.Log($"✅ Enhanced {entity.name} with continuous motion");
    }
    
    void SetupPhysicsForMotion(GameObject entity)
    {
        // Add Rigidbody if missing
        Rigidbody rb = entity.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = entity.AddComponent<Rigidbody>();
        }
        
        // Configure for motion
        rb.useGravity = false;
        rb.mass = 1f;
        rb.linearDamping = 1f;
        rb.angularDamping = 5f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        
        // Add collider if missing
        Collider col = entity.GetComponent<Collider>();
        if (col == null)
        {
            col = entity.AddComponent<CapsuleCollider>();
        }
    }
    
    void ListAllSceneObjects()
    {
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        Debug.Log("📋 All scene objects:");
        
        foreach (GameObject obj in allObjects)
        {
            Debug.Log($"   - {obj.name}");
        }
    }
    
    [ContextMenu("Enhance JSON Scene with Motion")]
    public void EnhanceJSONSceneWithMotionNow()
    {
        EnhanceJSONSceneWithMotion();
    }
    
    void Update()
    {
        // Press E to enhance scene with motion
        if (Input.GetKeyDown(KeyCode.E))
        {
            if (showDebug)
                Debug.Log("🎮 E key pressed - Enhancing JSON scene with motion!");
            EnhanceJSONSceneWithMotion();
        }
    }
}

/// <summary>
/// Simple continuous motion component for JSON entities.
/// </summary>
public class SimpleContinuousMotion : MonoBehaviour
{
    [Header("Simple Continuous Motion")]
    public float speed = 4f;
    public bool showDebug = true;
    
    private Rigidbody rb;
    private Vector3 direction;
    private float nextDirectionChange;
    private Vector3 startPos;
    
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        startPos = transform.position;
        
        // Set initial random direction
        SetRandomDirection();
        nextDirectionChange = Time.time + 2f;
        
        if (showDebug)
            Debug.Log($"🚀 SimpleContinuousMotion started on {name}");
    }
    
    void Update()
    {
        if (rb == null) return;
        
        // Change direction every 2 seconds
        if (Time.time >= nextDirectionChange)
        {
            SetRandomDirection();
            nextDirectionChange = Time.time + 2f + Random.Range(-0.5f, 0.5f);
        }
        
        // Apply movement
        Vector3 velocity = direction * speed;
        velocity.y = 0; // Keep on same Y level
        rb.linearVelocity = velocity;
        
        // Check boundaries
        CheckBounds();
        
        // Debug
        if (showDebug && Time.frameCount % 120 == 0)
        {
            Debug.Log($"🏃 {name} moving at {rb.linearVelocity.magnitude:F1} speed");
        }
    }
    
    void SetRandomDirection()
    {
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)).normalized;
        
        if (showDebug)
            Debug.Log($"🔄 {name} new direction: {direction}");
    }
    
    void CheckBounds()
    {
        Vector3 pos = transform.position;
        float distance = Vector3.Distance(new Vector3(pos.x, 0, pos.z), new Vector3(startPos.x, 0, startPos.z));
        
        if (distance > 6f)
        {
            // Turn back toward start
            Vector3 toStart = (startPos - pos).normalized;
            direction = new Vector3(toStart.x, 0, toStart.z).normalized;
            
            if (showDebug)
                Debug.Log($"🔄 {name} turning back to start");
        }
    }
}
