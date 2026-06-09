using System.Collections.Generic;
using UnityEngine;

namespace EasyPopupSystem {
    public class EasyToastContainer : Singleton<EasyToastContainer>
    {
        [SerializeField] public List<GameObject> toastPositions = new List<GameObject>();
        [SerializeField] private List<EasyToast> toasts = new List<EasyToast>();

        public void AddToast(EasyToast toast) {
            toast.transform.SetParent(toastPositions[(int)toast.toastPosition].transform);
            toasts.Add(toast);
        }

        public void RemoveToast(EasyToast toast) {
            toasts.Remove(toast);
        }
        
    }
}