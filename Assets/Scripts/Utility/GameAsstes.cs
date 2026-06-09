
using UnityEngine;
using Jy_Util;
using System.Collections.Generic;
using UnityEngine.UI;
using Photon.Pun;


public class GameAsstes : MonoSingleton<GameAsstes>
{
    public SimulationConfigSO simulationConfigSO;
    [Header("UI Prefabs")]
    [Header("Chat")]
    public ChatBubble chatBubblePrefab;
    public TaskChatBubble taskChatBubblePrefab;
    public Sprite userChatSprite;
    public Sprite aiChatSprite;
    public GameEvent Event_OnNewUserContainerClicked;
    public UIItemContainer uIItemContainerPrefb;
    [Header("Multiplayer")]
    public List<Color> possibleColorTint = new List<Color>();
    public Sprite waitingSprite;
    public Sprite readySprite;
    [Space]
    [Header("Script Components ref")]
    public PhotonView pv;
    public FixedJoystick fixedJoystick;
    public FixedTouchField fixedTouchField;
    public UIButtonEvents interactButton;
    public MiniMapCameraManager miniMapCameraManager;

    public Sprite taskCompletionStatus_Completed;
    public Sprite taskCompletionStatus_Pending;
    public Sprite taskCompletionStatus_OnGoing;
    public Sprite taskCompletionStatus_Faild;










    
    [SerializeField] List<InventorySCO> inventorySCOs = new List<InventorySCO>();
    public Sprite GetIcon(E_Inventory_Item_Type item_Type)
    {
        foreach (InventorySCO item in inventorySCOs)
        {
            if (item.itemType == item_Type)
            {
                return item.icon;
            }
        }
        return null;
    }
    public InventorySCO GetInventorySCO(E_Inventory_Item_Type item_Type)
    {
        foreach (InventorySCO item in inventorySCOs)
        {
            if (item.itemType == item_Type)
            {
                return item;
            }
        }
        return null;
    }
}
