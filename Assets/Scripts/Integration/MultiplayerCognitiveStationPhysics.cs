using UnityEngine;

/// <summary>
/// Ensures cognitive stations block AgentGroundMotor (M1 + designated player) in multiplayer zone 0 embed.
/// </summary>
public static class MultiplayerCognitiveStationPhysics
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

        foreach (CognitiveStationInteractable station in UnityEngine.Object.FindObjectsOfType<CognitiveStationInteractable>(true))
        {
            if (station == null)
                continue;

            ApplySolidColliderToStation(station.gameObject);
        }

        foreach (EnvironmentSolidCollider tagged in UnityEngine.Object.FindObjectsOfType<EnvironmentSolidCollider>(true))
        {
            if (tagged != null)
                tagged.ConfigureSolidCollider();
        }

        _applied = true;
    }

    public static void ResetForDomainReload()
    {
        _applied = false;
    }

    static void ApplySolidColliderToStation(GameObject stationRoot)
    {
        if (stationRoot == null)
            return;

        Transform nav = stationRoot.transform.Find("CognitiveNavObstacle");
        if (nav != null)
        {
            BoxCollider box = nav.GetComponent<BoxCollider>();
            if (box != null)
                box.isTrigger = false;

            EnvironmentSolidCollider solid = nav.GetComponent<EnvironmentSolidCollider>();
            if (solid == null)
                solid = nav.gameObject.AddComponent<EnvironmentSolidCollider>();
            solid.ConfigureSolidCollider();
            ScenePhysicsLayers.ApplyEnvironmentLayer(nav.gameObject);
            return;
        }

        EnvironmentSolidCollider.EnsureOnObject(stationRoot, addBoxIfMissing: true);
    }
}
