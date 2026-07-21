MultiplayerSetup
ProtoypeSceneMultiplayer

MultiplayerSetup: Photon lobby
ProtoypeSceneMultiplayer: the actual game scene 
     training sim (tasks, score, motor, keyboard, chat, timer).

zone0 bsg 


**BSG ML + RAG merge:** `ProtoypeSceneMultiplayer` bootstraps **zone 0 only** (P1, M1, cognitive stations) via `MultiplayerRagZone0Anchor` — no 4-zone grid overlay. Multiplayer UI, joystick, motor environment, and player camera stay primary; BSG training HUD is hidden. See `Assets/BSGIntegration/README.md`.

Solo ML training: `ProtoypeSceneMLTraining` (legacy 4-zone grid — **not** locomotion Y-Bot)  
**Locomotion training:** `MultiplayerSetup` → `ProtoypeSceneMultiplayer` + `mlagents-learn` (see `cursor/plans/locomotion_phase1_2_plan.md`)
ONNX inference: `ProtoypeSceneMLInference`  
Multiplayer ONNX inference: `MultiplayerInferenceSetup` → `ProtoypeSceneMultiplayerInference` (no Python; see `Assets/Scripts/Integration/MultiplayerInference/README.md`)


PhotonNetwork is the main static class provided by Photon Engine

Allows your Unity game
To connect to Photon servers
Join Lobby
Create a Room

Two type of roles :

Supervisor : (Room master / host) can start the game once the workers status are ready in lobby.
Workers: all other member consider as workersMultiplayerSetup = Photon lobby (connect → join room → ready → start).



-----------  Game over view -----


<!-- Lobby: Players connect via Photon, join a shared room, and get a role automatically — the room host (Master Client) becomes the Supervisor, and everyone else is a Worker. Workers press Ready; the Supervisor’s Start button only unlocks when all Workers are ready.

Game scene: After the 5-second countdown, all players enter the training simulation (ProtoypeSceneMultiplayer), where Workers complete sequential tasks under a timer, with movement, interaction, chat, inventory, and scoring. Tasks may show a “supervisor” name in the UI as the assigner, but that’s narrative text — not a separate AI character.

In short: Multiplayer lobby → Workers ready up → human Supervisor starts → everyone plays the same 3D training sim together, with the Supervisor acting as session host

Game end: When the full task list is finished sequentially, ChapterEnd() runs — not “each worker finished their own assigned piece.” A global timer tracks overall time; failing it can fail the current task. -->



source /Users/mac/UnityProjects/BSG/ml-agents-env/bin/activate
cd "/Users/mac/UnityProjects/BSGAgentTraining"

mlagents-learn Assets/BSGIntegration/config/worker_training_config_fixed.yaml \
  --run-id=persona_training \
  --force

# ONNX appears under results/persona_training/<BehaviorName>/ after checkpoints save.
# Config uses checkpoint_interval: 10000 — leave training running several minutes after Unity
# shows "RAG ML bootstrap complete" in the Console. Ctrl+C also exports final .onnx if steps > 0.
# Verify: find results/persona_training -name "*.onnx"


source /Users/mac/UnityProjects/BSG/ml-agents-env/bin/activate
cd "/Users/mac/UnityProjects/BSGAgentTraining"

mlagents-learn Assets/BSGIntegration/config/worker_training_config_fixed.yaml \
  --run-id=persona_training \
  --resume
  
  

BSG Inference scene (orignal scene):  JSONWorkflowSceneInference
BSg training scene:   JSONWorkflowSceneML -> 

multiplayer training : MultiplayerSetup
Second scen: ProtoypeSceneMultiplayer


multiplayer training : MultiplayerSetup 
multiplayer inference (orignal) : MultiplayerInferenceSetup





<!-- mlagents-learn Assets/BSGIntegration/config/worker_training_config_fixed.yaml \
  --run-id=bsg_loco_phase1 --time-scale=20 --no-graphics -->

source /Users/mac/UnityProjects/BSG/ml-agents-env/bin/activate
cd "/Users/mac/UnityProjects/BSGAgentTraining"


mlagents-learn Assets/BSGIntegration/config/worker_training_config_fixed.yaml \
  --run-id=bsg_loco_phase1 \
  --resume \
  --time-scale=20 \
  --no-graphics


  mlagents-learn Assets/BSGIntegration/config/worker_training_config_locomotion_phase2.yaml \
  --run-id=bsg_loco_phase2 \
  --initialize-from=bsg_loco_phase1 \
  --time-scale=20 \
  --no-graphics

  






Two checks:

A. Did the locomotion agent install? In the Unity Console, search for:

YBotLocomotionInstaller
[YBotLocomotionInstaller] PhysicalAgentZone0 (Y-Bot locomotion) installed on 'Y Bot': obs=58, continuous actions=21

B. Is the Python trainer collecting steps? 




Run it and report these three values from that line
behaviorType= → must be Default (it now should be, since I force it).
communicatorOn= → must be True (means Unity is actually connected to the trainer this session).
model= → should be none (if a .onnx is assigned, it runs inference, not the trainer).
Then watch the [YBotWalker] TRAINING line: actionsReceived should now climb above 0.

What "training happening correctly" looks like (so you can judge it)
actionsReceived > 0 and climbing → policy is driving the joints.
episodeReward trending UP over many episodes (less negative, then positive) — not steadily down like now.











ybot_walk_v3 was trained with the walk-to-target reward. You just changed the objective to stand-still stability, so its learned policy/value would fight the new reward. Start fresh:


mlagents-learn Assets/BSGIntegration/config/worker_training_config_fixed.yaml \
  --run-id=ybot_stand_01 --force --time-scale=20





Wait for Listening on port 5004, then Play.

What "good" looks like for Phase 1 (stability)
In TensorBoard (tensorboard --logdir results) and the [YBotWalker] line:

episodeReward trends up and episode length grows (it stays upright longer before tipping).
upright stays near 1.0, hipHeight near standHeight (~1.0).
Visually: it stops tipping over and holds a standing pose, roughly in place (not lurching around).
Phase-1 standing usually converges faster than walking — keep an eye on the reward plateauing high with long episodes.

When it stands reliably → Phase 2 (walk)
Because the agent is added at runtime, the stabilizeOnly inspector value doesn't persist. To move to walking:

Change the default in code: public bool stabilizeOnly = true; → false.
Recompile, then resume from the Phase-1 checkpoint:



mlagents-learn …yaml --run-id=ybot_walk_phase2 --initialize-from=ybot_stand_01 --time-scale=20














✅ ArticulationBody rig builds, stays grounded in-zone, no explosions/NaN.
✅ Trainer connects (communicatorOn=True), agent receives actions



  mlagents-learn …yaml --run-id=ybot_stand_03 --force --time-scale=20 --no-graphics
  
  
  source /Users/mac/UnityProjects/BSG/ml-agents-env/bin/activate
cd /Users/mac/UnityProjects/BSGAgentTraining
mlagents-learn Assets/BSGIntegration/config/worker_training_config_fixed.yaml \
  --run-id=ybot_stand_03 --resume --time-scale=20







  cmd for stability :

   source /Users/mac/UnityProjects/BSG/ml-agents-env/bin/activate
cd /Users/mac/UnityProjects/BSGAgentTraining

mlagents-learn Assets/BSGIntegration/config/worker_training_config_fixed.yaml \
  --run-id=ybot_stand_03 --resume



  mlagents-learn Assets/BSGIntegration/config/ybot_walk.yaml \
  --run-id=ybot_stand_03 --resume