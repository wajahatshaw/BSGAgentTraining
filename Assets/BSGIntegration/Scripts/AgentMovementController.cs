using UnityEngine;
using System.Collections.Generic;
using System.Collections;

[System.Serializable]
public class MovementPattern
{
    public string type;
    public float speed;
    public List<Vector3> waypoints;
    public float pauseDuration;
    public float rotationSpeed;
}

[System.Serializable]
public class MaterialColorData
{
    public float r;
    public float g;
    public float b;
    public float a;
    
    public Color ToColor()
    {
        return new Color(r, g, b, a);
    }
}

public class AgentMovementController : MonoBehaviour
{
    [Header("Movement Settings")]
    public MovementPattern movementPattern;
    public bool isMoving = true;
    
    [Header("Visual Settings")]
    public MaterialColorData materialColor;
    public string agentName;
    public string agentRole;
    
    private int currentWaypointIndex = 0;
    private bool isPaused = false;
    private Renderer agentRenderer;
    private Material agentMaterial;
    
    // Movement state
    private Vector3 targetPosition;
    private bool isRotating = false;
    
    void Start()
    {
        SetupMaterial();
        
        if (movementPattern != null && movementPattern.waypoints != null && movementPattern.waypoints.Count > 0)
        {
            targetPosition = movementPattern.waypoints[0];
            transform.position = targetPosition;
            
            if (movementPattern.waypoints.Count > 1)
            {
                StartCoroutine(MovementLoop());
            }
        }
    }
    
    void SetupMaterial()
    {
        agentRenderer = GetComponent<Renderer>();
        if (agentRenderer != null)
        {
            // Try to create URP material first, fallback to Standard
            agentMaterial = CreateURPMaterial();
            if (agentMaterial == null)
            {
                agentMaterial = CreateStandardMaterial();
            }
            
            if (materialColor != null)
            {
                agentMaterial.color = materialColor.ToColor();
            }
            
            agentRenderer.material = agentMaterial;
        }
    }
    
    Material CreateURPMaterial()
    {
        // Try to create URP/Lit material
        Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
        if (urpShader == null)
        {
            urpShader = Shader.Find("URP/Lit");
        }
        
        if (urpShader != null)
        {
            Material urpMat = new Material(urpShader);
            urpMat.name = $"Agent_{agentName}_URP_Material";
            
            // Set URP specific properties
            if (urpMat.HasProperty("_BaseColor"))
            {
                urpMat.SetColor("_BaseColor", materialColor?.ToColor() ?? Color.white);
            }
            if (urpMat.HasProperty("_Metallic"))
            {
                urpMat.SetFloat("_Metallic", 0.2f);
            }
            if (urpMat.HasProperty("_Smoothness"))
            {
                urpMat.SetFloat("_Smoothness", 0.6f);
            }
            
            Debug.Log($"✅ Created URP material for {agentName}");
            return urpMat;
        }
        
        return null;
    }
    
    Material CreateStandardMaterial()
    {
        Material standardMat = new Material(Shader.Find("Standard"));
        standardMat.name = $"Agent_{agentName}_Standard_Material";
        standardMat.color = materialColor?.ToColor() ?? Color.white;
        standardMat.SetFloat("_Metallic", 0.2f);
        standardMat.SetFloat("_Glossiness", 0.6f);
        
        Debug.Log($"✅ Created Standard material for {agentName}");
        return standardMat;
    }
    
    IEnumerator MovementLoop()
    {
        while (isMoving && movementPattern.waypoints.Count > 1)
        {
            // Move to next waypoint
            yield return StartCoroutine(MoveToWaypoint());
            
            // Pause at waypoint
            if (movementPattern.pauseDuration > 0)
            {
                isPaused = true;
                yield return new WaitForSeconds(movementPattern.pauseDuration);
                isPaused = false;
            }
            
            // Move to next waypoint
            currentWaypointIndex = (currentWaypointIndex + 1) % movementPattern.waypoints.Count;
        }
    }
    
    IEnumerator MoveToWaypoint()
    {
        Vector3 startPosition = transform.position;
        Vector3 targetPos = movementPattern.waypoints[currentWaypointIndex];
        
        // Rotate towards target first
        yield return StartCoroutine(RotateTowards(targetPos));
        
        // Move towards target
        float journeyLength = Vector3.Distance(startPosition, targetPos);
        float journeyTime = journeyLength / movementPattern.speed;
        float elapsedTime = 0;
        
        while (elapsedTime < journeyTime)
        {
            elapsedTime += Time.deltaTime;
            float fractionOfJourney = elapsedTime / journeyTime;
            
            transform.position = Vector3.Lerp(startPosition, targetPos, fractionOfJourney);
            yield return null;
        }
        
        transform.position = targetPos;
    }
    
    IEnumerator RotateTowards(Vector3 targetPosition)
    {
        Vector3 direction = (targetPosition - transform.position).normalized;
        if (direction == Vector3.zero) yield break;
        
        Quaternion targetRotation = Quaternion.LookRotation(direction);
        Quaternion startRotation = transform.rotation;
        
        float rotationTime = Quaternion.Angle(startRotation, targetRotation) / movementPattern.rotationSpeed;
        float elapsedTime = 0;
        
        while (elapsedTime < rotationTime)
        {
            elapsedTime += Time.deltaTime;
            float fraction = elapsedTime / rotationTime;
            
            transform.rotation = Quaternion.Slerp(startRotation, targetRotation, fraction);
            yield return null;
        }
        
        transform.rotation = targetRotation;
    }
    
    public void SetMovementPattern(MovementPattern pattern)
    {
        movementPattern = pattern;
        currentWaypointIndex = 0;
        
        if (isMoving && movementPattern.waypoints.Count > 1)
        {
            StopAllCoroutines();
            StartCoroutine(MovementLoop());
        }
    }
    
    public void PauseMovement()
    {
        isMoving = false;
        StopAllCoroutines();
    }
    
    public void ResumeMovement()
    {
        isMoving = true;
        if (movementPattern.waypoints.Count > 1)
        {
            StartCoroutine(MovementLoop());
        }
    }
    
    void OnDrawGizmos()
    {
        if (movementPattern?.waypoints != null && movementPattern.waypoints.Count > 1)
        {
            // Draw waypoint path
            Gizmos.color = materialColor?.ToColor() ?? Color.white;
            
            for (int i = 0; i < movementPattern.waypoints.Count; i++)
            {
                Vector3 waypoint = movementPattern.waypoints[i];
                
                // Draw waypoint sphere
                Gizmos.DrawWireSphere(waypoint, 0.3f);
                
                // Draw path line
                if (i < movementPattern.waypoints.Count - 1)
                {
                    Gizmos.DrawLine(waypoint, movementPattern.waypoints[i + 1]);
                }
                else
                {
                    // Connect last waypoint to first (loop)
                    Gizmos.DrawLine(waypoint, movementPattern.waypoints[0]);
                }
            }
        }
    }
}

