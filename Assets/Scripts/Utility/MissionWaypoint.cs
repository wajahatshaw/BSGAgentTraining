using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MissionWaypoint : MonoBehaviour
{
    [Header("UI")]
    public Image img;
    public TMP_Text meter;

    [Header("Target")]
    public Transform target;
    public Vector3 offset;

    [Header("References")]
    public Canvas canvas;
    public Camera mainCamera;

    private RectTransform canvasRect;
    private RectTransform iconRect;

    private void Awake()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        canvasRect = canvas.GetComponent<RectTransform>();
        iconRect = img.GetComponent<RectTransform>();

        //effect init
        initialScale = targetRect.localScale;
    }

    void OnEnable()
    {
        StartEffect();
    }

    void OnDisable()
    {
        StopEffect();
    }

    private void Update()
    {
        if (target == null)
            return;
        if(mainCamera == null)
        {
            mainCamera = Camera.main;
            if(mainCamera == null)
            {
                Debug.Log("Main Camera cast faild!");
            }
        }

        Vector3 worldPos = target.position + offset;
        Vector3 screenPos = mainCamera.WorldToScreenPoint(worldPos);

        // Distance text (use CAMERA position, not UI)
        float distance = Vector3.Distance(mainCamera.transform.position, target.position);
        meter.text = Mathf.RoundToInt(distance) + "m";

        // If target is behind the camera
        if (screenPos.z < 0)
        {
            screenPos.x = Screen.width - screenPos.x;
            screenPos.y = Screen.height - screenPos.y;
            screenPos.z = 0;
        }

        // Clamp to screen bounds
        float padding = 50f;
        screenPos.x = Mathf.Clamp(screenPos.x, padding, Screen.width - padding);
        screenPos.y = Mathf.Clamp(screenPos.y, padding, Screen.height - padding);

        // Convert screen position to UI position
        Vector2 uiPos;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            screenPos,
            canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : mainCamera,
            out uiPos
        );

        iconRect.localPosition = uiPos;
    }

    #region Effect
     [Header("References")]
    [SerializeField] private RectTransform targetRect;
    [SerializeField] private Image targetImage;

    [Header("Animation")]
    [SerializeField] private float scaleMultiplier = 1.5f;
    [SerializeField] private float duration = 1f;

    private Vector3 initialScale;
    private Sequence loopSequence;

   

    // =====================================
    // START LOOP
    // =====================================

    public void StartEffect()
    {
        StopEffect();

        // Reset state
        targetRect.localScale = initialScale;

        Color c = targetImage.color;
        c.a = 1f;
        targetImage.color = c;

        loopSequence = DOTween.Sequence()
            .Append(
                targetRect.DOScale(initialScale * scaleMultiplier, duration)
            )
            .Join(
                targetImage.DOFade(0f, duration)
            )
            .AppendCallback(ResetEffect)
            .SetLoops(-1, LoopType.Restart);
    }

    // =====================================
    // STOP LOOP
    // =====================================

    public void StopEffect()
    {
        if (loopSequence != null)
        {
            loopSequence.Kill();
            loopSequence = null;
        }

        // Reset visuals
        targetRect.localScale = initialScale;

        if (targetImage != null)
        {
            Color c = targetImage.color;
            c.a = 1f;
            targetImage.color = c;
        }
    }

    // =====================================
    // RESET BETWEEN LOOPS
    // =====================================

    private void ResetEffect()
    {
        targetRect.localScale = initialScale;

        Color c = targetImage.color;
        c.a = 1f;
        targetImage.color = c;
    }
    #endregion
}
