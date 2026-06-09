using System.Collections;
using UnityEngine;

/// <summary>
/// Three procedural spheres on Goal Buffer (<c>cognitive_008</c>) — bottom / middle / top.
/// Balls parent to <see cref="CognitiveStationVisualStyler"/> composite so they sit on the bench.
/// </summary>
[DefaultExecutionOrder(20)]
public class GoalBufferStationPresenter : MonoBehaviour
{
    static readonly System.Collections.Generic.Dictionary<int, GoalBufferStationPresenter> Registry
        = new System.Collections.Generic.Dictionary<int, GoalBufferStationPresenter>();

    const string CompositeName = "CognitiveCompositeVisual";

    [Tooltip("Zone index (must match RagSequenceAgentMover / BSGMLAgent). Leave -1 to parse from object name e.g. cognitive_008_zone1.")]
    public int zoneIndex = -1;

    [Tooltip("Parent for balls; auto-resolved to composite workbench if present.")]
    public Transform ballsParentOverride;

    const float BallScale = 0.36f;
    const float Spacing = 0.52f;
    const float BallLocalYOnBench = 1.12f;
    const float BallLocalZ = 0.2f;

    static readonly string[] SlotLabels = { "bottom", "middle", "top" };

    Transform[] ballRoots = new Transform[3];
    Renderer[] ballRenderers = new Renderer[3];
    readonly Color NormalOrange = new Color(0.98f, 0.45f, 0.08f);
    readonly Color DimmedGray = new Color(0.32f, 0.32f, 0.36f);
    readonly Color LabelNormal = new Color(0.96f, 0.96f, 1f);
    readonly Color LabelDimmed = new Color(0.45f, 0.45f, 0.48f);

    [Header("Ball word labels (TextMesh — match station title)")]
    [Tooltip("Station title uses characterSize 0.036 on scale-1; balls are ~0.36 scale so labels need larger local characterSize. Keep transform scale at 1 unless tweaking.")]
    public float ballLabelTransformScale = 1.1f;
    [Tooltip("Slightly smaller than the Goal Buffer title (~0.036 on root); compensated for ball parent scale.")]
    public float ballLabelCharacterSize = 0.095f;
    [Tooltip("Buffer station title uses fontSize 26; ball labels default just under that for readability.")]
    public int ballLabelMeshFontSize = 25;
    [Tooltip("Local Y above ball center so text clears the sphere.")]
    public float ballLabelHeightAboveBall = 0.4f;

    /// <summary>Called from <see cref="CognitiveStationVisualStyler.ApplyStyle"/> so balls exist even if Start order varies.</summary>
    public static void EnsureForStation(GameObject stationRoot, string stationId)
    {
        if (stationRoot == null || string.IsNullOrEmpty(stationId)) return;
        if (stationId.IndexOf("cognitive_008", System.StringComparison.OrdinalIgnoreCase) < 0) return;

        GoalBufferStationPresenter p = stationRoot.GetComponent<GoalBufferStationPresenter>()
            ?? stationRoot.AddComponent<GoalBufferStationPresenter>();
        int z = ParseZoneIndexFromStationId(stationId);
        if (p.zoneIndex < 0)
            p.zoneIndex = z;
        p.EnsureBallsBuilt();
        p.RegisterZone();
    }

    public static int ParseZoneIndexFromStationId(string stationId)
    {
        if (string.IsNullOrEmpty(stationId)) return 0;
        int idx = stationId.LastIndexOf("_zone", System.StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return 0;
        string tail = stationId.Substring(idx + 5);
        return int.TryParse(tail, out int z) ? z : 0;
    }

    void Start()
    {
        if (zoneIndex < 0)
            zoneIndex = ParseZoneIndexFromStationId(gameObject.name);

        // Older builds serialized huge label defaults (scale ~3.2, char ~0.12). Normalize once.
        if (ballLabelTransformScale >= 2.4f && ballLabelCharacterSize >= 0.11f)
        {
            ballLabelTransformScale = 1.1f;
            ballLabelCharacterSize = 0.095f;
            ballLabelMeshFontSize = 25;
            ballLabelHeightAboveBall = 0.4f;
        }

        EnsureBallsBuilt();
        RegisterZone();
    }

    public void ConfigureZone(int z)
    {
        if (zoneIndex >= 0 && Registry.TryGetValue(zoneIndex, out var old) && old == this)
            Registry.Remove(zoneIndex);

        zoneIndex = Mathf.Max(0, z);
        EnsureBallsBuilt();
        RegisterZone();
    }

    void OnDestroy()
    {
        if (zoneIndex >= 0 && Registry.TryGetValue(zoneIndex, out var p) && p == this)
            Registry.Remove(zoneIndex);
    }

    void RegisterZone()
    {
        if (zoneIndex >= 0)
            Registry[zoneIndex] = this;
    }

    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        for (int i = 0; i < 3; i++)
        {
            if (ballRoots[i] == null || !ballRoots[i].gameObject.activeInHierarchy) continue;
            Transform lbl = ballRoots[i].Find("BallLabel");
            if (lbl == null) continue;
            lbl.rotation = Quaternion.LookRotation(lbl.position - cam.transform.position);
        }
    }

    public static GoalBufferStationPresenter TryGetForZone(int z)
    {
        if (Registry.TryGetValue(z, out var p) && p != null)
            return p;

        GoalBufferStationPresenter[] presenters = FindObjectsOfType<GoalBufferStationPresenter>();
        for (int i = 0; i < presenters.Length; i++)
        {
            GoalBufferStationPresenter candidate = presenters[i];
            if (candidate == null) continue;
            if (candidate.zoneIndex < 0)
                candidate.zoneIndex = ParseZoneIndexFromStationId(candidate.gameObject.name);
            if (candidate.zoneIndex != z) continue;

            candidate.EnsureBallsBuilt();
            candidate.RegisterZone();
            return candidate;
        }

        return null;
    }

    Transform ResolveBallsParent()
    {
        if (ballsParentOverride != null) return ballsParentOverride;

        Transform composite = transform.Find(CompositeName);
        return composite != null ? composite : transform;
    }

    public void EnsureBallsBuilt()
    {
        Transform parent = ResolveBallsParent();

        if (ballRoots[0] != null)
        {
            for (int i = 0; i < 3; i++)
            {
                if (ballRoots[i] != null && ballRoots[i].parent != parent)
                    ballRoots[i].SetParent(parent, false);
                EnsureBallLabel(ballRoots[i], i);
            }
            return;
        }

        for (int i = 0; i < 3; i++)
        {
            GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = $"GoalBufferBall_{SlotLabels[i]}";
            ball.transform.SetParent(parent, false);
            ball.transform.localPosition = new Vector3((i - 1) * Spacing, BallLocalYOnBench, BallLocalZ);
            ball.transform.localScale = Vector3.one * BallScale;
            Collider col = ball.GetComponent<Collider>();
            if (col != null) Destroy(col);

            Renderer r = ball.GetComponent<Renderer>();
            if (r != null)
            {
                r.material.color = NormalOrange;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            ballRoots[i] = ball.transform;
            ballRenderers[i] = r;
            EnsureBallLabel(ball.transform, i);
        }
    }

    void EnsureBallLabel(Transform ball, int slotIndex)
    {
        if (ball == null || slotIndex < 0 || slotIndex >= SlotLabels.Length) return;

        Transform lblTf = ball.Find("BallLabel");
        bool created = false;
        if (lblTf == null)
        {
            GameObject go = new GameObject("BallLabel");
            lblTf = go.transform;
            lblTf.SetParent(ball, false);
            lblTf.gameObject.AddComponent<TextMesh>();
            created = true;
        }

        TextMesh tm = lblTf.GetComponent<TextMesh>();
        if (tm == null)
        {
            tm = lblTf.gameObject.AddComponent<TextMesh>();
            created = true;
        }

        ApplyBallLabelSizing(tm, lblTf, slotIndex);
        if (created)
            tm.color = LabelNormal;
    }

    void ApplyBallLabelSizing(TextMesh tm, Transform lblTf, int slotIndex)
    {
        float scale = Mathf.Clamp(ballLabelTransformScale, 0.35f, 2.5f);
        float ch = Mathf.Clamp(ballLabelCharacterSize, 0.028f, 0.14f);
        int fs = Mathf.Clamp(ballLabelMeshFontSize, 14, 34);
        float yUp = Mathf.Clamp(ballLabelHeightAboveBall, 0.22f, 0.65f);

        lblTf.localPosition = new Vector3(0f, yUp, 0f);
        lblTf.localScale = Vector3.one * scale;

        tm.text = SlotLabels[slotIndex];
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.fontSize = fs;
        tm.characterSize = ch;
        tm.fontStyle = FontStyle.Bold;
        tm.richText = false;
    }

    static void SetBallLabelColor(Transform ball, Color c)
    {
        if (ball == null) return;
        Transform lbl = ball.Find("BallLabel");
        if (lbl == null) return;
        TextMesh tm = lbl.GetComponent<TextMesh>();
        if (tm != null) tm.color = c;
    }

    public void ResetAllVisually()
    {
        EnsureBallsBuilt();

        for (int i = 0; i < 3; i++)
        {
            if (ballRoots[i] == null) continue;
            ballRoots[i].gameObject.SetActive(true);
            ballRoots[i].localScale = Vector3.one * BallScale;
            if (ballRenderers[i] != null) ballRenderers[i].material.color = NormalOrange;
            SetBallLabelColor(ballRoots[i], LabelNormal);
        }
    }

    public void SetSlotDimmed(GoalBufferSlot slot)
    {
        EnsureBallsBuilt();
        int i = (int)slot;
        if (ballRenderers[i] != null) ballRenderers[i].material.color = DimmedGray;
        SetBallLabelColor(ballRoots[i], LabelDimmed);
    }

    public void PlayGrab(GoalBufferSlot slot)
    {
        EnsureBallsBuilt();
        StartCoroutine(CoGrab(slot));
    }

    IEnumerator CoGrab(GoalBufferSlot slot)
    {
        int i = (int)slot;
        if (ballRoots[i] == null) yield break;

        Vector3 start = ballRoots[i].localScale;
        float dur = 0.35f;
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            ballRoots[i].localScale = Vector3.Lerp(start, Vector3.zero, t / dur);
            yield return null;
        }

        ballRoots[i].gameObject.SetActive(false);
    }

    public void ApplyPreviewForContract(GoalBufferContract c)
    {
        if (c == null) return;
        ResetAllVisually();
        for (int i = 0; i < 3; i++)
        {
            if (GoalBufferTripleOrchestrator.GetSlotText(c, (GoalBufferSlot)i) == null)
                SetSlotDimmed((GoalBufferSlot)i);
        }
    }
}
