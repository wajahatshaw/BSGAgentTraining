using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// Identity Statement (IS) system — the OUTCOME record for the PHYSICAL agent (P1).
//
// Where the Operating Paragraph records the JOURNEY (per physical step: verb, effector,
// live position, measured duration), the Identity Statement records the RESULT: for the
// P1 physical agent (Marketing Manager), the before→after state of every interactable
// object that was touched, categorised by sub-task and organised so meronym parts
// (left_mouse_button, enter_key, …) sit UNDER their parent scene entity (computer mouse,
// computer keyboard, …). The agent's own body parts (scene_009 Marketing Manager) are excluded.
//
//   PREDICTED (reference)  ── RAG identityStatements[] (task overview + per sub-task),
//                             parsed here, re-organised by parent + attributed to P1.
//   EXPECTED (comparison)  ── the physical-actionable subset (objects the physical steps
//                             touch), derived from the operatingParagraph chunks so the
//                             comparison is scoped exactly like the OP — cognitive-only
//                             objects are not counted as "missing".
//   ACTUAL                 ── built at runtime from the physical steps that actually
//                             COMPLETED (state transitions + causing physical_step_id
//                             provenance + measured duration).
//
// This mirrors OperatingParagraphRuntime: partial-friendly Compute(), one block written on
// ANY game end (ideal or mid-run) into final_game_results.json + run_logs.
// ─────────────────────────────────────────────────────────────────────────────

namespace BSG.IdentityStatement
{
    // ── Parsed PREDICTED model (RAG identityStatements) ───────────────────────
    [Serializable]
    public class IsChange
    {
        public string @object;   // JSON key "object"
        public string from;
        public string to;
    }

    [Serializable]
    public class IsEntry
    {
        public string id;               // "overview" | "st_1" | …
        public string scope;            // "task" | "sub_task"
        public string sub_task_id;      // "1".."6" | null (overview)
        public string status;
        public string statement;
        public IsChange[] environment_state_changes;
        public int duration_ms;
        public string occupation;       // overview only
        public string agent;            // overview only
    }

    [Serializable]
    class IsEntryArray { public IsEntry[] items; }

    /// <summary>Parses + caches the RAG identityStatements and the sceneEntities meronym→parent map.</summary>
    public static class IdentityStatementCatalog
    {
        static bool _loaded;
        public static bool IsLoaded => _loaded;

        public static IsEntry Overview { get; private set; }
        /// <summary>Sub-task IS entries keyed by their id ("st_1", …) — matches operatingParagraph chunk sub_task_id.</summary>
        public static Dictionary<string, IsEntry> BySubTaskEntryId { get; private set; }
            = new Dictionary<string, IsEntry>(StringComparer.OrdinalIgnoreCase);

        public class ParentRef { public string parentName; public string parentId; }

        /// <summary>meronym object name (lower) → its parent scene entity (name + id). Agent parts excluded.</summary>
        public static Dictionary<string, ParentRef> MeronymParent { get; private set; }
            = new Dictionary<string, ParentRef>(StringComparer.OrdinalIgnoreCase);

        // The scene entity that represents the agent itself — its parts are NOT environment objects.
        const string AgentEntityName = "Marketing Manager";

        public static bool EnsureLoaded()
        {
            if (_loaded) return true;

            SceneUILoader loader = UnityEngine.Object.FindObjectOfType<SceneUILoader>();
            if (loader == null) return false;

            string rag = !string.IsNullOrEmpty(loader.MergedRawRagJson) ? loader.MergedRawRagJson
                       : !string.IsNullOrEmpty(loader.RawJsonText) ? loader.RawJsonText
                       : loader.EffectivePipelineJson;
            if (string.IsNullOrEmpty(rag)) return false;

            return LoadFromRag(rag);
        }

        public static bool LoadFromRag(string ragText)
        {
            if (string.IsNullOrEmpty(ragText)) return false;

            ParseIdentityStatements(ragText);
            ParseMeronymParents(ragText);

            _loaded = Overview != null || BySubTaskEntryId.Count > 0;
            Debug.Log($"[IdentityStatement] Loaded predicted IS: overview={(Overview != null)}, {BySubTaskEntryId.Count} sub-task(s), {MeronymParent.Count} meronym→parent mapping(s).");
            return _loaded;
        }

        public static void Reset()
        {
            _loaded = false;
            Overview = null;
            BySubTaskEntryId = new Dictionary<string, IsEntry>(StringComparer.OrdinalIgnoreCase);
            MeronymParent = new Dictionary<string, ParentRef>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Resolve the parent scene entity of an object; falls back to the object itself when unmapped.</summary>
        public static ParentRef ParentOf(string objectName)
        {
            if (!string.IsNullOrEmpty(objectName) && MeronymParent.TryGetValue(objectName, out ParentRef p))
                return p;
            return new ParentRef { parentName = objectName ?? "", parentId = "" };
        }

        // ── identityStatements parse ──────────────────────────────────────────
        static void ParseIdentityStatements(string ragText)
        {
            Overview = null;
            BySubTaskEntryId = new Dictionary<string, IsEntry>(StringComparer.OrdinalIgnoreCase);

            string arr = ExtractArray(ragText, "identityStatements");
            if (string.IsNullOrEmpty(arr)) { Debug.LogWarning("[IdentityStatement] No identityStatements block in RAG."); return; }

            string sanitized = SanitizeNullNumbers(arr, "duration_ms");
            try
            {
                var parsed = JsonUtility.FromJson<IsEntryArray>("{\"items\":" + sanitized + "}");
                if (parsed?.items != null)
                {
                    foreach (IsEntry e in parsed.items)
                    {
                        if (e == null) continue;
                        if (string.Equals(e.scope, "task", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(e.id, "overview", StringComparison.OrdinalIgnoreCase))
                            Overview = e;
                        else if (!string.IsNullOrEmpty(e.id))
                            BySubTaskEntryId[e.id] = e;
                    }
                }
            }
            catch (Exception ex) { Debug.LogError($"[IdentityStatement] identityStatements parse failed: {ex.Message}"); }
        }

        // ── sceneEntities → meronym→parent map ────────────────────────────────
        static void ParseMeronymParents(string ragText)
        {
            MeronymParent = new Dictionary<string, ParentRef>(StringComparer.OrdinalIgnoreCase);

            string arr = ExtractArray(ragText, "sceneEntities");
            if (string.IsNullOrEmpty(arr)) return;

            foreach (string entity in SplitTopLevelObjects(arr))
            {
                string parentName = ExtractStringValue(entity, "name");
                string parentId = ExtractStringValue(entity, "id");
                if (string.Equals(parentName, AgentEntityName, StringComparison.OrdinalIgnoreCase))
                    continue; // agent body parts are not environment objects

                string meronyms = ExtractArray(entity, "meronyms");
                if (string.IsNullOrEmpty(meronyms)) continue;
                foreach (string part in SplitTopLevelObjects(meronyms))
                {
                    string partName = ExtractStringValue(part, "name");
                    if (string.IsNullOrEmpty(partName)) continue;
                    MeronymParent[partName] = new ParentRef { parentName = parentName ?? "", parentId = parentId ?? "" };
                }
            }
        }

        // ── tiny JSON helpers (string-aware, self-contained) ──────────────────
        static string ExtractArray(string text, string key)
        {
            int k = text.IndexOf($"\"{key}\"", StringComparison.Ordinal);
            if (k < 0) return string.Empty;
            int start = text.IndexOf('[', k);
            if (start < 0) return string.Empty;
            int end = FindMatchingBracket(text, start, '[', ']');
            return end < 0 ? string.Empty : text.Substring(start, end - start + 1);
        }

        /// <summary>Split an array's text into its top-level "{...}" object substrings.</summary>
        static IEnumerable<string> SplitTopLevelObjects(string arrayText)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(arrayText)) return list;
            int i = 0;
            while (i < arrayText.Length)
            {
                if (arrayText[i] == '{')
                {
                    int end = FindMatchingBracket(arrayText, i, '{', '}');
                    if (end < 0) break;
                    list.Add(arrayText.Substring(i, end - i + 1));
                    i = end + 1;
                }
                else i++;
            }
            return list;
        }

        /// <summary>First-level string value for a key (does not recurse into nested objects/arrays).</summary>
        static string ExtractStringValue(string text, string key)
        {
            int depth = 0; bool inStr = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inStr) { if (c == '\\') { i++; continue; } if (c == '"') inStr = false; continue; }
                if (c == '"')
                {
                    // read the key token
                    int q = i + 1; var kb = new StringBuilder();
                    while (q < text.Length && text[q] != '"') { if (text[q] == '\\') q++; else kb.Append(text[q]); q++; }
                    string token = kb.ToString();
                    i = q; // now at closing quote of the token
                    if (depth == 1 && token == key)
                    {
                        int colon = text.IndexOf(':', i + 1);
                        if (colon < 0) return null;
                        int vq = text.IndexOf('"', colon + 1);
                        // ensure the value is a string (no non-space before the quote other than ':')
                        if (vq < 0) return null;
                        bool valueIsString = true;
                        for (int j = colon + 1; j < vq; j++) if (!char.IsWhiteSpace(text[j])) { valueIsString = false; break; }
                        if (!valueIsString) continue;
                        return ReadString(text, vq);
                    }
                    continue;
                }
                if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') depth--;
            }
            return null;
        }

        static string ReadString(string text, int openQuoteIdx)
        {
            var sb = new StringBuilder();
            for (int i = openQuoteIdx + 1; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\\' && i + 1 < text.Length)
                {
                    char n = text[++i];
                    switch (n) { case 'n': sb.Append('\n'); break; case 't': sb.Append('\t'); break;
                        case '"': sb.Append('"'); break; case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break; default: sb.Append(n); break; }
                    continue;
                }
                if (c == '"') break;
                sb.Append(c);
            }
            return sb.ToString();
        }

        static int FindMatchingBracket(string text, int openIdx, char open, char close)
        {
            int depth = 0; bool inStr = false;
            for (int i = openIdx; i < text.Length; i++)
            {
                char c = text[i];
                if (inStr) { if (c == '\\') { i++; continue; } if (c == '"') inStr = false; continue; }
                if (c == '"') { inStr = true; continue; }
                if (c == open) depth++;
                else if (c == close) { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        static string SanitizeNullNumbers(string json, params string[] keys)
        {
            foreach (string key in keys)
                json = json.Replace($"\"{key}\": null", $"\"{key}\": 0").Replace($"\"{key}\":null", $"\"{key}\":0");
            return json;
        }
    }
}
