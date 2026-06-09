using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EventManager : MonoSingleton<EventManager>
{
    public static Action OnChapterStartEvent;
    public static Action OnChapterEndEvent;
    public static Action Init;

    //public Action<TeleportPoint> AC_OnTeleportDone;
    public Action AC_OnTeleportInitate;
    //public static Action<PickupBase> AC_ObjectDropped;
    
    public static Action<int, bool, bool> AC_PlaceItemStatusChanged;

    public Action<bool> AC_OnPickableHoved;

    #region Task
    public static Action<int> AC_OnTaskAccpet;
    public static Action<int> AC_OnTaskReject;
    public static Action AC_OnMotorTaskInteracted;
    public static Action<int> AC_OnMotorQuizAnswered;
    #endregion

    #region  LOG
    public static Action<string,string> AC_OnObjectPickup;
    public static Action<string,string> AC_OnObjectDrop;
    #endregion

    #region ActionList
    public static Action<int> AC_SelectedAction;
    #endregion

    public static Action<InventorySCO> AC_inventoryItemEquip;
}
