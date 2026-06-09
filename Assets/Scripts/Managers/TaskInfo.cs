using Jy_Util;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TaskInfo : MonoBehaviour
{
    [SerializeField] TMP_Text taskDescText;
    [SerializeField] TMP_Text taskTimeText;
    [SerializeField] TMP_Text performerNameText;
    [SerializeField] Image taskCompleteionStatus;

    public void Init(string taskDesc,int availableTime,string performerName,E_Task_Completion_Status taskStatus)
    {
        taskDescText.text = "Task Dec: "+taskDesc;
        taskTimeText.text = $"Task Time:<color=yellow>{TimeFormatUtility.GetTimeFormatedSecondstoString(availableTime)}</color> min";
        performerNameText.text ="Assigned: " + performerName;

        switch (taskStatus)
        {
            case E_Task_Completion_Status.Pending:
                taskCompleteionStatus.sprite = GameAsstes.Instance.taskCompletionStatus_Pending;
                break;
            case E_Task_Completion_Status.Completed:
                taskCompleteionStatus.sprite = GameAsstes.Instance.taskCompletionStatus_Completed;
                break;
            case E_Task_Completion_Status.OnGoing:
                taskCompleteionStatus.sprite = GameAsstes.Instance.taskCompletionStatus_OnGoing;
                break;
            case E_Task_Completion_Status.faild:
                taskCompleteionStatus.sprite = GameAsstes.Instance.taskCompletionStatus_Faild;
                break;
            default:
                break;
        }
    }

    public void Init(TaskInfoDS taskInfoDS)
    {
        Init(taskInfoDS.taskDesc,taskInfoDS.availableTime,taskInfoDS.performerName,taskInfoDS.taskStatus);
    }
}
