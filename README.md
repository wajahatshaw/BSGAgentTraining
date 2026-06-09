MultiplayerSetup
ProtoypeSceneMultiplayer

MultiplayerSetup: Photon lobby
ProtoypeSceneMultiplayer: the actual game scene 
     training sim (tasks, score, motor, keyboard, chat, timer).

zone0 bsg 


**BSG ML + RAG merge:** `ProtoypeSceneMultiplayer` bootstraps **zone 0 only** (P1, M1, cognitive stations) via `MultiplayerRagZone0Anchor` — no 4-zone grid overlay. Multiplayer UI, joystick, motor environment, and player camera stay primary; BSG training HUD is hidden. See `Assets/BSGIntegration/README.md`.

Solo ML training: `ProtoypeSceneMLTraining` + `mlagents-learn config/worker_training_config_fixed.yaml`  
ONNX inference: `ProtoypeSceneMLInference`


PhotonNetwork is the main static class provided by Photon Engine

Allows your Unity game
To connect to Photon servers
Join Lobby
Create a Room

Two type of roles :

Supervisor : (Room master / host) can start the game once the workers status are ready in lobby.
Workers: all other member consider as workersMultiplayerSetup = Photon lobby (connect → join room → ready → start).



-----------  Game over view -----


Lobby: Players connect via Photon, join a shared room, and get a role automatically — the room host (Master Client) becomes the Supervisor, and everyone else is a Worker. Workers press Ready; the Supervisor’s Start button only unlocks when all Workers are ready.

Game scene: After the 5-second countdown, all players enter the training simulation (ProtoypeSceneMultiplayer), where Workers complete sequential tasks under a timer, with movement, interaction, chat, inventory, and scoring. Tasks may show a “supervisor” name in the UI as the assigner, but that’s narrative text — not a separate AI character.

In short: Multiplayer lobby → Workers ready up → human Supervisor starts → everyone plays the same 3D training sim together, with the Supervisor acting as session host

Game end: When the full task list is finished sequentially, ChapterEnd() runs — not “each worker finished their own assigned piece.” A global timer tracks overall time; failing it can fail the current task.








----------

Photon Unity Networking (PUN)  

    Room creation & joining
    Multiplayer Basics
    Room creation & joining
    Networking

    Networking
    Region selection 


DOTween is a Unity plugin
    used to create smooth animations over time (tweens) without manually writing Update loops.
    
    Instead of writing complex interpolation code, you simply say:
    
    “Move this object here in 1 second” and DOTween handles the animation.










source /Users/mac/UnityProjects/BSG/ml-agents-env/bin/activate
cd "/Users/mac/UnityProjects/Ronald-Johnson-Game-Development V2"

mlagents-learn Assets/BSGIntegration/config/worker_training_config_fixed.yaml \
  --run-id=persona_training \
  --force

# ONNX appears under results/persona_training/<BehaviorName>/ after checkpoints save.
# Config uses checkpoint_interval: 10000 — leave training running several minutes after Unity
# shows "RAG ML bootstrap complete" in the Console. Ctrl+C also exports final .onnx if steps > 0.
# Verify: find results/persona_training -name "*.onnx"


 source /Users/mac/UnityProjects/BSG/ml-agents-env/bin/activate
cd "/Users/mac/UnityProjects/Ronald-Johnson-Game-Development V2"

mlagents-learn Assets/BSGIntegration/config/worker_training_config_fixed.yaml \
  --run-id=persona_training \
  --resume
  