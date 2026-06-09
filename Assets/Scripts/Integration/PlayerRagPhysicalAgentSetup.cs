using UnityEngine;

/// <summary>
/// Applies the same physics/collision setup SceneGenerator uses for spawned P1 agents
/// onto the Photon blue player when it replaces P1 in ProtoypeSceneMultiplayer.
/// </summary>
public static class PlayerRagPhysicalAgentSetup
{
    public const int DefaultZoneIndex = 0;

    public static AgentGroundMotor Configure(GameObject playerGo, int zoneIndex = DefaultZoneIndex)
    {
        if (playerGo == null)
            return null;

        ScenePhysicsLayers.EnsureInitialized();
        ScenePhysicsLayers.ApplyCharacterLayer(playerGo);
        DesignatedPhysicalPlayerAppearance.ApplyScale(playerGo.transform);

        CapsuleCollider cap = playerGo.GetComponent<CapsuleCollider>();
        if (cap == null)
            cap = playerGo.AddComponent<CapsuleCollider>();

        cap.isTrigger = false;
        cap.height = 1.75f;
        cap.radius = 0.28f;
        cap.center = new Vector3(0f, 0.875f, 0f);
        cap.direction = 1;

        Rigidbody rb = playerGo.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.useGravity = false;
            rb.freezeRotation = true;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        AgentGroundMotor motor = playerGo.GetComponent<AgentGroundMotor>();
        if (motor == null)
            motor = playerGo.AddComponent<AgentGroundMotor>();
        motor.clampZoneIndex = zoneIndex;
        motor.skipCognitiveStationSolids = true;
        motor.SnapFeetToGround();

        HideWorldMapDot(playerGo);
        RagPhysicalAgentCollisionFx.EnsureOnAgent(playerGo);

        return motor;
    }

    static void HideWorldMapDot(GameObject playerGo)
    {
        if (playerGo == null)
            return;

        Transform mapDot = playerGo.transform.Find("PlayerMapDot");
        if (mapDot != null)
            mapDot.gameObject.SetActive(false);
    }
}
