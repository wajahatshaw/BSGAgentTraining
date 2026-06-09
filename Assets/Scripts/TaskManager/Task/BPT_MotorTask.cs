using EasyPopupSystem;
using UnityEngine;

public class BPT_MotorTask : TaskBase
{
    

    [SerializeField] InteractableBase motor;

    [NaughtyAttributes.Button]
    public void DebugAccepctTask()
    {
        OnTaskAccept(myTaskIndex);
    }



    
    public override void OnTaskAccept(int AccpetedTaskIndex)
    {
        if(AccpetedTaskIndex != myTaskIndex) return;

        base.OnTaskAccept(AccpetedTaskIndex);
        

        motor.OnObjectInteracted += OnMotorInteracted;

        motor.canInteract = true;
        taskManager.SetTaskUidatabyIndex(TaskNo);

        LogManager.Instance.ShowLog("Task Accpeted",System.DateTime.UtcNow.ToString(),true);
        UIManager.Instance.SetWaypoint(motor.transform,true);
    }

    public override void OnTaskReject(int RejectedTaskIndex)
    {
        if(RejectedTaskIndex != myTaskIndex) return;

        base.OnTaskReject(RejectedTaskIndex);
        

        LogManager.Instance.ShowLog("Task Rejcted",System.DateTime.UtcNow.ToString(),true);
    }


    public override void OnTaskActivate()
    {
        base.OnTaskActivate();
        

        LogManager.Instance.AssignTask(taskDescription,timeToCompleteinSeceonds,System.DateTime.UtcNow.ToString(),myTaskIndex);
    }


    public override void OnTaskDeactivate()
    {
        base.OnTaskDeactivate();
        motor.b_CanInteract = false;
        motor.OnObjectInteracted -= OnMotorInteracted;
        UIManager.Instance.SetWaypoint(motor.transform,false);
    }




    void OnMotorInteracted()
    {
        motor.OnObjectInteracted -= OnMotorInteracted;
        motor.canInteract = false;

        UIManager.Instance.SetWaypoint(motor.transform,false);

        EventManager.AC_OnMotorTaskInteracted?.Invoke();

        if (!AskQuestion.SAskQuestion) return;
        AskQuestion.SAskQuestion.OnQuestionAnswered += OnQuestionAnswered;
        AskQuestion.SAskQuestion.ShowQuestion("The motor is not starting when power is supplied. What should you check first?", "Check the power supply", "Replace the motor immediately", "Increase the motor speed setting", "Apply lubrication to the motor shaft");
    }


    private void OnQuestionAnswered(int obj)
    {
        AskQuestion.SAskQuestion.OnQuestionAnswered -= OnQuestionAnswered;
        if(obj == 1)
        {
            EasyPopupManager.Instance.CreateToast(0);
            SkillScoreManager.Instance.AddScore(100);
        }
        else
        {
            EasyPopupManager.Instance.CreateToast(1);
            SkillScoreManager.Instance.RemoveScore(50);
        }

        taskManager.SetTaskUidatabyIndex(TaskNo+1);

        EventManager.AC_OnMotorQuizAnswered?.Invoke(obj);
    }
}
