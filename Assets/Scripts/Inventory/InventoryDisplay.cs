using Jy_Util;
using UnityEngine;

public class InventoryDisplay : MonoBehaviour
{
    private InventoryItem[] items;
    public void Configure(Inventory inventory)
    {
        items = inventory.GetInventoryItems();
        UIItemContainer uIItemContainer;

        InventorySCO tempHolding_inventorySCO;
        for (int i = 0; i < items.Length; i++)
        {
            if (i < UIManager.Instance.inventoryConatienr.childCount)
            {
                //use preiviousely created containers
                uIItemContainer = UIManager.Instance.inventoryConatienr.GetChild(i).GetComponent<UIItemContainer>();
                uIItemContainer.gameObject.SetActive(true);

                tempHolding_inventorySCO = GameAsstes.Instance.GetInventorySCO(items[i].item_type);
                if(tempHolding_inventorySCO == null) continue;
                
                //Update just the amount text
                uIItemContainer.Configure(tempHolding_inventorySCO,items[i].amount);

                
            }else
            {
                tempHolding_inventorySCO = GameAsstes.Instance.GetInventorySCO(items[i].item_type);
                if(tempHolding_inventorySCO == null) continue;

                //when there is not enough container create new
                uIItemContainer = Instantiate(GameAsstes.Instance.uIItemContainerPrefb, UIManager.Instance.inventoryConatienr);
                uIItemContainer.Configure(tempHolding_inventorySCO,items[i].amount);
            }

            tempHolding_inventorySCO = null; //rest SCO
        }
    }

    public void UpdateDisplay(Inventory inventory)
    {
        Configure(inventory);
    }
}
