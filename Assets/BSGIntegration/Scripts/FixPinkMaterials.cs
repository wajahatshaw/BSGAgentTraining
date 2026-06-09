using UnityEngine;

/// <summary>
/// FIX PINK MATERIALS - This will fix pink materials by using proper shaders
/// </summary>
public class FixPinkMaterials : MonoBehaviour
{
    [Header("FIX PINK MATERIALS")]
    [Tooltip("Start fixing immediately")]
    public bool fixNow = true;
    
    void Start()
    {
        if (fixNow)
        {
            Invoke(nameof(FixAllPinkMaterials), 0.5f);
        }
    }
    
    void Update()
    {
        // Press P to fix pink materials
        if (Input.GetKeyDown(KeyCode.P))
        {
            FixAllPinkMaterials();
        }
    }
    
    void FixAllPinkMaterials()
    {
        Debug.Log("🎨 FIXING PINK MATERIALS...");
        
        // Find all renderers with pink materials
        Renderer[] allRenderers = FindObjectsOfType<Renderer>();
        int fixedCount = 0;
        
        foreach (Renderer renderer in allRenderers)
        {
            if (HasPinkMaterial(renderer))
            {
                FixRendererMaterial(renderer);
                fixedCount++;
            }
        }
        
        Debug.Log($"✅ Fixed {fixedCount} pink materials!");
    }
    
    bool HasPinkMaterial(Renderer renderer)
    {
        if (renderer.material == null) return false;
        
        // Check if material is pink (Unity's default error color)
        Color materialColor = renderer.material.color;
        return (materialColor.r > 0.9f && materialColor.g < 0.1f && materialColor.b > 0.9f) ||
               renderer.material.shader.name.Contains("Hidden") ||
               renderer.material.shader.name.Contains("Error");
    }
    
    void FixRendererMaterial(Renderer renderer)
    {
        string objectName = renderer.gameObject.name;
        Debug.Log($"🎨 Fixing pink material on {objectName}...");
        
        // Create a new material with proper shader
        Material newMaterial = new Material(Shader.Find("Standard"));
        
        // Set appropriate color based on object type
        Color newColor = GetAppropriateColor(objectName);
        newMaterial.color = newColor;
        
        // Set material properties
        newMaterial.SetFloat("_Metallic", 0.1f);
        newMaterial.SetFloat("_Glossiness", 0.3f);
        
        // Apply the new material
        renderer.material = newMaterial;
        
        Debug.Log($"✅ Fixed {objectName} with color {newColor}");
    }
    
    Color GetAppropriateColor(string objectName)
    {
        string name = objectName.ToLower();
        
        if (name.Contains("technician"))
        {
            return new Color(0.2f, 0.4f, 1f); // Blue
        }
        else if (name.Contains("supervisor"))
        {
            return new Color(1f, 0.2f, 0.2f); // Red
        }
        else if (name.Contains("tool"))
        {
            return new Color(1f, 0.8f, 0.2f); // Yellow
        }
        else if (name.Contains("wall"))
        {
            return new Color(0.6f, 0.6f, 0.6f); // Gray
        }
        else if (name.Contains("floor") || name.Contains("plane"))
        {
            return new Color(0.2f, 0.8f, 0.2f); // Green
        }
        else if (name.Contains("plan"))
        {
            return new Color(1f, 0.6f, 0.2f); // Orange
        }
        else
        {
            return new Color(0.5f, 0.5f, 0.5f); // Default gray
        }
    }
    
    [ContextMenu("Fix Pink Materials")]
    public void FixPinkMaterialsNow()
    {
        FixAllPinkMaterials();
    }
}
