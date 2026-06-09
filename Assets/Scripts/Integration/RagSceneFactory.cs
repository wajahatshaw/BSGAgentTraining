using UnityEngine;

/// <summary>
/// Spawns the BSG RAG runtime stack (REPLICA_SceneManager) used by JSONWorkflowSceneML.
/// Shared by solo ML scenes and the zone-0 embed in ProtoypeSceneMultiplayer.
/// </summary>
public static class RagSceneFactory
{
    public const string DefaultJsonFileName = "basicUI_ml2.json";
    public const string ReplicaRootName = "REPLICA_SceneManager";

    public struct RagSceneOptions
    {
        public string jsonFileName;
        public bool enableMlTrainingInRagMode;
        public bool enableMlTrainingForCognitiveAgents;
        public int trainZonesMask;
        public bool ragOnlyMode;
        public bool multiplayerEmbedMode;
        public int spawnZonesMask;
        public bool skipEnvironmentGeneration;
        public bool useSceneAnchorLayout;
        public Vector3 ragWorldOrigin;
    }

    public static RagSceneOptions DefaultTrainingOptions => new RagSceneOptions
    {
        jsonFileName = DefaultJsonFileName,
        enableMlTrainingInRagMode = true,
        enableMlTrainingForCognitiveAgents = true,
        trainZonesMask = 15,
        ragOnlyMode = true,
        multiplayerEmbedMode = false,
        spawnZonesMask = 0,
        skipEnvironmentGeneration = false,
        useSceneAnchorLayout = false,
        ragWorldOrigin = Vector3.zero,
    };

    /// <summary>Multiplayer: zone 0 only via scene anchor; no BSG HUD; keep player camera.</summary>
    public static RagSceneOptions DefaultMultiplayerOptions => new RagSceneOptions
    {
        jsonFileName = DefaultJsonFileName,
        enableMlTrainingInRagMode = false,
        enableMlTrainingForCognitiveAgents = false,
        trainZonesMask = 0,
        ragOnlyMode = true,
        multiplayerEmbedMode = true,
        spawnZonesMask = 1,
        skipEnvironmentGeneration = true,
        useSceneAnchorLayout = true,
        ragWorldOrigin = Vector3.zero,
    };

    public static GameObject EnsureReplicaSceneManager(RagSceneOptions options)
    {
        ReplicaSceneSetup existing = Object.FindObjectOfType<ReplicaSceneSetup>();
        if (existing != null)
        {
            ApplyOptions(existing, options);
            return existing.gameObject;
        }

        GameObject root = new GameObject(ReplicaRootName);
        root.AddComponent<SkillBasedActionSystem>();
        var loader = root.AddComponent<SceneUILoader>();
        var setup = root.AddComponent<ReplicaSceneSetup>();

        if (!options.multiplayerEmbedMode)
            root.AddComponent<TrainingSpeedIndicator>();

        loader.jsonFileName = string.IsNullOrEmpty(options.jsonFileName)
            ? DefaultJsonFileName
            : options.jsonFileName;

        ApplyOptions(setup, options);
        Debug.Log($"[RagSceneFactory] Created {ReplicaRootName} — json={setup.jsonFileName}, " +
                  $"embed={setup.multiplayerEmbedMode}, zonesMask={setup.spawnZonesMask}, " +
                  $"anchorLayout={setup.useSceneAnchorLayout}, mlTraining={setup.enableMlTrainingInRagMode}");
        return root;
    }

    static void ApplyOptions(ReplicaSceneSetup setup, RagSceneOptions options)
    {
        if (setup == null) return;

        if (!string.IsNullOrEmpty(options.jsonFileName))
            setup.jsonFileName = options.jsonFileName;

        setup.ragOnlyMode = options.ragOnlyMode;
        setup.singleZoneMode = false;
        setup.multiplayerEmbedMode = options.multiplayerEmbedMode;
        setup.spawnZonesMask = options.spawnZonesMask;
        setup.skipEnvironmentGeneration = options.skipEnvironmentGeneration;
        setup.useSceneAnchorLayout = options.useSceneAnchorLayout;
        setup.ragWorldOrigin = options.ragWorldOrigin;
        setup.enableMlTrainingInRagMode = options.enableMlTrainingInRagMode;
        setup.enableMlTrainingForCognitiveAgents = options.enableMlTrainingForCognitiveAgents;
        setup.trainZonesMask = options.trainZonesMask;
        setup.setupOnStart = true;

        SceneUILoader loader = setup.GetComponent<SceneUILoader>();
        if (loader != null)
        {
            if (!string.IsNullOrEmpty(options.jsonFileName))
                loader.jsonFileName = options.jsonFileName;
            loader.spawnZonesMask = options.spawnZonesMask;
            if (options.spawnZonesMask != 0 && loader.sceneData != null)
                loader.FilterSceneDataBySpawnMask();
        }

        BsgIntegrationSettings.ApplyFromSetup(setup);
    }
}
