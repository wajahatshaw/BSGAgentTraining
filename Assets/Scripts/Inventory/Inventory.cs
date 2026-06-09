using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Jy_Util;


public class Inventory
{
    [SerializeField] List<InventoryItem> items = new List<InventoryItem>();


    public void AddItemToInventory(E_Inventory_Item_Type itemType, int amount)
    {
        bool cropFound = false;

        foreach (InventoryItem item in items)
        {
            if (itemType == item.item_type)
            {
                cropFound = true;
                item.amount += amount;
                break;
            }
        }

        if (cropFound) return;

        //create new Item for inventroy as its a new type of crop added to inventory
        items.Add(new InventoryItem(itemType, amount));
    }





    public void AddItemToInventory(InventoryItem item)
    {
        AddItemToInventory(item.item_type, item.amount);
    }

    public int GetItemAmountInInventory(E_Inventory_Item_Type item_Type)
    {
        foreach (InventoryItem item in items)
        {
            if (item.item_type == item_Type)
            {
                return item.amount;
            }
        }
        return 0;
    }
    

    public InventoryItem[] GetInventoryItems()
    {
        return items.ToArray();
    }

    public void ClearInventory()
    {
        items.Clear();
    }

    public void RemoveAllItem(InventoryItem item)
    {
        items.Remove(item);
    }

    public bool RemoveItem(E_Inventory_Item_Type item_Type, int amount)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].item_type == item_Type)
            {
                if (items[i].amount < amount)
                    return false; // Not enough items to remove

                items[i].amount -= amount;
                if (items[i].amount == 0)
                    items.RemoveAt(i);

                return true; // Successfully removed items
            }
        }
        return false; // Item not found in inventory
    }
}
