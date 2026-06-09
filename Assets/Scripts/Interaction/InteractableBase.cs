using System;
using NaughtyAttributes;
using Photon.Pun;
using UnityEngine;

public abstract class InteractableBase : MonoBehaviourPun, IInteractable
{
    [Header("Pointer UI")]
    [SerializeField] protected InteractPointerWidget pointerWidget;

    
    [Foldout("Interaction Info")] [SerializeField] private string interactKey = "E";
    [Foldout("Interaction Info")] [SerializeField] protected string headerText = "Interact";
    [Foldout("Interaction Info")] [SerializeField][TextArea] protected string toolTipText = "";
    [Foldout("Interaction Info")] [SerializeField] private E_Interact_Type interactionType = E_Interact_Type.Press;

    private bool isHovered = false;
    public bool b_CanInteract = true;
    protected Sprite iconSprite;
    public Action OnObjectInteracted;
    public PhotonView pv;

    public bool canInteract
    {
        get => b_CanInteract;
        set => b_CanInteract = value;
    }


    public virtual void ShowPointer()
    {
        if (!b_CanInteract) return;
        if (pointerWidget == null)
        {
            pointerWidget = GetComponentInChildren<InteractPointerWidget>();
            if (pointerWidget == null) return; //still null then go back
        }
        pointerWidget.ShowPointerWidget();
    }

    public virtual void HidePointer()
    {
        if (pointerWidget == null) return;
        pointerWidget.HidePointerWidget();
    }

    public virtual void Hover()
    {
        if (!b_CanInteract) return;
        if (pointerWidget == null || isHovered) return;

        pointerWidget?.ShowFullPanelStatus(
            true,
            interactKey,
            headerText,
            toolTipText,iconSprite
        );

        isHovered = true;
    }

    public virtual void UnHover()
    {
        if (pointerWidget == null || !isHovered) return;

        pointerWidget.ShowFullPanelStatus(false, "", "", "",iconSprite);
        isHovered = false;
    }

    public virtual void Interact(GameObject interctingObject)
    {
        // To be overridden by derived class if needed
    }
    public virtual void DeInteract(GameObject interactingObject)
    {
        
    }

    public virtual void PushInteractStatus(float status)
    {
        if (pointerWidget == null) return;
        pointerWidget.SetFillAmount(status);
    }

    public virtual E_Interact_Type GetInteractType()
    {
        return interactionType;
    }

    // Optional: Helper to dynamically change text
    protected void UpdateTooltipText(string newText)
    {
        toolTipText = newText;
        if (isHovered)
        {
            pointerWidget.ShowFullPanelStatus(true, interactKey, headerText, toolTipText,iconSprite);
        }
    }
    protected void UpdateHeaderText(string newText)
    {
        headerText = newText;
        if (isHovered)
        {
            pointerWidget.ShowFullPanelStatus(true, interactKey, headerText, toolTipText,iconSprite);
        }

    }
}
