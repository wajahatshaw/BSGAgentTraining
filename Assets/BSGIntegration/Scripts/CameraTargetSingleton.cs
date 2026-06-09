using UnityEngine;

namespace PAP.EarnoutBSG
{
    public class CameraTargetSingleton : MonoBehaviour
    {
        public static CameraTargetSingleton Instance;

        private void Awake()
        {
            if (Instance != null)
            {
                Debug.LogWarning("Warning: Multiple instances of CameraTargetSingleton are present. Disabling newly added instance.", this);
                enabled = false;
                return;
            }

            Instance = this;
        }
    }
}