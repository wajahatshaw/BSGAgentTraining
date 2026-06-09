using UnityEngine;

[CreateAssetMenu(menuName ="GAME/Feedback/Tranform/TF_Position")]
public class TF_Transform_Position : FB_Transform
{
    public TF_Transform_Position(FB_Transform fb_TranformBase) : base(fb_TranformBase)
    {
         
    }

    public override void OnFeedbackActiavte()
    {
        base.OnFeedbackActiavte();
        if(currentFeedbackManager)
        {
            evalutedVector=currentFeedbackManager.targetTramform.localPosition;
           EvaluateTimeline();
        }
    }



    public override void EffectLocal()
    {
       currentFeedbackManager.targetTramform.localPosition=evalutedVector;
        
    }


    public override void EffectGlobal()
    {
        currentFeedbackManager.targetTramform.position=evalutedVector;
    }

    public override FeedbackBase CloneMe()
    {
        return new TF_Transform_Position(this);
    }
}
