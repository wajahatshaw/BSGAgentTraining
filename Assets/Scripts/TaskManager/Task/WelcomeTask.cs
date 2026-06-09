using NaughtyAttributes;
using UnityEngine;

public class WelcomeTask : TaskBase
{
    [SerializeField] bool useWaitTimeInstedofAudio = false;
    [ShowIf("useWaitTimeInstedofAudio")]
    [SerializeField] float waitTime = 5f;
    
    public override void OnTaskActivate()
    {
        base.OnTaskActivate();
        taskManager.SetTaskUidatabyIndex(TaskNo);
        taskManager.guide.AC_OnAudioFinished += FinsihPlyingAudio;

        
        if(useWaitTimeInstedofAudio)
        {
            Invoke(nameof(FinsihPlyingAudio),waitTime);
        }
        
    }

    public override void OnTaskDeactivate()
    {
        base.OnTaskDeactivate();
        TaskComplete();
    }

    void FinsihPlyingAudio()
    {
        taskManager.guide.AC_OnAudioFinished -= FinsihPlyingAudio;
        TaskDeactive();
    }
}
