using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Serializable Unity-side contract for semantic/declarative object identity.
/// The RAG schema can evolve; runtime systems should consume this stable shape.
/// </summary>
[Serializable]
public class DeclarativeObjectData
{
    public string objectId;
    public string displayName;
    public string semanticThread;
    public string thread;
    public DeclarativeRelationData[] relations;
    public DeclarativeDerivativeData[] derivatives;
    public DeclarativeSequenceData[] sequences;
    public float frequency;
    public float duration;
    public string currentState;
    public string expectedState;
    public string rawDeclarativeJson;
    public string metadataSource;
    public bool isPlaceholder = true;

    public string EffectiveThread
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(semanticThread)) return semanticThread;
            if (!string.IsNullOrWhiteSpace(thread)) return thread;
            return string.Empty;
        }
    }
}

[Serializable]
public class DeclarativeRelationData
{
    public string relationType;
    public string sourceId;
    public string targetId;
    public string description;
}

[Serializable]
public class DeclarativeDerivativeData
{
    public string property;
    public string fromState;
    public string toState;
    public string bufferTarget;
    public string description;
}

[Serializable]
public class DeclarativeSequenceData
{
    public string sequenceId;
    public int order;
    public string action;
    public string targetObjectId;
    public float startTimeSec;
    public float durationSec;
}

public enum DeclarativeThreadMatchQuality
{
    MissingMetadata,
    PlaceholderFallback,
    ObjectIdFallback,
    ThreadMatch,
    ThreadMismatch
}

[Serializable]
public struct DeclarativeThreadMatchResult
{
    public bool isMatch;
    public DeclarativeThreadMatchQuality quality;
    public string observedThread;
    public string expectedThread;
    public string reason;
}

/// <summary>
/// Attach to scene GameObjects so physical/cognitive agents can read the same
/// declarative identity regardless of whether it came from real RAG frames or a placeholder.
/// </summary>
public class DeclarativeObjectMetadata : MonoBehaviour
{
    [Header("Identity")]
    public string objectId;
    public string displayName;
    public string semanticThread;
    public string currentState;
    public string expectedState;

    [Header("Declarative Frame")]
    public DeclarativeRelationData[] relations = new DeclarativeRelationData[0];
    public DeclarativeDerivativeData[] derivatives = new DeclarativeDerivativeData[0];
    public DeclarativeSequenceData[] sequences = new DeclarativeSequenceData[0];
    public float frequency;
    public float duration;
    [TextArea(2, 8)] public string rawDeclarativeJson;

    [Header("Source")]
    public string metadataSource = "unity_placeholder";
    public bool isPlaceholder = true;

    public void Apply(DeclarativeObjectData data, string fallbackObjectId, string fallbackName, string fallbackType, string fallbackState)
    {
        DeclarativeObjectData effective = data ?? CreatePlaceholder(fallbackObjectId, fallbackName, fallbackType, fallbackState);

        objectId = FirstNonEmpty(effective.objectId, fallbackObjectId);
        displayName = FirstNonEmpty(effective.displayName, fallbackName, objectId);
        semanticThread = FirstNonEmpty(effective.EffectiveThread, BuildPlaceholderThread(objectId, displayName, fallbackType));
        currentState = FirstNonEmpty(effective.currentState, fallbackState);
        expectedState = effective.expectedState ?? string.Empty;
        relations = effective.relations ?? new DeclarativeRelationData[0];
        derivatives = effective.derivatives ?? BuildPlaceholderDerivatives(currentState);
        sequences = effective.sequences ?? new DeclarativeSequenceData[0];
        frequency = effective.frequency;
        duration = effective.duration;
        rawDeclarativeJson = effective.rawDeclarativeJson ?? string.Empty;
        metadataSource = FirstNonEmpty(effective.metadataSource, data == null ? "unity_placeholder" : "rag_declarative_frame");
        isPlaceholder = effective.isPlaceholder || string.Equals(metadataSource, "unity_placeholder", StringComparison.OrdinalIgnoreCase);
    }

    public DeclarativeObjectData ToData()
    {
        return new DeclarativeObjectData
        {
            objectId = objectId,
            displayName = displayName,
            semanticThread = semanticThread,
            relations = relations,
            derivatives = derivatives,
            sequences = sequences,
            frequency = frequency,
            duration = duration,
            currentState = currentState,
            expectedState = expectedState,
            rawDeclarativeJson = rawDeclarativeJson,
            metadataSource = metadataSource,
            isPlaceholder = isPlaceholder
        };
    }

    public string BuildObservationFact()
    {
        string src = isPlaceholder ? "placeholder" : "declarative";
        return $"{objectId}|thread={semanticThread}|state={currentState}|source={src}";
    }

    public static DeclarativeObjectData CreatePlaceholder(string objectId, string displayName, string type, string state)
    {
        string id = string.IsNullOrWhiteSpace(objectId) ? "unknown_object" : objectId;
        string name = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
        return new DeclarativeObjectData
        {
            objectId = id,
            displayName = name,
            semanticThread = BuildPlaceholderThread(id, name, type),
            currentState = state ?? string.Empty,
            relations = new DeclarativeRelationData[0],
            derivatives = BuildPlaceholderDerivatives(state),
            sequences = new DeclarativeSequenceData[0],
            metadataSource = "unity_placeholder",
            isPlaceholder = true
        };
    }

    public static DeclarativeThreadMatchResult CompareExpected(GameObject observedObject, string expectedObjectId, DeclarativeObjectData expectedData = null)
    {
        DeclarativeObjectMetadata observed = observedObject != null
            ? observedObject.GetComponentInParent<DeclarativeObjectMetadata>()
            : null;

        if (observed == null)
        {
            return new DeclarativeThreadMatchResult
            {
                isMatch = false,
                quality = DeclarativeThreadMatchQuality.MissingMetadata,
                expectedThread = expectedData?.EffectiveThread ?? string.Empty,
                reason = "Observed object has no DeclarativeObjectMetadata component."
            };
        }

        string observedThread = observed.semanticThread ?? string.Empty;
        string expectedThread = expectedData?.EffectiveThread ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(observedThread) && !string.IsNullOrWhiteSpace(expectedThread))
        {
            bool same = string.Equals(observedThread, expectedThread, StringComparison.OrdinalIgnoreCase);
            return new DeclarativeThreadMatchResult
            {
                isMatch = same,
                quality = same ? DeclarativeThreadMatchQuality.ThreadMatch : DeclarativeThreadMatchQuality.ThreadMismatch,
                observedThread = observedThread,
                expectedThread = expectedThread,
                reason = same ? "Observed declarative thread matched expected thread." : "Observed declarative thread did not match expected thread."
            };
        }

        if (!string.IsNullOrWhiteSpace(expectedObjectId))
        {
            bool sameId = IdMatches(observed.objectId, expectedObjectId) || IdMatches(observed.gameObject.name, expectedObjectId);
            return new DeclarativeThreadMatchResult
            {
                isMatch = sameId,
                quality = observed.isPlaceholder ? DeclarativeThreadMatchQuality.PlaceholderFallback : DeclarativeThreadMatchQuality.ObjectIdFallback,
                observedThread = observedThread,
                expectedThread = expectedThread,
                reason = sameId ? "No expected thread; matched by object id fallback." : "No expected thread; object id fallback did not match."
            };
        }

        return new DeclarativeThreadMatchResult
        {
            isMatch = false,
            quality = DeclarativeThreadMatchQuality.MissingMetadata,
            observedThread = observedThread,
            expectedThread = expectedThread,
            reason = "No expected thread or object id was available."
        };
    }

    static DeclarativeDerivativeData[] BuildPlaceholderDerivatives(string state)
    {
        if (string.IsNullOrWhiteSpace(state)) return new DeclarativeDerivativeData[0];
        return new[]
        {
            new DeclarativeDerivativeData
            {
                property = "state",
                fromState = state,
                toState = "",
                description = "Placeholder derivative until declarative memory frame supplies state transitions."
            }
        };
    }

    static string BuildPlaceholderThread(string objectId, string displayName, string type)
    {
        List<string> parts = new List<string> { "placeholder" };
        if (!string.IsNullOrWhiteSpace(type)) parts.Add(SanitizeThreadToken(type));
        if (!string.IsNullOrWhiteSpace(displayName)) parts.Add(SanitizeThreadToken(displayName));
        else if (!string.IsNullOrWhiteSpace(objectId)) parts.Add(SanitizeThreadToken(objectId));
        return string.Join("/", parts);
    }

    static string SanitizeThreadToken(string value)
    {
        return (value ?? string.Empty).Trim().Replace(' ', '_').ToLowerInvariant();
    }

    static string FirstNonEmpty(params string[] values)
    {
        if (values == null) return string.Empty;
        for (int i = 0; i < values.Length; i++)
            if (!string.IsNullOrWhiteSpace(values[i]))
                return values[i];
        return string.Empty;
    }

    static bool IdMatches(string observed, string expected)
    {
        if (string.IsNullOrWhiteSpace(observed) || string.IsNullOrWhiteSpace(expected)) return false;
        if (string.Equals(observed, expected, StringComparison.OrdinalIgnoreCase)) return true;
        return observed.StartsWith(expected + "_zone", StringComparison.OrdinalIgnoreCase)
               || observed.StartsWith("Tool_" + expected, StringComparison.OrdinalIgnoreCase);
    }
}
