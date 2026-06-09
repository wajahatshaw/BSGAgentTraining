using UnityEngine;

/// <summary>
/// AGGRESSIVE DIRECT MOVEMENT - This WILL make entities move, no matter what!
/// This script bypasses all physics and directly changes positions every frame.
/// </summary>
public class FORCE_DIRECT_MOVEMENT : MonoBehaviour
{
    [Header("FORCE DIRECT MOVEMENT - GUARANTEED TO WORK")]
    [Tooltip("Start immediately")]
    public bool START_NOW = true;
    
    [Tooltip("Movement speed (units per second)")]
    public float SPEED = 5f;
    
    [Tooltip("Show debug info")]
    public bool SHOW_DEBUG = true;
    
    void Start()
    {
        if (START_NOW)
        {
            Invoke(nameof(FORCE_START_MOVEMENT), 0.5f);
        }
    }
    
    void FORCE_START_MOVEMENT()
    {
        if (SHOW_DEBUG)
            Debug.Log("🔥 FORCE DIRECT MOVEMENT - STARTING AGGRESSIVE MOVEMENT!");
        
        // Find ALL objects and force movement on entities
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        int forcedMovement = 0;
        
        foreach (GameObject obj in allObjects)
        {
            if (IS_ENTITY(obj))
            {
                FORCE_MOVEMENT_ON_ENTITY(obj);
                forcedMovement++;
            }
        }
        
        if (SHOW_DEBUG)
            Debug.Log($"🔥 FORCED MOVEMENT ON {forcedMovement} ENTITIES!");
    }
    
    bool IS_ENTITY(GameObject obj)
    {
        string name = obj.name.ToLower();
        return name.Contains("technician") || 
               name.Contains("supervisor") ||
               name.Contains("simple_technician") ||
               name.Contains("simple_supervisor") ||
               name.Contains("agent");
    }
    
    void FORCE_MOVEMENT_ON_ENTITY(GameObject entity)
    {
        // DESTROY ALL EXISTING MOVEMENT COMPONENTS
        Component[] allComponents = entity.GetComponents<Component>();
        foreach (Component comp in allComponents)
        {
            if (comp != entity.transform && comp != this)
            {
                string typeName = comp.GetType().Name.ToLower();
                if (typeName.Contains("movement") || 
                    typeName.Contains("motion") || 
                    typeName.Contains("controller") ||
                    typeName.Contains("rigidbody"))
                {
                    DestroyImmediate(comp);
                }
            }
        }
        
        // ADD DIRECT POSITION MOVER
        DIRECT_POSITION_MOVER mover = entity.GetComponent<DIRECT_POSITION_MOVER>();
        if (mover == null)
        {
            mover = entity.AddComponent<DIRECT_POSITION_MOVER>();
        }
        
        mover.SPEED = SPEED;
        mover.SHOW_DEBUG = SHOW_DEBUG;
        
        if (SHOW_DEBUG)
            Debug.Log($"🔥 FORCED DIRECT MOVEMENT ON {entity.name}");
    }
    
    [ContextMenu("FORCE START NOW")]
    public void FORCE_START_NOW()
    {
        FORCE_START_MOVEMENT();
    }
    
    void Update()
    {
        // No input needed - movement is forced automatically on Start
    }
}

/// <summary>
/// DIRECT POSITION MOVER - Changes position directly every frame.
/// NO PHYSICS, NO RIGIDBODY, NO CONFLICTS - JUST DIRECT POSITION CHANGES!
/// </summary>
public class DIRECT_POSITION_MOVER : MonoBehaviour
{
    [Header("DIRECT POSITION MOVER")]
    public float SPEED = 5f;
    public bool SHOW_DEBUG = true;
    
    private Vector3 START_POSITION;
    private Vector3 CURRENT_DIRECTION;
    private float NEXT_DIRECTION_CHANGE;
    private bool IS_MOVING = true;
    
    void Start()
    {
        START_POSITION = transform.position;
        SET_RANDOM_DIRECTION();
        NEXT_DIRECTION_CHANGE = Time.time + 2f;
        
        if (SHOW_DEBUG)
            Debug.Log($"🔥 DIRECT POSITION MOVER STARTED ON {name}");
    }
    
    void Update()
    {
        if (!IS_MOVING) return;
        
        // Change direction every 2 seconds
        if (Time.time >= NEXT_DIRECTION_CHANGE)
        {
            SET_RANDOM_DIRECTION();
            NEXT_DIRECTION_CHANGE = Time.time + 2f + Random.Range(-0.5f, 0.5f);
        }
        
        // MOVE DIRECTLY - NO PHYSICS!
        MOVE_POSITION_DIRECTLY();
        
        // Check boundaries
        CHECK_BOUNDARIES();
        
        // Debug every 2 seconds
        if (SHOW_DEBUG && Time.frameCount % 120 == 0)
        {
            Debug.Log($"🔥 {name} MOVING DIRECTLY AT POSITION: {transform.position}");
        }
    }
    
    void SET_RANDOM_DIRECTION()
    {
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        CURRENT_DIRECTION = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)).normalized;
        
        if (SHOW_DEBUG)
            Debug.Log($"🔥 {name} NEW DIRECTION: {CURRENT_DIRECTION}");
    }
    
    void MOVE_POSITION_DIRECTLY()
    {
        // DIRECT POSITION CHANGE - BYPASSES ALL PHYSICS!
        Vector3 movement = CURRENT_DIRECTION * SPEED * Time.deltaTime;
        Vector3 newPosition = transform.position + movement;
        
        // Keep same Y level
        newPosition.y = START_POSITION.y;
        
        // FORCE THE POSITION CHANGE
        transform.position = newPosition;
    }
    
    void CHECK_BOUNDARIES()
    {
        Vector3 currentPos = transform.position;
        float distance = Vector3.Distance(
            new Vector3(currentPos.x, 0, currentPos.z), 
            new Vector3(START_POSITION.x, 0, START_POSITION.z)
        );
        
        if (distance > 5f)
        {
            // Turn back toward start
            Vector3 toStart = (START_POSITION - currentPos).normalized;
            CURRENT_DIRECTION = new Vector3(toStart.x, 0, toStart.z).normalized;
            
            if (SHOW_DEBUG)
                Debug.Log($"🔥 {name} TURNING BACK TO START");
        }
    }
    
    public void START_MOVEMENT()
    {
        IS_MOVING = true;
        if (SHOW_DEBUG)
            Debug.Log($"🔥 {name} MOVEMENT STARTED");
    }
    
    public void STOP_MOVEMENT()
    {
        IS_MOVING = false;
        if (SHOW_DEBUG)
            Debug.Log($"🔥 {name} MOVEMENT STOPPED");
    }
}
