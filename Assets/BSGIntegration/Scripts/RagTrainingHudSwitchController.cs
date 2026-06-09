using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// HUD pill switch (Settings track + knob) left of Chat in ProtoypeSceneMultiplayer only.
/// Uses <see cref="Toggle"/> + onValueChanged like SwitchController. OFF by default.
/// </summary>
[DefaultExecutionOrder(250)]
public class RagTrainingHudSwitchController : MonoBehaviour
{
    public const string SwitchObjectName = "RagTrainingHudSwitch";

    /// <summary>Only this scene gets the switch (not MultiplayerSetup lobby).</summary>
    public const string AllowedSceneName = "ProtoypeSceneMultiplayer";

    const string TrackChildName = "Background";
    const string KnobChildName = "Handle";

    /// <summary>Matches settings ToggleButton (94×57) handle travel.</summary>
    const float ReferenceSwitchWidth = 94f;
    const float ReferenceHandleXOn = 18f;
    const float ReferenceHandleXOff = -18f;

    [Header("Switch (SwitchController pattern)")]
    public Toggle switchToggle;

    [Header("Placement — left of ChatboxButton")]
    [SerializeField] Vector2 anchoredPosition = new Vector2(-304f, -84f);
    [SerializeField] Vector2 switchSize = new Vector2(60f, 44f);
    [SerializeField] float gapFromChatIcon = 16f;

    [Header("Switch visuals (grey HUD style)")]
    [SerializeField] Color trackColor = new Color(1f, 1f, 1f, 0.72f);
    [SerializeField] Color trackColorOn = new Color(1f, 1f, 1f, 1f);
    [SerializeField] Color knobColor = new Color(1f, 1f, 1f, 0.92f);

    Image _trackImage;
    Image _knobImage;
    RectTransform _knobRect;
    Sprite _trackOnSprite;
    Sprite _trackOffSprite;
    Sprite _knobSprite;
    float _handleXOn;
    float _handleXOff;
    bool _suppressCallback;

    public static bool IsAllowedScene(string sceneName)
    {
        return string.Equals(sceneName, AllowedSceneName, System.StringComparison.Ordinal);
    }

    public static RagTrainingHudSwitchController EnsureInScene()
    {
        if (!IsAllowedScene(SceneManager.GetActiveScene().name))
        {
            CleanupInScene();
            return null;
        }

        RagTrainingHudVisibility.Initialize(defaultVisible: false);

        RagTrainingHudSwitchController existing = FindObjectOfType<RagTrainingHudSwitchController>();
        if (existing != null)
        {
            existing.RebuildHudSwitchVisuals();
            return existing;
        }

        DestroyStrayHudSwitchObjects();

        GameObject go = new GameObject(SwitchObjectName);
        var controller = go.AddComponent<RagTrainingHudSwitchController>();
        controller.BuildSwitchUi(RagTrainingHudSwitchBootstrap.ResolveHudParent());
        return controller;
    }

    public static void CleanupInScene()
    {
        RagTrainingHudSwitchController ctrl = FindObjectOfType<RagTrainingHudSwitchController>();
        if (ctrl != null)
            Destroy(ctrl.gameObject);

        DestroyStrayHudSwitchObjects();

        GameObject strayCanvas = GameObject.Find("RagHudSwitchCanvas");
        if (strayCanvas != null && strayCanvas.transform.childCount == 0)
            Destroy(strayCanvas);
    }

    void Awake()
    {
        if (!IsAllowedScene(SceneManager.GetActiveScene().name))
        {
            Destroy(gameObject);
            return;
        }

        RagTrainingHudVisibility.Initialize(defaultVisible: false);
        RebuildHudSwitchVisuals();
        RagTrainingHudVisibility.Changed += OnVisibilityChanged;
    }

    void Start()
    {
        BindSwitch();
    }

    void OnDestroy()
    {
        RagTrainingHudVisibility.Changed -= OnVisibilityChanged;
    }

    void BindSwitch()
    {
        if (switchToggle == null)
            switchToggle = GetComponent<Toggle>();

        if (switchToggle == null)
            return;

        switchToggle.onValueChanged.RemoveListener(OnSwitchChanged);
        switchToggle.onValueChanged.AddListener(OnSwitchChanged);
    }

    void OnSwitchChanged(bool isOn)
    {
        if (_suppressCallback) return;
        RagTrainingHudVisibility.SetVisible(isOn);
        ApplyVisualState(isOn);
    }

    void OnVisibilityChanged(bool visible)
    {
        SyncToggleFromVisibility();
        ApplyVisualState(visible);
    }

    public void SyncToggleFromVisibility()
    {
        if (switchToggle == null) return;

        bool visible = RagTrainingHudVisibility.IsVisible;
        if (switchToggle.isOn == visible) return;

        _suppressCallback = true;
        switchToggle.isOn = visible;
        _suppressCallback = false;
        ApplyVisualState(visible);
    }

    void ApplyVisualState(bool isOn)
    {
        if (_trackImage != null)
        {
            _trackImage.sprite = isOn ? _trackOnSprite : _trackOffSprite;
            _trackImage.color = isOn ? trackColorOn : trackColor;
        }

        if (_knobRect != null)
        {
            float x = isOn ? _handleXOn : _handleXOff;
            _knobRect.anchoredPosition = new Vector2(x, _knobRect.anchoredPosition.y);
        }

        if (_knobImage != null)
            _knobImage.color = knobColor;
    }

    public void BuildSwitchUi(Transform parent)
    {
        if (parent == null)
            parent = RagTrainingHudSwitchBootstrap.ResolveHudParent();

        transform.SetParent(parent, false);
        gameObject.name = SwitchObjectName;

        RectTransform rootRect = GetComponent<RectTransform>();
        if (rootRect == null)
            rootRect = gameObject.AddComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(1f, 1f);
        rootRect.anchorMax = new Vector2(1f, 1f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);

        RebuildHudSwitchVisuals();
        if (switchToggle != null)
            switchToggle.isOn = false;
    }

    public void RebuildHudSwitchVisuals()
    {
        gameObject.name = SwitchObjectName;
        StripAllTextComponents();
        RemoveLabelChildren();
        CacheSwitchSprites();
        UpdateHandleTravelForSize();

        Transform parent = transform.parent;
        if (parent == null || parent.name != "HudButtonHolder")
            transform.SetParent(ResolveHudButtonHolder(), false);

        ApplyHudRowLayoutFromScene();
        BuildPillSwitchHierarchy();
        ConfigureToggle();
        BindSwitch();
        SyncToggleFromVisibility();
    }

    void CacheSwitchSprites()
    {
        _trackOnSprite = LoadSwitchSprite("RagHudToggleTrackOn", "Assets/Sprites/Settings/Toggle.png");
        _trackOffSprite = LoadSwitchSprite("RagHudToggleTrackOff", "Assets/Sprites/Settings/Toggle (1).png");
        _knobSprite = LoadSwitchSprite("RagHudToggleKnob", "Assets/Sprites/Settings/Knob.png");

        if (_trackOnSprite == null)
            _trackOnSprite = _trackOffSprite;
        if (_trackOffSprite == null)
            _trackOffSprite = _trackOnSprite;
    }

    void UpdateHandleTravelForSize()
    {
        float scale = switchSize.x / ReferenceSwitchWidth;
        _handleXOn = ReferenceHandleXOn * scale;
        _handleXOff = ReferenceHandleXOff * scale;
    }

    void BuildPillSwitchHierarchy()
    {
        Image rootImage = GetComponent<Image>();
        if (rootImage != null)
            Destroy(rootImage);

        ClearSwitchChildren();

        GameObject trackGo = new GameObject(TrackChildName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        trackGo.transform.SetParent(transform, false);
        RectTransform trackRect = trackGo.GetComponent<RectTransform>();
        trackRect.anchorMin = Vector2.zero;
        trackRect.anchorMax = Vector2.one;
        trackRect.offsetMin = Vector2.zero;
        trackRect.offsetMax = Vector2.zero;
        _trackImage = trackGo.GetComponent<Image>();
        _trackImage.sprite = _trackOffSprite;
        _trackImage.type = Image.Type.Simple;
        _trackImage.preserveAspect = false;
        _trackImage.raycastTarget = true;
        _trackImage.color = trackColor;

        GameObject knobGo = new GameObject(KnobChildName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        knobGo.transform.SetParent(transform, false);
        _knobRect = knobGo.GetComponent<RectTransform>();
        _knobRect.anchorMin = new Vector2(0.5f, 0.5f);
        _knobRect.anchorMax = new Vector2(0.5f, 0.5f);
        _knobRect.pivot = new Vector2(0.5f, 0.5f);
        float knobSize = Mathf.Min(switchSize.y * 0.86f, switchSize.x * 0.52f);
        _knobRect.sizeDelta = new Vector2(knobSize, knobSize);
        _knobImage = knobGo.GetComponent<Image>();
        _knobImage.sprite = _knobSprite;
        _knobImage.type = Image.Type.Simple;
        _knobImage.preserveAspect = true;
        _knobImage.raycastTarget = false;
        _knobImage.color = knobColor;
    }

    void ClearSwitchChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);

        _trackImage = null;
        _knobImage = null;
        _knobRect = null;
    }

    void ConfigureToggle()
    {
        if (switchToggle == null)
            switchToggle = GetComponent<Toggle>();
        if (switchToggle == null)
            switchToggle = gameObject.AddComponent<Toggle>();

        switchToggle.targetGraphic = _trackImage;
        switchToggle.graphic = null;
        switchToggle.transition = Selectable.Transition.None;
    }

    void StripAllTextComponents()
    {
        DestroyUiTextComponents(gameObject);
    }

    void RemoveLabelChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (HasUiTextComponent(child.gameObject)
                || child.name.IndexOf("label", System.StringComparison.OrdinalIgnoreCase) >= 0
                || child.name.IndexOf("HudLabel", System.StringComparison.OrdinalIgnoreCase) >= 0)
                Destroy(child.gameObject);
        }
    }

    static bool HasUiTextComponent(GameObject go)
    {
        if (go.GetComponent<Text>() != null)
            return true;

        Component[] components = go.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            Component c = components[i];
            if (c == null) continue;
            string typeName = c.GetType().Name;
            if (typeName == "TextMeshProUGUI" || typeName == "TextMeshPro")
                return true;
        }

        return false;
    }

    static void DestroyUiTextComponents(GameObject go)
    {
        Text legacyText = go.GetComponent<Text>();
        if (legacyText != null)
            Object.Destroy(legacyText);

        Component[] components = go.GetComponents<Component>();
        for (int i = components.Length - 1; i >= 0; i--)
        {
            Component c = components[i];
            if (c == null) continue;
            string typeName = c.GetType().Name;
            if (typeName == "TextMeshProUGUI" || typeName == "TextMeshPro")
                Object.Destroy(c);
        }
    }

    static void DestroyStrayHudSwitchObjects()
    {
        DestroyIfExists("RagTrainingHudToggleButton");
        DestroyIfExists("UIControl");
        DestroyIfExists("UICOntrol");
    }

    static void DestroyIfExists(string objectName)
    {
        GameObject go = GameObject.Find(objectName);
        if (go == null)
            return;

        if (go.GetComponent<RagTrainingHudSwitchController>() != null)
            return;

        Object.Destroy(go);
    }

    static Transform ResolveHudButtonHolder()
    {
        GameObject holder = GameObject.Find("HudButtonHolder");
        if (holder != null)
            return holder.transform;

        return RagTrainingHudSwitchBootstrap.ResolveHudParent();
    }

    /// <summary>Placed one slot left of ChatboxButton; pill size matches settings toggle scale.</summary>
    public void ApplyHudRowLayoutFromScene()
    {
        GameObject chat = GameObject.Find("ChatboxButton");

        if (chat != null)
        {
            RectTransform chatRect = chat.GetComponent<RectTransform>();
            if (chatRect != null)
            {
                float chatHalf = chatRect.sizeDelta.x * 0.5f;
                float switchHalf = switchSize.x * 0.5f;
                anchoredPosition = new Vector2(
                    chatRect.anchoredPosition.x - chatHalf - gapFromChatIcon - switchHalf,
                    chatRect.anchoredPosition.y);
            }
        }

        RectTransform self = GetComponent<RectTransform>();
        if (self != null)
        {
            self.sizeDelta = switchSize;
            self.anchoredPosition = anchoredPosition;
        }

        UpdateHandleTravelForSize();
    }

    static Sprite LoadSwitchSprite(string resourcesName, string editorAssetPath)
    {
        Sprite s = Resources.Load<Sprite>(resourcesName);
#if UNITY_EDITOR
        if (s == null && !string.IsNullOrEmpty(editorAssetPath))
            s = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(editorAssetPath);
#endif
        return s;
    }

}

/// <summary>Cleans up HUD switch outside ProtoypeSceneMultiplayer; creation is driven by <see cref="RagMultiplayerSceneBootstrap"/>.</summary>
public static class RagTrainingHudSwitchBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void RegisterSceneCleanup()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!RagTrainingHudSwitchController.IsAllowedScene(scene.name))
            RagTrainingHudSwitchController.CleanupInScene();
    }

    public static Transform ResolveHudParent()
    {
        GameObject holder = GameObject.Find("HudButtonHolder");
        if (holder != null)
            return holder.transform;

        GameObject settings = GameObject.Find("SettingsButton");
        if (settings != null && settings.transform.parent != null)
            return settings.transform.parent;

        Canvas canvas = FindTopHudCanvas();
        return canvas != null ? canvas.transform : null;
    }

    static Canvas FindTopHudCanvas()
    {
        Canvas[] canvases = Object.FindObjectsOfType<Canvas>();
        Canvas best = null;
        int bestOrder = int.MinValue;
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas c = canvases[i];
            if (c == null || !c.isActiveAndEnabled || c.renderMode == RenderMode.WorldSpace)
                continue;
            if (c.sortingOrder >= bestOrder)
            {
                bestOrder = c.sortingOrder;
                best = c;
            }
        }

        return best;
    }
}
