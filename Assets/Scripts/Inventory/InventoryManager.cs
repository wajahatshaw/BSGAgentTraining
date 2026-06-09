using Jy_Util;
using UnityEngine;


[RequireComponent(typeof(InventoryDisplay))]
public class InventoryManager : MonoSingleton<InventoryManager>
{
   private Inventory inventory;
   private InventoryDisplay inventoryDisplay;
   private string dataPath="";
    [SerializeField] E_Inventory_Item_Type debugItemtoAddinInv;


   
    void Start()
    { 
        dataPath = Application.persistentDataPath + "/inventoryData.txt";
        #if UNITY_EDITOR
        dataPath = Application.dataPath + "/inventoryData.txt";
        #endif
        
        LoadInventory();
        ConfigureInventoryDisplay();
        SubcribeToInput(true);

        UIManager.Instance.InventoryObject.SetActive(false);
    }



    #region INPUT
    void SubcribeToInput(bool val)
    {
       
    }

    public void ShowInventory()
    {
        if (UIManager.Instance.InventoryObject.activeSelf)
        {
            UIManager.Instance.InventoryObject.SetActive(false);
            VolumeManager.Instance.SetNormalMode();
        }
        else
        {
            inventoryDisplay.Configure(inventory);
            UIManager.Instance.InventoryObject.SetActive(true);
            VolumeManager.Instance.SetInventoryMode();
            
        }
    }
    

    

    #endregion




    public Inventory GetInventory()
    {
        return inventory;
    }

    private void ConfigureInventoryDisplay()
    {
        inventoryDisplay = GetComponent<InventoryDisplay>();
        inventoryDisplay.Configure(inventory);
    }



    [NaughtyAttributes.Button]
    public void AddDebugItemTOInv()
    {
        AddItemToInventory(debugItemtoAddinInv, 5);
    }
   
    


    [NaughtyAttributes.Button]
    public void ClearInventory()
    {
        inventory.ClearInventory();
        inventoryDisplay.UpdateDisplay(inventory);

        SaveInventory();
    }
   

   private void LoadInventory()
   {
       inventory = SaveAndLoad.Load<Inventory>(dataPath);
       if(inventory == null)
       {
            inventory = new Inventory();
            SaveInventory();
       }
   }

   private void SaveInventory()
   {
        SaveAndLoad.Save<Inventory>(dataPath,inventory);
   }

    #region EVENTS
   
   
    #endregion

    public void AddItemToInventory(E_Inventory_Item_Type item, int amount)
    {
        inventory.AddItemToInventory(item, amount);

        inventoryDisplay.UpdateDisplay(inventory);

        SaveInventory();

    }

    public bool RemoveItemFromInventory(E_Inventory_Item_Type item_type,int itemAmount)
    {
        bool result = inventory.RemoveItem(item_type, itemAmount);;
        inventoryDisplay.UpdateDisplay(inventory);
        return result;
    }
    
    public void AddInventoryToInventory(Inventory inventory)
    {
        InventoryItem[] temp = inventory.GetInventoryItems();

        for(int i=0 ;i< temp.Length; i++)
        {
            this.inventory.AddItemToInventory(temp[i]);
        }
        inventoryDisplay.UpdateDisplay(this.inventory);

        SaveInventory();
    }

}
