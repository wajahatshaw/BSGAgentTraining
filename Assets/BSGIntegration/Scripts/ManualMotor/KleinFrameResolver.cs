using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Maps RAG physical steps to Klein frames from the manual buffer catalog.</summary>
public static class KleinFrameResolver
{
    static readonly Dictionary<string, int> _targetCommandSequence = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public static void ResetSequenceCounters()
    {
        _targetCommandSequence.Clear();
    }

    public static KleinFrameResolveResult Resolve(ActionSequenceStep step)
    {
        return ResolveInternal(step, consumeSequence: true);
    }

    public static KleinFrameResolveResult Peek(ActionSequenceStep step)
    {
        return ResolveInternal(step, consumeSequence: false);
    }

    static KleinFrameResolveResult ResolveInternal(ActionSequenceStep step, bool consumeSequence)
    {
        KleinFrameResolveResult result = new KleinFrameResolveResult();
        if (step == null)
            return result;

        if (!ManualBufferCatalog.IsLoaded && !ManualBufferCatalog.TryLoad())
            return result;

        string manualCommand = string.Empty;
        string targetObject = step.targetObjectName ?? string.Empty;

        if (SceneStateLogBridge.TryGetForStep(step.stepId, out SceneStateKleinRef sceneRef))
        {
            result.sceneRef = sceneRef;
            if (!string.IsNullOrWhiteSpace(sceneRef.targetObject))
                targetObject = sceneRef.targetObject;
            manualCommand = ManualBufferCatalog.ThreadToManualCommand(sceneRef.thread);
        }

        if (string.IsNullOrWhiteSpace(manualCommand))
            manualCommand = ManualBufferCatalog.InferManualCommandFromStep(step);

        // Contact-prep step has no dedicated finger frame in the manual buffer.
        if (IsContactPrepStep(step, targetObject))
        {
            result.isReachOnly = true;
            result.manualCommand = manualCommand;
            result.targetObject = targetObject;
            result.resolved = true;
            return result;
        }

        string seqKey = ManualBufferCatalog.TargetCommandKey(targetObject, manualCommand);
        if (!_targetCommandSequence.TryGetValue(seqKey, out int seqIndex))
            seqIndex = 0;

        if (consumeSequence)
        {
            if (ManualBufferCatalog.TryConsumeNextForTargetCommand(targetObject, manualCommand, ref seqIndex, out KleinFrame frame))
            {
                _targetCommandSequence[seqKey] = seqIndex;
                result.frame = frame;
                result.manualCommand = manualCommand;
                result.targetObject = targetObject;
                result.resolved = true;
                return result;
            }
        }
        else if (ManualBufferCatalog.TryGetNextForTargetCommand(targetObject, manualCommand, ref seqIndex, out KleinFrame peekFrame))
        {
            result.frame = peekFrame;
            result.manualCommand = manualCommand;
            result.targetObject = targetObject;
            result.resolved = true;
            return result;
        }

        // Fallback: try step target name directly when sceneStateLog object differs.
        if (!string.IsNullOrWhiteSpace(step.targetObjectName)
            && !step.targetObjectName.Equals(targetObject, StringComparison.OrdinalIgnoreCase))
        {
            string altKey = ManualBufferCatalog.TargetCommandKey(step.targetObjectName, manualCommand);
            if (!_targetCommandSequence.TryGetValue(altKey, out seqIndex))
                seqIndex = 0;

            if (consumeSequence)
            {
                if (ManualBufferCatalog.TryConsumeNextForTargetCommand(step.targetObjectName, manualCommand, ref seqIndex, out KleinFrame frame))
                {
                    _targetCommandSequence[altKey] = seqIndex;
                    result.frame = frame;
                    result.manualCommand = manualCommand;
                    result.targetObject = step.targetObjectName;
                    result.resolved = true;
                }
            }
            else if (ManualBufferCatalog.TryGetNextForTargetCommand(step.targetObjectName, manualCommand, ref seqIndex, out KleinFrame peekFrame))
            {
                result.frame = peekFrame;
                result.manualCommand = manualCommand;
                result.targetObject = step.targetObjectName;
                result.resolved = true;
            }
        }

        return result;
    }

    static bool IsContactPrepStep(ActionSequenceStep step, string targetObject)
    {
        if (step == null) return false;
        string name = targetObject ?? string.Empty;
        if (name.IndexOf("fingertip", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (step.description != null && step.description.IndexOf("contact preparation", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        return string.Equals(step.stepId, "t01_phy_s14", StringComparison.OrdinalIgnoreCase);
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
