using System;
using UnityEngine;

/// <summary>
/// XZ play area per quadrant — matches <see cref="SceneGenerator"/> / ReplicaSceneSetup layout.
/// Agents must stay inside their own zone; movement is hard-clamped here because
/// <see cref="RagSequenceAgentMover"/> bypasses <see cref="BSGMLAgent.EnforcePlayAreaBounds"/>.
/// </summary>
public static class ZonePlayAreaBounds
{
    public const float ZoneHalf = 20f;
    public const float GridShiftZ = 18f;
    public const float DefaultInset = 17.5f;

    static readonly Vector3[] ZoneCenters =
    {
        new Vector3(-ZoneHalf, 0f, -ZoneHalf + GridShiftZ),
        new Vector3( ZoneHalf, 0f, -ZoneHalf + GridShiftZ),
        new Vector3(-ZoneHalf, 0f,  ZoneHalf + GridShiftZ),
        new Vector3( ZoneHalf, 0f,  ZoneHalf + GridShiftZ),
    };

    public static Vector3 GetZoneCenter(int zoneIndex)
    {
        return ZoneCenters[Mathf.Clamp(zoneIndex, 0, 3)];
    }

    public static void GetXZBounds(int zoneIndex, out float minX, out float maxX, out float minZ, out float maxZ, float inset = DefaultInset)
    {
        if (TryGetMultiplayerWorldBounds(zoneIndex, out minX, out maxX, out minZ, out maxZ))
            return;

        Vector3 c = GetZoneCenter(zoneIndex);
        float pad = Mathf.Max(0.5f, inset);
        minX = c.x - pad;
        maxX = c.x + pad;
        minZ = c.z - pad;
        maxZ = c.z + pad;
    }

    static bool TryGetMultiplayerWorldBounds(int zoneIndex, out float minX, out float maxX, out float minZ, out float maxZ)
    {
        minX = maxX = minZ = maxZ = 0f;
        if (zoneIndex != 0)
            return false;

        ZonePlayAreaWorldRect? area = BsgIntegrationSettings.MultiplayerZone0PlayAreaWorld;
        if (!area.HasValue || !area.Value.IsValid)
            return false;

        ZonePlayAreaWorldRect r = area.Value;
        minX = r.minX;
        maxX = r.maxX;
        minZ = r.minZ;
        maxZ = r.maxZ;
        return true;
    }

    public static Vector3 ClampPosition(int zoneIndex, Vector3 worldPos, float inset = DefaultInset)
    {
        GetXZBounds(zoneIndex, out float minX, out float maxX, out float minZ, out float maxZ, inset);
        worldPos.x = Mathf.Clamp(worldPos.x, minX, maxX);
        worldPos.z = Mathf.Clamp(worldPos.z, minZ, maxZ);
        return worldPos;
    }

    public static bool Contains(int zoneIndex, Vector3 worldPos, float inset = DefaultInset)
    {
        GetXZBounds(zoneIndex, out float minX, out float maxX, out float minZ, out float maxZ, inset);
        return worldPos.x >= minX && worldPos.x <= maxX && worldPos.z >= minZ && worldPos.z <= maxZ;
    }

    public static int ParseZoneIndexFromId(string objectId)
    {
        if (string.IsNullOrEmpty(objectId)) return -1;
        int idx = objectId.LastIndexOf("_zone", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return -1;
        string tail = objectId.Substring(idx + 5);
        return int.TryParse(tail, out int z) ? z : -1;
    }

    public static bool BelongsToZone(string objectOrStationId, int zoneIndex)
    {
        int parsed = ParseZoneIndexFromId(objectOrStationId);
        if (parsed >= 0)
            return parsed == zoneIndex;
        return true;
    }

    public static bool WorldPositionInZone(int zoneIndex, Vector3 worldPos, float inset = DefaultInset)
    {
        return Contains(zoneIndex, worldPos, inset);
    }
}
