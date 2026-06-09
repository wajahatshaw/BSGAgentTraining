using UnityEngine;

/// <summary>
/// Clears multiplayer embed static state on domain reload to avoid stale Unity object handles
/// ("Release of invalid GC handle. The handle is from a previous domain.").
/// </summary>
static class MultiplayerEmbedDomainReset
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        BsgIntegrationSettings.Reset();
        MultiplayerRagZone0Anchor.ResetForDomainReload();
        MultiplayerProximityZoneSetup.ResetForDomainReload();
        MultiplayerEnvironmentToolPhysics.ResetForDomainReload();
        MultiplayerCognitiveStationPhysics.ResetForDomainReload();
        PlayerRagPhysicalBridge.ResetForDomainReload();
        RagPhysicalAgentLocalMode.ResetForDomainReload();
        CognitivePhaseOrchestrator.ResetStaticRegistry();
        ProximityConfigLoader.ResetForDomainReload();
    }
}
