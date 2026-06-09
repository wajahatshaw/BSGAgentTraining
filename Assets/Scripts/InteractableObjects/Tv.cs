using UnityEngine;

public class Tv : InteractableBase
{
    [Header("Tv Interaction")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private GameObject tvSprite;
    private Outline outline;

    void Start()
    {
        tvSprite.SetActive(false);
        outline = GetComponent<Outline>();
    }

    
    private void OnQuestionAnswered(int obj)
    {
        AskQuestion.SAskQuestion.OnQuestionAnswered -= OnQuestionAnswered;

        JobAlert.SJobAlert.ShowAlert("TV Station",
            obj == 1
                ? "Thank you for turning on the TV! Here's your reward."
                // Reward the player
                : "Oh, it seems there was a misunderstanding. Please try again.");
    }


    public override void Hover()
    {
        base.Hover();
        if(outline) outline.enabled = true;
    }


    public override void UnHover()
    {
        base.UnHover();
        if(outline) outline.enabled = false;
    }


    public override void Interact(GameObject interctingObject)
    {
        base.Interact(interctingObject);
        tvSprite.SetActive(true);

        if (!AskQuestion.SAskQuestion) return;
        AskQuestion.SAskQuestion.OnQuestionAnswered += OnQuestionAnswered;
        AskQuestion.SAskQuestion.ShowQuestion("Did you just turn on the TV?", "Yes, I did.", "No, I didn't.", "TvInteraction", "table");
    }
}