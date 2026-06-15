using System.Collections;
using UnityEngine;

/// <summary>
/// Professional press-state UI for physical equipment targets (mouse, keys, scroll).
/// Visual overlays only — root tool scale is unchanged.
/// </summary>
[DisallowMultipleComponent]
public class PhysicalTargetPressVisual : MonoBehaviour
{
    public const string VisualRootName = "PressTargetVisual";

    static readonly Color ReadyAccent = new Color(0.22f, 0.52f, 0.96f, 1f);
    static readonly Color PressingAccent = new Color(0.98f, 0.72f, 0.14f, 1f);
    static readonly Color PressedAccent = new Color(0.14f, 0.82f, 0.42f, 1f);
    static readonly Color ReadyBase = new Color(0.92f, 0.94f, 0.98f, 1f);
    static readonly Color PressedBase = new Color(0.78f, 0.9f, 0.82f, 1f);

    [SerializeField] float pressDepressLocalY = 0.08f;

    Renderer _bodyRenderer;
    Material _bodyMaterial;
    Color _bodyBaseColor = Color.white;

    Transform _visualRoot;
    Transform _topCap;
    Transform _statusBadge;
    TextMesh _statusLabel;
    TextMesh _nameLabel;
    Renderer _ringRenderer;
    Vector3 _topCapRestLocalPos;
    Vector3 _topCapRestLocalScale;

    bool _isPressed;
    bool _isPressing;
    Coroutine _pulseRoutine;

    public bool IsPressed => _isPressed;

    public static bool IsPressableEquipment(GameObject go)
    {
        if (go == null)
            return false;

        DeclarativeObjectMetadata meta = go.GetComponent<DeclarativeObjectMetadata>();
        if (meta == null)
            return false;

        string type = meta.metadataSource ?? string.Empty;
        string objectId = meta.objectId ?? go.name;
        string name = meta.displayName ?? objectId;
        if (name.IndexOf("menu_option", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return false;
        if (go.GetComponentInParent<CognitiveStationInteractable>() != null)
            return false;

        string state = meta.currentState ?? string.Empty;
        bool pressableState = state.IndexOf("press", System.StringComparison.OrdinalIgnoreCase) >= 0
                              || state.IndexOf("unpressed", System.StringComparison.OrdinalIgnoreCase) >= 0
                              || state.IndexOf("stationary", System.StringComparison.OrdinalIgnoreCase) >= 0;

        return pressableState
               || objectId.IndexOf("mouse", System.StringComparison.OrdinalIgnoreCase) >= 0
               || objectId.IndexOf("key", System.StringComparison.OrdinalIgnoreCase) >= 0
               || objectId.IndexOf("scroll", System.StringComparison.OrdinalIgnoreCase) >= 0
               || objectId.IndexOf("button", System.StringComparison.OrdinalIgnoreCase) >= 0
               || name.IndexOf("mouse", System.StringComparison.OrdinalIgnoreCase) >= 0
               || name.IndexOf("key", System.StringComparison.OrdinalIgnoreCase) >= 0
               || name.IndexOf("scroll", System.StringComparison.OrdinalIgnoreCase) >= 0
               || name.IndexOf("button", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static PhysicalTargetPressVisual EnsureOn(GameObject toolRoot, string displayName, string initialState)
    {
        if (toolRoot == null)
            return null;

        PhysicalTargetPressVisual existing = toolRoot.GetComponent<PhysicalTargetPressVisual>();
        if (existing != null)
        {
            existing.RefreshLabels(displayName, initialState);
            return existing;
        }

        PhysicalTargetPressVisual visual = toolRoot.AddComponent<PhysicalTargetPressVisual>();
        visual.Build(displayName, initialState);
        return visual;
    }

    public static PhysicalTargetPressVisual EnsureForStepTarget(string targetObjectId, int zoneIndex)
    {
        PhysicalTargetPressVisual existing = FindForTarget(targetObjectId, zoneIndex);
        if (existing != null)
            return existing;

        GameObject root = PhysicalTargetInteraction.FindStepTargetRoot(targetObjectId, zoneIndex);
        if (root == null)
            return null;

        DeclarativeObjectMetadata meta = root.GetComponent<DeclarativeObjectMetadata>();
        string displayName = meta != null && !string.IsNullOrWhiteSpace(meta.displayName)
            ? meta.displayName
            : root.name;
        string state = meta != null && !string.IsNullOrWhiteSpace(meta.currentState)
            ? meta.currentState
            : "unpressed_0";

        return EnsureOn(root, displayName, state);
    }

    public static PhysicalTargetPressVisual FindForTarget(string targetObjectId, int zoneIndex)
    {
        if (string.IsNullOrWhiteSpace(targetObjectId))
            return null;

        GameObject root = PhysicalTargetInteraction.FindStepTargetRoot(targetObjectId, zoneIndex);
        if (root != null)
        {
            PhysicalTargetPressVisual onRoot = root.GetComponent<PhysicalTargetPressVisual>();
            if (onRoot != null)
                return onRoot;
        }

        return null;
    }

    void Build(string displayName, string initialState)
    {
        if (_bodyRenderer == null)
            _bodyRenderer = GetComponent<Renderer>();
        if (_bodyRenderer != null)
        {
            _bodyMaterial = _bodyRenderer.material;
            _bodyBaseColor = _bodyMaterial != null ? _bodyMaterial.color : Color.white;
        }

        _visualRoot = transform.Find(VisualRootName);
        if (_visualRoot == null)
        {
            GameObject root = new GameObject(VisualRootName);
            root.transform.SetParent(transform, false);
            _visualRoot = root.transform;
        }

        EnsureTopCap();
        EnsureStatusBadge();
        EnsureNameLabel(displayName);
        EnsureAccentRing();

        bool pressed = IsPressedState(initialState);
        ApplyVisualState(pressed ? TargetPressState.Pressed : TargetPressState.Ready, immediate: true);
    }

    public void RefreshLabels(string displayName, string state)
    {
        if (_nameLabel != null && !string.IsNullOrWhiteSpace(displayName))
            _nameLabel.text = FormatDisplayName(displayName);

        bool pressed = IsPressedState(state);
        ApplyVisualState(pressed ? TargetPressState.Pressed : TargetPressState.Ready, immediate: true);
    }

    public void BeginPressPulse()
    {
        _isPressing = true;
        ApplyVisualState(TargetPressState.Pressing, immediate: true);
        if (_pulseRoutine != null)
            StopCoroutine(_pulseRoutine);
        _pulseRoutine = StartCoroutine(CoPressPulse());
    }

    public void CommitPressedState(string newState = "pressed_1")
    {
        _isPressing = false;
        _isPressed = true;
        if (_pulseRoutine != null)
        {
            StopCoroutine(_pulseRoutine);
            _pulseRoutine = null;
        }

        DeclarativeObjectMetadata meta = GetComponent<DeclarativeObjectMetadata>();
        if (meta != null)
            meta.currentState = newState;

        ApplyVisualState(TargetPressState.Pressed, immediate: true);
    }

    public void ResetToReady(string newState = "unpressed_0")
    {
        _isPressing = false;
        _isPressed = false;
        if (_pulseRoutine != null)
        {
            StopCoroutine(_pulseRoutine);
            _pulseRoutine = null;
        }

        DeclarativeObjectMetadata meta = GetComponent<DeclarativeObjectMetadata>();
        if (meta != null)
            meta.currentState = newState;

        ApplyVisualState(TargetPressState.Ready, immediate: false);
    }

    IEnumerator CoPressPulse()
    {
        float t = 0f;
        while (_isPressing && t < 2.5f)
        {
            t += Time.deltaTime;
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 8f);
            if (_ringRenderer != null && _ringRenderer.material != null)
                _ringRenderer.material.color = Color.Lerp(PressingAccent, PressedAccent, pulse * 0.35f);
            yield return null;
        }
    }

    enum TargetPressState
    {
        Ready,
        Pressing,
        Pressed
    }

    void ApplyVisualState(TargetPressState state, bool immediate)
    {
        Color accent = ReadyAccent;
        Color body = _bodyBaseColor;
        string badge = "READY";
        float capOffset = 0f;

        switch (state)
        {
            case TargetPressState.Pressing:
                accent = PressingAccent;
                body = Color.Lerp(_bodyBaseColor, PressingAccent, 0.18f);
                badge = "PRESS";
                capOffset = -pressDepressLocalY * 0.55f;
                break;
            case TargetPressState.Pressed:
                accent = PressedAccent;
                body = Color.Lerp(_bodyBaseColor, PressedBase, 0.28f);
                badge = "ACTIVE";
                capOffset = -pressDepressLocalY;
                break;
            default:
                accent = ReadyAccent;
                body = Color.Lerp(_bodyBaseColor, ReadyBase, 0.08f);
                badge = "READY";
                capOffset = 0f;
                break;
        }

        if (_bodyMaterial != null)
            _bodyMaterial.color = body;

        if (_statusLabel != null)
            _statusLabel.text = badge;

        if (_ringRenderer != null && _ringRenderer.material != null)
            _ringRenderer.material.color = accent;

        if (_topCap != null)
        {
            Vector3 pos = _topCapRestLocalPos;
            pos.y += capOffset;
            _topCap.localPosition = immediate ? pos : Vector3.Lerp(_topCap.localPosition, pos, 0.35f);
        }
    }

    void EnsureTopCap()
    {
        _topCap = _visualRoot.Find("PressTopCap");
        if (_topCap != null)
        {
            _topCapRestLocalPos = _topCap.localPosition;
            _topCapRestLocalScale = _topCap.localScale;
            return;
        }

        GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cap.name = "PressTopCap";
        cap.transform.SetParent(_visualRoot, false);
        cap.transform.localPosition = new Vector3(0f, 0.42f, 0f);
        cap.transform.localScale = new Vector3(0.82f, 0.12f, 0.82f);

        Collider col = cap.GetComponent<Collider>();
        if (col != null)
            Destroy(col);

        Renderer r = cap.GetComponent<Renderer>();
        if (r != null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (shader != null)
            {
                r.material = new Material(shader);
                r.material.color = new Color(0.96f, 0.97f, 1f, 1f);
            }
        }

        _topCap = cap.transform;
        _topCapRestLocalPos = _topCap.localPosition;
        _topCapRestLocalScale = _topCap.localScale;
    }

    void EnsureStatusBadge()
    {
        _statusBadge = _visualRoot.Find("PressStatusBadge");
        if (_statusBadge == null)
        {
            GameObject badge = new GameObject("PressStatusBadge");
            badge.transform.SetParent(_visualRoot, false);
            badge.transform.localPosition = new Vector3(0f, 0.78f, 0f);
            badge.transform.localRotation = Quaternion.Euler(58f, 0f, 0f);
            _statusBadge = badge.transform;

            GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "BadgePlate";
            plate.transform.SetParent(_statusBadge, false);
            plate.transform.localPosition = Vector3.zero;
            plate.transform.localScale = new Vector3(0.72f, 0.14f, 0.22f);
            Collider plateCol = plate.GetComponent<Collider>();
            if (plateCol != null)
                Destroy(plateCol);
            Renderer plateR = plate.GetComponent<Renderer>();
            if (plateR != null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                if (shader != null)
                {
                    plateR.material = new Material(shader);
                    plateR.material.color = new Color(0.08f, 0.1f, 0.16f, 1f);
                }
            }
        }

        Transform labelTf = _statusBadge.Find("StatusLabel");
        if (labelTf == null)
        {
            GameObject labelGo = new GameObject("StatusLabel");
            labelGo.transform.SetParent(_statusBadge, false);
            labelGo.transform.localPosition = new Vector3(0f, 0f, -0.12f);
            _statusLabel = labelGo.AddComponent<TextMesh>();
            _statusLabel.fontSize = 22;
            _statusLabel.characterSize = 0.05f;
            _statusLabel.anchor = TextAnchor.MiddleCenter;
            _statusLabel.alignment = TextAlignment.Center;
            _statusLabel.fontStyle = FontStyle.Bold;
            _statusLabel.color = Color.white;
        }
        else
        {
            _statusLabel = labelTf.GetComponent<TextMesh>();
        }
    }

    void EnsureNameLabel(string displayName)
    {
        Transform existing = transform.Find("ToolObjectNameLabel");
        if (existing == null)
            existing = transform.Find("DynamicObjectNameLabel");

        if (existing != null)
        {
            _nameLabel = existing.GetComponent<TextMesh>();
            if (_nameLabel != null && !string.IsNullOrWhiteSpace(displayName))
                _nameLabel.text = FormatDisplayName(displayName);
            return;
        }

        GameObject labelGo = new GameObject("PressTargetNameLabel");
        labelGo.transform.SetParent(_visualRoot, false);
        labelGo.transform.localPosition = new Vector3(0f, 1.02f, 0f);
        labelGo.transform.localRotation = Quaternion.Euler(58f, 0f, 0f);
        _nameLabel = labelGo.AddComponent<TextMesh>();
        _nameLabel.text = FormatDisplayName(displayName);
        _nameLabel.fontSize = 18;
        _nameLabel.characterSize = 0.055f;
        _nameLabel.anchor = TextAnchor.MiddleCenter;
        _nameLabel.alignment = TextAlignment.Center;
        _nameLabel.color = new Color(0.92f, 0.95f, 1f, 1f);
        labelGo.AddComponent<IndicatorBillboard>();
    }

    void EnsureAccentRing()
    {
        Transform ringTf = _visualRoot.Find("PressAccentRing");
        if (ringTf != null)
        {
            _ringRenderer = ringTf.GetComponent<Renderer>();
            return;
        }

        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "PressAccentRing";
        ring.transform.SetParent(_visualRoot, false);
        ring.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        ring.transform.localScale = new Vector3(1.18f, 0.015f, 1.18f);
        Collider ringCol = ring.GetComponent<Collider>();
        if (ringCol != null)
            Destroy(ringCol);

        _ringRenderer = ring.GetComponent<Renderer>();
        if (_ringRenderer != null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (shader != null)
            {
                _ringRenderer.material = new Material(shader);
                _ringRenderer.material.color = ReadyAccent;
            }
        }
    }

    static bool IsPressedState(string state)
    {
        if (string.IsNullOrWhiteSpace(state))
            return false;
        return state.IndexOf("pressed", System.StringComparison.OrdinalIgnoreCase) >= 0
               && state.IndexOf("unpressed", System.StringComparison.OrdinalIgnoreCase) < 0;
    }

    static string FormatDisplayName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "TARGET";
        return raw.Replace('_', ' ').ToUpperInvariant();
    }
}
