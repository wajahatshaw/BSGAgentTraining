using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TaskChatBubble : ChatBubble
{
    private int taskIndex = 0 ;
    [SerializeField] Button acceptButton;
    [SerializeField] Button rejectButton;
    [SerializeField] TMP_Text taskTimeInfoText;

    public void OnTaskAcceptButtonPressed()
    {
        EventManager.AC_OnTaskAccpet?.Invoke(taskIndex);
    }

    public void OnTaskRejectButtonPressed()
    {
        EventManager.AC_OnTaskReject?.Invoke(taskIndex);
    }
    public void SetChat(string message, string availableTimeForTask, string timestamp, bool isAi, int index)
    {
        SetChat(message, timestamp, isAi);

        taskTimeInfoText.text = 
            $"Task completion time: <color=yellow>{availableTimeForTask}</color> min";

        taskIndex = index;
    }

    public void SetTaskAvilableStatus(bool val)
    {
        if(!val)
        {
            acceptButton.interactable = false;
            rejectButton.interactable = false;
        }
    }
}
