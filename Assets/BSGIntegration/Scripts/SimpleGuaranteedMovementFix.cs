using UnityEngine;

/// <summary>
/// SIMPLE GUARANTEED MOVEMENT FIX - Uses only GuaranteedMovement system
/// This will fix your movement issue by using the working GuaranteedMovement script
/// </summary>
public class SimpleGuaranteedMovementFix : MonoBehaviour
{
    [Header("SIMPLE GUARANTEED MOVEMENT FIX")]
    [Tooltip("Start fixing immediately")]
    public bool fixNow = true;
    
    [Tooltip("Movement speed")]
    public float speed = 15f;
    
    [Tooltip("Show debug info")]
    public bool showDebug = true;
    
    void Start()
    {
        if (fixNow)
        {
            Invoke(nameof(FixMovementWithGuaranteedSystem), 0.1f);
        }
    }
    
    void Update()
    {
        // Press SPACE to fix movement
        if (Input.GetKeyDown(KeyCode.Space))
        {
            FixMovementWithGuaranteedSystem();
        }
    }
    
    void FixMovementWithGuaranteedSystem()
    {
        Debug.Log("🚀 SIMPLE GUARANTEED MOVEMENT FIX - Starting!");
        
        // First, disable all other movement scripts to prevent conflicts
        DisableAllOtherMovementScripts();
        
        // Find all entities and fix them using GuaranteedMovement approach
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        int fixedCount = 0;
        
        foreach (GameObject obj in allObjects)
        {
            if (IsEntity(obj))
            {
                FixEntityWithGuaranteedMovement(obj);
                fixedCount++;
            }
        }
        
        Debug.Log($"✅ Fixed movement for {fixedCount} entities using GuaranteedMovement system!");
        Debug.Log("🎮 Press SPACE to fix again if needed");
    }
    
    void DisableAllOtherMovementScripts()
    {
        Debug.Log("🔇 Disabling conflicting movement scripts...");
        
        MonoBehaviour[] allScripts = FindObjectsOfType<MonoBehaviour>();
        int disabledCount = 0;
        
        foreach (MonoBehaviour script in allScripts)
        {
            if (script != this && IsConflictingMovementScript(script))
            {
                Debug.Log($"🔇 Disabling: {script.GetType().Name}");
                script.enabled = false;
                disabledCount++;
            }
        }
        
        Debug.Log($"✅ Disabled {disabledCount} conflicting scripts");
    }
    
    bool IsConflictingMovementScript(MonoBehaviour script)
    {
        string scriptName = script.GetType().Name;
        return scriptName.Contains("Movement") || 
               scriptName.Contains("Controller") ||
               scriptName.Contains("Setup") ||
               scriptName.Contains("Generator") ||
               scriptName.Contains("Force") ||
               scriptName.Contains("SimpleFour") ||
               scriptName.Contains("QuickMovement") ||
               scriptName.Contains("Continuous") ||
               scriptName.Contains("RandomMoving") ||
               scriptName.Contains("Diagnostic");
    }
    
    void FixEntityWithGuaranteedMovement(GameObject entity)
    {
        string entityName = entity.name;
        Debug.Log($"🔧 Fixing {entityName} with GuaranteedMovement approach");
        
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
        
        // Ensure Rigidbody exists and configure it like GuaranteedMovement does
        Rigidbody rb = entity.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = entity.AddComponent<Rigidbody>();
            Debug.Log($"📦 Added Rigidbody to {entityName}");
        }
        
        // Configure Rigidbody exactly like GuaranteedMovement
        rb.useGravity = false; // Like GuaranteedMovement
        rb.mass = 1f;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        
        // Ensure Collider exists
        Collider col = entity.GetComponent<Collider>();
        if (col == null)
        {
            col = entity.AddComponent<BoxCollider>();
            Debug.Log($"📦 Added BoxCollider to {entityName}");
        }
        col.isTrigger = false;
        
        // Add SimpleGuaranteedMovement component (same as GuaranteedMovement uses)
        SimpleGuaranteedMovement movement = entity.AddComponent<SimpleGuaranteedMovement>();
        movement.speed = speed;
        movement.showDebug = showDebug;
        
        Debug.Log($"✅ Added SimpleGuaranteedMovement to {entityName} with speed {speed}");
    }
    
    bool IsEntity(GameObject obj)
    {
        string name = obj.name.ToLower();
        return name.Contains("technician") || 
               name.Contains("supervisor") || 
               name.Contains("simple_technician") ||
               name.Contains("simple_supervisor") ||
               name.Contains("agent");
    }
    
    [ContextMenu("Fix Movement Now")]
    public void FixMovementNow()
    {
        FixMovementWithGuaranteedSystem();
    }
}
