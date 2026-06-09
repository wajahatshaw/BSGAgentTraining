using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TaskBase : MonoBehaviour
{

    public int TaskNo = -1;
    public Action AC_OnTaskFinished;
    public Action AC_AllSubtaskFinihsed;
    public bool b_TaskDone = false;
    public List<TaskBase> subtaskList = new List<TaskBase>();
    public int timeToCompleteinSeceonds = 120;

    protected int myTaskIndex = 0 ;
    [Space]
    [SerializeField]protected string taskAssignerName;
    [SerializeField]protected string taskAssignerDesignation;
    [SerializeField]protected string taskDescription;


    [HideInInspector] public TaskManagerBase taskManager;

    public void ActivateTask(TaskManagerBase taskManagerBase,int taskIndex)
    {
        taskManager = taskManagerBase;
        myTaskIndex = taskIndex;
        OnTaskActivate();
    }
    
    #region VIRTUAL FUN
    public virtual void OnTaskActivate()
    {
        if (RagPhysicalAgentLocalMode.ShouldUseRagPhysicalAgentMode())
            return;

        UIManager.Instance.PopupTaskPopup(true,taskAssignerName,taskAssignerDesignation,taskDescription,timeToCompleteinSeceonds,myTaskIndex);
        EventManager.AC_OnTaskAccpet += OnTaskAccept;
        EventManager.AC_OnTaskReject += OnTaskReject;
    }

    public virtual void OnTaskDeactivate()
    {

    }

    public virtual void OnTaskAccept(int AccpetedTaskIndex)
    {
        if(AccpetedTaskIndex != myTaskIndex) return;

        UIManager.Instance.PopupTaskPopup(false,taskAssignerName,taskAssignerDesignation,taskDescription,timeToCompleteinSeceonds,myTaskIndex);
        EventManager.AC_OnTaskAccpet -= OnTaskAccept;
        EventManager.AC_OnTaskReject -= OnTaskReject;
        //Time
        CountdownTimer.Instance.AddTime(timeToCompleteinSeceonds);

    }

    public virtual void OnTaskReject(int RejectedTaskIndex)
    {
        if(RejectedTaskIndex != myTaskIndex) return;
        UIManager.Instance.PopupTaskPopup(false,taskAssignerName,taskAssignerDesignation,taskDescription,timeToCompleteinSeceonds,-1);
        EventManager.AC_OnTaskAccpet -= OnTaskAccept;
        EventManager.AC_OnTaskReject -= OnTaskReject;
    }
    public virtual void OnTaskTimeFaild()
    {
        TaskDeactive();
    }

    #endregion
    public void TaskComplete()
    {
        AC_OnTaskFinished?.Invoke();
        
        CountdownTimer.Instance.StopTimer(); //might remove later but now its making sense
    }

    public void TaskDeactive()
    {
        if (!b_TaskDone) OnTaskDeactivate();
        b_TaskDone = true;
    }


    #region  Subtask
    int currentSubtaskIndex = 0;

    public void InitateSubtask(int index)
    {
        if (subtaskList.Count > index)
        {
            subtaskList[index].ActivateTask(taskManager,index);
        }
    }

    
    public void InitateSubtask()
    {
        if (subtaskList.Count < currentSubtaskIndex)
        {
            subtaskList[currentSubtaskIndex].AC_OnTaskFinished += SubtaskFinished;
            subtaskList[currentSubtaskIndex].ActivateTask(taskManager,currentSubtaskIndex);
        }
        else
        {
            AC_AllSubtaskFinihsed?.Invoke();
        }
    }

    void SubtaskFinished()
    {
        subtaskList[currentSubtaskIndex].AC_OnTaskFinished -= SubtaskFinished;
        currentSubtaskIndex++;
        InitateSubtask();
    }

    public void ForceEndAllSubtask()
    {
        foreach (TaskBase item in subtaskList)
        {
            item.TaskDeactive();
            item.TaskComplete();
        }


    }



    #endregion


}