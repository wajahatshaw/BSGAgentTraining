#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class BodyPartRegistryEditor
{
    [MenuItem("BSG/Validate Body Part Registry")]
    public static void ValidateBodyPartRegistry()
    {
        BodyPartRegistry.TryLoad();
        ManualBufferCatalog.TryLoad();

        string ragPath = "Assets/JsonFile/RagRespnse2.0.json";
        string ragText = System.IO.File.Exists(ragPath) ? System.IO.File.ReadAllText(ragPath) : null;

        BodyPartRegistryValidator.ValidationReport report = BodyPartRegistryValidator.ValidateAll(ragText);

        for (int i = 0; i < report.messages.Count; i++)
        {
            string msg = report.messages[i];
            if (msg.StartsWith("[ERROR]", System.StringComparison.Ordinal))
                Debug.LogError(msg);
            else
                Debug.LogWarning(msg);
        }

        if (report.IsClean)
            Debug.Log("[BSG] Body part registry validation passed (0 warnings, 0 errors).");
        else
            Debug.LogWarning($"[BSG] Body part registry validation finished: {report.errorCount} error(s), {report.warningCount} warning(s).");
    }
}
#endif
