
using UnityEngine;
using Jy_Util;


[CreateAssetMenu(menuName = "GAME/InventorySCO")]
public class InventorySCO : ScriptableObject
{
    public string itemName;
    [TextArea(3,5)]
    public string itemDescription;
    public Sprite icon;
    public bool isStackable = true;

    //[ShowIf(nameof(isStackable))]
    public int stackUpto = 64;
    public E_Inventory_Item_Type itemType;
    public float itemWeight=1;
    public PickableBase perfabRef;
}
