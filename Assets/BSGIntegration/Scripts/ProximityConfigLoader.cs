using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System;

[System.Serializable]
public class ProximityZoneData
{
    public string zoneId;
    public string centerObject;
    public float radius;
    public ProximityEffectsData effects;
    public string[] affectedAgents;
}

[System.Serializable]
public class ProximityEffectsData
{
    public ColorChangeEffectData colorChange;
    public DirectionChangeEffectData directionChange;
    public SoundEffectData soundEffect;
}

[System.Serializable]
public class ColorChangeEffectData
{
    public bool enabled;
    public string targetColor;
    public ProximityMaterialColor materialColor;
    public float fadeSpeed;
}

[System.Serializable]
public class ProximityMaterialColor
{
    public float r;
    public float g;
    public float b;
    public float a;
}

[System.Serializable]
public class DirectionChangeEffectData
{
    public bool enabled;
    public float avoidanceForce;
    public float rotationSpeed;
}

[System.Serializable]
public class SoundEffectData
{
    public bool enabled;
    public string soundClip;
    public float volume;
}

[System.Serializable]
public class ProximityDetectionData
{
    public bool enabled;
    public float detectionRadius;
    public float updateFrequency;
    public ProximityZoneData[] proximityZones;
}

[System.Serializable]
public class BasicUiData
{
    public string scene_id;
    public ProximityDetectionData proximityDetection;
    public CognitiveInteractionData cognitiveInteraction;
    // Add other fields as needed
}

[System.Serializable]
public class CognitiveInteractionData
{
    public bool enabled;
    public bool spawnBeforeAgentActions;
    public float proximityRadius;
    public float defaultProximityRadius;
    public string interactionMode;
}

/// <summary>Runs after <see cref="SceneGenerator"/> when possible so zones are built from the same JSON as spawned tools.</summary>
[DefaultExecutionOrder(20)]
public class ProximityConfigLoader : MonoBehaviour
{
    [Header("Configuration")]
    public string jsonFilePath = "Assets/JsonFile/basicUi.json";
    
    [Header("Loaded Data")]
    public ProximityDetectionData loadedProximityConfig;
    
    private ProximityDetectionSystem proximitySystem;
    
    void Start()
    {
        proximitySystem = FindObjectOfType<ProximityDetectionSystem>();
        ResolveJsonPathFromSceneLoader();

        // Multiplayer embed refreshes registries once after SceneGenerator spawn (see ApplyAfterSceneSpawn).
        if (BsgIntegrationSettings.MultiplayerEmbedMode
            || BsgIntegrationSettings.UseSceneAnchorLayout
            || BsgIntegrationSettings.UsePhotonPlayerAsPhysicalAgent)
        {
            return;
        }

        LoadProximityConfiguration();
    }
    
    public void LoadProximityConfiguration()
    {
        string resolvedPath = ResolveJsonFilePath(jsonFilePath);
        if (File.Exists(resolvedPath))
        {
            try
            {
                string jsonContent = File.ReadAllText(resolvedPath);
                Debug.Log($"JSON content length: {jsonContent.Length}");
                
                // Try to parse the JSON - we need to extract just the proximity detection part
                // Since the full JSON might be too complex, let's parse it manually
                LoadProximityFromJsonString(jsonContent);
                
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error loading proximity configuration: {e.Message}");
                Debug.LogError($"Stack trace: {e.StackTrace}");
            }
        }
        else
        {
            Debug.LogError($"JSON file not found at: {resolvedPath}");
        }
    }
    
    void LoadProximityFromJsonString(string jsonContent)
    {
        try
        {
            // Create a simplified proximity config for now
            loadedProximityConfig = new ProximityDetectionData();
            loadedProximityConfig.enabled = true;
            loadedProximityConfig.detectionRadius = 3.0f;
            loadedProximityConfig.updateFrequency = 0.35f;

            float cognitiveRadius = 2.0f;
            BasicUiData parsed = JsonUtility.FromJson<BasicUiData>(jsonContent);
            if (parsed != null)
            {
                if (parsed.proximityDetection != null)
                {
                    loadedProximityConfig.enabled = parsed.proximityDetection.enabled;
                    loadedProximityConfig.detectionRadius = parsed.proximityDetection.detectionRadius > 0f ? parsed.proximityDetection.detectionRadius : loadedProximityConfig.detectionRadius;
                    loadedProximityConfig.updateFrequency = parsed.proximityDetection.updateFrequency > 0f ? parsed.proximityDetection.updateFrequency : loadedProximityConfig.updateFrequency;
                }

                if (parsed.cognitiveInteraction != null)
                {
                    if (parsed.cognitiveInteraction.proximityRadius > 0f)
                        cognitiveRadius = parsed.cognitiveInteraction.proximityRadius;
                    else if (parsed.cognitiveInteraction.defaultProximityRadius > 0f)
                        cognitiveRadius = parsed.cognitiveInteraction.defaultProximityRadius;
                }
            }
            
            // Create basic proximity zones based on the JSON structure we know exists
            cognitiveRadius = ResolveCognitiveProximityRadius(cognitiveRadius);
            CreateDefaultProximityZones(cognitiveRadius);
            
            // Apply configuration to proximity system (registry refresh happens after tools spawn).
            if (proximitySystem != null)
                ApplyConfigurationToSystem();
            
            Debug.Log("Proximity configuration loaded successfully with default zones!");
            LogLoadedConfiguration();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error creating default proximity configuration: {e.Message}");
        }
    }
    
    /// <summary>
    /// Stations: cognitive station keys from <c>initialStates</c> only (fixed placement), excluding misc prop keys.
    /// Agents: one proximity zone per profile in <c>agentProfiles</c> (physical + mental), centered on the spawned <c>Agent_*</c>.
    /// Only objects whose <c>ToolState.isCognitiveStation</c> flag is true receive a proximity zone.
    /// That flag is set by <see cref="SceneUILoader"/> during normalization for any station that
    /// carries an ACT-R layer, a stationType property, or whose key conventionally starts with
    /// "cognitive_" — so generic props (mouse, keyboard, buttons, scenario objects, etc.) are
    /// automatically excluded without any name-based hardcoding.
    /// </summary>
    void CreateDefaultProximityZones(float cognitiveRadius)
    {
        List<ProximityZoneData> zones = new List<ProximityZoneData>();

        List<string> agentIds = new List<string>();
        SceneUILoader loader = FindObjectOfType<SceneUILoader>();
        SceneData data = loader != null ? loader.GetSceneData() : null;
        if (data?.agentProfiles != null)
            agentIds.AddRange(data.agentProfiles.Keys);

        string[] allAgentsOrFallback = agentIds.Count > 0
            ? agentIds.ToArray()
            : new[]
            {
                "agent_technician_A", "agent_supervisor_B", "agent_inspector_C",
                "SIMPLE_Technician_01", "SIMPLE_Technician_02", "SIMPLE_Supervisor_01", "SIMPLE_Supervisor_02"
            };

        float stationRadius = cognitiveRadius > 0f ? cognitiveRadius : 2f;
        float agentZoneRadius = Mathf.Clamp(stationRadius, 2f, 4f);
        float physicalRadius = ResolvePhysicalProximityRadius(stationRadius);

        // Only include entries flagged as cognitive stations by SceneUILoader normalization.
        // No name-based filtering — the flag already excludes all generic props regardless of their IDs.
        List<string> stationKeys = new List<string>();
        if (data?.initialStates != null)
        {
            foreach (var kvp in data.initialStates)
            {
                if (kvp.Value != null && kvp.Value.isCognitiveStation)
                    stationKeys.Add(kvp.Key);
            }
        }
        stationKeys.Sort(StringComparer.Ordinal);

        foreach (string stationKey in stationKeys)
        {
            zones.Add(MakeProximityZone(
                $"{stationKey}_proximity",
                stationKey,
                stationRadius,
                allAgentsOrFallback,
                true,
                "cognitive_station_proximity"));
        }

        // Physical environment props (scene_*, HubSpot, mouse keys, etc.)
        if (data?.initialStates != null)
        {
            foreach (var kvp in data.initialStates)
            {
                if (kvp.Value == null || kvp.Value.isCognitiveStation)
                    continue;
                if (kvp.Value.position == null)
                    continue;

                zones.Add(MakeProximityZone(
                    $"{kvp.Key}_proximity",
                    kvp.Key,
                    physicalRadius,
                    allAgentsOrFallback,
                    false,
                    "physical_env_proximity",
                    targetColorHex: "#66BBFF"));
            }
        }

        foreach (string aid in agentIds)
        {
            zones.Add(MakeProximityZone(
                $"{aid}_agent_proximity",
                aid,
                agentZoneRadius,
                allAgentsOrFallback,
                false,
                "agent_proximity",
                targetColorHex: "#44CCAA"));
        }

        loadedProximityConfig.proximityZones = zones.ToArray();
        Debug.Log($"[ProximityConfigLoader] Zones: {stationKeys.Count} cognitive station(s), {agentIds.Count} agent-centered, physical={(zones.Count - stationKeys.Count - agentIds.Count)}.");
    }

    static float ResolvePhysicalProximityRadius(float cognitiveRadius)
    {
        if (BsgIntegrationSettings.MultiplayerPhysicalProximityRadius > 0.5f)
            return BsgIntegrationSettings.MultiplayerPhysicalProximityRadius;

        return Mathf.Clamp(cognitiveRadius * 0.92f, 2f, 3.5f);
    }

    static float ResolveCognitiveProximityRadius(float fallback)
    {
        if (BsgIntegrationSettings.MultiplayerCognitiveProximityRadius > 0.5f)
            return BsgIntegrationSettings.MultiplayerCognitiveProximityRadius;
        return fallback;
    }

    /// <summary>Reload zones after SceneGenerator spawn; enables visual discs in multiplayer embed.</summary>
    public void ApplyAfterSceneSpawn()
    {
        if (proximitySystem == null)
            proximitySystem = UnityEngine.Object.FindObjectOfType<ProximityDetectionSystem>();

        ResolveJsonPathFromSceneLoader();
        LoadProximityConfiguration();

        if (proximitySystem == null)
            return;

        if (BsgIntegrationSettings.MultiplayerShowProximityZones)
            proximitySystem.showVisualZones = true;

        if (!_registriesRefreshedAfterSpawn)
        {
            proximitySystem.RefreshProximityRegistries();
            _registriesRefreshedAfterSpawn = true;
        }
    }

    static bool _registriesRefreshedAfterSpawn;

    public static void ResetForDomainReload()
    {
        _registriesRefreshedAfterSpawn = false;
    }

    ProximityZoneData MakeProximityZone(
        string zoneId,
        string centerObject,
        float radius,
        string[] affectedAgents,
        bool cognitiveStyle,
        string soundClip,
        string targetColorHex = null)
    {
        if (string.IsNullOrEmpty(targetColorHex))
            targetColorHex = cognitiveStyle ? "#AA44FF" : "#FF3333";

        ProximityZoneData zone = new ProximityZoneData();
        zone.zoneId = zoneId;
        zone.centerObject = centerObject;
        zone.radius = radius;
        zone.affectedAgents = affectedAgents;

        zone.effects = new ProximityEffectsData();
        zone.effects.colorChange = new ColorChangeEffectData();
        zone.effects.colorChange.enabled = true;
        zone.effects.colorChange.targetColor = targetColorHex;
        zone.effects.colorChange.fadeSpeed = 2f;
        zone.effects.colorChange.materialColor = new ProximityMaterialColor { r = 1f, g = 0.2f, b = 0.2f, a = 1f };

        zone.effects.directionChange = new DirectionChangeEffectData();
        zone.effects.directionChange.enabled = true;
        zone.effects.directionChange.avoidanceForce = 0.5f;
        zone.effects.directionChange.rotationSpeed = 15f;

        zone.effects.soundEffect = new SoundEffectData();
        zone.effects.soundEffect.enabled = true;
        zone.effects.soundEffect.soundClip = soundClip;
        zone.effects.soundEffect.volume = 0.7f;

        return zone;
    }

    void ResolveJsonPathFromSceneLoader()
    {
        SceneUILoader loader = FindObjectOfType<SceneUILoader>();
        if (loader == null || string.IsNullOrWhiteSpace(loader.jsonFileName))
        {
            return;
        }

        jsonFilePath = $"Assets/JsonFile/{loader.jsonFileName}";
    }

    string ResolveJsonFilePath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        if (configuredPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            string root = Application.dataPath;
            string relative = configuredPath.Substring("Assets/".Length);
            return Path.Combine(root, relative);
        }

        return Path.Combine(Application.dataPath, configuredPath);
    }
    
    void ApplyConfigurationToSystem()
    {
        if (proximitySystem == null) return;
        
        // Convert loaded data to system format
        proximitySystem.config.enabled = loadedProximityConfig.enabled;
        proximitySystem.config.detectionRadius = loadedProximityConfig.detectionRadius;
        proximitySystem.config.updateFrequency = loadedProximityConfig.updateFrequency;
        
        // Convert proximity zones
        List<ProximityZone> convertedZones = new List<ProximityZone>();
        
        foreach (var zoneData in loadedProximityConfig.proximityZones)
        {
            ProximityZone zone = new ProximityZone();
            zone.zoneId = zoneData.zoneId;
            zone.centerObject = zoneData.centerObject;
            zone.radius = zoneData.radius;
            zone.affectedAgents = zoneData.affectedAgents;
            
            // Convert effects
            zone.effects = new ProximityEffects();
            
            // Color change effect
            zone.effects.colorChange = new ColorChangeEffect();
            zone.effects.colorChange.enabled = zoneData.effects.colorChange.enabled;
            zone.effects.colorChange.fadeSpeed = zoneData.effects.colorChange.fadeSpeed;
            
            // Convert hex color to Unity Color
            if (ColorUtility.TryParseHtmlString(zoneData.effects.colorChange.targetColor, out Color targetColor))
            {
                zone.effects.colorChange.targetColor = targetColor;
            }
            else
            {
                // Fallback to material color values
                var matColor = zoneData.effects.colorChange.materialColor;
                zone.effects.colorChange.targetColor = new Color(matColor.r, matColor.g, matColor.b, matColor.a);
            }
            
            // Direction change effect
            zone.effects.directionChange = new DirectionChangeEffect();
            zone.effects.directionChange.enabled = zoneData.effects.directionChange.enabled;
            zone.effects.directionChange.avoidanceForce = zoneData.effects.directionChange.avoidanceForce;
            zone.effects.directionChange.rotationSpeed = zoneData.effects.directionChange.rotationSpeed;
            
            // Sound effect
            zone.effects.soundEffect = new SoundEffect();
            zone.effects.soundEffect.enabled = zoneData.effects.soundEffect.enabled;
            zone.effects.soundEffect.soundClip = zoneData.effects.soundEffect.soundClip;
            zone.effects.soundEffect.volume = zoneData.effects.soundEffect.volume;
            
            convertedZones.Add(zone);
        }
        
        proximitySystem.config.proximityZones = convertedZones.ToArray();
        
        Debug.Log($"Applied {convertedZones.Count} proximity zones to the system.");
    }
    
    void LogLoadedConfiguration()
    {
        Debug.Log($"Proximity Detection - Enabled: {loadedProximityConfig.enabled}");
        Debug.Log($"Detection Radius: {loadedProximityConfig.detectionRadius}");
        Debug.Log($"Update Frequency: {loadedProximityConfig.updateFrequency}");
        Debug.Log($"Number of Proximity Zones: {loadedProximityConfig.proximityZones.Length}");
        
        foreach (var zone in loadedProximityConfig.proximityZones)
        {
            Debug.Log($"Zone: {zone.zoneId} - Center: {zone.centerObject} - Radius: {zone.radius}");
            Debug.Log($"  Color Change: {zone.effects.colorChange.enabled}");
            Debug.Log($"  Direction Change: {zone.effects.directionChange.enabled}");
            Debug.Log($"  Sound Effect: {zone.effects.soundEffect.enabled}");
            Debug.Log($"  Affected Agents: {string.Join(", ", zone.affectedAgents)}");
        }
    }
    
    // Public method to reload configuration at runtime
    public void ReloadConfiguration()
    {
        LoadProximityConfiguration();
    }
    
    // Method to validate configuration
    public bool ValidateConfiguration()
    {
        if (loadedProximityConfig == null)
        {
            Debug.LogError("No proximity configuration loaded!");
            return false;
        }
        
        if (loadedProximityConfig.proximityZones == null || loadedProximityConfig.proximityZones.Length == 0)
        {
            Debug.LogError("No proximity zones configured!");
            return false;
        }
        
        foreach (var zone in loadedProximityConfig.proximityZones)
        {
            if (string.IsNullOrEmpty(zone.zoneId))
            {
                Debug.LogError("Zone ID is missing!");
                return false;
            }
            
            if (string.IsNullOrEmpty(zone.centerObject))
            {
                Debug.LogError($"Center object is missing for zone: {zone.zoneId}");
                return false;
            }
            
            if (zone.radius <= 0)
            {
                Debug.LogError($"Invalid radius for zone: {zone.zoneId}");
                return false;
            }
        }
        
        Debug.Log("Configuration validation passed!");
        return true;
    }
}
