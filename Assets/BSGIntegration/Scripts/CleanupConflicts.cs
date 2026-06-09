using UnityEngine;

/// <summary>
/// Cleans up conflicting movement scripts and ensures only one system runs.
/// Attach this to any GameObject and it will remove all conflicting scripts.
/// </summary>
public class CleanupConflicts : MonoBehaviour
{
    [Header("CLEANUP CONFLICTS")]
    [Tooltip("Remove all conflicting movement scripts")]
    public bool cleanupOnStart = true;
    
    [Tooltip("Show debug information")]
    public bool showDebug = true;
    
    [Tooltip("Which movement system to keep (others will be removed)")]
    public MovementSystemToKeep keepSystem = MovementSystemToKeep.GuaranteedMovement;
    
    public enum MovementSystemToKeep
    {
        GuaranteedMovement,
        TechnicianController,
        SimpleFourEntitySystem,
        ForceMovementStarter
    }
    
    void Start()
    {
        if (cleanupOnStart)
        {
            Invoke(nameof(CleanupAllConflicts), 0.1f);
        }
    }
    
    void CleanupAllConflicts()
    {
        if (showDebug)
            Debug.Log("🧹 CLEANUP CONFLICTS - Removing conflicting movement scripts!");
        
        // Find all GameObjects that might have movement scripts
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        int cleanedObjects = 0;
        
        foreach (GameObject obj in allObjects)
        {
            if (HasMovementScripts(obj))
            {
                CleanupObject(obj);
                cleanedObjects++;
            }
        }
        
        if (showDebug)
            Debug.Log($"🎉 CLEANUP COMPLETE - Cleaned {cleanedObjects} objects!");
    }
    
    bool HasMovementScripts(GameObject obj)
    {
        // Check if object has any movement-related scripts
        return obj.GetComponent<TechnicianController>() != null ||
               obj.GetComponent<GuaranteedMovement>() != null ||
               obj.GetComponent<QuickMovementTest>() != null ||
               obj.GetComponent<ForceMovementStarter>() != null ||
               obj.GetComponent<SimpleFourEntitySystem>() != null ||
               obj.GetComponent<ForceWorkingMovement>() != null ||
               obj.GetComponent<ForceVisibleMovement>() != null ||
               obj.GetComponent<SimpleGuaranteedMovement>() != null ||
               obj.GetComponent<AggressiveMovement>() != null;
    }
    
    void CleanupObject(GameObject obj)
    {
        if (showDebug)
            Debug.Log($"🧹 Cleaning up {obj.name}");
        
        // Remove all movement scripts except the one we want to keep
        RemoveScriptIfNotKeep<TechnicianController>(obj);
        RemoveScriptIfNotKeep<GuaranteedMovement>(obj);
        RemoveScriptIfNotKeep<QuickMovementTest>(obj);
        RemoveScriptIfNotKeep<ForceMovementStarter>(obj);
        RemoveScriptIfNotKeep<SimpleFourEntitySystem>(obj);
        RemoveScriptIfNotKeep<ForceWorkingMovement>(obj);
        RemoveScriptIfNotKeep<ForceVisibleMovement>(obj);
        RemoveScriptIfNotKeep<SimpleGuaranteedMovement>(obj);
        RemoveScriptIfNotKeep<AggressiveMovement>(obj);
        
        // Remove this cleanup script too
        CleanupConflicts cleanup = obj.GetComponent<CleanupConflicts>();
        if (cleanup != null && cleanup != this)
        {
            DestroyImmediate(cleanup);
        }
    }
    
    void RemoveScriptIfNotKeep<T>(GameObject obj) where T : Component
    {
        T script = obj.GetComponent<T>();
        if (script != null)
        {
            // Check if this is the system we want to keep
            bool shouldKeep = false;
            
            switch (keepSystem)
            {
                case MovementSystemToKeep.GuaranteedMovement:
                    shouldKeep = script is GuaranteedMovement;
                    break;
                case MovementSystemToKeep.TechnicianController:
                    shouldKeep = script is TechnicianController;
                    break;
                case MovementSystemToKeep.SimpleFourEntitySystem:
                    shouldKeep = script is SimpleFourEntitySystem;
                    break;
                case MovementSystemToKeep.ForceMovementStarter:
                    shouldKeep = script is ForceMovementStarter;
                    break;
            }
            
            if (!shouldKeep)
            {
                if (showDebug)
                    Debug.Log($"🗑️ Removing {script.GetType().Name} from {obj.name}");
                DestroyImmediate(script);
            }
            else
            {
                if (showDebug)
                    Debug.Log($"✅ Keeping {script.GetType().Name} on {obj.name}");
            }
        }
    }
    
    [ContextMenu("Cleanup Conflicts Now")]
    public void CleanupConflictsNow()
    {
        CleanupAllConflicts();
    }
    
    void Update()
    {
        // Press C to cleanup conflicts
        if (Input.GetKeyDown(KeyCode.C))
        {
            if (showDebug)
                Debug.Log("🎮 C key pressed - Cleaning up conflicts!");
            CleanupAllConflicts();
        }
    }
}
