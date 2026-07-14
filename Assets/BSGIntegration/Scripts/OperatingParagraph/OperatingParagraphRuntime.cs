using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace BSG.OperatingParagraph
{
    /// <summary>
    /// Captures the ACTUAL physical operating paragraph as the simulation runs, then assembles + compares
    /// it against the RAG-defined PREDICTED paragraph. Built hierarchically, completed-steps-only:
    ///
    ///   per completed physical step → step_narrative (live pos + rig_bone + measured completionTime)
    ///     → when ALL steps of a sub-task complete → that chunk narrative
    ///       → when ALL sub-tasks complete → the actual complete paragraph
    ///
    /// Two entry points, both computing from the same partial-friendly Compute():
    ///   - GenerateAndCompare(zone) — fired when all PHYSICAL steps complete: console + run_logs JSON.
    ///   - BuildFinalResultsBlockJson(zone) — pulled by MLTrainingResultsWriter into final_game_results.json
    ///     on ANY game end (ideal or mid-run/partial), so even a half-finished run is stored + compared.
    ///
    /// The RAG operatingParagraph is READ-ONLY reference (predicted); nothing here writes back to it.
    /// </summary>
    public static class OperatingParagraphRuntime
    {
        public class ActualStep
        {
            public string stepId;
            public int order;           // completion order within the zone (0-based)
            public bool hasWorldPos;
            public Vector3 worldPos;    // live contact point captured at completion
            public float actualSec = -1f;
        }

        class StepResult
        {
            public string stepId;
            public bool isCompleted;
            public string obj;
            public string stateBefore;
            public string stateAfter;
            public string effector;
            public string rigBone;
            public string expectedNarrative;
            public string actualNarrative;      // only when completed
            public float narrativeSimilarity = -1f;
            public int expectedMs;
            public int actualMs = -1;
            public float actualSec = -1f;
            public bool hasPos;
            public Vector3 pos;
        }

        class SubTaskResult
        {
            public string subTaskId;
            public string stepLabel;
            public string predictedNarrative;
            public string actualNarrative = "";  // only when the whole sub-task completed
            public bool completed;
            public int completedCount;
            public int total;
            public readonly List<StepResult> steps = new List<StepResult>();
        }

        class Result
        {
            public int zoneIndex;
            public string agentId;
            public bool allPhysicalComplete;
            public bool pass;
            public int matched, total, missing, extra;
            public bool orderMatch;
            public float coverage, textSim, meanStepSim, meanAbsTimingDeltaMs;
            public int timedSteps;
            public string predictedComplete = "";
            public string actualComplete = "";   // only when all sub-tasks complete
            public readonly List<SubTaskResult> subTasks = new List<SubTaskResult>();
            public readonly List<string> extraSteps = new List<string>();
        }

        static readonly Dictionary<int, List<ActualStep>> _byZone = new Dictionary<int, List<ActualStep>>();
        static readonly HashSet<int> _reported = new HashSet<int>();
        static readonly string[] PhysicalByZone = { "P1", "P2", "P3", "P4" };

        public static IReadOnlyList<ActualStep> ForZone(int zoneIndex)
            => _byZone.TryGetValue(zoneIndex, out var list) ? list : Array.Empty<ActualStep>();

        public static void ResetZone(int zoneIndex)
        {
            _byZone.Remove(zoneIndex);
            _reported.Remove(zoneIndex);
        }

        // ── Capture ──────────────────────────────────────────────────────────
        public static void RecordPhysicalStep(int zoneIndex, ActionSequenceStep step)
        {
            if (step == null || string.IsNullOrEmpty(step.stepId)) return;

            // Load the predicted paragraph now (during play) so it is cached before any quit hook,
            // when SceneUILoader may already be gone.
            OperatingParagraphCatalog.EnsureLoaded();

            if (!_byZone.TryGetValue(zoneIndex, out var list))
            {
                list = new List<ActualStep>();
                _byZone[zoneIndex] = list;
            }
            if (list.Exists(r => string.Equals(r.stepId, step.stepId, StringComparison.Ordinal)))
                return; // idempotent

            var rec = new ActualStep { stepId = step.stepId, order = list.Count };
            Vector3? pos = ResolveWorldPosition(step, zoneIndex);
            if (pos.HasValue) { rec.hasWorldPos = true; rec.worldPos = pos.Value; }
            list.Add(rec);
        }

        static Vector3? ResolveWorldPosition(ActionSequenceStep step, int zoneIndex)
        {
            var asm = AgentSequenceManager.Instance;
            if (asm == null) return null;
            if (!string.IsNullOrEmpty(step.physicalTarget))
            {
                Vector3? p = asm.GetTargetPositionById(step.physicalTarget, zoneIndex);
                if (p.HasValue) return p;
            }
            if (!string.IsNullOrEmpty(step.physicalTargetId))
            {
                Vector3? p = asm.GetTargetPositionById(step.physicalTargetId, zoneIndex);
                if (p.HasValue) return p;
            }
            return null;
        }

        static Vector3? ResolveContactPoint(OpAction a, int zoneIndex)
        {
            var asm = AgentSequenceManager.Instance;
            if (asm == null || a == null) return null;
            if (!string.IsNullOrEmpty(a.@object))
            {
                Vector3? p = asm.GetTargetPositionById(a.@object, zoneIndex);
                if (p.HasValue) return p;
            }
            if (!string.IsNullOrEmpty(a.main_target_object_id))
            {
                Vector3? p = asm.GetTargetPositionById(a.main_target_object_id, zoneIndex);
                if (p.HasValue) return p;
            }
            return null;
        }

        // ── Compute (partial-friendly) ─────────────────────────────────────────
        static Result Compute(int zoneIndex)
        {
            OperatingParagraphCatalog.EnsureLoaded();

            var res = new Result { zoneIndex = zoneIndex, agentId = ResolvePhysicalAgentId(zoneIndex) };
            res.predictedComplete = OperatingParagraphCatalog.ExpectedComplete ?? "";

            var actual = new List<ActualStep>(ForZone(zoneIndex));
            foreach (ActualStep r in actual)
                if (MLTrainingResultsWriter.TryGetRecordedStepCompletionTime(res.agentId, r.stepId, out float sec) && sec >= 0f)
                    r.actualSec = sec;

            var actualById = new Dictionary<string, ActualStep>(StringComparer.OrdinalIgnoreCase);
            foreach (ActualStep r in actual) actualById[r.stepId] = r;
            var expectedByStep = OperatingParagraphCatalog.ExpectedByStepId;

            var expectedOrder = new List<string>();
            int timed = 0; float totalAbsDeltaMs = 0f, simSum = 0f; int simCount = 0;

            foreach (OpChunk c in OperatingParagraphCatalog.ExpectedChunks)
            {
                var sr = new SubTaskResult { subTaskId = c.sub_task_id, stepLabel = c.step_label, predictedNarrative = c.narrative };
                if (c.actions != null)
                {
                    var completedInChunk = new List<StepResult>();
                    foreach (OpAction a in c.actions)
                    {
                        if (a == null || string.IsNullOrWhiteSpace(a.physical_step_id)) continue;
                        expectedOrder.Add(a.physical_step_id);
                        sr.total++; res.total++;

                        var step = new StepResult
                        {
                            stepId = a.physical_step_id,
                            obj = a.@object,
                            stateBefore = a.state_before,
                            stateAfter = a.state_after,
                            effector = a.effector,
                            rigBone = a.rig_bone,
                            expectedNarrative = a.step_narrative,
                            expectedMs = a.duration_ms,
                        };

                        Vector3? contact = ResolveContactPoint(a, zoneIndex);
                        if (contact.HasValue) { step.hasPos = true; step.pos = contact.Value; }

                        if (actualById.TryGetValue(a.physical_step_id, out ActualStep rec))
                        {
                            step.isCompleted = true;
                            sr.completedCount++; res.matched++;
                            if (rec.hasWorldPos) { step.hasPos = true; step.pos = rec.worldPos; }

                            float durSec = rec.actualSec >= 0f ? rec.actualSec : a.duration_ms / 1000f;
                            step.actualSec = durSec;
                            step.actualMs = rec.actualSec >= 0f ? Mathf.RoundToInt(rec.actualSec * 1000f) : -1;

                            float x = step.hasPos ? step.pos.x : (a.coordinates?.x ?? 0f);
                            float y = step.hasPos ? step.pos.y : (a.coordinates?.y ?? 0f);
                            float z = step.hasPos ? step.pos.z : (a.coordinates?.z ?? 0f);
                            step.actualNarrative = OperatingParagraphText.BuildSentence(a, x, y, z, 0, durSec);
                            step.narrativeSimilarity = OperatingParagraphText.Similarity(step.actualNarrative, a.step_narrative);
                            simSum += step.narrativeSimilarity; simCount++;

                            if (step.actualMs >= 0 && a.duration_ms > 0) { timed++; totalAbsDeltaMs += Math.Abs(step.actualMs - a.duration_ms); }
                            completedInChunk.Add(step);
                        }
                        sr.steps.Add(step);
                    }
                    sr.completed = sr.total > 0 && sr.completedCount == sr.total;

                    // Sub-task narrative only when the WHOLE sub-task completed — combine its step narratives.
                    if (sr.completed)
                        sr.actualNarrative = BuildFlow(completedInChunk);
                }
                res.subTasks.Add(sr);
            }

            // actual COMPLETE = combined narrative of FULLY-completed sub-tasks (their chunk narratives).
            // Partial sub-tasks still contribute per-step narratives in chunks[].steps, but are excluded
            // from complete. So a mid-run end yields the completed sub-tasks (non-empty), and a full run
            // yields the whole paragraph. allPhysicalComplete flags whether it is the full set.
            var completeSteps = new List<StepResult>();
            foreach (SubTaskResult sr in res.subTasks) if (sr.completed) foreach (StepResult s in sr.steps) completeSteps.Add(s);
            bool everySubTaskComplete = res.subTasks.Count > 0;
            foreach (SubTaskResult sr in res.subTasks) if (!sr.completed) everySubTaskComplete = false;
            res.allPhysicalComplete = everySubTaskComplete && res.matched == res.total && res.total > 0;
            res.actualComplete = BuildFlow(completeSteps);

            // For text similarity, compare ALL completed steps (continuous flow) vs predicted complete.
            var allCompleted = new List<StepResult>();
            foreach (SubTaskResult sr in res.subTasks) foreach (StepResult s in sr.steps) if (s.isCompleted) allCompleted.Add(s);
            string actualFlow = BuildFlow(allCompleted);   // completed steps only, continuous

            // Extras (completed but not in the predicted set).
            foreach (ActualStep r in actual) if (!expectedByStep.ContainsKey(r.stepId)) res.extraSteps.Add(r.stepId);

            // Metrics.
            res.missing = res.total - res.matched;
            res.extra = res.extraSteps.Count;
            var actualExpectedSeq = new List<string>();
            foreach (ActualStep r in actual) if (expectedByStep.ContainsKey(r.stepId)) actualExpectedSeq.Add(r.stepId);
            var expectedCompletedSeq = new List<string>();
            foreach (string sid in expectedOrder) if (actualById.ContainsKey(sid)) expectedCompletedSeq.Add(sid);
            res.orderMatch = SequenceEqual(actualExpectedSeq, expectedCompletedSeq);
            res.coverage = res.total > 0 ? (float)res.matched / res.total : (actual.Count == 0 ? 1f : 0f);
            res.textSim = OperatingParagraphText.Similarity(actualFlow, res.predictedComplete);
            res.meanStepSim = simCount > 0 ? simSum / simCount : -1f;
            res.meanAbsTimingDeltaMs = timed > 0 ? totalAbsDeltaMs / timed : -1f;
            res.timedSteps = timed;
            res.pass = res.missing == 0 && res.extra == 0 && res.orderMatch;
            return res;
        }

        /// <summary>Combine step results into one flowing paragraph (running connectors, live duration).</summary>
        static string BuildFlow(List<StepResult> steps)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < steps.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(steps[i].actualNarrative == null ? "" : RethreadConnector(steps[i].actualNarrative, i));
            }
            return sb.ToString();
        }

        /// <summary>The actual narrative was built at index 0 (standalone). For positions after the first in a
        /// flow, swap the "Over X seconds, the P1 physical agent (Marketing Manager)" lead for the connector form.</summary>
        static string RethreadConnector(string standalone, int index)
        {
            if (index == 0 || string.IsNullOrEmpty(standalone)) return standalone;
            const string marker = "the P1 physical agent (Marketing Manager) ";
            int m = standalone.IndexOf(marker, StringComparison.Ordinal);
            // standalone = "Over X seconds, the P1 physical agent (Marketing Manager) <verb...>."
            int over = standalone.IndexOf("Over ", StringComparison.Ordinal);
            if (m < 0 || over != 0) return standalone;
            string durPart = standalone.Substring(0, m).TrimEnd();       // "Over X seconds,"
            string rest = standalone.Substring(m + marker.Length);        // "<verb...>."
            // "Over X seconds," → "over X seconds,"
            if (durPart.StartsWith("Over ")) durPart = "over " + durPart.Substring(5);
            return $"{OperatingParagraphText.Connector(index)} {durPart} it {rest}";
        }

        // ── Entry point 1: all-physical-complete → console + run_logs JSON ──────
        public static void GenerateAndCompare(int zoneIndex)
        {
            if (_reported.Contains(zoneIndex)) return;
            _reported.Add(zoneIndex);

            Result res = Compute(zoneIndex);
            LogResult(res);
            try
            {
                string dir = MLTrainingResultsWriter.ResolveRunLogsDirStatic();
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "operating_paragraph_comparison.json"), BuildBlockJson(res, ""));
                    Debug.Log($"[OperatingParagraph] Wrote comparison → {Path.Combine(dir, "operating_paragraph_comparison.json")}");
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[OperatingParagraph] write failed: {ex.Message}"); }
        }

        // ── Entry point 2: pulled by MLTrainingResultsWriter (any game end, partial ok) ──
        public static string BuildFinalResultsBlockJson(int zoneIndex, string baseIndent)
        {
            try
            {
                Result res = Compute(zoneIndex);
                return BuildBlockJson(res, baseIndent);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OperatingParagraph] final-results block failed: {ex.Message}");
                return baseIndent + "{ \"error\": \"operating paragraph unavailable\" }";
            }
        }

        static void LogResult(Result res)
        {
            string textSimStr = (res.textSim * 100f).ToString("0.0", CultureInfo.InvariantCulture);
            string stepSimStr = res.meanStepSim >= 0f ? (res.meanStepSim * 100f).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "-";
            string timingStr = res.meanAbsTimingDeltaMs >= 0f ? $"{res.meanAbsTimingDeltaMs.ToString("0", CultureInfo.InvariantCulture)}ms" : "n/a";

            Debug.Log($"[OperatingParagraph] RESULT Zone {res.zoneIndex} ({res.agentId}): {(res.pass ? "PASS ✅" : "MISMATCH ⚠")} | steps {res.matched}/{res.total} | order {(res.orderMatch ? "MATCH" : "MISMATCH")} | textSim {textSimStr}% | stepSim {stepSimStr} | timing {timingStr} | extra {res.extra} | allPhysicalComplete {res.allPhysicalComplete} → see run_logs/operating_paragraph_comparison.json");

            string actualShown = !string.IsNullOrEmpty(res.actualComplete) ? res.actualComplete : BuildActualFlowForLog(res);
            Debug.Log($"[OperatingParagraph] Generate Actual Operating Paragraph (Zone {res.zoneIndex}):\n" +
                      (string.IsNullOrEmpty(actualShown) ? "(no physical steps completed)" : actualShown));

            var log = new StringBuilder();
            log.AppendLine($"[OperatingParagraph] Compare Expected vs Actual — Zone {res.zoneIndex} ({res.agentId}):");
            foreach (SubTaskResult sr in res.subTasks)
            {
                log.AppendLine($"  • {sr.subTaskId} \"{sr.stepLabel}\" — {sr.completedCount}/{sr.total} step(s) completed");
                foreach (StepResult s in sr.steps)
                {
                    string sim = s.narrativeSimilarity >= 0f ? $"{(s.narrativeSimilarity * 100f).ToString("0", CultureInfo.InvariantCulture)}%" : "-";
                    string t = s.actualMs >= 0 ? $"{s.actualMs}ms vs {s.expectedMs}ms" : $"expected {s.expectedMs}ms";
                    log.AppendLine($"      {(s.isCompleted ? "✓" : "✗")} {s.stepId} [{s.obj} {s.stateBefore}->{s.stateAfter}] narrative {sim} | {t}");
                }
            }
            log.AppendLine($"  Overall: coverage {res.matched}/{res.total} | missing {res.missing} | extra {res.extra} | order {(res.orderMatch ? "MATCH" : "MISMATCH")} | text sim {textSimStr}% | mean step-narrative sim {stepSimStr}");
            if (res.extraSteps.Count > 0) log.AppendLine("  extra (not in expected): " + string.Join(", ", res.extraSteps));
            Debug.Log(log.ToString());
        }

        static string BuildActualFlowForLog(Result res)
        {
            var completed = new List<StepResult>();
            foreach (SubTaskResult sr in res.subTasks) foreach (StepResult s in sr.steps) if (s.isCompleted) completed.Add(s);
            return BuildFlow(completed);
        }

        // ── Serialization of the full block (predicted + actual + comparison), pretty-printed ──
        static string BuildBlockJson(Result res, string ind)
        {
            string i1 = ind + "  ", i2 = ind + "    ", i3 = ind + "      ";
            var sb = new StringBuilder();
            sb.Append(ind).Append("{\n");
            FRaw(sb, i1, "zoneIndex", res.zoneIndex.ToString(CultureInfo.InvariantCulture), true);
            FStr(sb, i1, "agentId", res.agentId, true);
            FStr(sb, i1, "scope", "physical", true);
            FRaw(sb, i1, "allPhysicalComplete", B(res.allPhysicalComplete), true);

            // comparison
            sb.Append(i1).Append("\"comparison\": {\n");
            FRaw(sb, i2, "pass", B(res.pass), true);
            FRaw(sb, i2, "matched", res.matched.ToString(CultureInfo.InvariantCulture), true);
            FRaw(sb, i2, "total", res.total.ToString(CultureInfo.InvariantCulture), true);
            FRaw(sb, i2, "missing", res.missing.ToString(CultureInfo.InvariantCulture), true);
            FRaw(sb, i2, "extra", res.extra.ToString(CultureInfo.InvariantCulture), true);
            FRaw(sb, i2, "orderMatch", B(res.orderMatch), true);
            FRaw(sb, i2, "coverage", res.coverage.ToString("0.000", CultureInfo.InvariantCulture), true);
            FRaw(sb, i2, "textSimilarity", res.textSim.ToString("0.000", CultureInfo.InvariantCulture), true);
            FRaw(sb, i2, "meanStepNarrativeSimilarity", res.meanStepSim >= 0f ? res.meanStepSim.ToString("0.000", CultureInfo.InvariantCulture) : "-1", true);
            FRaw(sb, i2, "timedSteps", res.timedSteps.ToString(CultureInfo.InvariantCulture), true);
            FRaw(sb, i2, "meanAbsTimingDeltaMs", res.meanAbsTimingDeltaMs >= 0f ? res.meanAbsTimingDeltaMs.ToString("0.0", CultureInfo.InvariantCulture) : "-1", false);
            sb.Append(i1).Append("},\n");

            // predicted (reference from RAG)
            sb.Append(i1).Append("\"predicted\": {\n");
            FStr(sb, i2, "complete", res.predictedComplete, true);
            sb.Append(i2).Append("\"chunks\": [\n");
            for (int c = 0; c < res.subTasks.Count; c++)
                WriteChunk(sb, res.subTasks[c], i3, c < res.subTasks.Count - 1, actual: false);
            sb.Append(i2).Append("]\n");
            sb.Append(i1).Append("},\n");

            // actual (completed-steps-only)
            sb.Append(i1).Append("\"actual\": {\n");
            FStr(sb, i2, "complete", res.actualComplete, true);
            sb.Append(i2).Append("\"chunks\": [\n");
            for (int c = 0; c < res.subTasks.Count; c++)
                WriteChunk(sb, res.subTasks[c], i3, c < res.subTasks.Count - 1, actual: true);
            sb.Append(i2).Append("]\n");
            sb.Append(i1).Append("}\n");

            sb.Append(ind).Append("}");
            return sb.ToString();
        }

        static void WriteChunk(StringBuilder sb, SubTaskResult sr, string ind, bool comma, bool actual)
        {
            string i1 = ind + "  ", i2 = ind + "    ";
            sb.Append(ind).Append("{\n");
            FStr(sb, i1, "subTaskId", sr.subTaskId, true);
            FStr(sb, i1, "stepLabel", sr.stepLabel, true);
            if (actual)
            {
                FRaw(sb, i1, "completed", B(sr.completed), true);
                FRaw(sb, i1, "completedSteps", sr.completedCount.ToString(CultureInfo.InvariantCulture), true);
                FRaw(sb, i1, "totalSteps", sr.total.ToString(CultureInfo.InvariantCulture), true);
                // chunk narrative only when the whole sub-task completed; else "" (per-step narratives live in steps[])
                FStr(sb, i1, "narrative", sr.actualNarrative, true);
            }
            else
            {
                FStr(sb, i1, "narrative", sr.predictedNarrative, true);
            }

            // predicted lists all steps; actual lists COMPLETED steps only.
            var steps = new List<StepResult>();
            foreach (StepResult st in sr.steps) if (!actual || st.isCompleted) steps.Add(st);

            sb.Append(i1).Append("\"steps\": [\n");
            for (int s = 0; s < steps.Count; s++)
                WriteStep(sb, steps[s], i2, s < steps.Count - 1, actual);
            sb.Append(i1).Append("]\n");

            sb.Append(ind).Append("}").Append(comma ? "," : "").Append("\n");
        }

        static void WriteStep(StringBuilder sb, StepResult st, string ind, bool comma, bool actual)
        {
            string i1 = ind + "  ";
            sb.Append(ind).Append("{\n");
            FStr(sb, i1, "physical_step_id", st.stepId, true);
            FStr(sb, i1, "object", st.obj, true);
            FStr(sb, i1, "state_before", st.stateBefore, true);
            FStr(sb, i1, "state_after", st.stateAfter, true);
            FStr(sb, i1, "rig_bone", st.rigBone, true);
            if (actual)
            {
                string wp = st.hasPos ? $"({st.pos.x.ToString("0.00", CultureInfo.InvariantCulture)},{st.pos.y.ToString("0.00", CultureInfo.InvariantCulture)},{st.pos.z.ToString("0.00", CultureInfo.InvariantCulture)})" : "";
                string durMs = st.actualMs >= 0 ? st.actualMs.ToString(CultureInfo.InvariantCulture) : "null";
                int delta = (st.actualMs >= 0 && st.expectedMs > 0) ? st.actualMs - st.expectedMs : int.MinValue;
                FStr(sb, i1, "worldPos", wp, true);
                FRaw(sb, i1, "durationMs", durMs, true);
                FRaw(sb, i1, "expectedDurationMs", st.expectedMs.ToString(CultureInfo.InvariantCulture), true);
                FRaw(sb, i1, "deltaMs", delta != int.MinValue ? delta.ToString(CultureInfo.InvariantCulture) : "null", true);
                FRaw(sb, i1, "narrativeSimilarity", st.narrativeSimilarity >= 0f ? st.narrativeSimilarity.ToString("0.000", CultureInfo.InvariantCulture) : "null", true);
                FStr(sb, i1, "step_narrative", st.actualNarrative, false);
            }
            else
            {
                FRaw(sb, i1, "duration_ms", st.expectedMs.ToString(CultureInfo.InvariantCulture), true);
                FStr(sb, i1, "step_narrative", st.expectedNarrative, false);
            }
            sb.Append(ind).Append("}").Append(comma ? "," : "").Append("\n");
        }

        /// <summary>Write a "key": "escaped string" field line.</summary>
        static void FStr(StringBuilder sb, string ind, string key, string val, bool comma)
            => sb.Append(ind).Append('"').Append(key).Append("\": \"").Append(Esc(val)).Append('"').Append(comma ? "," : "").Append('\n');

        /// <summary>Write a "key": rawValue field line (numbers / bools / null).</summary>
        static void FRaw(StringBuilder sb, string ind, string key, string raw, bool comma)
            => sb.Append(ind).Append('"').Append(key).Append("\": ").Append(raw).Append(comma ? "," : "").Append('\n');

        static bool SequenceEqual(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            return true;
        }

        static string ResolvePhysicalAgentId(int zoneIndex)
            => (zoneIndex >= 0 && zoneIndex < PhysicalByZone.Length) ? PhysicalByZone[zoneIndex] : "P1";

        static string B(bool b) => b ? "true" : "false";

        static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t");
        }
    }
}
