using System;
using UnityEngine;

public class Motor : InteractableBase
{
    private Outline outline;
    

    void Start()
    {
        outline = GetComponent<Outline>();
    }

    


    public override void Hover()
    {
        base.Hover();
        if(outline) outline.enabled = true;
    }


    public override void UnHover()
    {
        base.UnHover();
        if(outline) outline.enabled = false;
    }


    public override void Interact(GameObject interctingObject)
    {
        base.Interact(interctingObject);
        OnObjectInteracted?.Invoke();       
    }
}
