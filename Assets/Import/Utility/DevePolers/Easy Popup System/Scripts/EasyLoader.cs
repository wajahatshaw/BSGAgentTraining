using UnityEngine;

namespace EasyPopupSystem {
    public class EasyLoader : MonoBehaviour
    {
        public static EasyLoader Create() {
            var prefab = Resources.Load<EasyLoader>("Loader");
            if (prefab == null) {
                Debug.LogError("Failed to load prefab: Loader");
                return null;
            }
            var loader = Instantiate(prefab, Vector3.zero, Quaternion.identity);
            return loader;
        }

        public static void Hide() {
            var loader = FindAnyObjectByType<EasyLoader>();
            if (loader != null) {
                Destroy(loader.gameObject);
            }
        }

        public static void HideAllLoaders() {
            var loaders = FindObjectsByType<EasyLoader>(FindObjectsSortMode.None);
            foreach (var loader in loaders) {
                Destroy(loader.gameObject);
            }
        }
    }
}
