using System;
using UnityEngine;

/// <summary>
/// Cross-assembly hook fired when a physical step COMPLETES — raised from
/// CognitivePhaseOrchestrator.NotifyStepCompleted's physical branch with the step's own target meronym
/// (step.physicalTarget). It is driven purely by step completion, NOT by the operatingParagraph state
/// flip, so an OP parsing/catalog error can never suppress typing. Reached by BOTH the physical agent's
/// KleinFrame+IK press and P1's hardware key (WorkstationKeyboardHardwareInput). Lives in the
/// BSGIntegration assembly so the orchestrator can raise it; the VirtualKeyboard in Assembly-CSharp (which
/// cannot be referenced from here) subscribes and maps the meronym to the text it types.
///
/// The payload is the pressed key's meronym name (e.g. "p_key", "enter_key").
/// </summary>
public static class WorkstationKeyPressEvents
{
    /// <summary>Raised once per keystroke with the pressed key's meronym name.</summary>
    public static event Action<string> KeyPressed;

    public static void Raise(string meronym)
    {
        Debug.Log($"Letter meronym: {meronym}");
        if (!string.IsNullOrEmpty(meronym))
            KeyPressed?.Invoke(meronym);
    }
}
