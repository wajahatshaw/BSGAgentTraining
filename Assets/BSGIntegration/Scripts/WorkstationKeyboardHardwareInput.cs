using System.Collections.Generic;
using UnityEngine;
using BSG.OperatingParagraph;

/// <summary>
/// Lets P1's real hardware keyboard drive the workstation keys through the SAME completion path as the
/// agent's IK press (NotifyStepCompleted → state flip + DAG + press visual), each press completing the
/// next authored operatingParagraph step for that key. Attach to any workstation-scene GameObject.
/// </summary>
public class WorkstationKeyboardHardwareInput : MonoBehaviour
{
    [System.Serializable]
    public struct KeyBinding
    {
        [Tooltip("Real key P1 presses on the physical keyboard.")]
        public KeyCode keyCode;

        [Tooltip("Workstation meronym object this key drives (e.g. p_key, enter_key, tab_key).")]
        public string meronym;

        [Tooltip("If the meronym has no authored operatingParagraph state (e.g. tab_key), only flash " +
                 "the green press visual and update no tracked state.")]
        public bool visualOnly;
    }

    [Header("Target")]
    [Tooltip("Workstation zone. P1 = zone 0.")]
    [SerializeField] private int zoneIndex = 0;

    [Header("Behaviour")]
    [SerializeField] private bool enableHardwareKeys = true;

    [Tooltip("Also flash the green press visual (ButtonPressContact) on each hardware press, like a physical contact.")]
    [SerializeField] private bool triggerPressVisual = true;

    [Tooltip("Real key → workstation key bindings. Defaults: P→p_key, Enter→enter_key, Tab→tab_key (visual-only).")]
    [SerializeField]
    private List<KeyBinding> bindings = new List<KeyBinding>
    {
        new KeyBinding { keyCode = KeyCode.P,      meronym = "p_key",     visualOnly = false },
        new KeyBinding { keyCode = KeyCode.Return, meronym = "enter_key", visualOnly = false },
        new KeyBinding { keyCode = KeyCode.Tab,    meronym = "tab_key",   visualOnly = true  },
    };

    // meronym → ordered authored press-step ids (from the operatingParagraph catalog, resolved lazily).
    private readonly Dictionary<string, List<string>> _stepsByMeronym =
        new Dictionary<string, List<string>>(System.StringComparer.OrdinalIgnoreCase);

    // Steps already fired, so repeated presses advance through the sequence.
    private readonly HashSet<string> _firedSteps = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

    private bool _resolved;

    private void Update()
    {
        if (!enableHardwareKeys) return;

        for (int i = 0; i < bindings.Count; i++)
        {
            KeyBinding b = bindings[i];
            if (b.keyCode == KeyCode.None || string.IsNullOrEmpty(b.meronym)) continue;
            if (!Input.GetKeyDown(b.keyCode)) continue;
            PressWorkstationKey(b.meronym, b.visualOnly);
        }
    }

    // Simulate a P1 keypress: completes the next authored press step for the key (unless visualOnly).
    // Public so it can also be driven from UnityEvents / UI buttons.
    public void PressWorkstationKey(string meronym, bool visualOnly)
    {
        if (triggerPressVisual)
            FlashPressVisual(meronym);

        if (visualOnly)
        {
            Debug.Log($"[WorkstationKbd] '{meronym}' visual-only press (no authored operatingParagraph state to update).");
            return;
        }

        string stepId = NextUncompletedStep(meronym);
        if (string.IsNullOrEmpty(stepId))
        {
            Debug.LogWarning($"[WorkstationKbd] '{meronym}': no remaining authored press step to complete " +
                             "(all done, or none authored in the operatingParagraph).");
            return;
        }

        _firedSteps.Add(stepId);

        // Same entry point the agent's IK press reaches (state flip + OP record + DAG), keyed by this step id.
        CognitivePhaseOrchestrator.GetOrCreateForZone(zoneIndex).NotifyStepCompleted(stepId);
        Debug.Log($"[WorkstationKbd] P1 hardware press '{meronym}' → completed step {stepId} (zone {zoneIndex}).");
    }

    // Clear fired-step tracking (e.g. on a new episode) so the key sequences replay.
    public void ResetPressState() => _firedSteps.Clear();

    private string NextUncompletedStep(string meronym)
    {
        EnsureResolved();
        if (!_stepsByMeronym.TryGetValue(meronym, out List<string> steps) || steps == null)
            return null;

        foreach (string stepId in steps)
        {
            if (_firedSteps.Contains(stepId)) continue;
            // Skip steps the agent already pressed, so hardware picks up where it left off.
            if (BSG.IdentityStatement.ObjectStateRegistry.WasStepVerified(zoneIndex, stepId)) continue;
            return stepId;
        }
        return null;
    }

    private void FlashPressVisual(string meronym)
    {
        GameObject key = MeronymPartRegistry.GetByName(meronym, zoneIndex)
                         ?? MeronymPartRegistry.GetByName(meronym, 0);
        if (key != null)
            ButtonPressContact.EnsureOn(key)?.Press();
    }

    // Build meronym → ordered press-step-ids from the operatingParagraph; retries until the catalog loads.
    private void EnsureResolved()
    {
        if (_resolved) return;
        if (!OperatingParagraphCatalog.EnsureLoaded()) return;

        _stepsByMeronym.Clear();
        foreach (OpChunk c in OperatingParagraphCatalog.ExpectedChunks)
        {
            if (c?.actions == null) continue;
            foreach (OpAction a in c.actions)
            {
                if (a == null || string.IsNullOrEmpty(a.@object) || string.IsNullOrWhiteSpace(a.physical_step_id))
                    continue;
                if (!_stepsByMeronym.TryGetValue(a.@object, out List<string> list))
                {
                    list = new List<string>();
                    _stepsByMeronym[a.@object] = list;
                }
                list.Add(a.physical_step_id);
            }
        }
        _resolved = true;
    }
}
