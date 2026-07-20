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

    // Listen for workstation keystrokes (physical agent / P1) only while this keyboard is active, and drop
    // the handler on disable so a destroyed keyboard leaves no dangling subscription.
    private void OnEnable()
    {
        WorkstationKeyPressEvents.KeyPressed += OnWorkstationKeyPressed;
    }

    private void OnDisable()
    {
        WorkstationKeyPressEvents.KeyPressed -= OnWorkstationKeyPressed;
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

    // Turn a pressed workstation meronym (e.g. p_key, enter_key) into the text it types, then append it via
    // the SAME OnKeyPressed the on-screen keys use. Non-typeable presses (mouse button, scroll wheel,
    // desktop surface, …) map to null and are ignored.
    private void OnWorkstationKeyPressed(string meronym)
    {
        string text = MeronymToText(meronym);
        if (text != null)
            OnKeyPressed(text);
    }

    private static string MeronymToText(string meronym)
    {
        if (string.IsNullOrEmpty(meronym)) return null;

        // EndsWith so a full object name (e.g. "Meronym_computer keyboard_p_key") also resolves.
        string m = meronym.Trim().ToLowerInvariant();
        if (m.EndsWith("enter_key")) return "\n";

        // "<char>_key" → that single character (p_key → "p", 7_key → "7"); everything else is not typed.
        int ki = m.LastIndexOf("_key", System.StringComparison.Ordinal);
        if (ki > 0)
        {
            string prefix = m.Substring(0, ki);
            int us = prefix.LastIndexOf('_');
            string token = us >= 0 ? prefix.Substring(us + 1) : prefix;
            if (token.Length == 1) return token;
        }
        return null;
    }
}
