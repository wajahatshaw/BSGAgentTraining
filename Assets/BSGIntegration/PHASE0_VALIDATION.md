# Phase 0 — BSG Standalone Validation

Validated on integration start:

- Python venv: `/Users/mac/UnityProjects/BSG/ml-agents-env` (`mlagents-learn` available)
- ONNX models (8): `PhysicalAgentZone0–3.onnx`, `CognitiveAgentZone0–3.onnx` in BSG `Assets/ML-Agents/Models/Inference/`
- Export scripts: `validate_onnx_export.sh`, `copy_onnx_to_unity.sh` copied to `Assets/BSGIntegration/scripts/`
- ONNX copies synced to this project at `Assets/ML-Agents/Models/Inference/`

Training command (BSG reference project):

```bash
cd /Users/mac/UnityProjects/BSG
source ml-agents-env/bin/activate
mlagents-learn config/worker_training_config_fixed.yaml --run-id=persona_training --force
```

Training command (this project, after Phase 3):

```bash
cd "/Users/mac/UnityProjects/Ronald-Johnson-Game-Development V2"
source /Users/mac/UnityProjects/BSG/ml-agents-env/bin/activate
mlagents-learn Assets/BSGIntegration/config/motor_single_zone_training.yaml --run-id=motor_zone0 --force
```

## Unity 6 packages (important)

Unity 6000.x renamed Sentis → **Inference Engine** (`com.unity.ai.inference`).  
ML-Agents **3.0.0** fails to compile on Unity 6 (`Unity.Sentis does not exist`).

Use in `Packages/manifest.json`:

- `com.unity.ml-agents`: **4.0.0**
- `com.unity.ai.inference`: **2.3.0**
- Do **not** add `com.unity.sentis` (Unity 6 builtin is an empty shim)

After changing packages: close Unity → delete `Library/` → reopen project.
