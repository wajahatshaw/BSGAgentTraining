using System;
using UnityEngine;

/// <summary>
/// Maps RAG physical steps to Klein frames from the manual buffer catalog.
/// Resolution is an exact (stepId, agentId, zoneIndex) match against mannualBuffer2.json —
/// frames are explicitly associated to one physical step, so only steps present in the
/// buffer resolve; every other step returns unresolved and the caller falls back.
/// </summary>
public static class KleinFrameResolver
{
    public static void ResetSequenceCounters()
    {
        // No running sequence counters anymore — frames are addressed by exact step key.
    }

    public static KleinFrameResolveResult Resolve(ActionSequenceStep step, string agentId, int zoneIndex)
    {
        return ResolveInternal(step, agentId, zoneIndex);
    }

    public static KleinFrameResolveResult Peek(ActionSequenceStep step, string agentId, int zoneIndex)
    {
        return ResolveInternal(step, agentId, zoneIndex);
    }

    static KleinFrameResolveResult ResolveInternal(ActionSequenceStep step, string agentId, int zoneIndex)
    {
        KleinFrameResolveResult result = new KleinFrameResolveResult();
        if (step == null || string.IsNullOrWhiteSpace(step.stepId))
            return result;

        // RAG-only: frames come from LoadFromRag (sceneStateLog). Never fall back to mannualBuffer2.json.
        if (!ManualBufferCatalog.IsLoaded)
            return result;

        if (!ManualBufferCatalog.TryGetForStep(step.stepId, agentId, zoneIndex, out KleinFrame frame) || frame == null)
            return result;

        result.frame = frame;
        result.manualCommand = frame.manualCommand;
        result.targetObject = frame.targetObject;
        result.resolved = true;
        return result;
    }
}

public class KleinFrameResolveResult
{
    public bool resolved;
    public bool isReachOnly;
    public KleinFrame frame;
    public SceneStateKleinRef sceneRef;
    public string manualCommand;
    public string targetObject;
}
