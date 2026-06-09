using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AskQuestion : MonoBehaviour
{
    public static AskQuestion SAskQuestion;

    public event Action<int> OnQuestionAnswered;

    [SerializeField] private GameObject questionPanel;
    [SerializeField] private Image bgPanel;
    [SerializeField] private Button close;
    [SerializeField] private Button submit;

    [SerializeField] private TextMeshProUGUI questionTxt;
    [SerializeField] private Button answer1;
    [SerializeField] private TextMeshProUGUI answer1Txt;
    [SerializeField] private Button answer2;
    [SerializeField] private TextMeshProUGUI answer2Txt;
    [SerializeField] private Button answer3;
    [SerializeField] private TextMeshProUGUI answer3Txt;
    [SerializeField] private Button answer4;
    [SerializeField] private TextMeshProUGUI answer4Txt;

    private int _selectedAnswer;

    private void Awake()
    {
        if(SAskQuestion) Destroy(this);
        SAskQuestion = this;
    }

    void Start()
    {
        close.onClick.AddListener(CloseButtonClicked);

        answer1.onClick.AddListener(() => { AnswerClicked(1); });
        answer2.onClick.AddListener(() => { AnswerClicked(2); });
        answer3.onClick.AddListener(() => { AnswerClicked(3); });
        answer4.onClick.AddListener(() => { AnswerClicked(4); });

        submit.onClick.AddListener(OnSubmitClicked);
        CloseButtonClicked();
    }

    public void CloseButtonClicked()
    {
        bgPanel.enabled = false;
        questionPanel.SetActive(false);
        
        UIManager.Instance.ToggleHudInput(true);
    }

    public void ShowQuestion(string question, string ans1, string ans2, string ans3, string ans4)
    {
        questionPanel.SetActive(true);
        bgPanel.enabled = true;
        _selectedAnswer = -1;
        questionTxt.text = question;
        answer1Txt.text = ans1;
        answer2Txt.text = ans2;
        answer3Txt.text = ans3;
        answer4Txt.text = ans4;

        UIManager.Instance.ToggleHudInput(false);
    }

    private void AnswerClicked(int n)
    {
        _selectedAnswer = n;
    }

    public void OnSubmitClicked()
    {
        OnQuestionAnswered?.Invoke(_selectedAnswer);
        Debug.Log("Selected Answer: " + _selectedAnswer);
        CloseButtonClicked();
    }
}
