using System;
using UnityEngine;

public class CognitiveStationInteractable : MonoBehaviour
{
    public static event Action<CognitiveStationEvent> StationEvent;

    [Header("Station Identity")]
    public string stationId;
    public string stationType;
    public string stationAction = "learn";
    public float interactionRadius = 2.5f;

    [Header("Stored Data (written by P-agent observation)")]
    [TextArea(2, 4)]
    public string storedData = string.Empty;

    [Header("Debug")]
    public bool verboseLogging = true;

    private SphereCollider triggerCollider;

    void Awake()
    {
        EnsureTriggerCollider();
        EnsureIdentity();
    }

    void EnsureIdentity()
    {
        if (string.IsNullOrWhiteSpace(stationId)) stationId = gameObject.name;
        if (string.IsNullOrWhiteSpace(stationType)) stationType = "cognitive_station";
        if (string.IsNullOrWhiteSpace(stationAction)) stationAction = "learn";
    }

    void EnsureTriggerCollider()
    {
        triggerCollider = GetComponent<SphereCollider>();
        if (triggerCollider == null)
        {
            triggerCollider = gameObject.AddComponent<SphereCollider>();
        }

        triggerCollider.isTrigger = true;
        triggerCollider.radius = Mathf.Max(0.5f, interactionRadius);
    }

    void OnTriggerEnter(Collider other)
    {
        HandleInteraction(other, "enter");
    }

    void OnTriggerStay(Collider other)
    {
        HandleInteraction(other, "stay");
    }

    void OnTriggerExit(Collider other)
    {
        var evt = BuildEvent(other.gameObject, "exit", string.Empty);
        StationEvent?.Invoke(evt);
        if (verboseLogging)
        {
            Debug.Log($"[CognitiveStation] EXIT station={stationId} agent={evt.agentId}");
        }
    }

    void HandleInteraction(Collider other, string phase)
    {
        if (other == null) return;
        GameObject agent = ResolveAgentRoot(other.gameObject);
        if (agent == null) return;

        AgentCognitiveMemory memory = agent.GetComponent<AgentCognitiveMemory>();
        if (memory == null) memory = agent.AddComponent<AgentCognitiveMemory>();

        string payloadKey = DerivePayloadKey();
        string payloadValue = $"{stationAction}:{stationType}";
        memory.Store(payloadKey, payloadValue, stationId);
        memory.Store("phase", stationType, stationId);

        var evt = BuildEvent(agent, phase, payloadValue);
        StationEvent?.Invoke(evt);

        if (verboseLogging && phase == "enter")
        {
            Debug.Log($"[CognitiveStation] ENTER station={stationId} action={stationAction} agent={evt.agentId} payload={payloadValue}");
        }
    }

    string DerivePayloadKey()
    {
        string lowered = stationAction.ToLowerInvariant();
        if (lowered.Contains("identify") || lowered.Contains("intent")) return "intention";
        if (lowered.Contains("goal")) return "goal";
        if (lowered.Contains("visual") || lowered.Contains("perceive")) return "visual_info";
        if (lowered.Contains("retrieve")) return "retrieved_schema";
        if (lowered.Contains("command")) return "command_state";
        if (lowered.Contains("plan")) return "plan_state";
        return "memory_note";
    }

    static GameObject ResolveAgentRoot(GameObject source)
    {
        if (source == null) return null;
        if (source.GetComponent<AgentCognitiveMemory>() != null) return source;
        if (source.transform.parent != null && source.transform.parent.GetComponent<AgentCognitiveMemory>() != null)
            return source.transform.parent.gameObject;

        // Fallback name heuristic for existing scene agents.
        string n = source.name;
        if (n.Contains("Agent_") || n.Contains("SIMPLE_") || n.Contains("Technician") || n.Contains("Supervisor"))
            return source;

        return null;
    }

    CognitiveStationEvent BuildEvent(GameObject agent, string phase, string payload)
    {
        return new CognitiveStationEvent
        {
            stationId = stationId,
            stationType = stationType,
            stationAction = stationAction,
            agentId = agent != null ? agent.name : "unknown_agent",
            phase = phase,
            payload = payload,
            timestamp = Time.time
        };
    }
}

[Serializable]
public struct CognitiveStationEvent
{
    public string stationId;
    public string stationType;
    public string stationAction;
    public string agentId;
    public string phase;
    public string payload;
    public float timestamp;
}
