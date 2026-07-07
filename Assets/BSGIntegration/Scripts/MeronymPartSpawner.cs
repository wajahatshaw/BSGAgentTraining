using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns the interactive meronym PARTS (left_mouse_button, scroll_wheel, enter_key, p_key,
/// desktop_surface, …) that the RAG now defines inside <c>sceneEntities[].meronyms</c> instead of as
/// stand-alone scene objects.
///
/// The RAG authors, per meronym: id, name, meronym_type, thread, localOffset (+ size/color/renderShape
/// for spawnable ones). Unity resolves the world placement exactly as:
///
///     part.worldPosition = parentTool.worldPosition + localOffset
///
/// Each part is a small primitive with its own renderer (so the fingertip press-contact can read its
/// bounds and turn it green) and is registered in <see cref="MeronymPartRegistry"/> keyed by
/// (parentId, meronymName, zone) so the mover can drive the press onto the exact part.
///
/// The spawner self-installs at runtime and keeps a light watch so it works no matter which pipeline
/// (single-player <c>GenerateEnvironment</c> or the multiplayer embed anchor) created the parent tools,
/// and it re-tracks the parent transform each tick so the parts ride along if the anchor repositions.
/// </summary>
public class MeronymPartSpawner : MonoBehaviour
{
    const int PhysicalZoneIndex = 0;   // sceneEntities physical env is authored for zone 0.
    const float TickSeconds = 0.5f;

    static MeronymPartSpawner _instance;
    readonly List<SceneEntityParts> _entities = new List<SceneEntityParts>();
    string _parsedFromRag;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (_instance != null)
            return;
        var go = new GameObject("~MeronymPartSpawner");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<MeronymPartSpawner>();
    }

    void OnEnable() => StartCoroutine(Watch());

    IEnumerator Watch()
    {
        var wait = new WaitForSeconds(TickSeconds);
        while (true)
        {
            try { Tick(); }
            catch (Exception ex) { Debug.LogWarning($"[MeronymPartSpawner] tick error: {ex.Message}"); }
            yield return wait;
        }
    }

    void Tick()
    {
        string ragText = ResolveActiveRagJsonText();
        if (string.IsNullOrWhiteSpace(ragText))
            return;

        // Re-parse only when the RAG text changes (scene (re)load).
        if (!ReferenceEquals(ragText, _parsedFromRag) && !string.Equals(ragText, _parsedFromRag, StringComparison.Ordinal))
        {
            _parsedFromRag = ragText;
            _entities.Clear();
            _entities.AddRange(MeronymJsonParser.ParseSpawnableEntities(ragText));
            MeronymPartRegistry.Clear();
            GameObject labelHolder = GameObject.Find("~MeronymLabels");
            if (labelHolder != null)
                Destroy(labelHolder);
        }

        foreach (SceneEntityParts entity in _entities)
        {
            GameObject parent = FindParentTool(entity.parentId);
            if (parent == null)
            {
                if (_missingParentsLogged.Add(entity.parentId))
                {
                    string names = string.Join(", ", entity.parts.ConvertAll(p => p.name));
                    Debug.LogWarning($"[MeronymPartSpawner] Parent tool for '{entity.parentId}' not found yet — its parts ({names}) can't spawn. Looked for Tool_{entity.parentId}_zone0 / Tool_{entity.parentId}.");
                }
                continue;
            }
            _missingParentsLogged.Remove(entity.parentId);

            foreach (MeronymPart part in entity.parts)
                EnsureAndTrackPart(parent, entity.parentId, part);
        }
    }

    readonly HashSet<string> _missingParentsLogged = new HashSet<string>();

    void EnsureAndTrackPart(GameObject parent, string parentId, MeronymPart part)
    {
        GameObject go = MeronymPartRegistry.Get(parentId, part.name, PhysicalZoneIndex);
        if (go == null)
        {
            go = CreatePartObject(parent, part);
            MeronymPartRegistry.Register(parentId, part.name, PhysicalZoneIndex, go);
            Debug.Log($"[MeronymPartSpawner] Spawned meronym '{part.name}' on {parentId} at {go.transform.position} (offset {part.localOffset}).");
        }

        // Shrink the parent box (once) so its TOP sits at a reachable height, then place the meronym on
        // that top surface — so the agent can physically touch/press it from a natural standing reach.
        ShrinkParentToReachable(parent, parentId);

        // Track the parent each tick so parts ride along if the layout anchor repositions/rescales it.
        Vector3 s = parent.transform.lossyScale;
        Vector3 basePos = EnvironmentSolidCollider.TryGetVisibleBounds(parent.transform, out Bounds b)
            ? new Vector3(b.center.x, b.max.y, b.center.z)
            : parent.transform.position;
        go.transform.position = basePos + Vector3.Scale(part.localOffset, s);
        go.transform.localScale = ScaledSize(part, s);
        TrackLabel(go);
    }

    // Target world height (metres) for the top of a press-target box, so meronyms on top are at a natural
    // standing reach for the humanoid agent.
    const float ReachableTopHeight = 1.0f;
    readonly HashSet<string> _resizedParents = new HashSet<string>();

    void ShrinkParentToReachable(GameObject parent, string parentId)
    {
        if (_resizedParents.Contains(parentId))
            return;
        _resizedParents.Add(parentId);

        Renderer r = parent.GetComponent<Renderer>() ?? parent.GetComponentInChildren<Renderer>();
        if (r == null)
            return;

        float baseY = r.bounds.min.y;
        float curHeight = r.bounds.size.y;
        float targetHeight = ReachableTopHeight - baseY;
        if (targetHeight < 0.1f || curHeight <= targetHeight + 0.05f)
            return;   // already short enough

        Vector3 ls = parent.transform.localScale;
        ls.y *= targetHeight / curHeight;
        parent.transform.localScale = ls;

        // Re-seat the base on the ground (scaling about the pivot would otherwise sink/raise it).
        Renderer after = parent.GetComponent<Renderer>() ?? parent.GetComponentInChildren<Renderer>();
        if (after != null)
            parent.transform.position += new Vector3(0f, baseY - after.bounds.min.y, 0f);

        Debug.Log($"[MeronymPartSpawner] Shrank '{parentId}' so its top is at reachable height (~{ReachableTopHeight}m) for pressing.");
    }

    // The floating name label hovers just above the part's top. It lives under its own UNSCALED holder
    // (not under the part) so the part's non-uniform scale can't skew the billboarded text. Kept small so
    // adjacent parts' labels don't overlap into an unreadable blur.
    const float LabelWorldScale = 0.15f;
    const float LabelHoverMargin = 0.05f;

    static void TrackLabel(GameObject part)
    {
        Transform label = LabelsHolder().Find(part.name + "::label");
        if (label == null)
            return;
        Vector3 pls = part.transform.lossyScale;
        label.position = part.transform.position + Vector3.up * (pls.y * 0.5f + LabelHoverMargin);
        label.localScale = Vector3.one * LabelWorldScale;
    }

    // worldPosition = parent.worldPosition + localOffset, generalized by the parent's lossyScale so the
    // part stays on the object when the multiplayer anchor scales the cluster (identical to the literal
    // contract when the parent is at scale 1). A cylinder's default mesh is 2 units tall → halve Y.
    static Vector3 ScaledSize(MeronymPart part, Vector3 parentScale)
    {
        Vector3 world = Vector3.Scale(part.size, parentScale);
        if (string.Equals(part.renderShape, "cylinder", StringComparison.OrdinalIgnoreCase))
            world.y *= 0.5f;
        return world;
    }

    GameObject CreatePartObject(GameObject parent, MeronymPart part)
    {
        PrimitiveType prim = string.Equals(part.renderShape, "cylinder", StringComparison.OrdinalIgnoreCase)
            ? PrimitiveType.Cylinder
            : PrimitiveType.Cube;
        GameObject go = GameObject.CreatePrimitive(prim);
        go.name = $"Meronym_{parent.name}_{part.name}";
        // Parts live in a flat holder (not under the parent) so they never inflate the parent's visible
        // bounds; position/scale are re-applied every tick in EnsureAndTrackPart.
        go.transform.SetParent(PartsHolder(), true);

        // The part must not block agent navigation — it's a press affordance read via renderer bounds.
        Collider col = go.GetComponent<Collider>();
        if (col != null)
            col.isTrigger = true;

        Renderer rend = go.GetComponent<Renderer>();
        if (rend != null && rend.material != null && ColorUtility.TryParseHtmlString(part.color, out Color c))
            rend.material.color = c;

        var meta = go.AddComponent<DeclarativeObjectMetadata>();
        meta.objectId = part.id;
        meta.displayName = part.name;

        CreateLabel(go, part.name);
        return go;
    }

    // Floating, camera-facing name label above the part for a clearer UI. Named "*::label" (contains
    // "label") so EnvironmentSolidCollider.TryGetVisibleBounds filters it out of any press-contact bounds.
    void CreateLabel(GameObject part, string text)
    {
        Transform holder = LabelsHolder();
        string labelName = part.name + "::label";
        Transform existing = holder.Find(labelName);
        if (existing != null)
            Destroy(existing.gameObject);

        var labelGO = new GameObject(labelName);
        labelGO.transform.SetParent(holder, false);
        labelGO.transform.localScale = Vector3.one * LabelWorldScale;

        TextMesh tm = labelGO.AddComponent<TextMesh>();
        tm.text = (text ?? string.Empty).Replace('_', ' ');
        tm.fontSize = 48;              // high res for crispness; world size comes from characterSize × scale
        tm.characterSize = 0.1f;       // small so the label reads as a tag, not a billboard
        tm.anchor = TextAnchor.LowerCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.white;

        labelGO.AddComponent<IndicatorBillboard>();
    }

    static Transform LabelsHolder()
    {
        GameObject holder = GameObject.Find("~MeronymLabels");
        if (holder == null)
            holder = new GameObject("~MeronymLabels");
        return holder.transform;
    }

    Transform PartsHolder()
    {
        GameObject holder = GameObject.Find("~MeronymParts");
        if (holder == null)
            holder = new GameObject("~MeronymParts");
        return holder.transform;
    }

    static GameObject FindParentTool(string parentId)
    {
        GameObject go = GameObject.Find($"Tool_{parentId}_zone{PhysicalZoneIndex}");
        if (go == null) go = GameObject.Find($"Tool_{parentId}");
        if (go == null) go = GameObject.Find($"{parentId}_zone{PhysicalZoneIndex}");
        return go;
    }

    static string ResolveActiveRagJsonText()
    {
        SceneUILoader loader = FindObjectOfType<SceneUILoader>();
        if (loader == null)
            return string.Empty;
        if (!string.IsNullOrEmpty(loader.MergedRawRagJson)) return loader.MergedRawRagJson;
        if (!string.IsNullOrEmpty(loader.RawJsonText)) return loader.RawJsonText;
        if (!string.IsNullOrEmpty(loader.EffectivePipelineJson)) return loader.EffectivePipelineJson;
        return string.Empty;
    }
}

/// <summary>Runtime lookup of spawned meronym part GameObjects by (parentId, meronymName, zone).</summary>
public static class MeronymPartRegistry
{
    static readonly Dictionary<string, GameObject> _byKey = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

    static string Key(string parentId, string meronymName, int zone) =>
        $"{(parentId ?? string.Empty).Trim()}|{(meronymName ?? string.Empty).Trim()}|{zone}".ToLowerInvariant();

    public static void Register(string parentId, string meronymName, int zone, GameObject go)
    {
        if (go != null)
            _byKey[Key(parentId, meronymName, zone)] = go;
    }

    /// <summary>Get the part GameObject, or null if not spawned / destroyed.</summary>
    public static GameObject Get(string parentId, string meronymName, int zone)
    {
        if (_byKey.TryGetValue(Key(parentId, meronymName, zone), out GameObject go) && go != null)
            return go;
        return null;
    }

    /// <summary>Resolve a part by meronym name across any parent (physical steps carry the parent id as
    /// target_id, but this also covers a bare name lookup).</summary>
    public static GameObject GetByName(string meronymName, int zone)
    {
        if (string.IsNullOrWhiteSpace(meronymName))
            return null;
        string suffix = $"|{meronymName.Trim()}|{zone}".ToLowerInvariant();
        foreach (var kvp in _byKey)
        {
            if (kvp.Key.EndsWith(suffix, StringComparison.Ordinal) && kvp.Value != null)
                return kvp.Value;
        }
        return null;
    }

    public static void Clear()
    {
        foreach (var kvp in _byKey)
            if (kvp.Value != null) UnityEngine.Object.Destroy(kvp.Value);
        _byKey.Clear();
    }
}

/// <summary>One spawnable meronym resolved from the RAG.</summary>
public class MeronymPart
{
    public string id;
    public string name;
    public string meronymType;
    public string renderShape;
    public string color;
    public Vector3 localOffset;
    public Vector3 size;
}

public class SceneEntityParts
{
    public string parentId;
    public readonly List<MeronymPart> parts = new List<MeronymPart>();
}

/// <summary>Extracts spawnable meronyms out of <c>sceneEntities[].meronyms</c> using JsonUtility on the
/// isolated array (unknown fields such as thread/geometry are ignored).</summary>
public static class MeronymJsonParser
{
    public static List<SceneEntityParts> ParseSpawnableEntities(string ragText)
    {
        var result = new List<SceneEntityParts>();
        string arrayText = ExtractArray(ragText, "sceneEntities");
        if (string.IsNullOrEmpty(arrayText))
            return result;

        SceneEntityArrayDto parsed = null;
        try { parsed = JsonUtility.FromJson<SceneEntityArrayDto>("{\"items\":" + arrayText + "}"); }
        catch (Exception ex) { Debug.LogError($"[MeronymJsonParser] sceneEntities parse failed: {ex.Message}"); }

        if (parsed?.items == null)
            return result;

        foreach (SceneEntityDto ent in parsed.items)
        {
            if (ent?.meronyms == null || string.IsNullOrWhiteSpace(ent.id))
                continue;

            SceneEntityParts entity = null;
            foreach (MeronymDto m in ent.meronyms)
            {
                if (m == null || !m.spawn || string.IsNullOrWhiteSpace(m.name))
                    continue;

                if (entity == null)
                    entity = new SceneEntityParts { parentId = ent.id.Trim() };

                entity.parts.Add(new MeronymPart
                {
                    id = string.IsNullOrWhiteSpace(m.id) ? $"{ent.id}_{m.name}" : m.id,
                    name = m.name,
                    meronymType = m.meronym_type,
                    renderShape = string.IsNullOrWhiteSpace(m.renderShape) ? "cube" : m.renderShape,
                    color = string.IsNullOrWhiteSpace(m.color) ? "#95A5A6" : m.color,
                    localOffset = ToVec3(m.localOffset),
                    size = m.size != null ? ToVec3(m.size) : new Vector3(0.06f, 0.03f, 0.06f),
                });
            }

            if (entity != null && entity.parts.Count > 0)
                result.Add(entity);
        }

        return result;
    }

    static Vector3 ToVec3(RagVector3Json v) => v == null ? Vector3.zero : new Vector3(v.x, v.y, v.z);

    static string ExtractArray(string text, string key)
    {
        int keyIdx = text.IndexOf($"\"{key}\"", StringComparison.Ordinal);
        if (keyIdx < 0) return string.Empty;
        int arrayStart = text.IndexOf('[', keyIdx);
        if (arrayStart < 0) return string.Empty;
        int arrayEnd = FindMatchingBracket(text, arrayStart, '[', ']');
        if (arrayEnd < 0) return string.Empty;
        return text.Substring(arrayStart, arrayEnd - arrayStart + 1);
    }

    static int FindMatchingBracket(string text, int openIndex, char open, char close)
    {
        int depth = 0; bool inString = false;
        for (int i = openIndex; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"' && (i == 0 || text[i - 1] != '\\')) { inString = !inString; continue; }
            if (inString) continue;
            if (c == open) depth++;
            else if (c == close) { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    [Serializable]
    class MeronymDto
    {
        public string id;
        public string name;
        public string meronym_type;
        public string renderShape;
        public string color;
        public RagVector3Json localOffset;
        public RagVector3Json size;
        public bool interactive;
        public bool spawn;
    }

    [Serializable]
    class SceneEntityDto
    {
        public string id;
        public MeronymDto[] meronyms;
    }

    [Serializable]
    class SceneEntityArrayDto
    {
        public SceneEntityDto[] items;
    }
}
