using UnityEngine;

/// <summary>
/// SIMPLE FOUR ENTITY MOVEMENT ENHANCER - Enhances the Simple Four Entity System
/// This script works WITH Simple Four Entity System to make entities move better
/// </summary>
public class SimpleFourEntityMovementEnhancer : MonoBehaviour
{
    [Header("SIMPLE FOUR ENTITY MOVEMENT ENHANCER")]
    [Tooltip("Start enhancing immediately")]
    public bool enhanceNow = true;
    
    [Tooltip("Enhanced movement speed")]
    public float enhancedSpeed = 12f;
    
    [Tooltip("Check for entities every few seconds")]
    public bool continuousEnhancement = true;
    
    private float lastEnhancementTime;
    
    void Start()
    {
        if (enhanceNow)
        {
            // Wait for Simple Four Entity System to create entities
            Invoke(nameof(EnhanceEntityMovement), 3f);
        }
    }
    
    void Update()
    {
        // Continuously enhance entities if enabled
        if (continuousEnhancement && Time.time - lastEnhancementTime > 4f)
        {
            EnhanceEntityMovement();
            lastEnhancementTime = Time.time;
        }
        
        // Press E to manually enhance
        if (Input.GetKeyDown(KeyCode.E))
        {
            EnhanceEntityMovement();
        }
    }
    
    void EnhanceEntityMovement()
    {
        Debug.Log("🔧 Enhancing Simple Four Entity System movement...");
        
        // Find entities created by Simple Four Entity System
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        int enhancedCount = 0;
        
        foreach (GameObject obj in allObjects)
        {
            if (IsSimpleFourEntity(obj))
            {
                EnhanceThisEntity(obj);
                enhancedCount++;
            }
        }
        
        Debug.Log($"✅ Enhanced {enhancedCount} Simple Four Entity System entities!");
    }
    
    void EnhanceThisEntity(GameObject entity)
    {
        string entityName = entity.name;
        Debug.Log($"🔧 Enhancing {entityName}...");
        
        // Check if already enhanced
        SimpleFourEntityEnhancement existingEnhancement = entity.GetComponent<SimpleFourEntityEnhancement>();
        if (existingEnhancement != null)
        {
            // Update speed
            existingEnhancement.enhancedSpeed = enhancedSpeed;
            return;
        }
        
        // Get existing ContinuousMovement component
        ContinuousMovement existingMovement = entity.GetComponent<ContinuousMovement>();
        if (existingMovement != null)
        {
            // Enhance the existing movement
            existingMovement.speed = enhancedSpeed;
            existingMovement.directionChangeInterval = 1.5f; // Faster direction changes
            existingMovement.randomIntensity = 0.03f; // Much lower random movement
            
            Debug.Log($"✅ Enhanced existing ContinuousMovement on {entityName}");
        }
        else
        {
            // No existing movement, add our enhancement
            SimpleFourEntityEnhancement enhancement = entity.AddComponent<SimpleFourEntityEnhancement>();
            enhancement.enhancedSpeed = enhancedSpeed;
            
            Debug.Log($"✅ Added SimpleFourEntityEnhancement to {entityName}");
        }
    }
    
    bool IsSimpleFourEntity(GameObject obj)
    {
        string name = obj.name.ToLower();
        return name.Contains("simple_technician") || 
               name.Contains("simple_supervisor");
    }
    
    [ContextMenu("Enhance Entity Movement")]
    public void EnhanceMovementNow()
    {
        EnhanceEntityMovement();
    }
}

/// <summary>
/// Enhancement component for Simple Four Entity System entities
/// </summary>
public class SimpleFourEntityEnhancement : MonoBehaviour
{
    public float enhancedSpeed = 12f;
    
    private Rigidbody rb;
    private Vector3 direction;
    private float nextChange;
    
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        
        // Set random direction
        SetRandomDirection();
        
        // Start moving immediately
        rb.linearVelocity = direction * enhancedSpeed;
        
        Debug.Log($"🚀 {name} Simple Four Entity enhancement started with speed {enhancedSpeed}");
    }
    
    void Update()
    {
        // Change direction every 1.5 seconds
        if (Time.time > nextChange)
        {
            SetRandomDirection();
        }
        
        // Apply enhanced movement
        Vector3 velocity = direction * enhancedSpeed;
        velocity.y = rb.linearVelocity.y; // Preserve gravity if any
        rb.linearVelocity = velocity;
        
        // Debug every 2 seconds
        if (Time.frameCount % 120 == 0)
        {
            Debug.Log($"🏃 {name} enhanced movement: {rb.linearVelocity.magnitude:F1} speed");
        }
    }
    
    void SetRandomDirection()
    {
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)).normalized;
        nextChange = Time.time + 1.5f;
        
        Debug.Log($"🔄 {name} enhanced direction: {direction}");
    }
}
