using UnityEngine;
using System.Collections;

/// <summary>
/// TELEPORT ENTITIES - Just teleports entities to random positions every second.
/// This is the SIMPLEST possible "movement" - just position jumps.
/// </summary>
public class TELEPORT_ENTITIES : MonoBehaviour
{
    [Header("TELEPORT ENTITIES - SIMPLEST MOVEMENT")]
    public bool START_TELEPORTING = true;
    public float TELEPORT_INTERVAL = 1f;
    public float TELEPORT_RANGE = 3f;
    
    void Start()
    {
        if (START_TELEPORTING)
        {
            StartCoroutine(TeleportAllEntities());
        }
    }
    
    IEnumerator TeleportAllEntities()
    {
        yield return new WaitForSeconds(1f); // Wait for scene to load
        
        Debug.Log("🚀 TELEPORT ENTITIES - Starting teleportation!");
        
        // Find all entities
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        while (true)
        {
            foreach (GameObject obj in allObjects)
            {
                if (obj != null && IsEntity(obj))
                {
                    TeleportEntity(obj);
                }
            }
            
            yield return new WaitForSeconds(TELEPORT_INTERVAL);
        }
    }
    
    bool IsEntity(GameObject obj)
    {
        string name = obj.name.ToLower();
        return name.Contains("technician") || 
               name.Contains("supervisor") ||
               name.Contains("simple");
    }
    
    void TeleportEntity(GameObject entity)
    {
        Vector3 currentPos = entity.transform.position;
        
        // Generate random offset
        Vector3 randomOffset = new Vector3(
            Random.Range(-TELEPORT_RANGE, TELEPORT_RANGE),
            0,
            Random.Range(-TELEPORT_RANGE, TELEPORT_RANGE)
        );
        
        Vector3 newPos = currentPos + randomOffset;
        newPos.y = currentPos.y; // Keep same height
        
        // TELEPORT - instant position change
        entity.transform.position = newPos;
        
        Debug.Log($"📍 TELEPORTED {entity.name} to {newPos}");
    }
    
    void Update()
    {
        // No input needed - teleportation starts automatically
    }
}
