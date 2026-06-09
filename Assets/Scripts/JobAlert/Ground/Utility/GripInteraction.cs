using UnityEngine;
using Photon.Pun;

public class GripInteraction : MonoBehaviourPunCallbacks
{
    [Header("Grip Settings")]
    [SerializeField] private Transform gripPoint;
    [SerializeField] private float gripRange = 2f;
    [SerializeField] private LayerMask grippableLayer;
    [SerializeField] private float gripMoveSpeed = 12f;
    [SerializeField] private float gripRotateSpeed = 15f;

    [Header("Physics Settings")]
    [SerializeField] private bool disableGravityOnGrip = true;
    [SerializeField] private bool freezeRotationOnGrip = false;

    private Rigidbody currentGrippedRb;
    private PhotonView currentPhotonView;
    private bool isGripping;

    protected virtual void Update()
    {
        if (!photonView.IsMine) return;

        if (Input.GetKeyDown(KeyCode.E))
            TryGrip();

        if (Input.GetKeyUp(KeyCode.E))
            ReleaseGrip();

        if (isGripping && currentGrippedRb != null)
            UpdateGripMovement();
    }

    private void TryGrip()
    {
        if (isGripping) return;

        Ray ray = new Ray(Camera.main.transform.position, Camera.main.transform.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, gripRange, grippableLayer))
        {
            Rigidbody rb = hit.collider.attachedRigidbody;
            if (rb == null) return;

            PhotonView targetView = rb.GetComponent<PhotonView>();
            if (targetView != null && !targetView.IsMine)
                targetView.RequestOwnership();

            currentGrippedRb = rb;
            currentPhotonView = targetView;

            BeginGrip();
        }
    }

    private void BeginGrip()
    {
        isGripping = true;

        if (disableGravityOnGrip)
            currentGrippedRb.useGravity = false;

        if (freezeRotationOnGrip)
            currentGrippedRb.freezeRotation = true;

        currentGrippedRb.linearDamping = 10f;
        currentGrippedRb.angularDamping = 10f;
    }

    private void UpdateGripMovement()
    {
        Vector3 targetPosition = gripPoint.position;
        Quaternion targetRotation = gripPoint.rotation;

        Vector3 moveDirection = targetPosition - currentGrippedRb.position;
        currentGrippedRb.linearVelocity = moveDirection * gripMoveSpeed;

        Quaternion deltaRotation = targetRotation * Quaternion.Inverse(currentGrippedRb.rotation);
        deltaRotation.ToAngleAxis(out float angle, out Vector3 axis);

        if (angle > 180f) angle -= 360f;

        Vector3 angularVelocity = axis * angle * Mathf.Deg2Rad * gripRotateSpeed;
        currentGrippedRb.angularVelocity = angularVelocity;
    }

    private void ReleaseGrip()
    {
        if (!isGripping) return;

        if (currentGrippedRb != null)
        {
            currentGrippedRb.useGravity = true;
            currentGrippedRb.freezeRotation = false;
            currentGrippedRb.linearDamping = 1f;
            currentGrippedRb.angularDamping = 0.05f;
        }

        currentGrippedRb = null;
        currentPhotonView = null;
        isGripping = false;
    }

    public bool IsGripping()
    {
        return isGripping;
    }

    public Rigidbody GetCurrentObject()
    {
        return currentGrippedRb;
    }
}