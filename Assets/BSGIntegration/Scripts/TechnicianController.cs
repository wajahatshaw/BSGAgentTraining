using UnityEngine;

/// <summary>
/// Controls a Technician GameObject with gravity-based landing and ground wandering behavior.
/// First drops with gravity until ground contact, then switches to continuous random movement.
/// </summary>
public class TechnicianController : MonoBehaviour
{
    [Header("Movement Settings")]
    [Tooltip("Speed of movement when wandering on ground")]
    public float movementSpeed = 0.1f;
    
    [Tooltip("How often to change direction (in seconds)")]
    public float directionChangeInterval = 3f;
    
    [Tooltip("Maximum distance from center before turning around")]
    public float boundaryDistance = 10f;
    
    [Header("Ground Detection")]
    [Tooltip("Layer mask for ground detection")]
    public LayerMask groundLayerMask = 1; // Default layer
    
    [Tooltip("Distance to check for ground below")]
    public float groundCheckDistance = 0.1f;
    
    [Tooltip("Offset for ground raycast from object center")]
    public float groundCheckOffset = 0.5f;
    
    [Header("Physics Settings")]
    [Tooltip("Mass of the Rigidbody")]
    public float mass = 1f;
    
    [Tooltip("Drag when moving on ground")]
    public float groundDrag = 2f;
    
    [Header("Debug")]
    [Tooltip("Show debug information in console")]
    public bool enableDebugLogs = true;
    
    [Tooltip("Draw debug rays in scene view")]
    public bool showDebugRays = true;
    
    [Tooltip("Skip falling phase and start wandering immediately")]
    public bool skipFallingPhase = false;
    
    [Header("Agent Information")]
    [Tooltip("Unique identifier for this agent")]
    public string agentId = "";
    
    // Agent profile data (for compatibility with existing systems)
    [System.NonSerialized]
    public object agentProfile;
    
    // Private variables
    private Rigidbody rb;
    private Collider col;
    private bool hasLanded = false;
    private bool isGrounded = false;
    private Vector3 currentDirection;
    private float nextDirectionChange;
    private float groundY;
    private Vector3 startPosition;
    
    // States
    private enum MovementState
    {
        Falling,
        Landing,
        Wandering
    }
    private MovementState currentState = MovementState.Falling;
    
    void Start()
    {
        InitializeComponents();
        SetupPhysics();
        startPosition = transform.position;
        
        if (enableDebugLogs)
            Debug.Log($"🚀 TechnicianController initialized on {name}");
        
        // Check if we should skip falling phase
        if (skipFallingPhase)
        {
            if (enableDebugLogs)
                Debug.Log($"⚡ {name} skipping falling phase - starting wandering immediately!");
            ForceStartWandering();
        }
    }
    
    void Update()
    {
        switch (currentState)
        {
            case MovementState.Falling:
                HandleFalling();
                break;
            case MovementState.Landing:
                HandleLanding();
                break;
            case MovementState.Wandering:
                HandleWandering();
                break;
        }
        
        if (showDebugRays)
            DrawDebugRays();
    }
    
    void FixedUpdate()
    {
        if (currentState == MovementState.Wandering)
        {
            ApplyGroundMovement();
        }
    }
    
    #region Initialization
    
    void InitializeComponents()
    {
        // Get or add Rigidbody
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            if (enableDebugLogs)
                Debug.Log($"📦 Added Rigidbody to {name}");
        }
        
        // Get collider
        col = GetComponent<Collider>();
        if (col == null)
        {
            col = gameObject.AddComponent<BoxCollider>();
            if (enableDebugLogs)
                Debug.Log($"📦 Added BoxCollider to {name}");
        }
    }
    
    void SetupPhysics()
    {
        rb.mass = mass;
        rb.useGravity = true;
        rb.freezeRotation = true; // Prevent tumbling
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
    }
    
    #endregion
    
    #region State Handlers
    
    void HandleFalling()
    {
        // Check if we've hit the ground
        if (IsGroundedCheck())
        {
            TransitionToLanding();
        }
    }
    
    void HandleLanding()
    {
        // Disable gravity and prepare for wandering
        rb.useGravity = false;
        rb.linearVelocity = Vector3.zero; // Stop all movement
        rb.linearDamping = groundDrag;
        
        // Record ground position
        groundY = transform.position.y;
        
        // Initialize wandering
        GenerateNewDirection();
        nextDirectionChange = Time.time + directionChangeInterval;
        
        currentState = MovementState.Wandering;
        hasLanded = true;
        
        if (enableDebugLogs)
            Debug.Log($"🎯 {name} landed at Y: {groundY:F2}, starting wandering mode");
    }
    
    void HandleWandering()
    {
        // Check if it's time to change direction
        if (Time.time >= nextDirectionChange)
        {
            GenerateNewDirection();
            nextDirectionChange = Time.time + directionChangeInterval + Random.Range(-0.5f, 0.5f);
        }
        
        // Check boundaries and obstacles
        CheckBoundariesAndObstacles();
        
        // Maintain ground position
        MaintainGroundPosition();
        
        // Debug logging
        if (enableDebugLogs && Time.frameCount % 180 == 0) // Every 3 seconds
        {
            Debug.Log($"🏃 {name} wandering - Speed: {rb.linearVelocity.magnitude:F1}, Direction: {currentDirection}");
        }
    }
    
    #endregion
    
    #region Ground Detection
    
    bool IsGroundedCheck()
    {
        Vector3 rayStart = transform.position + Vector3.up * groundCheckOffset;
        Vector3 rayDirection = Vector3.down;
        float rayDistance = groundCheckOffset + groundCheckDistance;
        
        RaycastHit hit;
        bool grounded = Physics.Raycast(rayStart, rayDirection, out hit, rayDistance, groundLayerMask);
        
        if (grounded && !isGrounded)
        {
            if (enableDebugLogs)
                Debug.Log($"🎯 {name} detected ground: {hit.collider.name}");
        }
        
        isGrounded = grounded;
        return grounded;
    }
    
    void MaintainGroundPosition()
    {
        if (hasLanded)
        {
            Vector3 pos = transform.position;
            pos.y = groundY;
            transform.position = pos;
        }
    }
    
    #endregion
    
    #region Movement Logic
    
    void GenerateNewDirection()
    {
        // Generate random direction on XZ plane
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        currentDirection = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)).normalized;
        
        if (enableDebugLogs)
            Debug.Log($"🔄 {name} new direction: {currentDirection}");
    }
    
    void ApplyGroundMovement()
    {
        if (currentState != MovementState.Wandering) return;
        
        // Apply horizontal movement
        Vector3 targetVelocity = currentDirection * movementSpeed;
        targetVelocity.y = 0; // No vertical movement
        
        rb.linearVelocity = targetVelocity;
    }
    
    void CheckBoundariesAndObstacles()
    {
        Vector3 currentPos = transform.position;
        Vector3 startPos = startPosition;
        
        // Check distance from start position
        float distanceFromStart = Vector3.Distance(new Vector3(currentPos.x, 0, currentPos.z), 
                                                  new Vector3(startPos.x, 0, startPos.z));
        
        if (distanceFromStart > boundaryDistance)
        {
            // Turn towards center
            Vector3 directionToCenter = (startPos - currentPos).normalized;
            currentDirection = new Vector3(directionToCenter.x, 0, directionToCenter.z).normalized;
            
            if (enableDebugLogs)
                Debug.Log($"🔄 {name} hit boundary, turning toward center");
        }
        
        // Raycast forward to check for obstacles
        CheckForObstacles();
    }
    
    void CheckForObstacles()
    {
        Vector3 rayStart = transform.position + Vector3.up * 0.5f;
        Vector3 rayDirection = currentDirection;
        float rayDistance = 2f;
        
        RaycastHit hit;
        if (Physics.Raycast(rayStart, rayDirection, out hit, rayDistance))
        {
            // Avoid obstacles by turning
            if (!hit.collider.CompareTag("Ground") && hit.collider.gameObject != gameObject)
            {
                // Turn 90 degrees randomly left or right
                float turnAngle = Random.Range(0f, 1f) > 0.5f ? 90f : -90f;
                currentDirection = Quaternion.AngleAxis(turnAngle, Vector3.up) * currentDirection;
                
                if (enableDebugLogs)
                    Debug.Log($"🚧 {name} avoiding obstacle: {hit.collider.name}");
            }
        }
    }
    
    void TransitionToLanding()
    {
        currentState = MovementState.Landing;
        
        if (enableDebugLogs)
            Debug.Log($"🎯 {name} transitioning to landing");
    }
    
    #endregion
    
    #region Collision Events
    
    void OnCollisionEnter(Collision collision)
    {
        if (currentState == MovementState.Falling)
        {
            // Check if we hit the ground
            if (collision.gameObject.CompareTag("Ground") || 
                collision.gameObject.name.ToLower().Contains("ground") ||
                collision.gameObject.name.ToLower().Contains("plane") ||
                collision.gameObject.name.ToLower().Contains("floor"))
            {
                if (enableDebugLogs)
                    Debug.Log($"🎯 {name} collision with ground: {collision.gameObject.name}");
                
                TransitionToLanding();
            }
        }
        else if (currentState == MovementState.Wandering)
        {
            // Bounce off walls and obstacles
            if (collision.gameObject.name.ToLower().Contains("wall") ||
                collision.gameObject.CompareTag("Wall"))
            {
                Vector3 reflection = Vector3.Reflect(currentDirection, collision.contacts[0].normal);
                currentDirection = new Vector3(reflection.x, 0, reflection.z).normalized;
                
                if (enableDebugLogs)
                    Debug.Log($"🏐 {name} bounced off: {collision.gameObject.name}");
            }
        }
    }
    
    #endregion
    
    #region Debug
    
    void DrawDebugRays()
    {
        if (!showDebugRays) return;
        
        // Ground check ray
        Vector3 rayStart = transform.position + Vector3.up * groundCheckOffset;
        Debug.DrawRay(rayStart, Vector3.down * (groundCheckOffset + groundCheckDistance), Color.green);
        
        // Forward obstacle check ray
        if (currentState == MovementState.Wandering)
        {
            Vector3 forwardRayStart = transform.position + Vector3.up * 0.5f;
            Debug.DrawRay(forwardRayStart, currentDirection * 2f, Color.red);
        }
    }
    
    void OnDrawGizmosSelected()
    {
        // Draw boundary sphere
        Gizmos.color = Color.yellow;
        Vector3 center = Application.isPlaying ? startPosition : transform.position;
        Gizmos.DrawWireSphere(center, boundaryDistance);
        
        // Draw ground check
        Gizmos.color = Color.green;
        Vector3 rayStart = transform.position + Vector3.up * groundCheckOffset;
        Gizmos.DrawRay(rayStart, Vector3.down * (groundCheckOffset + groundCheckDistance));
        
        // Draw current direction
        if (Application.isPlaying && currentState == MovementState.Wandering)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(transform.position, currentDirection * 2f);
        }
    }
    
    #endregion
    
    #region Public Methods
    
    /// <summary>
    /// Force the technician to start wandering immediately (skip falling)
    /// </summary>
    public void ForceStartWandering()
    {
        groundY = transform.position.y;
        TransitionToLanding();
        
        if (enableDebugLogs)
            Debug.Log($"🚀 {name} forced to start wandering");
    }
    
    /// <summary>
    /// Reset the technician to falling state
    /// </summary>
    public void ResetToFalling()
    {
        currentState = MovementState.Falling;
        hasLanded = false;
        isGrounded = false;
        rb.useGravity = true;
        rb.linearDamping = 0f;
        
        if (enableDebugLogs)
            Debug.Log($"🔄 {name} reset to falling state");
    }
    
    /// <summary>
    /// Get current movement state
    /// </summary>
    public string GetCurrentState()
    {
        return currentState.ToString();
    }
    
    /// <summary>
    /// Set agent information (for compatibility with existing systems)
    /// </summary>
    public void SetAgentInfo(string id, object profile)
    {
        agentId = id;
        agentProfile = profile;
        
        if (enableDebugLogs)
            Debug.Log($"🆔 {name} agent info set - ID: {agentId}");
    }
    
    /// <summary>
    /// Get agent ID
    /// </summary>
    public string GetAgentId()
    {
        return agentId;
    }
    
    /// <summary>
    /// Get agent profile
    /// </summary>
    public object GetAgentProfile()
    {
        return agentProfile;
    }
    
    #endregion
}
