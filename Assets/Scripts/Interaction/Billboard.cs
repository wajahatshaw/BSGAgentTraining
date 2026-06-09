using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Billboard : MonoBehaviour
{
    [SerializeField] Transform cameraTransfomr;
    [SerializeField] Transform myTransform;
    public bool IsActive=false;
    
    
    void Start()
    {
        EventManager.OnChapterStartEvent += OnChapterStart;
        this.enabled = false;
        
    }
    

    void OnChapterStart()
    {
        EventManager.OnChapterStartEvent -= OnChapterStart;
        if(!cameraTransfomr)
        {
            cameraTransfomr=GameObject.FindGameObjectWithTag("MainCamera").transform;
        }
        if(!myTransform)
        {
            myTransform=transform;
        }
        this.enabled = true;
    }

    
    void FixedUpdate()
    {
        if(IsActive)
            myTransform.LookAt(myTransform.position + cameraTransfomr.forward);
    }

}
