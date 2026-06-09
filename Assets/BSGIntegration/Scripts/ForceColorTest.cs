using UnityEngine;

public class ForceColorTest : MonoBehaviour
{
    [Header("Force Color Test")]
    public bool enableForceTest = false; // Disabled by default to prevent conflicts
    public Color testColor = Color.red;
    public float testInterval = 2f;
    
    private float lastTestTime;
    
    void Start()
    {
        // DISABLED: This was causing agents to turn red automatically
        // if (enableForceTest)
        // {
        //     StartCoroutine(DelayedTest());
        // }
    }
    
    System.Collections.IEnumerator DelayedTest()
    {
        yield return new WaitForSeconds(3f);
        
        Debug.Log("=== FORCE COLOR TEST STARTING ===");
        ForceColorChangeTest();
    }
    
    void Update()
    {
        // DISABLED: This was causing agents to turn red every 2 seconds
        // if (enableForceTest && Time.time - lastTestTime >= testInterval)
        // {
        //     ForceColorChangeTest();
        //     lastTestTime = Time.time;
        // }
    }
    
    void ForceColorChangeTest()
    {
        Debug.Log("=== FORCING COLOR CHANGES ===");
        
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            if (IsAgent(obj.name))
            {
                Debug.Log($"Found agent: {obj.name}");
                
                Renderer renderer = obj.GetComponent<Renderer>();
                if (renderer != null)
                {
                    // Create new material with test color
                    Material newMaterial = new Material(renderer.material);
                    newMaterial.color = testColor;
                    renderer.material = newMaterial;
                    
                    Debug.Log($"*** FORCED COLOR CHANGE: {obj.name} changed to {testColor} ***");
                }
                else
                {
                    Debug.LogWarning($"No renderer found on {obj.name}");
                }
            }
        }
        
        Debug.Log("=== FORCE COLOR TEST COMPLETE ===");
    }
    
    bool IsAgent(string objectName)
    {
        return objectName.Contains("SIMPLE_Technician") ||
               objectName.Contains("SIMPLE_Supervisor") ||
               objectName.Contains("Technician_") ||
               objectName.Contains("Supervisor_") ||
               objectName.Contains("agent_");
    }
    
    [ContextMenu("Force Red Color")]
    public void ForceRedColor()
    {
        testColor = Color.red;
        ForceColorChangeTest();
    }
    
    [ContextMenu("Force Green Color")]
    public void ForceGreenColor()
    {
        testColor = Color.green;
        ForceColorChangeTest();
    }
    
    [ContextMenu("Force Blue Color")]
    public void ForceBlueColor()
    {
        testColor = Color.blue;
        ForceColorChangeTest();
    }
    
    [ContextMenu("Force Yellow Color")]
    public void ForceYellowColor()
    {
        testColor = Color.yellow;
        ForceColorChangeTest();
    }
    
    [ContextMenu("Reset Original Colors")]
    public void ResetOriginalColors()
    {
        Debug.Log("=== RESETTING TO ORIGINAL COLORS ===");
        
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            if (IsAgent(obj.name))
            {
                Renderer renderer = obj.GetComponent<Renderer>();
                if (renderer != null)
                {
                    Color originalColor = GetOriginalColor(obj.name);
                    
                    Material newMaterial = new Material(renderer.material);
                    newMaterial.color = originalColor;
                    renderer.material = newMaterial;
                    
                    Debug.Log($"Reset {obj.name} to original color: {originalColor}");
                }
            }
        }
        
        Debug.Log("=== COLOR RESET COMPLETE ===");
    }
    
    Color GetOriginalColor(string agentName)
    {
        if (agentName.Contains("Technician"))
        {
            return Color.green;
        }
        else if (agentName.Contains("Supervisor"))
        {
            return Color.blue;
        }
        else
        {
            return Color.white;
        }
    }
}
