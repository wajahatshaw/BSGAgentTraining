using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Display 2 (targetDisplay 1): closer top-down view framed on zone 0 only (cognitive + physical RAG area).
/// </summary>
[DefaultExecutionOrder(250)]
public class MultiplayerZone0OverviewCamera : MonoBehaviour
{
    public const string CameraObjectName = "GameView_Display2_Zone0_Overview_Camera";

    [SerializeField] int targetDisplay = 1;
    [SerializeField] float heightAboveLookAt = 24f;
    [SerializeField] float backWallOffset = 7f;
    [SerializeField] float pitchDegrees = 58f;
    [SerializeField] float fieldOfView = 56f;

    Camera _cam;
    Text _cornerLabel;

    public static MultiplayerZone0OverviewCamera Active { get; private set; }

    public static MultiplayerZone0OverviewCamera EnsureInScene()
    {
        MultiplayerZone0OverviewCamera existing = FindObjectOfType<MultiplayerZone0OverviewCamera>();
        if (existing != null)
        {
            existing.EnsureCameraComponent();
            return existing;
        }

        GameObject existingGo = GameObject.Find(CameraObjectName);
        if (existingGo != null)
        {
            existing = existingGo.GetComponent<MultiplayerZone0OverviewCamera>()
                       ?? existingGo.AddComponent<MultiplayerZone0OverviewCamera>();
            existing.EnsureCameraComponent();
            return existing;
        }

        var go = new GameObject(CameraObjectName);
        return go.AddComponent<MultiplayerZone0OverviewCamera>();
    }

    void OnEnable() => Active = this;

    void OnDisable()
    {
        if (Active == this)
            Active = null;
    }

    void Awake()
    {
        MultiDisplayGameViewBootstrap.Touch();
        EnsureCameraComponent();
        BuildCornerLabel("Zone 0");
    }

    void LateUpdate()
    {
        EnforceExclusiveDisplay();
    }

    public void EnforceExclusiveDisplay()
    {
        EnsureCameraComponent();
        if (_cam == null)
            return;

        _cam.enabled = true;
        _cam.targetDisplay = targetDisplay;
        _cam.depth = 50f;

        foreach (Camera other in FindObjectsOfType<Camera>(true))
        {
            if (other == null || other == _cam)
                continue;
            if (other.targetDisplay != targetDisplay)
                continue;

            other.enabled = false;
        }
    }

    void BuildCornerLabel(string text)
    {
        if (_cornerLabel != null)
        {
            _cornerLabel.text = text;
            return;
        }

        GameObject root = new GameObject("DisplayNameOverlay");
        root.transform.SetParent(transform, false);

        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = _cam;
        canvas.planeDistance = 0.25f;
        canvas.sortingOrder = 500;

        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        root.AddComponent<GraphicRaycaster>();

        GameObject textGo = new GameObject("Label");
        textGo.transform.SetParent(root.transform, false);
        _cornerLabel = textGo.AddComponent<Text>();
        _cornerLabel.text = text;
        _cornerLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _cornerLabel.fontSize = 34;
        _cornerLabel.fontStyle = FontStyle.Bold;
        _cornerLabel.color = new Color(1f, 1f, 1f, 0.92f);
        _cornerLabel.alignment = TextAnchor.UpperLeft;

        RectTransform rt = _cornerLabel.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(18f, -14f);
        rt.sizeDelta = new Vector2(280f, 56f);
    }

    void Start()
    {
        StartCoroutine(FrameWhenReady());
    }

    IEnumerator FrameWhenReady()
    {
        for (int i = 0; i < 120; i++)
        {
            if (TryFrameOnZone0())
                yield break;
            yield return new WaitForSeconds(0.25f);
        }

        TryFrameOnZone0();
    }

    public bool TryFrameOnZone0()
    {
        MultiplayerRagZone0Anchor anchor = MultiplayerRagZone0Anchor.Instance;
        if (anchor == null || !anchor.TryGetOverviewFrame(out Vector3 lookAt, out float height, out float backOffset, out float pitch, out float fov))
            return false;

        heightAboveLookAt = height;
        backWallOffset = backOffset;
        pitchDegrees = pitch;
        fieldOfView = fov;

        transform.position = lookAt + new Vector3(0f, heightAboveLookAt, -backWallOffset);
        transform.rotation = Quaternion.Euler(pitchDegrees, 0f, 0f);
        EnsureCameraComponent();
        return true;
    }

    void EnsureCameraComponent()
    {
        _cam = GetComponent<Camera>();
        if (_cam == null)
            _cam = gameObject.AddComponent<Camera>();

        _cam.targetDisplay = targetDisplay;
        _cam.fieldOfView = fieldOfView;
        _cam.farClipPlane = 350f;
        _cam.nearClipPlane = 0.1f;
        _cam.depth = 50f;
        _cam.backgroundColor = new Color(0.06f, 0.06f, 0.12f, 1f);

        if (TryGetComponent<AudioListener>(out AudioListener listener))
            Destroy(listener);

        UniversalAdditionalCameraData urp = _cam.GetUniversalAdditionalCameraData();
        if (urp == null)
            return;

        urp.renderType = CameraRenderType.Base;
        while (urp.cameraStack.Count > 0)
            urp.cameraStack.RemoveAt(urp.cameraStack.Count - 1);
    }
}
