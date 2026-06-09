using UnityEngine;

/// <summary>
/// Quick test script to ensure immediate movement on Play.
/// This script will find all technicians and force them to start moving immediately.
/// </summary>
public class QuickMovementTest : MonoBehaviour
{
    [Header("QUICK MOVEMENT TEST")]
    [Tooltip("Automatically start movement when scene begins")]
    public bool autoStart = true;
    
    [Tooltip("Show detailed debug information")]
    public bool showDebugInfo = true;
    
    void Start()
    {
        if (autoStart)
        {
            // Small delay to ensure all objects are initialized
            Invoke(nameof(ForceStartAllMovement), 0.1f);
        }
    }
    
    void ForceStartAllMovement()
    {
        if (showDebugInfo)
            Debug.Log("🎯 QUICK MOVEMENT TEST - Starting immediate movement!");
        
        // Find all GameObjects that might be technicians
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        int techniciansFound = 0;
        int movementStarted = 0;
        
        foreach (GameObject obj in allObjects)
        {
            // Check if this looks like a technician
            if (obj.name.ToLower().Contains("technician") || 
                obj.name.ToLower().Contains("supervisor") ||
                obj.name.ToLower().Contains("agent"))
            {
                techniciansFound++;
                
                // Try to get TechnicianController
                TechnicianController controller = obj.GetComponent<TechnicianController>();
                if (controller == null)
                {
                    // Add TechnicianController if it doesn't exist
                    controller = obj.AddComponent<TechnicianController>();
                    controller.skipFallingPhase = true; // Skip falling, start immediately
                    controller.movementSpeed = 6f; // Set a good speed
                    
                    if (showDebugInfo)
                        Debug.Log($"📦 Added TechnicianController to {obj.name}");
                }
                else
                {
                    // Force start movement on existing controller
                    controller.ForceStartWandering();
                }
                
                // Ensure the object has a Rigidbody
                Rigidbody rb = obj.GetComponent<Rigidbody>();
                if (rb == null)
                {
                    rb = obj.AddComponent<Rigidbody>();
                    rb.useGravity = false; // No gravity for immediate movement
                    rb.freezeRotation = true;
                }
                
                // Ensure the object has a Collider
                Collider col = obj.GetComponent<Collider>();
                if (col == null)
                {
                    col = obj.AddComponent<BoxCollider>();
                }
                
                movementStarted++;
                
                if (showDebugInfo)
                    Debug.Log($"✅ Started movement on {obj.name}");
            }
        }
        
        if (showDebugInfo)
        {
            Debug.Log($"🎉 MOVEMENT TEST COMPLETE!");
            Debug.Log($"   - Found {techniciansFound} potential technicians");
            Debug.Log($"   - Started movement on {movementStarted} objects");
        }
        
        if (techniciansFound == 0)
        {
            Debug.LogWarning("⚠️ No technician objects found! Make sure your objects have 'technician', 'supervisor', or 'agent' in their names.");
        }
    }
    
    void Update()
    {
        // Press T to test movement again
        if (Input.GetKeyDown(KeyCode.T))
        {
            if (showDebugInfo)
                Debug.Log("🎮 T key pressed - Testing movement again!");
            ForceStartAllMovement();
        }
    }
    
    [ContextMenu("Test Movement Now")]
    public void TestMovementNow()
    {
        ForceStartAllMovement();
    }
}
