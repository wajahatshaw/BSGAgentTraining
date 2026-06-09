using UnityEngine;

/// <summary>
/// Ensures proximity zones + visual discs exist for all cognitive and physical RAG objects
/// after zone 0 embed finishes spawning. Runs once per play session.
/// </summary>
public static class MultiplayerProximityZoneSetup
{
    static bool _applied;

    public static void ApplyToScene()
    {
        if (_applied)
            return;

        if (!BsgIntegrationSettings.UsePhotonPlayerAsPhysicalAgent
            && !BsgIntegrationSettings.MultiplayerEmbedMode
            && !BsgIntegrationSettings.UseSceneAnchorLayout)
        {
            return;
        }

        ProximityConfigLoader loader = UnityEngine.Object.FindObjectOfType<ProximityConfigLoader>();
        if (loader == null)
        {
            GameObject go = new GameObject("ProximityConfigLoader");
            loader = go.AddComponent<ProximityConfigLoader>();
        }

        loader.ApplyAfterSceneSpawn();
        _applied = true;
    }

    public static void ResetForDomainReload()
    {
        _applied = false;
    }
}
