using UnityEngine;

public class PlayerGrabManager : MonoBehaviour
{
    //[SerializeField] PlayerHand playerHand;
    [SerializeField] Transform playerHandTransform;
    [SerializeField] InventorySCO debugitem;
    private PickableBase holdingObject;

    void Start()
    {
        EventManager.AC_inventoryItemEquip += OnInvItemEquipAc;
    }

    void OnDisable()
    {
        EventManager.AC_inventoryItemEquip -= OnInvItemEquipAc;
    }

    public void OnInvItemEquipAc(InventorySCO inventorySCO)
    {
        Destroy(holdingObject.gameObject);
        
        holdingObject = Instantiate(inventorySCO.perfabRef,playerHandTransform);

        holdingObject.OnGrab(playerHandTransform);


    }

    [NaughtyAttributes.Button]
    public void DebugFun()
    {
        OnInvItemEquipAc(debugitem);
    }
}
