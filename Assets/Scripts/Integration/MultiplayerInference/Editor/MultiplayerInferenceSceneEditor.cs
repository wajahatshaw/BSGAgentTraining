#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor utilities for MultiplayerInferenceSetup lobby + ProtoypeSceneMultiplayerInference game scene.
/// </summary>
public static class MultiplayerInferenceSceneEditor
{
    const string TrainingLobby = "Assets/Scenes/MultiplayerSetup.unity";
    const string TrainingGame = "Assets/Scenes/ProtoypeSceneMultiplayer.unity";
    const string InferenceLobby = "Assets/Scenes/MultiplayerInferenceSetup.unity";
    const string InferenceGame = "Assets/Scenes/ProtoypeSceneMultiplayerInference.unity";

    [MenuItem("BSG/Inference/Multiplayer/1. Duplicate Multiplayer Scenes (Inference)")]
    public static void DuplicateMultiplayerInferenceScenes()
    {
        if (!File.Exists(TrainingLobby) || !File.Exists(TrainingGame))
        {
            Debug.LogError("Missing MultiplayerSetup or ProtoypeSceneMultiplayer — open the training multiplayer project first.");
            return;
        }

        CopyScene(TrainingLobby, InferenceLobby);
        CopyScene(TrainingGame, InferenceGame);

        PatchLobbyNextLevel(InferenceLobby, MultiplayerInferenceSceneNames.GameScene);
        Debug.Log($"Created {InferenceLobby} and {InferenceGame}. Run '2. Add Scenes to Build Settings' then open MultiplayerInferenceSetup to test.");
    }

    [MenuItem("BSG/Inference/Multiplayer/2. Add Scenes to Build Settings")]
    public static void AddScenesToBuildSettings()
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        AddSceneIfMissing(scenes, InferenceLobby);
        AddSceneIfMissing(scenes, InferenceGame);
        EditorBuildSettings.scenes = scenes.ToArray();
        Debug.Log("Multiplayer inference scenes added to Build Settings.");
    }

    [MenuItem("BSG/Inference/Multiplayer/3. Verify ONNX Models")]
    public static void VerifyOnnxModels()
    {
        RagInferenceSceneEditor.VerifyDeployerModels();
    }

    static void CopyScene(string src, string dst)
    {
        if (File.Exists(dst))
        {
            Debug.LogWarning($"{dst} already exists — skipping copy.");
            return;
        }

        if (!AssetDatabase.CopyAsset(src, dst))
            Debug.LogError($"Failed to copy {src} → {dst}");
        else
            Debug.Log($"Copied {src} → {dst}");
    }

    static void PatchLobbyNextLevel(string lobbyPath, string nextLevel)
    {
        string text = File.ReadAllText(lobbyPath);
        const string oldNext = "nextLevelName: ProtoypeSceneMultiplayer";
        string newNext = $"nextLevelName: {nextLevel}";
        if (text.Contains(oldNext))
        {
            text = text.Replace(oldNext, newNext);
            File.WriteAllText(lobbyPath, text);
            AssetDatabase.ImportAsset(lobbyPath);
            Debug.Log($"Patched {lobbyPath} nextLevelName → {nextLevel}");
        }
        else if (text.Contains($"nextLevelName: {nextLevel}"))
        {
            Debug.Log($"{lobbyPath} already points to {nextLevel}.");
        }
        else
        {
            Debug.LogWarning($"Could not patch nextLevelName in {lobbyPath} — set NetworkManager.nextLevelName to {nextLevel} manually.");
        }
    }

    static void AddSceneIfMissing(System.Collections.Generic.List<EditorBuildSettingsScene> scenes, string path)
    {
        foreach (EditorBuildSettingsScene s in scenes)
        {
            if (s.path == path)
                return;
        }

        scenes.Add(new EditorBuildSettingsScene(path, true));
    }
}
#endif
