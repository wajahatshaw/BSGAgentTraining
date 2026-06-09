using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// No-op stubs for BSG PersonaSystem types excluded from Ronald-Johnson integration.
/// RAG ML training/inference does not use JSONWorkflowScenePersona; these satisfy compile-time references only.
/// </summary>
public class PersonaCognitiveControlSystem : MonoBehaviour
{
    public static PersonaCognitiveControlSystem Instance { get; private set; }

    public bool IsGateOpen => true;

    void Awake()
    {
        if (Instance == null)
            Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public static bool IsActionBlockedForAgent(string agentId) => false;

    public void InitializeForAgents(IEnumerable<GameObject> agentObjects) { }

    public void NotifyFirstActionStarted(string agentId) { }

    public void NotifyCognitiveStepCompleted(string agentId, string stepId, string targetId) { }

    public void NotifyCognitiveSequenceCompleted(string agentId) { }

    public void OpenGateForAgent(string agentId) { }

    public void NotifyStepTransition(string agentId, string stepId = null) { }

    public void NotifySequenceCompleted(string agentId) { }

    public void NotifyToolDiscovered(string agentId, string toolId) { }
}

public class PersonaPreActionHudOverlay : MonoBehaviour
{
}

public class ActrSceneModulesVisualizer : MonoBehaviour
{
}
