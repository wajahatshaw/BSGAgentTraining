using UnityEngine;
using UnityEngine.UI;

public class UIInputController : MonoBehaviour
{
    [Header("UI References")]
    public Button nextStepButton;
    public Button previousStepButton;
    public Button executeStepButton;
    
    [Header("Navigation")]
    public KeyCode nextStepKey = KeyCode.RightArrow;
    public KeyCode previousStepKey = KeyCode.LeftArrow;
    public KeyCode executeStepKey = KeyCode.Space;
    
    private SceneUILoader sceneLoader;
    
    void Start()
    {
        sceneLoader = GetComponent<SceneUILoader>();
        if (sceneLoader == null)
        {
            sceneLoader = FindObjectOfType<SceneUILoader>();
        }
        
        SetupButtons();
    }
    
    void SetupButtons()
    {
        if (nextStepButton != null)
        {
            nextStepButton.onClick.AddListener(() => {
                if (sceneLoader != null) sceneLoader.NextStep();
            });
        }
        
        if (previousStepButton != null)
        {
            previousStepButton.onClick.AddListener(() => {
                if (sceneLoader != null) sceneLoader.PreviousStep();
            });
        }
        
        if (executeStepButton != null)
        {
            executeStepButton.onClick.AddListener(() => {
                ExecuteCurrentStep();
            });
        }
    }
    
    void Update()
    {
        // Handle keyboard input
        if (Input.GetKeyDown(nextStepKey))
        {
            if (sceneLoader != null) sceneLoader.NextStep();
        }
        
        if (Input.GetKeyDown(previousStepKey))
        {
            if (sceneLoader != null) sceneLoader.PreviousStep();
        }
        
        if (Input.GetKeyDown(executeStepKey))
        {
            ExecuteCurrentStep();
        }
        
        // Update button states
        UpdateButtonStates();
    }
    
    void UpdateButtonStates()
    {
        if (sceneLoader == null) return;
        
        // Update navigation buttons
        if (nextStepButton != null)
        {
            nextStepButton.interactable = sceneLoader.GetCurrentStep() != null;
        }
        
        if (previousStepButton != null)
        {
            previousStepButton.interactable = true; // Always allow going back
        }
        
        // Update execute button
        if (executeStepButton != null)
        {
            bool canExecute = sceneLoader.CanExecuteCurrentStep();
            executeStepButton.interactable = canExecute;
            
            // Update button text based on state
            Text buttonText = executeStepButton.GetComponentInChildren<Text>();
            if (buttonText != null)
            {
                buttonText.text = canExecute ? "Execute Step" : "Step Locked";
            }
        }
    }
    
    void ExecuteCurrentStep()
    {
        if (sceneLoader == null) return;
        
        var currentStep = sceneLoader.GetCurrentStep();
        if (currentStep == null) return;
        
        if (sceneLoader.CanExecuteCurrentStep())
        {
            Debug.Log($"Executing step: {currentStep.stepId}");
            
            // Here you would implement the actual step execution logic
            // For now, just log the action
            Debug.Log($"Action: {currentStep.actionVerb} on {currentStep.tool}");
            Debug.Log($"Duration: {currentStep.estimatedDuration} minutes");
            
            // You could trigger animations, sound effects, or other feedback here
        }
        else
        {
            Debug.Log("Cannot execute current step - dependencies not met");
        }
    }
}
