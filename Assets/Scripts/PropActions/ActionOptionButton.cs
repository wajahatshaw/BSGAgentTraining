using TMPro;
using UnityEngine;

public class ActionOptionButton : MonoBehaviour
{
    [SerializeField] TMP_Text optionText;
    [SerializeField] int optionId = -1;

    public void SetUp(string optionText,int optionId)
    {
        this.optionId = optionId;
        this.optionText.text = optionText;
    }

    public void OnButtonpressed()
    {
        EventManager.AC_SelectedAction.Invoke(optionId);
    }
}
