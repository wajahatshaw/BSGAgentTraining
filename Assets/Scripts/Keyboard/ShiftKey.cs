using UnityEngine;

public class ShiftKey : MonoBehaviour,IInteractable
{
    public bool b_CanInteract = true;
    [SerializeField] private VirtualKeyboard keyboard;

    public bool canInteract
    {
        get => b_CanInteract;
        set => b_CanInteract = value;
    }

    public void DeInteract(GameObject interactingObject)
    {
        
    }

    public E_Interact_Type GetInteractType()
    {
        return E_Interact_Type.Press;
    }

    public void HidePointer()
    {
        
    }

    public void Hover()
    {
        
    }

    public void Interact(GameObject interactingObject)
    {
        OnShiftPressed();
    }

    void OnShiftPressed()
    {
        keyboard.ToggleShift();
    }

    public void PushInteractStatus(float status)
    {
        
    }

    public void ShowPointer()
    {
        
    }

    public void UnHover()
    {
        
    }
}