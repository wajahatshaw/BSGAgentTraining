using UnityEngine;

public class SetupSimplePhysicsScene : MonoBehaviour
{
    [Header("Auto Setup Simple Physics Scene")]
    public bool setupOnStart = true;
    
    void Start()
    {
        if (setupOnStart)
        {
            Debug.Log("🔧 SETTING UP SIMPLE PHYSICS SCENE...");
            SetupSimplePhysics();
        }
    }
    
    void SetupSimplePhysics()
    {
        // Disable any existing scene generators to avoid conflicts
        DisableOtherGenerators();
        
        // Find or create SimpleEntitySpawner
        SimpleEntitySpawner spawner = FindObjectOfType<SimpleEntitySpawner>();
        if (spawner == null)
        {
            GameObject spawnerObj = new GameObject("Simple_Physics_Spawner");
            spawner = spawnerObj.AddComponent<SimpleEntitySpawner>();
            Debug.Log("✅ Created SimpleEntitySpawner");
        }
        else
        {
            Debug.Log("✅ SimpleEntitySpawner already exists");
        }
        
        // Configure the spawner with optimal settings
        spawner.technicianCount = 10;
        spawner.supervisorCount = 5;
        spawner.technicianSpeed = 3f;
        spawner.supervisorSpeed = 2f;
        spawner.directionChangeInterval = 2f;
        
        // Set up areas
        spawner.spawnAreaMin = new Vector3(-8f, 0.5f, -8f);
        spawner.spawnAreaMax = new Vector3(8f, 0.5f, 8f);
        spawner.movementBoundsMin = new Vector3(-10f, 0f, -10f);
        spawner.movementBoundsMax = new Vector3(10f, 0f, 10f);
        
        // Set colors
        spawner.technicianBaseColor = Color.blue;
        spawner.supervisorBaseColor = Color.red;
        
        Debug.Log("✅ SIMPLE PHYSICS SCENE SETUP COMPLETE!");
        Debug.Log("🚀 The scene will generate with:");
        Debug.Log("   • ✅ 10 Blue Technicians (cubes) with random movement");
        Debug.Log("   • ✅ 5 Red Supervisors (spheres) with random movement");
        Debug.Log("   • ✅ Physics-based wall bouncing");
        Debug.Log("   • ✅ Rigidbody movement on XZ plane");
        Debug.Log("   • ✅ Collision detection with walls");
        Debug.Log("   • ✅ NO ECS dependencies - works everywhere!");
        
        // Destroy this setup script
        Destroy(this.gameObject);
    }
    
    void DisableOtherGenerators()
    {
        // Find and disable other scene generators to prevent conflicts
        MonoBehaviour[] allScripts = FindObjectsOfType<MonoBehaviour>();
        
        foreach (MonoBehaviour script in allScripts)
        {
            string scriptName = script.GetType().Name;
            
            if (scriptName.Contains("JSON") || 
                scriptName.Contains("ECS") || 
                scriptName.Contains("Enhanced") ||
                scriptName.Contains("Fixed") ||
                scriptName.Contains("Generator"))
            {
                if (script != this) // Don't disable ourselves
                {
                    Debug.Log($"🔇 Disabling conflicting script: {scriptName}");
                    script.enabled = false;
                }
            }
        }
    }
}
