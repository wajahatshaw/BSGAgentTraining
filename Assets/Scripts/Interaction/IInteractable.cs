using UnityEngine;

public enum E_Interact_Type
{
    Press,
    Hold
}

public interface IInteractable
{
    void ShowPointer();
    void HidePointer();
    void Hover();
    void UnHover();
    void Interact(GameObject interactingObject); // Used for Press type
    void DeInteract(GameObject interactingObject); // Used for Press type
    void PushInteractStatus(float status); // Used for Hold type
    E_Interact_Type GetInteractType();
    bool canInteract{get;set;}
}
