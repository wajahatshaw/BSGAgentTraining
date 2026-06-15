using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tight interaction surface for the agent's active step target (equipment, menu tiles, buttons).
/// Wrong obstacles keep the default wide proximity hull.
/// </summary>
[DisallowMultipleComponent]
public class PhysicalTargetInteraction : MonoBehaviour
{
    public const float HostSurfacePad = 0.04f;
    public const float DefaultSurfacePad = 0.16f;

    static readonly Dictionary<string, GameObject> RootCache = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, float> MissUntil = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    const float MissRetrySeconds = 2f;

    [SerializeField] float hostSurfacePad = HostSurfacePad;
    [SerializeField] float defaultSurfacePad = DefaultSurfacePad;

    Renderer _visualRenderer;
    Collider _visualCollider;

    public static void ClearRegistry()
    {
        RootCache.Clear();
        MissUntil.Clear();
    }

    public static void RegisterTarget(GameObject root, string objectId, int zoneIndex)
    {
        if (root == null || string.IsNullOrWhiteSpace(objectId))
            return;

        string normalized = NormalizeObjectId(objectId);
        RootCache[CacheKey(normalized, zoneIndex)] = root;
        RootCache[CacheKey(normalized, -1)] = root;
        RootCache[CacheKey(root.name, zoneIndex)] = root;
        MissUntil.Remove(CacheKey(normalized, zoneIndex));
    }

    static string CacheKey(string targetObjectId, int zoneIndex)
        => zoneIndex >= 0 ? targetObjectId + "|" + zoneIndex : targetObjectId + "|*";

    static string NormalizeObjectId(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        string id = raw;
        if (id.StartsWith("Tool_", StringComparison.OrdinalIgnoreCase))
            id = id.Substring("Tool_".Length);

        int zoneIdx = id.IndexOf("_zone", StringComparison.OrdinalIgnoreCase);
        if (zoneIdx > 0)
            id = id.Substring(0, zoneIdx);

        return id;
    }

    public static PhysicalTargetInteraction EnsureOn(GameObject toolRoot)
    {
        if (toolRoot == null)
            return null;

        PhysicalTargetInteraction existing = toolRoot.GetComponent<PhysicalTargetInteraction>();
        if (existing != null)
            return existing;

        if (!PhysicalTargetPressVisual.IsPressableEquipment(toolRoot))
            return null;

        PhysicalTargetInteraction interaction = toolRoot.AddComponent<PhysicalTargetInteraction>();
        interaction.ResolveVisualCollider();
        return interaction;
    }

    public static PhysicalTargetInteraction EnsureOnAny(GameObject targetRoot)
    {
        if (targetRoot == null)
            return null;

        PhysicalTargetInteraction existing = targetRoot.GetComponent<PhysicalTargetInteraction>();
        if (existing != null)
            return existing;

        PhysicalTargetInteraction interaction = targetRoot.AddComponent<PhysicalTargetInteraction>();
        interaction.ResolveVisualCollider();

        DeclarativeObjectMetadata meta = targetRoot.GetComponent<DeclarativeObjectMetadata>();
        if (meta != null && !string.IsNullOrWhiteSpace(meta.objectId))
            RegisterTarget(targetRoot, meta.objectId, ExtractZoneFromName(targetRoot.name));

        return interaction;
    }

    static int ExtractZoneFromName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return 0;

        int idx = name.LastIndexOf("_zone", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return 0;

        string suffix = name.Substring(idx + "_zone".Length);
        return int.TryParse(suffix, out int zone) ? zone : 0;
    }

    public static bool TryFindForStepTarget(string targetObjectId, int zoneIndex, out PhysicalTargetInteraction interaction)
    {
        interaction = null;
        if (string.IsNullOrWhiteSpace(targetObjectId))
            return false;

        GameObject root = FindStepTargetRoot(targetObjectId, zoneIndex);
        if (root == null)
            return false;

        interaction = root.GetComponent<PhysicalTargetInteraction>();
        if (interaction == null)
        {
            if (PhysicalTargetPressVisual.IsPressableEquipment(root))
            {
                DeclarativeObjectMetadata meta = root.GetComponent<DeclarativeObjectMetadata>();
                string displayName = meta != null ? meta.displayName : root.name;
                string state = meta != null ? meta.currentState : "unpressed_0";
                PhysicalTargetPressVisual.EnsureOn(root, displayName, state);
            }

            interaction = EnsureOnAny(root);
        }

        return interaction != null;
    }

    public static GameObject FindStepTargetRoot(string targetObjectId, int zoneIndex)
    {
        if (string.IsNullOrWhiteSpace(targetObjectId))
            return null;

        string normalized = NormalizeObjectId(targetObjectId);
        string cacheKey = CacheKey(normalized, zoneIndex);

        if (RootCache.TryGetValue(cacheKey, out GameObject cached) && cached != null)
            return cached;

        if (MissUntil.TryGetValue(cacheKey, out float retryAt) && Time.time < retryAt)
            return null;

        GameObject root = ResolveStepTargetRootFast(normalized, targetObjectId, zoneIndex);
        if (root != null)
        {
            RegisterTarget(root, normalized, zoneIndex);
            return root;
        }

        MissUntil[cacheKey] = Time.time + MissRetrySeconds;
        return null;
    }

    static GameObject ResolveStepTargetRootFast(string normalized, string rawTargetId, int zoneIndex)
    {
        SceneGenerator scene = SceneGenerator.Instance;
        if (scene != null)
        {
            if (zoneIndex >= 0)
            {
                GameObject zoneTool = scene.GetTool($"{normalized}_zone{zoneIndex}");
                if (zoneTool != null)
                    return zoneTool;

                zoneTool = scene.GetTool($"{rawTargetId}_zone{zoneIndex}");
                if (zoneTool != null)
                    return zoneTool;
            }

            GameObject tool = scene.GetTool(normalized);
            if (tool != null)
                return tool;

            tool = scene.GetTool(rawTargetId);
            if (tool != null)
                return tool;
        }

        if (RagMenuController.IsMenuOptionId(normalized) || RagMenuController.IsMenuOptionId(rawTargetId))
        {
            string runtimeName = RagMenuController.GetRuntimeOptionObjectName(normalized, zoneIndex);
            GameObject option = GameObject.Find(runtimeName);
            if (option != null)
                return option;
        }

        if (zoneIndex >= 0)
        {
            if (scene != null)
            {
                GameObject cogTool = scene.GetTool($"cognitive_{normalized}_zone{zoneIndex}");
                if (cogTool != null)
                    return cogTool;
            }

            GameObject cognitive = GameObject.Find($"cognitive_{normalized}_zone{zoneIndex}");
            if (cognitive != null)
                return cognitive;
        }

        return null;
    }

    void Awake()
    {
        ResolveVisualCollider();
        DeclarativeObjectMetadata meta = GetComponent<DeclarativeObjectMetadata>();
        if (meta != null && !string.IsNullOrWhiteSpace(meta.objectId))
            RegisterTarget(gameObject, meta.objectId, ExtractZoneFromName(gameObject.name));
    }

    public void ResolveVisualCollider()
    {
        if (_visualRenderer == null)
            _visualRenderer = GetComponent<Renderer>();
        if (_visualRenderer == null)
            _visualRenderer = GetComponentInChildren<Renderer>(true);

        _visualCollider = GetComponent<Collider>();
        if (_visualCollider != null && _visualCollider.isTrigger)
            _visualCollider = null;

        if (_visualCollider == null && _visualRenderer != null)
        {
            _visualCollider = gameObject.AddComponent<BoxCollider>();
            FitColliderToRenderer();
        }
    }

    void FitColliderToRenderer()
    {
        if (_visualCollider == null || _visualRenderer == null)
            return;

        Bounds b = _visualRenderer.bounds;
        Transform t = transform;
        Vector3 localCenter = t.InverseTransformPoint(b.center);
        Vector3 localSize = t.InverseTransformVector(b.extents) * 2f;
        if (_visualCollider is BoxCollider box)
        {
            box.center = localCenter;
            box.size = new Vector3(
                Mathf.Max(0.08f, Mathf.Abs(localSize.x)),
                Mathf.Max(0.08f, Mathf.Abs(localSize.y)),
                Mathf.Max(0.08f, Mathf.Abs(localSize.z)));
        }
    }

    public Vector3 GetInteractionSurfacePoint()
    {
        ResolveVisualCollider();
        if (_visualRenderer != null)
            return _visualRenderer.bounds.center;

        return transform.position + Vector3.up * 0.25f;
    }

    public float GetSurfaceDistance(Vector3 agentPosition)
    {
        ResolveVisualCollider();
        if (_visualCollider != null)
        {
            Vector3 closest = _visualCollider.ClosestPoint(agentPosition);
            Vector3 a = agentPosition;
            a.y = closest.y;
            return Vector3.Distance(a, closest);
        }

        Vector3 c = GetInteractionSurfacePoint();
        Vector3 flat = agentPosition - c;
        flat.y = 0f;
        return flat.magnitude;
    }

    public float GetArrivalDistance(float agentRadius, bool hostPlayer)
    {
        float pad = hostPlayer ? hostSurfacePad : defaultSurfacePad;
        return Mathf.Max(0.14f, agentRadius + pad);
    }

    public Vector3 GetApproachStandPoint(Vector3 fromAgent, float agentRadius, bool hostPlayer)
    {
        ResolveVisualCollider();
        float pad = hostPlayer ? hostSurfacePad : defaultSurfacePad;
        float standOff = Mathf.Max(0.06f, agentRadius + pad);

        if (_visualCollider != null)
        {
            Vector3 closest = _visualCollider.ClosestPoint(fromAgent);
            Vector3 outDir = fromAgent - closest;
            outDir.y = 0f;

            if (outDir.sqrMagnitude < 0.01f)
                outDir = fromAgent - transform.position;
            outDir.y = 0f;

            if (outDir.sqrMagnitude < 0.01f)
                outDir = transform.forward;
            outDir.Normalize();

            Vector3 stand = closest + outDir * standOff;
            stand.y = fromAgent.y;
            return stand;
        }

        Vector3 surface = GetInteractionSurfacePoint();
        Vector3 dir = fromAgent - surface;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f)
            dir = transform.forward;
        dir.Normalize();
        Vector3 fallback = surface + dir * standOff;
        fallback.y = fromAgent.y;
        return fallback;
    }
}
