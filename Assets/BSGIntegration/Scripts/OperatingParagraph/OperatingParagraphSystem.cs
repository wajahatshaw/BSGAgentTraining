using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// Operating Paragraph system (PHYSICAL steps only).
//
// Validation flow (see basicUI_ml2.json → operatingParagraph):
//     Expected Operating Paragraph (RAG)  ── parsed by OperatingParagraphCatalog
//                    │
//     Unity simulation → physical steps complete ── captured by OperatingParagraphRuntime
//                    │
//     Actual Operating Paragraph (built live, per physical step)
//                    │
//     Compare Expected vs Actual → result logged + written to run_logs
//
// The EXPECTED paragraph carries every fixed descriptor (object, effector, rig_bone,
// state_before/after, authored coordinates) keyed by physical_step_id. The runtime
// overlays only the DYNAMIC parts — completion order, live world position, and the
// measured completionTime — so the actual paragraph reflects what the agent really did.
// Cognitive / mental steps are intentionally excluded (physical only).
// ─────────────────────────────────────────────────────────────────────────────

namespace BSG.OperatingParagraph
{
    // ── Parsed EXPECTED model (JsonUtility DTOs) ──────────────────────────────
    [Serializable]
    public class OpCoord { public float x; public float y; public float z; }

    [Serializable]
    public class OpAction
    {
        public string klein_frame_id;
        public string physical_step_id;
        public string action;            // "resting" | "pressing" | "depressing"
        public string @object;           // JSON key "object" (target object name)
        public string main_target_object_id;
        public string state_before;
        public string state_after;
        public string effector;
        public string rig_bone;
        public float force_newtons;      // null in JSON → sanitized to 0
        public int duration_ms;          // authored EXPECTED duration (reference baseline for timing compare)
        public OpCoord coordinates;
        public string frame_mode;        // always "physical" in this block
        public string step_narrative;    // self-contained EXPECTED per-step narrative
    }

    [Serializable]
    public class OpChunk
    {
        public string chunk_id;
        public int step_n;
        public string sub_task_id;
        public string step_label;
        public string[] physical_step_ids;
        public string[] klein_frame_ids;
        public OpAction[] actions;
        public string narrative;
    }

    [Serializable]
    class OpChunkArray { public OpChunk[] items; }

    /// <summary>Parses and caches the RAG-defined (expected) physical operating paragraph.</summary>
    public static class OperatingParagraphCatalog
    {
        static bool _loaded;
        public static bool IsLoaded => _loaded;

        public static string ExpectedComplete { get; private set; } = string.Empty;
        public static List<OpChunk> ExpectedChunks { get; private set; } = new List<OpChunk>();
        /// <summary>Every expected physical action flattened + keyed by physical_step_id.</summary>
        public static Dictionary<string, OpAction> ExpectedByStepId { get; private set; }
            = new Dictionary<string, OpAction>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Bootstrap from the live SceneUILoader RAG text (same source the Klein motor uses).</summary>
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

            string opText = ExtractObject(ragText, "operatingParagraph");
            if (string.IsNullOrEmpty(opText))
            {
                Debug.LogWarning("[OperatingParagraph] No operatingParagraph block found in RAG.");
                return false;
            }

            ExpectedComplete = ExtractStringValue(opText, "complete") ?? string.Empty;

            var chunks = new List<OpChunk>();
            var byId = new Dictionary<string, OpAction>(StringComparer.OrdinalIgnoreCase);

            string chunksArray = ExtractArray(opText, "chunks");
            if (!string.IsNullOrEmpty(chunksArray))
            {
                string sanitized = SanitizeNullNumbers(chunksArray, "force_newtons", "x", "y", "z");
                try
                {
                    var parsed = JsonUtility.FromJson<OpChunkArray>("{\"items\":" + sanitized + "}");
                    if (parsed?.items != null)
                    {
                        foreach (OpChunk c in parsed.items)
                        {
                            if (c == null) continue;
                            chunks.Add(c);
                            if (c.actions != null)
                                foreach (OpAction a in c.actions)
                                    if (a != null && !string.IsNullOrWhiteSpace(a.physical_step_id))
                                        byId[a.physical_step_id] = a;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[OperatingParagraph] chunks parse failed: {ex.Message}");
                }
            }

            ExpectedChunks = chunks;
            ExpectedByStepId = byId;
            _loaded = chunks.Count > 0 || !string.IsNullOrEmpty(ExpectedComplete);
            Debug.Log($"[OperatingParagraph] Loaded expected paragraph: {chunks.Count} chunk(s), {byId.Count} physical action(s).");
            return _loaded;
        }

        public static void Reset()
        {
            _loaded = false;
            ExpectedComplete = string.Empty;
            ExpectedChunks = new List<OpChunk>();
            ExpectedByStepId = new Dictionary<string, OpAction>(StringComparer.OrdinalIgnoreCase);
        }

        // ── tiny JSON helpers (string-aware, local copies) ────────────────────
        static string ExtractObject(string text, string key)
        {
            int k = text.IndexOf($"\"{key}\"", StringComparison.Ordinal);
            if (k < 0) return string.Empty;
            int start = text.IndexOf('{', k);
            if (start < 0) return string.Empty;
            int end = FindMatchingBracket(text, start, '{', '}');
            return end < 0 ? string.Empty : text.Substring(start, end - start + 1);
        }

        static string ExtractArray(string text, string key)
        {
            int k = text.IndexOf($"\"{key}\"", StringComparison.Ordinal);
            if (k < 0) return string.Empty;
            int start = text.IndexOf('[', k);
            if (start < 0) return string.Empty;
            int end = FindMatchingBracket(text, start, '[', ']');
            return end < 0 ? string.Empty : text.Substring(start, end - start + 1);
        }

        static string ExtractStringValue(string text, string key)
        {
            int k = text.IndexOf($"\"{key}\"", StringComparison.Ordinal);
            if (k < 0) return null;
            int colon = text.IndexOf(':', k);
            if (colon < 0) return null;
            int q = text.IndexOf('"', colon + 1);
            if (q < 0) return null;
            var sb = new StringBuilder();
            for (int i = q + 1; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\\' && i + 1 < text.Length)
                {
                    char n = text[++i];
                    switch (n)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        default: sb.Append(n); break;
                    }
                    continue;
                }
                if (c == '"') break;
                sb.Append(c);
            }
            return sb.ToString();
        }

        static int FindMatchingBracket(string text, int openIdx, char open, char close)
        {
            int depth = 0;
            bool inStr = false;
            for (int i = openIdx; i < text.Length; i++)
            {
                char c = text[i];
                if (inStr)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') inStr = false;
                    continue;
                }
                if (c == '"') { inStr = true; continue; }
                if (c == open) depth++;
                else if (c == close) { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        /// <summary>Replace <c>"key": null</c> with <c>"key": 0</c> so JsonUtility floats parse.</summary>
        static string SanitizeNullNumbers(string json, params string[] keys)
        {
            foreach (string key in keys)
                json = json.Replace($"\"{key}\": null", $"\"{key}\": 0")
                           .Replace($"\"{key}\":null", $"\"{key}\":0");
            return json;
        }
    }

    // ── Shared narrative text builder + comparison utilities ──────────────────
    public static class OperatingParagraphText
    {
        static readonly string[] Connectors = { "", "Then,", "Next,", "After that," };

        public static string Connector(int index) => Connectors[index % Connectors.Length];

        static string Phrase(string s) => string.IsNullOrEmpty(s) ? s : s.Replace('_', ' ');

        static string EffectorPhrase(string effector)
        {
            switch (effector)
            {
                case "torso": return "torso";
                case "right_index_fingertip_pad": return "right index fingertip";
                default: return Phrase(effector);
            }
        }

        static string VerbPast(string action)
        {
            switch ((action ?? "").ToLowerInvariant())
            {
                case "resting": return "rested";
                case "pressing": return "pressed";
                case "depressing": return "depressed";
                default: return action ?? "acted on";
            }
        }

        static string F(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>One physical-step sentence. Positions + duration are supplied live for the actual
        /// paragraph (duration = measured completionTime); the predicted paragraph uses expected duration.
        /// Mirrors the wording gen_op.py bakes into the RAG so similarity stays high.</summary>
        public static string BuildSentence(OpAction a, float x, float y, float z, int index, float durationSeconds)
        {
            string obj = Phrase(a.@object);
            string eff = EffectorPhrase(a.effector);
            string rig = a.rig_bone ?? "";
            string before = a.state_before;
            string after = a.state_after;
            string pos = $"at x={F(x)} y={F(y)} z={F(z)}";
            string core;
            if (string.Equals(a.action, "resting", StringComparison.OrdinalIgnoreCase))
            {
                core = $"rested its {eff} (rig bone {rig}) on the {obj} {pos}, and the {obj} changed from {before} to {after}";
            }
            else
            {
                string force = a.force_newtons > 0f ? $" with about {a.force_newtons} N of force" : "";
                core = $"{VerbPast(a.action)} the {obj} with its {eff} (rig bone {rig}) {pos}{force}, and the {obj} changed from {before} to {after}";
            }
            string dur = F(durationSeconds < 0f ? 0f : durationSeconds);
            if (index == 0)
                return $"Over {dur} seconds, the P1 physical agent (Marketing Manager) {core}.";
            return $"{Connector(index)} over {dur} seconds, it {core}.";
        }

        // ── comparison (semantic, position/time-insensitive) ──────────────────
        static readonly char[] Split = { ' ', '\t', '\n', '\r', '.', ',', '(', ')', '=' };

        /// <summary>Normalize for text similarity: lowercase, strip coordinate/force/time
        /// numbers (dynamic), collapse whitespace.</summary>
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            s = s.ToLowerInvariant();
            var sb = new StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                // drop "x=..", "y=..", "z=.." and standalone numbers
                if ((s[i] == 'x' || s[i] == 'y' || s[i] == 'z') && i + 1 < s.Length && s[i + 1] == '=')
                {
                    i += 2;
                    while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == '-')) i++;
                    continue;
                }
                if (char.IsDigit(s[i]) || (s[i] == '-' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
                {
                    while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == '-')) i++;
                    continue;
                }
                sb.Append(s[i]);
                i++;
            }
            var tokens = sb.ToString().Split(Split, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", tokens);
        }

        /// <summary>Dice token-set similarity in [0,1] over normalized text.</summary>
        public static float Similarity(string a, string b)
        {
            var ta = new HashSet<string>(Normalize(a).Split(' '), StringComparer.Ordinal);
            var tb = new HashSet<string>(Normalize(b).Split(' '), StringComparer.Ordinal);
            ta.Remove(string.Empty); tb.Remove(string.Empty);
            if (ta.Count == 0 && tb.Count == 0) return 1f;
            if (ta.Count == 0 || tb.Count == 0) return 0f;
            int inter = 0;
            foreach (string t in ta) if (tb.Contains(t)) inter++;
            return (2f * inter) / (ta.Count + tb.Count);
        }
    }
}
