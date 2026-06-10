using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime flags shared between ReplicaSceneSetup, SceneGenerator, and multiplayer bootstrap.
/// </summary>
public static class BsgIntegrationSettings
{
    public static bool SingleZoneMode;
    public static Vector3 ZoneWorldOrigin = Vector3.zero;
    public static int ActiveZoneIndex = 0;

    /// <summary>ProtoypeSceneMultiplayer: spawn RAG zone 0 only; no BSG training HUD or camera takeover.</summary>
    public static bool MultiplayerEmbedMode;

    /// <summary>When true, do not spawn P1 — local Photon player performs RAG physical steps instead.</summary>
    public static bool UsePhotonPlayerAsPhysicalAgent;

    /// <summary>Bitmask of zones to spawn (bit 0 = zone 0). 0 = all zones.</summary>
    public static int SpawnZonesMask;

    /// <summary>When true, SceneGenerator skips BSG floor/walls/4-grid.</summary>
    public static bool SkipEnvironmentGeneration;

    /// <summary>Parent RAG content under MultiplayerRagZone0Anchor and remap JSON coords.</summary>
    public static bool UseSceneAnchorLayout;

    public static Vector3 RagWorldOrigin = Vector3.zero;

    /// <summary>Set by <c>MultiplayerRagZone0Anchor</c> (Assembly-CSharp) — avoids asmdef cross-reference.</summary>
    public static Func<string, Vector3, Vector3> MapJsonToWorld;
    public static Func<Transform> GetRagWorldRoot;
    public static Action ApplyPlayAreaToAgents;
    public static Func<IEnumerable<GameObject>, bool> ValidateOverlapAndNudge;
    public static Action<SceneData> PrepareLayoutFromSceneData;

    /// <summary>Set by <c>MultiplayerRagZone0Anchor</c> — Display 2 zone 0 overview (Assembly-CSharp).</summary>
    public static Action EnsureDisplay2OverviewCamera;

    public static float MultiplayerAgentScaleMultiplier = 1f;
    public static float MultiplayerCognitiveScaleMultiplier = 1f;
    public static float MultiplayerEnvironmentScaleMultiplier = 1f;
    public static float MultiplayerEnvironmentHeightMultiplier = 2.45f;
    public static float MultiplayerEnvironmentLabelScale = 1f;
    public static float MultiplayerPhysicalLabelStagger = 0.36f;

    public static float MultiplayerCognitiveProximityRadius = 2.65f;
    public static float MultiplayerPhysicalProximityRadius = 2.35f;
    public static bool MultiplayerShowProximityZones = true;

    /// <summary>When set, <see cref="RagSceneJsonBridge"/> uses these spacing values instead of solo defaults.</summary>
    public static RagZoneLayoutSpacing? ZoneLayoutSpacingOverride;

    /// <summary>World-space XZ play area for zone 0 when embedded in ProtoypeSceneMultiplayer (set by anchor).</summary>
    public static ZonePlayAreaWorldRect? MultiplayerZone0PlayAreaWorld;

    /// <summary>Per-zone flag: multiplayer embed finished full DAG — hold idle instead of ML episode restart.</summary>
    static readonly bool[] ZoneMlRunComplete = new bool[4];

    public static void MarkZoneMlRunComplete(int zoneIndex)
    {
        if (zoneIndex >= 0 && zoneIndex < ZoneMlRunComplete.Length)
            ZoneMlRunComplete[zoneIndex] = true;
    }

    public static bool IsZoneMlRunComplete(int zoneIndex) =>
        zoneIndex >= 0 && zoneIndex < ZoneMlRunComplete.Length && ZoneMlRunComplete[zoneIndex];

    public static bool ShouldHoldMultiplayerIdleAfterZoneComplete =>
        MultiplayerEmbedMode;

    /// <summary>Set by <c>MultiplayerRagZone0Anchor</c> — resolves designated P1 spawn in physical band (Assembly-CSharp bridge).</summary>
    public static System.Func<float, Vector3?> TryResolveDesignatedPhysicalSpawnWorld;

    public static bool HasSceneAnchorLayout =>
        MapJsonToWorld != null && GetRagWorldRoot != null;

    public static bool SuppressRagTrainingHud => MultiplayerEmbedMode;
    public static bool SuppressRagCameraOverride => MultiplayerEmbedMode;

    public static bool ShouldSpawnZone(int zoneIndex)
    {
        if (SpawnZonesMask == 0)
            return true;
        if (zoneIndex < 0)
            zoneIndex = 0;
        return (SpawnZonesMask & (1 << zoneIndex)) != 0;
    }

    public static void RegisterSceneAnchorLayout(
        Func<string, Vector3, Vector3> mapJson,
        Func<Transform> getRoot,
        Action applyPlayArea,
        Func<IEnumerable<GameObject>, bool> validateOverlap,
        Action<SceneData> prepareLayout = null)
    {
        MapJsonToWorld = mapJson;
        GetRagWorldRoot = getRoot;
        ApplyPlayAreaToAgents = applyPlayArea;
        ValidateOverlapAndNudge = validateOverlap;
        PrepareLayoutFromSceneData = prepareLayout;
    }

    public static void UnregisterSceneAnchorLayout()
    {
        MapJsonToWorld = null;
        GetRagWorldRoot = null;
        ApplyPlayAreaToAgents = null;
        ValidateOverlapAndNudge = null;
        PrepareLayoutFromSceneData = null;
        EnsureDisplay2OverviewCamera = null;
        MultiplayerZone0PlayAreaWorld = null;
    }

    public static void ApplyFromSetup(ReplicaSceneSetup setup)
    {
        if (setup == null) return;
        SingleZoneMode = setup.singleZoneMode;
        ZoneWorldOrigin = setup.zoneWorldOrigin;
        ActiveZoneIndex = 0;
        MultiplayerEmbedMode = setup.multiplayerEmbedMode;
        UsePhotonPlayerAsPhysicalAgent = setup.multiplayerEmbedMode;
        SpawnZonesMask = setup.spawnZonesMask;
        SkipEnvironmentGeneration = setup.skipEnvironmentGeneration;
        UseSceneAnchorLayout = setup.useSceneAnchorLayout;
        RagWorldOrigin = setup.ragWorldOrigin;
    }

    public static void Reset()
    {
        SingleZoneMode = false;
        ZoneWorldOrigin = Vector3.zero;
        ActiveZoneIndex = 0;
        MultiplayerEmbedMode = false;
        UsePhotonPlayerAsPhysicalAgent = false;
        SpawnZonesMask = 0;
        SkipEnvironmentGeneration = false;
        UseSceneAnchorLayout = false;
        RagWorldOrigin = Vector3.zero;
        UnregisterSceneAnchorLayout();
        MultiplayerAgentScaleMultiplier = 1f;
        MultiplayerCognitiveScaleMultiplier = 1f;
        MultiplayerEnvironmentScaleMultiplier = 1f;
        MultiplayerEnvironmentHeightMultiplier = 2.45f;
        MultiplayerEnvironmentLabelScale = 1f;
        MultiplayerPhysicalLabelStagger = 0.36f;
        MultiplayerCognitiveProximityRadius = 2.65f;
        MultiplayerPhysicalProximityRadius = 2.35f;
        MultiplayerShowProximityZones = true;
        ZoneLayoutSpacingOverride = null;
        EnsureDisplay2OverviewCamera = null;
        MultiplayerZone0PlayAreaWorld = null;
        for (int i = 0; i < ZoneMlRunComplete.Length; i++)
            ZoneMlRunComplete[i] = false;
        TryResolveDesignatedPhysicalSpawnWorld = null;
    }
}

/// <summary>World-space axis-aligned XZ rectangle for agent clamping in multiplayer zone 0.</summary>
public struct ZonePlayAreaWorldRect
{
    public float minX, maxX, minZ, maxZ;

    public bool IsValid => maxX > minX && maxZ > minZ;
}

/// <summary>JSON-local spacing for cognitive stations vs physical-environment objects (see RagSceneJsonBridge).</summary>
public struct RagZoneLayoutSpacing
{
    public float cognitiveSpacingMultiplier;
    public float environmentSpacingMultiplier;
    public Vector2 cognitiveGridAnchor;
    public Vector2 environmentGridAnchor;
    public Vector2 cognitivePlacementOffset;
    public Vector2 environmentPlacementOffset;
    public float cognitiveOverflowGridStep;

    /// <summary>Extra separation between module row (z≈9) and buffer rows in legacy cognitive grid.</summary>
    public float cognitiveModuleBufferGapMultiplier;

    public static RagZoneLayoutSpacing SoloDefaults => new RagZoneLayoutSpacing
    {
        cognitiveSpacingMultiplier = 1.54f,
        environmentSpacingMultiplier = 1.68f,
        cognitiveGridAnchor = new Vector2(0f, 9f),
        environmentGridAnchor = new Vector2(8f, -12f),
        cognitivePlacementOffset = new Vector2(9f, 1.25f),
        environmentPlacementOffset = new Vector2(-7f, 0f),
        cognitiveOverflowGridStep = 7f,
        cognitiveModuleBufferGapMultiplier = 1.35f,
    };

    public static RagZoneLayoutSpacing MultiplayerZone0Defaults => new RagZoneLayoutSpacing
    {
        cognitiveSpacingMultiplier = 1.85f,
        environmentSpacingMultiplier = 2.45f,
        cognitiveGridAnchor = new Vector2(0f, 9f),
        environmentGridAnchor = new Vector2(0f, -14f),
        cognitivePlacementOffset = new Vector2(0f, 5f),
        environmentPlacementOffset = new Vector2(0f, -12f),
        cognitiveOverflowGridStep = 8.5f,
        cognitiveModuleBufferGapMultiplier = 1.72f,
    };

    /// <summary>Preserves legacy RAG grid positions from <c>TryGetLegacyZone01CognitivePosition</c> and zone targetObjects.</summary>
    public static RagZoneLayoutSpacing RagFaithfulDefaults => new RagZoneLayoutSpacing
    {
        cognitiveSpacingMultiplier = 1f,
        environmentSpacingMultiplier = 1f,
        cognitiveGridAnchor = Vector2.zero,
        environmentGridAnchor = Vector2.zero,
        cognitivePlacementOffset = Vector2.zero,
        environmentPlacementOffset = Vector2.zero,
        cognitiveOverflowGridStep = 8.5f,
        cognitiveModuleBufferGapMultiplier = 1f,
    };
}
