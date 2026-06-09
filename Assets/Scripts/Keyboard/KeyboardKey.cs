using UnityEngine;
using TMPro;
using UnityEngine.Events;

public class KeyboardKey : MonoBehaviour,IInteractable
{
    public bool b_CanInteract = true;
    [Header("Key Values")]
    [SerializeField] private string normalValue;
    [SerializeField] private string shiftedValue;

    [Header("UI")]
    [SerializeField] private TMP_Text keyText;

    public UnityAction<string> OnKeyPressed;

    private bool isShifted;

    public bool canInteract
    {
        get => b_CanInteract;
        set => b_CanInteract = value;
    }

    private void Start()
    {
        UpdateKeyVisual();
    }

    void PressKey()
    {
        string value = isShifted ? shiftedValue : normalValue;
        OnKeyPressed?.Invoke(value);
    }

    public void SetShiftState(bool shifted)
    {
        isShifted = shifted;
        UpdateKeyVisual();
    }

    private void UpdateKeyVisual()
    {
        keyText.text = isShifted ? shiftedValue : normalValue;
    }

    public void ShowPointer()
    {
        
    }

    public void HidePointer()
    {
        
    }

    public void Hover()
    {
        
    }

    public void UnHover()
    {
        
    }

    public void Interact(GameObject interactingObject)
    {
        PressKey();
    }

    public void DeInteract(GameObject interactingObject)
    {
        
    }

    public void PushInteractStatus(float status)
    {
        
    }

    public E_Interact_Type GetInteractType()
    {
        return E_Interact_Type.Press;
    }
}