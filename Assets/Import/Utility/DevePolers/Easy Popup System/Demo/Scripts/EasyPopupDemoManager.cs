using UnityEngine;
using EasyPopupSystem;

namespace EasyPopupSystemDemo {
    public class EasyPopupDemoManager : MonoBehaviour
    {
        public void CreatePopupInfo() {
            EasyPopup.Create("Welcome to Easy Popup System!", "Hello! This is an amazing popup system created by DevePolers. \n\nThis system allows you to easily create beautiful, animated popups in Unity with a simple API.\n\nFeatures:\n• Easy-to-use API\n• Beautiful animations", 
            "PopupInfo", ConfirmButtonClicked, CancelButtonClicked, false);
        }
        public void CreatePopupError() {
            EasyPopup.Create("Error", "This is an error popup with a title and a message", "PopupError", null, null, true);
        }

        public void CreatePopupWarning() {
            EasyPopup.Create("Warning", "This is a warning popup with a title and a message", "PopupWarning", ConfirmButtonClicked, CancelButtonClicked, true, "Approve", "Not Approve");
        }

        public void CreatePopupRate() {
            EasyPopup.Create(null, null, "PopupRate");
        }

        public void CreateToastInfo() {
            EasyToast.Create("Info", "This is an info toast with a title and a message", "ToastInfo", EasyToastPosition.TopRight, () => {Debug.Log("ToastInfo hidden");}, 3f, true);
        }
        public void CreateToastError() {
            EasyToast.Create("Error", "This is an error toast with a title and a message", "ToastError", EasyToastPosition.Right, null, 3f, true);
        }
        public void CreateToastWarning() {
            EasyToast.Create("Warning", "This is a warning toast with a title and a message", "ToastWarning", EasyToastPosition.BottomRight, null, 3f, true);
        }
        public void ConfirmButtonClicked() {
            Debug.Log("Confirm button clicked");
        }
        public void CancelButtonClicked() {
            Debug.Log("Cancel button clicked");
        }

        public void ShowLoader() {
            EasyLoader.Create();
            Invoke("HideLoader", 3f);
        }   
        public void HideLoader() {
            EasyLoader.Hide();
        }
    }
}