using UnityEngine;

/// <summary>
/// Applies a polished, type-uniform visual treatment to JSON-defined cognitive stations.
///
/// Color policy (enforced regardless of JSON per-station color):
///   Module  → MODULE_COLOR  (#1A73E8 vivid blue)  — machine-style body, tall, prominent
///   Buffer  → BUFFER_COLOR  (#E07D05 amber orange) — workbench-style body
///   Hub     → HUB_COLOR     (dark teal)
///
/// Modules are made more prominent than buffers:
///   • Taller composite body with a bright StatusOrb on top
///   • Wide GroundRing disc at the base (marks position from above)
///   • Larger root scale (set by SceneGenerator after ApplyStyle)
///   • Bigger floating label
/// </summary>
public class CognitiveStationVisualStyler : MonoBehaviour
{
    // ── Canonical palette ─────────────────────────────────────────────────────
    public static readonly Color MODULE_COLOR = new Color(0.10f, 0.45f, 0.94f, 1f); // #1A73F0 vivid blue
    public static readonly Color BUFFER_COLOR = new Color(0.88f, 0.49f, 0.02f, 1f); // #E07D05 amber orange
    public static readonly Color HUB_COLOR    = new Color(0.18f, 0.31f, 0.31f, 1f); // dark teal

    [Header("Station Metadata")]
    public string stationId;
    public string displayName;
    public string stationType;   // "module" | "buffer" | "hub"
    public string stationAction;

    [Header("Visual Settings")]
    public bool applyOnStart      = true;
    public bool showFloatingLabel = true;
    public bool addHalo           = false;
    public bool addBasePlate      = false;
    public bool billboardLabelToCamera = false;
    public bool useMushroomStyle  = true;
    [Tooltip("Bright sphere on module tops — disabled by default for a cleaner scene.")]
    public bool showStatusOrb     = false;

    [Tooltip("When true, label shows full station display name. When false, compact abbreviation.")]
    public bool useFullNameInLabel;
    [Tooltip("Added to label local Y after base placement — SceneGenerator sets ± stagger so adjacent stations sit at different heights.")]
    public float labelHeightStaggerOffset = 0f;
    public Color fallbackColor = new Color(0.45f, 0.55f, 0.75f, 1f);

    private bool applied;
    private Transform labelTransform;

    void Start()
    {
        if (applyOnStart) ApplyStyle();
    }

    void LateUpdate()
    {
        if (!billboardLabelToCamera || labelTransform == null) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        labelTransform.rotation = Quaternion.LookRotation(labelTransform.position - cam.transform.position);
    }

    [ContextMenu("Apply Cognitive Style")]
    public void ApplyStyle()
    {
        Color baseColor = ResolveBaseColor();
        SetupPrimaryRenderer(baseColor);

        if (useMushroomStyle)
            EnsureCompositeVisual(baseColor);

        if (addBasePlate)
            EnsureBasePlate(baseColor);

        if (addHalo)
            EnsureHalo(baseColor);

        if (showFloatingLabel)
            EnsureLabel(baseColor);

        if (!string.IsNullOrEmpty(stationId)
            && stationId.IndexOf("cognitive_008", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            GoalBufferStationPresenter.EnsureForStation(gameObject, stationId);
        }

        // Production Memory station → add command-type billboard UI for Phase 2/3
        if (!string.IsNullOrEmpty(stationId)
            && stationId.IndexOf("cognitive_003", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            ProductionMemoryCommandUI.GetOrCreateForStation(gameObject);
        }

        applied = true;
    }

    // ── Color resolution ──────────────────────────────────────────────────────
    // Always returns the canonical type color — never the per-station JSON color.
    // This guarantees all modules share MODULE_COLOR and all buffers share BUFFER_COLOR.
    private Color ResolveBaseColor()
    {
        string type = (stationType ?? string.Empty).ToLowerInvariant();
        if (type.Contains("module")) return MODULE_COLOR;
        if (type.Contains("buffer")) return BUFFER_COLOR;
        if (type.Contains("hub"))    return HUB_COLOR;
        return fallbackColor;
    }

    // ── Primary renderer ──────────────────────────────────────────────────────
    private void SetupPrimaryRenderer(Color baseColor)
    {
        Renderer r = GetComponent<Renderer>();
        if (r == null) return;
        Material mat = CreateMaterial(baseColor);
        if (mat != null) r.material = mat;
    }

    // ── Composite visual body ─────────────────────────────────────────────────
    private void EnsureCompositeVisual(Color baseColor)
    {
        string type = (stationType ?? string.Empty).ToLowerInvariant();
        bool isModule = type.Contains("module");
        bool isBuffer = type.Contains("buffer");
        bool isHub    = type.Contains("hub");

        if (!isModule && !isBuffer && !isHub)
        {
            string title = (displayName ?? gameObject.name).ToLowerInvariant();
            isBuffer = title.Contains("buffer");
            isModule = title.Contains("module") || title.Contains("memory");
            isHub    = title.Contains("hub");
        }

        // Hide root primitive — composite parts carry all visual weight
        Renderer rootRend = GetComponent<Renderer>();
        if (rootRend != null) rootRend.enabled = false;

        transform.localScale = Vector3.one;

        const string visualRootName = "CognitiveCompositeVisual";
        Transform vr = transform.Find(visualRootName);
        if (vr == null)
        {
            vr = new GameObject(visualRootName).transform;
            vr.SetParent(transform, false);
        }
        vr.localPosition = Vector3.zero;
        vr.localRotation = Quaternion.identity;
        vr.localScale    = Vector3.one;

        // Remove any legacy cap from older versions
        Transform oldCap = transform.Find("MushroomCap");
        if (oldCap != null) Destroy(oldCap.gameObject);

        if (isModule)
            BuildModuleVisual(vr, baseColor);
        else if (isBuffer)
            BuildBufferVisual(vr, baseColor);
        else if (isHub)
            BuildHubVisual(vr, baseColor);
    }

    // ── Module: tall machine + status orb + ground ring ──────────────────────
    //   Intentionally taller and more prominent than buffers so they stand out
    //   as the primary "brain stations" in the scene.
    private void BuildModuleVisual(Transform parent, Color c)
    {
        // Main body — compact machine profile (closer to buffer footprint)
        Part(parent, "CoreBody",    PrimitiveType.Cube,     new Vector3(0f,   0.88f,  0f),    new Vector3(1.22f, 1.32f, 0.92f), Shade(c, -0.10f));
        // Top housing block
        Part(parent, "TopHousing",  PrimitiveType.Cube,     new Vector3(0f,   1.62f,  0f),    new Vector3(1.14f, 0.26f, 0.80f), Shade(c,  0.18f));
        // Front status panel
        Part(parent, "FrontPanel",  PrimitiveType.Cube,     new Vector3(0f,   0.92f,  0.46f), new Vector3(0.76f, 0.28f, 0.12f), Shade(c, -0.48f));
        // Side drums (ventilation look)
        Part(parent, "SideDrum_L",  PrimitiveType.Cylinder, new Vector3(-0.58f, 0.78f, 0f),  new Vector3(0.16f, 0.36f, 0.16f), Shade(c, -0.06f));
        Part(parent, "SideDrum_R",  PrimitiveType.Cylinder, new Vector3( 0.58f, 0.78f, 0f),  new Vector3(0.16f, 0.36f, 0.16f), Shade(c, -0.06f));
        // Foot blocks
        Part(parent, "Foot_L",      PrimitiveType.Cube,     new Vector3(-0.38f, 0.10f, -0.28f), new Vector3(0.24f, 0.14f, 0.24f), Shade(c, -0.28f));
        Part(parent, "Foot_R",      PrimitiveType.Cube,     new Vector3( 0.38f, 0.10f, -0.28f), new Vector3(0.24f, 0.14f, 0.24f), Shade(c, -0.28f));

        if (showStatusOrb)
        {
            Color orbColor = Color.Lerp(c, Color.white, 0.72f);
            Part(parent, "StatusOrb", PrimitiveType.Sphere, new Vector3(0f, 2.40f, 0f), new Vector3(0.42f, 0.42f, 0.42f), orbColor);
        }
        else
        {
            Transform oldOrb = parent.Find("StatusOrb");
            if (oldOrb != null) Destroy(oldOrb.gameObject);
        }

        // Ground ring — subtle footprint (buffers have no ring; keep modules understated)
        Color ringColor = Shade(c, 0.28f);
        Part(parent, "GroundRing",  PrimitiveType.Cylinder, new Vector3(0f,   0.02f,  0f),    new Vector3(1.55f, 0.04f, 1.55f), ringColor);
    }

    // ── Buffer: workbench-style, uniform amber ────────────────────────────────
    private void BuildBufferVisual(Transform parent, Color c)
    {
        Part(parent, "BenchTop",   PrimitiveType.Cube,     new Vector3(0f,   0.84f,  0f),    new Vector3(1.80f, 0.36f, 1.12f), Shade(c,  0.10f));
        Part(parent, "BenchRail",  PrimitiveType.Cube,     new Vector3(0f,   1.12f, -0.38f), new Vector3(1.68f, 0.16f, 0.24f), Shade(c,  0.22f));
        Part(parent, "BenchPanel", PrimitiveType.Cube,     new Vector3(0f,   0.60f,  0.44f), new Vector3(1.04f, 0.22f, 0.20f), Shade(c, -0.32f));
        Part(parent, "Leg_FL",     PrimitiveType.Cube,     new Vector3(-0.68f, 0.28f,  0.32f), new Vector3(0.16f, 0.48f, 0.16f), Shade(c, -0.24f));
        Part(parent, "Leg_FR",     PrimitiveType.Cube,     new Vector3( 0.68f, 0.28f,  0.32f), new Vector3(0.16f, 0.48f, 0.16f), Shade(c, -0.24f));
        Part(parent, "Leg_BL",     PrimitiveType.Cube,     new Vector3(-0.68f, 0.28f, -0.32f), new Vector3(0.16f, 0.48f, 0.16f), Shade(c, -0.24f));
        Part(parent, "Leg_BR",     PrimitiveType.Cube,     new Vector3( 0.68f, 0.28f, -0.32f), new Vector3(0.16f, 0.48f, 0.16f), Shade(c, -0.24f));
    }

    // ── Hub: layered disc tower ───────────────────────────────────────────────
    private void BuildHubVisual(Transform parent, Color c)
    {
        Part(parent, "HubBase",  PrimitiveType.Cylinder, new Vector3(0f, 0.68f, 0f), new Vector3(2.10f, 0.64f, 2.10f), Shade(c, -0.18f));
        Part(parent, "HubCore",  PrimitiveType.Cylinder, new Vector3(0f, 1.56f, 0f), new Vector3(1.44f, 0.52f, 1.44f), Shade(c,  0.10f));
        Part(parent, "HubCrown", PrimitiveType.Cylinder, new Vector3(0f, 2.08f, 0f), new Vector3(1.76f, 0.12f, 1.76f), Shade(c,  0.22f));
    }

    // ── Base plate ────────────────────────────────────────────────────────────
    private void EnsureBasePlate(Color baseColor)
    {
        const string n = "CognitiveBasePlate";
        if (transform.Find(n) != null) return;

        GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        plate.name = n;
        plate.transform.SetParent(transform, false);
        plate.transform.localPosition = new Vector3(0f, -0.42f, 0f);
        plate.transform.localScale    = new Vector3(1.4f, 0.04f, 1.4f);

        Collider col = plate.GetComponent<Collider>();
        if (col != null) Destroy(col);

        Renderer r = plate.GetComponent<Renderer>();
        if (r != null)
        {
            Material m = CreateMaterial(Color.Lerp(baseColor, Color.black, 0.55f));
            if (m != null) r.material = m;
        }
    }

    // ── Halo ring ─────────────────────────────────────────────────────────────
    private void EnsureHalo(Color baseColor)
    {
        const string n = "CognitiveHalo";
        if (transform.Find(n) != null) return;

        GameObject halo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        halo.name = n;
        halo.transform.SetParent(transform, false);
        halo.transform.localPosition = new Vector3(0f, 0.55f, 0f);
        halo.transform.localScale    = new Vector3(1.45f, 0.08f, 1.45f);

        Collider col = halo.GetComponent<Collider>();
        if (col != null) Destroy(col);

        Renderer r = halo.GetComponent<Renderer>();
        if (r != null)
        {
            Material m = CreateMaterial(new Color(baseColor.r, baseColor.g, baseColor.b, 0.45f));
            if (m != null) r.material = m;
        }
    }

    // ── Floating label ────────────────────────────────────────────────────────
    private void EnsureLabel(Color baseColor)
    {
        const string n = "CognitiveLabel";
        Transform existing = transform.Find(n);
        GameObject label   = existing != null ? existing.gameObject : new GameObject(n);
        if (existing == null)
            label.transform.SetParent(transform, false);

        labelTransform = label.transform;

        TextMesh tm = label.GetComponent<TextMesh>();
        if (tm == null) tm = label.AddComponent<TextMesh>();

        tm.anchor    = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.fontStyle = FontStyle.Bold;

        string type = (stationType ?? string.Empty).ToLowerInvariant();
        bool isModule = type.Contains("module");

        // Modules get a brighter label so they stand out more
        tm.color = isModule ? Color.white : new Color(1f, 0.95f, 0.80f, 1f);
        tm.text  = BuildLabelText();

        if (useFullNameInLabel)
        {
            // Match buffer label scale; modules sit slightly higher on the shorter body.
            float labelY     = isModule ? 1.82f : 1.72f;
            float charSize   = isModule ? 0.037f : 0.036f;
            int   fontSize   = isModule ? 27 : 26;

            labelY += labelHeightStaggerOffset;
            label.transform.localPosition = new Vector3(0f, labelY, 0f);
            label.transform.localRotation = Quaternion.Euler(68f, 0f, 0f);
            tm.characterSize = charSize;
            tm.fontSize      = fontSize;
        }
        else
        {
            float labelY   = isModule ? 2.05f : 2.40f;
            float charSize = isModule ? 0.062f : 0.060f;
            int   fontSize = isModule ? 38 : 36;

            labelY += labelHeightStaggerOffset;
            label.transform.localPosition = new Vector3(0f, labelY, 0f);
            label.transform.localRotation = Quaternion.Euler(75f, 0f, 0f);
            tm.characterSize = charSize;
            tm.fontSize      = fontSize;
        }
    }

    // ── Label text builder ────────────────────────────────────────────────────
    private string BuildLabelText()
    {
        string name = !string.IsNullOrWhiteSpace(displayName) ? displayName : gameObject.name;
        int zoneBracket = name.IndexOf(" [Z", System.StringComparison.Ordinal);
        if (zoneBracket >= 0) name = name.Substring(0, zoneBracket);

        string action = !string.IsNullOrWhiteSpace(stationAction) ? stationAction.ToUpperInvariant() : string.Empty;

        if (useFullNameInLabel)
        {
            string line = name.Trim();
            if (line.Length > 22) line = line.Substring(0, 20) + "…";
            return string.IsNullOrWhiteSpace(action) ? line : $"{line}\n<{action}>";
        }

        string abbr = GetAbbreviation(name);
        return string.IsNullOrWhiteSpace(action) ? abbr : $"{abbr}\n<{action}>";
    }

    private static string GetAbbreviation(string name)
    {
        switch (name.Trim())
        {
            case "Intentional Module":      return "Intent\nMod";
            case "Production Buffer":       return "Prod\nBuf";
            case "Temporal Module":         return "Temporal\nMod";
            case "Goal Buffer":             return "Goal\nBuf";
            case "Motor Module":            return "Motor\nMod";
            case "Visual Buffer":           return "Visual\nBuf";
            case "Visual Location Buffer":  return "VisLoc\nBuf";
            case "Temporal Buffer":         return "Temp\nBuf";
            case "Goal Module":             return "Goal\nMod";
            case "Retrieval Buffer":        return "Retriev\nBuf";
            case "Imaginal Buffer":         return "Imaginal\nBuf";
            case "Declarative Module":      return "Decl\nMod";
            case "Declarative Memory":      return "Declarative\nMemory";
            case "Aural Buffer":            return "Aural\nBuf";
            case "Aural Location Buffer":   return "AurLoc\nBuf";
            case "Manual Module":           return "Manual\nMod";
            case "Vocal Buffer":            return "Vocal\nBuf";
            default:
                string[] words = name.Split(' ');
                if (words.Length >= 2)
                    return $"{Trunc(words[0], 7)}\n{Trunc(words[1], 7)}";
                return Trunc(name, 10);
        }
    }

    private static string Trunc(string s, int max) => s.Length > max ? s.Substring(0, max) : s;

    // ── Part builder (creates or reuses child primitive) ──────────────────────
    private void Part(Transform parent, string partName, PrimitiveType primitive,
                      Vector3 localPos, Vector3 localScale, Color color)
    {
        Transform existing = parent.Find(partName);
        GameObject part    = existing != null ? existing.gameObject : GameObject.CreatePrimitive(primitive);
        if (existing == null)
        {
            part.name = partName;
            part.transform.SetParent(parent, false);
            Collider c = part.GetComponent<Collider>();
            if (c != null) Destroy(c);
        }

        part.transform.localPosition = localPos;
        part.transform.localRotation = Quaternion.identity;
        part.transform.localScale    = localScale;

        Renderer r = part.GetComponent<Renderer>();
        if (r != null)
        {
            Material m = CreateMaterial(color);
            if (m != null) r.material = m;
        }
    }

    // ── Material factory ──────────────────────────────────────────────────────
    private static Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");
        if (shader == null) return null;

        Material mat = new Material(shader);
        mat.color = color;
        return mat;
    }

    private static bool LooksLikeDefaultWhite(Color c) =>
        c.r > 0.93f && c.g > 0.93f && c.b > 0.93f;

    private static Color Shade(Color baseColor, float amount)
    {
        if (amount >= 0f)
            return Color.Lerp(baseColor, Color.white, Mathf.Clamp01(amount));
        return Color.Lerp(baseColor, Color.black, Mathf.Clamp01(-amount));
    }
}
