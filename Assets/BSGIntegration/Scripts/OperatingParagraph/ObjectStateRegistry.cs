using System;
using System.Collections.Generic;
using UnityEngine;
using BSG.OperatingParagraph;

namespace BSG.IdentityStatement
{
    /// <summary>
    /// Live per-object state model for the PHYSICAL scene objects. Where ZoneDeclarativeMemory records the
    /// motor frame per STEP (keyed by kleinFrameId/stepId) on a verified green contact, this holds the
    /// running CURRENT state per OBJECT and flips it to <c>state_after</c> the moment a physical step
    /// completes with verified contact — so the actual Identity Statement reflects genuinely-tracked state.
    ///
    /// Seeded from the RAG operatingParagraph: every object starts in the <c>state_before</c> of its first
    /// authored step. A completed step (which, for press/depress, can only finish AFTER
    /// KleinFrameExecutor.PressContactAchieved — see RagSequenceAgentMover.FinishStepDwellAndComplete) applies
    /// its transition; a step that never lands never completes, so it never mutates state here.
    /// </summary>
    public static class ObjectStateRegistry
    {
        public class Transition
        {
            public string stepId;
            public string before;          // the object's tracked state just before this step
            public string after;           // tracked state after (= state_after when verified)
            public string expectedBefore;  // authored precondition
            public bool contactVerified;
            public bool preconditionMatch; // tracked-before == authored-before (false = object wasn't where the step expected)
            public int order;
        }

        class ObjState
        {
            public string current;
            public readonly List<Transition> transitions = new List<Transition>();
        }

        static readonly Dictionary<int, Dictionary<string, ObjState>> _byZone = new Dictionary<int, Dictionary<string, ObjState>>();
        static readonly Dictionary<int, int> _order = new Dictionary<int, int>();
        static readonly HashSet<int> _seeded = new HashSet<int>();

        public static void ResetZone(int zoneIndex)
        {
            _byZone.Remove(zoneIndex);
            _order.Remove(zoneIndex);
            _seeded.Remove(zoneIndex);
        }

        /// <summary>Seed every physical object to its first authored <c>state_before</c> (its "before" baseline).</summary>
        static Dictionary<string, ObjState> EnsureZone(int zoneIndex)
        {
            if (!_byZone.TryGetValue(zoneIndex, out var map))
            {
                map = new Dictionary<string, ObjState>(StringComparer.OrdinalIgnoreCase);
                _byZone[zoneIndex] = map;
            }
            if (_seeded.Contains(zoneIndex)) return map;

            OperatingParagraphCatalog.EnsureLoaded();
            foreach (OpChunk c in OperatingParagraphCatalog.ExpectedChunks)
            {
                if (c?.actions == null) continue;
                foreach (OpAction a in c.actions)
                {
                    if (a == null || string.IsNullOrWhiteSpace(a.physical_step_id) || string.IsNullOrEmpty(a.@object)) continue;
                    if (!map.ContainsKey(a.@object))
                        map[a.@object] = new ObjState { current = a.state_before };
                }
            }
            _seeded.Add(zoneIndex);
            return map;
        }

        /// <summary>Apply a COMPLETED physical step's transition. For press/depress steps, completion already
        /// implies verified contact (the mover blocks finishing otherwise); RESTING is always verified.</summary>
        public static void ApplyCompletedStep(int zoneIndex, string stepId, bool contactVerified = true)
        {
            if (string.IsNullOrEmpty(stepId)) return;
            OperatingParagraphCatalog.EnsureLoaded();
            if (!OperatingParagraphCatalog.ExpectedByStepId.TryGetValue(stepId, out OpAction a) || a == null || string.IsNullOrEmpty(a.@object))
                return; // not a tracked physical object (e.g. cognitive step)

            var map = EnsureZone(zoneIndex);
            if (!map.TryGetValue(a.@object, out ObjState st))
            {
                st = new ObjState { current = a.state_before };
                map[a.@object] = st;
            }

            string before = st.current ?? a.state_before;
            bool pre = string.Equals(before, a.state_before, StringComparison.OrdinalIgnoreCase);
            string after = contactVerified ? a.state_after : before;

            int ord = _order.TryGetValue(zoneIndex, out int o) ? o : 0;
            _order[zoneIndex] = ord + 1;

            st.transitions.Add(new Transition
            {
                stepId = stepId, before = before, after = after,
                expectedBefore = a.state_before, contactVerified = contactVerified,
                preconditionMatch = pre, order = ord
            });
            if (contactVerified) st.current = a.state_after;

            Debug.Log($"[ObjectState] zone {zoneIndex}: '{a.@object}' {before} → {after} (step {stepId}, contactVerified={contactVerified}{(pre ? "" : ", PRECONDITION MISMATCH expected " + a.state_before)}).");
        }

        public static string CurrentState(int zoneIndex, string objectName)
        {
            if (_byZone.TryGetValue(zoneIndex, out var map) && objectName != null && map.TryGetValue(objectName, out ObjState st))
                return st.current;
            return null;
        }

        /// <summary>True if the given step recorded a verified transition in this zone.</summary>
        public static bool WasStepVerified(int zoneIndex, string stepId)
        {
            if (string.IsNullOrEmpty(stepId) || !_byZone.TryGetValue(zoneIndex, out var map)) return false;
            foreach (var kv in map)
                foreach (Transition t in kv.Value.transitions)
                    if (string.Equals(t.stepId, stepId, StringComparison.Ordinal)) return t.contactVerified;
            return false;
        }

        public static IReadOnlyList<Transition> TransitionsForObject(int zoneIndex, string objectName)
        {
            if (_byZone.TryGetValue(zoneIndex, out var map) && objectName != null && map.TryGetValue(objectName, out ObjState st))
                return st.transitions;
            return Array.Empty<Transition>();
        }
    }
}
