using UnityEngine;
using TMPro;
using System;

public class EnterToSend : MonoBehaviour
{
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] bool useStaticAction = false;
    public Action<string> AC_SendUsingEnter;

    void OnGUI()
    {
        if (!inputField.isFocused) return;

        bool pressEnter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
        bool holdShift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (!pressEnter) return;

        // SHIFT + ENTER → New Line
        if (holdShift)
        {
            int caret = inputField.caretPosition;

            inputField.text = inputField.text.Insert(caret, "\n");
            inputField.caretPosition = caret + 1;
            inputField.ForceLabelUpdate();
            return;
        }

        // ENTER alone → SEND MESSAGE
        string msg = inputField.text.Trim();

        if (msg.Length > 0)
        {
            Debug.Log("<color=White> Enter button pressed</color>");
            //if(useStaticAction)ActionManager.AC_OnInputFieldChatSendUsingEnter?.Invoke(msg);
            AC_SendUsingEnter?.Invoke(msg);

        }
        
    }
}
