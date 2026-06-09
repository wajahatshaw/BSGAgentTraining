#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using Unity.InferenceEngine;
using UnityEngine;

/// <summary>
/// Fixes Sentis ONNX import for inference folder and re-links RagMlBrainDeployer after successful import.
/// </summary>
public static class OnnxInferenceImportFix
{
    const string InferenceFolder = "Assets/ML-Agents/Models/Inference";

    [MenuItem("BSG/Inference/Fix ONNX Import (reimport + relink deployer)")]
    public static void ReimportAndRelink()
    {
        if (!Directory.Exists(InferenceFolder))
        {
            Debug.LogError($"Missing folder: {InferenceFolder}. Run BSG → Inference → 3. Copy ONNX first.");
            return;
        }

        int reimported = 0;
        foreach (string path in Directory.GetFiles(InferenceFolder, "*.onnx"))
        {
            string assetPath = path.Replace('\\', '/');
            if (!assetPath.StartsWith("Assets/"))
            {
                int idx = assetPath.IndexOf("Assets/");
                if (idx >= 0) assetPath = assetPath.Substring(idx);
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            reimported++;

            ModelAsset model = RagMlBrainDeployer.LoadModelAssetAtPath(assetPath);
            if (model == null)
                Debug.LogError($"ONNX import failed for {assetPath} — check Console for Sentis errors.");
            else
                Debug.Log($"OK imported ModelAsset: {assetPath} ({model.name})");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var deployer = Object.FindObjectOfType<RagMlBrainDeployer>();
        if (deployer != null)
        {
            deployer.TryAutoLoadModelsFromInferenceFolder();
            EditorUtility.SetDirty(deployer);
            Debug.Log("RagMlBrainDeployer models refreshed from Inference folder.");
        }

        Debug.Log($"Reimported {reimported} ONNX file(s). If errors remain, delete {InferenceFolder}/*.meta and run this again.");
    }

    [MenuItem("BSG/Inference/Copy ONNX from results (latest checkpoint)")]
    public static void CopyFromResults()
    {
        string root = Path.GetDirectoryName(Application.dataPath);
        string src = Path.Combine(root, "results/persona_training");
        if (!Directory.Exists(src))
        {
            Debug.LogError($"Missing {src}");
            return;
        }

        Directory.CreateDirectory(InferenceFolder);
        for (int z = 0; z < 4; z++)
        {
            foreach (string prefix in new[] { "PhysicalAgentZone", "CognitiveAgentZone" })
            {
                string name = $"{prefix}{z}.onnx";
                string found = FindBestOnnx(src, name);
                if (string.IsNullOrEmpty(found))
                {
                    Debug.LogWarning($"Not found: {name}");
                    continue;
                }

                string dest = Path.Combine(InferenceFolder, name);
                File.Copy(found, dest, true);
                string meta = dest + ".meta";
                if (File.Exists(meta))
                    File.Delete(meta);
                Debug.Log($"Copied {found} → {dest}");
            }
        }

        AssetDatabase.Refresh();
        ReimportAndRelink();
    }

    static string FindBestOnnx(string resultsDir, string fileName)
    {
        string root = Path.Combine(resultsDir, fileName);
        if (File.Exists(root))
            return root;

        string behavior = Path.GetFileNameWithoutExtension(fileName);
        string subDir = Path.Combine(resultsDir, behavior);
        if (!Directory.Exists(subDir))
            return null;

        string best = null;
        long bestStep = -1;
        foreach (string f in Directory.GetFiles(subDir, "*.onnx"))
        {
            string baseName = Path.GetFileNameWithoutExtension(f);
            int dash = baseName.LastIndexOf('-');
            if (dash < 0) continue;
            if (!long.TryParse(baseName.Substring(dash + 1), out long step))
                continue;
            if (step >= bestStep)
            {
                bestStep = step;
                best = f;
            }
        }
        return best;
    }
}
#endif
