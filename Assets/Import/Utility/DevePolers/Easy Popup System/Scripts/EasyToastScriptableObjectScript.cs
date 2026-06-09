using UnityEngine;

namespace EasyPopupSystem {
    [CreateAssetMenu(fileName = "EasyToastScriptableObjectScript", menuName = "DevePolers/Easy Popup System/EasyToastScriptableObjectScript")]
    public class EasyToastScriptableObjectScript : ScriptableObject
    {
        [Header("Toast Content")]
        public string title;
        public string message;
        public Color color = Color.red;
        public Sprite icon;
        
        [Header("Toast Configuration")]
        public string prefabName = "Toast";
        public bool autoHide = true;
        public float autoHideDelay = 3f;
        public EasyToastPosition toastPosition = EasyToastPosition.TopRight;
    }
}