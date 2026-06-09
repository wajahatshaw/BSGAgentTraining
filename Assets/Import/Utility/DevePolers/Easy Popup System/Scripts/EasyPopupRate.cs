using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EasyPopupSystem {
    public class EasyPopupRate : MonoBehaviour
    {
        int Rate = 5;
        [SerializeField] private GameObject RateGameObject;
        [SerializeField] private TMP_InputField RateInputField;
        [SerializeField] private Image[] StarImages;
        [SerializeField] private Color StarInactiveColor = new Color(0.737f, 0.737f, 0.737f, 1f); // #BCBCBC
        [SerializeField] private Color StarActiveColor = new Color(1f, 0.851f, 0.145f, 1f); // #FFC824

        private void Awake() {
            SetRate(Rate);
            for (int i = 0; i < StarImages.Length; i++)
            {
                int starIndex = i;
                Button starButton = StarImages[i].GetComponent<Button>();
                if (starButton != null)
                {
                    starButton.onClick.AddListener(() => SetRate(starIndex + 1));
                }
            }
        }

        public void ExpandRateObject()
        {
            if (RateGameObject != null)
            {
                RateGameObject.SetActive(true);
            }
        }

        public void CloseRateObject() {
            if (RateGameObject != null) {
                RateGameObject.SetActive(false);
            }
        }
        
        public void SetRate(int rate) {
            Rate = rate;

            if (Rate != 5) {
                ExpandRateObject();
            } else {
                CloseRateObject();
            }

            for (int i = 0; i < StarImages.Length; i++) {
                if (i < Rate) {
                    StarImages[i].color = StarActiveColor;
                } else {
                    StarImages[i].color = StarInactiveColor;
                }
            }
        }

        public void SendRate() {
            // TODO: Send rate to server. Recommend: every rate 5 redirect to store. Other rates can be sent to server.
            Debug.Log("Sending rate to server. Send rate to server. Recommend: every rate 5 redirect to store.");
        }
        
    }
}