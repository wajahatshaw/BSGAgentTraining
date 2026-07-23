using UnityEngine;

/// <summary>
/// Shared layer names/masks for agent vs environment collision in generated scenes.
/// Uses built-in layers from TagManager: Character (agents), Wall (solid scenery).
/// </summary>
public static class ScenePhysicsLayers
{
    public const string CharacterLayerName = "Character";
    public const string EnvironmentLayerName = "Wall";

    public static int CharacterLayer { get; private set; } = -1;
    public static int EnvironmentLayer { get; private set; } = -1;

    public static int EnvironmentMask { get; private set; }
    public static int GroundMask { get; private set; }

    public static void EnsureInitialized()
    {
        CacheLayers();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void CacheLayers()
    {
        CharacterLayer = LayerMask.NameToLayer(CharacterLayerName);
        EnvironmentLayer = LayerMask.NameToLayer(EnvironmentLayerName);

        if (CharacterLayer < 0)
            CharacterLayer = 0;
        if (EnvironmentLayer < 0)
            EnvironmentLayer = 0;

        EnvironmentMask = 1 << EnvironmentLayer;

        int ground = LayerMask.NameToLayer("Ground");
        GroundMask = ground >= 0 ? (1 << ground) : (1 << 0);
    }

    public static void ApplyCharacterLayer(GameObject go)
    {
        if (go == null) return;
        CacheLayers();
        go.layer = CharacterLayer;
    }

    public static void ApplyEnvironmentLayer(GameObject go)
    {
        if (go == null) return;
        CacheLayers();
        go.layer = EnvironmentLayer;
    }

    // --- physical-type tags for the locomotion ray sensor -----------------------------------
    // The Y-Bot walker's RayPerceptionSensor detects obstacles by TAG. Tags encode the static
    // physical IDENTITY of an object (what KIND it is), NOT its current role — "is this my
    // target right now" stays a runtime reference on the agent, never a tag. Keep these strings
    // in sync with ProjectSettings/TagManager.asset.
    //
    // FROZEN VOCABULARY. The set + ORDER below defines the RayPerceptionSensor observation width
    // (per ray = tagCount + 2). Adding OR removing a tag changes the policy input spec and forces a
    // from-scratch retrain (ML-Agents does a strict input-shape check, no partial transfer). So the
    // full foreseeable vocabulary is locked in ONCE here — including tags that sit dormant until
    // their objects appear in a scene. Reusing an existing tag for a new object later is FREE
    // (scene-only change, resume-safe); only introducing a brand-new tag category is not.
    public const string TagWall = "Wall";           // perimeter walls, fixed structural partitions
    public const string TagStation = "Station";     // cognitive stations (interactive panels)
    public const string TagFurniture = "Furniture"; // desks, tables, workstations, cabinets
    public const string TagProp = "Prop";           // small movable clutter (chairs, decor, tools)
    public const string TagAgent = "Agent";         // other AI / NPC agents (non-player)
    public const string TagHuman = "Human";         // real player avatars
    public const string TagReserved1 = "Reserved1"; // free future category (rename-in-place, no retrain)
    public const string TagReserved2 = "Reserved2"; // free future category (rename-in-place, no retrain)

    /// <summary>
    /// The frozen, ORDERED obstacle-tag vocabulary. This is the single source of truth for the
    /// RayPerceptionSensor's DetectableTags — its length fixes the ray observation spec. Do NOT
    /// change the count without accepting a from-scratch retrain. Renaming a Reserved* slot that no
    /// object currently carries is safe (count is unchanged).
    /// </summary>
    public static readonly string[] ObstacleTagVocabulary =
    {
        TagWall, TagStation, TagFurniture, TagProp, TagAgent, TagHuman, TagReserved1, TagReserved2,
    };

    /// <summary>
    /// Sets <paramref name="go"/>'s tag if that tag is defined in the project, else no-ops with a
    /// warning (Unity throws on an undefined tag). Safe to call on runtime-generated obstacles.
    /// </summary>
    public static void SafeSetTag(GameObject go, string tag)
    {
        if (go == null || string.IsNullOrEmpty(tag)) return;
        try
        {
            go.tag = tag;
        }
        catch (UnityException)
        {
            // Tag not defined in TagManager — leave as Untagged rather than crash scene setup.
            Debug.LogWarning($"[ScenePhysicsLayers] Tag '{tag}' is not defined in TagManager; '{go.name}' left Untagged.");
        }
    }
}
