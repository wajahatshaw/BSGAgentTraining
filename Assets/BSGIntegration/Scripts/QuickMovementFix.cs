using UnityEngine;

public class QuickMovementFix : MonoBehaviour
{
    [Header("Quick Fix Settings")]
    public bool autoFixOnStart = true;
    public bool disableAllProximityEffects = true;
    public bool resetAgentColors = true;
    public bool logFixActions = true;
    
    void Start()
    {
        if (autoFixOnStart)
        {
            StartCoroutine(DelayedFix());
        }
    }
    
    System.Collections.IEnumerator DelayedFix()
    {
        // Wait a moment for other systems to initialize
        yield return new WaitForSeconds(0.5f);
        
        ApplyQuickFix();
    }
    
    [ContextMenu("Apply Quick Fix")]
    public void ApplyQuickFix()
    {
        if (logFixActions)
        {
            Debug.Log("=== APPLYING QUICK MOVEMENT FIX ===");
        }
        
        // Find all proximity systems
        ProximityDetectionSystem proximitySystem = FindObjectOfType<ProximityDetectionSystem>();
        ProximityIntegration integration = FindObjectOfType<ProximityIntegration>();
        ProximitySystemSetup setup = FindObjectOfType<ProximitySystemSetup>();
        
        if (proximitySystem != null)
        {
            if (disableAllProximityEffects)
            {
                proximitySystem.DisableProximityDetection();
                if (logFixActions) Debug.Log("✓ Disabled proximity detection");
            }
            
            proximitySystem.DisableDirectionEffects();
            if (logFixActions) Debug.Log("✓ Disabled direction effects");
            
            if (resetAgentColors)
            {
                proximitySystem.ResetAllAgentColors();
                if (logFixActions) Debug.Log("✓ Reset agent colors");
            }
        }
        
        // Find all agents and ensure they have proper movement components
        FixAgentMovementComponents();
        
        if (logFixActions)
        {
            Debug.Log("=== QUICK FIX COMPLETE ===");
            Debug.Log("Agents should now move normally without proximity interference");
        }
    }
    
    void FixAgentMovementComponents()
    {
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        int agentsFixed = 0;
        
        foreach (GameObject obj in allObjects)
        {
            if (obj.name.Contains("agent_") || obj.name.Contains("Agent") || 
                obj.name.Contains("Technician") || obj.name.Contains("Supervisor"))
            {
                // Ensure agent has proper movement setup
                Rigidbody rb = obj.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    // Reset rigidbody to normal values
                    rb.useGravity = false;
                    rb.linearDamping = 0.5f; // Normal damping
                    rb.angularDamping = 0.5f; // Normal angular damping
                    
                    // Clear any accumulated forces
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    
                    agentsFixed++;
                }
                
                // Reset any stuck colors
                Renderer renderer = obj.GetComponent<Renderer>();
                if (renderer != null)
                {
                    // Reset to original colors based on agent type (matching SimpleFourEntitySystem)
                    if (obj.name.Contains("technician") || obj.name.Contains("Technician"))
                    {
                        // Technicians should be Blue (matching SimpleFourEntitySystem)
                        if (obj.name.Contains("01"))
                        {
                            renderer.material.color = new Color(0.1f, 0.4f, 1f, 1f); // Bright Blue
                        }
                        else
                        {
                            renderer.material.color = new Color(0.2f, 0.8f, 1f, 1f); // Cyan Blue
                        }
                    }
                    else if (obj.name.Contains("supervisor") || obj.name.Contains("Supervisor"))
                    {
                        // Supervisors should be Yellow (matching SimpleFourEntitySystem)
                        if (obj.name.Contains("01"))
                        {
                            renderer.material.color = new Color(1f, 1f, 0.1f, 1f); // Bright Yellow
                        }
                        else
                        {
                            renderer.material.color = new Color(1f, 0.8f, 0.1f, 1f); // Orange Yellow
                        }
                    }
                    else if (obj.name.Contains("inspector") || obj.name.Contains("Inspector"))
                    {
                        renderer.material.color = Color.magenta;
                    }
                }
            }
        }
        
        if (logFixActions)
        {
            Debug.Log($"✓ Fixed movement components for {agentsFixed} agents");
        }
    }
    
    [ContextMenu("Enable Safe Proximity (Color Only)")]
    public void EnableSafeProximity()
    {
        ProximityDetectionSystem proximitySystem = FindObjectOfType<ProximityDetectionSystem>();
        if (proximitySystem != null)
        {
            proximitySystem.EnableProximityDetection();
            proximitySystem.DisableDirectionEffects();
            Debug.Log("✓ Enabled safe proximity detection (color changes only, no movement interference)");
        }
    }
    
    [ContextMenu("Disable All Proximity")]
    public void DisableAllProximity()
    {
        ProximityDetectionSystem proximitySystem = FindObjectOfType<ProximityDetectionSystem>();
        if (proximitySystem != null)
        {
            proximitySystem.DisableProximityDetection();
            proximitySystem.ResetAllAgentColors();
            Debug.Log("✓ Disabled all proximity effects");
        }
    }
    
    [ContextMenu("Test Agent Movement")]
    public void TestAgentMovement()
    {
        Debug.Log("=== TESTING AGENT MOVEMENT ===");
        
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        int agentCount = 0;
        
        foreach (GameObject obj in allObjects)
        {
            if (obj.name.Contains("agent_") || obj.name.Contains("Agent"))
            {
                agentCount++;
                Rigidbody rb = obj.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    Debug.Log($"Agent {obj.name}: Velocity = {rb.linearVelocity.magnitude:F2}, Position = {obj.transform.position}");
                }
                else
                {
                    Debug.Log($"Agent {obj.name}: No Rigidbody component");
                }
            }
        }
        
        Debug.Log($"Found {agentCount} agents for movement testing");
        Debug.Log("=== END MOVEMENT TEST ===");
    }
    
    [ContextMenu("Force Apply Correct Colors")]
    public void ForceApplyCorrectColors()
    {
        Debug.Log("=== FORCING CORRECT AGENT COLORS ===");
        
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        int agentsFixed = 0;
        
        foreach (GameObject obj in allObjects)
        {
            if (obj.name.Contains("SIMPLE_Technician") || obj.name.Contains("SIMPLE_Supervisor"))
            {
                Renderer renderer = obj.GetComponent<Renderer>();
                if (renderer != null)
                {
                    // Apply correct colors based on SimpleFourEntitySystem
                    if (obj.name.Contains("SIMPLE_Technician_01"))
                    {
                        renderer.material.color = new Color(0.1f, 0.4f, 1f, 1f); // Bright Blue
                        Debug.Log($"✓ Applied Bright Blue to {obj.name}");
                    }
                    else if (obj.name.Contains("SIMPLE_Technician_02"))
                    {
                        renderer.material.color = new Color(0.2f, 0.8f, 1f, 1f); // Cyan Blue
                        Debug.Log($"✓ Applied Cyan Blue to {obj.name}");
                    }
                    else if (obj.name.Contains("SIMPLE_Supervisor_01"))
                    {
                        renderer.material.color = new Color(1f, 1f, 0.1f, 1f); // Bright Yellow
                        Debug.Log($"✓ Applied Bright Yellow to {obj.name}");
                    }
                    else if (obj.name.Contains("SIMPLE_Supervisor_02"))
                    {
                        renderer.material.color = new Color(1f, 0.8f, 0.1f, 1f); // Orange Yellow
                        Debug.Log($"✓ Applied Orange Yellow to {obj.name}");
                    }
                    
                    agentsFixed++;
                }
            }
        }
        
        Debug.Log($"✓ Applied correct colors to {agentsFixed} agents");
        Debug.Log("=== COLOR FIX COMPLETE ===");
    }
}
