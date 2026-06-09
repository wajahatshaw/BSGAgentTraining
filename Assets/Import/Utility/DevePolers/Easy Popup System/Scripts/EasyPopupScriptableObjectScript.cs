using UnityEngine;

namespace EasyPopupSystem {
    [CreateAssetMenu(fileName = "EasyPopupScriptableObjectScript", menuName = "DevePolers/Easy Popup System/EasyPopupScriptableObjectScript")]
    public class EasyPopupScriptableObjectScript : ScriptableObject
    {
        [Header("Popup Content")]
        public string title;
        public string message;
        public Color color = Color.white;
        public Sprite icon;
        public string animatorName;
        
        [Header("Button Settings")]
        public string confirmButtonText = "Confirm";
        public string cancelButtonText = "Cancel";
        
        [Header("Popup Configuration")]
        public string prefabName = "Popup";
        public bool disableBackground = false;
    }
}
