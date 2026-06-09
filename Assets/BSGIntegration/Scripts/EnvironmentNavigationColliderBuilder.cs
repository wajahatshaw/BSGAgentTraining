using UnityEngine;

/// <summary>
/// Builds solid navigation hulls for multiplayer physical-environment RAG props.
/// Visual meshes stay short/table-like; the nav box is tall enough to block the agent capsule.
/// </summary>
public static class EnvironmentNavigationColliderBuilder
{
    public const float MinAgentBlockingHeight = 1.55f;
    public const float MinAgentBlockingXZ = 0.92f;
    public const float NavBoundsPad = 1.12f;

    static readonly string[] IgnoredRendererNames =
    {
        "EnvToolBasePad",
        "MenuOptionLabel",
        "ObjectNameLabel",
        "ToolObjectNameLabel",
        "DynamicObjectNameLabel",
    };

    public static void EnsureOnTool(GameObject toolRoot, bool forceRebuild = false)
    {
        if (toolRoot == null)
            return;

        if (toolRoot.GetComponentInParent<CognitiveStationInteractable>() != null
            || toolRoot.GetComponent<CognitiveStationInteractable>() != null)
        {
            return;
        }

        const string childName = "EnvironmentNavObstacle";
        Transform existing = toolRoot.transform.Find(childName);
        if (existing != null)
        {
            if (!forceRebuild)
            {
                ConfigureExisting(existing.gameObject);
                return;
            }

            Object.Destroy(existing.gameObject);
        }

        if (!TryComputeLocalNavBox(toolRoot, out Vector3 localCenter, out Vector3 localSize))
            return;

        GameObject go = new GameObject(childName);
        go.transform.SetParent(toolRoot.transform, false);
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.center = localCenter;
        box.size = localSize;
        box.isTrigger = false;

        EnvironmentSolidCollider solid = go.AddComponent<EnvironmentSolidCollider>();
        solid.ConfigureSolidCollider();
        ScenePhysicsLayers.ApplyEnvironmentLayer(go);
    }

    public static void RebuildAllEnvironmentToolsInScene()
    {
        foreach (DeclarativeObjectMetadata meta in Object.FindObjectsOfType<DeclarativeObjectMetadata>(true))
        {
            if (meta == null)
                continue;
            EnsureOnTool(meta.gameObject, forceRebuild: true);
        }

        foreach (ToolComponent tool in Object.FindObjectsOfType<ToolComponent>(true))
        {
            if (tool == null)
                continue;
            if (tool.GetComponentInParent<CognitiveStationInteractable>() != null)
                continue;
            if (tool.GetComponent<CognitiveStationInteractable>() != null)
                continue;
            EnsureOnTool(tool.gameObject, forceRebuild: true);
        }
    }

    static void ConfigureExisting(GameObject navGo)
    {
        if (navGo == null)
            return;

        BoxCollider box = navGo.GetComponent<BoxCollider>();
        if (box == null)
            box = navGo.AddComponent<BoxCollider>();

        Transform root = navGo.transform.parent;
        if (root != null && TryComputeLocalNavBox(root.gameObject, out Vector3 localCenter, out Vector3 localSize))
        {
            box.center = localCenter;
            box.size = localSize;
        }

        box.isTrigger = false;
        EnvironmentSolidCollider solid = navGo.GetComponent<EnvironmentSolidCollider>();
        if (solid == null)
            solid = navGo.AddComponent<EnvironmentSolidCollider>();
        solid.ConfigureSolidCollider();
        ScenePhysicsLayers.ApplyEnvironmentLayer(navGo);
    }

    static bool TryComputeLocalNavBox(GameObject toolRoot, out Vector3 localCenter, out Vector3 localSize)
    {
        localCenter = Vector3.zero;
        localSize = Vector3.one;

        if (!TryCollectVisualBounds(toolRoot, out Bounds worldBounds))
            return false;

        Transform t = toolRoot.transform;
        Vector3 localMin = t.InverseTransformPoint(worldBounds.min);
        Vector3 localMax = t.InverseTransformPoint(worldBounds.max);

        float xSize = Mathf.Max(MinAgentBlockingXZ, Mathf.Abs(localMax.x - localMin.x) * NavBoundsPad);
        float zSize = Mathf.Max(MinAgentBlockingXZ, Mathf.Abs(localMax.z - localMin.z) * NavBoundsPad);
        float ySize = Mathf.Max(MinAgentBlockingHeight, Mathf.Abs(localMax.y - localMin.y) * NavBoundsPad);

        float footY = Mathf.Min(localMin.y, localMax.y);
        localCenter = new Vector3(
            (localMin.x + localMax.x) * 0.5f,
            footY + ySize * 0.5f,
            (localMin.z + localMax.z) * 0.5f);
        localSize = new Vector3(xSize, ySize, zSize);
        return true;
    }

    static bool TryCollectVisualBounds(GameObject toolRoot, out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;

        Renderer[] rends = toolRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++)
        {
            Renderer r = rends[i];
            if (r == null || ShouldIgnoreRenderer(r))
                continue;

            if (!hasBounds)
            {
                bounds = r.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        return hasBounds;
    }

    static bool ShouldIgnoreRenderer(Renderer renderer)
    {
        if (renderer == null)
            return true;

        Transform tr = renderer.transform;
        if (tr.name == "EnvironmentNavObstacle" || tr.name == "CognitiveNavObstacle")
            return true;

        for (int i = 0; i < IgnoredRendererNames.Length; i++)
        {
            if (tr.name == IgnoredRendererNames[i])
                return true;
        }

        return false;
    }
}
