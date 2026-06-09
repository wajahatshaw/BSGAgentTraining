using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;
using System;
using Photon.Pun;

public class Interactor : MonoBehaviour
{
    [Header("Sphere Cast Settings")]
    public float sphereRadius = 2f;
    public LayerMask interactableLayer;

    [Header("Raycast Settings")]
    public Transform rayOrigin;
    public float rayDistance = 5f;

    [Header("Input Settings")]
    public KeyCode interactKey = KeyCode.E;
    [Range(0.1f,3f)]
    public float holdTimeMultiplier = 1f;

    private List<IInteractable> currentSphereInteractables = new List<IInteractable>();
    private IInteractable currentRayInteractable;
    private float holdTimer = 0f;
    private bool isHolding = false;
    private Action onUpdateDel;

    [Header("UI interact Button")]
    [SerializeField] UIButtonEvents interactButton;
    [SerializeField] Sprite performSprite;
    [SerializeField] Sprite grabSprite;

    private PhotonView playerPV;

    void Awake()
    {
        playerPV = GetComponentInParent<PhotonView>();

        if (!playerPV.IsMine)
        {
            enabled = false; // disable Interactor for remote players
        }
    }


    
    void Start()
    {    
        if(!playerPV.IsMine)
        {
            return;
        }

        
        interactButton = GameAsstes.Instance.interactButton;

        interactButton.OnButtonPressed.AddListener(OnInteractButtonPressed);
        interactButton.OnButtonRelease.AddListener(OnInteractButtonRelease);
        
        onUpdateDel += PerformSphereCheck;
        onUpdateDel += PerformRayCheck;
        EventManager.Instance.AC_OnPickableHoved += OnPickableHover;
        if(interactButton == null)
        {
            onUpdateDel += HandleInteractionInput;
        }
        else
        {
            onUpdateDel += HandleUIButtonInput;

        }
    }
    public void Init(Component sender,object data)
    {
        Debug.Log("Init Recived:"+playerPV.Owner);

        if(!(sender is GameManager)) return;

        if(!playerPV.IsMine)
        {
            Debug.Log("Photon view is not mine:"+playerPV.Owner);
            Debug.Log("Photon view is not mine:"+playerPV.Owner.IsLocal);
            return;
        }
        Debug.Log("Photon view is  mine:"+playerPV.Owner);

        interactButton = GameAsstes.Instance.interactButton;
        
        onUpdateDel += PerformSphereCheck;
        onUpdateDel += PerformRayCheck;
        EventManager.Instance.AC_OnPickableHoved += OnPickableHover;
        if(interactButton == null)
        {
            onUpdateDel += HandleInteractionInput;
        }
        else
        {
            onUpdateDel += HandleUIButtonInput;

        }
    }

    void OnDisable()
    {
        onUpdateDel -= PerformSphereCheck;
        onUpdateDel -= PerformRayCheck;
        EventManager.Instance.AC_OnPickableHoved -= OnPickableHover;
        onUpdateDel -= HandleInteractionInput;
        onUpdateDel -= HandleUIButtonInput;
    }

    void Update()=>onUpdateDel?.Invoke();
    




    void OnPickableHover(bool isPickable)
    {
        interactButton.GetComponent<Image>().sprite = (isPickable)? grabSprite : performSprite;
    }










    void PerformSphereCheck()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, sphereRadius, interactableLayer);

        // Show pointer for current
        List<IInteractable> newDetected = new List<IInteractable>();
        foreach (var col in hits)
        {
            IInteractable interactable = col.GetComponent<IInteractable>();
            if (interactable != null)
            {
                if(!interactable.canInteract) continue;
                
                newDetected.Add(interactable);
                if (!currentSphereInteractables.Contains(interactable))
                {
                    interactable.ShowPointer();
                }
            }
        }

        // Hide pointer for those no longer in range
        foreach (var old in currentSphereInteractables)
        {
            if (!newDetected.Contains(old))
            {
                old.HidePointer();
            }
        }

        currentSphereInteractables = newDetected;
    }

    void PerformRayCheck()
    {
        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, rayDistance, interactableLayer))
        {
            IInteractable interactable = hit.collider.GetComponent<IInteractable>();
            if (interactable != null)
            {
                if (currentRayInteractable != interactable)
                {
                    currentRayInteractable?.UnHover();
                    currentRayInteractable = interactable;
                    currentRayInteractable.Hover();
                }
                return;
            }
        }

        if (currentRayInteractable != null)
        {
            currentRayInteractable.UnHover();
            currentRayInteractable = null;
        }
    }


    #region Input
    void HandleInteractionInput()
    {
        if (currentRayInteractable == null)
            return;

        E_Interact_Type type = currentRayInteractable.GetInteractType();

        if (type == E_Interact_Type.Press)
        {
            if (Input.GetKeyDown(interactKey))
            {
                currentRayInteractable.Interact(this.gameObject);
                currentRayInteractable.PushInteractStatus(1f);
            }
            else if (Input.GetKeyUp(interactKey))
            {
                currentRayInteractable.PushInteractStatus(0f);
                currentRayInteractable.DeInteract(this.gameObject);
            }
        }
        else if (type == E_Interact_Type.Hold)
        {
            if (Input.GetKey(interactKey))
            {
                isHolding = true;
                holdTimer += holdTimeMultiplier * Time.deltaTime;
                float holdProgress = Mathf.Clamp01(holdTimer / 1f); // Customize max hold duration
                currentRayInteractable.PushInteractStatus(holdProgress);
                if (holdTimer >= 1)
                {
                    currentRayInteractable.Interact(this.gameObject);
                }
            }
            else if (isHolding)
            {
                isHolding = false;
                holdTimer = 0f;
                currentRayInteractable.PushInteractStatus(0f); // Reset hold status
            }
        }
    }



    private bool interactPressed;
    private bool interactHeld;
    private bool interactReleased;
    void HandleUIButtonInput()
    {
        if (currentRayInteractable == null)
            return;

        E_Interact_Type type = currentRayInteractable.GetInteractType();

        if (type == E_Interact_Type.Press)
        {
            if (interactPressed)
            {
                currentRayInteractable.Interact(this.gameObject);
                currentRayInteractable.PushInteractStatus(1f);
            }
            else if (interactReleased)
            {
                currentRayInteractable.PushInteractStatus(0f);
                currentRayInteractable.DeInteract(this.gameObject);
            }
        }
        else if (type == E_Interact_Type.Hold)
        {
            if (interactHeld)
            {
                isHolding = true;
                holdTimer += holdTimeMultiplier * Time.deltaTime;

                float holdProgress = Mathf.Clamp01(holdTimer / 1f);
                currentRayInteractable.PushInteractStatus(holdProgress);

                if (holdTimer >= 1f)
                {
                    currentRayInteractable.Interact(this.gameObject);
                }
            }
            else if (isHolding)
            {
                isHolding = false;
                holdTimer = 0f;
                currentRayInteractable.PushInteractStatus(0f);
            }
        }

        // Reset one-frame inputs
        interactPressed = false;
        interactReleased = false;
    }

    public void OnInteractButtonPressed()
    {   
        interactPressed = true;
        interactHeld = true;
    }

    public void OnInteractButtonRelease()
    {
        interactHeld = false;
        interactReleased = true;
    }


    #endregion

}
