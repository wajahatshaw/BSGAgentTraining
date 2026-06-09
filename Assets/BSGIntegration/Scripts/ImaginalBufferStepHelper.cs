using System;

/// <summary>
/// Identifies cognitive steps that target the Imaginal Buffer station (Unity-side only).
/// </summary>
public static class ImaginalBufferStepHelper
{
    public static bool IsImaginalBufferStep(ActionSequenceStep step)
    {
        if (step == null) return false;

        if (!string.IsNullOrEmpty(step.currentCognitiveState)
            && string.Equals(step.currentCognitiveState, "ImaginalBuffer", StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(step.targetObjectId, "cognitive_009", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Station id form used in scene: cognitive_009_zone{N}.</summary>
    public static bool StationIdIsImaginalBuffer(string stationIdOrZoneSuffixed)
    {
        if (string.IsNullOrEmpty(stationIdOrZoneSuffixed)) return false;
        string baseId = CognitiveProcessManager.StripZoneSuffix(stationIdOrZoneSuffixed);
        return string.Equals(baseId, "cognitive_009", StringComparison.OrdinalIgnoreCase);
    }
}
