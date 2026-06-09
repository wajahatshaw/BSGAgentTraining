using UnityEngine;

/// <summary>
/// Ensures physical-environment RAG props (non-cognitive tools) have solid navigation hulls
/// in multiplayer zone 0 embed so the designated Photon player collides instead of passing through.
/// </summary>
public static class MultiplayerEnvironmentToolPhysics
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

        ScenePhysicsLayers.EnsureInitialized();
        EnvironmentNavigationColliderBuilder.RebuildAllEnvironmentToolsInScene();
        _applied = true;
    }

    public static void ResetForDomainReload()
    {
        _applied = false;
    }
}
