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
}
