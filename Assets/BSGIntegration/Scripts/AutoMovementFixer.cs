using UnityEngine;

/// <summary>
/// AUTO MOVEMENT FIXER - Automatically fixes movement for all entities
/// This script will find all entities and add guaranteed working movement
/// </summary>
public class AutoMovementFixer : MonoBehaviour
{
    [Header("AUTO MOVEMENT FIXER")]
    [Tooltip("Start fixing immediately")]
    public bool fixImmediately = true;
    
    [Tooltip("Movement speed")]
    public float speed = 10f;
    
    [Tooltip("Show debug logs")]
    public bool showDebug = true;
    
    private bool hasFixed = false;
    
    void Start()
    {
        if (fixImmediately)
        {
            Invoke(nameof(FixAllEntities), 1f); // Wait 1 second for scene to load
        }
    }
    
    void Update()
    {
        // Note: Input disabled due to Input System conflict
        // Use context menu or inspector button instead
    }
    
    void FixAllEntities()
    {
        if (hasFixed) return;
        
        Debug.Log("🔧 AUTO MOVEMENT FIXER - Fixing all entities...");
        
        // Find all entities
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        int fixedCount = 0;
        
        foreach (GameObject obj in allObjects)
        {
            if (IsEntity(obj))
            {
                FixEntity(obj);
                fixedCount++;
            }
        }
        
        Debug.Log($"✅ AUTO MOVEMENT FIXER - Fixed {fixedCount} entities!");
        hasFixed = true;
    }
    
    void FixEntity(GameObject entity)
    {
        string entityName = entity.name;
        Debug.Log($"🔧 Fixing {entityName}...");
        
        // Remove ALL existing movement components
        Component[] allComponents = entity.GetComponents<Component>();
        foreach (Component comp in allComponents)
        {
            if (comp != entity.transform && 
                comp.GetType().Name.Contains("Movement"))
            {
                Debug.Log($"🗑️ Removing: {comp.GetType().Name}");
                DestroyImmediate(comp);
            }
        }
        
        // Add guaranteed working movement
        GuaranteedWorkingMovement movement = entity.AddComponent<GuaranteedWorkingMovement>();
        movement.speed = speed;
        movement.showDebug = showDebug;
        movement.startImmediately = true;
        
        Debug.Log($"✅ Added GuaranteedWorkingMovement to {entityName}!");
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
            obj.name.Contains("Text"))
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
        bool hasColor = false;
        Renderer renderer = obj.GetComponent<Renderer>();
        if (renderer != null && renderer.material != null)
        {
            Color objColor = renderer.material.color;
            // Check if it's not white, black, or grey (likely an entity)
            hasColor = !IsGreyColor(objColor);
        }
        
        // Check for primitive shapes that could be entities
        bool isPrimitive = obj.name.Contains("primitive") || 
                          obj.GetComponent<MeshFilter>() != null;
        
        return hasEntityName || hasColor || isPrimitive;
    }
    
    bool IsGreyColor(Color color)
    {
        // Check if color is white, black, or grey
        float greyThreshold = 0.1f;
        return (Mathf.Abs(color.r - color.g) < greyThreshold && 
                Mathf.Abs(color.g - color.b) < greyThreshold && 
                Mathf.Abs(color.r - color.b) < greyThreshold);
    }
    
    [ContextMenu("Fix All Entities")]
    public void FixAllEntitiesNow()
    {
        hasFixed = false;
        FixAllEntities();
    }
}
