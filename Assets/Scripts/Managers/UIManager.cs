using System;
using DG.Tweening;
using NaughtyAttributes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoSingleton<UIManager>
{
    [Header("HUD")]
    [SerializeField] GameObject playerBasicHudInput;
    [Space]
    
    [Foldout("inventory")] public GameObject InventoryObject;
    [Foldout("inventory")] public Transform inventoryConatienr;
    [Foldout("inventory")] public GameObject itemDescriptionPanel;
    [Foldout("inventory")] public Image itemDesIcon;
    [Foldout("inventory")] public TMP_Text itemDesHeader;
    [Foldout("inventory")] public TMP_Text itemDesText;
    [Foldout("inventory")] public Button itemEquipButton;
    [Foldout("inventory")] public TMP_Text itemEquipButtonText;
    public Action AC_UIInventoryEquip;

    [Foldout("Task")] [SerializeField] UIPopInoutBase taskPopup;
    [Foldout("Task")] [SerializeField] GameObject taskPopupPanel;
    [Foldout("Task")] [SerializeField] TMP_Text taskAssignerName;
    [Foldout("Task")] [SerializeField] TMP_Text taskAssignerDesignation;
    [Foldout("Task")] [SerializeField] TMP_Text taskDescription;
    [Foldout("Task")] [SerializeField] TMP_Text taskTimeinfo;
    private int popupTaskIndex = 0;

    [Foldout("ChatBox")] [SerializeField] Transform chatContentPanel;
    [Foldout("ChatBox")] [SerializeField] Button sendButton;
    [Foldout("ChatBox")] [SerializeField] TMP_InputField userTextInputField;

    
    [Foldout("Timmer")] [SerializeField] private RectTransform timerRect;
    [Foldout("Timmer")] [SerializeField] private TMP_Text timerText;

    [Foldout("Timmer")] [SerializeField] private float punchScale = 1.2f;
    [Foldout("Timmer")] [SerializeField] private float animDuration = 0.4f;
    [SerializeField] MissionWaypoint missionWaypoint;
    [Foldout("ActionList")] [SerializeField] private GameObject actionPanel;
    [Foldout("ActionList")] [SerializeField] private TMP_Text propNameText;
    [Foldout("ActionList")] [SerializeField] private GameObject actionOptionHolder;
    [Foldout("ActionList")] [SerializeField] private ActionOptionButton actionOptionButtonPrefab;



    
    void Start()
    {
        CountdownTimer.OnLowTimeTick += PulseTimerWarning;
    }

    public void OnInventoryItemEquipPressed()
    {
        AC_UIInventoryEquip?.Invoke();
    }
    public void ToggleHudInput(bool isActive)
    {
        playerBasicHudInput.SetActive(isActive);
    }

    public void SetWaypoint(Transform target,bool isActive,Vector3? offset = null)
    {
        if(missionWaypoint)
        {
            missionWaypoint.gameObject.SetActive(isActive);
            missionWaypoint.target = target;
            if(offset != null)
            {
                missionWaypoint.offset = (Vector3)offset;
            }
        }
    }

    public void PopupTaskPopup(bool isActive,string taskAssignerName,string taskAssignerDesignation,string taskDescription,int timetoCompleteTask ,int taskIndex)
    {
        if (isActive && RagPhysicalAgentLocalMode.ShouldUseRagPhysicalAgentMode())
            return;

        taskPopup.TogglePop(isActive);
        if(!isActive)    return;
        
        this.taskAssignerName.text = taskAssignerName;
        this.taskAssignerDesignation.text = taskAssignerDesignation;
        this.taskDescription.text = taskDescription;
        taskTimeinfo.text = $"Task completion time: <color=yellow>{TimeFormatUtility.GetTimeFormatedSecondstoString(timetoCompleteTask)}</color> min";
        popupTaskIndex = taskIndex;
       
    }


    #region BUTTON EVENTS
    public void TaskAccept()
    {
        EventManager.AC_OnTaskAccpet?.Invoke(popupTaskIndex);
    }

    public void TaskReject()
    {
        EventManager.AC_OnTaskReject?.Invoke(popupTaskIndex);
    }



    #endregion

    #region Timmer
    [NaughtyAttributes.Button]
    void PulseTimer()
    {
        PulseTimerWarning(1);
    }
    public void PulseTimerWarning(float timeRemaning)
    {
        if (timerRect == null || timerText == null)
            return;

        // Kill existing animation (spam safe)
        timerRect.DOKill();
        timerText.DOKill();

        Vector3 originalScale = timerRect.localScale;
        Color originalColor = timerText.color;

        float half = animDuration * 0.5f;

        Sequence seq = DG.Tweening.DOTween.Sequence();

        seq.Append(timerRect.DOScale(originalScale * punchScale, half)
            .SetEase(DG.Tweening.Ease.OutBack));

        seq.Join(timerText.DOColor(Color.red, half));

        seq.Append(timerRect.DOScale(originalScale, half)
            .SetEase(DG.Tweening.Ease.InBack));

        seq.Join(timerText.DOColor(originalColor, half));
    }

    public void UpdateTimerUIdata(string time)
    {
        timerText.text = "Task Time: "+time+ " min";
    }
    #endregion


}
