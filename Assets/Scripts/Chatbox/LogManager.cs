using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Jy_Util;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class LogManager : MonoSingleton<LogManager>
{
    [SerializeField] CanvasGroup chatboXPanel;
    [SerializeField] bool isChatboxShowing = false;
    [SerializeField] float fadeDuration = 0.2f;
    [SerializeField] Transform chatContainer;
    [SerializeField] Transform personContainer;
    [SerializeField] UserAvatar userAvatarPrefab;
    [Header("ChatHeader")]
    [SerializeField] Image currentChatUserAvatar;
    [SerializeField] TMP_Text currentChatUserAvatarText;
    [SerializeField] TMP_Text currentChatUserName;
    [SerializeField] TMP_Text currentChatUserRole;
    private List<ConversationLog> loadedLogs = new List<ConversationLog>();
    private List<UserAvatar> personsInChat = new List<UserAvatar>();
    private bool isFirstTimeChatButtonPressed = true;


    void Start()
    {
        EventManager.AC_OnObjectPickup += OnObjectPicked;
        EventManager.AC_OnObjectDrop += OnObjectDropped;
        UserLogLoader.Instance.AC_onUserLogLoded += OnLogsLoaded;
        UserLogLoader.Instance.AC_onUserDataLoaded += OnUserDataLoaded;

    }

    void OnDisable()
    {
        EventManager.AC_OnObjectPickup -= OnObjectPicked;
        EventManager.AC_OnObjectDrop -= OnObjectDropped;
        UserLogLoader.Instance.AC_onUserLogLoded -= OnLogsLoaded;
        UserLogLoader.Instance.AC_onUserDataLoaded -= OnUserDataLoaded;
    }

    #region ButtonEvents
    public void OnChatButtonPressed()
    {
        chatboXPanel.gameObject.SetActive(true);

        // Kill any previous tween on this target
        chatboXPanel.DOKill();


        chatboXPanel.DOFade(1f, fadeDuration);
        if(isFirstTimeChatButtonPressed)
        {
            isFirstTimeChatButtonPressed = false;
            if(personsInChat.Count > 0)
            {
                personsInChat[0].OnButtonClicked();
            }
        }
    }

    public void OnChatCloseButtonPressed()
    {
        // Kill any previous tween on this target
        chatboXPanel.DOKill();

        chatboXPanel
            .DOFade(0f, fadeDuration)
            .OnComplete(() =>
            {
                chatboXPanel.gameObject.SetActive(false);
            });
    }
    #endregion

    public void ClearLogs()
    {

        for(int i = 0; i < chatContainer.childCount ; i++)
        {
            Destroy(chatContainer.GetChild(i).gameObject);
        }
    }
    public void SetHeader(Sprite avatar,string name,string role)
    {
        currentChatUserName.text = name;
        currentChatUserRole.text = role;
        if(avatar != null )
        {
            currentChatUserAvatar.sprite = avatar;
            currentChatUserAvatar.color = Color.white;
        }
        else
        {
            currentChatUserAvatarText.text = name.Substring(0,1);
            currentChatUserAvatar.sprite = null;
            currentChatUserAvatar.color = GameAsstes.Instance.possibleColorTint[0];
        }
    }
    public void ShowLog(string logMesg,string timestamp,bool isUser)
    {
        // ChatBubble temp_HoldingChatbubble = Instantiate(GameAsstes.Instance.chatBubblePrefab,chatContainer);
        // temp_HoldingChatbubble.SetChat(logMesg,!isUser);
        // temp_HoldingChatbubble.SetButton();
        Instantiate(GameAsstes.Instance.chatBubblePrefab,chatContainer).GetComponent<ChatBubble>().SetChat(logMesg,TimeFormatUtility.GetTimeOrDateTime(timestamp),!isUser);
        UserLogSender.Instance.SendLog(logMesg,"msg");
    }

    public void AssignTask(string taskLog,int availableTimeForTaskinSeconds,string timestamp,int taskindex)
    {
        TaskChatBubble temp_HoldingChatbubble = Instantiate(GameAsstes.Instance.taskChatBubblePrefab,chatContainer);
        temp_HoldingChatbubble.SetChat(taskLog,TimeFormatUtility.GetTimeFormatedSecondstoString(availableTimeForTaskinSeconds),TimeFormatUtility.GetTimeOrDateTime(timestamp),true,taskindex);
        UserLogSender.Instance.SendLog(taskLog,"task");
    }

    public void LoadPrevLog(string logMesg,string timestamp,bool isUser)
    {
        Instantiate(GameAsstes.Instance.chatBubblePrefab,chatContainer).GetComponent<ChatBubble>().SetChat(logMesg,TimeFormatUtility.GetTimeOrDateTime(timestamp),!isUser);
    }

    public void LoadPrevTask(string taskLog,string availableTimeForTask,string timestamp,int taskindex)
    {
        TaskChatBubble temp_HoldingChatbubble = Instantiate(GameAsstes.Instance.taskChatBubblePrefab,chatContainer);
        temp_HoldingChatbubble.SetChat(taskLog,availableTimeForTask,TimeFormatUtility.GetTimeOrDateTime(timestamp),true,taskindex);
        temp_HoldingChatbubble.SetTaskAvilableStatus(false);
    }


    #region EventListener

    void OnObjectPicked(string ownerName,string objName)
    {
        
    }

    void OnObjectDropped(string ownerName,string objName)
    {
        
    }

    #endregion


    public void OnLogsLoaded(List<ConversationLog> logs)
    {
        string tempHoldingLastuserID="";
        loadedLogs = logs;
        /*
        foreach (var log in logs)
        {
            string otherusersID = (log.sender_user_id == myUid)? log.receiver_user_id: log.sender_user_id;
            if(tempHoldingLastuserID == otherusersID) continue; //skip if the current id is same as prev


            List<ConversationLog> result = new List<ConversationLog>();
            for(int i =0 ; i< logs.Count;i++)
            {
                if(otherusersID == logs[i].sender_user_id || otherusersID == logs[i].receiver_user_id)
                {
                    result.Add(logs[i]);
                }
            }
            tempHoldingLastuserID = otherusersID;
            SpawnPersonContainer(result,otherusersID);

        }
        */
    }
    public void OnUserDataLoaded(List<ConversationUser> users)
    {
        if (users == null || users.Count == 0)
            return;

        foreach (var user in users)
        {
            if(user.user_id == GameAsstes.Instance.simulationConfigSO.userId) continue; //skip my self
            // Collect logs for this user
            List<ConversationLog> userLogs = loadedLogs
                .FindAll(l =>
                    l.sender_user_id == user.user_id ||
                    l.receiver_user_id == user.user_id);

            SpawnPersonContainer(userLogs, user);
        }
    }

   
    void SpawnPersonContainer(List<ConversationLog> logs, ConversationUser user)
    {
        UserAvatar container =
            Instantiate(userAvatarPrefab, personContainer);

        container.Setup(logs, user.user_id);
        container.SetName(user.name,user.role);

        personsInChat.Add(container);

        if (!string.IsNullOrEmpty(user.avatar))
        {
            StartCoroutine(DownloadAvatar(user.avatar, container));
        }
    }

    IEnumerator DownloadAvatar(string imageUrl, UserAvatar container)
    {
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(imageUrl))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("Avatar download failed: " + request.error);
                yield break;
            }

            Texture2D texture =
                DownloadHandlerTexture.GetContent(request);

            Sprite avatarSprite = Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f)
            );

            container.SetAvatar(avatarSprite);
        }
    }


}
