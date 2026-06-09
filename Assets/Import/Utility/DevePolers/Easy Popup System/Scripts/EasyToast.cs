using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

namespace EasyPopupSystem {
    public enum EasyToastPosition {
        TopRight,
        Top,
        TopLeft,
        Right,
        Center,
        Left,
        BottomRight,
        Bottom,
        BottomLeft,
    }
    
    public class EasyToast : MonoBehaviour
    {
        [Header("Toast Components")]
        [SerializeField] private TextMeshProUGUI TitleText;
        [SerializeField] private TextMeshProUGUI messageText;
        
        [Header("Animation")]
        [SerializeField] private Animator animator;
        [SerializeField] private string visibleAnimationTrigger = "Visible";
        
        [Header("Toast Settings")]
        [SerializeField] public float autoHideDelay = 3f;
        [SerializeField] public bool autoHide = true;
        [SerializeField] public EasyToastPosition toastPosition;
        private Button containerButton;
        [SerializeField] private Image[] colorImages;
        [SerializeField] private Image iconImage;
        private System.Action onToastHidden;

        void Awake() {
            containerButton = GetComponent<Button>();
            containerButton.onClick.AddListener(Hide);
        }

        public static EasyToast Create(EasyToastScriptableObjectScript scriptableObject, System.Action onToastHidden = null) {
            return Create(scriptableObject.title, scriptableObject.message, scriptableObject.prefabName, scriptableObject.toastPosition, onToastHidden, scriptableObject.autoHideDelay, scriptableObject.autoHide, scriptableObject.color, scriptableObject.icon);
        }
        
        public static EasyToast Create(string title, string message, string prefabName = "Toast", EasyToastPosition toastPosition = EasyToastPosition.TopRight, System.Action onToastHidden = null, float autoHideDelay = 3f, bool autoHide = true, Color color = default, Sprite icon = null) {
            var toastPrefab = Resources.Load<EasyToast>(prefabName);

            if (toastPrefab == null) {
                Debug.LogError("Failed to load toast prefab: " + prefabName);
                return null;
            }

            if (EasyToastContainer.Instance.toastPositions.Count == 0) {
                var toastContainer = Resources.Load<EasyToastContainer>("ToastContainer");
                if (toastContainer == null) {
                    Debug.LogError("Failed to load toast container: " + "ToastContainer");
                    return null;
                }
                Instantiate(toastContainer, Vector3.zero, Quaternion.identity);
            }
            var easyToast = Instantiate(toastPrefab, Vector3.zero, Quaternion.identity);
            easyToast.SetTitle(title);
            easyToast.SetMessage(message);
            easyToast.SetAutoHide(autoHide, autoHideDelay);
            easyToast.SetCallback(onToastHidden);
            easyToast.SetToastPosition(toastPosition);
            easyToast.SetColor(color);
            easyToast.SetIcon(icon);
            EasyToastContainer.Instance.AddToast(easyToast);
            return easyToast;
        }
        
        public void SetTitle(string title)
        {
            if (TitleText != null)
            {
                TitleText.text = title;
            }
        }

        public void SetToastPosition(EasyToastPosition toastPosition) {
            this.toastPosition = toastPosition;
        }
        
        public void SetMessage(string message)
        {
            if (messageText != null)
            {
                messageText.text = message;
            }
        }
        
        public void SetColor(Color color)
        {
            if (color == default) {
                return;
            }
            
            if (colorImages != null)
            {
                foreach (var image in colorImages)
                {
                    if (image != null)
                    {
                        image.color = color;
                    }
                }
            }
        }
        
        public void SetIcon(Sprite icon)
        {
            if (iconImage != null && icon != null)
            {
                iconImage.sprite = icon;
                iconImage.gameObject.SetActive(true);
            }
            else if (iconImage != null)
            {
                iconImage.gameObject.SetActive(false);
            }
        }
        
        public void SetAutoHide(bool autoHide, float delay = 3f)
        {
            this.autoHide = autoHide;
            this.autoHideDelay = delay;

            if (autoHide)
            {
                StartCoroutine(AutoHideCoroutine());
            }
        }
        
        public void SetCallback(System.Action onToastHidden = null)
        {
            this.onToastHidden = onToastHidden;
        }
        
        public void Show()
        {
            gameObject.SetActive(true);
            if (animator != null)
            {
                animator.SetBool(visibleAnimationTrigger, true);
            }
        }
        
        private IEnumerator AutoHideCoroutine()
        {
            yield return new WaitForSeconds(autoHideDelay);
            Hide();
        }
        
        public void Hide()
        {
            if (animator != null)
            {
                animator.SetBool(visibleAnimationTrigger, false);
            }
            else
            {
                gameObject.SetActive(false);
            }

            StartCoroutine(Destroy());
        }
        
        public void HideImmediate()
        {
            onToastHidden?.Invoke();
            gameObject.SetActive(false);
            Destroy(transform.parent.gameObject);
        }
        
        public IEnumerator Destroy() {
            yield return new WaitForSeconds(1f);
            onToastHidden?.Invoke();
            EasyToastContainer.Instance.RemoveToast(this);
            Destroy(transform.gameObject);
        }
    }
}
