using System;
using Photon.Pun;
using UnityEngine;

public class PickableBase : InteractableBase
{
    private Outline outline;
    [Tooltip("When the string field is empty the Header and Tooltip will be taken from this if its not null")]
    [SerializeField] InventorySCO itemInvSCO;
    private FeedBackManager feedBackManager;
    private bool isPicked = false;
    public Action AC_OnObjectPicked ;

    public Transform handRef;
    public GameObject SM_ref;
    [SerializeField] int animationIndex = 1;
    [SerializeField] Animator animator;
    [SerializeField] Transform playerHand;
    

    void Start()
    {
        if(!pv)
        {
            if(TryGetComponent<PhotonView>(out pv));
            else
            {
                pv = gameObject.AddComponent<PhotonView>();
                Debug.Log("Photon view was missing: "+gameObject.name);
            }
        }


        feedBackManager = GetComponent<FeedBackManager>();

        outline = GetComponent<Outline>();
        if(itemInvSCO != null)
        {
            headerText = itemInvSCO.itemName;
            toolTipText = itemInvSCO.itemDescription;
            iconSprite = itemInvSCO.icon;
        }
    }




    public override void Hover()
    {

        base.Hover();
        if(!canInteract) return;
        EventManager.Instance.AC_OnPickableHoved?.Invoke(true);
        if(outline)
        {
            outline.enabled = true;
        }
    }

    public override void UnHover()
    {
        base.UnHover();
        if(!canInteract) return;
        EventManager.Instance.AC_OnPickableHoved?.Invoke(false);

        if(outline)
        {
            outline.enabled = false;
        }
    }

    public override void Interact(GameObject interactingObject)
    {
        if (!canInteract || isPicked) return;

        // Only request, DO NOT play feedback yet
        pv.RPC(
            nameof(RPC_RequestInteract),
            RpcTarget.MasterClient,
            PhotonNetwork.LocalPlayer.ActorNumber
        );
    }


    void OnAnimationCompleteOwner()
    {
        feedBackManager.CompletePlayingFeedback -= OnAnimationCompleteOwner;

        InventoryManager.Instance.AddItemToInventory(
            itemInvSCO.itemType,
            1
        );
    }

    void OnAnimationComplete()
    {
        feedBackManager.CompletePlayingFeedback -= OnAnimationComplete;
        AC_OnObjectPicked?.Invoke();
    }


    



    [PunRPC]
    void RPC_RequestInteract(int requesterActorNumber)
    {
        // Only Master decides
        if (!PhotonNetwork.IsMasterClient)
            return;

        if (!canInteract || isPicked)
            return;

        canInteract = false;
        isPicked = true;

        // Broadcast final decision
        pv.RPC(
            nameof(RPC_ConfirmObjectInteracted),
            RpcTarget.All,
            requesterActorNumber
        );
    }


    [PunRPC]
    void RPC_ConfirmObjectInteracted(int winnerActorNumber)
    {
        UnHover();
        canInteract = false;
        isPicked = true;

        // Everyone plays animation
        feedBackManager.CompletePlayingFeedback += OnAnimationComplete;
        feedBackManager.PlayFeedback();

        // Only winner subscribes to inventory callback
        if (PhotonNetwork.LocalPlayer.ActorNumber == winnerActorNumber)
        {
            feedBackManager.CompletePlayingFeedback += OnAnimationCompleteOwner;
        }
    }


    public void OnGrab(Transform playerhandRef)
    {
        playerHand = playerhandRef;
        animator = playerHand.GetComponent<Animator>();

        // Step 1: Play the grab animation on player hand
        animator.SetInteger("pose",animationIndex);

        // Step 2: Align PickableObject so Hand ref matches player hand

        // -- Rotation first --
        Quaternion rotOffset = playerHand.rotation * Quaternion.Inverse(handRef.rotation);
        transform.rotation = rotOffset * transform.rotation;

        // -- Position after rotation (because rotation changes world pos of handRef) --
        Vector3 posOffset = playerHand.position - handRef.position;
        transform.position += posOffset;

        // Step 3: Parent to player hand so it moves with you
        transform.SetParent(playerHand);
        handRef.gameObject.SetActive(false);
    }
}
