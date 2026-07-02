using System;

/// <summary>
/// ── DECLARATIVE MEMORY SCHEMA ──────────────────────────────────────────────────
/// The canonical, editable structure that ONE cognitive step stores into
/// <see cref="ZoneDeclarativeMemory"/> the moment it completes. Think of this file as the
/// "interface" — change a field here and both the writer (<see cref="Build"/>, called from the
/// step-completion funnel) and the reader (DeclarativeMemoryGaugeUI) move with it.
///
/// (Named DeclarativeStepRecord to avoid the pre-existing, simpler CognitiveStepRecord in
/// AgentCognitiveMemory.cs.)
///
/// A cognitive step always visits exactly ONE of the 16 stations (6 modules + 10 buffers),
/// named by <c>currentCognitiveState</c> / <c>targetObjectName</c> in the RAG JSON
/// (Assets/JsonFile/basicUI_ml2.json → mentalAgents[].steps[]). The record therefore carries a
/// common envelope PLUS one populated per-station entry (<see cref="StationEntry"/>).
///
/// Station id map (from moduleAndBufferMemory):
///   MODULES  cognitive_001 Intentional · 002 Declarative · 003 ProductionMemory ·
///            004 Visual · 005 Manual · 006 Temporal · (007 Aural — reserved)
///   BUFFERS  cognitive_008 Goal · 009 Imaginal · 010 Retrieval · 011 Visual ·
///            012 VisualLocation · 013 Manual · 014 Temporal · (015/016 Aural/Vocal — reserved)
/// </summary>
[Serializable]
public class DeclarativeStepRecord
{
    // ── Identity ──────────────────────────────────────────────────────────────
    public string stepId;          // "t01_cog_s07"
    public int    stepOrder;       // 7
    public string subTaskId;       // "st_1"
    public string branchId;        // "branchA"

    // ── Station (which of the 16) ─────────────────────────────────────────────
    public string stationId;       // "cognitive_010"
    public string stationName;     // "Retrieval Buffer"
    public string stationState;    // currentCognitiveState, e.g. "RetrievalBuffer"
    public CognitiveStationKind stationKind;

    // ── Intent ────────────────────────────────────────────────────────────────
    public string description;

    // ── Data flow (chunks in / chunk out) ─────────────────────────────────────
    public string   producesPayload;    // output key, e.g. "decoded_inputs"
    public string   producedValue;      // the value stored on completion (via TryGetPayload)
    public string[] consumesPayload;    // required input keys (the DAG gate)

    // ── DAG shape ──────────────────────────────────────────────────────────────
    public string   actionType;         // "move" | "act" | "learn"
    public string   agentRole;          // "M" cognitive | "P" physical
    public string[] dependsOn;          // step ids that must complete first
    public bool     isBarrier;          // closes a subtask

    // ── Reward + status (the fields that flip on completion) ───────────────────
    public float correct_step_reward;
    public bool  isActivated;
    public bool  isStepCompleted;

    // ── Imaginal transition (imaginal-buffer steps) ───────────────────────────
    public string imaginalStateBefore;
    public string imaginalStateAfter;
    public string imaginationStatement;

    // ── Timing ────────────────────────────────────────────────────────────────
    public float startTimeSec;
    public float endTimeSec;
    public float expectedDurationSec;

    // ── Goal stack snapshot at completion (Goal Buffer) ───────────────────────
    public GoalStackSnapshot goalStack;

    // ── The one per-station entry that applies to this step ────────────────────
    public StationEntry station;

    // ── Full backend contract for the visited station (optional deep detail) ───
    public string stationContractJson;

    /// <summary>
    /// Build the record for a completed step from the step definition + live blackboard.
    /// This is the single mapping site — edit here when the schema changes.
    /// </summary>
    public static DeclarativeStepRecord Build(ActionSequenceStep s, ZoneDeclarativeMemory mem)
    {
        if (s == null) return null;

        var r = new DeclarativeStepRecord
        {
            stepId       = s.stepId,
            stepOrder    = s.stepOrder,
            subTaskId    = s.subTaskId,
            branchId     = null,
            stationId    = s.targetObjectId,
            stationName  = s.targetObjectName,
            stationState = s.currentCognitiveState,
            stationKind  = KindOf(s),
            description  = s.description,
            producesPayload = s.producesPayload,
            consumesPayload = s.consumesPayload,
            actionType   = s.actionType,
            agentRole    = s.agentRole,
            dependsOn    = s.dependsOn,
            isBarrier    = s.isBarrier,
            correct_step_reward = s.correct_step_reward,
            isActivated  = s.isActivated,
            isStepCompleted = s.isStepCompleted,
            imaginalStateBefore = s.imaginalStateBefore,
            imaginalStateAfter  = s.imaginalStateAfter,
            imaginationStatement = s.imaginalThoughtText,
            startTimeSec = s.startTimeSec,
            endTimeSec   = s.endTimeSec,
            expectedDurationSec = s.expectedDuration,
            stationContractJson = ContractFor(s),
            station = new StationEntry(),
        };

        if (mem != null && !string.IsNullOrWhiteSpace(s.producesPayload)
            && mem.TryGetPayload(s.producesPayload, out string v))
            r.producedValue = v;

        if (mem != null)
            r.goalStack = new GoalStackSnapshot
            {
                smartKeyResult    = mem.goalBufferSmartKeyResult,
                artifactReference = mem.goalBufferArtifactReference,
                subGoal           = mem.goalBufferSubGoal,
                resolvedDesireLevel = mem.goalBufferResolvedDesireLevel,
            };

        r.station.Populate(r, s, mem);
        return r;
    }

    public static CognitiveStationKind KindOf(ActionSequenceStep s)
    {
        // Prefer the station id: cognitive_001–007 are modules, cognitive_008–016 are buffers.
        int n = CognitiveNumber(s?.targetObjectId);
        if (n >= 1 && n <= 7)  return CognitiveStationKind.Module;
        if (n >= 8)            return CognitiveStationKind.Buffer;

        string state = (s?.currentCognitiveState ?? "").ToLowerInvariant();
        string name  = (s?.targetObjectName ?? "").ToLowerInvariant();
        // "Production Memory" is a module but has neither word in its name.
        if (state.Contains("module") || name.Contains("module") || state.Contains("production") || name.Contains("production"))
            return CognitiveStationKind.Module;
        if (state.Contains("buffer") || name.Contains("buffer")) return CognitiveStationKind.Buffer;
        return CognitiveStationKind.Unknown;
    }

    public static int CognitiveNumber(string stationId)
    {
        if (string.IsNullOrEmpty(stationId)
            || !stationId.StartsWith("cognitive_", StringComparison.OrdinalIgnoreCase)) return -1;
        return int.TryParse(stationId.Substring("cognitive_".Length), out int n) ? n : -1;
    }

    /// <summary>True for M-agent / cognitive-station steps (excludes physical press/type steps).</summary>
    public bool IsCognitive =>
        string.Equals(agentRole, "M", StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrEmpty(stationId) && stationId.StartsWith("cognitive_", StringComparison.OrdinalIgnoreCase));

    static string ContractFor(ActionSequenceStep s)
    {
        // Prefer the station-specific contract JSON the step carries, if any.
        if (s == null) return null;
        if (!string.IsNullOrEmpty(s.retrievalBufferContractJson)) return s.retrievalBufferContractJson;
        if (!string.IsNullOrEmpty(s.imaginalBufferContractJson))  return s.imaginalBufferContractJson;
        if (!string.IsNullOrEmpty(s.productionMemoryContractJson)) return s.productionMemoryContractJson;
        if (!string.IsNullOrEmpty(s.visualModuleContractJson))    return s.visualModuleContractJson;
        if (!string.IsNullOrEmpty(s.manualModuleContractJson))    return s.manualModuleContractJson;
        if (!string.IsNullOrEmpty(s.intentionalModuleContractJson)) return s.intentionalModuleContractJson;
        if (!string.IsNullOrEmpty(s.declarativeModuleContractJson)) return s.declarativeModuleContractJson;
        return null;
    }
}

public enum CognitiveStationKind { Unknown, Module, Buffer }

/// <summary>Goal Buffer stack snapshot captured with the step (the "goal stack").</summary>
[Serializable]
public class GoalStackSnapshot
{
    public string bottom;             // reserved for finalStack.bottom
    public string middle;             // reserved for finalStack.middle
    public string top;                // reserved for finalStack.top
    public string smartKeyResult;
    public string artifactReference;
    public string subGoal;
    public float  resolvedDesireLevel;
}

/// <summary>
/// One populated per-station entry per record. Only the field matching the visited station is set.
/// Add / remove / reshape freely — this is the per-station schema surface.
/// </summary>
[Serializable]
public class StationEntry
{
    // Modules
    public IntentionalEntry    intentional;
    public DeclarativeEntry    declarative;
    public ProductionEntry     production;
    public VisualModuleEntry   visualModule;
    public ManualModuleEntry   manualModule;
    public TemporalModuleEntry temporalModule;
    // Buffers
    public GoalBufferEntry     goalBuffer;
    public ImaginalEntry       imaginal;
    public RetrievalEntry      retrieval;
    public VisualBufferEntry   visualBuffer;
    public VisualLocationEntry visualLocation;
    public ManualBufferEntry   manualBuffer;
    public TemporalBufferEntry temporalBuffer;

    /// <summary>Populate the slot matching the visited station from live runtime data.</summary>
    public void Populate(DeclarativeStepRecord r, ActionSequenceStep s, ZoneDeclarativeMemory mem)
    {
        string state = (r.stationState ?? "").ToLowerInvariant();
        string output = !string.IsNullOrWhiteSpace(r.producedValue) ? r.producedValue : r.description;

        if (state.Contains("intentional"))
            intentional = new IntentionalEntry { desire = mem?.intention, intentAction = output };
        else if (state.Contains("declarative"))
            declarative = new DeclarativeEntry { source = output };
        else if (state.Contains("production"))
            production = new ProductionEntry { command = output, targetBuffer = Join(s.productionMemoryConnections) };
        else if (state.Contains("visualmodule") || state.Contains("visual module"))
            visualModule = new VisualModuleEntry { entity = r.stationName, note = output };
        else if (state.Contains("manualmodule") || state.Contains("manual module"))
            manualModule = new ManualModuleEntry { target = s.targetObjectName, note = output };
        else if (state.Contains("temporalmodule") || state.Contains("temporal module"))
            temporalModule = new TemporalModuleEntry { note = output };
        else if (state.Contains("goal"))
            goalBuffer = new GoalBufferEntry { item = output };
        else if (state.Contains("imaginal"))
            imaginal = new ImaginalEntry { stateLabel = r.imaginalStateAfter, imaginationStatement = r.imaginationStatement };
        else if (state.Contains("retrieval"))
            retrieval = new RetrievalEntry { schema = output, retrievalCue = Join(s.consumesPayload) };
        else if (state.Contains("visuallocation") || state.Contains("visual location"))
            visualLocation = new VisualLocationEntry { target = s.targetObjectName };
        else if (state.Contains("visual"))
            visualBuffer = new VisualBufferEntry { entity = output };
        else if (state.Contains("manual"))
            manualBuffer = new ManualBufferEntry { command = s.actionVerb, target = s.targetObjectName };
        else if (state.Contains("temporal"))
            temporalBuffer = new TemporalBufferEntry { action = output };
    }

    static string Join(string[] a) => (a == null || a.Length == 0) ? null : string.Join(",", a);
}

// ── Per-station entries (mirror moduleAndBufferMemory log-entry shapes; extend as needed) ──
[Serializable] public class IntentionalEntry    { public string desire; public string skill; public string intentAction; }
[Serializable] public class DeclarativeEntry    { public string chunkId; public string source; }
[Serializable] public class ProductionEntry     { public string ruleId; public string targetBuffer; public string command; }
[Serializable] public class VisualModuleEntry   { public string entity; public string scanningMode; public string note; }
[Serializable] public class ManualModuleEntry   { public string target; public string effector; public string note; }
[Serializable] public class TemporalModuleEntry { public int elapsedMs; public int remainingMs; public string timeBlock; public string note; }

[Serializable] public class GoalBufferEntry     { public string @event; public string slot; public string item; }
[Serializable] public class ImaginalEntry       { public string stateLabel; public string imaginationStatement; public string command; public string comparisonResult; }
[Serializable] public class RetrievalEntry      { public string schema; public string source; public string retrievalCue; }
[Serializable] public class VisualBufferEntry   { public string entity; public string entityStateBefore; public string entityStateAfter; public string interfaceContext; }
[Serializable] public class VisualLocationEntry { public string target; public float x; public float y; public float z; public string bodyPart; }
[Serializable] public class ManualBufferEntry   { public string command; public string effector; public string target; public string beforeState; public string afterState; public int durationMs; }
[Serializable] public class TemporalBufferEntry { public string action; public string sequencing; public string executionMode; }
