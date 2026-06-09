# BSG ML-Agents Integration (Ronald-Johnson V2)

Full **4-zone RAG** stack from BSG (`basicUI_ml2.json`, P1–P4 + M1–M4, 8 ML behaviors).  
Does **not** use the legacy single-zone motor stub or SIMPLE_Technician/Supervisor replica path.

## Source of truth (matches BSG Readme)

| BSG scene | PUN equivalent | Purpose |
|-----------|----------------|---------|
| `JSONWorkflowSceneML` | `ProtoypeSceneMLTraining` | Solo PPO training + Python |
| `JSONWorkflowSceneInference` | `ProtoypeSceneMLInference` | ONNX inference, no Python |
| — | `MultiplayerSetup` → `ProtoypeSceneMultiplayer` | **Photon + zone 0 RAG embed** (runtime bootstrap) |

## Layout

| Path | Purpose |
|------|---------|
| `Scripts/` | BSG RAG + ML runtime (ReplicaSceneSetup, SceneGenerator, BSGMLAgent, …) |
| `config/worker_training_config_fixed.yaml` | **8 behaviors** (PhysicalAgentZone0–3 + CognitiveAgentZone0–3) |
| `../JsonFile/basicUI_ml2.json` | 4-zone Marketing Manager RAG world |
| `../ML-Agents/Models/Inference/` | 8 exported ONNX brains |
| `../Scripts/Integration/RagMultiplayerSceneBootstrap.cs` | Zone 0 RAG embed into `ProtoypeSceneMultiplayer` |
| `../Scripts/Integration/MultiplayerRagZone0Anchor.cs` | Scene anchor + layout scale + overlap bounds |
| `../Scripts/Integration/TaskRagBridge.cs` | TaskManager ↔ CognitivePhaseOrchestrator |

## How to test

### A — Main multiplayer flow (recommended)

1. Open **`MultiplayerSetup`** → Play → connect Photon → start game  
2. Loads **`ProtoypeSceneMultiplayer`** (dominant scene)  
3. `RagMultiplayerSceneBootstrap` spawns **zone 0 only** (P1, M1, cognitive stations) under `MultiplayerRagZone0Anchor` — **no 4-zone grid**, no BSG floor/walls  
4. **Hidden in multiplayer:** BSG zone gauges, temporal buffer HUD, training speed canvas, god-view camera  
5. **Kept:** joystick, task accept/reject, chat, Photon UI, motor repair play area  

Tune placement in the Inspector: select **`MultiplayerRagZone0Anchor`** under `=Environment/Base` and adjust `layoutScale`, position, and `excludeOverlapBounds`.

Solo ML training still uses **`ProtoypeSceneMLTraining`** (full 4-zone BSG HUD + overview camera).

### B — Solo ML training (mirror BSG JSONWorkflowSceneML)

```bash
source /Users/mac/UnityProjects/BSG/ml-agents-env/bin/activate
cd "/Users/mac/UnityProjects/Ronald-Johnson-Game-Development V2"
mlagents-learn Assets/BSGIntegration/config/worker_training_config_fixed.yaml --run-id=persona_training --force
```

Open **`ProtoypeSceneMLTraining`** → Press Play.

Settings: `basicUI_ml2.json`, `ragOnlyMode=true`, `trainZonesMask=15` (all 4 zones).

### C — ONNX inference (mirror JSONWorkflowSceneInference)

Open **`ProtoypeSceneMLInference`** → Press Play (no Python).  
Uses `RagInferenceSceneController` + `RagMlBrainDeployer` with 8 ONNX files.

## Packages

- `com.unity.ml-agents` **4.0.0**
- `com.unity.ai.inference` **2.3.0**

See `PHASE0_VALIDATION.md` and `REGRESSION_CHECKLIST.md`.

## Deprecated (do not use)

- `motor_single_zone.json` / `motor_single_zone_training.yaml` — one-zone stub, not the BSG architecture
- `ProtoypeSceneMultiplayerML` — superseded by bootstrap on `ProtoypeSceneMultiplayer`
