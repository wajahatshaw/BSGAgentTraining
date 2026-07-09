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

    // restsOn-driven office workstation layout (parsed from sceneEntities + sceneLayout.workstation).
    WorkstationLayout _layout;
    readonly HashSet<string> _laidOutParents = new HashSet<string>();

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
            _layout = MeronymJsonParser.ParseLayout(ragText);
            WorkstationLayoutBaker.BakeWorldPositions(_layout);
            _laidOutParents.Clear();
            _resizedParents.Clear();
            MeronymPartRegistry.Clear();
            GameObject labelHolder = GameObject.Find("~MeronymLabels");
            if (labelHolder != null)
                Destroy(labelHolder);
        }

        // Stack the physical entities into an office (desk on floor, computer/keyboard/mouse on the desk,
        // database/software panels on the monitor) per the RAG restsOn / sceneLayout.workstation data.
        // Runs before the meronym-part loop so keys/buttons are placed on already-relocated parents.
        ApplyWorkstationLayout();

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

        bool workstation = UsesWorkstationSizing(parentId);
        if (!workstation)
        {
            // Fallback (non-workstation scenes): shrink the parent box (once) so its TOP sits at a reachable
            // height, then place the meronym on that top surface — a natural standing-reach press.
            ShrinkParentToReachable(parent, parentId);
        }

        // Track the parent each tick so parts ride along after the layout pass repositions/rescales it.
        Vector3 s = parent.transform.lossyScale;
        Vector3 basePos = EnvironmentSolidCollider.TryGetVisibleBounds(parent.transform, out Bounds b)
            ? new Vector3(b.center.x, b.max.y, b.center.z)
            : parent.transform.position;
        // Workstation props are sized to real-ish proportions (a flat keyboard, a small mouse). Scaling the
        // part's Y / vertical offset by that flat parent would crush the key to an unpressable sliver, so keep
        // height and the vertical offset in raw metres; only the XZ footprint/offset tracks the parent so keys
        // stay on the board (and still follow a multiplayer cluster rescale on the legacy path).
        Vector3 off = part.localOffset;
        Vector3 worldOffset = workstation
            ? new Vector3(off.x * s.x, off.y, off.z * s.z)
            : Vector3.Scale(off, s);
        go.transform.position = basePos + worldOffset;
        go.transform.localScale = ScaledSize(part, s, workstation);
        TrackLabel(go);
    }

    // Target world height (metres) for the top of a press-target box, so meronyms on top are at a natural
    // standing reach for the humanoid agent.
    const float ReachableTopHeight = 1.05f;
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

    // ---------------------------------------------------------------------------------------------------
    //  Office workstation layout (restsOn-driven stacking)
    // ---------------------------------------------------------------------------------------------------

    const float DeskTopHeight    = WorkstationLayoutBaker.DeskTopHeight;
    const float DeskWidth        = 1.4f;    // X (worker's left↔right)
    const float DeskDepth        = 0.7f;    // Z (worker↔back)
    const float DeskTopThickness = 0.04f;
    const float DeskLegThickness = 0.06f;
    const float PanelXOffset     = 0.13f;   // monitor-face 2×2 grid half-spacing (X)
    const float PanelYOffset     = 0.10f;   // monitor-face 2×2 grid half-spacing (Y)

    static readonly Color DeskWoodColor = new Color(0.42f, 0.29f, 0.16f);
    static readonly Color DeskLegColor  = new Color(0.20f, 0.14f, 0.09f);

    bool UsesWorkstationSizing(string parentId)
    {
        // Runs in BOTH single-player (toolsParent scale 1) and the multiplayer zone-0 embed (RAG_WorldRoot is
        // pinned at scale 1 — minRootScale=1 — so tool lossyScale ≈ own localScale in both). The office layout
        // overrides the anchor's one-shot spread; the anchor never re-fits, so this is stable.
        return _layout != null
            && !string.IsNullOrEmpty(parentId)
            && _layout.byId.ContainsKey(parentId.Trim());
    }

    void ApplyWorkstationLayout()
    {
        if (_layout == null || _layout.IsEmpty)
            return;

        // Depth order (floor → desk → desk-top items → monitor panels) so a surface is positioned before
        // whatever rests on it; each id is applied exactly once (guard), robust to staggered spawn.
        foreach (LayoutEntity e in _layout.OrderedByDepth())
        {
            if (e == null || _laidOutParents.Contains(e.id))
                continue;

            GameObject tool = FindParentTool(e.id);
            if (tool == null)
                continue;   // its Tool GameObject hasn't spawned yet — retry next tick

            if (_layout.IsWorker(e))
            {
                HideWorkerBox(tool);
                _laidOutParents.Add(e.id);
                continue;
            }

            if (_layout.IsDesk(e))
            {
                BuildDeskRig(tool);
                _laidOutParents.Add(e.id);
                continue;
            }

            LayoutEntity parent = _layout.ResolveRestsOnParent(e);
            if (parent != null && !_laidOutParents.Contains(parent.id))
                continue;   // wait until the surface it rests on is laid out

            PlaceStackedEntity(tool, e, parent);
            _laidOutParents.Add(e.id);
        }
    }

    // Build the office desk as a real 4-leg table: the root cube becomes the (hidden) collider host and the
    // visible top + legs are child primitives. The desk's meronym data alone isn't enough to read as a table.
    void BuildDeskRig(GameObject deskTool)
    {
        LayoutEntity deskEntity = _layout?.Get(_layout.deskId);
        Vector3 baked = deskEntity != null ? deskEntity.worldPosition : deskTool.transform.position;

        deskTool.transform.rotation   = Quaternion.identity;
        deskTool.transform.localScale = Vector3.one;
        deskTool.transform.position   = new Vector3(baked.x, 0f, baked.z);

        // Hide the root mesh — it stays only as the solid collider host (see below).
        foreach (Renderer rr in deskTool.GetComponents<Renderer>())
            rr.enabled = false;

        if (deskTool.transform.Find("~DeskRig") == null)
        {
            var rig = new GameObject("~DeskRig");
            rig.transform.SetParent(deskTool.transform, false);

            float topY = DeskTopHeight - DeskTopThickness * 0.5f;   // centre so the tabletop TOP == DeskTopHeight
            AddDeskPiece(rig.transform, "DeskTop", new Vector3(0f, topY, 0f),
                new Vector3(DeskWidth, DeskTopThickness, DeskDepth), DeskWoodColor);

            float legH = DeskTopHeight - DeskTopThickness;
            float legY = legH * 0.5f;
            float lx = DeskWidth * 0.5f - DeskLegThickness;
            float lz = DeskDepth * 0.5f - DeskLegThickness;
            Vector3 legSize = new Vector3(DeskLegThickness, legH, DeskLegThickness);
            AddDeskPiece(rig.transform, "DeskLeg_FL", new Vector3(-lx, legY,  lz), legSize, DeskLegColor);
            AddDeskPiece(rig.transform, "DeskLeg_FR", new Vector3( lx, legY,  lz), legSize, DeskLegColor);
            AddDeskPiece(rig.transform, "DeskLeg_BL", new Vector3(-lx, legY, -lz), legSize, DeskLegColor);
            AddDeskPiece(rig.transform, "DeskLeg_BR", new Vector3( lx, legY, -lz), legSize, DeskLegColor);
        }

        // Resize the single solid collider to the full table footprint so the capsule is blocked at every
        // desk face from floor to tabletop (a thin slab would let it walk under) while items on top stay
        // reach-over-able. Root is at scale 1 so local == world.
        BoxCollider box = deskTool.GetComponent<BoxCollider>();
        if (box != null)
        {
            box.center    = new Vector3(0f, DeskTopHeight * 0.5f, 0f);
            box.size      = new Vector3(DeskWidth, DeskTopHeight, DeskDepth);
            box.isTrigger = false;
        }

        SnapDeskBottomToFloor(deskTool);

        Debug.Log($"[MeronymPartSpawner] Built office desk rig for '{deskTool.name}' at {deskTool.transform.position} (top {DeskTopHeight}m).");
    }

    /// <summary>Seat the desk collider/rig bottom on the floor so it is not left floating after env height scaling.</summary>
    static void SnapDeskBottomToFloor(GameObject deskTool)
    {
        if (deskTool == null)
            return;

        float bottomY = float.MaxValue;
        BoxCollider box = deskTool.GetComponent<BoxCollider>();
        if (box != null)
            bottomY = box.bounds.min.y;

        foreach (Renderer r in deskTool.GetComponentsInChildren<Renderer>(true))
        {
            if (r != null && r.enabled)
                bottomY = Mathf.Min(bottomY, r.bounds.min.y);
        }

        if (bottomY > 0.02f && bottomY < float.MaxValue)
            deskTool.transform.position -= new Vector3(0f, bottomY, 0f);
    }

    void AddDeskPiece(Transform parent, string name, Vector3 localPos, Vector3 size, Color color)
    {
        GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
        piece.name = name;
        piece.transform.SetParent(parent, false);
        piece.transform.localPosition = localPos;
        piece.transform.localScale    = size;
        Collider c = piece.GetComponent<Collider>();
        if (c != null)
            Destroy(c);   // legs/top are visual only — the root box is the sole navigation blocker
        SetColor(piece, color);
    }

    // Position + size one entity that rests on a surface. Screen-UI entities mount on the monitor face; every
    // other entity sits base-on-parent-top.
    void PlaceStackedEntity(GameObject tool, LayoutEntity e, LayoutEntity parent)
    {
        Vector3 size = WorkstationEntitySizing.SizeFor(e);

        if (WorkstationEntitySizing.IsScreenPanel(e))
        {
            PlaceScreenPanel(tool, e, parent, size);
            return;
        }

        tool.transform.rotation   = Quaternion.Euler(0f, e.rotationYDeg, 0f);
        tool.transform.localScale = size;
        tool.transform.position   = e.worldPosition;
        SetColor(tool, ColorFor(e));
        Debug.Log($"[MeronymPartSpawner] Placed '{e.name}' ({e.id}) on '{parent?.name}' at {tool.transform.position} (size {size}).");
    }

    // Mount a database/software panel flat on the monitor's worker-facing (+Z) front face, in a 2×2 grid.
    void PlaceScreenPanel(GameObject tool, LayoutEntity e, LayoutEntity computer, Vector3 size)
    {
        GameObject comp = computer != null ? FindParentTool(computer.id) : null;
        if (comp == null || !EnvironmentSolidCollider.TryGetVisibleBounds(comp.transform, out Bounds b))
            return;

        int idx = _layout.ScreenIndex(e);
        int col = idx % 2;   // 0 = worker-left, 1 = worker-right
        int row = idx / 2;   // 0 = top, 1 = bottom
        float cx = b.center.x + (col == 0 ? PanelXOffset : -PanelXOffset);
        float cy = b.center.y + (row == 0 ? PanelYOffset : -PanelYOffset);
        float cz = b.max.z + size.z * 0.5f + 0.006f;   // just proud of the monitor's front face

        tool.transform.rotation   = Quaternion.identity;
        tool.transform.localScale = size;
        tool.transform.position   = new Vector3(cx, cy, cz);

        Collider col2 = tool.GetComponent<Collider>();
        if (col2 != null)
            col2.enabled = false;   // purely visual — must not block navigation (reachable:false)
        SetColor(tool, ColorFor(e));
        Debug.Log($"[MeronymPartSpawner] Mounted screen '{e.name}' ({e.id}) on the monitor face at {tool.transform.position}.");
    }

    float ParentVisibleTop(LayoutEntity parent)
    {
        if (parent == null)
            return WorkstationLayoutBaker.DeskTopHeight;
        GameObject p = FindParentTool(parent.id);
        if (p != null && EnvironmentSolidCollider.TryGetVisibleBounds(p.transform, out Bounds b))
            return b.max.y;
        return WorkstationLayoutBaker.DeskTopHeight;
    }

    static Color ColorFor(LayoutEntity e)
    {
        string gt = (e?.geometryType ?? string.Empty).ToLowerInvariant();
        if (gt.Contains("data_source"))           return new Color(0.16f, 0.52f, 0.86f);   // databases — blue
        if (gt.Contains("software_application"))  return new Color(0.90f, 0.49f, 0.13f);   // software — orange
        if (gt.Contains("electromechanical"))     return new Color(0.13f, 0.14f, 0.16f);   // monitor — near-black
        if (gt.Contains("input_device"))          return new Color(0.17f, 0.18f, 0.20f);   // keyboard/mouse — dark grey
        return new Color(0.60f, 0.60f, 0.62f);
    }

    static void SetColor(GameObject go, Color color)
    {
        Renderer r = go.GetComponent<Renderer>();
        if (r != null && r.material != null)
            r.material.color = color;   // instances the material — does not tint the shared primitive material
    }

    // The worker (Marketing Manager) is represented by the acting agent capsule (Agent_P1); its duplicate
    // Tool box is hidden so it neither shows nor blocks navigation. Never touches Agent_P1.
    void HideWorkerBox(GameObject tool)
    {
        foreach (Renderer r in tool.GetComponentsInChildren<Renderer>(true))
            r.enabled = false;
        foreach (Collider c in tool.GetComponentsInChildren<Collider>(true))
            c.enabled = false;
        Debug.Log($"[MeronymPartSpawner] Hid duplicate worker box '{tool.name}' — the agent capsule represents the Marketing Manager.");
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

    // Pressable-minimum footprint (world metres) for meronym parts on real-ish (flat) workstation props.
    static readonly Vector3 MinPartSize = new Vector3(0.10f, 0.06f, 0.10f);

    // worldPosition = parent.worldPosition + localOffset, generalized by the parent's lossyScale so the
    // part stays on the object when the multiplayer anchor scales the cluster (identical to the literal
    // contract when the parent is at scale 1). A cylinder's default mesh is 2 units tall → halve Y.
    // When <paramref name="workstation"/> is set, the part's height stays in raw metres and every axis is
    // floored to a pressable minimum so a flat keyboard/mouse doesn't collapse the key into a sliver.
    static Vector3 ScaledSize(MeronymPart part, Vector3 parentScale, bool workstation)
    {
        bool cylinder = string.Equals(part.renderShape, "cylinder", StringComparison.OrdinalIgnoreCase);
        if (workstation)
        {
            float x = Mathf.Max(MinPartSize.x, part.size.x * parentScale.x);
            float z = Mathf.Max(MinPartSize.z, part.size.z * parentScale.z);
            float y = Mathf.Max(MinPartSize.y, cylinder ? part.size.y * 0.5f : part.size.y);
            return new Vector3(x, y, z);
        }
        Vector3 world = Vector3.Scale(part.size, parentScale);
        if (cylinder)
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

/// <summary>One physical sceneEntity's placement metadata for the restsOn-driven office layout.</summary>
public class LayoutEntity
{
    public string id;
    public string name;
    public string type;
    public string restsOn;
    public string placementRole;
    public string geometryType;
    public string geometryShape;
    public bool reachable;
    public float rotationYDeg;
    public Vector3 worldPosition;
    public bool hasWorkstationOffset;
    public Vector3 workstationOffset;   // worker-frame metres from sceneLayout.workstation
}

/// <summary>The physical scene resolved for stacking: entities + the workstation offset map, with the
/// desk (surface-on-floor) and worker (placementRole worker) called out and restsOn parent resolution.</summary>
public class WorkstationLayout
{
    public readonly List<LayoutEntity> entities = new List<LayoutEntity>();
    public readonly Dictionary<string, LayoutEntity> byId =
        new Dictionary<string, LayoutEntity>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, LayoutEntity> _byName =
        new Dictionary<string, LayoutEntity>(StringComparer.OrdinalIgnoreCase);

    public string deskId;
    public string workerId;

    public bool IsEmpty => entities.Count == 0;

    public void Add(LayoutEntity e)
    {
        if (e == null) return;
        entities.Add(e);
        if (!string.IsNullOrWhiteSpace(e.id)) byId[e.id.Trim()] = e;
        if (!string.IsNullOrWhiteSpace(e.name)) _byName[e.name.Trim()] = e;
    }

    public void Resolve()
    {
        foreach (LayoutEntity e in entities)
        {
            if (string.Equals(e.placementRole, "worker", StringComparison.OrdinalIgnoreCase))
                workerId = e.id;
            if (deskId == null && string.Equals(e.placementRole, "surface", StringComparison.OrdinalIgnoreCase)
                && IsFloor(e.restsOn))
                deskId = e.id;
        }
        if (deskId == null)   // fallback: first floor entity that isn't the worker
        {
            foreach (LayoutEntity e in entities)
                if (IsFloor(e.restsOn) && !string.Equals(e.id, workerId, StringComparison.OrdinalIgnoreCase))
                { deskId = e.id; break; }
        }
    }

    public LayoutEntity Get(string id) =>
        !string.IsNullOrWhiteSpace(id) && byId.TryGetValue(id.Trim(), out LayoutEntity e) ? e : null;

    public LayoutEntity GetByName(string name) =>
        !string.IsNullOrWhiteSpace(name) && _byName.TryGetValue(name.Trim(), out LayoutEntity e) ? e : null;

    public bool IsWorker(LayoutEntity e) =>
        e != null && string.Equals(e.id, workerId, StringComparison.OrdinalIgnoreCase);

    public bool IsDesk(LayoutEntity e) =>
        e != null && string.Equals(e.id, deskId, StringComparison.OrdinalIgnoreCase);

    public LayoutEntity ResolveRestsOnParent(LayoutEntity e) =>
        (e == null || IsFloor(e.restsOn)) ? null : GetByName(e.restsOn);

    public int Depth(LayoutEntity e)
    {
        int d = 0;
        var seen = new HashSet<string>();
        LayoutEntity cur = e;
        while (cur != null && !IsFloor(cur.restsOn) && seen.Add(cur.id))
        {
            LayoutEntity parent = GetByName(cur.restsOn);
            if (parent == null) break;
            d++;
            cur = parent;
        }
        return d;
    }

    public List<LayoutEntity> OrderedByDepth()
    {
        var list = new List<LayoutEntity>(entities);
        list.Sort((a, b) => Depth(a).CompareTo(Depth(b)));
        return list;
    }

    /// <summary>Deterministic 0-based rank of a screen-UI entity, left→right by its workstation X.</summary>
    public int ScreenIndex(LayoutEntity e)
    {
        var screens = new List<LayoutEntity>();
        foreach (LayoutEntity le in entities)
        {
            string gt = (le.geometryType ?? string.Empty).ToLowerInvariant();
            if (gt.Contains("data_source") || gt.Contains("software_application"))
                screens.Add(le);
        }
        screens.Sort((a, b) => a.workstationOffset.x.CompareTo(b.workstationOffset.x));
        int idx = screens.IndexOf(e);
        return idx < 0 ? 0 : idx;
    }

    public static bool IsFloor(string restsOn)
    {
        if (string.IsNullOrWhiteSpace(restsOn)) return true;
        string r = restsOn.Trim();
        return string.Equals(r, "floor", StringComparison.OrdinalIgnoreCase)
            || string.Equals(r, "ground", StringComparison.OrdinalIgnoreCase);
    }
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

    /// <summary>Parse every sceneEntity's placement metadata (restsOn / geometry / placementRole /
    /// rotationYDeg / worldPosition) plus the sceneLayout.workstation offset map, for the restsOn-driven
    /// office layout. Uses the same isolated-array + JsonUtility technique as ParseSpawnableEntities.</summary>
    public static WorkstationLayout ParseLayout(string ragText)
    {
        var layout = new WorkstationLayout();
        string arrayText = ExtractArray(ragText, "sceneEntities");
        if (string.IsNullOrEmpty(arrayText))
            return layout;

        SceneEntityArrayDto parsed = null;
        try { parsed = JsonUtility.FromJson<SceneEntityArrayDto>("{\"items\":" + arrayText + "}"); }
        catch (Exception ex) { Debug.LogError($"[MeronymJsonParser] sceneEntities layout parse failed: {ex.Message}"); }
        if (parsed?.items == null)
            return layout;

        // sceneLayout.workstation is a name-keyed map, which JsonUtility can't deserialize directly, so pull
        // each "<entity name>": {x,y,z} out of the isolated object text by brace matching.
        string sceneLayoutText = ExtractObject(ragText, "sceneLayout");
        string workstationText = string.IsNullOrEmpty(sceneLayoutText)
            ? string.Empty
            : ExtractObject(sceneLayoutText, "workstation");

        Vector3 spawnOrigin = ResolveSpawnOrigin(sceneLayoutText);

        foreach (SceneEntityDto e in parsed.items)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.id))
                continue;

            var le = new LayoutEntity
            {
                id            = e.id.Trim(),
                name          = e.name,
                type          = e.type,
                restsOn       = e.restsOn,
                placementRole = e.placementRole,
                reachable     = e.reachable,
                rotationYDeg  = e.rotationYDeg,
                geometryType  = e.geometry != null ? e.geometry.type : null,
                geometryShape = e.geometry != null ? e.geometry.shape : null,
                worldPosition = e.worldPosition != null
                    ? new Vector3(e.worldPosition.x, e.worldPosition.y, e.worldPosition.z)
                    : Vector3.zero,
            };

            if (!string.IsNullOrEmpty(workstationText) && !string.IsNullOrWhiteSpace(le.name)
                && TryGetNamedVec3(workstationText, le.name, out Vector3 ofs))
            {
                le.hasWorkstationOffset = true;
                le.workstationOffset = ofs;
            }

            layout.Add(le);
        }

        layout.Resolve();
        if (!UsesAuthoredWorldPositions(sceneLayoutText))
            WorkstationLayoutBaker.BakeWorldPositions(layout, spawnOrigin);
        return layout;
    }

    /// <summary>When true, <c>sceneEntities[].worldPosition</c> in the RAG is used as-is (no restsOn bake).</summary>
    public static bool UsesAuthoredWorldPositions(string sceneLayoutText)
    {
        if (string.IsNullOrEmpty(sceneLayoutText))
            return false;
        int idx = sceneLayoutText.IndexOf("\"authoritativeWorldPositions\"", StringComparison.Ordinal);
        if (idx < 0)
            return false;
        int trueIdx = sceneLayoutText.IndexOf("true", idx, StringComparison.Ordinal);
        return trueIdx >= 0 && trueIdx < idx + 80;
    }

    /// <summary>Worker-frame origin on the physical band (zone-local). Cognitive band uses +Z; physical band uses negative Z.</summary>
    static Vector3 ResolveSpawnOrigin(string sceneLayoutText)
    {
        if (string.IsNullOrEmpty(sceneLayoutText))
            return WorkstationLayoutBaker.DefaultPhysicalBandOrigin;

        string spawnText = ExtractObject(sceneLayoutText, "spawnOrigin");
        if (string.IsNullOrEmpty(spawnText))
            return WorkstationLayoutBaker.DefaultPhysicalBandOrigin;

        RagVector3Json parsed = null;
        try { parsed = JsonUtility.FromJson<RagVector3Json>(spawnText); } catch { }
        return parsed != null
            ? new Vector3(parsed.x, parsed.y, parsed.z)
            : WorkstationLayoutBaker.DefaultPhysicalBandOrigin;
    }

    static string ExtractObject(string text, string key)
    {
        int keyIdx = text.IndexOf($"\"{key}\"", StringComparison.Ordinal);
        if (keyIdx < 0) return string.Empty;
        int objStart = text.IndexOf('{', keyIdx);
        if (objStart < 0) return string.Empty;
        int objEnd = FindMatchingBracket(text, objStart, '{', '}');
        if (objEnd < 0) return string.Empty;
        return text.Substring(objStart, objEnd - objStart + 1);
    }

    static bool TryGetNamedVec3(string objText, string name, out Vector3 v)
    {
        v = Vector3.zero;
        int idx = objText.IndexOf($"\"{name}\"", StringComparison.Ordinal);
        if (idx < 0) return false;
        int braceStart = objText.IndexOf('{', idx);
        if (braceStart < 0) return false;
        int braceEnd = FindMatchingBracket(objText, braceStart, '{', '}');
        if (braceEnd < 0) return false;
        string sub = objText.Substring(braceStart, braceEnd - braceStart + 1);
        RagVector3Json parsed = null;
        try { parsed = JsonUtility.FromJson<RagVector3Json>(sub); } catch { return false; }
        if (parsed == null) return false;
        v = new Vector3(parsed.x, parsed.y, parsed.z);
        return true;
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
        public string name;
        public string type;
        public GeometryLiteDto geometry;
        public RagVector3Json worldPosition;
        public string restsOn;
        public bool reachable;
        public float rotationYDeg;
        public string placementRole;
        public MeronymDto[] meronyms;
        // NOTE: deliberately NO `thread`/`semanticThread` (string[] in the RAG) — declaring an array-vs-string
        // mismatch here makes JsonUtility silently fail to populate the whole element.
    }

    [Serializable]
    class GeometryLiteDto
    {
        public string type;
        public string shape;
    }

    [Serializable]
    class SceneEntityArrayDto
    {
        public SceneEntityDto[] items;
    }
}
