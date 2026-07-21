# Y-Bot Walker — ML-Agents Locomotion Setup

Convert the designated Y-Bot player from an animation-driven character into a
fully physics-enabled ragdoll that learns to walk via ML-Agents PPO. This
document is the requirement + implementation summary for that conversion.

> **Scope note.** This is the *clean standalone* walker recipe (behavior
> `YBotWalker`, three dedicated scripts). The live multiplayer pipeline in this
> repo wires the same idea automatically at runtime under behavior name
> `PhysicalAgentZone0` (91 obs / 19 actions) via `LocomotionAgentSetup`. Use
> this doc when standing up a Y-Bot walker by hand; see the locomotion pipeline
> notes for the auto-wired path.

---

## Goal

Replace all scripted/animated motion on the Y-Bot with an articulated physics
skeleton driven entirely by a trained policy. Before training the body is a
ragdoll that falls over; after training the policy holds it upright and walks it
toward a target.

---

## ⚠️ Hard constraint — do not break the three camera/display views

The designated player is observed through **three game-view displays**. The
conversion must **not** disturb any of them — no changes to `targetDisplay`
values, camera object names, or the camera target-resolution chain.

| Display | targetDisplay | Camera object | What it shows | Coupling to the player |
| --- | --- | --- | --- | --- |
| **Display 1** | `0` | `PlayerCamera` (under `CameraContainer`, in `Player.prefab`) | First-person / front view | **Parented to the player root** — it physically rides the body. Once the body becomes an AB ragdoll it will tumble with the body until a policy stabilizes it. Do **not** reparent or destroy `CameraContainer`. |
| **Display 2** | `1` | `GameView_Display2_Zone0_Overview_Camera` ([MultiplayerZone0OverviewCamera.cs](../Scripts/Integration/MultiplayerZone0OverviewCamera.cs)) | Top-down overview of zone 0 | Frames the **zone**, not the player. Safe — no player-component dependency. |
| **Display 3** | `2` | `GameView_Display3_DesignatedPlayer_Drone_Camera` ([MultiplayerDesignatedPlayerFollowCamera.cs](../Scripts/Integration/MultiplayerDesignatedPlayerFollowCamera.cs)) | Drone third-person follow of the designated player | **Tightly coupled.** Finds its target via `ResolveDesignatedPlayerTransform()`, whose every path resolves through `PlayerMovement` or `PlayerRagPhysicalBridge.BoundMover` (itself a `PlayerMovement`). It also calls `DesignatedPhysicalPlayerAppearance.ResolveBodyHeight(target)` and reads `target.forward` / `target.position`. |

**Rules for the conversion:**

1. **Do not naively remove `PlayerMovement`** (Step 1) — that is the anchor the
   Display-3 drone camera resolves to. Either keep the component on the root
   (disable only its *driving* logic) **or** add an AB-rig-aware resolver to
   `TryFindDesignatedPlayerTransform()` / `ResolveDesignatedPlayerTransform()`
   **before** removing it. Verify Display 3 still follows after the change.
2. **Disabling the `Animator` is camera-safe** — no camera reads it. Prefer
   *disabling* over removing (matches the existing pipeline, which disables
   `Animator`/`PhotonAnimatorView`).
3. **Keep the root transform meaningful.** The drone camera reads the player
   root's `position` (feet) and `forward`, and `ResolveBodyHeight` reads
   `lossyScale` / the `CapsuleCollider`. Keep a sane root transform + the
   capsule so framing stays correct; with `LocomotionRigActive` the scale is
   `1`, which `ResolveBodyHeight` already handles.
4. **Do not touch** `targetDisplay` values, the camera object names, or
   `EnforceExclusiveDisplay` — display exclusivity is enforced by name/display.

---

## Implementation status (this branch)

The designated player is spawned and wired at **runtime** (Photon clone), so the
prep is done in code, not the Inspector. Gated by a single scene switch:
`MultiplayerRagZone0Anchor.enableZone0LocomotionTraining` (default **off**).

| Prep step | Status | Where |
| --- | --- | --- |
| Unit-scale gate | ✅ Done | `DesignatedPhysicalPlayerAppearance.LocomotionRigActive` |
| Detach from RAG (no stepping/autopilot/gait) | ✅ Done | `RagSequenceAgentMover.DetachForLocomotion()` + `Update()` early-out |
| Disable static `Animator` | ✅ Done | `PlayerRagPhysicalBridge.TryPrepareLocomotionForDesignatedPlayer()` |
| Skip RAG cognitive/physical ML on this body | ✅ Done | guard in `TryAttachMlTrainingForDesignatedPlayer()` |
| `ArticulationBody` rig (bones→AB, drives, self-collision, solver, reset) | ✅ Done | `Scripts/Locomotion/YBotLocomotionRig.cs` |
| Foot contact sensor | ✅ Done | `Scripts/Locomotion/YBotFootContact.cs` |
| Walker agent (obs/actions/rewards/episode/fall) | ✅ Done | `Scripts/Locomotion/YBotWalkerAgent.cs` |
| Runtime installer (rig + agent + BehaviorParameters + DecisionRequester) | ✅ Done | `Scripts/Locomotion/YBotLocomotionInstaller.cs` |
| Trainer YAML | ✅ Done | `config/ybot_walker_training.yaml` |

**Behavior name:** `PhysicalAgentZone0` (reuses the existing trainer slot so it runs
through the project's `worker_training_config_fixed.yaml` pipeline). **Action/obs sizes
are derived from the rig** (not hardcoded) so they can't drift: the reduced rig has
**11 actuated joints / 21 DOF** → **21 continuous actions** and **2·DOF + 16 = 58
observations**. (The original checklist's "17 joints × 4 = 68" was a placeholder; the
real count comes from the joint DOFs — spherical = 3, revolute = 1.) `BehaviorParameters`
and `DecisionRequester` (period 5) are configured in code by the installer.

The `PhysicalAgentZone0` block in `worker_training_config_fixed.yaml` was retuned for the
continuous walker (512×3 net, entropy, no curiosity); all other zone/cognitive behaviors
are commented out and cognitive ML is disabled on the scene anchor
(`enableMlTrainingForCognitiveAgents = 0`), so only the walker connects to the trainer.

**To enable:** tick `enableZone0LocomotionTraining` on the `MultiplayerRagZone0Anchor`
(already set in `ProtoypeSceneMultiplayer.unity`). On bind, the designated P1 detaches
from RAG, drops to unit scale, Animator disabled, then the AB rig + `YBotWalker` agent
are installed. With no trained model, the body **ragdolls and falls** (expected). The
three camera displays are unaffected (drone camera reads the same transform).

**To train:** start the trainer FIRST, then press Play (ordering gotcha). Use a NEW
run-id — the old `persona_training` checkpoints are from the discrete RAG agent and a
`--resume` would crash on the changed action space:
```
source /Users/mac/UnityProjects/BSG/ml-agents-env/bin/activate
cd /Users/mac/UnityProjects/BSGAgentTraining
mlagents-learn Assets/BSGIntegration/config/worker_training_config_fixed.yaml \
  --run-id=ybot_locomotion_01
```

The manual recipe below documents the equivalent Inspector steps / the end-state
contract, for reference.

---

## Step 1 — Strip the designated player GameObject

In the Inspector, **remove** every component that drives the body externally:

| Remove | Why |
| --- | --- |
| `Animator` | Physics, not keyframes, must drive the bones. **Disable, don't remove** — camera-safe, matches existing pipeline. |
| `Animation` (legacy clips) | Same — no clip playback |
| Movement script (e.g. `PlayerMovement.cs`) | Policy supplies motion. ⚠️ **Camera-coupled** — see the camera constraint above; keep it (disable driving logic) or add an AB-rig camera resolver first. |
| `CharacterController` (if used) | Replaced by `ArticulationBody` physics |
| RAG step-listener / movement-trigger scripts | No external motion triggers |
| Mixamo animation-event receivers | No animation events fire anymore |

## Step 2 — Disconnect from the RAG sequence

In the RAG / step manager, find where it references the designated player and
**comment out** that reference so the sequence no longer drives or moves it.

## Step 3 — Add `ArticulationBody` to every bone

Bone by bone on the Y-Bot skeleton, add an `ArticulationBody` and set the joint
type:

| Bone(s) | Joint type | Notes |
| --- | --- | --- |
| Hips (root) | — | `immovable = false`, **is root = true** |
| Spine / Chest / Neck / Head | Spherical | |
| UpperLeg L+R | Spherical | |
| LowerLeg L+R | Revolute | knee hinge |
| Foot L+R | Revolute | ankle hinge |
| UpperArm L+R | Spherical | |
| Forearm L+R | Revolute | elbow hinge |
| Hand L+R | Spherical | |

Also ensure each bone has a **collider** and a sensible **mass** so the rig
balances realistically.

## Step 4 — Attach the three scripts

| Script | Attach to |
| --- | --- |
| `YBotJointController.cs` | Hips (root bone) |
| `YBotWalkerAgent.cs` | Y-Bot root GameObject |
| `YBotFootContact.cs` | `LeftFoot` **and** `RightFoot` |

## Step 5 — Behavior Parameters (on the Y-Bot root)

- **Behavior Name:** `YBotWalker` — must match the `behaviors:` key in YAML
  **exactly** (one typo breaks the trainer connection).
- **Vector Observation Space Size:** must match what `CollectObservations()`
  writes.
- **Continuous Actions:** `68` (17 joints × 4).
- **Behavior Type:** `Heuristic Only` until training starts.
- **Model:** none assigned yet (filled in after training produces an ONNX).

## Step 6 — Add a Decision Requester

`Add Component → Decision Requester`

- **Decision Period:** `5`
- **Take Actions Between Decisions:** `true`

---

## What lives where: Unity (C#) vs YAML

| Concern | Where | Behavior |
| --- | --- | --- |
| **Observations** | `CollectObservations()` — joint angles, body height, direction to target | Identical code runs during training **and** ONNX inference; never changes |
| **Actions** | `OnActionReceived()`; size set in Behavior Parameters | `68` continuous; set once, never change between train/inference |
| **Rewards / Penalties** | `AddReward()` in `FixedUpdate` / `OnActionReceived` | Teaches the agent in training; still runs under ONNX but has no effect |
| **Episode reset** | `OnEpisodeBegin()` resets to standing + randomizes target; `EndEpisode()` on fall/reach | Both required |
| **Movement / game rules** | Joint drives, foot-contact detection, target-distance logic — all C# | Never in YAML |
| **Behavior name** | Inspector ↔ YAML `behaviors:` key | Must match exactly |

The same `CollectObservations()` / `OnActionReceived()` contract runs unchanged
across training and inference — only the policy source differs (trainer socket
vs. baked ONNX).

---

## Pre-training state of the designated player

**It IS:**

- ✓ Full Y-Bot mesh visible in the scene
- ✓ `ArticulationBody` on every bone (setup complete)
- ✓ Colliders on every limb
- ✓ Mass distribution set per bone
- ✓ `YBotWalkerAgent.cs` attached (Behavior Type = **Heuristic Only**)
- ✓ `YBotJointController.cs` attached
- ✓ `YBotFootContact.cs` on both feet
- ✓ Decision Requester attached
- ✓ Behavior Parameters configured (name, obs size, action size)
- ✓ Stands at spawn position
- ✓ Affected by gravity naturally via `ArticulationBody`

**It is NOT:**

- ✗ No `Animator` component
- ✗ No Mixamo animation clips
- ✗ No Unity movement script driving it
- ✗ No RAG step listener
- ✗ No hardcoded position targets
- ✗ No ONNX model assigned yet
- ✗ No Python / `mlagents-learn` running

---

## What happens when you press Play (before training)

1. Y-Bot spawns upright.
2. Immediately falls over (gravity + no policy yet).
3. Episode ends (height below fall threshold).
4. Resets back to upright.
5. Falls again — loop.

**This falling loop is correct and expected.** It confirms the wiring:
`ArticulationBody` is live, episode reset works, rewards are firing. The agent
simply has no trained policy yet.

---

## Verification checklist

| Check | How to verify |
| --- | --- |
| `ArticulationBody` working | Y-Bot ragdolls naturally on Play |
| Episode reset working | Body snaps back upright after falling |
| Observations firing | No Console errors about obs-size mismatch |
| Rewards firing | `Debug.Log(GetCumulativeReward())` changes each step |
| Behavior Parameters correct | No Console warning about action/obs size |
| Foot contact working | `Debug.Log` in `YBotFootContact.cs` fires on ground collision |

---

## Designated player body — capability verification

Verified against the actual project (`feat/agent-locomotion`).

**Hierarchy:** `Player(Clone)` → `PlayerCapsule` → `Y Bot` (Mixamo rig nested in
`Resources/Player.prefab`). Siblings of `Y Bot` under `PlayerCapsule` —
`Head`, `Body`, `handmesh`, `handmesh (1)` — are the **legacy procedural body
parts**, not the skeleton. `DesignatedPhysicalPlayerAppearance.ApplyYBotVisual`
disables them (`SetActive(false)`) when the Y-Bot avatar is shown, which is why
they appear greyed in the hierarchy. The real articulated head/torso live
**inside** `Y Bot` as `mixamorig:Head` / `mixamorig:Spine*`.

| Question | Verdict | Detail |
| --- | --- | --- |
| Skeleton suitable for ArticulationBody? | ✅ Yes | `Y Bot.fbx` is a full standard Mixamo humanoid: `Hips` root, `Spine/Spine1/Spine2`, `Neck`, `Head`, both `UpLeg→Leg→Foot→ToeBase`, both `Shoulder→Arm→ForeArm→Hand`. |
| Can it learn walking / stability? | ✅ Yes (rig must still be built) | Structurally complete. Current branch drives the body with a single `CapsuleCollider` + kinematic `Rigidbody` + `AgentGroundMotor` — **no `ArticulationBody` exists yet**; the AB conversion is still to be implemented. |
| Finger movement supported? | ✅ Yes | Full finger rig both hands — `Thumb/Index/Middle/Ring/Pinky` × 4 segments (40 bones). Keep as separate manual/IK behavior (`HandRotationManager`), **not** articulated during walk training. |
| Head/Body outside Y Bot a problem? | ✅ No | They are disabled legacy visuals; the articulated head/torso are inside `Y Bot`. Confirm they stay inactive so they add no stray colliders. |

### Unit scale — handled via gate

ArticulationBody's reduced-coordinate solver **assumes unit scale**; the
designated player's 1.55× avatar scale corrupts joint anchors → solver
divergence (hips integrates to ~1e6, rig explodes through the floor).

**Fixed** in `DesignatedPhysicalPlayerAppearance` with a
`LocomotionRigActive` gate: when set, `GetScale()` returns `1f`, so the rig and
everything that reads scale (including `ResolveBodyHeight`) run at unit size.
The 1.55× look is preserved for every other player/scene. **The AB rig builder
must set `DesignatedPhysicalPlayerAppearance.LocomotionRigActive = true` before
adding any `ArticulationBody`, and clear it on teardown.**

### Other known AB gotchas (from prior locomotion work)

- Ignore self-collisions across non-adjacent bones (siblings overlap; AB only
  auto-ignores parent↔child).
- Cap joint drive stiffness ≤ 300, force limit ≤ 250, damping ≥ 0.25×stiffness —
  stiffer drives ram limbs through the floor faster than the solver resolves.
- Bump `Physics.defaultSolverIterations` = 40 / velocity iterations = 8.
- Sample ground height from `hit.collider.bounds.max.y` (top surface), not raw
  `hit.point.y` (can report the box's bottom face → feet spawn buried).
- Emit zero observations when physics state is non-finite (NaN/Inf obs make
  ML-Agents stop returning actions).

## One-line summary

Before training, the designated player is a **fully physics-enabled ragdoll
body that falls over repeatedly** — all scripts attached, all joints configured,
just waiting for a trained policy to tell it how to use those joints properly.
