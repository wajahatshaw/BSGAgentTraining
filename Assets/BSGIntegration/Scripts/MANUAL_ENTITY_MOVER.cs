using UnityEngine;
using System.Collections;

/// <summary>
/// MANUAL ENTITY MOVER - Manually finds entities by exact name and moves them.
/// This will work because it targets specific objects directly.
/// </summary>
public class MANUAL_ENTITY_MOVER : MonoBehaviour
{
    [Header("MANUAL ENTITY MOVER")]
    public bool MOVE_NOW = true;
    
    void Start()
    {
        if (MOVE_NOW)
        {
            StartCoroutine(MoveEntitiesManually());
        }
    }
    
    IEnumerator MoveEntitiesManually()
    {
        yield return new WaitForSeconds(2f); // Wait for scene
        
        Debug.Log("🎯 MANUAL ENTITY MOVER - Starting manual movement!");
        
        // List all objects first
        ListAllObjects();
        
        while (true)
        {
            MoveSpecificEntities();
            yield return new WaitForSeconds(1f);
        }
    }
    
    void ListAllObjects()
    {
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        Debug.Log($"📋 FOUND {allObjects.Length} TOTAL OBJECTS:");
        
        foreach (GameObject obj in allObjects)
        {
            Debug.Log($"   - {obj.name}");
        }
    }
    
    void MoveSpecificEntities()
    {
        // Try to find entities by various possible names
        string[] possibleNames = {
            "SIMPLE_Technician_01",
            "SIMPLE_Technician_02", 
            "SIMPLE_Supervisor_01",
            "SIMPLE_Supervisor_02",
            "agent_technician_A",
            "agent_supervisor_B",
            "agent_inspector_C",
            "Alex Rodriguez",
            "Maria Santos",
            "David Kim"
        };
        
        foreach (string entityName in possibleNames)
        {
            GameObject entity = GameObject.Find(entityName);
            if (entity != null)
            {
                MoveEntityDirectly(entity);
            }
        }
        
        // Also try finding by contains
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        foreach (GameObject obj in allObjects)
        {
            if (obj.name.ToLower().Contains("technician") || 
                obj.name.ToLower().Contains("supervisor"))
            {
                MoveEntityDirectly(obj);
            }
        }
    }
    
    void MoveEntityDirectly(GameObject entity)
    {
        Vector3 currentPos = entity.transform.position;
        
        // Move to a new random position
        Vector3 newPos = new Vector3(
            currentPos.x + Random.Range(-2f, 2f),
            currentPos.y,
            currentPos.z + Random.Range(-2f, 2f)
        );
        
        // DIRECT POSITION CHANGE
        entity.transform.position = newPos;
        
        Debug.Log($"🚀 MOVED {entity.name} from {currentPos} to {newPos}");
    }
    
    void Update()
    {
        // No input needed - movement happens automatically via coroutine
    }
}
