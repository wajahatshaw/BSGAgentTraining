using UnityEngine;

/// <summary>
/// SIMPLE MOVER - Just moves any GameObject this is attached to.
/// Attach this directly to the red cube you see in the scene!
/// </summary>
public class SIMPLE_MOVER : MonoBehaviour
{
    [Header("SIMPLE MOVER")]
    public float speed = 10f;
    public bool startMoving = true;
    
    private Vector3 direction;
    private bool isMoving = false;
    
    void Start()
    {
        if (startMoving)
        {
            StartMoving();
        }
    }
    
    void StartMoving()
    {
        // Set random direction
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)).normalized;
        isMoving = true;
        
        Debug.Log($"🚀 SIMPLE MOVER STARTED on {name} with direction {direction} and speed {speed}");
    }
    
    void Update()
    {
        if (!isMoving) return;
        
        // Move this object
        Vector3 movement = direction * speed * Time.deltaTime;
        Vector3 newPos = transform.position + movement;
        
        // Keep Y at current level
        newPos.y = transform.position.y;
        
        // Bounce off boundaries
        if (newPos.x > 8f || newPos.x < -8f)
        {
            direction.x = -direction.x;
            newPos.x = Mathf.Clamp(newPos.x, -8f, 8f);
        }
        
        if (newPos.z > 8f || newPos.z < -8f)
        {
            direction.z = -direction.z;
            newPos.z = Mathf.Clamp(newPos.z, -8f, 8f);
        }
        
        // Apply movement
        transform.position = newPos;
        
        // Debug
        if (Time.frameCount % 60 == 0)
        {
            Debug.Log($"🏃 {name} MOVING at {transform.position}");
        }
        
        // Change direction every 2 seconds
        if (Time.time % 2f < Time.deltaTime)
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)).normalized;
            Debug.Log($"🔄 {name} NEW DIRECTION: {direction}");
        }
    }
}
