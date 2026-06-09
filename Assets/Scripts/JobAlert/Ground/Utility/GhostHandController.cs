using UnityEngine;

public class GhostHandController : MonoBehaviour
{
    [Header("Ghost Hand Settings")]
    [SerializeField] private Transform handBone;
    [SerializeField] private float alignSpeed = 15f;
    [SerializeField] private float rotationSpeed = 20f;

    private Transform currentGripAnchor;
    private bool isAligning;

    private void LateUpdate()
    {
        if (!isAligning || currentGripAnchor == null)
            return;

        AlignToGripAnchor();
    }

    public void SetGripAnchor(Transform anchor)
    {
        currentGripAnchor = anchor;
        isAligning = true;
    }

    public void ClearGripAnchor()
    {
        currentGripAnchor = null;
        isAligning = false;
    }

    private void AlignToGripAnchor()
    {
        Vector3 targetPosition = currentGripAnchor.position;
        Quaternion targetRotation = currentGripAnchor.rotation;

        handBone.position = Vector3.Lerp(
            handBone.position,
            targetPosition,
            Time.deltaTime * alignSpeed
        );

        handBone.rotation = Quaternion.Slerp(
            handBone.rotation,
            targetRotation,
            Time.deltaTime * rotationSpeed
        );
    }

    public bool IsAligned(float tolerance = 0.02f)
    {
        if (currentGripAnchor == null) return false;

        float distance = Vector3.Distance(handBone.position, currentGripAnchor.position);
        return distance <= tolerance;
    }
}