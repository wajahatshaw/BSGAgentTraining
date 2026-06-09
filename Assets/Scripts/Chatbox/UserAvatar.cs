using System;
using System.Collections.Generic;
using System.Linq;
using Jy_Util;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UserAvatar : MonoBehaviour
{
   [SerializeField] string uid_user;
   [SerializeField] TMP_Text userName;
   [SerializeField] TMP_Text userDesignation;
   [SerializeField] Image userAvater;
   [SerializeField] TMP_Text userAvaterText;
   [SerializeField] TMP_Text lastMessgText;
   [SerializeField] TMP_Text lastMessgTime; 
   [SerializeField] int lastMessgVisibleLetterLength = 10;   
   [SerializeField] GameObject redDot;
   public bool isShowingChat = false;
   [SerializeField] private List<ConversationLog> conversationLogs = new List<ConversationLog>();



    public void Setup(List<ConversationLog> mylogs,string myUid)
    {
        conversationLogs = mylogs;
        uid_user = myUid;
        UpdateContainerVisual();
        redDot.SetActive(true);
    }
    public void OnButtonClicked()
    {
        redDot.SetActive(false);
        if(isShowingChat) return;
        isShowingChat = true;

        GameAsstes.Instance.Event_OnNewUserContainerClicked.Raise(this,true); //notify
        LoadConvolog();
    }

    void LoadConvolog()
    {
        LogManager.Instance.ClearLogs();
        LogManager.Instance.SetHeader(userAvater.sprite,userName.text,userDesignation.text);

        foreach(ConversationLog item in conversationLogs)
        {
            if(item.msg_type == "msg")
            {
                LogManager.Instance.LoadPrevLog(item.message,item.timestamp,(item.receiver_user_id == uid_user));
            }
            else
            {
                LogManager.Instance.LoadPrevTask(item.message,"1:20",item.timestamp,0);
            }
        }
        UpdateContainerVisual();
        
    }

    void UpdateContainerVisual()
    {
        if(conversationLogs.Count <= 0)
        {
            lastMessgText.text = "";
            return;
        }

       
        int lastMessgLength = 0;
        lastMessgLength = Math.Min(conversationLogs[conversationLogs.Count-1].message.Length,lastMessgVisibleLetterLength);
        lastMessgText.text = conversationLogs[conversationLogs.Count-1].message.Substring(0,lastMessgLength);
        lastMessgTime.text = TimeFormatUtility.GetDayLabel(conversationLogs[conversationLogs.Count-1].timestamp);
    }








    public void SetName(string name,string designation)
    {
        userName.text = name;
        userDesignation.text = designation;
        SetAvatar(null);
    }

    public void SetAvatar(Sprite sprite)
    {
        if(sprite == null)
        {
            userAvaterText.text = userName.text.Substring(0,1);
            userAvater.color = GameAsstes.Instance.possibleColorTint[0];
            return;
        }
        userAvater.sprite = sprite;
        userAvater.color = Color.white;
    }


    public void ListenToOnNewUserContainerClicked(Component sender,object data)
    {
        if(sender != this)
        {
            isShowingChat = false;
        }
    }
}
