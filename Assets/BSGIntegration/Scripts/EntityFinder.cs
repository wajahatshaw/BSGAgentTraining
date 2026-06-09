using UnityEngine;

/// <summary>
/// ENTITY FINDER - Finds and lists all entities in the scene
/// This script will help us debug what entities exist and where they are
/// </summary>
public class EntityFinder : MonoBehaviour
{
    [Header("ENTITY FINDER")]
    [Tooltip("Find entities immediately")]
    public bool findImmediately = true;
    
    void Start()
    {
        if (findImmediately)
        {
            Invoke(nameof(FindAllEntities), 1f);
        }
    }
    
    void FindAllEntities()
    {
        Debug.Log("🔍 ENTITY FINDER - Searching for all entities...");
        
        // Find all GameObjects
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        Debug.Log($"📊 Total GameObjects in scene: {allObjects.Length}");
        
        int entityCount = 0;
        int technicianCount = 0;
        int supervisorCount = 0;
        int coloredObjectCount = 0;
        
        foreach (GameObject obj in allObjects)
        {
            string name = obj.name;
            
            // Check for specific entity names
            if (name.Contains("Technician"))
            {
                technicianCount++;
                Debug.Log($"🎯 Found Technician: {name} at {obj.transform.position}");
                LogEntityDetails(obj);
            }
            else if (name.Contains("Supervisor"))
            {
                supervisorCount++;
                Debug.Log($"🎯 Found Supervisor: {name} at {obj.transform.position}");
                LogEntityDetails(obj);
            }
            else if (IsColoredObject(obj))
            {
                coloredObjectCount++;
                Debug.Log($"🎨 Found Colored Object: {name} at {obj.transform.position}");
                LogEntityDetails(obj);
            }
            
            if (IsEntity(obj))
            {
                entityCount++;
            }
        }
        
        Debug.Log($"📊 ENTITY FINDER RESULTS:");
        Debug.Log($"   Technicians: {technicianCount}");
        Debug.Log($"   Supervisors: {supervisorCount}");
        Debug.Log($"   Colored Objects: {coloredObjectCount}");
        Debug.Log($"   Total Entities: {entityCount}");
        
        // Try to find specific entities by name
        Debug.Log("🔍 Searching for specific entities by name...");
        FindSpecificEntity("SIMPLE_Technician_01");
        FindSpecificEntity("SIMPLE_Technician_02");
        FindSpecificEntity("SIMPLE_Supervisor_01");
        FindSpecificEntity("SIMPLE_Supervisor_02");
    }
    
    void FindSpecificEntity(string entityName)
    {
        GameObject entity = GameObject.Find(entityName);
        if (entity != null)
        {
            Debug.Log($"✅ Found {entityName} at {entity.transform.position}");
            LogEntityDetails(entity);
        }
        else
        {
            Debug.LogWarning($"❌ {entityName} not found!");
        }
    }
    
    void LogEntityDetails(GameObject obj)
    {
        Debug.Log($"   Position: {obj.transform.position}");
        Debug.Log($"   Scale: {obj.transform.localScale}");
        
        // Check components
        Rigidbody rb = obj.GetComponent<Rigidbody>();
        Collider col = obj.GetComponent<Collider>();
        Renderer renderer = obj.GetComponent<Renderer>();
        
        Debug.Log($"   Rigidbody: {(rb != null ? "YES" : "NO")}");
        Debug.Log($"   Collider: {(col != null ? "YES" : "NO")}");
        Debug.Log($"   Renderer: {(renderer != null ? "YES" : "NO")}");
        
        if (renderer != null && renderer.material != null)
        {
            Debug.Log($"   Color: {renderer.material.color}");
        }
        
        // Check for movement components
        Component[] allComponents = obj.GetComponents<Component>();
        foreach (Component comp in allComponents)
        {
            if (comp.GetType().Name.Contains("Movement"))
            {
                Debug.Log($"   Movement Component: {comp.GetType().Name}");
            }
        }
        
        Debug.Log("   ---");
    }
    
    bool IsEntity(GameObject obj)
    {
        // Skip if it's a UI element, camera, or light
        if (obj.GetComponent<Canvas>() != null || 
            obj.GetComponent<Camera>() != null || 
            obj.GetComponent<Light>() != null ||
            obj.name.Contains("EventSystem") ||
            obj.name.Contains("Canvas") ||
            obj.name.Contains("Camera") ||
            obj.name.Contains("Wall") ||
            obj.name.Contains("Border") ||
            obj.name.Contains("Surface") ||
            obj.name.Contains("Text") ||
            obj.name.Contains("Plan_"))
        {
            return false;
        }
        
        string name = obj.name.ToLower();
        
        // Check for specific entity names
        bool hasEntityName = name.Contains("technician") || 
                           name.Contains("supervisor") || 
                           name.Contains("simple_technician") ||
                           name.Contains("simple_supervisor") ||
                           name.Contains("agent");
        
        // Check for colored objects (likely entities)
        bool hasColor = IsColoredObject(obj);
        
        return hasEntityName || hasColor;
    }
    
    bool IsColoredObject(GameObject obj)
    {
        Renderer renderer = obj.GetComponent<Renderer>();
        if (renderer != null && renderer.material != null)
        {
            Color objColor = renderer.material.color;
            // Check if it's not white, black, or grey (likely an entity)
            return !IsGreyColor(objColor);
        }
        return false;
    }
    
    bool IsGreyColor(Color color)
    {
        // Check if color is white, black, or grey
        float greyThreshold = 0.1f;
        return (Mathf.Abs(color.r - color.g) < greyThreshold && 
                Mathf.Abs(color.g - color.b) < greyThreshold && 
                Mathf.Abs(color.r - color.b) < greyThreshold);
    }
    
    [ContextMenu("Find All Entities")]
    public void FindAllEntitiesNow()
    {
        FindAllEntities();
    }
}
