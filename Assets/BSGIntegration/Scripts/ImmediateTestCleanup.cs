using UnityEngine;

namespace PAP.EarnoutBSG
{
    /// <summary>
    /// Simple script that removes test objects immediately when added to a GameObject
    /// Just drag this script onto any GameObject in the scene to execute cleanup
    /// </summary>
    public class ImmediateTestCleanup : MonoBehaviour
    {
        void Awake()
        {
            Debug.Log("ImmediateTestCleanup: Starting cleanup...");
            
            // Remove specific test objects
            var testAgent = GameObject.Find("TestAgent");
            if (testAgent != null)
            {
                Debug.Log("Removing TestAgent");
                DestroyImmediate(testAgent);
            }
            
            var testObject = GameObject.Find("TestObject");
            if (testObject != null)
            {
                Debug.Log("Removing TestObject");
                DestroyImmediate(testObject);
            }
            
            // Remove any objects with AgentComponent or ObjectComponent that have "Test" in name
            var agentComponents = FindObjectsOfType<MonoBehaviour>();
            foreach (var comp in agentComponents)
            {
                if (comp.gameObject.name.Contains("Test") && 
                    (comp.GetType().Name.Contains("Agent") || comp.GetType().Name.Contains("Object")))
                {
                    Debug.Log($"Removing test component object: {comp.gameObject.name}");
                    DestroyImmediate(comp.gameObject);
                }
            }
            
            Debug.Log("ImmediateTestCleanup: Cleanup complete, removing cleanup script");
            
            // Remove this script after cleanup
            DestroyImmediate(this);
        }
    }
}
