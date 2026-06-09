# Python trainer alignment (ML-Agents 3 / Sentis)

Use when Sentis fails to import ONNX exported from an older Unity 2.x / Barracuda trainer.

## Steps

1. Match Python package to Unity `com.unity.ml-agents` version (see [ML-Agents releases](https://github.com/Unity-Technologies/ml-agents/releases) — Unity **3.0.0** typically pairs with **`mlagents` 1.1.0** on PyPI).
2. Activate your venv and upgrade:

   ```bash
   source ml-agents-env/bin/activate
   pip install --upgrade "mlagents==1.1.0"
   ```

3. Re-export or finish a short train, then copy ONNX:

   ```bash
   mlagents-learn config/worker_training_config_fixed.yaml --run-id=persona_training --force
   ```

4. In Unity: **BSG → Inference → 3. Copy ONNX** then **Fix ONNX Import**.

Configs use `memory: null` — no LSTM re-export issue for Sentis.
