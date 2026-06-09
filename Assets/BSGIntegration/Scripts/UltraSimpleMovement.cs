using UnityEngine;

/// <summary>
/// Ultra simple movement - just randomly changes position every 2 seconds.
/// No physics, no Rigidbody, no conflicts. Just position changes.
/// </summary>
public class UltraSimpleMovement : MonoBehaviour
{
    [Header("ULTRA SIMPLE MOVEMENT")]
    [Tooltip("Start immediately")]
    public bool startImmediately = true;
    
    [Tooltip("How fast to change positions")]
    public float changeSpeed = 2f;
    
    [Tooltip("Show debug info")]
    public bool showDebug = true;
    
    void Start()
    {
        if (startImmediately)
        {
            Invoke(nameof(StartUltraSimpleMovement), 0.3f);
        }
    }
    
    void StartUltraSimpleMovement()
    {
        if (showDebug)
            Debug.Log("🎯 ULTRA SIMPLE MOVEMENT - Starting position changes!");
        
        // Find all entities
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        int movedEntities = 0;
        
        foreach (GameObject obj in allObjects)
        {
            if (IsEntity(obj))
            {
                StartCoroutine(MoveEntityRandomly(obj));
                movedEntities++;
            }
        }
        
        if (showDebug)
            Debug.Log($"🎉 Started random movement for {movedEntities} entities!");
    }
    
    bool IsEntity(GameObject obj)
    {
        string name = obj.name.ToLower();
        return name.Contains("technician") || 
               name.Contains("supervisor") ||
               name.Contains("simple_technician") ||
               name.Contains("simple_supervisor");
    }
    
    System.Collections.IEnumerator MoveEntityRandomly(GameObject entity)
    {
        Vector3 startPos = entity.transform.position;
        
        while (true)
        {
            // Wait for change speed
            yield return new WaitForSeconds(changeSpeed);
            
            // Generate random position near start position
            Vector3 randomPos = startPos + new Vector3(
                Random.Range(-3f, 3f),
                0,
                Random.Range(-3f, 3f)
            );
            
            // Keep on ground level
            randomPos.y = startPos.y;
            
            // Move the entity
            entity.transform.position = randomPos;
            
            if (showDebug)
                Debug.Log($"🏃 {entity.name} moved to: {randomPos}");
        }
    }
    
    [ContextMenu("Start Movement Now")]
    public void StartMovementNow()
    {
        StartUltraSimpleMovement();
    }
    
    void Update()
    {
        // Press U to start movement
        if (Input.GetKeyDown(KeyCode.U))
        {
            if (showDebug)
                Debug.Log("🎮 U key pressed - Starting ultra simple movement!");
            StartUltraSimpleMovement();
        }
    }
}
