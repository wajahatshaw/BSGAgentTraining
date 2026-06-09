using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class WidgetSwitcher : MonoBehaviour
{
    [System.Serializable]
    public class WidgetPair
    {
        public Button button;
        public GameObject panel;
        public Image arrowImg; // Image to tint
    }

    [Header("Widgets")]
    [SerializeField] private List<WidgetPair> widgets = new List<WidgetPair>();

    [Header("Button Colors")]
    [SerializeField] private Color activeButtonColor = Color.white;
    [SerializeField] private Color inactiveButtonColor = Color.gray;

    private void Awake()
    {
        // Register button callbacks
        for (int i = 0; i < widgets.Count; i++)
        {
            int index = i;
            widgets[i].button.onClick.AddListener(() => SwitchTo(index));
        }

        // Optional: activate first widget by default
        if (widgets.Count > 0)
            SwitchTo(0);
    }

    /// <summary>
    /// Switch to widget by index
    /// </summary>
    public void SwitchTo(int index)
    {
        for (int i = 0; i < widgets.Count; i++)
        {
            bool isActive = (i == index);

            // Panel
            if (widgets[i].panel != null)
                widgets[i].panel.SetActive(isActive);

            if(widgets[i].arrowImg != null)
                widgets[i].arrowImg.gameObject.SetActive(isActive);

            // Button tint
            if (widgets[i].button.GetComponent<Image>() != null)
                widgets[i].button.GetComponent<Image>().color = isActive
                    ? activeButtonColor
                    : inactiveButtonColor;
        }
    }

    /// <summary>
    /// Switch using button reference (optional helper)
    /// </summary>
    public void SwitchTo(Button button)
    {
        for (int i = 0; i < widgets.Count; i++)
        {
            if (widgets[i].button == button)
            {
                SwitchTo(i);
                return;
            }
        }
    }
}
