using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class VirtualKeyboard : MonoBehaviour
{
    [SerializeField] private bool isShifted;
    [SerializeField] TMP_Text outputText;

    [SerializeField] private List<KeyboardKey> keys = new List<KeyboardKey>();

    private void Awake()
    {
        if(keys.Count == 0)
            keys.AddRange(GetComponentsInChildren<KeyboardKey>());
        
    }
    void Start()
    {
        foreach (var key in keys)
        {
            key.OnKeyPressed += OnKeyPressed;
        }
    }

    public void ToggleShift()
    {
        isShifted = !isShifted;
        UpdateKeys();
    }

    public void SetShift(bool value)
    {
        isShifted = value;
        UpdateKeys();
    }

    private void UpdateKeys()
    {
        foreach (var key in keys)
        {
            key.SetShiftState(isShifted);
        }
    }

    public bool IsShifted()
    {
        return isShifted;
    }

    public void OnKeyPressed(string value)
    {
        outputText.text += value;
    }
}