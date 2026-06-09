using System.Collections.Generic;
using Jy_Util;
using UnityEngine;
using UnityEngine.UI;

public class TaskInfoListManager : MonoBehaviour
{
    [SerializeField] Button taskListButton;
    [Header("Reference")]
    [SerializeField] GameObject taskListPanel;
    [SerializeField] Transform taskListContainer;
    [SerializeField] TaskInfo taskInfoPrefab;

    [Header("Demo")]
    [SerializeField] List<TaskInfoDS> demoTaskList = new List<TaskInfoDS>();


    
    bool HasRequiredReferences()
    {
        if (taskListPanel == null || taskListContainer == null || taskInfoPrefab == null)
        {
            Debug.LogError(
                "TaskInfoListManager: Assign taskListPanel, taskListContainer, and taskInfoPrefab in the Inspector.",
                this);
            return false;
        }
        return true;
    }

    void ShowTaskInfoList()
    {
        if (!HasRequiredReferences()) return;

        for(int i=0;i<taskListContainer.childCount;i++) Destroy(taskListContainer.GetChild(i));
        
        foreach(TaskInfoDS item in demoTaskList)
        {
            TaskInfo temp = Instantiate(taskInfoPrefab,taskListContainer);

            temp.Init(item);
        }
    }


    void HideTaskInfoList()
    {
        if (!HasRequiredReferences()) return;
        taskListPanel.SetActive(false);
    }

    public void ToggleTaskList()
    {
        if (!HasRequiredReferences()) return;

        if(taskListPanel.activeSelf)
        {
            HideTaskInfoList();
        }
        else
        {
            taskListPanel.SetActive(true);
            ShowTaskInfoList();
        }
    }

}
