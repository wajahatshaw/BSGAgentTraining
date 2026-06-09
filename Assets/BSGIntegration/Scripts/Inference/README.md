# JSONWorkflowSceneInference — scripts

Runtime and editor code for **JSONWorkflowSceneInference** (ONNX inference via **Sentis**, no `mlagents-learn`).

**Packages:** `com.unity.ml-agents` 3.x, `com.unity.sentis` (replaces Barracuda + `NNModel`).

## Layout

| Path | Role |
|------|------|
| `RagInferenceSceneController.cs` | Scene entry: disables training, hides mental visuals, runs bootstrap |
| `RagInferenceMLBootstrap.cs` | Attaches `BSGMLAgent`, keeps physical movers on (scripted walk), ONNX for interaction |
| `RagMlBrainDeployer.cs` | Assigns eight `ModelAsset` assets (Physical/Cognitive × zones 0–3) |
| `RagInferenceVisuals.cs` | Hides cognitive/mental renderers while keeping GameObjects active |
| `RagCognitiveInferenceCoordinator.cs` | Optional fast-forward path when scripted mental walk is off |
| `Editor/RagInferenceSceneEditor.cs` | Menu: **BSG → Inference** (duplicate scene, wire, copy ONNX) |
| `Editor/OnnxInferenceImportFix.cs` | Sentis reimport + relink deployer |
| `JSONWorkflowSceneInference_SETUP.md` | Setup checklist |

## Related (stay in `Assets/Scripts/`)

Shared with training / RAG workflow: `BSGMLAgent`, `RagSequenceAgentMover`, `CognitivePhaseOrchestrator`, `MLAgentAttacher`, `ReplicaSceneSetup`, etc. They call into this folder via `RagInferenceSceneController.IsInferenceSceneActive()`.

## Scene & assets

- Scene: `Assets/Scenes/JSONWorkflowSceneInference.unity`
- Models: `Assets/ML-Agents/Models/Inference/` (import as Sentis **ModelAsset**)
