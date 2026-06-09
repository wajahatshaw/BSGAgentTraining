using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Display 3 (targetDisplay 2): full-body third-person follow on the designated zone 0 physical Photon player.
/// </summary>
[DefaultExecutionOrder(300)]
public class MultiplayerDesignatedPlayerFollowCamera : MonoBehaviour
{
    public const string CameraObjectName = "GameView_Display3_DesignatedPlayer_Drone_Camera";

    [SerializeField] int targetDisplay = 2;
    [SerializeField] float distanceBehind = 4.6f;
    [SerializeField] float distanceBodyFactor = 2.15f;
    [SerializeField] float heightAboveFeet = 1.1f;
    [SerializeField] float droneExtraHeight = 0.15f;
    [SerializeField] float bodyCenterFraction = 0.5f;
    [SerializeField] float lookAheadDistance = 0.25f;
    [SerializeField] float fieldOfView = 56f;
    [SerializeField] float shoulderOffset = 0.32f;
    [SerializeField] float positionSmoothTime = 0.08f;
    [SerializeField] float rotationSlerp = 14f;

    Camera _cam;
    Vector3 _posVel;
    Transform _lastTarget;
    Text _cornerLabel;

    public static MultiplayerDesignatedPlayerFollowCamera Active { get; private set; }

    public static MultiplayerDesignatedPlayerFollowCamera EnsureInScene()
    {
        MultiplayerRagZone0Anchor.DestroyStrayMultiplayerFollowCamerasPublic();

        MultiplayerDesignatedPlayerFollowCamera existing = FindObjectOfType<MultiplayerDesignatedPlayerFollowCamera>();
        if (existing != null)
        {
            existing.ApplySettingsFromAnchor();
            existing.EnsureCameraComponent();
            existing.EnforceExclusiveDisplay();
            return existing;
        }

        GameObject existingGo = GameObject.Find(CameraObjectName);
        if (existingGo != null)
        {
            existing = existingGo.GetComponent<MultiplayerDesignatedPlayerFollowCamera>()
                       ?? existingGo.AddComponent<MultiplayerDesignatedPlayerFollowCamera>();
            existing.ApplySettingsFromAnchor();
            existing.EnsureCameraComponent();
            existing.EnforceExclusiveDisplay();
            return existing;
        }

        var go = new GameObject(CameraObjectName);
        var cam = go.AddComponent<MultiplayerDesignatedPlayerFollowCamera>();
        cam.ApplySettingsFromAnchor();
        cam.EnsureCameraComponent();
        cam.EnforceExclusiveDisplay();
        return cam;
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
        ApplySettingsFromAnchor();
        EnsureCameraComponent();
        BuildCornerLabel("Zone 0 Physical");
    }

    void LateUpdate()
    {
        EnforceExclusiveDisplay();

        Transform target = ResolveDesignatedPlayerTransform();
        if (target == null)
            return;

        DesignatedPhysicalPlayerAppearance.ApplyScale(target);

        if (target != _lastTarget)
        {
            _lastTarget = target;
            _posVel = Vector3.zero;
        }

        float bodyHeight = DesignatedPhysicalPlayerAppearance.ResolveBodyHeight(target);
        float bodyScale = bodyHeight / DesignatedPhysicalPlayerAppearance.DefaultBodyHeight;
        float bodyCenterY = bodyHeight * bodyCenterFraction;
        float d = Mathf.Max(distanceBehind, bodyHeight * distanceBodyFactor);
        float camY = (heightAboveFeet + droneExtraHeight) * bodyScale;

        Vector3 flatFwd = new Vector3(target.forward.x, 0f, target.forward.z);
        if (flatFwd.sqrMagnitude < 1e-4f)
            flatFwd = Vector3.forward;
        flatFwd.Normalize();

        Vector3 flatRight = Vector3.Cross(Vector3.up, flatFwd).normalized;

        Vector3 feet = target.position;
        Vector3 bodyCenter = feet + Vector3.up * bodyCenterY;
        Vector3 desired = feet - flatFwd * d + flatRight * shoulderOffset + Vector3.up * camY;
        transform.position = Vector3.SmoothDamp(transform.position, desired, ref _posVel, positionSmoothTime);

        Vector3 look = bodyCenter + flatFwd * lookAheadDistance;
        Quaternion want = Quaternion.LookRotation(look - transform.position, Vector3.up);
        float k = 1f - Mathf.Exp(-rotationSlerp * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, want, k);
    }

    public void EnforceExclusiveDisplay()
    {
        EnsureCameraComponent();
        if (_cam == null)
            return;

        _cam.enabled = true;
        _cam.targetDisplay = targetDisplay;
        _cam.depth = 100f;

        foreach (Camera other in FindObjectsOfType<Camera>(true))
        {
            if (other == null || other == _cam)
                continue;
            if (other.targetDisplay != targetDisplay)
                continue;

            other.enabled = false;
        }
    }

    static Transform ResolveDesignatedPlayerTransform()
    {
        Transform designated = DesignatedPhysicalPlayerAppearance.TryFindDesignatedPlayerTransform();
        if (designated != null)
            return designated;

        if (PlayerRagPhysicalBridge.IsBound && PlayerRagPhysicalBridge.BoundMover != null)
        {
            Transform bound = PlayerRagPhysicalBridge.BoundMover.transform;
            if (IsDesignatedPhysicalTransform(bound))
                return bound;
        }

        RagPhysicalAgentAssignment.EnsureAssignedInRoom();
        RagPhysicalAgentAssignment.RefreshFromRoom();
        int actor = RagPhysicalAgentAssignment.DesignatedActorNumber;

        if (RagPhysicalAgentAssignment.IsLocalPlayerRagPhysicalAgent())
        {
            foreach (PlayerMovement pm in FindObjectsOfType<PlayerMovement>())
            {
                if (pm == null)
                    continue;
                PhotonView pv = pm.GetComponent<PhotonView>();
                if (pv != null && pv.IsMine)
                    return pm.transform;
            }
        }

        foreach (RagSequenceAgentMover mover in FindObjectsOfType<RagSequenceAgentMover>())
        {
            if (mover == null || mover.isMentalAgent || !mover.hostPlayerMovement)
                continue;

            Transform t = mover.transform;
            if (!IsDesignatedPhysicalTransform(t))
                continue;

            PhotonView pv = t.GetComponent<PhotonView>();
            if (actor >= 0 && pv != null && pv.Owner != null && pv.Owner.ActorNumber != actor)
                continue;

            return t;
        }

        if (actor >= 0)
        {
            foreach (PhotonView pv in FindObjectsOfType<PhotonView>())
            {
                if (pv == null || pv.Owner == null || pv.Owner.ActorNumber != actor)
                    continue;

                PlayerMovement pm = pv.GetComponent<PlayerMovement>() ?? pv.GetComponentInChildren<PlayerMovement>();
                if (pm != null && IsDesignatedPhysicalTransform(pm.transform))
                    return pm.transform;
            }
        }

        return null;
    }

    static bool IsDesignatedPhysicalTransform(Transform t)
    {
        if (t == null)
            return false;

        if (t.name.StartsWith("Agent_M", System.StringComparison.OrdinalIgnoreCase))
            return false;

        RagSequenceAgentMover mover = t.GetComponent<RagSequenceAgentMover>();
        if (mover != null && mover.isMentalAgent)
            return false;

        return t.GetComponent<PlayerMovement>() != null
               || (mover != null && mover.hostPlayerMovement && !mover.isMentalAgent);
    }

    public void ApplySettingsFromAnchor()
    {
        MultiplayerRagZone0Anchor anchor = MultiplayerRagZone0Anchor.Instance;
        if (anchor == null)
            return;

        distanceBehind = anchor.designatedFollowDistanceBehind;
        distanceBodyFactor = anchor.designatedFollowDistanceBodyFactor;
        heightAboveFeet = anchor.designatedFollowHeight;
        droneExtraHeight = anchor.designatedFollowDroneLift;
        bodyCenterFraction = anchor.designatedFollowBodyCenterFraction;
        lookAheadDistance = anchor.designatedFollowLookAhead;
        fieldOfView = anchor.designatedFollowFov;
        shoulderOffset = anchor.designatedFollowShoulderOffset;
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

    void EnsureCameraComponent()
    {
        _cam = GetComponent<Camera>();
        if (_cam == null)
            _cam = gameObject.AddComponent<Camera>();

        _cam.targetDisplay = targetDisplay;
        _cam.fieldOfView = fieldOfView;
        _cam.farClipPlane = 350f;
        _cam.nearClipPlane = 0.08f;
        _cam.depth = 100f;
        _cam.backgroundColor = new Color(0.06f, 0.06f, 0.12f, 1f);

        if (TryGetComponent<AudioListener>(out AudioListener listener))
            Destroy(listener);

        UniversalAdditionalCameraData urp = _cam.GetUniversalAdditionalCameraData();
        if (urp == null)
            return;

        urp.renderType = CameraRenderType.Base;
        while (urp.cameraStack.Count > 0)
            urp.cameraStack.RemoveAt(urp.cameraStack.Count - 1);

        if (_cornerLabel != null)
            _cornerLabel.text = "Zone 0 Physical";
    }
}
