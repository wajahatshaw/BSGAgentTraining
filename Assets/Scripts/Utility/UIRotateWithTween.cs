using UnityEngine;
using DG.Tweening;

public class UIRotateWithTween : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private RectTransform target;

    [Header("Rotation")]
    [SerializeField] private float rotateAngle = 180f;
    [SerializeField] private float rotateDuration = 0.3f;
    [SerializeField] private Ease rotateEase = Ease.OutQuad;

    private Vector3 initialRotation;

    void Awake()
    {
        if (target == null)
            target = GetComponent<RectTransform>();

        initialRotation = target.localEulerAngles;
    }

    /// <summary>
    /// Call this on button click to rotate
    /// </summary>
    public void Rotate()
    {
        target.DOKill();

        Vector3 targetRotation =
            initialRotation + new Vector3(0f, 0f, rotateAngle);

        target
            .DOLocalRotate(targetRotation, rotateDuration)
            .SetEase(rotateEase);
    }

    /// <summary>
    /// Call this to revert rotation
    /// </summary>
    public void RevertRotation()
    {
        target.DOKill();

        target
            .DOLocalRotate(initialRotation, rotateDuration)
            .SetEase(rotateEase);
    }
}
