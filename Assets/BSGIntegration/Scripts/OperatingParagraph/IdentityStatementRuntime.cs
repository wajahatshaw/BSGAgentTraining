using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using BSG.OperatingParagraph;

namespace BSG.IdentityStatement
{
    /// <summary>
    /// Builds the ACTUAL Identity Statement for the P1 physical agent and compares it against the
    /// PREDICTED (RAG) one, then serialises predicted + actual + comparison — mirroring
    /// <see cref="OperatingParagraphRuntime"/>. Partial-friendly: written on ANY game end.
    ///
    ///   - GenerateAndCompare(zone) — console + run_logs/identity_statement_comparison.json.
    ///   - BuildFinalResultsBlockJson(zone) — pulled by MLTrainingResultsWriter into final_game_results.json.
    ///
    /// Scope: interactable scene objects touched by PHYSICAL steps (the agent's own body parts excluded).
    /// Comparison basis = the physical-actionable subset derived from operatingParagraph chunks, so it is
    /// scoped exactly like the OP; cognitive-only objects are not counted against the agent.
    /// </summary>
    public static class IdentityStatementRuntime
    {
        const string AgentId = "P1";
        const string AgentName = "Marketing Manager";

        /// <summary>One state update of an object by a single physical step — lets the same object show
        /// multiple transitions in different steps within the same sub-task (e.g. enter_key s03 + s04).</summary>
        class StepUpdate
        {
            public string stepId;
            public string from;                // object state before this step (always known)
            public string to;                  // state_after — null/empty until the step COMPLETES successfully
            public int order = int.MaxValue;   // completion order (actual)
            public int ms = -1;                // measured duration (actual)
            public bool completed;             // did this physical step complete? (actual)
            public bool contactVerified;       // green / verified physical contact (actual)
        }

        class ObjChange
        {
            public string obj;
            public string parentName;
            public string parentId;
            public string from;                // baseline state before any step (authored state_before)
            public string to;                  // net after-state — null until at least one step completes
            public readonly List<string> stepIds = new List<string>();
            public readonly List<StepUpdate> updates = new List<StepUpdate>();
            public int order = int.MaxValue;   // first completion order (actual) — for stable grouping
            public int durationMs = -1;        // measured (actual) sum, -1 if unknown
            public string finalState;          // live tracked state (ObjectStateRegistry): from until completed, then to
        }

        class SubTask
        {
            public string subTaskId;           // "st_1" …
            public string stepLabel;
            public string predictedStatement = "";
            public string actualStatement = "";
            public float statementSimilarity = -1f;
            public bool completed;
            public int totalObjects;
            public int completedObjects;
            public readonly List<ObjChange> expected = new List<ObjChange>();
            public readonly List<ObjChange> actual = new List<ObjChange>();
        }

        class Result
        {
            public string agentId = AgentId;
            public string agent = AgentName;
            public bool allPhysicalComplete;
            public bool pass;
            public int objectsExpected, objectsMatched, objectsMissing, objectsExtra, transitionMismatches;
            public float coverage;
            public string predictedOverview = "";
            public string actualOverview = "";
            public float overviewSimilarity = -1f;
            public float meanSubTaskSimilarity = -1f;
            public int subTasksExpected, subTasksComplete;
            public readonly List<SubTask> subTasks = new List<SubTask>();
            // Task-level (overview) aggregated changes across all physical sub-tasks.
            public readonly List<ObjChange> overviewExpected = new List<ObjChange>();
            public readonly List<ObjChange> overviewActual = new List<ObjChange>();
        }

        static readonly HashSet<int> _reported = new HashSet<int>();

        public static void ResetZone(int zoneIndex) => _reported.Remove(zoneIndex);

        // ── Compute (partial-friendly) ─────────────────────────────────────────
        static Result Compute(int zoneIndex)
        {
            OperatingParagraphCatalog.EnsureLoaded();
            IdentityStatementCatalog.EnsureLoaded();

            var res = new Result();
            if (IdentityStatementCatalog.Overview != null)
                res.predictedOverview = IdentityStatementCatalog.Overview.statement ?? "";

            // Which physical steps actually completed (+ their completion order + measured duration).
            var completedById = new Dictionary<string, OperatingParagraphRuntime.ActualStep>(StringComparer.OrdinalIgnoreCase);
            foreach (OperatingParagraphRuntime.ActualStep r in OperatingParagraphRuntime.ForZone(zoneIndex))
                completedById[r.stepId] = r;

            int simCount = 0; float simSum = 0f;
            var globalExpected = new Dictionary<string, ObjChange>(StringComparer.OrdinalIgnoreCase);
            var globalActual = new Dictionary<string, ObjChange>(StringComparer.OrdinalIgnoreCase);

            foreach (OpChunk c in OperatingParagraphCatalog.ExpectedChunks)
            {
                if (c == null || c.actions == null) continue;

                // Only chunks that carry physical actions are physical sub-tasks.
                var expectedByObj = new Dictionary<string, ObjChange>(StringComparer.OrdinalIgnoreCase);
                bool anyPhysical = false;

                foreach (OpAction a in c.actions)
                {
                    if (a == null || string.IsNullOrWhiteSpace(a.physical_step_id) || string.IsNullOrEmpty(a.@object)) continue;
                    anyPhysical = true;

                    AccumulateExpected(expectedByObj, a);
                    AccumulateExpected(globalExpected, a);

                    // completed-only global map drives the comparison metrics + overview statement.
                    if (completedById.TryGetValue(a.physical_step_id, out var rec))
                    {
                        int ms = -1;
                        if (MLTrainingResultsWriter.TryGetRecordedStepCompletionTime(AgentId, a.physical_step_id, out float sec) && sec >= 0f)
                            ms = Mathf.RoundToInt(sec * 1000f);
                        AccumulateActual(globalActual, a, rec.order, ms);
                    }
                }
                if (!anyPhysical) continue;

                var st = new SubTask { subTaskId = c.sub_task_id, stepLabel = c.step_label };
                st.expected.AddRange(Ordered(expectedByObj));
                // ACTUAL view = every expected object, with state_after GATED on real completion (null until done).
                st.actual.AddRange(BuildActualView(zoneIndex, expectedByObj, completedById));
                st.totalObjects = st.expected.Count;
                int fullyDone = 0;
                foreach (ObjChange av in st.actual)
                {
                    bool all = av.updates.Count > 0;
                    foreach (StepUpdate u in av.updates) if (!u.completed) all = false;
                    if (all) fullyDone++;
                }
                st.completedObjects = fullyDone;
                st.completed = st.totalObjects > 0 && fullyDone == st.totalObjects;

                if (IdentityStatementCatalog.BySubTaskEntryId.TryGetValue(c.sub_task_id ?? "", out IsEntry pe) && pe != null)
                    st.predictedStatement = pe.statement ?? "";
                st.actualStatement = BuildSubTaskStatement(st);
                if (!string.IsNullOrEmpty(st.predictedStatement))
                {
                    st.statementSimilarity = OperatingParagraphText.Similarity(st.actualStatement, st.predictedStatement);
                    simSum += st.statementSimilarity; simCount++;
                }

                res.subTasks.Add(st);
            }

            // Global metrics (scope = physical-actionable objects, deduped across sub-tasks).
            res.objectsExpected = globalExpected.Count;
            int matched = 0, mismatch = 0;
            foreach (var kv in globalExpected)
            {
                if (globalActual.TryGetValue(kv.Key, out ObjChange act))
                {
                    matched++;
                    if (!TransitionEqual(kv.Value, act)) mismatch++;
                }
            }
            foreach (var kv in globalActual) if (!globalExpected.ContainsKey(kv.Key)) res.objectsExtra++;
            res.objectsMatched = matched;
            res.objectsMissing = res.objectsExpected - matched;
            res.transitionMismatches = mismatch;
            res.coverage = res.objectsExpected > 0 ? (float)matched / res.objectsExpected : 1f;

            res.subTasksExpected = res.subTasks.Count;
            foreach (SubTask st in res.subTasks) if (st.completed) res.subTasksComplete++;

            // Task-level (overview) change lists: expected = every object once; actual = gated all-objects view.
            res.overviewExpected.AddRange(Ordered(globalExpected));
            res.overviewActual.AddRange(BuildActualView(zoneIndex, globalExpected, completedById));

            res.allPhysicalComplete = res.objectsExpected > 0 && res.objectsMissing == 0 && mismatch == 0;
            res.actualOverview = BuildOverviewStatement(res, globalActual);
            if (!string.IsNullOrEmpty(res.predictedOverview))
                res.overviewSimilarity = OperatingParagraphText.Similarity(res.actualOverview, res.predictedOverview);
            res.meanSubTaskSimilarity = simCount > 0 ? simSum / simCount : -1f;
            res.pass = res.objectsMissing == 0 && res.objectsExtra == 0 && mismatch == 0 && res.objectsExpected > 0;
            return res;
        }

        static void AccumulateExpected(Dictionary<string, ObjChange> map, OpAction a)
        {
            if (!map.TryGetValue(a.@object, out ObjChange ch))
            {
                var pr = IdentityStatementCatalog.ParentOf(a.@object);
                ch = new ObjChange { obj = a.@object, parentName = pr.parentName, parentId = pr.parentId, from = a.state_before };
                map[a.@object] = ch;
            }
            ch.to = a.state_after;                 // last write wins → net after-state
            if (!ch.stepIds.Contains(a.physical_step_id))
            {
                ch.stepIds.Add(a.physical_step_id);
                ch.updates.Add(new StepUpdate { stepId = a.physical_step_id, from = a.state_before, to = a.state_after });
            }
        }

        static void AccumulateActual(Dictionary<string, ObjChange> map, OpAction a, int order, int ms)
        {
            if (!map.TryGetValue(a.@object, out ObjChange ch))
            {
                var pr = IdentityStatementCatalog.ParentOf(a.@object);
                ch = new ObjChange { obj = a.@object, parentName = pr.parentName, parentId = pr.parentId, from = a.state_before, durationMs = 0 };
                map[a.@object] = ch;
            }
            ch.to = a.state_after;
            if (order < ch.order) ch.order = order;
            if (!ch.stepIds.Contains(a.physical_step_id))
            {
                ch.stepIds.Add(a.physical_step_id);
                ch.updates.Add(new StepUpdate { stepId = a.physical_step_id, from = a.state_before, to = a.state_after, order = order, ms = ms });
            }
            if (ms >= 0) ch.durationMs = Math.Max(0, ch.durationMs) + ms;
        }

        static List<ObjChange> Ordered(Dictionary<string, ObjChange> map)
        {
            var list = new List<ObjChange>(map.Values);
            list.Sort((x, y) => x.order != y.order ? x.order.CompareTo(y.order)
                                                   : string.Compare(x.obj, y.obj, StringComparison.Ordinal));
            return list;
        }

        /// <summary>Build the ACTUAL object view from the expected objects: every object keeps its
        /// <c>from</c> (state_before) baseline, but its <c>to</c> (state_after) stays null until the step(s)
        /// that drive it COMPLETE successfully. finalState is the live ObjectStateRegistry value.</summary>
        static List<ObjChange> BuildActualView(int zoneIndex, Dictionary<string, ObjChange> expectedByObj,
            Dictionary<string, OperatingParagraphRuntime.ActualStep> completedById)
        {
            var list = new List<ObjChange>();
            foreach (ObjChange e in expectedByObj.Values)
            {
                var av = new ObjChange { obj = e.obj, parentName = e.parentName, parentId = e.parentId, from = e.from };
                av.stepIds.AddRange(e.stepIds);
                string lastAfter = null; int firstOrder = int.MaxValue; int msSum = -1;
                foreach (StepUpdate eu in e.updates)
                {
                    bool done = completedById.TryGetValue(eu.stepId, out OperatingParagraphRuntime.ActualStep rec);
                    int ms = -1, ord = int.MaxValue;
                    if (done)
                    {
                        ord = rec.order;
                        if (MLTrainingResultsWriter.TryGetRecordedStepCompletionTime(AgentId, eu.stepId, out float sec) && sec >= 0f)
                            ms = Mathf.RoundToInt(sec * 1000f);
                    }
                    av.updates.Add(new StepUpdate
                    {
                        stepId = eu.stepId, from = eu.from,
                        to = done ? eu.to : null,               // after-state only once the step completes
                        completed = done,
                        contactVerified = done && ObjectStateRegistry.WasStepVerified(zoneIndex, eu.stepId),
                        order = ord, ms = ms
                    });
                    if (done) { lastAfter = eu.to; if (ord < firstOrder) firstOrder = ord; if (ms >= 0) msSum = Math.Max(0, msSum) + ms; }
                }
                av.to = lastAfter;                              // null until at least one step completes
                av.order = firstOrder;
                av.durationMs = msSum;
                av.finalState = ObjectStateRegistry.CurrentState(zoneIndex, e.obj) ?? e.from;
                list.Add(av);
            }
            list.Sort((x, y) => x.order != y.order ? x.order.CompareTo(y.order)
                                                   : string.Compare(x.obj, y.obj, StringComparison.Ordinal));
            return list;
        }

        static bool TransitionEqual(ObjChange a, ObjChange b)
            => string.Equals(a.from, b.from, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.to, b.to, StringComparison.OrdinalIgnoreCase);

        // ── Statement text (actual) — mirrors the RAG wording so similarity stays high ──
        static string BuildSubTaskStatement(SubTask st)
        {
            // Only objects whose state actually reached its after-state (to != null) belong in the outcome sentence.
            var changes = st.actual.FindAll(c => c.to != null);
            if (changes.Count == 0) return "";
            string label = string.IsNullOrEmpty(st.stepLabel) ? st.subTaskId : st.stepLabel;
            return $"The {AgentId} physical agent ({AgentName}) completed '{label}', changing {changes.Count} object(s): {JoinChanges(changes)}.";
        }

        static string BuildOverviewStatement(Result res, Dictionary<string, ObjChange> globalActual)
        {
            var changes = Ordered(globalActual);
            if (changes.Count == 0) return "";
            return $"The {AgentId} physical agent ({AgentName}) completed {res.subTasksComplete} sub-task(s), changing {changes.Count} object(s): {JoinChanges(changes)}.";
        }

        static string JoinChanges(List<ObjChange> changes)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < changes.Count; i++)
            {
                if (i > 0) sb.Append("; ");
                sb.Append(changes[i].obj).Append(" from ").Append(changes[i].from).Append(" to ").Append(changes[i].to);
            }
            return sb.ToString();
        }

        // ── Entry point 1: console + run_logs JSON ─────────────────────────────
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
                    File.WriteAllText(Path.Combine(dir, "identity_statement_comparison.json"), BuildBlockJson(res, ""));
                    Debug.Log($"[IdentityStatement] Wrote comparison → {Path.Combine(dir, "identity_statement_comparison.json")}");
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[IdentityStatement] write failed: {ex.Message}"); }
        }

        // ── Entry point 2: pulled by MLTrainingResultsWriter (any game end) ────
        public static string BuildFinalResultsBlockJson(int zoneIndex, string baseIndent)
        {
            try { return BuildBlockJson(Compute(zoneIndex), baseIndent); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[IdentityStatement] final-results block failed: {ex.Message}");
                return baseIndent + "{ \"error\": \"identity statement unavailable\" }";
            }
        }

        static void LogResult(Result res)
        {
            string ov = res.overviewSimilarity >= 0f ? (res.overviewSimilarity * 100f).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "-";
            Debug.Log($"[IdentityStatement] RESULT ({res.agentId}/{res.agent}): {(res.pass ? "PASS ✅" : "MISMATCH ⚠")} | objects {res.objectsMatched}/{res.objectsExpected} | missing {res.objectsMissing} | extra {res.objectsExtra} | transitionMismatch {res.transitionMismatches} | overviewSim {ov} | subTasks {res.subTasksComplete}/{res.subTasksExpected} | allPhysicalComplete {res.allPhysicalComplete} → run_logs/identity_statement_comparison.json");
            Debug.Log($"[IdentityStatement] Actual Identity Statement:\n" + (string.IsNullOrEmpty(res.actualOverview) ? "(no physical objects changed)" : res.actualOverview));
        }

        // ── Serialization ──────────────────────────────────────────────────────
        // predicted[] and actual[] mirror the RAG identityStatements array EXACTLY (overview entry + one
        // entry per physical sub-task, each with a flat environment_state_changes[]). The actual[] carries
        // the same shape with the dynamic data (state_after gated on completion, finalState, contactVerified).
        static string BuildBlockJson(Result res, string ind)
        {
            string i1 = ind + "  ", i2 = ind + "    ";
            var sb = new StringBuilder();
            sb.Append(ind).Append("{\n");
            FStr(sb, i1, "agentId", res.agentId, true);
            FStr(sb, i1, "agent", res.agent, true);
            FRaw(sb, i1, "allPhysicalComplete", B(res.allPhysicalComplete), true);

            // comparison
            sb.Append(i1).Append("\"comparison\": {\n");
            FRaw(sb, i2, "pass", B(res.pass), true);
            FRaw(sb, i2, "objectsExpected", res.objectsExpected.ToString(CultureInfo.InvariantCulture), true);
            FRaw(sb, i2, "objectsMatched", res.objectsMatched.ToString(CultureInfo.InvariantCulture), true);
            FRaw(sb, i2, "objectsMissing", res.objectsMissing.ToString(CultureInfo.InvariantCulture), true);
            FRaw(sb, i2, "objectsExtra", res.objectsExtra.ToString(CultureInfo.InvariantCulture), true);
            FRaw(sb, i2, "transitionMismatches", res.transitionMismatches.ToString(CultureInfo.InvariantCulture), true);
            FRaw(sb, i2, "coverage", res.coverage.ToString("0.000", CultureInfo.InvariantCulture), true);
            FRaw(sb, i2, "overviewStatementSimilarity", res.overviewSimilarity >= 0f ? res.overviewSimilarity.ToString("0.000", CultureInfo.InvariantCulture) : "-1", true);
            FRaw(sb, i2, "meanSubTaskStatementSimilarity", res.meanSubTaskSimilarity >= 0f ? res.meanSubTaskSimilarity.ToString("0.000", CultureInfo.InvariantCulture) : "-1", true);
            FRaw(sb, i2, "subTasksExpected", res.subTasksExpected.ToString(CultureInfo.InvariantCulture), true);
            FRaw(sb, i2, "subTasksComplete", res.subTasksComplete.ToString(CultureInfo.InvariantCulture), false);
            sb.Append(i1).Append("},\n");

            // predicted[] — RAG identityStatements shape (read-only reference).
            sb.Append(i1).Append("\"predicted\": [\n");
            WriteEntryArray(sb, i2, res, actual: false);
            sb.Append(i1).Append("],\n");

            // actual[] — same shape, dynamic data (built live from completed physical steps).
            sb.Append(i1).Append("\"actual\": [\n");
            WriteEntryArray(sb, i2, res, actual: true);
            sb.Append(i1).Append("]\n");

            sb.Append(ind).Append("}");
            return sb.ToString();
        }

        static void WriteEntryArray(StringBuilder sb, string ind, Result res, bool actual)
        {
            // overview entry first, then one per physical sub-task — mirrors the RAG array order.
            WriteEntry(sb, ind, res, null, actual, comma: res.subTasks.Count > 0);
            for (int s = 0; s < res.subTasks.Count; s++)
                WriteEntry(sb, ind, res, res.subTasks[s], actual, comma: s < res.subTasks.Count - 1);
        }

        /// <summary>Write one RAG-shaped identity-statement entry (overview when st==null).</summary>
        static void WriteEntry(StringBuilder sb, string ind, Result res, SubTask st, bool actual, bool comma)
        {
            string i1 = ind + "  ", i2 = ind + "    ";
            bool overview = st == null;

            IsEntry meta = overview
                ? IdentityStatementCatalog.Overview
                : (IdentityStatementCatalog.BySubTaskEntryId.TryGetValue(st.subTaskId ?? "", out IsEntry e) ? e : null);

            List<ObjChange> changes = overview
                ? (actual ? res.overviewActual : res.overviewExpected)
                : (actual ? st.actual : st.expected);

            string statement = overview
                ? (actual ? res.actualOverview : res.predictedOverview)
                : (actual ? st.actualStatement : st.predictedStatement);

            sb.Append(ind).Append("{\n");
            FStr(sb, i1, "id", overview ? "overview" : st.subTaskId, true);
            FStr(sb, i1, "scope", overview ? "task" : "sub_task", true);
            FStrOrNull(sb, i1, "sub_task_id", meta?.sub_task_id, true);          // null on overview (as in RAG)
            FStr(sb, i1, "status", string.IsNullOrEmpty(meta?.status) ? "finalized" : meta.status, true);
            FStr(sb, i1, "agentId", AgentId, true);
            FStr(sb, i1, "agent", AgentName, true);
            if (overview) FStr(sb, i1, "occupation", string.IsNullOrEmpty(meta?.occupation) ? "Marketing Managers" : meta.occupation, true);
            if (!overview) FStr(sb, i1, "step_label", st.stepLabel, true);

            if (actual)
            {
                int matched = overview ? res.objectsMatched : st.completedObjects;
                int total = overview ? res.objectsExpected : st.totalObjects;
                bool done = overview ? res.allPhysicalComplete : st.completed;
                float sim = overview ? res.overviewSimilarity : st.statementSimilarity;
                FRaw(sb, i1, "completed", B(done), true);
                FRaw(sb, i1, "completedObjects", matched.ToString(CultureInfo.InvariantCulture), true);
                FRaw(sb, i1, "totalObjects", total.ToString(CultureInfo.InvariantCulture), true);
                FRaw(sb, i1, "statementSimilarity", sim >= 0f ? sim.ToString("0.000", CultureInfo.InvariantCulture) : "null", true);
            }

            FStr(sb, i1, "statement", statement, true);

            // environment_state_changes[] — flat, each carries parent/parentId inline (RAG shape).
            sb.Append(i1).Append("\"environment_state_changes\": [\n");
            for (int c = 0; c < changes.Count; c++)
                WriteChange(sb, changes[c], i2, c < changes.Count - 1, actual);
            sb.Append(i1).Append("],\n");

            FRaw(sb, i1, "physical_step_ids", EntryStepIds(changes), true);
            // duration_ms: predicted = authored (RAG); actual = measured sum over completed steps (null if none).
            if (actual)
            {
                int sum = -1;
                foreach (ObjChange c in changes) if (c.durationMs >= 0) sum = Math.Max(0, sum) + c.durationMs;
                FRaw(sb, i1, "duration_ms", sum >= 0 ? sum.ToString(CultureInfo.InvariantCulture) : "null", false);
            }
            else
            {
                FRaw(sb, i1, "duration_ms", meta != null ? meta.duration_ms.ToString(CultureInfo.InvariantCulture) : "0", false);
            }

            sb.Append(ind).Append("}").Append(comma ? "," : "").Append("\n");
        }

        /// <summary>Ordered, de-duplicated step ids across all changes of an entry.</summary>
        static string EntryStepIds(List<ObjChange> changes)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var ordered = new List<string>();
            foreach (ObjChange c in changes)
                foreach (string s in c.stepIds)
                    if (seen.Add(s)) ordered.Add(s);
            return StrArray(ordered);
        }

        static void WriteChange(StringBuilder sb, ObjChange c, string ind, bool comma, bool actual)
        {
            string i1 = ind + "  ", i2 = ind + "    ", i3 = ind + "      ";
            sb.Append(ind).Append("{\n");
            FStr(sb, i1, "object", c.obj, true);
            FStr(sb, i1, "parent", c.parentName, true);
            FStr(sb, i1, "parentId", c.parentId, true);
            FStr(sb, i1, "from", c.from, true);
            // stateAfter (to) is null until the step(s) complete successfully (actual); predicted shows the target.
            if (actual) FStrOrNull(sb, i1, "to", c.to, true);
            else FStr(sb, i1, "to", c.to, true);
            if (actual)
            {
                FStrOrNull(sb, i1, "finalState", c.finalState, true);   // live tracked state (from until done, then to)
                FRaw(sb, i1, "durationMs", c.durationMs >= 0 ? c.durationMs.ToString(CultureInfo.InvariantCulture) : "null", true);
            }
            FRaw(sb, i1, "physical_step_ids", StrArray(c.stepIds), true);

            // updates[]: one entry per physical step that touched this object — so the same object updated by
            // multiple steps (e.g. enter_key by s03 + s04) is shown individually; each step's `to` stays null
            // until THAT step completes with verified contact.
            var ups = new List<StepUpdate>(c.updates);
            if (actual) ups.Sort((x, y) => x.order != y.order ? x.order.CompareTo(y.order) : string.Compare(x.stepId, y.stepId, StringComparison.Ordinal));
            sb.Append(i1).Append("\"updates\": [\n");
            for (int u = 0; u < ups.Count; u++)
            {
                StepUpdate up = ups[u];
                sb.Append(i2).Append("{\n");
                FStr(sb, i3, "physical_step_id", up.stepId, true);
                FRaw(sb, i3, "seq", (u + 1).ToString(CultureInfo.InvariantCulture), true);
                FStr(sb, i3, "from", up.from, true);
                if (actual)
                {
                    FStrOrNull(sb, i3, "to", up.to, true);
                    FRaw(sb, i3, "completed", B(up.completed), true);
                    FRaw(sb, i3, "contactVerified", B(up.contactVerified), true);
                    FRaw(sb, i3, "durationMs", up.ms >= 0 ? up.ms.ToString(CultureInfo.InvariantCulture) : "null", false);
                }
                else FStr(sb, i3, "to", up.to, false);
                sb.Append(i2).Append("}").Append(u < ups.Count - 1 ? "," : "").Append("\n");
            }
            sb.Append(i1).Append("]\n");
            sb.Append(ind).Append("}").Append(comma ? "," : "").Append("\n");
        }

        /// <summary>Write "key": "val" — but "key": null when val is null (state not yet reached).</summary>
        static void FStrOrNull(StringBuilder sb, string ind, string key, string val, bool comma)
        {
            if (val == null) FRaw(sb, ind, key, "null", comma);
            else FStr(sb, ind, key, val, comma);
        }

        static string StrArray(List<string> items)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Count; i++) { if (i > 0) sb.Append(", "); sb.Append('"').Append(Esc(items[i])).Append('"'); }
            sb.Append(']');
            return sb.ToString();
        }

        static void FStr(StringBuilder sb, string ind, string key, string val, bool comma)
            => sb.Append(ind).Append('"').Append(key).Append("\": \"").Append(Esc(val)).Append('"').Append(comma ? "," : "").Append('\n');

        static void FRaw(StringBuilder sb, string ind, string key, string raw, bool comma)
            => sb.Append(ind).Append('"').Append(key).Append("\": ").Append(raw).Append(comma ? "," : "").Append('\n');

        static string B(bool b) => b ? "true" : "false";

        static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t");
        }
    }
}
