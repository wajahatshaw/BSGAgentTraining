using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class JobAlert : MonoBehaviour
{
    public static JobAlert SJobAlert;

    public Action<bool> OnJobAnswered;

    [SerializeField] private GameObject jobPanel;
    [SerializeField] private Image bgPanel;
    [SerializeField] private Button close;

    [SerializeField] private TextMeshProUGUI headerText;
    [SerializeField] private TextMeshProUGUI sentByText;
    [SerializeField] private TextMeshProUGUI bodyText;

    [SerializeField] private Button acceptButton;
    [SerializeField] private Button declineButton;

    private int _selectedAnswer;

    private void Awake()
    {
        if (SJobAlert) Destroy(this);
        SJobAlert = this;
    }

    void Start()
    {
        close.onClick.AddListener(CloseButtonClicked);

        acceptButton.onClick.AddListener(() => { AnswerClicked(true); });
        declineButton.onClick.AddListener(() => { AnswerClicked(false); });

        CloseButtonClicked();
    }

    private void CloseButtonClicked()
    {
        bgPanel.enabled = false;
        jobPanel.SetActive(false);
    }

    public void ShowAlert(string sentBy, string body, string header = "New Task Alert")
    {
        jobPanel.SetActive(true);
        bgPanel.enabled = true;
        headerText.text = header;
        sentByText.text = sentBy;
        bodyText.text = body;
    }

    private void AnswerClicked(bool accepted)
    {
        OnJobAnswered?.Invoke(accepted);
        Debug.Log("Selected Answer: " + accepted);
        CloseButtonClicked();
    }
}