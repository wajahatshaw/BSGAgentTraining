using System;
using System.Collections.Generic;
using UnityEngine;

public class CognitiveTransferContractEngine : MonoBehaviour
{
    public static CognitiveTransferContractEngine Instance { get; private set; }

    [Header("Validation Rules")]
    public bool enforcePhaseOrder = true;
    public bool enforceRetrievalGateway = true;
    public bool logViolations = true;

    private readonly Dictionary<string, int> phaseIndexByAgent = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> phaseOrder = new List<string>
    {
        "phase_0", "phase_1", "phase_2", "phase_3", "phase_4", "opening_sequence", "closing_sequence"
    };

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnEnable()
    {
        CognitiveStationInteractable.StationEvent += OnStationEvent;
    }

    void OnDisable()
    {
        CognitiveStationInteractable.StationEvent -= OnStationEvent;
    }

    void OnStationEvent(CognitiveStationEvent evt)
    {
        if (evt.phase != "enter") return;
        ValidatePhase(evt);
        ValidateGateway(evt);
    }

    void ValidatePhase(CognitiveStationEvent evt)
    {
        if (!enforcePhaseOrder) return;
        string phaseToken = InferPhaseToken(evt.stationId, evt.stationType);
        int incomingIndex = phaseOrder.IndexOf(phaseToken);
        if (incomingIndex < 0) return;

        phaseIndexByAgent.TryGetValue(evt.agentId, out int currentIndex);
        if (incomingIndex + 1 < currentIndex)
        {
            if (logViolations)
            {
                Debug.LogWarning($"[CognitiveTransfer] Phase violation: agent={evt.agentId} attempted {phaseToken} after reaching {phaseOrder[currentIndex]}");
            }
            return;
        }

        if (incomingIndex > currentIndex)
        {
            phaseIndexByAgent[evt.agentId] = incomingIndex;
        }
    }

    void ValidateGateway(CognitiveStationEvent evt)
    {
        if (!enforceRetrievalGateway) return;
        // Declarative module gateway station (updated cognitive station map).
        if (!evt.stationId.Equals("cognitive_003", StringComparison.OrdinalIgnoreCase)) return;

        AgentCognitiveMemory memory = FindAgentMemory(evt.agentId);
        if (memory == null || !memory.TryGet("retrieved_schema", out _))
        {
            if (logViolations)
            {
                Debug.LogWarning($"[CognitiveTransfer] Gateway violation: {evt.agentId} reached declarative module without retrieval payload.");
            }
        }
    }

    static AgentCognitiveMemory FindAgentMemory(string agentName)
    {
        AgentCognitiveMemory[] all = FindObjectsOfType<AgentCognitiveMemory>();
        foreach (var mem in all)
        {
            if (mem.gameObject.name == agentName) return mem;
        }
        return null;
    }

    static string InferPhaseToken(string stationId, string stationType)
    {
        string id = stationId?.ToLowerInvariant() ?? string.Empty;
        string t = stationType?.ToLowerInvariant() ?? string.Empty;
        if (id == "cognitive_001" || id == "cognitive_002" || id == "cognitive_008") return "phase_0";
        if (id == "cognitive_004" || id == "cognitive_005" || id == "cognitive_006" || id == "cognitive_007" ||
            id == "cognitive_012" || id == "cognitive_013" || id == "cognitive_014" ||
            id == "cognitive_015" || id == "cognitive_016") return "phase_1";
        if (id == "cognitive_011" || id == "cognitive_017") return "phase_2";
        if (id == "cognitive_010" || id == "cognitive_003") return "phase_3";
        if (id == "cognitive_009") return "phase_4";
        if (t.Contains("opening")) return "opening_sequence";
        if (t.Contains("closing")) return "closing_sequence";
        return string.Empty;
    }
}
