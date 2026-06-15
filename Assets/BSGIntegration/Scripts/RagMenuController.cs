using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime visibility controller for RAG-authored menu option targets.
/// Builds a floating spatial board with physical option targets for hand/finger selection.
/// GameObjects stay active so ML target lookup keeps working; only renderers are toggled.
/// </summary>
public class RagMenuController : MonoBehaviour
{
    static RagMenuController _instance;

    readonly List<GameObject> menuObjects     = new List<GameObject>();
    readonly Dictionary<int, GameObject> menuRootsByZone = new Dictionary<int, GameObject>();
    readonly HashSet<string> visibleOptionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    ActionSequenceStep _visibleMenuStep;
    int _visibleMenuZoneIndex = -1;

    public string CurrentlySelectedOptionId { get; private set; }
    public int CurrentlySelectedZoneIndex { get; private set; } = -1;

    // Option tile colours
    static readonly Color BackingColor  = new Color(0.07f, 0.09f, 0.16f, 1f);  // dark screen
    static readonly Color AccentColor   = new Color(0.20f, 0.42f, 0.88f, 1f);  // blue title bar
    static readonly Color OptionColor   = new Color(0.18f, 0.38f, 0.95f, 1f);  // default tile
    static readonly Color SelectedColor = new Color(0.10f, 0.72f, 0.30f, 1f);  // highlighted tile
    static readonly Color PressedColor  = new Color(0.95f, 0.72f, 0.10f, 1f);  // pressed flash

    // Per-zone press-feedback objects
    readonly Dictionary<int, GameObject> pressIndicators = new Dictionary<int, GameObject>();

    const float BoardHeightAboveAnchor = 1.25f;
    const float BoardTowardAgentOffset = 0.65f;

    // ── Singleton ────────────────────────────────────────────────────────────
    public static RagMenuController Instance
    {
        get
        {
            if (_instance == null && Application.isPlaying && !PlayModeQuitGuard.IsQuitting)
            {
                GameObject go = new GameObject("RagMenuController");
                _instance = go.AddComponent<RagMenuController>();
            }
            return _instance;
        }
    }

    public static RagMenuController EnsureInScene() => Instance;

    void Awake()
    {
        if (_instance == null) _instance = this;
        else if (_instance != this) Destroy(gameObject);
    }

    // ── Public query helpers ─────────────────────────────────────────────────
    public static bool IsMenuOptionId(string id)
        => !string.IsNullOrWhiteSpace(id)
           && id.StartsWith("scene_menu_", StringComparison.OrdinalIgnoreCase);

    public static bool IsMenuStep(ActionSequenceStep step)
    {
        if (step == null) return false;
        if (string.Equals(step.actionSubType, "menu_open", StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrWhiteSpace(step.selectedMenuOptionObjectId)) return true;
        return step.menuOptions != null && step.menuOptions.Length > 0;
    }

    public static bool ShouldProtectFromInferenceHide(GameObject go)
    {
        if (go == null) return false;
        string n = go.name ?? "";
        if (n.StartsWith("RagMenuRoot_zone", StringComparison.OrdinalIgnoreCase)) return true;
        if (n.StartsWith("PressIndicator_zone", StringComparison.OrdinalIgnoreCase)) return true;
        if (IsMenuOptionId(StripToolPrefix(n))) return true;
        var meta = go.GetComponent<DeclarativeObjectMetadata>();
        if (meta != null && IsMenuOptionId(meta.objectId)) return true;
        Transform p = go.transform.parent;
        while (p != null)
        {
            if (p.name.StartsWith("RagMenuRoot_zone", StringComparison.OrdinalIgnoreCase))
                return true;
            p = p.parent;
        }
        return false;
    }

    public static string GetSelectedOptionTargetId(ActionSequenceStep step)
    {
        if (step == null) return "";
        if (!string.IsNullOrWhiteSpace(step.selectedMenuOptionObjectId))
            return step.selectedMenuOptionObjectId;
        if (step.menuContract != null && !string.IsNullOrWhiteSpace(step.menuContract.selectedOptionId))
            return step.menuContract.selectedOptionId;
        if (step.menuOptions != null)
            for (int i = 0; i < step.menuOptions.Length; i++)
                if (!string.IsNullOrWhiteSpace(step.menuOptions[i]))
                    return step.menuOptions[i];
        string[] contractOptions = GetMenuOptionIds(step);
        for (int i = 0; i < contractOptions.Length; i++)
            if (!string.IsNullOrWhiteSpace(contractOptions[i]))
                return contractOptions[i];
        return "";
    }

    static string[] GetMenuOptionIds(ActionSequenceStep step)
    {
        if (step == null) return new string[0];
        if (step.menuOptions != null && step.menuOptions.Length > 0)
            return step.menuOptions;
        if (step.menuContract == null || step.menuContract.options == null)
            return new string[0];

        var ids = new List<string>();
        foreach (var option in step.menuContract.options)
            if (option != null && !string.IsNullOrWhiteSpace(option.id))
                ids.Add(option.id);
        return ids.ToArray();
    }

    public static string GetRuntimeOptionObjectName(string optionId, int zoneIndex)
        => ZoneOptionName(optionId, zoneIndex);

    // ── Scene-object discovery ───────────────────────────────────────────────
    public void RefreshMenuOptions()
    {
        menuObjects.Clear();

        DeclarativeObjectMetadata[] metadata = FindObjectsOfType<DeclarativeObjectMetadata>(true);
        foreach (var m in metadata)
        {
            if (m == null) continue;
            if (IsMenuOptionId(m.objectId) || IsMenuOptionId(StripToolPrefix(m.gameObject.name)))
                AddMenuObject(m.gameObject);
        }

        foreach (var go in FindObjectsOfType<GameObject>(true))
        {
            if (go == null) continue;
            if (IsMenuOptionId(StripToolPrefix(go.name)))
                AddMenuObject(go);
        }

        HideAllMenus();
    }

    // ── Show / hide ──────────────────────────────────────────────────────────
    public void ShowMenuForStep(ActionSequenceStep step, int zoneIndex)
    {
        if (!IsMenuStep(step)) return;
        EnsureRuntimeMenuForStep(step, zoneIndex);
        if (menuObjects.Count == 0) RefreshMenuOptions();

        visibleOptionKeys.Clear();
        string[] optionIds = GetMenuOptionIds(step);
        for (int i = 0; i < optionIds.Length; i++)
            if (!string.IsNullOrWhiteSpace(optionIds[i]))
                visibleOptionKeys.Add(NormalizeBaseId(optionIds[i]));

        string selected = NormalizeBaseId(GetSelectedOptionTargetId(step));
        if (!string.IsNullOrWhiteSpace(selected)) visibleOptionKeys.Add(selected);

        _visibleMenuStep = step;
        _visibleMenuZoneIndex = zoneIndex;
        ApplyVisibleMenuRenderers(step, zoneIndex);
    }

    public void ReassertVisibleMenus()
    {
        if (_visibleMenuStep == null || _visibleMenuZoneIndex < 0) return;
        ApplyVisibleMenuRenderers(_visibleMenuStep, _visibleMenuZoneIndex);
    }

    void ApplyVisibleMenuRenderers(ActionSequenceStep step, int zoneIndex)
    {
        foreach (var go in menuObjects)
        {
            if (go == null) continue;
            string id = NormalizeBaseId(GetMenuObjectId(go));
            bool show = visibleOptionKeys.Contains(id) && ObjectMatchesZone(go, zoneIndex);
            SetRenderersEnabled(go, show);
            if (show)
                ApplyOptionColor(go, false);
        }

        if (menuRootsByZone.TryGetValue(Mathf.Max(0, zoneIndex), out GameObject root) && root != null)
            SetRenderersEnabled(root, true);
    }

    public void HideAllMenus()
    {
        visibleOptionKeys.Clear();
        _visibleMenuStep = null;
        _visibleMenuZoneIndex = -1;
        foreach (var go in menuObjects)
            if (go != null) SetRenderersEnabled(go, false);

        foreach (var root in menuRootsByZone.Values)
            if (root != null) SetRenderersEnabled(root, false);

        foreach (var pi in pressIndicators.Values)
            if (pi != null) SetRenderersEnabled(pi, false);
    }

    // ── Button-press visual feedback ─────────────────────────────────────────
    /// <summary>
    /// Call when the agent's finger dwell completes on the target button.
    /// Flashes the target object and shows a "PRESSED" floating label for 1 second.
    /// </summary>
    public void ShowButtonPressedFeedback(string targetObjectId, int zoneIndex)
    {
        // Flash the target scene object
        GameObject targetGO = FindSceneObject(targetObjectId, zoneIndex);
        if (targetGO != null)
            FlashObjectColor(targetGO, PressedColor);

        // Floating "PRESSED" indicator above the target
        Vector3 pos = targetGO != null
            ? targetGO.transform.position + Vector3.up * 0.6f
            : Vector3.up * 2f;

        GameObject pi = GetOrCreatePressIndicator(zoneIndex, pos);
        SetRenderersEnabled(pi, true);

        // Auto-hide after 0.9 s via a thin MonoBehaviour coroutine
        AutoHide.Schedule(pi, 0.9f);
    }

    public void MarkOptionSelected(string optionId, int zoneIndex)
    {
        string normalized = NormalizeBaseId(optionId);
        if (string.IsNullOrWhiteSpace(normalized)) return;

        CurrentlySelectedOptionId = normalized;
        CurrentlySelectedZoneIndex = zoneIndex;

        GameObject selected = GameObject.Find(ZoneOptionName(normalized, zoneIndex));
        if (selected != null)
        {
            ApplyOptionColor(selected, true);
            FlashObjectColor(selected, PressedColor);
        }

        Debug.Log($"[RagMenu] Option Saved to RAG state: {normalized} (zone={zoneIndex})");
    }

    // ── Floating spatial menu board construction ─────────────────────────────
    void EnsureRuntimeMenuForStep(ActionSequenceStep step, int zoneIndex)
    {
        string[] optionIds = GetMenuOptionIds(step);
        if (step == null || optionIds.Length == 0) return;

        GameObject root = GetOrCreateMenuRoot(zoneIndex);

        // Position: floating in front of/above the pressed control at natural hand height.
        Vector3 anchor = ResolveAnchor(step, zoneIndex);
        Vector3 toAgent = ResolveFlatDirectionToAgent(anchor, zoneIndex);
        root.transform.position = anchor + Vector3.up * BoardHeightAboveAnchor + toAgent * BoardTowardAgentOffset;

        // Vertical board: local +Z faces the agent/finger.
        root.transform.rotation = Quaternion.LookRotation(toAgent, Vector3.up);

        ClearChildren(root.transform);
        menuObjects.RemoveAll(go => go == null || go.transform.IsChildOf(root.transform));

        int count = optionIds.Length;
        BuildSpatialBoard(root.transform, count);

        float spacing = 0.48f;
        float startY  = 0.24f * (count - 1);

        for (int i = 0; i < count; i++)
        {
            string optionId = optionIds[i];
            if (string.IsNullOrWhiteSpace(optionId)) continue;

            // Physical button target on the vertical board; the mover selects it using fingertip proximity.
            GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tile.name = ZoneOptionName(optionId, zoneIndex);
            tile.transform.SetParent(root.transform, false);
            tile.transform.localPosition = new Vector3(0f, startY - i * spacing, 0.06f);
            tile.transform.localScale    = new Vector3(1.55f, 0.34f, 0.08f);

            Renderer r = tile.GetComponent<Renderer>();
            if (r != null)
            {
                Shader s = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
                r.material = new Material(s) { color = OptionColor };
            }

            Collider c = tile.GetComponent<Collider>();
            if (c != null) c.isTrigger = true;

            var metadata = tile.AddComponent<DeclarativeObjectMetadata>();
            metadata.Apply(null, NormalizeBaseId(optionId), FormatOptionLabel(optionId).Replace("\n", " "), "spatial_menu_button", "visible");

            AttachForwardLabel(root.transform, optionId, tile.transform.localPosition);
            AddMenuObject(tile);
        }
    }

    /// <summary>Dark vertical slab + title strip behind stacked button colliders.</summary>
    static void BuildSpatialBoard(Transform root, int optionCount)
    {
        float panelW = 1.95f;
        float panelH = Mathf.Max(1.55f, optionCount * 0.48f + 0.42f);

        GameObject backing = GameObject.CreatePrimitive(PrimitiveType.Cube);
        backing.name = "MenuScreenBacking";
        backing.transform.SetParent(root, false);
        backing.transform.localPosition = new Vector3(0f, 0f, 0f);
        backing.transform.localScale    = new Vector3(panelW, panelH, 0.05f);
        Renderer br = backing.GetComponent<Renderer>();
        if (br != null)
        {
            Shader s = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            br.material = new Material(s) { color = BackingColor };
        }
        Collider bc = backing.GetComponent<Collider>();
        if (bc != null) bc.isTrigger = true;

        GameObject strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        strip.name = "MenuAccentStrip";
        strip.transform.SetParent(root, false);
        strip.transform.localPosition = new Vector3(0f, panelH * 0.5f - 0.13f, 0.08f);
        strip.transform.localScale    = new Vector3(panelW, 0.18f, 0.04f);
        Renderer sr = strip.GetComponent<Renderer>();
        if (sr != null)
        {
            Shader s = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            sr.material = new Material(s) { color = AccentColor };
        }
        Collider sc = strip.GetComponent<Collider>();
        if (sc != null) sc.isTrigger = true;

        AttachScreenTitle(root, "SELECT OPTION", panelH);
    }

    static void AttachScreenTitle(Transform root, string title, float panelH)
    {
        GameObject labelGO = new GameObject("MenuTitleLabel");
        labelGO.transform.SetParent(root, false);
        labelGO.transform.localPosition = new Vector3(0f, panelH * 0.5f - 0.13f, 0.13f);
        labelGO.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        labelGO.transform.localScale    = new Vector3(0.06f, 0.06f, 0.06f);

        TextMesh tm = labelGO.AddComponent<TextMesh>();
        tm.text          = title;
        tm.anchor        = TextAnchor.MiddleCenter;
        tm.alignment     = TextAlignment.Center;
        tm.characterSize = 0.42f;
        tm.fontSize      = 42;
        tm.fontStyle     = FontStyle.Bold;
        tm.color         = new Color(0.70f, 0.80f, 1f, 1f);
    }

    /// <summary>Label sits just in front of the physical button, facing the agent.</summary>
    static void AttachForwardLabel(Transform root, string optionId, Vector3 tileLocalPosition)
    {
        GameObject labelGO = new GameObject("MenuOptionLabel");
        labelGO.transform.SetParent(root, false);
        labelGO.transform.localPosition = new Vector3(tileLocalPosition.x, tileLocalPosition.y, 0.13f);
        labelGO.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        labelGO.transform.localScale    = new Vector3(0.055f, 0.055f, 0.055f);

        TextMesh tm = labelGO.AddComponent<TextMesh>();
        tm.text          = FormatOptionLabel(optionId);
        tm.anchor        = TextAnchor.MiddleCenter;
        tm.alignment     = TextAlignment.Center;
        tm.characterSize = 0.44f;
        tm.fontSize      = 44;
        tm.fontStyle     = FontStyle.Bold;
        tm.color         = Color.white;
    }

    // ── Press indicator construction ─────────────────────────────────────────
    GameObject GetOrCreatePressIndicator(int zoneIndex, Vector3 pos)
    {
        int key = Mathf.Max(0, zoneIndex);
        if (pressIndicators.TryGetValue(key, out GameObject pi) && pi != null)
        {
            pi.transform.position = pos;
            return pi;
        }

        pi = new GameObject("PressIndicator_zone" + key);
        pi.transform.position = pos;

        // Small bright disc
        GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.transform.SetParent(pi.transform, false);
        disc.transform.localPosition = Vector3.zero;
        disc.transform.localScale    = new Vector3(0.45f, 0.025f, 0.45f);
        Renderer dr = disc.GetComponent<Renderer>();
        if (dr != null)
        {
            Shader s = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            dr.material = new Material(s) { color = PressedColor };
        }
        Collider dc = disc.GetComponent<Collider>();
        if (dc != null) dc.enabled = false;

        // "PRESSED!" label
        GameObject labelGO = new GameObject("PressedLabel");
        labelGO.transform.SetParent(pi.transform, false);
        labelGO.transform.localPosition = new Vector3(0f, 0.05f, 0f);
        labelGO.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        labelGO.transform.localScale    = new Vector3(0.16f, 0.16f, 0.16f);

        TextMesh tm = labelGO.AddComponent<TextMesh>();
        tm.text          = "PRESSED!";
        tm.anchor        = TextAnchor.MiddleCenter;
        tm.alignment     = TextAlignment.Center;
        tm.characterSize = 0.5f;
        tm.fontSize      = 48;
        tm.fontStyle     = FontStyle.Bold;
        tm.color         = Color.yellow;

        pressIndicators[key] = pi;
        return pi;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    static Vector3 ResolveAnchor(ActionSequenceStep step, int zoneIndex)
    {
        if (SceneGenerator.Instance != null && !string.IsNullOrWhiteSpace(step.targetObjectId))
        {
            Vector3 p = SceneGenerator.Instance.GetTargetPositionById(step.targetObjectId, zoneIndex);
            if (p != Vector3.zero) return p;
        }

        GameObject go = FindSceneObject(step.targetObjectId, zoneIndex);
        if (go != null) return go.transform.position;

        GameObject agent = FindAgent(zoneIndex);
        if (agent != null) return agent.transform.position + agent.transform.forward * 1.1f;

        return Vector3.zero;
    }

    static Vector3 ResolveFlatDirectionToAgent(Vector3 anchor, int zoneIndex)
    {
        GameObject agent = FindAgent(zoneIndex);
        if (agent != null)
        {
            Vector3 toAgent = agent.transform.position - anchor;
            toAgent.y = 0f;
            if (toAgent.sqrMagnitude > 0.001f)
                return toAgent.normalized;
        }

        return Vector3.back;
    }

    static GameObject FindAgent(int zoneIndex)
        => GameObject.Find("Agent_P" + (zoneIndex + 1));

    static GameObject FindSceneObject(string objectId, int zoneIndex)
    {
        if (string.IsNullOrWhiteSpace(objectId)) return null;
        if (zoneIndex >= 0)
        {
            GameObject go = GameObject.Find(objectId + "_zone" + zoneIndex);
            if (go != null) return go;
        }
        return GameObject.Find(objectId);
    }

    static void FlashObjectColor(GameObject go, Color color)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
            if (r != null && r.material != null)
                r.material.color = color;
    }

    GameObject GetOrCreateMenuRoot(int zoneIndex)
    {
        int key = Mathf.Max(0, zoneIndex);
        if (menuRootsByZone.TryGetValue(key, out GameObject existing) && existing != null)
            return existing;

        GameObject root = new GameObject("RagMenuRoot_zone" + key);
        menuRootsByZone[key] = root;
        return root;
    }

    static void ClearChildren(Transform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
            Destroy(root.GetChild(i).gameObject);
    }

    void AddMenuObject(GameObject go)
    {
        if (go == null || menuObjects.Contains(go)) return;
        menuObjects.Add(go);
    }

    static string GetMenuObjectId(GameObject go)
    {
        if (go == null) return "";
        var meta = go.GetComponent<DeclarativeObjectMetadata>();
        if (meta != null && !string.IsNullOrWhiteSpace(meta.objectId)) return meta.objectId;
        return StripToolPrefix(go.name);
    }

    static string StripToolPrefix(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "";
        return id.StartsWith("Tool_", StringComparison.OrdinalIgnoreCase) ? id.Substring(5) : id;
    }

    static string NormalizeBaseId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "";
        string clean = StripToolPrefix(id);
        int zoneIdx = clean.IndexOf("_zone", StringComparison.OrdinalIgnoreCase);
        return zoneIdx >= 0 ? clean.Substring(0, zoneIdx) : clean;
    }

    static string ZoneOptionName(string optionId, int zoneIndex)
        => NormalizeBaseId(optionId) + "_zone" + Mathf.Max(0, zoneIndex);

    static bool ObjectMatchesZone(GameObject go, int zoneIndex)
    {
        if (zoneIndex < 0 || go == null) return true;
        string id     = StripToolPrefix(GetMenuObjectId(go));
        string suffix = "_zone" + zoneIndex;
        if (id.IndexOf("_zone", StringComparison.OrdinalIgnoreCase) < 0) return true;
        return id.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }

    static void SetRenderersEnabled(GameObject go, bool enabled)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
            if (r != null) r.enabled = enabled;
    }

    static void ApplyOptionColor(GameObject go, bool selected)
    {
        Color color = selected ? SelectedColor : OptionColor;
        Renderer rootRenderer = go.GetComponent<Renderer>();
        if (rootRenderer != null && rootRenderer.material != null)
            rootRenderer.material.color = color;

        foreach (var tm in go.GetComponentsInChildren<TextMesh>(true))
            if (tm != null) tm.color = Color.white;
    }

    static string FormatOptionLabel(string optionId)
    {
        if (string.IsNullOrWhiteSpace(optionId)) return "";
        string label = NormalizeBaseId(optionId);
        if (label.StartsWith("scene_menu_", StringComparison.OrdinalIgnoreCase))
            label = label.Substring("scene_menu_".Length);
        if (label.StartsWith("menu_option_", StringComparison.OrdinalIgnoreCase))
            label = label.Substring("menu_option_".Length);
        return label.Replace("_", "\n"); // newline so two-word labels stack neatly on tile
    }
}

/// <summary>Lightweight helper that hides a GameObject after a delay without a coroutine.</summary>
internal class AutoHide : MonoBehaviour
{
    float _remaining;

    public static void Schedule(GameObject target, float seconds)
    {
        if (target == null) return;
        var h = target.GetComponent<AutoHide>() ?? target.AddComponent<AutoHide>();
        h._remaining = seconds;
        h.enabled = true;
    }

    void Update()
    {
        _remaining -= Time.deltaTime;
        if (_remaining > 0f) return;

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers) if (r != null) r.enabled = false;
        enabled = false;
    }
}
