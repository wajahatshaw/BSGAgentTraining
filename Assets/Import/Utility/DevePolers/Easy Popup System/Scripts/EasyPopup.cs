using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

namespace EasyPopupSystem {
    public class EasyPopup : MonoBehaviour
    {
        [Header("Popup Components")]
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI messageText;
        [SerializeField] private Button backgroundButton;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Image[] colorImages;
        [SerializeField] private Image iconImage;
        
        [Header("Animation")]
        [SerializeField] private Animator animator;
        [SerializeField] private string visibleAnimationTrigger = "Visible";
        [SerializeField] private string animatorName = "Scale";
        
        private System.Action onConfirm;
        private System.Action onCancel;
        private bool disableBackground = false;

        public static EasyPopup Create(EasyPopupScriptableObjectScript scriptableObject, System.Action onConfirm = null, System.Action onCancel = null) {
            var popupPrefab = Resources.Load(scriptableObject.prefabName);
            if (popupPrefab == null) {
                Debug.LogError("Failed to load prefab: " + scriptableObject.prefabName);
                return null;
            }
            
            return Create(scriptableObject.title, scriptableObject.message, scriptableObject.prefabName, onConfirm, onCancel, scriptableObject.disableBackground, scriptableObject.confirmButtonText, scriptableObject.cancelButtonText, scriptableObject.color, scriptableObject.animatorName);
        }

        public static EasyPopup Create(string title, string message, string prefabName = "PopupError", System.Action onConfirm = null, System.Action onCancel = null, bool disableBackground = false, string confirmButtonText = "Confirm", string cancelButtonText = "Cancel", Color color = default, string animatorName = null) {
            var popupPrefab = Resources.Load<GameObject>(prefabName);

            if (popupPrefab == null) {
                Debug.LogError("Failed to load prefab: " + prefabName);
                return null;
            }
            
            var popup = Instantiate(popupPrefab, Vector3.zero, Quaternion.identity);
            var easyPopup = popup.GetComponentInChildren<EasyPopup>();
            if (title != null) {
                easyPopup.SetTitle(title);
            }
            if (message != null) {
                easyPopup.SetMessage(message);
            }
            easyPopup.SetDisableBackground(disableBackground);
            easyPopup.SetCallbacks(onConfirm, onCancel);
            easyPopup.SetupButtons(confirmButtonText, cancelButtonText);
            easyPopup.SetColor(color);
            easyPopup.SetAnimatorName(animatorName);
            return easyPopup;
        }
        
        void SetDisableBackground(bool disable) {
            disableBackground = disable;
        }
        public void SetupButtons(string confirmButtonText, string cancelButtonText)
        {
            if (!disableBackground) {
                if (backgroundButton != null)
                    backgroundButton.onClick.AddListener(Cancel);
            }

            if (confirmButton != null)
                confirmButton.onClick.AddListener(Confirm);
            var confirmButtonTextMesh = confirmButton.GetComponentInChildren<TextMeshProUGUI>();
            if (confirmButtonTextMesh != null)
                confirmButtonTextMesh.text = confirmButtonText;
                
            if (cancelButton != null)
                cancelButton.onClick.AddListener(Cancel);

            var cancelButtonTextMesh = cancelButton.GetComponentInChildren<TextMeshProUGUI>();
            if (cancelButtonTextMesh != null)
                cancelButtonTextMesh.text = cancelButtonText;

            if (onCancel == null)
                cancelButton.gameObject.SetActive(false);           
        }

        public void SetColor(Color color) {
            if (color == default) {
                return;
            }
            
            foreach (var colorImage in colorImages) {
                colorImage.color = color;
            }
        }

        public void SetIcon(Sprite icon) {
            if (iconImage != null) {
                iconImage.sprite = icon;
                iconImage.gameObject.SetActive(true);
            }
        }

        public void SetAnimatorName(string animatorName) {
            if (animator != null) { 
                if (animatorName != null && animatorName != "") {
                    animator.SetBool(this.animatorName, false);
                    animator.SetBool(animatorName, true);
                } else {
                    animator.SetBool(this.animatorName, true);
                }
            } else {}
        }

        public void SetTitle(string title)
        {
            if (titleText != null)
            {
                titleText.text = title;
            }
        }
        
        public void SetMessage(string message)
        {
            if (messageText != null)
            {
                messageText.text = message;
            }
        }
        
        public void SetCallbacks(System.Action onConfirm = null, System.Action onCancel = null)
        {
            this.onConfirm = onConfirm;
            this.onCancel = onCancel;
        }
        
        public void Show()
        {
            gameObject.SetActive(true);
            if (animator != null)
            {
                animator.SetBool(visibleAnimationTrigger, true);
            }
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

            StartCoroutine(DestroyParent());
        }
        
        public void Confirm()
        {
            onConfirm?.Invoke();
            Hide();
        }
        
        public void Cancel()
        {
            onCancel?.Invoke();
            Hide();
        }

        // Update is called once per frame
        void Update()
        {
            
        }

        IEnumerator DestroyParent() {
            yield return new WaitForSeconds(0.3f);
            Destroy(transform.parent.gameObject);
        }
    }
}