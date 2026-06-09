using UnityEngine;

/// <summary>
/// Visualizes proximity zones in Scene view only (NOT in Game view)
/// Draws light green spheres around tools to show detection zones
/// Uses Gizmos which only render in Scene view, never in Game view
/// </summary>
public class ProximityZoneVisualizer : MonoBehaviour
{
    [Header("Visualization Settings")]
    public float radius = 3f;
    public Color zoneColor = new Color(0.6f, 1f, 0.6f, 0.3f); // Light green (not pink!)
    
    void OnDrawGizmos()
    {
        // Gizmos ONLY draw in Scene view, never in Game view
        // This ensures proximity zones are visible in editor but not during gameplay
        Gizmos.color = zoneColor;
        
        // Draw filled translucent sphere
        Gizmos.DrawSphere(transform.position, radius);
        
        // Also draw wireframe outline for better visibility
        Color wireColor = new Color(zoneColor.r, zoneColor.g, zoneColor.b, 1f);
        Gizmos.color = wireColor;
        Gizmos.DrawWireSphere(transform.position, radius);
    }
    
    void OnDrawGizmosSelected()
    {
        // Draw brighter when selected
        Gizmos.color = new Color(0.8f, 1f, 0.8f, 0.5f);
        Gizmos.DrawSphere(transform.position, radius);
        
        Gizmos.color = new Color(0.8f, 1f, 0.8f, 1f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}

