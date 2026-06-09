using System;
using System.Collections;
using System.Collections.Generic;
using NaughtyAttributes;

using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
//using UnityEngine.Rendering.Universal;


public class FeedBackManager : MonoBehaviour
{
    [Header("Ref")]
    public Transform targetTramform;

    [Space]
    [Header("Settings")]
    public bool overrideRemaps=false;
    [ShowIf("overrideRemaps")]
    public float curveZeroRemap=0f;
    [ShowIf("overrideRemaps")]
    public float curveOneRemap=0f;
    [SerializeField] bool isSequencialFlow=true;
    [ShowIf("isSequencialFlow")]
    [SerializeField] int startIndex=0;

    [Space]
    [Header("Feedbacks")]
    [OnValueChanged("Start")]
    public List<FeedbackBase> feedbackList=new List<FeedbackBase>();

    public UnityEvent OnCompletePlayingFeedback;



    private int playingFeedbackIndexForSeq=-1;
    private bool isAlreadyPlayingFeedback=false;
    public Action CompletePlayingFeedback;
    private List<Component> compList=new List<Component>();
    private List<FeedbackBase> tempInsteanceOfFeedback=new List<FeedbackBase>();
   
    void Start()
    {
        tempInsteanceOfFeedback.Clear();
        CopyList();

        if(targetTramform) compList.Add(targetTramform);
        else compList.Add(transform);
        compList.Add(this);

    }

    void CopyList()
    {
        
        
        foreach(FeedbackBase item in feedbackList)
        {
            FeedbackBase tempHoldingFeedback = item.CloneMe();
            if(tempHoldingFeedback)
                tempInsteanceOfFeedback.Add(tempHoldingFeedback);
        }
    }
    [NaughtyAttributes.Button]
    public void PlayFeedback()
    {
        if(feedbackList.Count<=0){
            Debug.Log("No feedback to play");
            return;
        }
        if(isSequencialFlow)
        {
            playingFeedbackIndexForSeq=
            startIndex;
            InitiateFeedbackseq();
        }else
            InitiateFeedbackParallel();
    }



    void InitiateFeedbackParallel()
    {
        float longestDuration = 0.01f;

        for(int i=startIndex;i<feedbackList.Count;i++)
        {
            if(tempInsteanceOfFeedback[i].duration > longestDuration) longestDuration = tempInsteanceOfFeedback[i].duration;
            tempInsteanceOfFeedback[i].PushNeededComponent(compList);
            tempInsteanceOfFeedback[i].OnFeedbackActiavte();
        }

        Invoke(nameof(ParallelTaskCompleteed),longestDuration); //when all task initiated at the same time fire event after the longest task duration 
    }

    void ParallelTaskCompleteed()
    {
        CompletePlayingFeedback?.Invoke();
        OnCompletePlayingFeedback?.Invoke();
    }

    void InitiateFeedbackseq()
    {
        if(playingFeedbackIndexForSeq!=startIndex)
            tempInsteanceOfFeedback[playingFeedbackIndexForSeq-1].feedbackFinishedExe-=InitiateFeedbackseq;

       
        //when its the last feedback
        if(tempInsteanceOfFeedback.Count<=playingFeedbackIndexForSeq)
        {
            Debug.Log("Feedback comeple");
            playingFeedbackIndexForSeq=-1;
            isAlreadyPlayingFeedback=false;
            //raise event on Complete
            CompletePlayingFeedback?.Invoke();
            OnCompletePlayingFeedback?.Invoke();
            return;
        }

       
        tempInsteanceOfFeedback[playingFeedbackIndexForSeq].PushNeededComponent(compList);
        tempInsteanceOfFeedback[playingFeedbackIndexForSeq].OnFeedbackActiavte();
        tempInsteanceOfFeedback[playingFeedbackIndexForSeq].feedbackFinishedExe+=InitiateFeedbackseq;
        playingFeedbackIndexForSeq++;
    }
}
