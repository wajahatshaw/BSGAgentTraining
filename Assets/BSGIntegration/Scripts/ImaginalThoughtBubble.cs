using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Imaginal Buffer dwell UI: one procedural fluffy-cloud sprite (SDF union + outer stroke only).
/// World-space canvas; billboards to <see cref="Camera.main"/>.
/// </summary>
public class ImaginalThoughtBubble : MonoBehaviour
{
    [Tooltip("Height above agent root (feet / capsule base).")]
    public float headOffsetY = 2.05f;

    [Tooltip("World-space scale of the canvas (smaller = smaller bubble on screen).")]
    public float canvasWorldScale = 0.0035f;

    Transform _anchor;
    Canvas _canvas;
    Text _thinkingText;
    bool _built;

    public bool IsVisible => _canvas != null && _canvas.gameObject.activeSelf;

    void LateUpdate()
    {
        if (_anchor == null || !_anchor.gameObject.activeSelf) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        _anchor.rotation = Quaternion.LookRotation(_anchor.position - cam.transform.position);
    }

    public void Show()
    {
        EnsureBuilt();
        if (_canvas != null)
            _canvas.gameObject.SetActive(true);
    }

    public void Show(string message)
    {
        EnsureBuilt();
        SetText(message);
        if (_canvas != null)
            _canvas.gameObject.SetActive(true);
    }

    public void SetText(string message)
    {
        EnsureBuilt();
        if (_thinkingText == null) return;

        string text = CompactThoughtText(string.IsNullOrWhiteSpace(message) ? "Thinking..." : message.Trim());
        _thinkingText.text = text;
        _thinkingText.fontSize = ResolveThoughtFontSize(text);
        _thinkingText.resizeTextForBestFit = true;
        _thinkingText.resizeTextMinSize = 16;
        _thinkingText.resizeTextMaxSize = ResolveThoughtFontSize(text);
    }

    public void Hide()
    {
        if (_canvas != null)
            _canvas.gameObject.SetActive(false);
    }

    public static ImaginalThoughtBubble GetOrCreate(Transform agentRoot)
    {
        if (agentRoot == null) return null;
        ImaginalThoughtBubble b = agentRoot.GetComponent<ImaginalThoughtBubble>();
        if (b == null)
            b = agentRoot.gameObject.AddComponent<ImaginalThoughtBubble>();
        return b;
    }

    void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        GameObject anchorGo = new GameObject("ImaginalThoughtBubbleAnchor");
        _anchor = anchorGo.transform;
        _anchor.SetParent(transform, false);
        _anchor.localPosition = new Vector3(0f, headOffsetY, 0f);
        _anchor.localRotation = Quaternion.identity;

        GameObject canvasGo = new GameObject("ImaginalThoughtBubbleCanvas");
        canvasGo.transform.SetParent(_anchor, false);
        canvasGo.transform.localPosition = new Vector3(0.35f, 0f, 0f);
        canvasGo.transform.localScale = Vector3.one * canvasWorldScale;

        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.sortingOrder = 120;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        canvasGo.AddComponent<GraphicRaycaster>();

        RectTransform canvasRt = canvasGo.GetComponent<RectTransform>();
        canvasRt.sizeDelta = new Vector2(560f, 280f);
        canvasRt.pivot = new Vector2(0.5f, 0.5f);

        Sprite cloudSprite = CloudThoughtBubbleSpriteFactory.CreateMainCloudSprite();
        Sprite dotSprite = CloudThoughtBubbleSpriteFactory.CreateTrailDotSprite();

        BuildTrail(canvasRt, dotSprite);
        BuildCloudWithText(canvasRt, cloudSprite);

        Hide();
    }

    static void BuildTrail(RectTransform canvasRt, Sprite dotSprite)
    {
        float[] diameters = { 22f, 32f, 42f };
        float baseX = -232f;
        float baseY = -48f;
        for (int i = 0; i < 3; i++)
        {
            GameObject c = new GameObject($"ThoughtTrail_{i}");
            c.transform.SetParent(canvasRt, false);
            Image img = c.AddComponent<Image>();
            img.sprite = dotSprite;
            img.color = Color.white;

            RectTransform rt = img.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            float d = diameters[i];
            rt.sizeDelta = new Vector2(d, d);
            rt.anchoredPosition = new Vector2(baseX + i * 54f, baseY + i * 16f);
        }
    }

    void BuildCloudWithText(RectTransform canvasRt, Sprite cloudSprite)
    {
        GameObject root = new GameObject("ThoughtCloud");
        root.transform.SetParent(canvasRt, false);
        RectTransform rootRt = root.AddComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0.5f, 0.5f);
        rootRt.anchorMax = new Vector2(0.5f, 0.5f);
        rootRt.pivot = new Vector2(0.5f, 0.5f);
        rootRt.sizeDelta = new Vector2(CloudThoughtBubbleSpriteFactory.MainTexW, CloudThoughtBubbleSpriteFactory.MainTexH);
        rootRt.anchoredPosition = new Vector2(46f, 8f);

        GameObject shadowGo = new GameObject("CloudShadow");
        shadowGo.transform.SetParent(root.transform, false);
        Image shadow = shadowGo.AddComponent<Image>();
        shadow.sprite = cloudSprite;
        shadow.color = new Color(0f, 0f, 0f, 0.22f);
        shadow.raycastTarget = false;
        RectTransform srt = shadow.rectTransform;
        srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0.5f, 0.5f);
        srt.sizeDelta = new Vector2(CloudThoughtBubbleSpriteFactory.MainTexW + 10f, CloudThoughtBubbleSpriteFactory.MainTexH + 10f);
        srt.anchoredPosition = new Vector2(-7f, -9f);
        srt.SetAsFirstSibling();

        GameObject cloudGo = new GameObject("CloudFill");
        cloudGo.transform.SetParent(root.transform, false);
        Image cloud = cloudGo.AddComponent<Image>();
        cloud.sprite = cloudSprite;
        cloud.color = Color.white;
        cloud.raycastTarget = false;

        RectTransform crt = cloud.rectTransform;
        crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 0.5f);
        crt.sizeDelta = new Vector2(CloudThoughtBubbleSpriteFactory.MainTexW, CloudThoughtBubbleSpriteFactory.MainTexH);
        crt.anchoredPosition = Vector2.zero;

        GameObject textGo = new GameObject("ThinkingText");
        textGo.transform.SetParent(root.transform, false);
        Text tx = textGo.AddComponent<Text>();
        tx.text = "Thinking...";
        tx.alignment = TextAnchor.MiddleCenter;
        tx.color = new Color(0.12f, 0.12f, 0.14f);
        tx.fontSize = 25;
        tx.fontStyle = FontStyle.Bold;
        tx.font = ResolveUiFont();
        tx.raycastTarget = false;
        tx.horizontalOverflow = HorizontalWrapMode.Wrap;
        tx.verticalOverflow = VerticalWrapMode.Truncate;
        _thinkingText = tx;

        RectTransform trt = tx.rectTransform;
        trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
        trt.localRotation = Quaternion.identity;
        trt.localScale = Vector3.one;
        trt.sizeDelta = new Vector2(270f, 112f);
        trt.anchoredPosition = new Vector2(0f, 6f);
    }

    static string CompactThoughtText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Thinking...";

        text = text.Replace("\r", " ").Replace("\n", " ");
        while (text.Contains("  "))
            text = text.Replace("  ", " ");

        const int maxChars = 92;
        if (text.Length <= maxChars)
            return text;

        int cut = text.LastIndexOf(' ', maxChars - 3);
        if (cut < 48) cut = maxChars - 3;
        return text.Substring(0, cut).TrimEnd('.', ',', ';', ':', ' ') + "...";
    }

    static int ResolveThoughtFontSize(string text)
    {
        int len = string.IsNullOrEmpty(text) ? 0 : text.Length;
        if (len > 78) return 18;
        if (len > 62) return 20;
        if (len > 45) return 22;
        return 25;
    }

    static Font ResolveUiFont()
    {
        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f != null) return f;
        try
        {
            return Font.CreateDynamicFontFromOSFont("Arial", 32);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Procedural RGBA: merged SDF cloud + subtle gray stroke on outer boundary only; trail dots match.</summary>
static class CloudThoughtBubbleSpriteFactory
{
    public const int MainTexW = 420;
    public const int MainTexH = 260;

    const float PixelsPerUnit = 100f;
    static readonly Color Fill = Color.white;
    static readonly Color Stroke = new Color(0.52f, 0.54f, 0.58f, 1f);
    const float OutlinePx = 3.8f;
    const float OuterFadePx = 3f;
    const float InnerEdgeAaPx = 1.35f;

    public static Sprite CreateMainCloudSprite()
    {
        Texture2D tex = new Texture2D(MainTexW, MainTexH, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        float cx = MainTexW * 0.5f;
        float cy = MainTexH * 0.5f;

        for (int y = 0; y < MainTexH; y++)
        {
            for (int x = 0; x < MainTexW; x++)
            {
                float px = x - cx;
                float py = y - cy;
                float sdf = MainCloudSdf(px, py);
                tex.SetPixel(x, y, ColorFromSdf(sdf));
            }
        }

        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, MainTexW, MainTexH), new Vector2(0.5f, 0.5f), PixelsPerUnit);
    }

    public static Sprite CreateTrailDotSprite()
    {
        const int sz = 64;
        Texture2D tex = new Texture2D(sz, sz, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        float c = sz * 0.5f;
        float r = sz * 0.36f;
        for (int y = 0; y < sz; y++)
        {
            for (int x = 0; x < sz; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c)) - r;
                d += (Mathf.PerlinNoise(x * 0.12f + 3f, y * 0.12f) - 0.5f) * 1.6f;
                tex.SetPixel(x, y, ColorFromSdf(d, outlineScale: 0.85f, innerEdgeAaScale: 0f));
            }
        }

        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, sz, sz), new Vector2(0.5f, 0.5f), PixelsPerUnit);
    }

    /// <summary>Union of soft blobs — one continuous silhouette, no internal seams.</summary>
    static float MainCloudSdf(float px, float py)
    {
        Vector2 p = new Vector2(px, py);

        float d = float.MaxValue;
        d = Mathf.Min(d, SdfCircle(p, new Vector2(0f, 12f), 94f));
        d = Mathf.Min(d, SdfCircle(p, new Vector2(-82f, 6f), 64f));
        d = Mathf.Min(d, SdfCircle(p, new Vector2(82f, 6f), 64f));
        d = Mathf.Min(d, SdfCircle(p, new Vector2(0f, 68f), 54f));
        d = Mathf.Min(d, SdfCircle(p, new Vector2(-56f, -46f), 48f));
        d = Mathf.Min(d, SdfCircle(p, new Vector2(56f, -46f), 48f));
        d = Mathf.Min(d, SdfCircle(p, new Vector2(0f, -54f), 44f));

        d += Wobble(px, py);
        return d;
    }

    static float Wobble(float px, float py)
    {
        return (Mathf.PerlinNoise(px * 0.032f + 11.7f, py * 0.032f + 4.2f) - 0.5f) * 5.5f;
    }

    static float SdfCircle(Vector2 p, Vector2 center, float radius)
    {
        return Vector2.Distance(p, center) - radius;
    }

    static Color ColorFromSdf(float sdf, float outlineScale = 1f, float innerEdgeAaScale = 1f)
    {
        float ow = OutlinePx * outlineScale;
        float fade = OuterFadePx;
        float iaa = InnerEdgeAaPx * outlineScale * innerEdgeAaScale;

        if (sdf <= 0f && iaa < 0.001f)
            return Fill;

        if (sdf < -iaa)
            return Fill;

        if (sdf < 0f)
            return Color.Lerp(Fill, Stroke, Mathf.SmoothStep(-iaa, 0f, sdf));

        if (sdf <= ow)
            return Stroke;

        if (sdf <= ow + fade)
        {
            float t = Mathf.SmoothStep(0f, 1f, (sdf - ow) / fade);
            return new Color(Stroke.r, Stroke.g, Stroke.b, 1f - t);
        }

        return new Color(0f, 0f, 0f, 0f);
    }
}
