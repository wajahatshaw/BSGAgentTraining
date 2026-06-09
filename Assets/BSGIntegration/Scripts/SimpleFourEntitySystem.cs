using UnityEngine;

public class SimpleFourEntitySystem : MonoBehaviour
{
    [Header("SIMPLE 4 ENTITY SYSTEM")]
    public bool startOnPlay = true;
    
    [Header("Fix Status")]
    private bool planSurfaceFixed = false;
    private bool supervisorMovementFixed = false;
    private float fixTimer = 0f;

    private AgentProfile ResolveRuntimeAgentProfile(string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId))
            return null;

        SceneUILoader loader = FindObjectOfType<SceneUILoader>();
        if (loader != null && loader.sceneData != null && loader.sceneData.agentProfiles != null
            && loader.sceneData.agentProfiles.TryGetValue(agentId, out AgentProfile loaderProfile) && loaderProfile != null)
        {
            return loaderProfile;
        }

        SkillBasedActionSystem skillSystem = FindObjectOfType<SkillBasedActionSystem>();
        if (skillSystem != null && skillSystem.IsDataReady())
        {
            AgentProfile skillProfile = skillSystem.GetAgentProfile(agentId);
            if (skillProfile != null)
                return skillProfile;
        }

        return null;
    }
    
    void Start()
    {
        if (startOnPlay)
        {
            Debug.Log("🎯 SIMPLE 4 ENTITY SYSTEM STARTING!");
            
            // Clear everything first
            ClearAllObjects();
            
            // Create system with 2 technicians + 2 supervisors
            CreateSimpleSystem();
            
            Debug.Log("✅ SIMPLE 4 ENTITY SYSTEM COMPLETE!");
        }
    }
    
    void Update()
    {
        fixTimer += Time.deltaTime;
        
        // Keep trying to fix Plan_Surface_Orange every frame until successful
        if (!planSurfaceFixed)
        {
            TryFixPlanSurfaceOrange();
        }
        
        // Movement is handled by SingleMovementController
        // if (!supervisorMovementFixed && fixTimer > 0.5f)
        // {
        //     TryFixSupervisorMovement();
        // }
    }
    
    void ClearAllObjects()
    {
        // Destroy all existing objects except camera and this controller
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            if (obj != this.gameObject && 
                obj.name != "Main Camera" && 
                obj.name != "JSONSceneController" &&
                !obj.name.Contains("Camera"))
            {
                if (obj.name.Contains("WORKING") || 
                    obj.name.Contains("COLORED") ||
                    obj.name.Contains("Technician") ||
                    obj.name.Contains("Supervisor") ||
                    obj.name.Contains("Wall") ||
                    obj.name.Contains("Floor") ||
                    obj.name.Contains("workbench_") ||
                    obj.name.Contains("tool_"))
                {
                    Debug.Log($"🗑️ Clearing: {obj.name}");
                    DestroyImmediate(obj);
                }
            }
        }
    }
    
    void CreateSimpleSystem()
    {
        Debug.Log("🏗️ Creating simple system with prominent colors...");
        
        // Create distinctive plane
        CreateDistinctivePlane();
        
        // Create colorful walls
        CreateColorfulWalls();
        
        // Fix any existing Plan_Surface_Orange
        FixExistingPlanSurface();
        
        // Create 2 technicians + 2 supervisors
        CreateFourEntities();
        
        // Ensure both technicians are configured identically (with delay)
        StartCoroutine(EnsureTechnicianConsistencyDelayed());
        
        // Create workbench and tools for proximity interaction
        CreateWorkbenchAndTools();
        
        // Setup proximity detection system (with delay to ensure objects are created)
        StartCoroutine(SetupProximityDetectionDelayed());
        
        // Movement will be handled by SingleMovementController
        // StartCoroutine(EnsureSupervisorMovementDelayed());
        
        // Setup camera
        SetupCamera();
        
        Debug.Log("🎯 4 entities created with prominent colors and gravity!");
        
        // Add comprehensive debug check
        StartCoroutine(DebugSceneStatus());
    }
    
    void CreateDistinctivePlane()
    {
        GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.name = "SIMPLE_Plane";
        plane.transform.position = Vector3.zero;
        plane.transform.localScale = new Vector3(2.5f, 1f, 2.5f); // 25x25 area
        
        // Use Unity's default material and just change the color directly
        Renderer planeRenderer = plane.GetComponent<Renderer>();
        
        // Try multiple shader options to avoid pink
        Material planeMat = null;
        
        // Try Unlit/Color first (always works)
        Shader unlitShader = Shader.Find("Unlit/Color");
        if (unlitShader != null)
        {
            planeMat = new Material(unlitShader);
            planeMat.color = new Color(0.1f, 0.8f, 0.1f, 1f); // Bright Green
            Debug.Log("✅ Using Unlit/Color shader for plane");
        }
        else
        {
            // Fallback to Legacy Diffuse
            Shader legacyShader = Shader.Find("Legacy Shaders/Diffuse");
            if (legacyShader != null)
            {
                planeMat = new Material(legacyShader);
                planeMat.color = new Color(0.1f, 0.8f, 0.1f, 1f); // Bright Green
                Debug.Log("✅ Using Legacy Diffuse shader for plane");
            }
            else
            {
                // Last resort - use the existing material and just change color
                planeMat = planeRenderer.material;
                planeMat.color = new Color(0.1f, 0.8f, 0.1f, 1f); // Bright Green
                Debug.Log("✅ Using existing material for plane");
            }
        }
        
        planeRenderer.material = planeMat;
        Debug.Log("✅ Green plane created with working shader");
    }
    
    void CreateColorfulWalls()
    {
        // Create walls with consistent gray color
        Color consistentWallColor = new Color(0.7f, 0.7f, 0.7f, 1f); // Light gray
        
        CreateProminentWall("SIMPLE_North_Wall", new Vector3(0, 1.5f, 12), new Vector3(26, 3, 1), consistentWallColor);
        CreateProminentWall("SIMPLE_South_Wall", new Vector3(0, 1.5f, -12), new Vector3(26, 3, 1), consistentWallColor);
        CreateProminentWall("SIMPLE_East_Wall", new Vector3(12, 1.5f, 0), new Vector3(1, 3, 26), consistentWallColor);
        CreateProminentWall("SIMPLE_West_Wall", new Vector3(-12, 1.5f, 0), new Vector3(1, 3, 26), consistentWallColor);
        
        Debug.Log("✅ Consistent gray walls created");
    }
    
    void FixExistingPlanSurface()
    {
        Debug.Log("🔍 Searching for Plan_Surface_Orange to fix...");
        
        // Find and fix any existing Plan_Surface_Orange
        GameObject planSurface = GameObject.Find("Plan_Surface_Orange");
        if (planSurface != null)
        {
            Debug.Log("🔧 Found existing Plan_Surface_Orange, fixing color...");
            
            Renderer planRenderer = planSurface.GetComponent<Renderer>();
            if (planRenderer != null)
            {
                Material planMat = null;
                
                // Try Unlit/Color first (always works)
                Shader unlitShader = Shader.Find("Unlit/Color");
                if (unlitShader != null)
                {
                    planMat = new Material(unlitShader);
                    planMat.color = new Color(0.1f, 0.8f, 0.1f, 1f); // Bright Green
                    Debug.Log("✅ Using Unlit/Color shader for Plan_Surface_Orange");
                }
                else
                {
                    // Fallback to Legacy Diffuse
                    Shader legacyShader = Shader.Find("Legacy Shaders/Diffuse");
                    if (legacyShader != null)
                    {
                        planMat = new Material(legacyShader);
                        planMat.color = new Color(0.1f, 0.8f, 0.1f, 1f); // Bright Green
                        Debug.Log("✅ Using Legacy Diffuse shader for Plan_Surface_Orange");
                    }
                    else
                    {
                        // Last resort - use existing material
                        planMat = planRenderer.material;
                        planMat.color = new Color(0.1f, 0.8f, 0.1f, 1f); // Bright Green
                        Debug.Log("✅ Using existing material for Plan_Surface_Orange");
                    }
                }
                
                planRenderer.material = planMat;
                Debug.Log("✅ Plan_Surface_Orange fixed to green!");
            }
            else
            {
                Debug.LogWarning("⚠️ Plan_Surface_Orange found but has no Renderer component!");
            }
        }
        else
        {
            Debug.Log("ℹ️ No Plan_Surface_Orange found to fix - will try again later");
            // Try to find it with a coroutine delay
            StartCoroutine(FixPlanSurfaceDelayed());
        }
    }
    
    System.Collections.IEnumerator FixPlanSurfaceDelayed()
    {
        yield return new WaitForSeconds(0.5f);
        
        Debug.Log("🔍 Delayed search for Plan_Surface_Orange...");
        GameObject planSurface = GameObject.Find("Plan_Surface_Orange");
        if (planSurface != null)
        {
            Debug.Log("🔧 Found Plan_Surface_Orange in delayed search, fixing color...");
            
            Renderer planRenderer = planSurface.GetComponent<Renderer>();
            if (planRenderer != null)
            {
                Shader unlitShader = Shader.Find("Unlit/Color");
                if (unlitShader != null)
                {
                    Material planMat = new Material(unlitShader);
                    planMat.color = new Color(0.1f, 0.8f, 0.1f, 1f); // Bright Green
                    planRenderer.material = planMat;
                    Debug.Log("✅ Plan_Surface_Orange fixed to green in delayed search!");
                }
            }
        }
        else
        {
            Debug.Log("ℹ️ Plan_Surface_Orange still not found in delayed search");
        }
    }
    
    void CreateProminentWall(string name, Vector3 position, Vector3 scale, Color color)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.position = position;
        wall.transform.localScale = scale;
        // Skip Wall tag assignment to avoid errors
        // wall.tag = "Wall"; // Commented out to prevent "Tag: Wall is not defined" error
        
        // Use simple shader that definitely works
        Renderer wallRenderer = wall.GetComponent<Renderer>();
        Material wallMat = null;
        
        // Try Unlit/Color first (always works)
        Shader unlitShader = Shader.Find("Unlit/Color");
        if (unlitShader != null)
        {
            wallMat = new Material(unlitShader);
            wallMat.color = color;
            Debug.Log($"✅ Using Unlit/Color shader for {name}");
        }
        else
        {
            // Fallback to Legacy Diffuse
            Shader legacyShader = Shader.Find("Legacy Shaders/Diffuse");
            if (legacyShader != null)
            {
                wallMat = new Material(legacyShader);
                wallMat.color = color;
                Debug.Log($"✅ Using Legacy Diffuse shader for {name}");
            }
            else
            {
                // Last resort - modify existing material
                wallMat = wallRenderer.material;
                wallMat.color = color;
                Debug.Log($"✅ Using existing material for {name}");
            }
        }
        
        wallRenderer.material = wallMat;
        
        // Add kinematic rigidbody
        Rigidbody rb = wall.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        
        Debug.Log($"✅ Created {name} with working color {color}");
    }
    
    void CreateFourEntities()
    {
        Debug.Log("👥 Creating 2 technicians + 2 supervisors...");
        
        // Create 2 technicians with distinct green shades
        CreateProminentEntity("SIMPLE_Technician_01", true, 0, new Color(0.2f, 1f, 0.2f, 1f)); // Light Green
        CreateProminentEntity("SIMPLE_Technician_02", true, 1, new Color(0.2f, 1f, 0.2f, 1f)); // Light Green
        
        // Create 2 supervisors with distinct green shades
        CreateProminentEntity("SIMPLE_Supervisor_01", false, 0, new Color(0.2f, 1f, 0.2f, 1f)); // Light Green
        CreateProminentEntity("SIMPLE_Supervisor_02", false, 1, new Color(0.2f, 1f, 0.2f, 1f)); // Light Green
        
        Debug.Log("✅ All 4 entities created with prominent colors!");
    }
    
    void EnsureTechnicianConsistency()
    {
        Debug.Log("🔧 Ensuring both technicians are configured identically...");
        
        // Get both technicians
        GameObject tech01 = GameObject.Find("SIMPLE_Technician_01");
        GameObject tech02 = GameObject.Find("SIMPLE_Technician_02");
        
        if (tech01 != null && tech02 != null)
        {
            Debug.Log("✅ Found both technicians, applying identical configuration...");
            
            // Copy rigidbody settings from tech01 to tech02
            CopyRigidbodySettings(tech01, tech02);
            
            // Copy collider settings from tech01 to tech02
            CopyColliderSettings(tech01, tech02);
            
            // Copy movement component settings from tech01 to tech02
            CopyMovementComponentSettings(tech01, tech02);
            
            // Copy renderer/material settings from tech01 to tech02
            CopyRendererSettings(tech01, tech02);
            
            // Force identical initial state
            ForceIdenticalInitialState(tech01, tech02);
            
            Debug.Log("✅ Both technicians are now configured identically!");
        }
        else
        {
            Debug.LogWarning("⚠️ Could not find both technicians for consistency check");
        }
    }
    
    void CopyRigidbodySettings(GameObject sourceTech, GameObject targetTech)
    {
        Rigidbody sourceRb = sourceTech.GetComponent<Rigidbody>();
        Rigidbody targetRb = targetTech.GetComponent<Rigidbody>();
        
        if (sourceRb != null && targetRb != null)
        {
            // Copy all rigidbody settings from source to target
            targetRb.useGravity = sourceRb.useGravity;
            targetRb.mass = sourceRb.mass;
            targetRb.linearDamping = sourceRb.linearDamping;
            targetRb.angularDamping = sourceRb.angularDamping;
            targetRb.freezeRotation = sourceRb.freezeRotation;
            targetRb.interpolation = sourceRb.interpolation;
            targetRb.collisionDetectionMode = sourceRb.collisionDetectionMode;
            targetRb.isKinematic = sourceRb.isKinematic;
            
            Debug.Log($"✅ Copied rigidbody settings from {sourceTech.name} to {targetTech.name}");
        }
    }
    
    void CopyColliderSettings(GameObject sourceTech, GameObject targetTech)
    {
        Collider sourceCol = sourceTech.GetComponent<Collider>();
        Collider targetCol = targetTech.GetComponent<Collider>();
        
        if (sourceCol != null && targetCol != null)
        {
            // If collider types are different, make them match
            if (sourceCol.GetType() != targetCol.GetType())
            {
                DestroyImmediate(targetCol);
                // Add the correct collider type with explicit casting
                if (sourceCol is CapsuleCollider)
                {
                    targetCol = targetTech.AddComponent<CapsuleCollider>();
                }
                else if (sourceCol is BoxCollider)
                {
                    targetCol = targetTech.AddComponent<BoxCollider>();
                }
                else if (sourceCol is SphereCollider)
                {
                    targetCol = targetTech.AddComponent<SphereCollider>();
                }
                else
                {
                    targetCol = targetTech.AddComponent<Collider>();
                }
            }
            
            // Copy basic collider settings
            targetCol.isTrigger = sourceCol.isTrigger;
            
            // Copy specific settings for CapsuleCollider
            if (sourceCol is CapsuleCollider sourceCap && targetCol is CapsuleCollider targetCap)
            {
                targetCap.radius = sourceCap.radius;
                targetCap.height = sourceCap.height;
                targetCap.direction = sourceCap.direction;
                targetCap.center = sourceCap.center;
            }
            // Copy specific settings for BoxCollider
            else if (sourceCol is BoxCollider sourceBox && targetCol is BoxCollider targetBox)
            {
                targetBox.size = sourceBox.size;
                targetBox.center = sourceBox.center;
            }
            // Copy specific settings for SphereCollider
            else if (sourceCol is SphereCollider sourceSphere && targetCol is SphereCollider targetSphere)
            {
                targetSphere.radius = sourceSphere.radius;
                targetSphere.center = sourceSphere.center;
            }
            
            Debug.Log($"✅ Copied collider settings from {sourceTech.name} to {targetTech.name}");
        }
    }
    
    void CopyMovementComponentSettings(GameObject sourceTech, GameObject targetTech)
    {
        ContinuousMovement sourceMovement = sourceTech.GetComponent<ContinuousMovement>();
        ContinuousMovement targetMovement = targetTech.GetComponent<ContinuousMovement>();
        
        if (sourceMovement != null && targetMovement != null)
        {
            // Copy all movement settings from source to target
            targetMovement.speed = sourceMovement.speed;
            targetMovement.isTechnician = sourceMovement.isTechnician;
            targetMovement.entityColor = sourceMovement.entityColor;
            targetMovement.directionChangeInterval = sourceMovement.directionChangeInterval;
            targetMovement.randomVariationInterval = sourceMovement.randomVariationInterval;
            targetMovement.randomIntensity = sourceMovement.randomIntensity;
            
            Debug.Log($"✅ Copied movement component settings from {sourceTech.name} to {targetTech.name}");
        }
        else if (sourceMovement != null && targetMovement == null)
        {
            // If target doesn't have movement component, add one with same settings
            targetMovement = targetTech.AddComponent<ContinuousMovement>();
            targetMovement.speed = sourceMovement.speed;
            targetMovement.isTechnician = sourceMovement.isTechnician;
            targetMovement.entityColor = sourceMovement.entityColor;
            targetMovement.directionChangeInterval = sourceMovement.directionChangeInterval;
            targetMovement.randomVariationInterval = sourceMovement.randomVariationInterval;
            targetMovement.randomIntensity = sourceMovement.randomIntensity;
            
            Debug.Log($"✅ Added movement component to {targetTech.name} with settings from {sourceTech.name}");
        }
    }
    
    void CopyRendererSettings(GameObject sourceTech, GameObject targetTech)
    {
        Renderer sourceRenderer = sourceTech.GetComponent<Renderer>();
        Renderer targetRenderer = targetTech.GetComponent<Renderer>();
        
        if (sourceRenderer != null && targetRenderer != null)
        {
            // Copy material settings
            targetRenderer.material = sourceRenderer.material;
            targetRenderer.enabled = sourceRenderer.enabled;
            
            Debug.Log($"✅ Copied renderer settings from {sourceTech.name} to {targetTech.name}");
        }
    }
    
    void ForceIdenticalInitialState(GameObject sourceTech, GameObject targetTech)
    {
        Rigidbody sourceRb = sourceTech.GetComponent<Rigidbody>();
        Rigidbody targetRb = targetTech.GetComponent<Rigidbody>();
        
        if (sourceRb != null && targetRb != null)
        {
            // Copy current velocity from source to target
            targetRb.linearVelocity = sourceRb.linearVelocity;
            targetRb.angularVelocity = sourceRb.angularVelocity;
            
            // Ensure both are at the same Y level (ground level)
            Vector3 sourcePos = sourceTech.transform.position;
            Vector3 targetPos = targetTech.transform.position;
            targetPos.y = sourcePos.y; // Match Y position
            targetTech.transform.position = targetPos;
            
            Debug.Log($"✅ Forced identical initial state: velocity={sourceRb.linearVelocity}, position={targetPos}");
        }
    }
    
    void CreateProminentEntity(string entityName, bool isTechnician, int index, Color prominentColor)
    {
        // Create appropriate shape - both technicians and supervisors get capsules
        GameObject entity = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        entity.name = entityName;
        
        // CRITICAL: Assign completely unique positions - no overlap allowed
        // Use different positions based on entity name to ensure uniqueness
        Vector3 spawnPos = new Vector3(0f, 1f, 0f);
        
        if (entityName == "SIMPLE_Technician_01")
        {
            spawnPos = new Vector3(-8f, 1f, -6f);  // Far left, back
        }
        else if (entityName == "SIMPLE_Technician_02")
        {
            spawnPos = new Vector3(8f, 1f, 6f);    // Far right, front
        }
        else if (entityName == "SIMPLE_Supervisor_01")
        {
            spawnPos = new Vector3(8f, 1f, -6f);   // Far right, back
        }
        else if (entityName == "SIMPLE_Supervisor_02")
        {
            spawnPos = new Vector3(-8f, 1f, 6f);   // Far left, front
        }
        else
        {
            // Fallback: use index-based positions with large spacing
            float spacing = 10f;
            spawnPos = new Vector3(
                -10f + (index * spacing),
                1f,
                -10f + ((index % 2) * spacing * 2)
            );
        }
        
        entity.transform.position = spawnPos;
        Debug.Log($"📍 Spawning {entityName} at UNIQUE position: {spawnPos} (Y={spawnPos.y})");
        
        // Add rigidbody - CRITICAL: Disable gravity to prevent jumping
        Rigidbody rb = entity.AddComponent<Rigidbody>();
        rb.useGravity = false; // DISABLED to prevent jumping/falling
        rb.freezeRotation = true; // Prevent tumbling
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ | RigidbodyConstraints.FreezePositionY; // Lock Y position
        
        // Enhanced physics configuration for better movement
        rb.interpolation = RigidbodyInterpolation.Interpolate; // Smoother movement
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous; // Better collision detection
        
        // Optimized physics settings for faster capsule movement
        rb.mass = 1.8f; // Slightly lighter for faster movement
        rb.linearDamping = 0.1f; // Lower damping for faster movement
        rb.angularDamping = 2f; // Lower angular damping
        rb.linearDamping = 0.3f; // Lower drag for faster movement
        rb.angularDamping = 1.5f; // Lower angular drag
        
        // Create working material for entities
        Renderer entityRenderer = entity.GetComponent<Renderer>();
        Material entityMat = null;
        
        // Try Unlit/Color first (always works)
        Shader unlitShader = Shader.Find("Unlit/Color");
        if (unlitShader != null)
        {
            entityMat = new Material(unlitShader);
            entityMat.color = prominentColor;
            Debug.Log($"✅ Using Unlit/Color shader for {entityName}");
        }
        else
        {
        
            Shader legacyShader = Shader.Find("Legacy Shaders/Diffuse");
            if (legacyShader != null)
            {
                entityMat = new Material(legacyShader);
                entityMat.color = prominentColor;
                Debug.Log($"✅ Using Legacy Diffuse shader for {entityName}");
            }
            else
            {
                entityMat = entityRenderer.material;
                entityMat.color = prominentColor;
                Debug.Log($"✅ Using existing material for {entityName}");
            }
        }
        
        entityRenderer.material = entityMat;
        
        // Add simple AgentProximity component for direct color changes
        AgentProximity proximity = entity.AddComponent<AgentProximity>();
        proximity.agentID = entityName;
        proximity.detectionRadius = 3f;
        proximity.targetLayer = LayerMask.GetMask("Default"); // Will detect all objects
        
        Debug.Log($"✅ Added AgentProximity to {entityName} with ID: {entityName}");
        
        // Attach ML-Agents components to enable ML training
        string behaviorName = MLAgentAttacher.GetBehaviorNameForAgent(entityName);
        MLAgentAttacher.AttachMLAgentComponents(entity, behaviorName, entityName);
        
        // Update agent properties from JSON if available
        var mlAgent = entity.GetComponent<BSGMLAgent>();
        if (mlAgent != null)
        {
            AgentProfile runtimeProfile = ResolveRuntimeAgentProfile(entityName);
            if (runtimeProfile != null)
            {
                mlAgent.skillLevel = runtimeProfile.skillLevel;
                mlAgent.desireLevel = runtimeProfile.desireLevel;
            }
            else if (entityName == "SIMPLE_Technician_01")
            {
                mlAgent.skillLevel = 0f;
                mlAgent.desireLevel = 100f;
            }
            else if (entityName == "SIMPLE_Technician_02")
            {
                mlAgent.skillLevel = 0f;
                mlAgent.desireLevel = 80f;
            }
            else if (entityName == "SIMPLE_Supervisor_01")
            {
                mlAgent.skillLevel = 0f;
                mlAgent.desireLevel = 90f;
            }
            else if (entityName == "SIMPLE_Supervisor_02")
            {
                mlAgent.skillLevel = 0f;
                mlAgent.desireLevel = 100f;
            }
        }
        
        // Don't add movement component - let ML-Agent or SingleMovementController handle it
        // ContinuousMovement movement = entity.AddComponent<ContinuousMovement>();
        // movement.speed = isTechnician ? 6f : 4f; // Technicians faster
        // movement.isTechnician = isTechnician;
        // movement.entityColor = prominentColor;
        
        Debug.Log($"✅ Created {entityName} at {spawnPos} with prominent color {prominentColor}, ML-Agent: {behaviorName}");
    }
    
    void CreateWorkbenchAndTools()
    {
        Debug.Log("🔧 Creating workbench and tools for proximity interaction...");
        
        // Create workbench_001 (Main Assembly Workbench)
        CreateWorkbench("workbench_001", "Main Assembly Workbench", 
                       new Vector3(2, 0.8f, 3), new Color(1f, 0.5f, 0f), new Vector3(2f, 0.6f, 1.2f));
        
        // Create tool_001 (Industrial Motor Unit)
        CreateTool("tool_001", "Industrial Motor Unit", 
                  new Vector3(-4, 0.5f, -5), new Color(0.86f, 0.08f, 0.24f), PrimitiveType.Cylinder);
        
        // Create tool_002 (Secure Toolbox Alpha)
        CreateTool("tool_002", "Secure Toolbox Alpha", 
                  new Vector3(0, 0.5f, -5), new Color(1f, 0.84f, 0f), PrimitiveType.Cube);
        
        // Create tool_003 (Hydraulic Lift Station)
        CreateTool("tool_003", "Hydraulic Lift Station", 
                  new Vector3(4, 0.5f, -5), new Color(0.12f, 0.56f, 1f), PrimitiveType.Cube, new Vector3(1.5f, 1.2f, 1.5f));
        
        // Create tool_004 (Safety Inspection Station)
        CreateTool("tool_004", "Safety Inspection Station", 
                  new Vector3(-2, 0.5f, 3), new Color(0.12f, 0.56f, 1f), PrimitiveType.Cube);
        
        Debug.Log("✅ Workbench and tools created for proximity interaction!");
    }
    
    void CreateWorkbench(string objectName, string displayName, Vector3 position, Color color, Vector3 scale)
    {
        Debug.Log($"🔧 Creating detailed workbench: {displayName}");
        
        // Create main workbench body
        GameObject workbench = GameObject.CreatePrimitive(PrimitiveType.Cube);
        workbench.name = objectName;
        workbench.transform.position = position;
        workbench.transform.localScale = scale;
        
        // Add rigidbody (kinematic so it doesn't move)
        Rigidbody rb = workbench.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        
        // Apply color directly to workbench
        Renderer renderer = workbench.GetComponent<Renderer>();
        ApplyDirectColor(renderer, color);
        
        // Add workbench legs
        CreateWorkbenchLegs(workbench, position, scale);
        
        // Add workbench surface details
        CreateWorkbenchSurface(workbench, position, scale);
        
        // Add control panel
        CreateControlPanel(workbench, position, scale);
        
        Debug.Log($"✅ Created detailed workbench: {objectName} ({displayName}) at {position}");
    }
    
    void CreateTool(string objectName, string displayName, Vector3 position, Color color, PrimitiveType type, Vector3? scale = null)
    {
        Debug.Log($"🔧 Creating detailed machine: {displayName}");
        
        // Create main tool body
        GameObject tool = GameObject.CreatePrimitive(type);
        tool.name = objectName;
        tool.transform.position = position;
        
        if (scale.HasValue)
        {
            tool.transform.localScale = scale.Value;
        }
        
        // Add rigidbody (kinematic so it doesn't move)
        Rigidbody rb = tool.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        
        // Apply color directly to tool
        Renderer renderer = tool.GetComponent<Renderer>();
        ApplyDirectColor(renderer, color);
        
        // Add machine-specific details based on type
        if (objectName == "tool_001") // Industrial Motor Unit
        {
            CreateMotorDetails(tool, position, scale ?? Vector3.one);
        }
        else if (objectName == "tool_002") // Secure Toolbox Alpha
        {
            CreateToolboxDetails(tool, position, scale ?? Vector3.one);
        }
        else if (objectName == "tool_003") // Hydraulic Lift Station
        {
            CreateHydraulicDetails(tool, position, scale ?? Vector3.one);
        }
        else if (objectName == "tool_004") // Safety Inspection Station
        {
            CreateInspectionStationDetails(tool, position, scale ?? Vector3.one);
        }
        
        Debug.Log($"✅ Created detailed machine: {objectName} ({displayName}) at {position}");
    }
    
    Material CreateIndustrialMaterial(Color baseColor, float metallic, float smoothness)
    {
        // Use Unlit/Color shader which always works and never shows pink
        Material material = null;
        
        // Try Unlit/Color first (most reliable)
        Shader unlitShader = Shader.Find("Unlit/Color");
        if (unlitShader != null)
        {
            material = new Material(unlitShader);
            material.color = baseColor;
            Debug.Log($"✅ Using Unlit/Color shader for industrial material - Color: {baseColor}");
        }
        else
        {
            // Fallback to Legacy Diffuse
            Shader legacyShader = Shader.Find("Legacy Shaders/Diffuse");
            if (legacyShader != null)
            {
                material = new Material(legacyShader);
                material.color = baseColor;
                Debug.Log($"✅ Using Legacy Diffuse shader for industrial material - Color: {baseColor}");
            }
            else
            {
                // Last resort - use Sprites/Default
                Shader spriteShader = Shader.Find("Sprites/Default");
                if (spriteShader != null)
                {
                    material = new Material(spriteShader);
                    material.color = baseColor;
                    Debug.Log($"✅ Using Sprites/Default shader for industrial material - Color: {baseColor}");
                }
                else
                {
                    // Create basic material with default shader
                    material = new Material(Shader.Find("UI/Default"));
                    material.color = baseColor;
                    Debug.Log($"✅ Using UI/Default shader for industrial material - Color: {baseColor}");
                }
            }
        }
        
        return material;
    }
    
    Material CreateAgentStyleMaterial(Color prominentColor)
    {
        // Use the same material creation method that works for agents
        Material entityMat = null;
        
        // Try Unlit/Color first (always works)
        Shader unlitShader = Shader.Find("Unlit/Color");
        if (unlitShader != null)
        {
            entityMat = new Material(unlitShader);
            entityMat.color = prominentColor;
            Debug.Log($"✅ Using Unlit/Color shader for agent-style material - Color: {prominentColor}");
        }
        else
        {
            // Fallback to Legacy Diffuse
            Shader legacyShader = Shader.Find("Legacy Shaders/Diffuse");
            if (legacyShader != null)
            {
                entityMat = new Material(legacyShader);
                entityMat.color = prominentColor;
                Debug.Log($"✅ Using Legacy Diffuse shader for agent-style material - Color: {prominentColor}");
            }
            else
            {
                // Last resort - use existing material and just change color
                entityMat = new Material(Shader.Find("Sprites/Default"));
                entityMat.color = prominentColor;
                Debug.Log($"✅ Using Sprites/Default shader for agent-style material - Color: {prominentColor}");
            }
        }
        
        return entityMat;
    }
    
    Material CreateFallbackMaterial(Color color)
    {
        // Create a simple material that always works
        Material material = new Material(Shader.Find("Sprites/Default"));
        if (material.shader == null)
        {
            // Last resort - create basic material
            material = new Material(Shader.Find("Legacy Shaders/Diffuse"));
        }
        material.color = color;
        Debug.Log($"✅ Created fallback material with color {color}");
        return material;
    }
    
    void ForceMaterialApplication(Renderer renderer, Material material)
    {
        if (renderer != null && material != null)
        {
            // Check if material shader is valid
            if (material.shader == null)
            {
                Debug.LogWarning($"⚠️ Material shader is null for {renderer.gameObject.name}, using fallback");
                material = CreateFallbackMaterial(material.color);
            }
            
            // Force material assignment
            renderer.material = material;
            
            // Ensure the material is properly applied
            renderer.enabled = true;
            
            // Force update the material properties
            Debug.Log($"✅ Forced material application: {material.shader.name} with color {material.color}");
        }
    }
    
    void ApplyDirectColor(Renderer renderer, Color color)
    {
        if (renderer != null)
        {
            // Simply change the color of the existing material
            renderer.material.color = color;
            Debug.Log($"✅ Applied direct color {color} to {renderer.gameObject.name}");
        }
    }
    
    void CreateWorkbenchLegs(GameObject workbench, Vector3 position, Vector3 scale)
    {
        // Create 4 legs for the workbench
        Vector3 legSize = new Vector3(0.1f, scale.y * 0.8f, 0.1f);
        Vector3[] legPositions = {
            new Vector3(position.x - scale.x * 0.4f, position.y - scale.y * 0.4f, position.z - scale.z * 0.4f),
            new Vector3(position.x + scale.x * 0.4f, position.y - scale.y * 0.4f, position.z - scale.z * 0.4f),
            new Vector3(position.x - scale.x * 0.4f, position.y - scale.y * 0.4f, position.z + scale.z * 0.4f),
            new Vector3(position.x + scale.x * 0.4f, position.y - scale.y * 0.4f, position.z + scale.z * 0.4f)
        };
        
        for (int i = 0; i < 4; i++)
        {
            GameObject leg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leg.name = $"{workbench.name}_Leg_{i + 1}";
            leg.transform.position = legPositions[i];
            leg.transform.localScale = legSize;
            
            Renderer legRenderer = leg.GetComponent<Renderer>();
            ApplyDirectColor(legRenderer, new Color(0.3f, 0.3f, 0.3f));
            
            // Make legs kinematic
            Rigidbody legRb = leg.AddComponent<Rigidbody>();
            legRb.isKinematic = true;
        }
    }
    
    void CreateWorkbenchSurface(GameObject workbench, Vector3 position, Vector3 scale)
    {
        // Create workbench surface with slight bevel
        GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
        surface.name = $"{workbench.name}_Surface";
        surface.transform.position = new Vector3(position.x, position.y + scale.y * 0.1f, position.z);
        surface.transform.localScale = new Vector3(scale.x * 1.1f, scale.y * 0.1f, scale.z * 1.1f);
        
        Renderer surfaceRenderer = surface.GetComponent<Renderer>();
        ApplyDirectColor(surfaceRenderer, new Color(0.4f, 0.4f, 0.4f));
        
        Rigidbody surfaceRb = surface.AddComponent<Rigidbody>();
        surfaceRb.isKinematic = true;
    }
    
    void CreateControlPanel(GameObject workbench, Vector3 position, Vector3 scale)
    {
        // Create control panel on the front of workbench
        GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = $"{workbench.name}_ControlPanel";
        panel.transform.position = new Vector3(position.x, position.y + scale.y * 0.3f, position.z - scale.z * 0.6f);
        panel.transform.localScale = new Vector3(scale.x * 0.6f, scale.y * 0.4f, 0.05f);
        
        Renderer panelRenderer = panel.GetComponent<Renderer>();
        ApplyDirectColor(panelRenderer, new Color(0.1f, 0.1f, 0.1f));
        
        Rigidbody panelRb = panel.AddComponent<Rigidbody>();
        panelRb.isKinematic = true;
        
        // Add control buttons
        CreateControlButtons(panel, position, scale);
    }
    
    void CreateControlButtons(GameObject panel, Vector3 position, Vector3 scale)
    {
        // Create 3 control buttons
        for (int i = 0; i < 3; i++)
        {
            GameObject button = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            button.name = $"{panel.name}_Button_{i + 1}";
            button.transform.position = new Vector3(
                position.x - scale.x * 0.2f + i * scale.x * 0.2f,
                position.y + scale.y * 0.3f,
                position.z - scale.z * 0.55f
            );
            button.transform.localScale = new Vector3(0.1f, 0.02f, 0.1f);
            button.transform.rotation = Quaternion.Euler(90, 0, 0);
            
            Renderer buttonRenderer = button.GetComponent<Renderer>();
            Color[] buttonColors = { Color.red, Color.yellow, Color.green };
            ApplyDirectColor(buttonRenderer, buttonColors[i]);
            
            Rigidbody buttonRb = button.AddComponent<Rigidbody>();
            buttonRb.isKinematic = true;
        }
    }
    
    void CreateMotorDetails(GameObject motor, Vector3 position, Vector3 scale)
    {
        // Create motor housing
        GameObject housing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        housing.name = $"{motor.name}_Housing";
        housing.transform.position = new Vector3(position.x, position.y + scale.y * 0.3f, position.z);
        housing.transform.localScale = new Vector3(scale.x * 0.8f, scale.y * 0.6f, scale.z * 0.8f);
        
        Renderer housingRenderer = housing.GetComponent<Renderer>();
        ApplyDirectColor(housingRenderer, new Color(0.2f, 0.2f, 0.2f));
        
        Rigidbody housingRb = housing.AddComponent<Rigidbody>();
        housingRb.isKinematic = true;
        
        // Create cooling fins
        for (int i = 0; i < 8; i++)
        {
            GameObject fin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fin.name = $"{motor.name}_Fin_{i + 1}";
            float angle = i * 45f;
            Vector3 finPos = new Vector3(
                position.x + Mathf.Cos(angle * Mathf.Deg2Rad) * scale.x * 0.5f,
                position.y + scale.y * 0.3f,
                position.z + Mathf.Sin(angle * Mathf.Deg2Rad) * scale.z * 0.5f
            );
            fin.transform.position = finPos;
            fin.transform.localScale = new Vector3(0.05f, scale.y * 0.4f, 0.2f);
            fin.transform.rotation = Quaternion.Euler(0, angle, 0);
            
            Renderer finRenderer = fin.GetComponent<Renderer>();
            ApplyDirectColor(finRenderer, new Color(0.3f, 0.3f, 0.3f));
            
            Rigidbody finRb = fin.AddComponent<Rigidbody>();
            finRb.isKinematic = true;
        }
    }
    
    void CreateToolboxDetails(GameObject toolbox, Vector3 position, Vector3 scale)
    {
        // Create toolbox lid
        GameObject lid = GameObject.CreatePrimitive(PrimitiveType.Cube);
        lid.name = $"{toolbox.name}_Lid";
        lid.transform.position = new Vector3(position.x, position.y + scale.y * 0.6f, position.z);
        lid.transform.localScale = new Vector3(scale.x * 0.9f, scale.y * 0.2f, scale.z * 0.9f);
        
        Renderer lidRenderer = lid.GetComponent<Renderer>();
        ApplyDirectColor(lidRenderer, new Color(0.8f, 0.6f, 0.1f));
        
        Rigidbody lidRb = lid.AddComponent<Rigidbody>();
        lidRb.isKinematic = true;
        
        // Create lock mechanism
        GameObject lockMechanism = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        lockMechanism.name = $"{toolbox.name}_Lock";
        lockMechanism.transform.position = new Vector3(position.x, position.y + scale.y * 0.4f, position.z + scale.z * 0.4f);
        lockMechanism.transform.localScale = new Vector3(0.1f, 0.05f, 0.1f);
        lockMechanism.transform.rotation = Quaternion.Euler(90, 0, 0);
        
        Renderer lockRenderer = lockMechanism.GetComponent<Renderer>();
        ApplyDirectColor(lockRenderer, new Color(0.1f, 0.1f, 0.1f));
        
        Rigidbody lockRb = lockMechanism.AddComponent<Rigidbody>();
        lockRb.isKinematic = true;
    }
    
    void CreateHydraulicDetails(GameObject hydraulic, Vector3 position, Vector3 scale)
    {
        // Create hydraulic cylinder
        GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cylinder.name = $"{hydraulic.name}_Cylinder";
        cylinder.transform.position = new Vector3(position.x, position.y + scale.y * 0.5f, position.z);
        cylinder.transform.localScale = new Vector3(scale.x * 0.6f, scale.y * 0.8f, scale.z * 0.6f);
        
        Renderer cylinderRenderer = cylinder.GetComponent<Renderer>();
        ApplyDirectColor(cylinderRenderer, new Color(0.1f, 0.3f, 0.8f));
        
        Rigidbody cylinderRb = cylinder.AddComponent<Rigidbody>();
        cylinderRb.isKinematic = true;
        
        // Create hydraulic piston
        GameObject piston = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        piston.name = $"{hydraulic.name}_Piston";
        piston.transform.position = new Vector3(position.x, position.y + scale.y * 0.9f, position.z);
        piston.transform.localScale = new Vector3(scale.x * 0.4f, scale.y * 0.2f, scale.z * 0.4f);
        
        Renderer pistonRenderer = piston.GetComponent<Renderer>();
        ApplyDirectColor(pistonRenderer, new Color(0.2f, 0.2f, 0.2f));
        
        Rigidbody pistonRb = piston.AddComponent<Rigidbody>();
        pistonRb.isKinematic = true;
        
        // Create hydraulic hoses
        for (int i = 0; i < 2; i++)
        {
            GameObject hose = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hose.name = $"{hydraulic.name}_Hose_{i + 1}";
            hose.transform.position = new Vector3(
                position.x - scale.x * 0.3f + i * scale.x * 0.6f,
                position.y + scale.y * 0.2f,
                position.z - scale.z * 0.3f
            );
            hose.transform.localScale = new Vector3(0.05f, scale.y * 0.3f, 0.05f);
            hose.transform.rotation = Quaternion.Euler(0, 0, 90);
            
            Renderer hoseRenderer = hose.GetComponent<Renderer>();
            ApplyDirectColor(hoseRenderer, new Color(0.1f, 0.1f, 0.1f));
            
            Rigidbody hoseRb = hose.AddComponent<Rigidbody>();
            hoseRb.isKinematic = true;
        }
    }
    
    void CreateInspectionStationDetails(GameObject station, Vector3 position, Vector3 scale)
    {
        // Create inspection table
        GameObject table = GameObject.CreatePrimitive(PrimitiveType.Cube);
        table.name = $"{station.name}_Table";
        table.transform.position = new Vector3(position.x, position.y + scale.y * 0.3f, position.z);
        table.transform.localScale = new Vector3(scale.x * 0.8f, scale.y * 0.1f, scale.z * 0.8f);
        
        Renderer tableRenderer = table.GetComponent<Renderer>();
        ApplyDirectColor(tableRenderer, new Color(0.1f, 0.6f, 0.1f));
        
        Rigidbody tableRb = table.AddComponent<Rigidbody>();
        tableRb.isKinematic = true;
        
        // Create inspection equipment
        GameObject equipment = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        equipment.name = $"{station.name}_Equipment";
        equipment.transform.position = new Vector3(position.x, position.y + scale.y * 0.7f, position.z);
        equipment.transform.localScale = new Vector3(scale.x * 0.3f, scale.y * 0.6f, scale.z * 0.3f);
        
        Renderer equipmentRenderer = equipment.GetComponent<Renderer>();
        ApplyDirectColor(equipmentRenderer, new Color(0.1f, 0.1f, 0.1f));
        
        Rigidbody equipmentRb = equipment.AddComponent<Rigidbody>();
        equipmentRb.isKinematic = true;
        
        // Create status lights
        CreateStatusLights(station, position, scale);
    }
    
    void CreateStatusLights(GameObject station, Vector3 position, Vector3 scale)
    {
        // Create 3 status lights
        Color[] lightColors = { Color.red, Color.yellow, Color.green };
        string[] lightNames = { "Power", "Ready", "Active" };
        
        for (int i = 0; i < 3; i++)
        {
            GameObject light = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            light.name = $"{station.name}_Light_{lightNames[i]}";
            light.transform.position = new Vector3(
                position.x - scale.x * 0.2f + i * scale.x * 0.2f,
                position.y + scale.y * 0.8f,
                position.z + scale.z * 0.3f
            );
            light.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
            
            Renderer lightRenderer = light.GetComponent<Renderer>();
            ApplyDirectColor(lightRenderer, lightColors[i]);
            
            Rigidbody lightRb = light.AddComponent<Rigidbody>();
            lightRb.isKinematic = true;
        }
    }
    
    void SetupProximityDetection()
    {
        Debug.Log("🔍 Setting up proximity detection system...");
        
        // Create proximity detection manager if it doesn't exist
        GameObject proximityManager = GameObject.Find("ProximityDetectionManager");
        if (proximityManager == null)
        {
            proximityManager = new GameObject("ProximityDetectionManager");
            ProximityDetectionSystem proximitySystem = proximityManager.AddComponent<ProximityDetectionSystem>();
            Debug.Log("✅ Created ProximityDetectionSystem");
        }
        
        // Create proximity config loader if it doesn't exist
        GameObject configLoaderObj = GameObject.Find("ProximityConfigLoader");
        if (configLoaderObj == null)
        {
            configLoaderObj = new GameObject("ProximityConfigLoader");
            ProximityConfigLoader configLoader = configLoaderObj.AddComponent<ProximityConfigLoader>();
            Debug.Log("✅ Created ProximityConfigLoader");
        }
        
        Debug.Log("✅ Proximity detection system setup complete!");
    }
    
    System.Collections.IEnumerator EnsureTechnicianConsistencyDelayed()
    {
        yield return new WaitForSeconds(0.5f); // Wait for all components to be initialized
        EnsureTechnicianConsistency();
    }
    
    System.Collections.IEnumerator SetupProximityDetectionDelayed()
    {
        yield return new WaitForSeconds(1f); // Wait for all objects to be created
        SetupProximityDetection();
    }
    
    void EnsureSupervisorMovement()
    {
        Debug.Log("🔧 Ensuring supervisors have movement...");
        
        // Find and fix SIMPLE_Supervisor_01
        GameObject supervisor01 = GameObject.Find("SIMPLE_Supervisor_01");
        if (supervisor01 != null)
        {
            EnsureEntityHasMovement(supervisor01, "SIMPLE_Supervisor_01", false);
        }
        
        // Find and fix SIMPLE_Supervisor_02
        GameObject supervisor02 = GameObject.Find("SIMPLE_Supervisor_02");
        if (supervisor02 != null)
        {
            EnsureEntityHasMovement(supervisor02, "SIMPLE_Supervisor_02", false);
        }
        
        Debug.Log("✅ Supervisor movement ensured!");
    }
    
    System.Collections.IEnumerator EnsureSupervisorMovementDelayed()
    {
        yield return new WaitForSeconds(0.2f);
        
        Debug.Log("🔧 Delayed check: Ensuring supervisors have movement...");
        
        // Find and fix SIMPLE_Supervisor_01
        GameObject supervisor01 = GameObject.Find("SIMPLE_Supervisor_01");
        if (supervisor01 != null)
        {
            Debug.Log("🎯 Found SIMPLE_Supervisor_01, checking movement...");
            EnsureEntityHasMovement(supervisor01, "SIMPLE_Supervisor_01", false);
        }
        else
        {
            Debug.LogWarning("⚠️ SIMPLE_Supervisor_01 not found!");
        }
        
        // Find and fix SIMPLE_Supervisor_02
        GameObject supervisor02 = GameObject.Find("SIMPLE_Supervisor_02");
        if (supervisor02 != null)
        {
            Debug.Log("🎯 Found SIMPLE_Supervisor_02, checking movement...");
            EnsureEntityHasMovement(supervisor02, "SIMPLE_Supervisor_02", false);
        }
        else
        {
            Debug.LogWarning("⚠️ SIMPLE_Supervisor_02 not found!");
        }
        
        Debug.Log("✅ Delayed supervisor movement check completed!");
    }
    
    void EnsureEntityHasMovement(GameObject entity, string entityName, bool isTechnician)
    {
        Debug.Log($"🔍 Checking movement for {entityName}...");
        
        // Check if movement component exists
        ContinuousMovement existingMovement = entity.GetComponent<ContinuousMovement>();
        if (existingMovement == null)
        {
            Debug.Log($"🚀 No movement found on {entityName}, adding components...");
            
            // Ensure rigidbody exists
            Rigidbody rb = entity.GetComponent<Rigidbody>();
            if (rb == null)
            {
                Debug.Log($"📦 Adding Rigidbody to {entityName}");
                rb = entity.AddComponent<Rigidbody>();
                rb.useGravity = true;
                rb.mass = isTechnician ? 2f : 1.5f;
                rb.linearDamping = 0.3f;
                rb.angularDamping = 5f;
                rb.freezeRotation = true;
                Debug.Log($"✅ Rigidbody added to {entityName} with gravity=ON");
            }
            else
            {
                Debug.Log($"📦 Rigidbody already exists on {entityName}");
            }
            
            // DISABLED: Movement component not added - agents stay at initial positions
            // Debug.Log($"🏃 Adding ContinuousMovement to {entityName}");
            // ContinuousMovement movement = entity.AddComponent<ContinuousMovement>();
            // movement.speed = isTechnician ? 20f : 16f;
            // movement.isTechnician = isTechnician;
            // movement.entityColor = Color.green;
            
            // Ensure agent stays at initial position
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            
            Debug.Log($"🚫 Random movement DISABLED for {entityName} - agent will stay at initial position");
        }
        else
        {
            // Movement exists, ensure it's enabled and configured
            Debug.Log($"🔧 Movement component found on {entityName}, verifying settings...");
            existingMovement.enabled = true;
            existingMovement.speed = isTechnician ? 0.08f : 0.06f;
            existingMovement.isTechnician = isTechnician;
            
            Debug.Log($"✅ Movement verified and updated for {entityName} - Speed: {existingMovement.speed}, Enabled: {existingMovement.enabled}");
        }
    }
    
    void SetupCamera()
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(0, 18, -15);
            cam.transform.rotation = Quaternion.Euler(50, 0, 0);
            cam.backgroundColor = new Color(0.1f, 0.1f, 0.2f, 1f); // Dark blue background
            cam.fieldOfView = 60f;
        }
        
        Debug.Log("📷 Camera positioned for 4-entity scene");
    }
    
    System.Collections.IEnumerator DebugSceneStatus()
    {
        yield return new WaitForSeconds(1f);
        
        Debug.Log("🔍 === SCENE STATUS DEBUG ===");
        
        // Check Plan_Surface_Orange
        GameObject planSurface = GameObject.Find("Plan_Surface_Orange");
        if (planSurface != null)
        {
            Renderer planRenderer = planSurface.GetComponent<Renderer>();
            if (planRenderer != null)
            {
                Debug.Log($"✅ Plan_Surface_Orange found - Color: {planRenderer.material.color}");
            }
        }
        else
        {
            Debug.Log("❌ Plan_Surface_Orange not found");
        }
        
        // Check supervisors
        string[] supervisorNames = {"SIMPLE_Supervisor_01", "SIMPLE_Supervisor_02"};
        foreach (string name in supervisorNames)
        {
            GameObject supervisor = GameObject.Find(name);
            if (supervisor != null)
            {
                ContinuousMovement movement = supervisor.GetComponent<ContinuousMovement>();
                Rigidbody rb = supervisor.GetComponent<Rigidbody>();
                
                Debug.Log($"✅ {name} found:");
                Debug.Log($"   - Movement: {(movement != null ? "YES" : "NO")} {(movement != null && movement.enabled ? "(Enabled)" : "(Disabled)")}");
                Debug.Log($"   - Rigidbody: {(rb != null ? "YES" : "NO")} {(rb != null ? $"(Gravity: {rb.useGravity})" : "")}");
                Debug.Log($"   - Position: {supervisor.transform.position}");
                
                if (movement != null)
                {
                    Debug.Log($"   - Speed: {movement.speed}, Type: {(movement.isTechnician ? "Technician" : "Supervisor")}");
                }
            }
            else
            {
                Debug.LogWarning($"❌ {name} not found!");
            }
        }
        
        Debug.Log("🔍 === END SCENE STATUS ===");
    }
    
    void TryFixPlanSurfaceOrange()
    {
        GameObject planSurface = GameObject.Find("Plan_Surface_Orange");
        if (planSurface != null)
        {
            Renderer planRenderer = planSurface.GetComponent<Renderer>();
            if (planRenderer != null)
            {
                // Force create new material with green color
                Shader unlitShader = Shader.Find("Unlit/Color");
                if (unlitShader != null)
                {
                    Material greenMat = new Material(unlitShader);
                    greenMat.color = new Color(0.1f, 0.8f, 0.1f, 1f); // Bright Green
                    planRenderer.material = greenMat;
                    
                    planSurfaceFixed = true;
                    Debug.Log("✅ DIRECT FIX: Plan_Surface_Orange fixed to green!");
                }
            }
        }
    }
    
    void TryFixSupervisorMovement()
    {
        bool allFixed = true;
        
        // Fix SIMPLE_Supervisor_01
        GameObject supervisor01 = GameObject.Find("SIMPLE_Supervisor_01");
        if (supervisor01 != null)
        {
            if (!HasWorkingMovement(supervisor01))
            {
                ForceAddMovement(supervisor01, "SIMPLE_Supervisor_01", false);
                allFixed = false;
            }
        }
        else
        {
            allFixed = false;
        }
        
        // Fix SIMPLE_Supervisor_02
        GameObject supervisor02 = GameObject.Find("SIMPLE_Supervisor_02");
        if (supervisor02 != null)
        {
            if (!HasWorkingMovement(supervisor02))
            {
                ForceAddMovement(supervisor02, "SIMPLE_Supervisor_02", false);
                allFixed = false;
            }
        }
        else
        {
            allFixed = false;
        }
        
        if (allFixed)
        {
            supervisorMovementFixed = true;
            Debug.Log("✅ DIRECT FIX: All supervisor movement fixed!");
        }
    }
    
    bool HasWorkingMovement(GameObject entity)
    {
        ContinuousMovement movement = entity.GetComponent<ContinuousMovement>();
        Rigidbody rb = entity.GetComponent<Rigidbody>();
        
        return (movement != null && movement.enabled && rb != null && rb.useGravity);
    }
    
    void ForceAddMovement(GameObject entity, string entityName, bool isTechnician)
    {
        Debug.Log($"🚀 FORCE ADDING movement to {entityName}");
        
        // Ensure rigidbody with enhanced settings
        Rigidbody rb = entity.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = entity.AddComponent<Rigidbody>();
        }
        
        // Enhanced rigidbody configuration for better movement
        rb.useGravity = true;
        rb.mass = isTechnician ? 1.8f : 1.5f; // Lighter for technicians (capsules)
        rb.linearDamping = isTechnician ? 0.05f : 0.1f; // Lower damping for capsules
        rb.angularDamping = isTechnician ? 3f : 8f; // Lower angular damping for capsules
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate; // Smoother movement
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        
        // Ensure collider exists for physics
        Collider col = entity.GetComponent<Collider>();
        if (col == null)
        {
            col = entity.AddComponent<BoxCollider>();
            Debug.Log($"📦 Added BoxCollider to {entityName}");
        }
        
        // Remove existing movement if any
        ContinuousMovement existingMovement = entity.GetComponent<ContinuousMovement>();
        if (existingMovement != null)
        {
            DestroyImmediate(existingMovement);
        }
        
        // DISABLED: Random movement component not added
        // Agents will stay at their initial positions
        // ContinuousMovement movement = entity.AddComponent<ContinuousMovement>();
        // movement.speed = isTechnician ? 24f : 20f;
        // movement.isTechnician = isTechnician;
        // movement.entityColor = Color.green;
        // movement.directionChangeInterval = 1.5f;
        // movement.randomVariationInterval = 0.3f;
        // movement.randomIntensity = 0.05f;
        // rb.linearVelocity = new Vector3(Random.Range(-0.02f, 0.02f), rb.linearVelocity.y, Random.Range(-0.02f, 0.02f));
        
        // Ensure agent stays at initial position
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        
        Debug.Log($"🚫 Random movement DISABLED for {entityName} - agent will stay at initial position");
    }
}

// Continuous movement component with gravity support
public class ContinuousMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    public float speed = 16f; // Doubled default speed
    public bool isTechnician = true;
    public Color entityColor = Color.white;
    
    [Header("Continuous Random Movement")]
    public float directionChangeInterval = 2f;
    public float randomVariationInterval = 0.4f;
    public float randomIntensity = 0.05f;
    
    private Rigidbody rb;
    private Vector3 baseDirection;
    private Vector3 randomVariation;
    private float nextDirectionChange;
    private float nextRandomVariation;
    private bool isGrounded = false;
    
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        
        if (rb == null)
        {
            Debug.LogError($"❌ No Rigidbody found on {name}!");
            return;
        }
        
        // DISABLED: Random movement initialization removed
        // Agents will stay at their initial positions
        
        // Stop any movement immediately
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        
        Debug.Log($"🚫 {name} random movement DISABLED - agent will stay at initial position");
        
        // Original movement initialization commented out:
        // if (isTechnician)
        // {
        //     directionChangeInterval = Random.Range(1.5f, 2.5f);
        //     randomVariationInterval = 0.3f;
        //     randomIntensity = 0.1f;
        // }
        // else
        // {
        //     directionChangeInterval = Random.Range(1f, 2f);
        //     randomVariationInterval = 0.2f;
        //     randomIntensity = 0.08f;
        // }
        // GenerateNewDirection();
        // GenerateRandomVariation();
        // nextDirectionChange = Time.time + directionChangeInterval;
        // nextRandomVariation = Time.time + randomVariationInterval;
        // Vector3 initialVelocity = baseDirection * (speed * 0.05f);
        // initialVelocity.y = rb.linearVelocity.y;
        // rb.linearVelocity = initialVelocity;
    }
    
    void Update()
    {
        // DISABLED: Random movement removed - agents should stay at initial positions
        // Agents will not move randomly in the scene
        
        // Stop any existing movement
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        
        // Original movement code commented out:
        // CheckGrounded();
        // if (Time.time > nextDirectionChange)
        // {
        //     GenerateNewDirection();
        // }
        // if (Time.time > nextRandomVariation)
        // {
        //     GenerateRandomVariation();
        // }
        // ApplyMovement();
        // CheckBounds();
    }
    
    void CheckGrounded()
    {
        // Raycast down to check if on ground
        RaycastHit hit;
        Vector3 rayStart = transform.position + Vector3.up * 0.1f;
        
        if (Physics.Raycast(rayStart, Vector3.down, out hit, 1f))
        {
            isGrounded = true;
        }
        else
        {
            isGrounded = false;
        }
        
        // Debug ray
        Debug.DrawRay(rayStart, Vector3.down * 1f, isGrounded ? Color.green : Color.red);
    }
    
    void GenerateNewDirection()
    {
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        baseDirection = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
        nextDirectionChange = Time.time + directionChangeInterval + Random.Range(-0.5f, 0.5f);
        
        Debug.Log($"🔄 {name} new base direction: {baseDirection}");
    }
    
    void GenerateRandomVariation()
    {
        // Generate small random variations for natural movement
        randomVariation = new Vector3(
            Random.Range(-randomIntensity, randomIntensity),
            0f, // No Y component
            Random.Range(-randomIntensity, randomIntensity)
        ) * 0.3f;
        
        nextRandomVariation = Time.time + randomVariationInterval + Random.Range(-0.1f, 0.1f);
        
        Debug.Log($"🎲 {name} random variation: {randomVariation}");
    }
    
    void ApplyMovement()
    {
        if (rb == null) return;
        
        // Combine base direction with random variation
        Vector3 finalDirection = (baseDirection + randomVariation).normalized;
        
        // Apply horizontal movement while preserving gravity
        Vector3 targetVelocity = finalDirection * speed;
        targetVelocity.y = rb.linearVelocity.y; // PRESERVE GRAVITY
        
        // Ensure minimum movement speed for visibility
        if (targetVelocity.magnitude < 8f) // Doubled minimum speed
        {
            targetVelocity = targetVelocity.normalized * 12f; // Doubled minimum speed
            targetVelocity.y = rb.linearVelocity.y;
        }
        
        // Use AddForce for smoother movement with optimized physics
        Vector3 forceDirection = (targetVelocity - rb.linearVelocity).normalized;
        float forceMagnitude = Vector3.Distance(rb.linearVelocity, targetVelocity);
        rb.AddForce(forceDirection * forceMagnitude * 0.5f, ForceMode.Force);
        
        // Also set velocity directly for immediate response
        rb.linearVelocity = targetVelocity;
        
        // FORCE GROUND LEVEL POSITIONING
        Vector3 pos = transform.position;
        pos.y = 1f; // Force to ground level
        transform.position = pos;
        
        // Debug movement application more frequently
        if (Time.frameCount % 60 == 0) // Log every second at 60fps
        {
            Debug.Log($"🏃 {name} moving: velocity={rb.linearVelocity.magnitude:F1}, direction={finalDirection}, pos={transform.position}");
        }
    }
    
    void CheckBounds()
    {
        Vector3 pos = transform.position;
        Vector3 vel = rb.linearVelocity;
        
        // FORCE GROUND LEVEL AND BOUNDARY CONSTRAINTS
        pos.y = 1f; // Force to ground level
        
        // Keep within ground plane boundaries (adjust these values based on your ground size)
        pos.x = Mathf.Clamp(pos.x, -10f, 10f); // X boundaries
        pos.z = Mathf.Clamp(pos.z, -10f, 10f); // Z boundaries
        
        transform.position = pos;
        bool bounced = false;
        
        // X boundaries
        if (pos.x < -11f && vel.x < 0)
        {
            vel.x = Mathf.Abs(vel.x);
            baseDirection.x = Mathf.Abs(baseDirection.x);
            bounced = true;
        }
        else if (pos.x > 11f && vel.x > 0)
        {
            vel.x = -Mathf.Abs(vel.x);
            baseDirection.x = -Mathf.Abs(baseDirection.x);
            bounced = true;
        }
        
        // Z boundaries
        if (pos.z < -11f && vel.z < 0)
        {
            vel.z = Mathf.Abs(vel.z);
            baseDirection.z = Mathf.Abs(baseDirection.z);
            bounced = true;
        }
        else if (pos.z > 11f && vel.z > 0)
        {
            vel.z = -Mathf.Abs(vel.z);
            baseDirection.z = -Mathf.Abs(baseDirection.z);
            bounced = true;
        }
        
        if (bounced)
        {
            rb.linearVelocity = vel;
            Debug.Log($"🏀 {name} bounced off wall!");
            
            // Generate new direction after bounce
            GenerateNewDirection();
        }
    }
    
    void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Wall"))
        {
            Debug.Log($"💥 {name} hit wall: {collision.gameObject.name}");
        }
    }
}
