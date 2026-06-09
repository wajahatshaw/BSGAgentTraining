/// <summary>
/// Optional bridge from BSGIntegration to Assembly-CSharp Photon sync without asmdef cycles.
/// </summary>
public interface IRagOrchestratorStepReporter
{
    bool IsActive { get; }

    void ReportStepCompleted(int zoneIndex, string stepId, bool isMentalStep);

    void ReportCognitivePhaseComplete(int zoneIndex);

    void ReportExternalStepCompleted(int zoneIndex, string stepId);
}

public static class RagOrchestratorStepReporterRegistry
{
    static IRagOrchestratorStepReporter _reporter;

    public static bool HasReporter => _reporter != null && _reporter.IsActive;

    public static void Register(IRagOrchestratorStepReporter reporter)
    {
        _reporter = reporter;
    }

    public static void ReportStepCompleted(int zoneIndex, string stepId, bool isMentalStep)
    {
        if (_reporter != null && _reporter.IsActive)
            _reporter.ReportStepCompleted(zoneIndex, stepId, isMentalStep);
        else
            CognitivePhaseOrchestrator.GetOrCreateForZone(zoneIndex)?.NotifyStepCompleted(stepId);
    }

    public static void ReportCognitivePhaseComplete(int zoneIndex)
    {
        if (_reporter != null && _reporter.IsActive)
            _reporter.ReportCognitivePhaseComplete(zoneIndex);
        else
            CognitivePhaseOrchestrator.GetOrCreateForZone(zoneIndex)?.NotifyCognitivePhaseCompleteForPhysicalUnlock();
    }

    public static void ReportExternalStepCompleted(int zoneIndex, string stepId)
    {
        if (_reporter != null && _reporter.IsActive)
            _reporter.ReportExternalStepCompleted(zoneIndex, stepId);
        else
            CognitivePhaseOrchestrator.GetOrCreateForZone(zoneIndex)?.NotifyStepCompleted(stepId);
    }
}
