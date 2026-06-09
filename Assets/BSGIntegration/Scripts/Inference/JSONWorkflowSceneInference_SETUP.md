# JSONWorkflowSceneInference — ONNX test scene

Separate from **JSONWorkflowSceneML** (training). No Python, no `mlagents-learn`, no YAML.

**Stack:** Unity ML-Agents **3.x** + **Sentis** (`ModelAsset`, not Barracuda `NNModel`).

## Quick setup (Unity menu)

1. **BSG → Inference → 1. Duplicate Training Scene** → creates `Assets/Scenes/JSONWorkflowSceneInference.unity`
2. **BSG → Inference → 3. Copy ONNX** (or **BSG → Inference → Copy ONNX from results**) → eight `.onnx` in `Assets/ML-Agents/Models/Inference/`
3. **BSG → Inference → Fix ONNX Import (reimport + relink deployer)** — Sentis import + auto-fill `RagMlBrainDeployer`
4. **BSG → Inference → 2. Wire Inference Scene** → adds controller + deployer, loads ModelAssets, applies brains
5. Open **JSONWorkflowSceneInference** (Hierarchy must show `REPLICA_SceneManager`, not only Camera/Light)
6. **Play** (no `mlagents-learn`)

### ONNX error: “corrupted or newer version of Unity”

The `.onnx` file is usually fine; Unity failed to build the **ModelAsset** sub-asset. Fix:

1. **BSG → Inference → Fix ONNX Import** (or delete `Assets/ML-Agents/Models/Inference/*.meta` and reimport)
2. Click `CognitiveAgentZone1.onnx` in Project — Inspector should show **Sentis** importer, no red errors in Console
3. Expand the asset — you should see a **ModelAsset** child; drag that (not the raw file) if assigning manually
4. `RagMlBrainDeployer` loads from the Inference folder when slots are empty

### After upgrading from ML-Agents 2 / Barracuda

1. Confirm `Packages/manifest.json` includes **`com.unity.sentis`** (e.g. `2.1.2`) alongside `com.unity.ml-agents` `3.0.0`
2. Let Package Manager finish resolving; if you see `Unity.Sentis` does not exist, delete `Library/` and reopen the project
3. Run steps 3–4 above (old `NNModel` scene references will be cleared)

## What runs

| Layer | Training scene | Inference scene |
|--------|----------------|-----------------|
| Python | Required | **Off** |
| `ReplicaSceneSetup.enableMlTrainingInRagMode` | true | **false** |
| Mental boxes | Visible | **Renderers off** (objects stay active) |
| M1–M4 mesh | Visible | **Hidden** (GameObjects active) |
| Cognitive steps | `RagSequenceAgentMover` walks stations | **Option 1 (default):** invisible `RagSequenceAgentMover` walks DAG; or fast-forward + silent ONNX |
| Physical P1–P4 | `RagSequenceAgentMover` on + PPO for interaction | **`RagSequenceAgentMover` on** (same walk/proximity as training); **ONNX interaction only** (`useScriptedPhysicalLocomotion=true`, default) |
| Behavior type | Default (train) | **Inference Only** (via `SetModel`) |
| Debug lines (thread/scan) | Visible during cognitive/observation | **Hidden** in inference |

## Scripts

All inference-only code lives under **`Assets/Scripts/Inference/`** (see `README.md` there).

- `RagInferenceSceneController` — disables training flags, hides visuals, attaches inference ML (`useScriptedPhysicalLocomotion` on REPLICA_SceneManager)
- `RagInferenceMLBootstrap` — attaches `BSGMLAgent`, keeps physical movers enabled, applies ONNX for interaction
- `RagCognitiveInferenceCoordinator` — silent cognitive decisions → `IsCognitivePhaseComplete`
- `RagMlBrainDeployer` — assign eight Sentis **ModelAsset** slots

## Python (re-export ONNX)

If Sentis reimport fails on old checkpoints, upgrade the trainer and re-export:

```bash
pip install "mlagents==1.1.0"   # pairs with Unity com.unity.ml-agents 3.0.0 — verify against ML-Agents release notes
mlagents-learn config/worker_training_config_fixed.yaml --run-id=reexport --force
```

Then copy ONNX again via **BSG → Inference → 3. Copy ONNX**.

## Checklist

- [ ] `com.unity.ml-agents` 3.x + `com.unity.sentis` in Package Manager
- [ ] Eight ONNX in `Assets/ML-Agents/Models/Inference/`
- [ ] All eight **ModelAssets** assigned on `RagMlBrainDeployer`
- [ ] No `mlagents-learn` process running
- [ ] Open **JSONWorkflowSceneInference** (not ML training scene)
- [ ] `RagInferenceSceneController.useScriptedPhysicalLocomotion` is **true** (default) on REPLICA_SceneManager
- [ ] Physical agents walk with leg animation and stop beside targets (not on top of objects)
- [ ] No yellow thread/scan lines visible during play
