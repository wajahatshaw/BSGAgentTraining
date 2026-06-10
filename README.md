MultiplayerSetup
ProtoypeSceneMultiplayer

MultiplayerSetup: Photon lobby
ProtoypeSceneMultiplayer: the actual game scene 
     training sim (tasks, score, motor, keyboard, chat, timer).

zone0 bsg 


**BSG ML + RAG merge:** `ProtoypeSceneMultiplayer` bootstraps **zone 0 only** (P1, M1, cognitive stations) via `MultiplayerRagZone0Anchor` — no 4-zone grid overlay. Multiplayer UI, joystick, motor environment, and player camera stay primary; BSG training HUD is hidden. See `Assets/BSGIntegration/README.md`.

Solo ML training: `ProtoypeSceneMLTraining` + `mlagents-learn config/worker_training_config_fixed.yaml`  
ONNX inference: `ProtoypeSceneMLInference`