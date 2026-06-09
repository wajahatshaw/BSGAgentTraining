using UnityEngine;

public class RandomMovingEntity : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 3f;
    public float directionChangeInterval = 2f;
    
    [Header("Entity Settings")]
    public EntityType entityType = EntityType.Technician;
    public Color entityColor = Color.blue;
    
    [Header("Bounds")]
    public Vector3 movementBoundsMin = new Vector3(-10f, 0f, -10f);
    public Vector3 movementBoundsMax = new Vector3(10f, 0f, 10f);
    
    private Rigidbody rb;
    private Vector3 currentDirection;
    private float nextDirectionChangeTime;
    private Renderer entityRenderer;
    
    public enum EntityType
    {
        Technician,
        Supervisor
    }
    
    void Start()
    {
        // Get or add Rigidbody
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }
        
        // Configure Rigidbody
        rb.useGravity = true; // Use gravity to stay on plane
        rb.freezeRotation = true; // Prevent tumbling
        rb.linearDamping = 0.5f; // Some air resistance for control
        rb.mass = 1f; // Standard mass
        
        // Get renderer for color - Apply color immediately
        entityRenderer = GetComponent<Renderer>();
        ApplyEntityColor();
        
        // Set initial random direction
        GenerateNewDirection();
        
        Debug.Log($"✅ {entityType} spawned at {transform.position} with speed {moveSpeed}");
    }
    
    void Update()
    {
        // Check if it's time to change direction
        if (Time.time >= nextDirectionChangeTime)
        {
            GenerateNewDirection();
        }
        
        // Apply movement (keep Y velocity for gravity)
        Vector3 newVelocity = currentDirection * moveSpeed;
        newVelocity.y = rb.linearVelocity.y; // Preserve gravity
        rb.linearVelocity = newVelocity;
        
        // Check bounds and bounce if necessary
        CheckBoundsAndBounce();
    }
    
    void GenerateNewDirection()
    {
        // Generate random direction on XZ plane
        float angle = Random.Range(0f, 2f * Mathf.PI);
        currentDirection = new Vector3(
            Mathf.Cos(angle),
            0f,
            Mathf.Sin(angle)
        ).normalized;
        
        // Set next direction change time
        nextDirectionChangeTime = Time.time + directionChangeInterval + Random.Range(-0.5f, 0.5f);
        
        Debug.Log($"🔄 {entityType} {name} changed direction to {currentDirection}");
    }
    
    void CheckBoundsAndBounce()
    {
        Vector3 position = transform.position;
        Vector3 velocity = rb.linearVelocity;
        bool bounced = false;
        
        // Check X boundaries
        if (position.x <= movementBoundsMin.x && velocity.x < 0)
        {
            velocity.x = Mathf.Abs(velocity.x);
            currentDirection.x = Mathf.Abs(currentDirection.x);
            bounced = true;
            Debug.Log($"🏀 {name} bounced off left wall");
        }
        else if (position.x >= movementBoundsMax.x && velocity.x > 0)
        {
            velocity.x = -Mathf.Abs(velocity.x);
            currentDirection.x = -Mathf.Abs(currentDirection.x);
            bounced = true;
            Debug.Log($"🏀 {name} bounced off right wall");
        }
        
        // Check Z boundaries
        if (position.z <= movementBoundsMin.z && velocity.z < 0)
        {
            velocity.z = Mathf.Abs(velocity.z);
            currentDirection.z = Mathf.Abs(currentDirection.z);
            bounced = true;
            Debug.Log($"🏀 {name} bounced off back wall");
        }
        else if (position.z >= movementBoundsMax.z && velocity.z > 0)
        {
            velocity.z = -Mathf.Abs(velocity.z);
            currentDirection.z = -Mathf.Abs(currentDirection.z);
            bounced = true;
            Debug.Log($"🏀 {name} bounced off front wall");
        }
        
        // Keep Y velocity for gravity (don't reset to 0)
        // velocity.y = rb.linearVelocity.y;
        
        // Apply bounced velocity
        rb.linearVelocity = velocity;
        
        // If bounced, delay next direction change
        if (bounced)
        {
            nextDirectionChangeTime = Time.time + 1f; // Wait 1 second after bounce
        }
    }
    
    void OnCollisionEnter(Collision collision)
    {
        // Additional collision handling for physical walls
        if (collision.gameObject.CompareTag("Wall"))
        {
            Vector3 normal = collision.contacts[0].normal;
            
            // Reflect velocity based on collision normal
            Vector3 reflectedVelocity = Vector3.Reflect(rb.linearVelocity, normal);
            reflectedVelocity.y = 0f; // Keep on XZ plane
            
            rb.linearVelocity = reflectedVelocity;
            currentDirection = reflectedVelocity.normalized;
            
            Debug.Log($"🏀 {name} collided with wall, reflected velocity: {reflectedVelocity}");
        }
    }
    
    void ApplyEntityColor()
    {
        if (entityRenderer != null)
        {
            // Create a working material with proper color
            Material material = new Material(Shader.Find("Standard"));
            material.color = entityColor;
            material.SetFloat("_Metallic", 0.1f);
            material.SetFloat("_Glossiness", 0.3f);
            
            // Apply the material
            entityRenderer.material = material;
            
            Debug.Log($"🎨 Applied color {entityColor} to {name}");
        }
        else
        {
            Debug.LogWarning($"⚠️ No renderer found on {name}");
        }
    }
    
    // Public method to update color from external scripts
    public void SetEntityColor(Color newColor)
    {
        entityColor = newColor;
        ApplyEntityColor();
    }
    
    void OnDrawGizmos()
    {
        // Draw movement bounds
        Gizmos.color = entityColor;
        Vector3 center = (movementBoundsMin + movementBoundsMax) * 0.5f;
        Vector3 size = movementBoundsMax - movementBoundsMin;
        Gizmos.DrawWireCube(center, size);
        
        // Draw current direction
        if (Application.isPlaying)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(transform.position, currentDirection * 2f);
        }
    }
}
