using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene anchor for BSG zone 0 RAG content inside ProtoypeSceneMultiplayer.
/// Derives zone 0 walls from Base geometry, pre-fits JSON cluster before spawn, clamps inside walls after spawn.
/// </summary>
[DefaultExecutionOrder(-200)]
public class MultiplayerRagZone0Anchor : MonoBehaviour
{
    public static MultiplayerRagZone0Anchor Instance { get; private set; }

    public static void ResetForDomainReload()
    {
        Instance = null;
    }

    [Header("Wall detection")]
    [Min(0.25f)]
    public float wallInteriorMargin = 1f;

    [Header("Layout fit")]
    [Min(0.05f)]
    public float maxLayoutScale = 0.46f;

    [Range(0.5f, 1f)]
    public float fitMargin = 0.95f;

    [Tooltip("Shift RAG cluster center in Base-local XZ (extra nudge after centerRagOnZoneInterior).")]
    public Vector2 layoutCenterOffset = Vector2.zero;

    [Tooltip("Place the full BSG zone 0 cluster at the geometric center of zone 0 walls.")]
    public bool centerRagOnZoneInterior = true;

    [Header("Clear scene props from RAG center (ProtoypeSceneMultiplayer objects)")]
    [Tooltip("When off, scene props keep their authored positions from the scene file.")]
    public bool relocateSceneObjectsFromCenter = false;
    [Tooltip("Only move =GAME/Props and =Environment/Props that sit inside the center clear radius.")]
    public bool relocateCenterBlockingPropsOnly = true;
    [Tooltip("When relocateSceneObjectsFromCenter is on, also nudge spawn points to anchor offsets.")]
    public bool relocateSpawnPoints = false;
    [Tooltip("3D Zone - 0 label (MiniMapLayout/ZoneText) — Base-local XZ when autoCenterZoneLabel is off.")]
    public Vector2 zoneLabelBaseLocal = new Vector2(0f, -12.5f);
    [Tooltip("Place Zone - 0 text at the geometric center of zone 0 walls.")]
    public bool autoCenterZoneLabel = true;
    [Tooltip("Supervisor spawn — Base-local XZ.")]
    public Vector2 supervisorSpawnBaseLocal = new Vector2(-12f, -4f);
    [Tooltip("Worker spawn points — Base-local XZ (applied in order found).")]
    public Vector2[] workerSpawnBaseLocals =
    {
        new Vector2(-12f, -7f),
        new Vector2(-12f, -11f),
        new Vector2(-12f, -15f),
    };
    public Vector2 playerStartBaseLocal = new Vector2(-12f, -18f);
    [Tooltip("Base-local XZ for the designated RAG physical agent (Photon blue player). (0,0) = auto at the front of the physical/target band.")]
    public Vector2 designatedPhysicalAgentSpawnBaseLocal = Vector2.zero;
    [Tooltip("Base-local radius around zone interior center — only props inside are relocated when relocateCenterBlockingPropsOnly is on.")]
    [Min(2f)] public float scenePropsCenterClearRadius = 9f;
    [Tooltip("Back-wall staging for center props that block the BSG cluster (Base-local XZ).")]
    public Vector2 scenePropsRelocationBaseLocal = new Vector2(-2f, -22f);
    [Tooltip("West-side corner staging for props that block the RAG physical target band (TablePivot, Cylinder, etc.). ShowCaseTable objects are not moved.")]
    public Vector2 scenePropsCornerRelocationBaseLocal = Vector2.zero;
    [Tooltip("Minimum inset from zone 0 walls when relocating/staging scene props (Base-local).")]
    [Min(0.75f)] public float scenePropBoundaryInset = 2.5f;
    [Tooltip("Relocate legacy scene props (TablePivot, Cylinder) that sit inside the RAG physical band.")]
    public bool relocatePhysicalBandBlockingProps = true;

    [Header("Cylinder + Cube staging (=GAME/Props)")]
    [Tooltip("Base-local XZ for Cylinder — west corridor, below cognitive band (not center/bifurcation).")]
    public Vector2 cylinderStagingBaseLocal = new Vector2(-12f, -8f);
    [Tooltip("Local position for the Cube child on Cylinder after staging.")]
    public Vector3 cylinderCubeChildLocalPosition = new Vector3(0f, -0.45f, 0f);

    static readonly string[] PhysicalBandBlockingPropNames = { "TablePivot" };

    [Header("Object spacing (JSON layout — cognitive vs physical env props)")]
    [Tooltip("Spread between individual cognitive stations (modules/buffers). 1 = exact RAG grid.")]
    [Range(1f, 2.5f)] public float cognitiveSpacingMultiplier = 1.58f;
    [Tooltip("Spread between individual physical-environment target objects. 1 = exact RAG positions.")]
    [Range(1f, 3.5f)] public float environmentSpacingMultiplier = 1f;
    [Tooltip("JSON-local grid anchor for cognitive stations.")]
    public Vector2 cognitiveGridAnchor = Vector2.zero;
    [Tooltip("JSON-local grid anchor for physical-environment objects.")]
    public Vector2 environmentGridAnchor = Vector2.zero;
    [Tooltip("Cognitive cluster anchor offset in JSON-local XZ.")]
    public Vector2 cognitivePlacementOffset = Vector2.zero;
    [Tooltip("Environment cluster anchor offset in JSON-local XZ.")]
    public Vector2 environmentPlacementOffset = Vector2.zero;
    [Min(4f)] public float cognitiveOverflowGridStep = 10.5f;
    [Tooltip("Extra Z separation between orange modules and blue buffer rows in the cognitive grid.")]
    [Range(1f, 2.5f)] public float cognitiveModuleBufferGapMultiplier = 1.72f;
    [Tooltip("Legacy JSON Z reference for the module row when applying buffer gap.")]
    public float cognitiveModuleRowReferenceZ = 9f;

    [Header("Proximity zones (multiplayer zone 0)")]
    [Tooltip("Show translucent proximity discs around cognitive + physical RAG objects.")]
    public bool showProximityZones = true;
    [Min(1.5f)] public float cognitiveProximityRadius = 2.65f;
    [Min(1.5f)] public float physicalProximityRadius = 2.35f;

    [Header("Mental / physical split (multiplayer zone 0)")]
    [Tooltip("Keep cognitive stations + M agent near zone center; push P agent + env props toward the back wall.")]
    public bool splitMentalPhysicalLayout = true;
    [Tooltip("Base-local Z bias for the mental band above zone geometric center (toward bifurcation / top of zone 0).")]
    public float cognitiveBandBiasZ = 8f;
    [Tooltip("Inset from the back wall when placing the physical/environment band.")]
    [Min(0.5f)] public float physicalBandInsetFromBackWall = 3f;
    [Tooltip("Depth of the physical/environment band along Base-local Z.")]
    [Min(4f)] public float physicalBandDepth = 9f;
    [Tooltip("Extra horizontal spread for physical-environment props at the bottom band.")]
    [Range(1f, 3f)] public float physicalBandHorizontalSpread = 1.85f;
    [Tooltip("Extra depth spread for physical-environment props within the bottom band.")]
    [Range(1f, 3f)] public float physicalBandDepthSpread = 1.55f;

    [Header("Post-spawn clamp")]
    [Range(0.75f, 1f)]
    public float minRootScale = 1f;

    [Header("Optional overrides (leave zero size to auto-detect from walls)")]
    public Bounds zone0InteriorBoundsOverride;
    public bool useZone0InteriorOverride;

    [Header("Designated zone 0 physical player (Photon blue worker)")]
    [Tooltip("Uniform scale for the designated physical Photon player so they read clearly in zone 0.")]
    [Range(1f, 2.5f)] public float designatedPhysicalPlayerScale = 1.55f;

    [Header("Multiplayer size overrides")]
    [Range(0.4f, 1.2f)] public float agentScaleMultiplier = 0.54f;
    [Range(0.4f, 1.2f)] public float cognitiveScaleMultiplier = 0.36f;
    [Range(0.5f, 1.2f)] public float environmentScaleMultiplier = 0.72f;
    [Range(1.2f, 3.5f)] public float environmentHeightMultiplier = 2.45f;

    [Header("Zone 0 boundary (use ProtoypeSceneMultiplayer Base walls)")]
    [Tooltip("Hide MiniMapLayout wall sprites and floor overlay; clamp RAG to Base Left/Right/Back/Bifurcation.")]
    public bool useSceneWallBoundaryOnly = true;
    [Tooltip("Remove any BSG-generated zone walls/floors if they appear at runtime.")]
    public bool destroyStrayBsgZoneMeshes = true;
    [Tooltip("Hide MiniMapLayout 'Square' wall segments (not the scene Base walls).")]
    public bool hideMiniMapZoneBoundaryWalls = true;
    [Tooltip("Hide MiniMapLayout GroundSquare floor overlay.")]
    public bool hideMiniMapGroundOverlay = true;
    [Range(0.5f, 1.2f)] public float zoneLabelScale = 1f;

    [Header("P1 play area (local XZ clamps for BSGMLAgent when ML training is enabled)")]
    public float playAreaMinX = 2f;
    public float playAreaMaxX = 14f;
    public float playAreaMinZ = -22f;
    public float playAreaMaxZ = -2f;

    [Header("ML-Agents training (zone 0 embed)")]
    [Tooltip("When on, mlagents-learn collects PhysicalAgentZone0 from the designated Photon player (P1) and CognitiveAgentZone0 from M1. Turn off for normal multiplayer without Python.")]
    public bool enableMlTrainingInRagMode = true;
    [Tooltip("Train M1 cognitive brain → CognitiveAgentZone0.onnx.")]
    public bool enableMlTrainingForCognitiveAgents = true;
    [Tooltip("Bitmask for ML bootstrap. 1 = zone 0 only (recommended). 0 = all zones.")]
    public int trainZonesMask = 1;

    [Header("Overlap avoidance")]
    public Bounds excludeOverlapBounds = new Bounds(new Vector3(-2f, 0.5f, 0f), new Vector3(12f, 2f, 14f));
    [Min(0.5f)] public float playerSpawnPadding = 3.5f;
    public bool autoCollectSpawnExclusion = true;

    [Header("Display 2 — zone 0 overview camera")]
    public bool enableDisplay2Overview = true;
    [Min(10f)] public float overviewCameraHeight = 32f;
    [Min(4f)] public float overviewCameraBackOffset = 16f;
    [Range(35f, 75f)] public float overviewCameraPitch = 52f;
    [Range(50f, 90f)] public float overviewCameraFov = 68f;
    [Tooltip("Look-at point along zone 0 depth (0 = back wall, 1 = bifurcation). 0.5 = geometric center.")]
    [Range(0.35f, 0.75f)] public float overviewLookAtDepthBias = 0.5f;
    [Tooltip("Extra margin when fitting the full zone 0 rectangle in frame.")]
    [Range(1f, 1.25f)] public float overviewFrameMargin = 1.1f;

    [Header("Display 3 — designated zone 0 physical player (full-body follow)")]
    public bool enableDisplay3DesignatedPlayerFollow = true;
    [Tooltip("Minimum distance behind the agent (meters); also scaled by body height.")]
    [Min(2f)] public float designatedFollowDistanceBehind = 4.6f;
    [Tooltip("Distance behind = max(min distance, bodyHeight × this factor).")]
    [Min(1.5f)] public float designatedFollowDistanceBodyFactor = 2.15f;
    [Tooltip("Extra camera height above feet, scaled with body height.")]
    [Min(0.5f)] public float designatedFollowHeight = 1.1f;
    [Min(0f)] public float designatedFollowDroneLift = 0.15f;
    [Tooltip("Body-height fraction to aim at (0.5 ≈ torso center for full-body framing).")]
    [Range(0.35f, 0.65f)] public float designatedFollowBodyCenterFraction = 0.5f;
    [Tooltip("Small forward look offset from the body center (meters).")]
    [Min(0f)] public float designatedFollowLookAhead = 0.25f;
    [Range(42f, 75f)] public float designatedFollowFov = 56f;
    [Min(0f)] public float designatedFollowShoulderOffset = 0.32f;

    Transform _ragWorldRoot;
    Transform _baseTransform;

    Bounds _zone0InteriorBounds;
    Bounds _ragPlacementBounds;
    Vector3 _layoutCentroid;
    float _layoutScale = 0.3f;
    Vector3 _layoutOffsetAnchorLocal;

    struct BandLayoutData
    {
        public Vector3 centroid;
        public float scale;
        public Vector3 offsetAnchorLocal;
        public bool valid;
    }

    BandLayoutData _mentalBandLayout;
    BandLayoutData _physicalBandLayout;
    bool _useSplitBandLayout;

    readonly List<Bounds> _exclusionBounds = new List<Bounds>(4);

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[MultiplayerRagZone0Anchor] Duplicate anchor — keeping first.");
            return;
        }
        Instance = this;
        _baseTransform = transform.parent;

        if (useSceneWallBoundaryOnly)
            ConfigureMiniMapLayoutForSceneWalls();
        if (destroyStrayBsgZoneMeshes)
            DestroyStrayBsgZoneMeshes();

        BsgIntegrationSettings.MultiplayerAgentScaleMultiplier = agentScaleMultiplier;
        BsgIntegrationSettings.MultiplayerCognitiveScaleMultiplier = cognitiveScaleMultiplier;
        BsgIntegrationSettings.MultiplayerEnvironmentScaleMultiplier = environmentScaleMultiplier;
        BsgIntegrationSettings.MultiplayerEnvironmentHeightMultiplier = environmentHeightMultiplier;
        BsgIntegrationSettings.MultiplayerCognitiveProximityRadius = cognitiveProximityRadius;
        BsgIntegrationSettings.MultiplayerPhysicalProximityRadius = physicalProximityRadius;
        BsgIntegrationSettings.MultiplayerShowProximityZones = showProximityZones;
        ApplyLayoutSpacingOverride();

        ResolveZone0InteriorFromScene();
        RefreshExclusionBounds();
        UpdatePlayAreaFromRagBounds();
        SyncMultiplayerPlayAreaToSettings();

        BsgIntegrationSettings.RegisterSceneAnchorLayout(
            MapJsonToWorld,
            GetOrCreateRagWorldRoot,
            ApplyPlayAreaToZoneAgents,
            ValidateOverlapAndNudge,
            PrepareLayoutFromSceneData);
        BsgIntegrationSettings.EnsureDisplay2OverviewCamera = EnsureDisplay2OverviewCamera;
        BsgIntegrationSettings.TryResolveDesignatedPhysicalSpawnWorld = ResolveDesignatedPhysicalSpawnForBridge;
    }

    Vector3? ResolveDesignatedPhysicalSpawnForBridge(float worldY)
    {
        return TryGetDesignatedPhysicalAgentSpawnWorld(out Vector3 worldPos, worldY) ? worldPos : (Vector3?)null;
    }

    void Start()
    {
        if (useSceneWallBoundaryOnly)
            ConfigureMiniMapLayoutForSceneWalls();
        if (relocateSceneObjectsFromCenter || relocateCenterBlockingPropsOnly || relocatePhysicalBandBlockingProps)
            RelocateSceneObjectsFromCenter();
    }

    void ConfigureMiniMapLayoutForSceneWalls()
    {
        Transform miniMapLayout = GameObject.Find("MiniMapLayout")?.transform;
        if (miniMapLayout == null)
            return;

        for (int i = 0; i < miniMapLayout.childCount; i++)
        {
            Transform child = miniMapLayout.GetChild(i);
            if (child == null)
                continue;

            string name = child.name;
            if (hideMiniMapZoneBoundaryWalls && string.Equals(name, "Square", System.StringComparison.Ordinal))
            {
                child.gameObject.SetActive(false);
                continue;
            }

            if (hideMiniMapGroundOverlay && string.Equals(name, "GroundSquare", System.StringComparison.Ordinal))
            {
                child.gameObject.SetActive(false);
                continue;
            }

            if (name.IndexOf("ZoneText", System.StringComparison.OrdinalIgnoreCase) >= 0
                && Mathf.Abs(zoneLabelScale - 1f) > 0.01f)
            {
                child.localScale = Vector3.one * zoneLabelScale;
            }
        }
    }

    static void DestroyStrayBsgZoneMeshes()
    {
        DestroyObjectsWithNamePrefix("Wall_Zone");
        DestroyObjectsWithNamePrefix("JSON_Generated_Wall");
        DestroyObjectsWithNamePrefix("Ground_Zone");
        DestroyObjectsWithNamePrefix("Wall_Divider");
        DestroyObjectsWithNamePrefix("JSON_Generated_Floor");
        DestroyObjectsWithNamePrefix("JSON_BaseGreyPlane");
    }

    static void DestroyObjectsWithNamePrefix(string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
            return;

        GameObject[] all = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            GameObject go = all[i];
            if (go == null || !go.name.StartsWith(prefix, System.StringComparison.Ordinal))
                continue;

            Object.Destroy(go);
        }
    }

    void OnDestroy()
    {
        if (BsgIntegrationSettings.TryResolveDesignatedPhysicalSpawnWorld == ResolveDesignatedPhysicalSpawnForBridge)
            BsgIntegrationSettings.TryResolveDesignatedPhysicalSpawnWorld = null;

        if (Instance == this)
        {
            BsgIntegrationSettings.MultiplayerAgentScaleMultiplier = 1f;
            BsgIntegrationSettings.MultiplayerCognitiveScaleMultiplier = 1f;
            BsgIntegrationSettings.MultiplayerEnvironmentScaleMultiplier = 1f;
            BsgIntegrationSettings.ZoneLayoutSpacingOverride = null;
            BsgIntegrationSettings.EnsureDisplay2OverviewCamera = null;
            BsgIntegrationSettings.UnregisterSceneAnchorLayout();
            Instance = null;
        }
    }

    void ApplyLayoutSpacingOverride()
    {
        BsgIntegrationSettings.ZoneLayoutSpacingOverride = new RagZoneLayoutSpacing
        {
            cognitiveSpacingMultiplier = cognitiveSpacingMultiplier,
            environmentSpacingMultiplier = environmentSpacingMultiplier,
            cognitiveGridAnchor = cognitiveGridAnchor,
            environmentGridAnchor = environmentGridAnchor,
            cognitivePlacementOffset = cognitivePlacementOffset,
            environmentPlacementOffset = environmentPlacementOffset,
            cognitiveOverflowGridStep = cognitiveOverflowGridStep,
            cognitiveModuleBufferGapMultiplier = cognitiveModuleBufferGapMultiplier,
        };
    }

    public bool TryGetOverviewLookAtWorld(out Vector3 world)
    {
        return TryGetOverviewFrame(out world, out _, out _, out _, out _);
    }

    /// <summary>Framing for Display 2 — full zone 0 interior in frame (excludes zone 1).</summary>
    public bool TryGetOverviewFrame(out Vector3 lookAtWorld, out float height, out float backOffset, out float pitch, out float fov)
    {
        lookAtWorld = default;
        height = overviewCameraHeight;
        backOffset = overviewCameraBackOffset;
        pitch = overviewCameraPitch;
        fov = overviewCameraFov;

        if (!TryGetZone0InteriorWorldBounds(out Bounds zoneWorld))
            return false;

        float lookZ = Mathf.Lerp(zoneWorld.min.z, zoneWorld.max.z, overviewLookAtDepthBias);
        lookAtWorld = new Vector3(zoneWorld.center.x, zoneWorld.min.y + 0.5f, lookZ);

        pitch = overviewCameraPitch;
        fov = overviewCameraFov;

        float pitchRad = pitch * Mathf.Deg2Rad;
        float halfV = fov * 0.5f * Mathf.Deg2Rad;
        float halfH = Mathf.Atan(Mathf.Tan(halfV) * (16f / 9f));
        float margin = overviewFrameMargin;

        float halfDepth = zoneWorld.extents.z * margin;
        float halfWidth = zoneWorld.extents.x * margin;

        float distForDepth = halfDepth / Mathf.Max(0.25f, Mathf.Tan(halfV) * Mathf.Cos(pitchRad * 0.5f));
        float distForWidth = halfWidth / Mathf.Max(0.25f, Mathf.Tan(halfH));
        float horizontalDist = Mathf.Max(distForDepth, distForWidth, zoneWorld.size.z * 0.35f);

        height = Mathf.Max(overviewCameraHeight, horizontalDist * Mathf.Tan(pitchRad) + 6f);
        backOffset = Mathf.Max(overviewCameraBackOffset, horizontalDist);

        return true;
    }

    public bool TryGetZone0InteriorWorldBounds(out Bounds worldBounds)
    {
        worldBounds = default;
        ResolveZone0InteriorFromScene();

        Transform baseTf = _baseTransform != null ? _baseTransform : transform.parent;
        if (baseTf == null)
            return false;

        Vector3 worldCenter = baseTf.TransformPoint(_zone0InteriorBounds.center);
        Vector3 worldExtents = baseTf.TransformVector(_zone0InteriorBounds.extents);
        worldExtents.x = Mathf.Abs(worldExtents.x);
        worldExtents.y = Mathf.Max(Mathf.Abs(worldExtents.y), 1f);
        worldExtents.z = Mathf.Abs(worldExtents.z);
        worldBounds = new Bounds(worldCenter, worldExtents * 2f);
        return worldBounds.size.sqrMagnitude > 0.01f;
    }

    Vector3 GetLayoutPlacementCenterBase()
    {
        Bounds fit = GetLayoutFitBounds();
        return new Vector3(
            fit.center.x + layoutCenterOffset.x,
            0f,
            fit.center.z + layoutCenterOffset.y);
    }

    Bounds GetLayoutFitBounds()
    {
        return centerRagOnZoneInterior ? _zone0InteriorBounds : _ragPlacementBounds;
    }

    void RelocateSceneObjectsFromCenter()
    {
        if (_baseTransform == null)
            _baseTransform = transform.parent;
        if (_baseTransform == null)
            return;

        ResolveZone0InteriorFromScene();

        Transform zoneLabel = FindZoneLabelTransform();
        if (zoneLabel != null && autoCenterZoneLabel)
            SetBaseLocalXZ(zoneLabel, new Vector2(_zone0InteriorBounds.center.x, _zone0InteriorBounds.center.z));
        else if (zoneLabel != null && relocateSceneObjectsFromCenter)
            SetBaseLocalXZ(zoneLabel, zoneLabelBaseLocal);

        if (relocateSceneObjectsFromCenter && relocateSpawnPoints)
            RelocateSpawnPointsToAnchorOffsets();

        if (relocateCenterBlockingPropsOnly)
            RelocateCenterOverlappingPropsOnly();

        if (relocatePhysicalBandBlockingProps)
            RelocatePhysicalBandBlockingProps();
    }

    /// <summary>Moves legacy scene props (TablePivot, Cylinder, etc.) out of the RAG physical target band to a corner. Safe to call after zone 0 spawn.</summary>
    public void RelocatePhysicalBandBlockingProps()
    {
        if (_baseTransform == null)
            _baseTransform = transform.parent;
        if (_baseTransform == null)
            return;

        ResolveZone0InteriorFromScene();

        Vector2 bandCenter = GetPhysicalPlacementCenterBase();
        float clearRadius = Mathf.Max(scenePropsCenterClearRadius * 0.85f, physicalBandDepth * 0.55f + 3f);
        float clearRadiusSq = clearRadius * clearRadius;
        int slot = 0;

        for (int i = 0; i < PhysicalBandBlockingPropNames.Length; i++)
        {
            RelocateNamedPropIfInsideRadius(
                PhysicalBandBlockingPropNames[i],
                bandCenter,
                clearRadiusSq,
                ref slot,
                useCornerStaging: true);
        }

        RelocateCylinderAssemblyIfInsideRadius(bandCenter, clearRadiusSq);
    }

    void RelocateCylinderAssemblyIfInsideRadius(Vector2 bandCenter, float clearRadiusSq)
    {
        GameObject cylinderGo = GameObject.Find("Cylinder");
        if (cylinderGo == null)
            return;

        if (!ShouldStageCylinderAssembly(cylinderGo.transform, bandCenter, clearRadiusSq))
            return;

        ApplyCylinderAssemblyStaging(cylinderGo.transform);
    }

    bool ShouldStageCylinderAssembly(Transform cylinder, Vector2 bandCenter, float clearRadiusSq)
    {
        if (cylinder == null)
            return false;

        Transform cube = cylinder.Find("Cube");
        if (cube != null)
        {
            if ((cube.localPosition - cylinderCubeChildLocalPosition).sqrMagnitude > 0.04f)
                return true;
            if (cube.position.y > 1.25f)
                return true;
        }

        if (IsTransformInsideRadius(cylinder, bandCenter, clearRadiusSq))
            return true;

        return cube != null && IsTransformInsideRadius(cube, bandCenter, clearRadiusSq);
    }

    bool IsTransformInsideRadius(Transform t, Vector2 bandCenter, float clearRadiusSq)
    {
        if (t == null)
            return false;

        Vector2 baseLocal = WorldToBaseLocalXZ(t.position);
        return (baseLocal - bandCenter).sqrMagnitude <= clearRadiusSq;
    }

    void ApplyCylinderAssemblyStaging(Transform cylinder)
    {
        if (cylinder == null)
            return;

        Vector2 stagingBase = ClampBaseLocalToZoneInterior(cylinderStagingBaseLocal);
        stagingBase = AvoidCognitiveBandBaseLocal(stagingBase);

        Vector3 world = BaseLocalXZToWorld(stagingBase.x, stagingBase.y, 0f);

        SetTransformWorldPosition(cylinder, world);

        Transform cube = cylinder.Find("Cube");
        if (cube == null)
            return;

        cube.localPosition = cylinderCubeChildLocalPosition;
        cube.localRotation = Quaternion.identity;

        Rigidbody cubeRb = cube.GetComponent<Rigidbody>();
        if (cubeRb != null)
        {
            bool wasKinematic = cubeRb.isKinematic;
            cubeRb.isKinematic = true;
            cubeRb.linearVelocity = Vector3.zero;
            cubeRb.angularVelocity = Vector3.zero;
            cubeRb.position = cube.position;
            cubeRb.isKinematic = wasKinematic;
        }
    }

    void SetTransformWorldPosition(Transform target, Vector3 worldPos)
    {
        if (target == null)
            return;

        Rigidbody rb = target.GetComponent<Rigidbody>();
        if (rb != null)
        {
            bool wasKinematic = rb.isKinematic;
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = worldPos;
            rb.isKinematic = wasKinematic;
        }
        else
        {
            target.position = worldPos;
        }
    }

    static readonly string[] NeverRelocatePropNames =
    {
        "ShowCaseTable", "ShowCaseTable (1)", "Motor", "MotorTask", "PlayerStart",
    };

    void RelocateSpawnPointsToAnchorOffsets()
    {
        PlayerSpawner spawner = FindObjectOfType<PlayerSpawner>();
        if (spawner != null)
        {
            Transform supervisor = spawner.transform.Find("SupervisorSpawn");
            if (supervisor != null)
                SetBaseLocalXZ(supervisor, supervisorSpawnBaseLocal);

            int workerIndex = 0;
            for (int i = 0; i < spawner.transform.childCount; i++)
            {
                Transform child = spawner.transform.GetChild(i);
                if (child == null || child == supervisor)
                    continue;
                if (child.name.IndexOf("spawn", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (workerSpawnBaseLocals != null && workerIndex < workerSpawnBaseLocals.Length)
                {
                    SetBaseLocalXZ(child, workerSpawnBaseLocals[workerIndex]);
                    workerIndex++;
                }
            }
        }

        GameObject playerStart = GameObject.Find("PlayerStart");
        if (playerStart != null)
            SetBaseLocalXZ(playerStart.transform, playerStartBaseLocal);
    }

    void RelocateCenterOverlappingPropsOnly()
    {
        Vector2 zoneCenter = new Vector2(_zone0InteriorBounds.center.x, _zone0InteriorBounds.center.z);
        float clearRadiusSq = scenePropsCenterClearRadius * scenePropsCenterClearRadius;
        int slot = 0;

        Transform envProps = GameObject.Find("=Environment")?.transform?.Find("Props");
        RelocatePropsChildren(envProps, zoneCenter, clearRadiusSq, ref slot, NeverRelocatePropNames);

        Transform gameProps = FindGamePropsTransform();
        RelocatePropsChildren(gameProps, zoneCenter, clearRadiusSq, ref slot, NeverRelocatePropNames);
    }

    void RelocateNamedPropIfInsideRadius(string objectName, Vector2 center, float clearRadiusSq, ref int slot, bool useCornerStaging = false)
    {
        GameObject go = GameObject.Find(objectName);
        if (go == null)
            return;

        Vector2 baseLocal = WorldToBaseLocalXZ(go.transform.position);
        if ((baseLocal - center).sqrMagnitude > clearRadiusSq)
            return;

        ApplyScenePropRelocation(go.transform, GetScenePropRelocationOffset(slot, useCornerStaging));
        slot++;
    }

    static Transform FindGamePropsTransform()
    {
        Transform gameRoot = GameObject.Find("=GAME")?.transform;
        if (gameRoot != null)
        {
            Transform props = gameRoot.Find("Props");
            if (props != null)
                return props;
        }

        GameObject propsGo = GameObject.Find("Props");
        if (propsGo != null && propsGo.transform.parent != null
            && propsGo.transform.parent.name.IndexOf("GAME", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return propsGo.transform;
        }

        return null;
    }

    Vector2 GetScenePropRelocationOffset(int slot, bool useCornerStaging = false)
    {
        const int columns = 3;
        int col = slot % columns;
        int row = slot / columns;
        Vector2 origin = useCornerStaging
            ? GetSafeWestCorridorBaseLocal() + scenePropsCornerRelocationBaseLocal
            : scenePropsRelocationBaseLocal;
        Vector2 spaced = origin + new Vector2(col * 2.8f, row * -2.8f);
        return ClampBaseLocalToZoneInterior(spaced);
    }

    float GetScenePropBoundaryInset()
    {
        return Mathf.Max(scenePropBoundaryInset, wallInteriorMargin + 1.25f);
    }

    /// <summary>Inside zone 0 west edge, mid corridor — below cognitive band, above physical RAG band.</summary>
    Vector2 GetSafeWestCorridorBaseLocal()
    {
        float inset = GetScenePropBoundaryInset();
        float maxZBelowCognitive = _zone0InteriorBounds.center.z - 2f;
        if (splitMentalPhysicalLayout)
        {
            maxZBelowCognitive = Mathf.Min(
                maxZBelowCognitive,
                GetMentalPlacementCenterBase().z - physicalBandDepth * 0.35f);
        }

        float z = Mathf.Clamp(
            cylinderStagingBaseLocal.y,
            _zone0InteriorBounds.min.z + inset,
            maxZBelowCognitive);
        float x = _zone0InteriorBounds.min.x + inset;
        return new Vector2(x, z);
    }

    /// <summary>Push staging south if it would land in the mental/cognitive band (high Base-local Z).</summary>
    Vector2 AvoidCognitiveBandBaseLocal(Vector2 baseLocalXZ)
    {
        if (!splitMentalPhysicalLayout)
            return baseLocalXZ;

        float cognitiveFloorZ = GetMentalPlacementCenterBase().z - physicalBandDepth * 0.45f;
        if (baseLocalXZ.y > cognitiveFloorZ)
            baseLocalXZ.y = cognitiveFloorZ;

        return ClampBaseLocalToZoneInterior(baseLocalXZ);
    }

    Vector3 BaseLocalXZToWorld(float baseX, float baseZ, float worldY)
    {
        if (_baseTransform == null)
            return new Vector3(baseX, worldY, baseZ);

        Vector3 world = _baseTransform.TransformPoint(new Vector3(baseX, 0f, baseZ));
        world.y = worldY;
        return world;
    }

    /// <summary>Inside zone 0, west edge — uses safe corridor (not bifurcation / cognitive side).</summary>
    Vector2 GetSafeWestFrontCornerBaseLocal()
    {
        return GetSafeWestCorridorBaseLocal() + scenePropsCornerRelocationBaseLocal;
    }

    Vector2 ClampBaseLocalToZoneInterior(Vector2 baseLocalXZ)
    {
        float inset = GetScenePropBoundaryInset();
        float minX = _zone0InteriorBounds.min.x + inset;
        float maxX = _zone0InteriorBounds.max.x - inset;
        float minZ = _zone0InteriorBounds.min.z + inset;
        float maxZ = _zone0InteriorBounds.max.z - inset;

        if (minX > maxX || minZ > maxZ)
            return baseLocalXZ;

        return new Vector2(
            Mathf.Clamp(baseLocalXZ.x, minX, maxX),
            Mathf.Clamp(baseLocalXZ.y, minZ, maxZ));
    }

    void ApplyScenePropRelocation(Transform target, Vector2 baseLocalXZ)
    {
        if (target == null || _baseTransform == null)
            return;

        baseLocalXZ = ClampBaseLocalToZoneInterior(baseLocalXZ);

        if (!target.gameObject.activeSelf)
            target.gameObject.SetActive(true);

        Vector3 world = _baseTransform.TransformPoint(new Vector3(baseLocalXZ.x, 0f, baseLocalXZ.y));
        float preservedY = target.position.y;
        Vector3 pos = new Vector3(world.x, preservedY, world.z);

        Rigidbody rb = target.GetComponent<Rigidbody>();
        if (rb != null)
        {
            bool wasKinematic = rb.isKinematic;
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = pos;
            rb.isKinematic = wasKinematic;
        }
        else
        {
            target.position = pos;
        }
    }

    void RelocatePropsChildren(
        Transform props,
        Vector2 zoneCenter,
        float clearRadiusSq,
        ref int slot,
        string[] skipNames = null,
        bool useCornerStaging = false)
    {
        if (props == null)
            return;

        for (int i = 0; i < props.childCount; i++)
        {
            Transform child = props.GetChild(i);
            if (child == null || ShouldSkipSceneProp(child, skipNames))
                continue;

            Vector2 baseLocal = WorldToBaseLocalXZ(child.position);
            Vector2 delta = baseLocal - zoneCenter;
            if (delta.sqrMagnitude > clearRadiusSq)
                continue;

            ApplyScenePropRelocation(child, GetScenePropRelocationOffset(slot, useCornerStaging));
            slot++;
        }
    }

    void RelocatePropsChildren(Transform props, Vector2 zoneCenter, float clearRadiusSq, ref int slot, string[] skipNames = null)
    {
        RelocatePropsChildren(props, zoneCenter, clearRadiusSq, ref slot, skipNames, useCornerStaging: false);
    }

    static bool ShouldSkipSceneProp(Transform t, string[] skipNames)
    {
        if (t == null || skipNames == null)
            return false;

        string name = t.name;
        for (int i = 0; i < skipNames.Length; i++)
        {
            if (name.IndexOf(skipNames[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    Vector2 WorldToBaseLocalXZ(Vector3 world)
    {
        Vector3 local = WorldToBaseLocal(world);
        return new Vector2(local.x, local.z);
    }

    static Transform FindZoneLabelTransform()
    {
        Transform miniMap = GameObject.Find("MiniMapLayout")?.transform;
        if (miniMap == null)
            return null;

        Transform zoneText = miniMap.Find("ZoneText");
        return zoneText != null ? zoneText : miniMap.Find("ZoneText (1)");
    }

    void SetBaseLocalXZ(Transform target, Vector2 baseLocalXZ)
    {
        ApplyScenePropRelocation(target, baseLocalXZ);
    }

    public void EnsureDisplay2OverviewCamera()
    {
        EnsureSecondaryDisplayCameras();
    }

    public void EnsureSecondaryDisplayCameras()
    {
        DestroyStrayMultiplayerFollowCameras();

        if (enableDisplay2Overview)
        {
            MultiplayerZone0OverviewCamera overview = MultiplayerZone0OverviewCamera.EnsureInScene();
            overview?.TryFrameOnZone0();
            overview?.EnforceExclusiveDisplay();
        }

        if (enableDisplay3DesignatedPlayerFollow)
        {
            MultiplayerDesignatedPlayerFollowCamera drone = MultiplayerDesignatedPlayerFollowCamera.EnsureInScene();
            drone?.EnforceExclusiveDisplay();
        }
    }

    /// <summary>One-shot physics + proximity setup after RAG tools spawn (not on every camera refresh).</summary>
    public void EnsureEmbedCollisionAndProximity()
    {
        MultiplayerCognitiveStationPhysics.ApplyToScene();
        MultiplayerEnvironmentToolPhysics.ApplyToScene();
        MultiplayerProximityZoneSetup.ApplyToScene();
    }

    public static void DestroyStrayMultiplayerFollowCamerasPublic()
    {
        DestroyStrayMultiplayerFollowCameras();
    }

    static void DestroyStrayMultiplayerFollowCameras()
    {
        foreach (string name in new[]
        {
            "GameView_Display2_Rag_MentalPhysical_Camera",
            "GameView_Display3_Rag_Physical_Camera",
            "GameView_Display2_MentalPhysical_Camera",
            "GameView_Display3_Physical_Camera",
            "GameView_Display4_MA_Camera",
            "GameView_Display5_MB_Camera",
            "GameView_Display6_MC_Camera",
        })
        {
            GameObject go = GameObject.Find(name);
            if (go != null)
                Object.Destroy(go);
        }

        foreach (ZoneAgentFollowCamera cam in Object.FindObjectsOfType<ZoneAgentFollowCamera>(true))
        {
            if (cam == null)
                continue;
            if (cam.targetDisplay == 1 || cam.targetDisplay == 2)
                Object.Destroy(cam.gameObject);
        }
    }

    public void ResolveZone0InteriorFromScene()
    {
        if (_baseTransform == null)
            _baseTransform = transform.parent;

        if (useZone0InteriorOverride && zone0InteriorBoundsOverride.size.sqrMagnitude > 0.01f)
        {
            _zone0InteriorBounds = zone0InteriorBoundsOverride;
        }
        else
        {
            float leftX = -15f;
            float rightX = 15f;
            float backZ = -25f;
            float dividerZ = 0f;

            if (_baseTransform != null)
            {
                Transform left = _baseTransform.Find("Left");
                Transform right = _baseTransform.Find("Right");
                Transform back = _baseTransform.Find("Back");
                Transform divider = _baseTransform.Find("Bifurcation");
                if (left != null) leftX = left.localPosition.x;
                if (right != null) rightX = right.localPosition.x;
                if (back != null) backZ = back.localPosition.z;
                if (divider != null) dividerZ = divider.localPosition.z;
            }

            float m = wallInteriorMargin;
            Vector3 center = new Vector3(
                (leftX + rightX) * 0.5f,
                0.5f,
                (backZ + dividerZ) * 0.5f);
            Vector3 size = new Vector3(
                Mathf.Abs(rightX - leftX) - m * 2f,
                2f,
                Mathf.Abs(dividerZ - backZ) - m * 2f);
            _zone0InteriorBounds = new Bounds(center, size);
        }

        BuildRagPlacementBounds();
        Debug.Log($"[MultiplayerRagZone0Anchor] Zone0 interior (Base-local) center={_zone0InteriorBounds.center}, size={_zone0InteriorBounds.size}");
        Debug.Log($"[MultiplayerRagZone0Anchor] RAG placement (Base-local) center={_ragPlacementBounds.center}, size={_ragPlacementBounds.size}");
    }

    void BuildRagPlacementBounds()
    {
        _ragPlacementBounds = _zone0InteriorBounds;

        float westMaxX = _zone0InteriorBounds.min.x;
        if (_baseTransform != null)
        {
            PlayerSpawner spawner = FindObjectOfType<PlayerSpawner>();
            var spawnBaseLocal = new List<Vector3>(8);
            if (spawner != null)
            {
                Transform root = spawner.transform;
                for (int i = 0; i < root.childCount; i++)
                {
                    Transform child = root.GetChild(i);
                    if (child == null) continue;
                    if (child.name.IndexOf("spawn", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        spawnBaseLocal.Add(_baseTransform.InverseTransformPoint(child.position));
                }
            }

            if (spawnBaseLocal.Count > 0)
            {
                float maxSpawnX = spawnBaseLocal[0].x;
                for (int i = 1; i < spawnBaseLocal.Count; i++)
                    maxSpawnX = Mathf.Max(maxSpawnX, spawnBaseLocal[i].x);
                westMaxX = Mathf.Max(westMaxX, maxSpawnX + playerSpawnPadding);
            }
        }

        float minX = westMaxX;
        float maxX = _zone0InteriorBounds.max.x;
        float minZ = _zone0InteriorBounds.min.z;
        float maxZ = _zone0InteriorBounds.max.z;
        Vector3 center = new Vector3((minX + maxX) * 0.5f, 0.5f, (minZ + maxZ) * 0.5f);
        Vector3 size = new Vector3(Mathf.Max(maxX - minX, 2f), 2f, Mathf.Max(maxZ - minZ, 2f));
        _ragPlacementBounds = new Bounds(center, size);
    }

    void UpdatePlayAreaFromRagBounds()
    {
        Bounds bounds = centerRagOnZoneInterior ? _zone0InteriorBounds : _ragPlacementBounds;
        float inset = wallInteriorMargin * 0.5f;
        playAreaMinX = bounds.min.x + inset;
        playAreaMaxX = bounds.max.x - inset;
        playAreaMinZ = bounds.min.z + inset;
        playAreaMaxZ = bounds.max.z - inset;
        SyncMultiplayerPlayAreaToSettings();
    }

    void SyncMultiplayerPlayAreaToSettings()
    {
        if (_baseTransform == null)
            _baseTransform = transform.parent;
        if (_baseTransform == null)
        {
            BsgIntegrationSettings.MultiplayerZone0PlayAreaWorld = null;
            return;
        }

        Vector3 minWorld = _baseTransform.TransformPoint(new Vector3(playAreaMinX, 0f, playAreaMinZ));
        Vector3 maxWorld = _baseTransform.TransformPoint(new Vector3(playAreaMaxX, 0f, playAreaMaxZ));
        BsgIntegrationSettings.MultiplayerZone0PlayAreaWorld = new ZonePlayAreaWorldRect
        {
            minX = Mathf.Min(minWorld.x, maxWorld.x),
            maxX = Mathf.Max(minWorld.x, maxWorld.x),
            minZ = Mathf.Min(minWorld.z, maxWorld.z),
            maxZ = Mathf.Max(minWorld.z, maxWorld.z),
        };
    }

    public void PrepareLayoutFromSceneData(SceneData data)
    {
        ResolveZone0InteriorFromScene();

        if (data == null)
            return;

        _useSplitBandLayout = splitMentalPhysicalLayout;
        if (_useSplitBandLayout)
        {
            var mentalPoints = new List<Vector3>(24);
            var physicalPoints = new List<Vector3>(32);
            CollectSplitBandJsonPositions(data, mentalPoints, physicalPoints);

            if (mentalPoints.Count > 0)
                _mentalBandLayout = ComputeBandLayout(mentalPoints, GetMentalFitBounds(), GetMentalPlacementCenterBase());
            if (physicalPoints.Count > 0)
                _physicalBandLayout = ComputeBandLayout(physicalPoints, GetPhysicalFitBounds(), GetPhysicalPlacementCenterBase());

            Debug.Log($"[MultiplayerRagZone0Anchor] Pre-spawn split fit: mental={mentalPoints.Count} scale={_mentalBandLayout.scale:F3}, physical={physicalPoints.Count} scale={_physicalBandLayout.scale:F3}");
            return;
        }

        var points = new List<Vector3>(64);
        CollectJsonPositions(data, points);
        if (points.Count == 0)
            return;

        ApplyUnifiedLayoutFromPoints(points);
    }

    void ApplyUnifiedLayoutFromPoints(List<Vector3> points)
    {
        Vector3 min = points[0];
        Vector3 max = points[0];
        for (int i = 1; i < points.Count; i++)
        {
            min = Vector3.Min(min, points[i]);
            max = Vector3.Max(max, points[i]);
        }

        _layoutCentroid = (min + max) * 0.5f;
        float jsonWidth = Mathf.Max(max.x - min.x, 1f);
        float jsonDepth = Mathf.Max(max.z - min.z, 1f);

        Bounds fitBounds = GetLayoutFitBounds();
        float scaleX = fitBounds.size.x / jsonWidth;
        float scaleZ = fitBounds.size.z / jsonDepth;
        _layoutScale = Mathf.Min(scaleX, scaleZ) * fitMargin;
        _layoutScale = Mathf.Min(_layoutScale, maxLayoutScale);
        _layoutScale = Mathf.Max(_layoutScale, 0.05f);

        float halfWidth = jsonWidth * _layoutScale * 0.5f;
        float halfDepth = jsonDepth * _layoutScale * 0.5f;
        Vector3 placementCenterBase = GetLayoutPlacementCenterBase();
        Bounds clampBounds = centerRagOnZoneInterior ? _zone0InteriorBounds : _ragPlacementBounds;
        placementCenterBase.x = Mathf.Clamp(
            placementCenterBase.x,
            clampBounds.min.x + halfWidth,
            clampBounds.max.x - halfWidth);
        placementCenterBase.z = Mathf.Clamp(
            placementCenterBase.z,
            clampBounds.min.z + halfDepth,
            clampBounds.max.z - halfDepth);
        _layoutOffsetAnchorLocal = transform.InverseTransformPoint(_baseTransform.TransformPoint(placementCenterBase));
        _layoutOffsetAnchorLocal.y = 0f;

        Debug.Log($"[MultiplayerRagZone0Anchor] Pre-spawn fit: jsonSize=({jsonWidth:F1}x{jsonDepth:F1}), scale={_layoutScale:F3}, centroid={_layoutCentroid}");
    }

    static void CollectSplitBandJsonPositions(SceneData data, List<Vector3> mentalPoints, List<Vector3> physicalPoints)
    {
        if (data.initialStates == null)
            return;

        foreach (var kvp in data.initialStates)
        {
            ToolState t = kvp.Value;
            if (t?.position == null)
                continue;

            Vector3 p = new Vector3(t.position.x, t.position.y, t.position.z);
            if (IsMentalLayoutObjectId(kvp.Key, t))
                mentalPoints.Add(p);
            else
                physicalPoints.Add(p);
        }

        if (data.agentProfiles != null)
        {
            foreach (var kvp in data.agentProfiles)
            {
                AgentProfile profile = kvp.Value;
                if (profile?.position == null)
                    continue;

                Vector3 p = new Vector3(profile.position.x, profile.position.y, profile.position.z);
                if (IsMentalLayoutObjectId(kvp.Key, null))
                    mentalPoints.Add(p);
                else
                    physicalPoints.Add(p);
            }
        }
    }

    static bool IsMentalLayoutObjectId(string objectId, ToolState state)
    {
        if (state != null && state.isCognitiveStation)
            return true;
        if (string.IsNullOrWhiteSpace(objectId))
            return false;

        string id = objectId.Trim();
        if (id.StartsWith("cognitive_", System.StringComparison.OrdinalIgnoreCase))
            return true;

        if (id.StartsWith("M", System.StringComparison.OrdinalIgnoreCase)
            && !id.StartsWith("Machine", System.StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    Bounds GetMentalFitBounds()
    {
        Bounds fit = _zone0InteriorBounds;
        float centerZ = Mathf.Min(
            _zone0InteriorBounds.center.z + layoutCenterOffset.y + cognitiveBandBiasZ,
            _zone0InteriorBounds.max.z - wallInteriorMargin - 1.5f);
        float minZ = _zone0InteriorBounds.center.z + layoutCenterOffset.y;
        fit.min = new Vector3(fit.min.x, fit.min.y, minZ);
        fit.max = new Vector3(fit.max.x, fit.max.y, centerZ + 4f);
        return fit;
    }

    Bounds GetPhysicalFitBounds()
    {
        float centerZ = _zone0InteriorBounds.min.z + physicalBandInsetFromBackWall + physicalBandDepth * 0.5f;
        Bounds fit = _zone0InteriorBounds;
        fit.min = new Vector3(fit.min.x, fit.min.y, centerZ - physicalBandDepth * 0.5f);
        fit.max = new Vector3(fit.max.x, fit.max.y, centerZ + physicalBandDepth * 0.5f);
        return fit;
    }

    Vector3 GetMentalPlacementCenterBase()
    {
        return new Vector3(
            _zone0InteriorBounds.center.x + layoutCenterOffset.x,
            0f,
            Mathf.Min(
                _zone0InteriorBounds.center.z + layoutCenterOffset.y + cognitiveBandBiasZ,
                _zone0InteriorBounds.max.z - wallInteriorMargin - 1.5f));
    }

    Vector3 GetPhysicalPlacementCenterBase()
    {
        return new Vector3(
            _zone0InteriorBounds.center.x + layoutCenterOffset.x,
            0f,
            _zone0InteriorBounds.min.z + physicalBandInsetFromBackWall + physicalBandDepth * 0.5f);
    }

    /// <summary>Base-local XZ for the designated Photon physical agent — near physical targets, not the cognitive band.</summary>
    public Vector2 GetDesignatedPhysicalAgentSpawnBaseLocal()
    {
        ResolveZone0InteriorFromScene();

        if (designatedPhysicalAgentSpawnBaseLocal.sqrMagnitude > 0.01f)
            return ClampBaseLocalToZoneInterior(designatedPhysicalAgentSpawnBaseLocal);

        return ComputeAutoDesignatedPhysicalAgentSpawnBaseLocal();
    }

    Vector2 ComputeAutoDesignatedPhysicalAgentSpawnBaseLocal()
    {
        Vector3 physicalCenter = GetPhysicalPlacementCenterBase();
        float backZ = _zone0InteriorBounds.min.z + physicalBandInsetFromBackWall;
        float frontZ = backZ + physicalBandDepth;

        if (TryGetPhysicalTargetClusterCentroidBaseLocal(out Vector2 clusterCentroid))
        {
            float spawnZ = Mathf.Clamp(clusterCentroid.y + physicalBandDepth * 0.12f, backZ + 1.25f, frontZ - 0.85f);
            return ClampBaseLocalToZoneInterior(new Vector2(clusterCentroid.x, spawnZ));
        }

        float spawnZFallback = physicalCenter.z + physicalBandDepth * 0.32f;
        spawnZFallback = Mathf.Clamp(spawnZFallback, backZ + 1.25f, frontZ - 0.85f);
        return ClampBaseLocalToZoneInterior(new Vector2(physicalCenter.x, spawnZFallback));
    }

    bool TryGetPhysicalTargetClusterCentroidBaseLocal(out Vector2 centroid)
    {
        centroid = Vector2.zero;
        if (_baseTransform == null)
            _baseTransform = transform.parent;
        if (_baseTransform == null)
            return false;

        Vector2 sum = Vector2.zero;
        int count = 0;

        foreach (DeclarativeObjectMetadata meta in FindObjectsOfType<DeclarativeObjectMetadata>())
        {
            if (meta == null || IsMentalClusterObject(meta.gameObject))
                continue;
            if (!IsPhysicalClusterObject(meta.gameObject))
                continue;

            Vector2 bl = WorldToBaseLocalXZ(meta.transform.position);
            sum += bl;
            count++;
        }

        Transform ragRoot = GetOrCreateRagWorldRoot();
        if (ragRoot != null)
        {
            foreach (Transform child in ragRoot.GetComponentsInChildren<Transform>(true))
            {
                if (child == null || child == ragRoot)
                    continue;
                GameObject go = child.gameObject;
                if (!go.name.StartsWith("Tool_", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (IsMentalClusterObject(go))
                    continue;

                Vector2 bl = WorldToBaseLocalXZ(go.transform.position);
                sum += bl;
                count++;
            }
        }

        if (count < 1)
            return false;

        centroid = sum / count;
        return true;
    }

    public bool TryGetDesignatedPhysicalAgentSpawnWorld(out Vector3 worldPos, float worldY = 0f)
    {
        worldPos = Vector3.zero;
        if (_baseTransform == null)
            _baseTransform = transform.parent;
        if (_baseTransform == null)
            return false;

        Vector2 baseLocal = GetDesignatedPhysicalAgentSpawnBaseLocal();
        worldPos = BaseLocalXZToWorld(baseLocal.x, baseLocal.y, worldY);
        worldPos = NudgeSpawnClearOfCognitiveStations(worldPos);
        return true;
    }

    Vector3 NudgeSpawnClearOfCognitiveStations(Vector3 worldPos)
    {
        float playerRadius = Mathf.Max(0.35f, designatedPhysicalPlayerScale * 0.32f);
        float playerHeight = Mathf.Max(1.5f, designatedPhysicalPlayerScale * 0.95f);
        Vector3 physicalBandDir = GetPhysicalPlacementCenterBase() - GetMentalPlacementCenterBase();
        physicalBandDir.y = 0f;
        if (physicalBandDir.sqrMagnitude < 0.01f)
            physicalBandDir = Vector3.back;
        physicalBandDir.Normalize();

        for (int attempt = 0; attempt < 12; attempt++)
        {
            if (!IsOverlappingCognitiveStation(worldPos, playerRadius, playerHeight))
                return worldPos;

            worldPos += physicalBandDir * (playerRadius * 1.15f);
            worldPos = BaseLocalXZToWorld(
                WorldToBaseLocalXZ(worldPos).x,
                WorldToBaseLocalXZ(worldPos).y,
                worldPos.y);
            Vector2 clamped = ClampBaseLocalToZoneInterior(WorldToBaseLocalXZ(worldPos));
            worldPos = BaseLocalXZToWorld(clamped.x, clamped.y, worldPos.y);
        }

        return worldPos;
    }

    static bool IsOverlappingCognitiveStation(Vector3 feetWorld, float radius, float height)
    {
        Vector3 p1 = feetWorld + Vector3.up * radius;
        Vector3 p2 = feetWorld + Vector3.up * Mathf.Max(height - radius, radius * 2f);
        Collider[] hits = Physics.OverlapCapsule(p1, p2, radius, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i];
            if (c == null || c.isTrigger)
                continue;
            if (c.GetComponentInParent<CognitiveStationInteractable>() != null)
                return true;
            if (c.transform != null && c.transform.name == "CognitiveNavObstacle")
                return true;
        }

        return false;
    }

    public Quaternion GetDesignatedPhysicalAgentSpawnWorldRotation()
    {
        if (_baseTransform == null)
            _baseTransform = transform.parent;
        if (_baseTransform == null)
            return Quaternion.identity;

        if (!TryGetDesignatedPhysicalAgentSpawnWorld(out Vector3 spawnWorld, 0f))
            return Quaternion.identity;

        Vector3 lookTarget = GetMentalPlacementCenterBase();
        lookTarget = _baseTransform.TransformPoint(lookTarget);
        Vector3 dir = lookTarget - spawnWorld;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f)
            return Quaternion.identity;
        return Quaternion.LookRotation(dir.normalized, Vector3.up);
    }

    BandLayoutData ComputeBandLayout(List<Vector3> points, Bounds fitBounds, Vector3 placementCenterBase)
    {
        var layout = new BandLayoutData();
        if (points == null || points.Count == 0 || fitBounds.size.sqrMagnitude < 0.01f)
            return layout;

        Vector3 min = points[0];
        Vector3 max = points[0];
        for (int i = 1; i < points.Count; i++)
        {
            min = Vector3.Min(min, points[i]);
            max = Vector3.Max(max, points[i]);
        }

        layout.centroid = (min + max) * 0.5f;
        float jsonWidth = Mathf.Max(max.x - min.x, 1f);
        float jsonDepth = Mathf.Max(max.z - min.z, 1f);

        float scaleX = fitBounds.size.x / jsonWidth;
        float scaleZ = fitBounds.size.z / jsonDepth;
        layout.scale = Mathf.Min(scaleX, scaleZ) * fitMargin;
        layout.scale = Mathf.Min(layout.scale, maxLayoutScale);
        layout.scale = Mathf.Max(layout.scale, 0.05f);

        float halfWidth = jsonWidth * layout.scale * 0.5f;
        float halfDepth = jsonDepth * layout.scale * 0.5f;
        Bounds clampBounds = centerRagOnZoneInterior ? _zone0InteriorBounds : _ragPlacementBounds;
        placementCenterBase.x = Mathf.Clamp(
            placementCenterBase.x,
            clampBounds.min.x + halfWidth,
            clampBounds.max.x - halfWidth);
        placementCenterBase.z = Mathf.Clamp(
            placementCenterBase.z,
            clampBounds.min.z + halfDepth,
            clampBounds.max.z - halfDepth);

        layout.offsetAnchorLocal = transform.InverseTransformPoint(_baseTransform.TransformPoint(placementCenterBase));
        layout.offsetAnchorLocal.y = 0f;
        layout.valid = true;
        return layout;
    }

    static void CollectJsonPositions(SceneData data, List<Vector3> points)
    {
        if (data.agentProfiles != null)
        {
            foreach (var kvp in data.agentProfiles)
            {
                AgentProfile p = kvp.Value;
                if (p?.position == null) continue;
                points.Add(new Vector3(p.position.x, p.position.y, p.position.z));
            }
        }

        if (data.initialStates != null)
        {
            foreach (var kvp in data.initialStates)
            {
                ToolState t = kvp.Value;
                if (t?.position == null) continue;
                points.Add(new Vector3(t.position.x, t.position.y, t.position.z));
            }
        }
    }

    public void RefreshExclusionBounds()
    {
        _exclusionBounds.Clear();

        if (!centerRagOnZoneInterior && excludeOverlapBounds.size.sqrMagnitude > 0.01f)
        {
            Vector3 excludeCenter = _baseTransform != null
                ? _baseTransform.TransformPoint(excludeOverlapBounds.center)
                : excludeOverlapBounds.center;
            _exclusionBounds.Add(new Bounds(excludeCenter, excludeOverlapBounds.size));
        }

        if (!autoCollectSpawnExclusion)
            return;

        PlayerSpawner spawner = FindObjectOfType<PlayerSpawner>();
        if (spawner == null)
            return;

        var points = new List<Vector3>(8);
        CollectSpawnPoints(spawner, points);
        if (points.Count == 0)
            return;

        Vector3 min = points[0];
        Vector3 max = points[0];
        for (int i = 1; i < points.Count; i++)
        {
            min = Vector3.Min(min, points[i]);
            max = Vector3.Max(max, points[i]);
        }

        float pad = playerSpawnPadding;
        Vector3 center = (min + max) * 0.5f;
        center.y = 0.5f;
        Vector3 size = max - min;
        size.y = 2.5f;
        size.x = Mathf.Max(size.x + pad * 2f, pad * 2f);
        size.z = Mathf.Max(size.z + pad * 2f, pad * 2f);
        _exclusionBounds.Add(new Bounds(center, size));
    }

    static void CollectSpawnPoints(PlayerSpawner spawner, List<Vector3> points)
    {
        if (spawner == null) return;
        Transform root = spawner.transform;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null) continue;
            if (child.name.IndexOf("spawn", System.StringComparison.OrdinalIgnoreCase) >= 0)
                points.Add(child.position);
        }
    }

    public Vector3 MapJsonToWorld(string objectId, Vector3 jsonLocalPosition)
    {
        jsonLocalPosition = ApplyModuleBufferGapInJsonSpace(objectId, jsonLocalPosition);

        if (_useSplitBandLayout)
        {
            BandLayoutData band = IsMentalLayoutObjectId(objectId, null) ? _mentalBandLayout : _physicalBandLayout;
            if (band.valid)
                return MapJsonToWorldWithBand(jsonLocalPosition, band);
        }

        return MapJsonToWorldWithBand(jsonLocalPosition, new BandLayoutData
        {
            centroid = _layoutCentroid,
            scale = _layoutScale,
            offsetAnchorLocal = _layoutOffsetAnchorLocal,
            valid = true,
        });
    }

    Vector3 MapJsonToWorldWithBand(Vector3 jsonLocalPosition, BandLayoutData band)
    {
        Vector3 centered = new Vector3(
            jsonLocalPosition.x - band.centroid.x,
            jsonLocalPosition.y,
            jsonLocalPosition.z - band.centroid.z);
        Vector3 scaled = centered * band.scale;
        scaled.y = jsonLocalPosition.y * band.scale;
        Vector3 anchorLocal = scaled + band.offsetAnchorLocal;
        return transform.TransformPoint(anchorLocal);
    }

    Vector3 ApplyModuleBufferGapInJsonSpace(string objectId, Vector3 jsonLocal)
    {
        if (cognitiveModuleBufferGapMultiplier <= 1.001f || !IsMentalLayoutObjectId(objectId, null))
            return jsonLocal;

        if (!TryParseCognitiveNumericIndex(objectId, out int idx) || idx < 7)
            return jsonLocal;

        float delta = jsonLocal.z - cognitiveModuleRowReferenceZ;
        if (Mathf.Abs(delta) < 0.01f)
            return jsonLocal;

        jsonLocal.z = cognitiveModuleRowReferenceZ + delta * cognitiveModuleBufferGapMultiplier;
        return jsonLocal;
    }

    static bool TryParseCognitiveNumericIndex(string objectId, out int index)
    {
        index = 0;
        if (string.IsNullOrWhiteSpace(objectId))
            return false;

        string id = objectId.Trim();
        const string prefix = "cognitive_";
        if (!id.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
            return false;

        string tail = id.Substring(prefix.Length);
        int zoneIdx = tail.IndexOf("_zone", System.StringComparison.OrdinalIgnoreCase);
        if (zoneIdx > 0)
            tail = tail.Substring(0, zoneIdx);

        return int.TryParse(tail, out index) && index > 0;
    }

    public Transform GetOrCreateRagWorldRoot()
    {
        if (_ragWorldRoot != null)
            return _ragWorldRoot;

        Transform existing = transform.Find("RAG_WorldRoot");
        if (existing != null)
        {
            _ragWorldRoot = existing;
            return _ragWorldRoot;
        }

        var go = new GameObject("RAG_WorldRoot");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        _ragWorldRoot = go.transform;
        return _ragWorldRoot;
    }

    public void ApplyPlayAreaToZoneAgents()
    {
        UpdatePlayAreaFromRagBounds();

        ZonePlayAreaWorldRect? area = BsgIntegrationSettings.MultiplayerZone0PlayAreaWorld;
        if (!area.HasValue || !area.Value.IsValid)
            return;

        ZonePlayAreaWorldRect r = area.Value;
        foreach (var ml in FindObjectsOfType<BSGMLAgent>(true))
        {
            if (ml == null || ml.zoneIndex != 0)
                continue;

            ml.constrainToPlayArea = true;
            ml.playAreaMinX = r.minX;
            ml.playAreaMaxX = r.maxX;
            ml.playAreaMinZ = r.minZ;
            ml.playAreaMaxZ = r.maxZ;
        }
    }

    public bool ValidateOverlapAndNudge(IEnumerable<GameObject> spawnedObjects)
    {
        if (spawnedObjects == null)
            return false;

        RefreshExclusionBounds();
        Transform root = GetOrCreateRagWorldRoot();
        root.localPosition = Vector3.zero;
        root.localScale = Vector3.one;

        bool adjusted = false;
        float rootScale = 1f;

        if (splitMentalPhysicalLayout && ApplyMentalPhysicalSplitLayout(spawnedObjects))
            adjusted = true;

        for (int attempt = 0; attempt < 24; attempt++)
        {
            root.localScale = Vector3.one * rootScale;
            ClampRootTranslationToBounds(spawnedObjects, root);

            if (ClusterFitsPlacement(spawnedObjects))
            {
                if (adjusted)
                    Debug.Log($"[MultiplayerRagZone0Anchor] Cluster clamped inside zone 0 (rootScale={rootScale:F2}, rootLocal={root.localPosition}).");
                SyncMultiplayerPlayAreaToSettings();
                return adjusted;
            }

            if (rootScale > minRootScale + 0.02f)
            {
                rootScale = Mathf.Max(minRootScale, rootScale - 0.05f);
                adjusted = true;
                continue;
            }

            if (TryMinimalTranslateToFit(spawnedObjects, root))
            {
                adjusted = true;
                continue;
            }

            break;
        }

        if (!ClusterInsideBounds(spawnedObjects, _zone0InteriorBounds))
            Debug.LogWarning("[MultiplayerRagZone0Anchor] RAG cluster still outside zone 0 walls after clamp — reduce content or widen zone.");
        else if (splitMentalPhysicalLayout && ApplyMentalPhysicalSplitLayout(spawnedObjects))
            adjusted = true;

        return adjusted;
    }

    bool ApplyMentalPhysicalSplitLayout(IEnumerable<GameObject> spawnedObjects)
    {
        ResolveZone0InteriorFromScene();

        var mentalCluster = new List<Transform>(24);
        var physicalCluster = new List<Transform>(24);
        ClassifySpawnedObjects(spawnedObjects, mentalCluster, physicalCluster);

        if (mentalCluster.Count == 0 && physicalCluster.Count == 0)
            return false;

        Vector2 mentalTarget = new Vector2(
            _zone0InteriorBounds.center.x + layoutCenterOffset.x,
            Mathf.Min(
                _zone0InteriorBounds.center.z + layoutCenterOffset.y + cognitiveBandBiasZ,
                _zone0InteriorBounds.max.z - wallInteriorMargin - 1.5f));
        Vector2 physicalTarget = new Vector2(
            _zone0InteriorBounds.center.x + layoutCenterOffset.x,
            _zone0InteriorBounds.min.z + physicalBandInsetFromBackWall + physicalBandDepth * 0.5f);

        bool moved = false;
        if (mentalCluster.Count > 0)
            moved |= TranslateClusterToTargetBaseLocal(mentalCluster, mentalTarget);
        if (physicalCluster.Count > 0)
        {
            moved |= TranslateClusterToTargetBaseLocal(physicalCluster, physicalTarget);
            moved |= SpreadClusterInBandBaseLocal(
                physicalCluster,
                physicalTarget,
                physicalBandHorizontalSpread,
                physicalBandDepthSpread);
        }

        return moved;
    }

    static void ClassifySpawnedObjects(
        IEnumerable<GameObject> spawnedObjects,
        List<Transform> mentalCluster,
        List<Transform> physicalCluster)
    {
        foreach (GameObject go in spawnedObjects)
        {
            if (go == null)
                continue;

            if (IsMentalClusterObject(go))
                mentalCluster.Add(go.transform);
            else if (IsPhysicalClusterObject(go))
                physicalCluster.Add(go.transform);
        }
    }

    static bool IsMentalClusterObject(GameObject go)
    {
        string name = go.name ?? string.Empty;
        if (name.IndexOf("cognitive_", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        BSGMLAgent ml = go.GetComponent<BSGMLAgent>();
        if (ml != null && ml.agentRole == BSGMLAgent.AgentRole.Mental)
            return true;

        if (name.IndexOf("Agent_M", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return false;
    }

    static bool IsPhysicalClusterObject(GameObject go)
    {
        if (IsMentalClusterObject(go))
            return false;

        string name = go.name ?? string.Empty;
        if (name.StartsWith("Tool_", System.StringComparison.OrdinalIgnoreCase))
            return true;

        BSGMLAgent ml = go.GetComponent<BSGMLAgent>();
        if (ml != null && ml.agentRole == BSGMLAgent.AgentRole.Physical)
            return true;

        if (name.IndexOf("Agent_P", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return false;
    }

    bool TranslateClusterToTargetBaseLocal(List<Transform> cluster, Vector2 targetBaseLocal)
    {
        if (!TryGetTransformsCentroidBaseLocal(cluster, out Vector2 centroid))
            return false;

        Vector2 delta = targetBaseLocal - centroid;
        if (delta.sqrMagnitude < 0.0001f)
            return false;

        Vector3 worldDelta = _baseTransform.TransformVector(new Vector3(delta.x, 0f, delta.y));
        for (int i = 0; i < cluster.Count; i++)
            MoveTransformXZ(cluster[i], worldDelta);

        return true;
    }

    bool SpreadClusterInBandBaseLocal(
        List<Transform> cluster,
        Vector2 anchor,
        float spreadX,
        float spreadZ)
    {
        if (cluster.Count < 2)
            return false;

        if (Mathf.Abs(spreadX - 1f) < 0.01f && Mathf.Abs(spreadZ - 1f) < 0.01f)
            return false;

        bool moved = false;
        for (int i = 0; i < cluster.Count; i++)
        {
            Transform t = cluster[i];
            if (t == null)
                continue;

            Vector2 local = WorldToBaseLocalXZ(t.position);
            float offsetX = local.x - anchor.x;
            float offsetZ = local.y - anchor.y;
            float targetX = anchor.x + offsetX * spreadX;
            float targetZ = anchor.y + offsetZ * spreadZ;
            if (Mathf.Abs(targetX - local.x) < 0.0001f && Mathf.Abs(targetZ - local.y) < 0.0001f)
                continue;

            Vector3 world = _baseTransform.TransformPoint(new Vector3(targetX, 0f, targetZ));
            Vector3 pos = t.position;
            pos.x = world.x;
            pos.z = world.z;
            t.position = pos;
            moved = true;
        }

        return moved;
    }

    bool SpreadClusterHorizontallyBaseLocal(List<Transform> cluster, float anchorX, float spreadMultiplier)
    {
        if (cluster.Count < 2 || Mathf.Abs(spreadMultiplier - 1f) < 0.01f)
            return false;

        bool moved = false;
        for (int i = 0; i < cluster.Count; i++)
        {
            Transform t = cluster[i];
            if (t == null)
                continue;

            Vector2 local = WorldToBaseLocalXZ(t.position);
            float offsetX = local.x - anchorX;
            float targetX = anchorX + offsetX * spreadMultiplier;
            if (Mathf.Abs(targetX - local.x) < 0.0001f)
                continue;

            Vector3 world = _baseTransform.TransformPoint(new Vector3(targetX, 0f, local.y));
            Vector3 pos = t.position;
            pos.x = world.x;
            t.position = pos;
            moved = true;
        }

        return moved;
    }

    bool TryGetTransformsCentroidBaseLocal(List<Transform> cluster, out Vector2 centroid)
    {
        centroid = Vector2.zero;
        int count = 0;
        for (int i = 0; i < cluster.Count; i++)
        {
            Transform t = cluster[i];
            if (t == null)
                continue;

            centroid += WorldToBaseLocalXZ(t.position);
            count++;
        }

        if (count == 0)
            return false;

        centroid /= count;
        return true;
    }

    void MoveTransformXZ(Transform target, Vector3 worldDelta)
    {
        if (target == null)
            return;

        Vector3 pos = target.position + worldDelta;
        Rigidbody rb = target.GetComponent<Rigidbody>();
        if (rb != null)
        {
            bool wasKinematic = rb.isKinematic;
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = pos;
            rb.isKinematic = wasKinematic;
        }
        else
        {
            target.position = pos;
        }
    }

    bool ClusterFitsPlacement(IEnumerable<GameObject> spawnedObjects)
    {
        if (!ClusterInsideBounds(spawnedObjects, _zone0InteriorBounds))
            return false;
        if (!centerRagOnZoneInterior && !ClusterInsideBounds(spawnedObjects, _ragPlacementBounds))
            return false;
        return true;
    }

    bool TryMinimalTranslateToFit(IEnumerable<GameObject> spawnedObjects, Transform root)
    {
        if (!TryGetClusterBoundsBaseLocal(spawnedObjects, out Vector3 min, out Vector3 max))
            return false;

        Bounds placementBounds = centerRagOnZoneInterior ? _zone0InteriorBounds : _ragPlacementBounds;
        Vector3 shift = Vector3.zero;
        if (max.x > placementBounds.max.x)
            shift.x += placementBounds.max.x - max.x;
        if (min.x < placementBounds.min.x)
            shift.x += placementBounds.min.x - min.x;
        if (max.z > placementBounds.max.z)
            shift.z += placementBounds.max.z - max.z;
        if (min.z < placementBounds.min.z)
            shift.z += placementBounds.min.z - min.z;

        if (max.x + shift.x > _zone0InteriorBounds.max.x)
            shift.x += _zone0InteriorBounds.max.x - (max.x + shift.x);
        if (min.x + shift.x < _zone0InteriorBounds.min.x)
            shift.x += _zone0InteriorBounds.min.x - (min.x + shift.x);
        if (max.z + shift.z > _zone0InteriorBounds.max.z)
            shift.z += _zone0InteriorBounds.max.z - (max.z + shift.z);
        if (min.z + shift.z < _zone0InteriorBounds.min.z)
            shift.z += _zone0InteriorBounds.min.z - (min.z + shift.z);

        if (shift.sqrMagnitude < 0.0001f)
            return false;

        root.localPosition += new Vector3(shift.x, 0f, shift.z);
        return true;
    }

    void ClampRootTranslationToBounds(IEnumerable<GameObject> spawnedObjects, Transform root)
    {
        for (int i = 0; i < 8; i++)
        {
            if (!TryGetClusterBoundsBaseLocal(spawnedObjects, out Vector3 min, out Vector3 max))
                return;

            Vector3 shift = Vector3.zero;
            if (max.x > _zone0InteriorBounds.max.x)
                shift.x = _zone0InteriorBounds.max.x - max.x;
            else if (min.x < _zone0InteriorBounds.min.x)
                shift.x = _zone0InteriorBounds.min.x - min.x;

            if (max.z > _zone0InteriorBounds.max.z)
                shift.z = _zone0InteriorBounds.max.z - max.z;
            else if (min.z < _zone0InteriorBounds.min.z)
                shift.z = _zone0InteriorBounds.min.z - min.z;

            if (shift.sqrMagnitude < 0.0001f)
                break;

            root.localPosition += new Vector3(shift.x, 0f, shift.z);
        }
    }

    bool ClusterInsideBounds(IEnumerable<GameObject> spawnedObjects, Bounds boundsBaseLocal)
    {
        if (!TryGetClusterBoundsBaseLocal(spawnedObjects, out Vector3 min, out Vector3 max))
            return true;

        return min.x >= boundsBaseLocal.min.x && max.x <= boundsBaseLocal.max.x
            && min.z >= boundsBaseLocal.min.z && max.z <= boundsBaseLocal.max.z;
    }

    bool TryGetClusterBoundsBaseLocal(IEnumerable<GameObject> spawnedObjects, out Vector3 min, out Vector3 max)
    {
        min = max = Vector3.zero;
        bool any = false;

        foreach (GameObject go in spawnedObjects)
        {
            if (go == null) continue;

            foreach (Collider col in go.GetComponentsInChildren<Collider>(true))
            {
                if (col == null || !col.enabled) continue;
                Bounds b = col.bounds;
                Vector3 cMin = WorldToBaseLocal(b.min);
                Vector3 cMax = WorldToBaseLocal(b.max);
                Vector3 bMin = Vector3.Min(cMin, cMax);
                Vector3 bMax = Vector3.Max(cMin, cMax);
                if (!any)
                {
                    min = bMin;
                    max = bMax;
                    any = true;
                }
                else
                {
                    min = Vector3.Min(min, bMin);
                    max = Vector3.Max(max, bMax);
                }
            }

            Vector3 p = WorldToBaseLocal(go.transform.position);
            if (!any)
            {
                min = max = p;
                any = true;
            }
            else
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }

        return any;
    }

    bool AnySpawnedObjectOverlaps(IEnumerable<GameObject> spawnedObjects)
    {
        foreach (GameObject go in spawnedObjects)
        {
            if (go != null && IntersectsAnyExclusion(go))
                return true;
        }
        return false;
    }

    bool IntersectsAnyExclusion(GameObject go)
    {
        for (int i = 0; i < _exclusionBounds.Count; i++)
        {
            Bounds worldBounds = _exclusionBounds[i];
            foreach (Collider col in go.GetComponentsInChildren<Collider>(true))
            {
                if (col == null || !col.enabled) continue;
                if (col.bounds.Intersects(worldBounds))
                    return true;
            }

            if (worldBounds.Contains(go.transform.position))
                return true;
        }
        return false;
    }

    Vector3 WorldToBaseLocal(Vector3 world)
    {
        if (_baseTransform == null)
            _baseTransform = transform.parent;
        return _baseTransform != null
            ? _baseTransform.InverseTransformPoint(world)
            : world;
    }

    void OnDrawGizmosSelected()
    {
        Transform baseTf = transform.parent;
        if (baseTf == null) return;

        Gizmos.matrix = baseTf.localToWorldMatrix;

        Bounds zone0 = Application.isPlaying ? _zone0InteriorBounds : ComputeEditorZone0Bounds();
        Gizmos.color = new Color(0.1f, 0.85f, 0.2f, 0.35f);
        Gizmos.DrawWireCube(zone0.center, zone0.size);

        Bounds rag = Application.isPlaying ? _ragPlacementBounds : zone0;
        Gizmos.color = new Color(0.2f, 0.5f, 1f, 0.35f);
        Gizmos.DrawWireCube(rag.center, rag.size);

        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.35f);
        Gizmos.DrawWireCube(excludeOverlapBounds.center, excludeOverlapBounds.size);

        if (autoCollectSpawnExclusion && Application.isPlaying && _exclusionBounds.Count > 1)
        {
            Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.35f);
            Gizmos.DrawWireCube(_exclusionBounds[1].center, _exclusionBounds[1].size);
        }
    }

    Bounds ComputeEditorZone0Bounds()
    {
        float leftX = -15f, rightX = 15f, backZ = -25f, dividerZ = 0f;
        Transform left = baseTfFind("Left");
        Transform right = baseTfFind("Right");
        Transform back = baseTfFind("Back");
        Transform divider = baseTfFind("Bifurcation");
        if (left != null) leftX = left.localPosition.x;
        if (right != null) rightX = right.localPosition.x;
        if (back != null) backZ = back.localPosition.z;
        if (divider != null) dividerZ = divider.localPosition.z;
        float m = wallInteriorMargin;
        return new Bounds(
            new Vector3((leftX + rightX) * 0.5f, 0.5f, (backZ + dividerZ) * 0.5f),
            new Vector3(Mathf.Abs(rightX - leftX) - m * 2f, 2f, Mathf.Abs(dividerZ - backZ) - m * 2f));
    }

    Transform baseTfFind(string name)
    {
        Transform baseTf = transform.parent;
        return baseTf != null ? baseTf.Find(name) : null;
    }
}
