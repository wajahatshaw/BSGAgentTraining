using UnityEngine;

/// <summary>
/// INSTANT MOVING CUBE - This will create a cube and make it move IMMEDIATELY!
/// No delays, no complex logic - just instant movement!
/// </summary>
public class INSTANT_MOVING_CUBE : MonoBehaviour
{
    [Header("INSTANT MOVING CUBE")]
    public bool CREATE_AND_MOVE_NOW = true;
    public float MOVEMENT_SPEED = 15f;
    
    private GameObject movingCube;
    private Vector3 moveDirection;
    private bool isMoving = false;
    
    void Start()
    {
        if (CREATE_AND_MOVE_NOW)
        {
            CreateInstantMovingCube();
        }
    }
    
    void CreateInstantMovingCube()
    {
        Debug.Log("🚀 CREATING INSTANT MOVING CUBE!");
        
        // Create cube
        movingCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        movingCube.name = "INSTANT_MOVING_CUBE";
        
        // Position on ground
        movingCube.transform.position = new Vector3(2f, 0.5f, 2f);
        movingCube.transform.localScale = Vector3.one * 1.2f;
        
        // Make it BRIGHT YELLOW so it's visible
        Renderer renderer = movingCube.GetComponent<Renderer>();
        Material material = new Material(Shader.Find("Unlit/Color"));
        material.color = Color.yellow;
        renderer.material = material;
        
        // Set movement direction
        moveDirection = new Vector3(1f, 0f, 0.5f).normalized;
        isMoving = true;
        
        Debug.Log($"✅ INSTANT MOVING CUBE CREATED at {movingCube.transform.position}");
        Debug.Log($"🏃 MOVEMENT STARTED with direction {moveDirection} and speed {MOVEMENT_SPEED}");
    }
    
    void Update()
    {
        // MOVE THE CUBE EVERY FRAME
        if (movingCube != null && isMoving)
        {
            // Calculate movement
            Vector3 movement = moveDirection * MOVEMENT_SPEED * Time.deltaTime;
            Vector3 oldPos = movingCube.transform.position;
            Vector3 newPos = oldPos + movement;
            
            // Keep on ground
            newPos.y = 0.5f;
            
            // Bounce off boundaries
            if (newPos.x > 8f || newPos.x < -8f)
            {
                moveDirection.x = -moveDirection.x;
                newPos.x = Mathf.Clamp(newPos.x, -8f, 8f);
                Debug.Log("🏐 BOUNCED X!");
            }
            
            if (newPos.z > 8f || newPos.z < -8f)
            {
                moveDirection.z = -moveDirection.z;
                newPos.z = Mathf.Clamp(newPos.z, -8f, 8f);
                Debug.Log("🏐 BOUNCED Z!");
            }
            
            // FORCE MOVE THE CUBE
            movingCube.transform.position = newPos;
            
            // Debug every 30 frames
            if (Time.frameCount % 30 == 0)
            {
                Debug.Log($"🏃 CUBE MOVING: From {oldPos} to {newPos} (Movement: {movement})");
            }
            
            // Change direction randomly every 3 seconds
            if (Time.time % 3f < Time.deltaTime)
            {
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                moveDirection = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)).normalized;
                Debug.Log($"🔄 NEW DIRECTION: {moveDirection}");
            }
        }
    }
    
    void OnGUI()
    {
        // Show status on screen
        GUI.Label(new Rect(10, 10, 300, 20), $"Cube Position: {(movingCube ? movingCube.transform.position.ToString() : "None")}");
        GUI.Label(new Rect(10, 30, 300, 20), $"Is Moving: {isMoving}");
        GUI.Label(new Rect(10, 50, 300, 20), $"Direction: {moveDirection}");
        GUI.Label(new Rect(10, 70, 300, 20), $"Speed: {MOVEMENT_SPEED}");
    }
}
