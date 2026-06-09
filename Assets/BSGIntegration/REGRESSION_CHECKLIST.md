# Phase 6 — Regression Checklist

Run manually in Unity after ML integration changes.

## Unchanged scenes (must still work)

- [ ] **MultiplayerSetup** — connect, join room, workers Ready, supervisor Start, 5s countdown
- [ ] **ProtoypeSceneMultiplayer** — spawn, move, motor task, quiz, score, chat, timer; zone 0 RAG (P1/M1 + cognitive stations) via anchor, no 4-grid overlay
- [ ] **ProtoypeScene** — single-player baseline unchanged

## New ML scenes (isolated)

- [ ] **ProtoypeSceneMLTraining** — RAG scene generates from `motor_single_zone.json`, zone 0 only, Python connects on port 5004
- [ ] **ProtoypeSceneMLInference** — ONNX inference without Python; mental visuals hidden

## Optional ML multiplayer

- [ ] **ProtoypeSceneMultiplayerML** — copy of multiplayer + `TaskRagBridge` auto-bootstrap; set `NetworkManager.nextLevelName` to this scene for testing

## Console

- [ ] No new errors in non-ML scenes when ML packages are installed
- [ ] Original Photon scripts untouched (`NetworkManager`, `PlayerContainerManager`)
