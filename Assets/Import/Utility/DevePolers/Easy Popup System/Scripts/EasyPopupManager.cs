using System;
using UnityEngine;
using UnityEngine.Events;

namespace EasyPopupSystem {
    [Serializable]
    public class EasyPopupManagerData {
        [SerializeField] public string popupName;
        [SerializeField] public EasyPopupScriptableObjectScript popupScriptableObject;
        // These will be set at runtime, not in inspector
        [SerializeField] public UnityEvent onConfirm;
        [SerializeField] public UnityEvent onCancel;
    }

    [Serializable]
    public class EasyToastManagerData {
        [SerializeField] public string toastName;
        [SerializeField] public EasyToastScriptableObjectScript toastScriptableObject;
        [SerializeField] public UnityEvent onToastHidden;
    }
    public class EasyPopupManager : Singleton<EasyPopupManager>
    {
        [SerializeField] private EasyPopupManagerData[] popupScriptableObjects;
        [SerializeField] private EasyToastManagerData[] toastScriptableObjects;

        public void CreatePopup(string popupName) {
            int index = Array.FindIndex(popupScriptableObjects, data => data.popupName == popupName);
            if (index != -1) {
                CreatePopup(index);
            }
        }
        
        public void CreatePopup(int index) {
            Action onConfirm = null;
            Action onCancel = null;
            
            if (popupScriptableObjects[index].onConfirm.GetPersistentEventCount() > 0) {
                onConfirm = () => popupScriptableObjects[index].onConfirm.Invoke();
            }
            
            if (popupScriptableObjects[index].onCancel.GetPersistentEventCount() > 0) {
                onCancel = () => popupScriptableObjects[index].onCancel.Invoke();
            }
            
            EasyPopup.Create(popupScriptableObjects[index].popupScriptableObject, onConfirm, onCancel);
        }

        public void CreateToast(string toastName) {
            int index = Array.FindIndex(toastScriptableObjects, data => data.toastName == toastName);
            if (index != -1) {
                CreateToast(index);
            }
        }

        public void CreateToast(int index) {
            Action onToastHidden = null;
            
            EasyToast.Create(toastScriptableObjects[index].toastScriptableObject, onToastHidden);
        }

        public void ShowLoader() {
            EasyLoader.Create();
        }

        public void HideLoader() {
            EasyLoader.Hide();
        }
    }
}
