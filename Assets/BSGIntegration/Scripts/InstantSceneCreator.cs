using UnityEngine;

public class InstantSceneCreator : MonoBehaviour
{
    void Start()
    {
        Debug.Log("🚀 INSTANT Scene Creator Starting...");
        CreateEverythingNow();
    }
    
    void CreateEverythingNow()
    {
        Debug.Log("Creating objects RIGHT NOW...");
        
        // Create orange platform
        GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
        platform.name = "Plan_Surface_Orange";
        platform.transform.position = Vector3.zero;
        platform.transform.localScale = new Vector3(20f, 0.3f, 15f);
        platform.GetComponent<Renderer>().material.color = new Color(1f, 0.6f, 0.2f);
        Debug.Log("✅ Created orange platform");
        
        // Create green sphere technician
        GameObject tech1 = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        tech1.name = "Technician_Green";
        tech1.transform.position = new Vector3(-5, 2, 0);
        tech1.transform.localScale = Vector3.one * 2f;
        tech1.GetComponent<Renderer>().material.color = Color.green;
        Debug.Log("✅ Created green technician");
        
        // Create blue sphere technician
        GameObject tech2 = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        tech2.name = "Technician_Blue";
        tech2.transform.position = new Vector3(0, 2, 5);
        tech2.transform.localScale = Vector3.one * 2f;
        tech2.GetComponent<Renderer>().material.color = Color.blue;
        Debug.Log("✅ Created blue technician");
        
        // Create purple sphere technician
        GameObject tech3 = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        tech3.name = "Technician_Purple";
        tech3.transform.position = new Vector3(5, 2, 0);
        tech3.transform.localScale = Vector3.one * 2f;
        tech3.GetComponent<Renderer>().material.color = Color.magenta;
        Debug.Log("✅ Created purple technician");
        
        // Create red tool
        GameObject tool1 = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        tool1.name = "Tool_Red_Motor";
        tool1.transform.position = new Vector3(-4, 1, -5);
        tool1.GetComponent<Renderer>().material.color = Color.red;
        Debug.Log("✅ Created red tool");
        
        // Create yellow tool
        GameObject tool2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tool2.name = "Tool_Yellow_Toolbox";
        tool2.transform.position = new Vector3(0, 1, -5);
        tool2.GetComponent<Renderer>().material.color = Color.yellow;
        Debug.Log("✅ Created yellow tool");
        
        // Position camera
        Camera.main.transform.position = new Vector3(0, 15, -10);
        Camera.main.transform.rotation = Quaternion.Euler(35, 0, 0);
        Debug.Log("✅ Positioned camera");
        
        Debug.Log("🎉 INSTANT Scene Created! Check hierarchy now!");
    }
    
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            Debug.Log("🔄 SPACE pressed - Creating scene again...");
            CreateEverythingNow();
        }
    }
}
