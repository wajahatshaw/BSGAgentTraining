using UnityEngine;

/// <summary>
/// Derives authoritative <c>worldPosition</c> values for physical <c>sceneEntities</c> from
/// <c>sceneLayout.workstation</c> offsets and each entity's <c>restsOn</c> chain. Used at RAG parse
/// time (spawn) and by <see cref="MeronymPartSpawner"/> so placement is data-driven, not hardcoded.
/// </summary>
public static class WorkstationLayoutBaker
{
    public const float DeskTopHeight = 0.95f;   // raised to standing hand-height so the agent presses items on top without a deep downward reach (IK tuned ~1.05m)
    public const float FloorStandingCenterY = 0.5f;

    /// <summary>Default worker origin on zone 0 physical band — south of cognitive (+Z ~9), near P1 spawn (~-18).</summary>
    public static readonly Vector3 DefaultPhysicalBandOrigin = new Vector3(0f, 0f, -14f);

    /// <summary>Fill <see cref="LayoutEntity.worldPosition"/> for every entity that has a workstation offset.</summary>
    public static void BakeWorldPositions(WorkstationLayout layout, Vector3 spawnOrigin = default)
    {
        if (layout == null || layout.IsEmpty)
            return;

        foreach (LayoutEntity e in layout.OrderedByDepth())
        {
            if (e == null || !e.hasWorkstationOffset)
                continue;
            e.worldPosition = ComputeEntityCenter(layout, e, spawnOrigin);
        }
    }

    /// <summary>World-space centre of a spawnable meronym on its parent (metres).</summary>
    public static Vector3 MeronymWorldPoint(Vector3 parentCenter, Vector3 parentSize, MeronymPart part, bool workstation)
    {
        if (part == null)
            return parentCenter;

        float topY = parentCenter.y + parentSize.y * 0.5f;
        Vector3 basePos = new Vector3(parentCenter.x, topY, parentCenter.z);
        Vector3 off = part.localOffset;
        if (workstation)
            return basePos + new Vector3(off.x, off.y, off.z);

        return basePos + Vector3.Scale(off, parentSize);
    }

    /// <summary>Resolve a Klein-frame / step contact coordinate from parent entity centre + meronym offset.</summary>
    public static Vector3 MeronymContactFromLayout(LayoutEntity parent, MeronymPart part)
    {
        if (parent == null || part == null)
            return Vector3.zero;
        Vector3 size = WorkstationEntitySizing.SizeFor(parent);
        return MeronymWorldPoint(parent.worldPosition, size, part, workstation: true);
    }

    static Vector3 ComputeEntityCenter(WorkstationLayout layout, LayoutEntity e, Vector3 origin)
    {
        Vector3 ws = origin + e.workstationOffset;
        Vector3 size = WorkstationEntitySizing.SizeFor(e);

        if (WorkstationLayout.IsFloor(e.restsOn))
        {
            if (layout.IsDesk(e))
                return new Vector3(ws.x, 0f, ws.z);
            if (layout.IsWorker(e))
                return new Vector3(ws.x, FloorStandingCenterY, ws.z);
            return new Vector3(ws.x, ws.y + size.y * 0.5f, ws.z);
        }

        if (WorkstationEntitySizing.IsScreenPanel(e))
            return ws;

        LayoutEntity parent = layout.ResolveRestsOnParent(e);
        float surfaceY = SurfaceTopY(layout, parent);
        return new Vector3(ws.x, surfaceY + size.y * 0.5f, ws.z);
    }

    static float SurfaceTopY(WorkstationLayout layout, LayoutEntity parent)
    {
        if (parent == null)
            return DeskTopHeight;
        if (layout.IsDesk(parent))
            return DeskTopHeight;

        Vector3 parentSize = WorkstationEntitySizing.SizeFor(parent);
        return parent.worldPosition.y + parentSize.y * 0.5f;
    }
}

/// <summary>Real-ish box sizes for workstation props (metres), keyed off RAG geometry type/shape.</summary>
public static class WorkstationEntitySizing
{
    public static bool IsScreenPanel(LayoutEntity e)
    {
        string gt = (e?.geometryType ?? string.Empty).ToLowerInvariant();
        return gt.Contains("data_source") || gt.Contains("software_application");
    }

    public static Vector3 SizeFor(LayoutEntity e)
    {
        if (IsScreenPanel(e))
            return new Vector3(0.24f, 0.17f, 0.012f);

        string gt = (e.geometryType ?? string.Empty).ToLowerInvariant();
        string gs = (e.geometryShape ?? string.Empty).ToLowerInvariant();

        if (gt.Contains("electromechanical") || gs.Contains("tower") || gs.Contains("monitor"))
            return new Vector3(0.55f, 0.45f, 0.10f);
        if (gt.Contains("input_device"))
            return gs.Contains("contour")
                ? new Vector3(0.5f, 0.06f, 0.35f)     // mouse — gameplay scale so its buttons are big enough to press
                : new Vector3(0.62f, 0.05f, 0.20f);   // keyboard
        if (gt.Contains("support_furniture") || gs.Contains("rectangular"))
            return new Vector3(1.4f, WorkstationLayoutBaker.DeskTopHeight, 0.7f);

        return new Vector3(0.3f, 0.3f, 0.3f);
    }
}
