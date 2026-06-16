using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>Validates RAG body_part entities and manual buffer effectors against BodyPartRegistry.</summary>
public static class BodyPartRegistryValidator
{
    const string BasicUiPath = "Assets/JsonFile/basicUI_ml2.json";
    const string ManualBufferPath = "Assets/JsonFile/mannualBuffer.json";

    public struct ValidationReport
    {
        public int warningCount;
        public int errorCount;
        public List<string> messages;

        public bool IsClean => warningCount == 0 && errorCount == 0;
    }

    public static ValidationReport ValidateAll(string ragText = null)
    {
        ValidationReport report = new ValidationReport { messages = new List<string>() };

        if (!BodyPartRegistry.IsLoaded && !BodyPartRegistry.TryLoad())
        {
            AddError(ref report, "BodyPartRegistry failed to load.");
            return report;
        }

        ValidateBasicUi(ref report);
        ValidateManualBuffer(ref report);

        if (!string.IsNullOrWhiteSpace(ragText))
            ValidateRagText(ref report, ragText);

        if (report.IsClean)
            Debug.Log("[BodyPartRegistryValidator] All body part checks passed.");
        else
            Debug.LogWarning($"[BodyPartRegistryValidator] Finished with {report.errorCount} error(s) and {report.warningCount} warning(s).");

        return report;
    }

    public static void ValidateAtBootstrap(string ragText)
    {
        ValidationReport report = ValidateAll(ragText);
        for (int i = 0; i < report.messages.Count; i++)
        {
            string msg = report.messages[i];
            if (msg.StartsWith("[ERROR]", StringComparison.Ordinal))
                Debug.LogError(msg);
            else
                Debug.LogWarning(msg);
        }
    }

    static void ValidateBasicUi(ref ValidationReport report)
    {
        string path = ResolvePath(BasicUiPath, "basicUI_ml2.json");
        if (!File.Exists(path))
        {
            AddWarning(ref report, $"basicUI_ml2.json not found at {path}");
            return;
        }

        string raw = File.ReadAllText(path);
        if (!RagSceneJsonBridge.TryExtractUnitySceneDataJson(raw, out string dataJson, out _))
        {
            AddWarning(ref report, "Could not extract unity_scene.data from basicUI_ml2.json.");
            return;
        }

        ValidateCognitiveObjectsJson(ref report, dataJson, "basicUI_ml2.json");
    }

    static void ValidateRagText(ref ValidationReport report, string ragText)
    {
        if (!RagSceneJsonBridge.TryExtractUnitySceneDataJson(ragText, out string dataJson, out string err))
        {
            AddWarning(ref report, $"Could not extract unity_scene.data from RAG text: {err}");
            return;
        }

        ValidateCognitiveObjectsJson(ref report, dataJson, "RAG runtime");
    }

    static void ValidateCognitiveObjectsJson(ref ValidationReport report, string dataJson, string sourceLabel)
    {
        RagUnityDataInnerModel model;
        try
        {
            model = JsonUtility.FromJson<RagUnityDataInnerModel>(dataJson);
        }
        catch (Exception ex)
        {
            AddWarning(ref report, $"{sourceLabel}: failed to parse cognitiveObjects — {ex.Message}");
            return;
        }

        if (model?.cognitiveObjects == null)
            return;

        int bodyPartCount = 0;
        for (int i = 0; i < model.cognitiveObjects.Length; i++)
        {
            RagCognitiveObject obj = model.cognitiveObjects[i];
            if (obj == null || !string.Equals(obj.type, "body_part", StringComparison.OrdinalIgnoreCase))
                continue;

            bodyPartCount++;
            if (!BodyPartRegistry.TryGetBySceneId(obj.id, out BodyPartEntry entry))
            {
                AddWarning(ref report, $"{sourceLabel}: unknown body_part scene id '{obj.id}' (name='{obj.name}').");
                continue;
            }

            if (!string.Equals(entry.ragName, obj.name, StringComparison.OrdinalIgnoreCase))
            {
                AddWarning(ref report,
                    $"{sourceLabel}: body_part id/name mismatch for '{obj.id}': RAG name='{obj.name}', registry expects '{entry.ragName}'.");
            }
        }

        if (bodyPartCount == 0)
            AddWarning(ref report, $"{sourceLabel}: no body_part cognitiveObjects found.");
    }

    static void ValidateManualBuffer(ref ValidationReport report)
    {
        string path = ResolvePath(ManualBufferPath, "mannualBuffer.json");
        if (!File.Exists(path))
        {
            AddWarning(ref report, $"mannualBuffer.json not found at {path}");
            return;
        }

        string raw = File.ReadAllText(path);
        ValidateDistinctEffectors(ref report, raw, path);

        if (!ManualBufferCatalog.IsLoaded && !ManualBufferCatalog.TryLoad(path))
        {
            AddWarning(ref report, "ManualBufferCatalog failed to load for effector validation.");
            return;
        }

        foreach (KeyValuePair<string, KleinFrame> pair in ManualBufferCatalog.ById)
        {
            KleinFrame frame = pair.Value;
            if (frame == null || string.IsNullOrWhiteSpace(frame.effectorBodyPart))
                continue;

            if (!BodyPartRegistry.IsKnownEffector(frame.effectorBodyPart))
            {
                AddWarning(ref report,
                    $"mannualBuffer.json: Klein frame '{frame.kleinFrameId}' uses unknown effector '{frame.effectorBodyPart}'.");
                continue;
            }

            if (!BodyPartRegistry.TryResolve(frame.effectorBodyPart, out BodyPartEntry entry))
                continue;

            if (entry.HasMixamoBone && !string.IsNullOrWhiteSpace(frame.rigBone)
                && !string.Equals(entry.mixamoBone, frame.rigBone, StringComparison.OrdinalIgnoreCase))
            {
                AddWarning(ref report,
                    $"mannualBuffer.json: '{frame.kleinFrameId}' rig_bone '{frame.rigBone}' != registry '{entry.mixamoBone}'.");
            }
        }
    }

    static void ValidateDistinctEffectors(ref ValidationReport report, string raw, string path)
    {
        int keyIdx = raw.IndexOf("\"distinct_effectors\"", StringComparison.Ordinal);
        if (keyIdx < 0)
        {
            AddWarning(ref report, $"{path}: distinct_effectors block not found.");
            return;
        }

        int braceStart = raw.IndexOf('{', keyIdx);
        if (braceStart < 0)
            return;

        int depth = 1;
        int i = braceStart + 1;
        while (i < raw.Length && depth > 0)
        {
            char c = raw[i];
            if (c == '{') depth++;
            else if (c == '}') depth--;
            i++;
        }

        if (depth != 0)
            return;

        string inner = raw.Substring(braceStart + 1, i - braceStart - 2);
        MatchCollection matches = Regex.Matches(inner, "\"([^\"]+)\"\\s*:");
        for (int m = 0; m < matches.Count; m++)
        {
            string effector = matches[m].Groups[1].Value;
            if (!BodyPartRegistry.IsKnownEffector(effector))
            {
                AddWarning(ref report,
                    $"mannualBuffer.json distinct_effectors: unknown effector key '{effector}'.");
            }
        }
    }

    static string ResolvePath(string relativePath, string fileName)
    {
        if (File.Exists(relativePath))
            return relativePath;

        string dataPath = Path.Combine(Application.dataPath, "JsonFile", fileName);
        return File.Exists(dataPath) ? dataPath : relativePath;
    }

    static void AddWarning(ref ValidationReport report, string message)
    {
        report.warningCount++;
        report.messages.Add("[WARN] " + message);
    }

    static void AddError(ref ValidationReport report, string message)
    {
        report.errorCount++;
        report.messages.Add("[ERROR] " + message);
    }
}
