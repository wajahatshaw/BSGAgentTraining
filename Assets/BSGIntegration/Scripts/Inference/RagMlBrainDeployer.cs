using UnityEngine;
using Unity.InferenceEngine;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Assigns exported ONNX <see cref="ModelAsset"/> assets to zone physical (P) and cognitive (M) <see cref="BSGMLAgent"/> brains.
/// </summary>
public class RagMlBrainDeployer : MonoBehaviour
{
    [Header("Physical brains (locomotion / tasks)")]
    public ModelAsset zone0PhysicalModel;
    public ModelAsset zone1PhysicalModel;
    public ModelAsset zone2PhysicalModel;
    public ModelAsset zone3PhysicalModel;

    [Header("Cognitive brains (M_A decision / navigation)")]
    public ModelAsset zone0CognitiveModel;
    public ModelAsset zone1CognitiveModel;
    public ModelAsset zone2CognitiveModel;
    public ModelAsset zone3CognitiveModel;

    [Tooltip("If true, assigns models at Start(). Leave false during Python training.")]
    public bool applyOnStart;

    public bool deployCognitiveBrains = true;
    public bool deployPhysicalBrains = true;
    public bool autoLoadModelsFromInferenceFolder = true;

    const string InferenceOnnxFolder = "Assets/ML-Agents/Models/Inference";

    void Awake()
    {
        if (autoLoadModelsFromInferenceFolder)
            TryAutoLoadModelsFromInferenceFolder();
    }

    void Start()
    {
        if (applyOnStart)
            ApplyBrains();
    }

    /// <summary>Load ModelAsset sub-assets from Inference/*.onnx (editor). Reimport if missing.</summary>
    public bool EnsureModelsReadyForInference()
    {
        if (autoLoadModelsFromInferenceFolder)
            TryAutoLoadModelsFromInferenceFolder();

#if UNITY_EDITOR
        if (!AllZoneModelsAssigned())
            EditorImportInferenceOnnxFolder();
        TryAutoLoadModelsFromInferenceFolder();
#endif

        bool ok = AllZoneModelsAssigned();
        if (!ok)
            Debug.LogError("[RagMlBrainDeployer] One or more zone ONNX models missing. Run BSG → Inference → 3. Copy ONNX, then Fix ONNX Import.");
        return ok;
    }

    bool AllZoneModelsAssigned()
    {
        return zone0PhysicalModel != null && zone1PhysicalModel != null && zone2PhysicalModel != null && zone3PhysicalModel != null
               && zone0CognitiveModel != null && zone1CognitiveModel != null && zone2CognitiveModel != null && zone3CognitiveModel != null;
    }

    public void TryAutoLoadModelsFromInferenceFolder()
    {
        if (zone0PhysicalModel == null) zone0PhysicalModel = LoadModelAsset("PhysicalAgentZone0.onnx");
        if (zone1PhysicalModel == null) zone1PhysicalModel = LoadModelAsset("PhysicalAgentZone1.onnx");
        if (zone2PhysicalModel == null) zone2PhysicalModel = LoadModelAsset("PhysicalAgentZone2.onnx");
        if (zone3PhysicalModel == null) zone3PhysicalModel = LoadModelAsset("PhysicalAgentZone3.onnx");
        if (zone0CognitiveModel == null) zone0CognitiveModel = LoadModelAsset("CognitiveAgentZone0.onnx");
        if (zone1CognitiveModel == null) zone1CognitiveModel = LoadModelAsset("CognitiveAgentZone1.onnx");
        if (zone2CognitiveModel == null) zone2CognitiveModel = LoadModelAsset("CognitiveAgentZone2.onnx");
        if (zone3CognitiveModel == null) zone3CognitiveModel = LoadModelAsset("CognitiveAgentZone3.onnx");
    }

    static ModelAsset LoadModelAsset(string fileName)
    {
#if UNITY_EDITOR
        string path = $"{InferenceOnnxFolder}/{fileName}";
        return LoadModelAssetAtPath(path);
#else
        return null;
#endif
    }

#if UNITY_EDITOR
    static void EditorImportInferenceOnnxFolder()
    {
        if (!System.IO.Directory.Exists(InferenceOnnxFolder))
            return;

        foreach (string path in System.IO.Directory.GetFiles(InferenceOnnxFolder, "*.onnx"))
        {
            string assetPath = path.Replace('\\', '/');
            if (!assetPath.StartsWith("Assets/"))
            {
                int idx = assetPath.IndexOf("Assets/");
                if (idx >= 0) assetPath = assetPath.Substring(idx);
            }
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    public static ModelAsset LoadModelAssetAtPath(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath)) return null;

        Object[] sub = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        if (sub != null)
        {
            foreach (Object o in sub)
            {
                if (o is ModelAsset modelAsset)
                    return modelAsset;
            }
        }

        return AssetDatabase.LoadAssetAtPath<ModelAsset>(assetPath);
    }

    /// <summary>Legacy name for editor menus — loads Sentis <see cref="ModelAsset"/>.</summary>
    public static ModelAsset LoadNnModelAtPath(string assetPath) => LoadModelAssetAtPath(assetPath);
#endif

    [ContextMenu("Apply ONNX-backed brains (physical + cognitive)")]
    public void ApplyBrains()
    {
        EnsureModelsReadyForInference();

        BSGMLAgent[] agents = FindObjectsOfType<BSGMLAgent>(true);
        int physical = 0;
        int cognitive = 0;

        foreach (BSGMLAgent ml in agents)
        {
            if (ml == null) continue;

            if (ml.agentRole == BSGMLAgent.AgentRole.Physical && deployPhysicalBrains)
            {
                if (ApplyPhysicalBrainToAgent(ml))
                    physical++;
            }
            else if (ml.agentRole == BSGMLAgent.AgentRole.Mental && deployCognitiveBrains)
            {
                if (ApplyCognitiveBrainToAgent(ml))
                    cognitive++;
            }
        }

        Debug.Log($"[RagMlBrainDeployer] ApplyBrains: {physical} physical + {cognitive} cognitive agents received Sentis ModelAssets.");
    }

    public bool ApplyPhysicalBrainToAgent(BSGMLAgent ml) => ApplyPhysicalBrain(ml);

    public bool ApplyCognitiveBrainToAgent(BSGMLAgent ml) => ApplyCognitiveBrain(ml);

    bool ApplyPhysicalBrain(BSGMLAgent ml)
    {
        if (ml == null || !deployPhysicalBrains) return false;

        ModelAsset mdl = PhysicalModelForZone(ml.zoneIndex);
        if (mdl == null)
        {
            Debug.LogError($"[RagMlBrainDeployer] No Physical ModelAsset for zone {ml.zoneIndex} ({ml.agentId}). Assign PhysicalAgentZone{ml.zoneIndex} on deployer.");
            return false;
        }

        string bn = string.IsNullOrEmpty(ml.behaviorName)
            ? MLAgentAttacher.ResolveMlBehaviorNameForRagPhysical(null, ml.zoneIndex, ml.agentId)
            : ml.behaviorName;

        var bp = ml.GetComponent<BehaviorParameters>();
        if (bp == null)
        {
            Debug.LogError($"[RagMlBrainDeployer] {ml.agentId} has no BehaviorParameters.");
            return false;
        }

        bp.BehaviorName = bn;
        bp.Model = mdl;
        bp.BehaviorType = BehaviorType.InferenceOnly;

        ml.SetModel(bn, mdl, InferenceDevice.Default);
        Debug.Log($"[RagMlBrainDeployer] Physical {ml.agentId} zone {ml.zoneIndex} ← {mdl.name} ({bn}) InferenceOnly");
        return true;
    }

    bool ApplyCognitiveBrain(BSGMLAgent ml)
    {
        if (ml == null || !deployCognitiveBrains || ml.agentRole != BSGMLAgent.AgentRole.Mental)
            return false;

        var mac = ml.GetComponent<MentalAgentController>();
        if (mac != null && mac.role != MentalAgentController.MRole.M_A)
            return false;

        var ragMover = ml.GetComponent<RagSequenceAgentMover>();
        if (mac == null && (ragMover == null || !ragMover.isMentalAgent))
            return false;

        ModelAsset mdl = CognitiveModelForZone(ml.zoneIndex);
        if (mdl == null)
        {
            Debug.LogError($"[RagMlBrainDeployer] No Cognitive ModelAsset for zone {ml.zoneIndex} ({ml.agentId}). Assign CognitiveAgentZone{ml.zoneIndex} on deployer.");
            return false;
        }

        string bn = string.IsNullOrEmpty(ml.behaviorName)
            ? MLAgentAttacher.ResolveMlBehaviorNameForRagCognitive(null, ml.zoneIndex)
            : ml.behaviorName;

        var bp = ml.GetComponent<BehaviorParameters>();
        if (bp == null)
        {
            Debug.LogError($"[RagMlBrainDeployer] {ml.agentId} has no BehaviorParameters.");
            return false;
        }

        bp.BehaviorName = bn;
        bp.Model = mdl;
        bp.BehaviorType = BehaviorType.InferenceOnly;

        ml.SetModel(bn, mdl, InferenceDevice.Default);
        Debug.Log($"[RagMlBrainDeployer] Cognitive {ml.agentId} zone {ml.zoneIndex} ← {mdl.name} ({bn}) InferenceOnly");
        return true;
    }

    ModelAsset PhysicalModelForZone(int zoneIndex)
    {
        switch (Mathf.Clamp(zoneIndex, 0, 3))
        {
            case 0: return zone0PhysicalModel;
            case 1: return zone1PhysicalModel;
            case 2: return zone2PhysicalModel;
            default: return zone3PhysicalModel;
        }
    }

    ModelAsset CognitiveModelForZone(int zoneIndex)
    {
        switch (Mathf.Clamp(zoneIndex, 0, 3))
        {
            case 0: return zone0CognitiveModel;
            case 1: return zone1CognitiveModel;
            case 2: return zone2CognitiveModel;
            default: return zone3CognitiveModel;
        }
    }
}
