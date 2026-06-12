# Multiplayer ONNX inference

Separate from **MultiplayerSetup** (Python training). Test exported ONNX models with M1 + designated Photon P1 in zone 0.

## Scenes

| Scene | Role |
|-------|------|
| `MultiplayerInferenceSetup` | Photon lobby (same flow as MultiplayerSetup) |
| `ProtoypeSceneMultiplayerInference` | Game scene — zone 0 RAG + Sentis inference |

Training scenes (`MultiplayerSetup`, `ProtoypeSceneMultiplayer`) are unchanged.

## Scripts (this folder)

| File | Role |
|------|------|
| `MultiplayerInferenceSceneNames.cs` | Scene name constants |
| `RagMultiplayerInferenceSceneBootstrap.cs` | Runtime embed: RAG zone 0, ONNX, network sync |
| `RagMultiplayerInferenceMLBootstrap.cs` | Attach CognitiveAgentZone0 + PhysicalAgentZone0 ONNX |
| `PlayerRagInferenceBridge.cs` | Bind designated Photon player as P1 with InferenceOnly brain |
| `Editor/MultiplayerInferenceSceneEditor.cs` | BSG → Inference → Multiplayer menu |

Shared inference code lives in `Assets/BSGIntegration/Scripts/Inference/` (`RagInferenceSceneController`, `RagMlBrainDeployer`).

## Setup

1. Train and export ONNX (see root README) or copy models:
   **BSG → Inference → 3. Copy ONNX**
2. **BSG → Inference → Multiplayer → 1. Duplicate Multiplayer Scenes (Inference)**
3. **BSG → Inference → Multiplayer → 2. Add Scenes to Build Settings**
4. Open **MultiplayerInferenceSetup**, press Play — no `mlagents-learn` process.

## ONNX models

Auto-loaded from `Assets/ML-Agents/Models/Inference/`:

- `CognitiveAgentZone0.onnx` → M1 mental agent
- `PhysicalAgentZone0.onnx` → designated Photon player (P1)

Assign custom models on `REPLICA_SceneManager` → `RagMlBrainDeployer` in the game scene if needed.

## Flow

1. Lobby: connect, ready, start (same as training multiplayer).
2. Game scene bootstraps zone 0 RAG from `basicUI_ml2.json`.
3. M1 runs cognitive sequence with ONNX (invisible mental walk by default).
4. Designated player (room property / nick) binds as P1 with PhysicalAgentZone0 ONNX.
5. Cognitive then physical DAG steps run like training; verify tasks complete in Console + UI.

## Verify

- Console: `[RagMultiplayerInferenceMLBootstrap]` and `[PlayerRagInferenceBridge]` success logs.
- No Python trainer required; `BehaviorType.InferenceOnly` on M1 and P1.
