using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using DG.Tweening;
using System;


public class FB_Transform : FeedbackBase
{
    //[HideInInspector]
//    public Transform targetTranform;

    public bool effectX,effectY,effectZ;
    public AnimationCurve curveX,curveY,curveZ;
    [Tooltip("Curve 0 and 1 values will be reamped in this values and set to target transform")]
    public float reampCurveZero=0f;
    public float reampCurveOne=0f;
    
    public bool effectLocal=false;
    [HideInInspector]
    public  Vector3 evalutedVector;
    protected FeedBackManager currentFeedbackManager;
    private bool isAlreadyOverrideDone=false;

    public FB_Transform(FB_Transform fb_TranformBase) : base(fb_TranformBase)
    {
       
        effectX = fb_TranformBase.effectX;
        effectY = fb_TranformBase.effectY;
        effectZ = fb_TranformBase.effectZ;

        curveX = fb_TranformBase.curveX;
        curveY = fb_TranformBase.curveY;
        curveZ = fb_TranformBase.curveZ;

        reampCurveOne = fb_TranformBase.reampCurveOne;
        reampCurveZero = fb_TranformBase.reampCurveZero;

        effectLocal = fb_TranformBase.effectLocal;

    }


    void EvaluteCurveX()
    {
        Sequence sx= DOTween.Sequence();
        
        sx.Append(DOVirtual.Float(0, 1f, duration, v =>PerformEffectX(v) )).onComplete=EndExe;
    }

    void EvaluteCurveY()
    {
        Sequence sy= DOTween.Sequence();
        
        sy.Append(DOVirtual.Float(0, 1f, duration, v =>PerformEffectY(v) )).onComplete=EndExe;
    }

    void EvaluteCurveZ()
    {
        Sequence sz= DOTween.Sequence();
        
        sz.Append(DOVirtual.Float(0, 1f, duration, v =>PerformEffectZ(v) )).onComplete=EndExe;
    }

  

    void EndExe()=>feedbackFinishedExe?.Invoke();





    #region  Copy




    public override void PerformeEffect(float val)
    {
        
    }

    public virtual void EffectLocal()
    {
       currentFeedbackManager.targetTramform.localScale=evalutedVector;
        
    }


    public virtual void EffectGlobal()
    {
        currentFeedbackManager.targetTramform.localScale=evalutedVector;
    }

    public virtual void PerformEffectX(float val)
    {
        float curveEvaluteval=curveX.Evaluate(val);
        float remapedVal=Mathf.Lerp(reampCurveZero,reampCurveOne,curveEvaluteval);

        evalutedVector.x=remapedVal;

        if(effectLocal) EffectLocal();
        else EffectGlobal();
    }
    public virtual void PerformEffectY(float val)
    {
        float curveEvaluteval=curveY.Evaluate(val);
        float remapedVal=Mathf.Lerp(reampCurveZero,reampCurveOne,curveEvaluteval);

        evalutedVector.y=remapedVal;

        if(effectLocal) EffectLocal();
        else EffectGlobal();
    }

    public virtual void PerformEffectZ(float val)
    {
        float curveEvaluteval=curveZ.Evaluate(val);
        float remapedVal=Mathf.Lerp(reampCurveZero,reampCurveOne,curveEvaluteval);

        evalutedVector.z=remapedVal;

        if(effectLocal) EffectLocal();
        else EffectGlobal();
    }

    private void CheckOverride()
    {
        if (!isAlreadyOverrideDone && currentFeedbackManager != null && currentFeedbackManager.overrideRemaps)
        {
            reampCurveOne = currentFeedbackManager.curveOneRemap;
            reampCurveZero = currentFeedbackManager.curveZeroRemap;
            isAlreadyOverrideDone = true;
        }
        
    }







#endregion





    #region  Parent_Override

    public override void OnFeedbackActiavte()
    {
        base.OnFeedbackActiavte();
        
        feedbackFinishedExe+=OnFeedbackDeactivate;

    }

    public override void OnFeedbackDeactivate()
    {
        base.OnFeedbackDeactivate();
        evaluteAction-=PerformeEffect;
    }

    public override void EvaluateTimeline()
    {
        if(effectX) EvaluteCurveX();
        if(effectY) EvaluteCurveY();
        if(effectZ) EvaluteCurveZ();
    }



    public override void PushNeededComponent(List<Component> comp)
    {
        

        //when target ref is not set get it from manager
        foreach(Component item in comp)
            if(item is FeedBackManager)
                currentFeedbackManager=(FeedBackManager)item;

        CheckOverride();
            
    }
    #endregion
}


