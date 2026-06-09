using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InteractPointerWidget : MonoBehaviour
{
    [SerializeField] GameObject pointer;
    [SerializeField] GameObject panelParent;
    [SerializeField] TMP_Text toolTipText;
    [SerializeField] TMP_Text headerText;
    [SerializeField] TMP_Text buttonText;
    [SerializeField] Image iconImage;
    [SerializeField] Image holdFillImage;
    private Billboard billboard;


    void Start()
    {
        pointer.SetActive(false);
        panelParent.SetActive(false);
        billboard = GetComponent<Billboard>();
        billboard.IsActive = false;

    }
    public void SetFillAmount(float amount)
    {
        holdFillImage.fillAmount = amount;
    }

    public void ShowPointerWidget()
    {
        this.gameObject.SetActive(true);
        pointer.SetActive(true);
        billboard.IsActive = true;
    }

    public void HidePointerWidget()
    {
        this.gameObject.SetActive(false);
        pointer.SetActive(false);
        billboard.IsActive = false;
    }

    public void ShowFullPanelStatus(bool show, string interactKey, string headerText, string toolTipText,Sprite iconSprite)
    {
        if (show)
        {
            if(buttonText)buttonText.text = interactKey;
            else iconImage.sprite = iconSprite;
            this.headerText.text = headerText;
            this.toolTipText.text = toolTipText;

            panelParent.SetActive(true);
        }
        else
        {
            panelParent.SetActive(false);
        }
    }
}
