
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIItemContainer : MonoBehaviour
{
    [SerializeField] Image icon;
    [SerializeField] TMP_Text amountText;
    [SerializeField] TMP_Text itemNameText;
    private InventorySCO inventorySCO;
    



    public void Configure(InventorySCO inventorySCO, int amount)
    {
        this.inventorySCO = inventorySCO;
        icon.sprite = inventorySCO.icon;
        amountText.text = (amount > 0) ? amount.ToString() : "";
        itemNameText.text = inventorySCO.itemName;
    }
    
    public void Pressed()
    {
       // ActionManager.OnRecipieSelected?.Invoke(recipe);

        if(!UIManager.Instance.itemDescriptionPanel.activeSelf)
        {
           UIManager.Instance.itemDescriptionPanel.SetActive(true) ;
        }

        UIManager.Instance.itemDesHeader.text = inventorySCO.itemName;
        UIManager.Instance.itemDesText.text = inventorySCO.itemDescription;
        UIManager.Instance.itemDesIcon.sprite = inventorySCO.icon;

        UIManager.Instance.AC_UIInventoryEquip = onEquip;
    }

    void onEquip()
    {
        UIManager.Instance.AC_UIInventoryEquip -= onEquip;
        EventManager.AC_inventoryItemEquip?.Invoke(inventorySCO);
    }
}
