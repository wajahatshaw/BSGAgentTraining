#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RagInferenceSceneEditor
{
    const string TrainingScene = "Assets/Scenes/JSONWorkflowSceneML.unity";
    const string InferenceScene = "Assets/Scenes/JSONWorkflowSceneInference.unity";
    const string OnnxSourceDir = "results/persona_training";
    const string OnnxDestDir = "Assets/ML-Agents/Models/Inference";

    [MenuItem("BSG/Inference/1. Duplicate Training Scene → JSONWorkflowSceneInference")]
    public static void DuplicateInferenceScene()
    {
        if (!File.Exists(TrainingScene))
        {
            Debug.LogError($"Missing training scene: {TrainingScene}");
            return;
        }

        if (AssetDatabase.CopyAsset(TrainingScene, InferenceScene))
            Debug.Log($"Created {InferenceScene}. Open it and run '2. Wire Inference Scene'.");
        else if (File.Exists(InferenceScene))
            Debug.LogWarning($"{InferenceScene} already exists — open it and run step 2.");
        else
            Debug.LogError("Failed to duplicate scene.");
    }

    [MenuItem("BSG/Inference/2. Wire Inference Scene (add controller + deployer)")]
    public static void WireInferenceScene()
    {
        var scene = EditorSceneManager.OpenScene(InferenceScene, OpenSceneMode.Single);
        var manager = GameObject.Find("REPLICA_SceneManager");
        if (manager == null)
        {
            Debug.LogError("REPLICA_SceneManager not found. Is this a replica/RAG scene?");
            return;
        }

        var ctrl = manager.GetComponent<RagInferenceSceneController>();
        if (ctrl == null)
            ctrl = manager.AddComponent<RagInferenceSceneController>();

        var deployer = manager.GetComponent<RagMlBrainDeployer>();
        if (deployer == null)
            deployer = manager.AddComponent<RagMlBrainDeployer>();

        deployer.applyOnStart = false;
        deployer.deployPhysicalBrains = true;
        deployer.deployCognitiveBrains = true;
        ctrl.brainDeployer = deployer;

        if (Object.FindObjectOfType<RagCognitiveInferenceCoordinator>() == null)
        {
            var coordGo = new GameObject("RagCognitiveInferenceCoordinator");
            coordGo.AddComponent<RagCognitiveInferenceCoordinator>();
        }

        var setup = manager.GetComponent<ReplicaSceneSetup>();
        if (setup != null)
        {
            setup.enableMlTrainingInRagMode = false;
            setup.enableMlTrainingForCognitiveAgents = false;
            setup.trainZonesMask = 0;
        }

        ctrl.patchReplicaSceneSetup = true;
        EditorUtility.SetDirty(manager);
        deployer.autoLoadModelsFromInferenceFolder = true;
        deployer.EnsureModelsReadyForInference();
        deployer.ApplyBrains();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Inference scene wired on REPLICA_SceneManager. Sentis ModelAssets loaded via RagMlBrainDeployer (run Fix ONNX Import if any slot is missing).");
    }

    [MenuItem("BSG/Inference/3. Copy ONNX from results/persona_training → Assets/ML-Agents/Models/Inference")]
    public static void CopyOnnxAssets()
    {
        string root = Path.GetDirectoryName(Application.dataPath);
        string src = Path.Combine(root, OnnxSourceDir);
        string dst = Path.Combine(Application.dataPath, "ML-Agents/Models/Inference");

        if (!Directory.Exists(src))
        {
            Debug.LogError($"Missing {src}. Train first or change OnnxSourceDir.");
            return;
        }

        Directory.CreateDirectory(dst);
        int copied = 0;
        for (int z = 0; z < 4; z++)
        {
            foreach (string prefix in new[] { "PhysicalAgentZone", "CognitiveAgentZone" })
            {
                string name = $"{prefix}{z}.onnx";
                string found = FindOnnx(src, name);
                if (string.IsNullOrEmpty(found))
                {
                    Debug.LogWarning($"Missing {name} under {src}");
                    continue;
                }
                string destPath = Path.Combine(dst, name);
                File.Copy(found, destPath, true);
                string meta = destPath + ".meta";
                if (File.Exists(meta))
                    File.Delete(meta);
                copied++;
            }
        }

        AssetDatabase.Refresh();
        OnnxInferenceImportFix.ReimportAndRelink();
        Debug.Log($"Copied {copied} ONNX file(s) to {OnnxDestDir} and ran Sentis reimport.");
    }

    [MenuItem("BSG/hide/Apply Inference Hide (mental + cognitive stations)")]
    public static void HideInferenceVisualsMenu()
    {
        RagInferenceVisuals.ApplyFullInferenceHide();
        Debug.Log("BSG/hide: mental agents and cognitive station visuals hidden. GameObjects remain active.");
    }

    [MenuItem("BSG/Inference/4. Hide Cognitive Boxes (current scene)")]
    public static void HideBoxesMenu() => HideInferenceVisualsMenu();

    [MenuItem("BSG/Inference/5. Verify Sentis ModelAssets on deployer")]
    public static void VerifyDeployerModels()
    {
        var deployer = Object.FindObjectOfType<RagMlBrainDeployer>();
        if (deployer == null)
        {
            Debug.LogError("RagMlBrainDeployer not found. Run BSG → Inference → 2. Wire Inference Scene.");
            return;
        }

        deployer.EnsureModelsReadyForInference();
        int missing = 0;
        if (deployer.zone0PhysicalModel == null) { missing++; Debug.LogError("Missing zone0PhysicalModel"); }
        if (deployer.zone1PhysicalModel == null) { missing++; Debug.LogError("Missing zone1PhysicalModel"); }
        if (deployer.zone2PhysicalModel == null) { missing++; Debug.LogError("Missing zone2PhysicalModel"); }
        if (deployer.zone3PhysicalModel == null) { missing++; Debug.LogError("Missing zone3PhysicalModel"); }
        if (deployer.zone0CognitiveModel == null) { missing++; Debug.LogError("Missing zone0CognitiveModel"); }
        if (deployer.zone1CognitiveModel == null) { missing++; Debug.LogError("Missing zone1CognitiveModel"); }
        if (deployer.zone2CognitiveModel == null) { missing++; Debug.LogError("Missing zone2CognitiveModel"); }
        if (deployer.zone3CognitiveModel == null) { missing++; Debug.LogError("Missing zone3CognitiveModel"); }

        if (missing == 0)
            Debug.Log("All 8 Sentis ModelAssets assigned on RagMlBrainDeployer.");
        else
            Debug.LogError($"{missing}/8 ModelAssets missing. Run Fix ONNX Import or Copy ONNX.");
    }

    static string FindOnnx(string dir, string fileName)
    {
        string root = Path.Combine(dir, fileName);
        if (File.Exists(root))
            return root;

        string behavior = Path.GetFileNameWithoutExtension(fileName);
        string subDir = Path.Combine(dir, behavior);
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
