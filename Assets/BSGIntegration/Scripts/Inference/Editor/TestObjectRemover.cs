using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace PAP.EarnoutBSG.Editor
{
    /// <summary>
    /// Editor script to immediately remove test objects from the scene
    /// </summary>
    public class TestObjectRemover : MonoBehaviour
    {
        [MenuItem("BSG/Remove Test Objects")]
        public static void RemoveTestObjects()
        {
            // Find and remove TestAgent
            GameObject testAgent = GameObject.Find("TestAgent");
            if (testAgent != null)
            {
                Debug.Log("Removing TestAgent from scene");
                DestroyImmediate(testAgent);
            }
            
            // Find and remove TestObject
            GameObject testObject = GameObject.Find("TestObject");
            if (testObject != null)
            {
                Debug.Log("Removing TestObject from scene");
                DestroyImmediate(testObject);
            }
            
            // Find any objects with "Test" in their name
            GameObject[] allObjects = FindObjectsOfType<GameObject>();
            int removedCount = 0;
            
            foreach (GameObject obj in allObjects)
            {
                if (obj.name.Contains("Test") && 
                    (obj.name.Contains("Agent") || obj.name.Contains("Object")) &&
                    !obj.name.Contains("Worker") && 
                    !obj.name.Contains("Machine"))
                {
                    Debug.Log($"Removing test object: {obj.name}");
                    DestroyImmediate(obj);
                    removedCount++;
                }
            }
            
            // Also remove any primitive objects that might be test objects
            foreach (GameObject obj in allObjects)
            {
                var meshFilter = obj.GetComponent<MeshFilter>();
                if (meshFilter != null)
                {
                    string meshName = meshFilter.sharedMesh?.name ?? "";
                    if ((meshName.Contains("Cube") || meshName.Contains("Sphere")) &&
                        obj.name.Contains("Test"))
                    {
                        Debug.Log($"Removing primitive test object: {obj.name}");
                        DestroyImmediate(obj);
                        removedCount++;
                    }
                }
            }
            
            Debug.Log($"Test object removal complete. Removed {removedCount} objects.");
            
            // Mark scene as dirty so changes are saved
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        }
        
        [MenuItem("BSG/List All Scene Objects")]
        public static void ListAllObjects()
        {
            GameObject[] allObjects = FindObjectsOfType<GameObject>();
            Debug.Log($"=== All Scene Objects ({allObjects.Length}) ===");
            
            foreach (GameObject obj in allObjects)
            {
                var components = obj.GetComponents<Component>();
                string componentList = "";
                foreach (var comp in components)
                {
                    if (comp != null && !(comp is Transform))
                        componentList += comp.GetType().Name + ", ";
                }
                
                Debug.Log($"Object: {obj.name} | Components: {componentList}");
            }
        }
    }
}
