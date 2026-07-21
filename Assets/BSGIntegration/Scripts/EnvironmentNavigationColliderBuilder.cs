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

        EnsureVisibleSurfaceSolid(toolRoot);
    }

    /// <summary>
    /// Tight solid hull fitted to the visible mesh — agents collide with what they see on screen,
    /// not only the inflated navigation box used for pathfinding clearance.
    /// </summary>
    public static void EnsureVisibleSurfaceSolid(GameObject toolRoot, bool forceRebuild = false)
    {
        if (toolRoot == null)
            return;

        const string childName = "EnvironmentVisibleSolid";
        Transform existing = toolRoot.transform.Find(childName);
        if (existing != null)
        {
            if (!forceRebuild)
            {
                ConfigureVisibleSolid(existing.gameObject);
                return;
            }

            Object.Destroy(existing.gameObject);
        }

        if (!TryComputeLocalVisibleBox(toolRoot, out Vector3 localCenter, out Vector3 localSize))
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

    static void ConfigureVisibleSolid(GameObject visGo)
    {
        if (visGo == null)
            return;

        BoxCollider box = visGo.GetComponent<BoxCollider>();
        if (box == null)
            box = visGo.AddComponent<BoxCollider>();

        Transform root = visGo.transform.parent;
        if (root != null && TryComputeLocalVisibleBox(root.gameObject, out Vector3 localCenter, out Vector3 localSize))
        {
            box.center = localCenter;
            box.size = localSize;
        }

        box.isTrigger = false;
        EnvironmentSolidCollider solid = visGo.GetComponent<EnvironmentSolidCollider>();
        if (solid == null)
            solid = visGo.AddComponent<EnvironmentSolidCollider>();
        solid.ConfigureSolidCollider();
        ScenePhysicsLayers.ApplyEnvironmentLayer(visGo);
    }

    /// <summary>
    /// Local-space center/size of a prop's VISIBLE mesh bounds (excludes nav-obstacle helpers and labels).
    /// Public so cognitive stations can fit their collision box to the actual visible body too, instead of an
    /// oversized constant box that leaves an invisible gap between the agent and what it sees.
    /// </summary>
    public static bool TryComputeLocalVisibleBox(GameObject toolRoot, out Vector3 localCenter, out Vector3 localSize)
    {
        localCenter = Vector3.zero;
        localSize = Vector3.one;

        if (!TryCollectVisualBounds(toolRoot, out Bounds worldBounds))
            return false;

        Transform t = toolRoot.transform;
        Vector3 localMin = t.InverseTransformPoint(worldBounds.min);
        Vector3 localMax = t.InverseTransformPoint(worldBounds.max);

        localSize = new Vector3(
            Mathf.Max(0.12f, Mathf.Abs(localMax.x - localMin.x)),
            Mathf.Max(0.12f, Mathf.Abs(localMax.y - localMin.y)),
            Mathf.Max(0.12f, Mathf.Abs(localMax.z - localMin.z)));
        localCenter = new Vector3(
            (localMin.x + localMax.x) * 0.5f,
            (localMin.y + localMax.y) * 0.5f,
            (localMin.z + localMax.z) * 0.5f);
        return true;
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
            EnsureVisibleSurfaceSolid(tool.gameObject, forceRebuild: true);
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

        float xSize = Mathf.Max(Mathf.Abs(localMax.x - localMin.x) * NavBoundsPad, Mathf.Abs(localMax.x - localMin.x) + 0.08f);
        float zSize = Mathf.Max(Mathf.Abs(localMax.z - localMin.z) * NavBoundsPad, Mathf.Abs(localMax.z - localMin.z) + 0.08f);
        float ySize = Mathf.Max(Mathf.Abs(localMax.y - localMin.y) * NavBoundsPad, Mathf.Abs(localMax.y - localMin.y) + 0.08f);
        // Nav hull only needs to block pathing — visible solid handles contact. Do not inflate XZ to 0.92m.
        xSize = Mathf.Min(xSize, MinAgentBlockingXZ);
        zSize = Mathf.Min(zSize, MinAgentBlockingXZ);
        ySize = Mathf.Max(ySize, MinAgentBlockingHeight * 0.55f);

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
