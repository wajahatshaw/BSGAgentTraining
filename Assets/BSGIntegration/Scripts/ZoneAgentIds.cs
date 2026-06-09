using System;
using UnityEngine;

/// <summary>
/// Zone index → physical P-agent id. RAG uses P1–P4; legacy replica uses SIMPLE_* ids.
/// Prefer <see cref="TryResolvePhysicalAgentId"/> when SceneUILoader profiles are available.
/// </summary>
public static class ZoneAgentIds
{
    static readonly string[] RagPhysicalByZone = { "P1", "P2", "P3", "P4" };
    static readonly string[] LegacyPhysicalByZone =
    {
        "SIMPLE_Technician_01",
        "SIMPLE_Technician_02",
        "SIMPLE_Supervisor_01",
        "SIMPLE_Supervisor_02"
    };

    public static string GetRagPhysicalAgentId(int zoneIndex)
    {
        int z = Mathf.Clamp(zoneIndex, 0, 3);
        return RagPhysicalByZone[z];
    }

    public static string GetLegacyPhysicalAgentId(int zoneIndex)
    {
        int z = Mathf.Clamp(zoneIndex, 0, 3);
        return LegacyPhysicalByZone[z];
    }

    /// <summary>
    /// Resolves physical agent id for a zone: SceneUILoader profile (role physical / leader link), else RAG P1–P4, else legacy SIMPLE_*.
    /// </summary>
    public static string TryResolvePhysicalAgentId(int zoneIndex)
    {
        int z = Mathf.Clamp(zoneIndex, 0, 3);

        SceneUILoader loader = UnityEngine.Object.FindObjectOfType<SceneUILoader>();
        if (loader != null && loader.sceneData != null && loader.sceneData.agentProfiles != null)
        {
            foreach (var kvp in loader.sceneData.agentProfiles)
            {
                AgentProfile p = kvp.Value;
                if (p == null || p.zoneIndex != z) continue;
                string id = !string.IsNullOrWhiteSpace(p.agentId) ? p.agentId.Trim() : kvp.Key;
                if (string.IsNullOrEmpty(id)) continue;
                bool isPhysical = id.StartsWith("P", StringComparison.OrdinalIgnoreCase)
                    || (p.role != null && p.role.IndexOf("physical", StringComparison.OrdinalIgnoreCase) >= 0);
                if (isPhysical)
                    return id;
            }

            foreach (var kvp in loader.sceneData.agentProfiles)
            {
                AgentProfile p = kvp.Value;
                if (p == null || p.zoneIndex != z) continue;
                string id = !string.IsNullOrWhiteSpace(p.agentId) ? p.agentId.Trim() : kvp.Key;
                if (!string.IsNullOrEmpty(id) && id.StartsWith("P", StringComparison.OrdinalIgnoreCase))
                    return id;
            }
        }

        ReplicaSceneSetup setup = UnityEngine.Object.FindObjectOfType<ReplicaSceneSetup>();
        bool ragMode = setup != null && setup.ragOnlyMode
            && setup.jsonFileName != null
            && setup.jsonFileName.IndexOf("ml2", StringComparison.OrdinalIgnoreCase) >= 0;
        return ragMode ? GetRagPhysicalAgentId(z) : GetLegacyPhysicalAgentId(z);
    }

    /// <summary>
    /// Maps SceneGenerator object names and ML fallbacks to JSON / AgentSequenceManager keys
    /// (e.g. <c>Agent_P3</c> → <c>P3</c>, <c>Agent_M1</c> → <c>M1</c>).
    /// </summary>
    public static string NormalizeProfileAgentId(string rawId, int zoneIndex = -1)
    {
        if (string.IsNullOrWhiteSpace(rawId))
            return zoneIndex >= 0 ? TryResolvePhysicalAgentId(zoneIndex) : rawId;

        string id = rawId.Trim();
        if (id.StartsWith("Agent_", StringComparison.Ordinal))
        {
            id = id.Substring("Agent_".Length);
            int paren = id.IndexOf('(');
            if (paren > 0)
                id = id.Substring(0, paren).TrimEnd();
            id = id.Trim();
        }

        if (!string.IsNullOrEmpty(id))
            return id;

        return zoneIndex >= 0 ? TryResolvePhysicalAgentId(zoneIndex) : rawId.Trim();
    }

    public static int ZoneIndexFromPhysicalAgentId(string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId)) return -1;
        string id = NormalizeProfileAgentId(agentId);
        for (int i = 0; i < RagPhysicalByZone.Length; i++)
        {
            if (string.Equals(id, RagPhysicalByZone[i], StringComparison.OrdinalIgnoreCase))
                return i;
        }
        for (int i = 0; i < LegacyPhysicalByZone.Length; i++)
        {
            if (string.Equals(id, LegacyPhysicalByZone[i], StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}
