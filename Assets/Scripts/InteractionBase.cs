using UnityEngine;

public class InteractionBase : MonoBehaviour
{
    [Header("InteractionBase")]
    [SerializeField] protected GameObject ui;

    [SerializeField] protected bool canShowUiOnEnter = true;
    private bool _bActive;

    protected virtual void Start()
    {
        ui.SetActive(false);
    }

    private void OnTriggerEnter(Collider other)
    {
        var playerMovement = other.gameObject.transform.parent.GetComponent<PlayerMovement>();
        if (!playerMovement)
            return;

        if(canShowUiOnEnter)
            ui.SetActive(true);

        playerMovement.OnInteract += OnInteract;
        OnDopaTriggerEnter(playerMovement);
    }

    protected virtual void OnDopaTriggerEnter(PlayerMovement other)
    {

    }

    private void OnTriggerExit(Collider other)
    {
        var playerMovement = other.gameObject.transform.parent.GetComponent<PlayerMovement>();
        if (!playerMovement) return;

        playerMovement.OnInteract -= OnInteract;

        ui.SetActive(false); 
        _bActive = false;
        OnDopaTriggerExit(playerMovement);
    }

    protected virtual void OnDopaTriggerExit(PlayerMovement other)
    {

    }

    private void OnInteract()
    {
        _bActive = !_bActive;
        OnDopaInteract(_bActive);
    }

    protected virtual void OnDopaInteract(bool bActive)
    {

    }
}