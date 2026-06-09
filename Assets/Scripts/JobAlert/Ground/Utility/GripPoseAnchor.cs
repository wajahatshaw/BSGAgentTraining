using UnityEngine;

public class GripPoseAnchor : MonoBehaviour
{
    [Header("Grip Pose Configuration")]
    [SerializeField] private Transform gripPoint;
    [SerializeField] private Vector3 localPositionOffset;
    [SerializeField] private Vector3 localRotationOffset;

    public Transform GetGripPoint()
    {
        return gripPoint;
    }

    public Vector3 GetWorldPosition()
    {
        return gripPoint.position + gripPoint.TransformDirection(localPositionOffset);
    }

    public Quaternion GetWorldRotation()
    {
        Quaternion offsetRotation = Quaternion.Euler(localRotationOffset);
        return gripPoint.rotation * offsetRotation;
    }

    private void OnDrawGizmosSelected()
    {
        if (gripPoint == null) return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(GetWorldPosition(), 0.02f);
        Gizmos.DrawLine(gripPoint.position, GetWorldPosition());
    }
}