using UnityEngine;

/// <summary>
/// Simple script that randomly changes position every few seconds to simulate movement.
/// This doesn't use physics or Rigidbody - just direct position changes.
/// Safe for scene UI - only affects technicians and supervisors.
/// </summary>
public class SimpleRandomPositionMovement : MonoBehaviour
{
    [Header("SIMPLE RANDOM POSITION MOVEMENT")]
    [Tooltip("Start movement immediately")]
    public bool startImmediately = true;
    
    [Tooltip("How often to change position (in seconds)")]
    public float positionChangeInterval = 2f;
    
    [Tooltip("Maximum distance to move from current position")]
    public float maxMoveDistance = 5f;
    
    [Tooltip("Keep entities on ground level")]
    public bool keepOnGround = true;
    
    [Tooltip("Ground Y level")]
    public float groundY = 0f;
    
    [Tooltip("Show debug information")]
    public bool showDebug = true;
    
    void Start()
    {
        if (startImmediately)
        {
            Invoke(nameof(StartRandomMovement), 0.5f);
        }
    }
    
    void StartRandomMovement()
    {
        if (showDebug)
            Debug.Log("🎯 SIMPLE RANDOM POSITION MOVEMENT - Starting position changes!");
        
        // Find all technicians and supervisors
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        int entitiesFound = 0;
        
        foreach (GameObject obj in allObjects)
        {
            if (IsEntity(obj))
            {
                AddPositionMovementToEntity(obj);
                entitiesFound++;
            }
        }
        
        if (showDebug)
            Debug.Log($"🎉 Added random position movement to {entitiesFound} entities!");
    }
    
    bool IsEntity(GameObject obj)
    {
        string name = obj.name.ToLower();
        return name.Contains("technician") || 
               name.Contains("supervisor") ||
               name.Contains("simple_technician") ||
               name.Contains("simple_supervisor");
    }
    
    void AddPositionMovementToEntity(GameObject obj)
    {
        // Add our position movement component
        PositionChanger changer = obj.GetComponent<PositionChanger>();
        if (changer == null)
        {
            changer = obj.AddComponent<PositionChanger>();
        }
        
        // Configure the position changer
        changer.positionChangeInterval = positionChangeInterval;
        changer.maxMoveDistance = maxMoveDistance;
        changer.keepOnGround = keepOnGround;
        changer.groundY = groundY;
        changer.showDebug = showDebug;
        
        if (showDebug)
            Debug.Log($"✅ Added PositionChanger to {obj.name}");
    }
    
    [ContextMenu("Start Random Movement Now")]
    public void StartRandomMovementNow()
    {
        StartRandomMovement();
    }
    
    void Update()
    {
        // Press R to restart random movement
        if (Input.GetKeyDown(KeyCode.R))
        {
            if (showDebug)
                Debug.Log("🎮 R key pressed - Restarting random movement!");
            StartRandomMovement();
        }
    }
}

/// <summary>
/// Component that randomly changes position every few seconds.
/// </summary>
public class PositionChanger : MonoBehaviour
{
    [Header("Position Changer Settings")]
    public float positionChangeInterval = 2f;
    public float maxMoveDistance = 5f;
    public bool keepOnGround = true;
    public float groundY = 0f;
    public bool showDebug = true;
    
    private Vector3 startPosition;
    private float nextPositionChange;
    private bool isMoving = false;
    
    void Start()
    {
        startPosition = transform.position;
        nextPositionChange = Time.time + positionChangeInterval;
        
        if (showDebug)
            Debug.Log($"🚀 PositionChanger started on {name}");
    }
    
    void Update()
    {
        // Check if it's time to change position
        if (Time.time >= nextPositionChange)
        {
            ChangeToRandomPosition();
            nextPositionChange = Time.time + positionChangeInterval + Random.Range(-0.5f, 0.5f);
        }
        
        // Debug info
        if (showDebug && Time.frameCount % 120 == 0) // Every 2 seconds
        {
            Debug.Log($"🏃 {name} at position: {transform.position}");
        }
    }
    
    void ChangeToRandomPosition()
    {
        // Generate random position within bounds
        Vector3 randomOffset = new Vector3(
            Random.Range(-maxMoveDistance, maxMoveDistance),
            0,
            Random.Range(-maxMoveDistance, maxMoveDistance)
        );
        
        Vector3 newPosition = startPosition + randomOffset;
        
        // Keep on ground if enabled
        if (keepOnGround)
        {
            newPosition.y = groundY;
        }
        
        // Apply the new position
        transform.position = newPosition;
        
        if (showDebug)
            Debug.Log($"🔄 {name} moved to: {newPosition}");
    }
    
    [ContextMenu("Change Position Now")]
    public void ChangePositionNow()
    {
        ChangeToRandomPosition();
    }
}
