# ML-Agents Training Setup - Complete Guide

## ✅ **ML-DRIVEN MOVEMENT (NOT HEURISTIC)**

**IMPORTANT**: The system uses **Default (0) Behavior Type** where the **Python ML model controls all agent movement**. This is NOT heuristic-based - it's true machine learning training.

---

## 🤖 **How ML-Agents Training Works**

### **1. Behavior Type: Default (0)**
- **ML Model Controls Movement**: The Python training server sends actions to agents via `OnActionReceived()`
- **Not Heuristic**: Agents do NOT use the `Heuristic()` method for movement
- **True ML Training**: The model learns from observations and rewards to make better decisions

### **2. Training Flow**

```
Python Server → Unity Environment → Agents
     ↓                  ↓             ↓
  Learns          Collects       Execute
  from            Observations    Actions
  Rewards         & Rewards       from ML
```

#### **Step-by-Step:**
1. **Python Server Starts**: `mlagents-learn config/worker_training_config.yaml --run-id=bsg_training`
2. **Unity Scene Loads**: Agents are created with `BehaviorType = Default (0)`
3. **ML-Agents Connects**: Python server connects to Unity (logs: "Connected to Unity environment")
4. **Observations Collected**: Agents send 30 observations to ML model:
   - Agent skills (5 obs)
   - Tool proximity (5 obs)
   - Agent state (5 obs)
   - Tool requirements (5 obs)
   - Skill comparisons (5 obs)
   - Pathfinding data (5 obs)
5. **ML Model Decides**: Python ML model processes observations and sends actions
6. **OnActionReceived() Called**: Unity receives actions from ML model
7. **Movement Applied**: Agents move based on ML decisions
8. **Rewards Calculated**: Agents get rewards/penalties for actions
9. **ML Model Learns**: Python server updates model based on rewards
10. **Repeat**: This loops continuously for training

### **3. Actions from ML Model**

The ML model sends 3 discrete actions to each agent:

```csharp
// Branch 0: Move (3 options)
0 = Stop
1 = Forward
2 = Backward

// Branch 1: Rotate (3 options)
0 = Left
1 = Straight
2 = Right

// Branch 2: Tool Action (3 options)
0 = None
1 = Positive (interact/learn)
2 = Negative/Neutral
```

### **4. Rewards Structure**

Agents receive rewards/penalties based on their actions:

#### **Pathfinding Rewards:**
- `+2.0`: Moving toward target (from `correct_step_reward` in JSON)
- `-1.0`: Moving away from target
- `+4.0`: Reaching target (from `target_reached_reward`)

#### **Skill Learning Rewards:**
- `+2.0`: Completing a "learn" action step
- Skill-specific rewards from JSON when learning new skills

#### **Other Rewards:**
- Small rewards for proper skill usage
- Penalties for boundary violations (if any)

---

## 📋 **Training Configuration**

### **Behavior Parameters (Auto-configured by MLAgentAttacher):**
```csharp
BehaviorType: Default (0)  // ML model controls movement
BehaviorName: "Technician1", "Technician2", etc.
VectorObservationSize: 30  // 30 observations sent to ML model
DiscreteActionBranches: [3, 3, 3]  // Move, Rotate, Tool Action
```

### **Decision Requester:**
```csharp
DecisionPeriod: 5  // ML makes decisions every 5 FixedUpdate steps
TakeActionsBetweenDecisions: true  // Repeat last action between decisions
```

---

## 🚀 **How to Train**

### **Step 1: Start Python Training Server**
```bash
cd /Users/mac/UnityProjects/BSG
source ml-agents-env/bin/activate
mlagents-learn config/worker_training_config.yaml --run-id=bsg_training --force
```

**You should see:**
```
[INFO] Listening on port 5004. Start training by pressing the Play button in the Unity Editor.
```

### **Step 2: Press Play in Unity**
- Agents will spawn at unique positions
- ML-Agents will connect to Python server

**You should see in Console:**
```
✅ [SIMPLE_Technician_01] Academy initialized - ML-Agents ready for training
🚀 [SIMPLE_Technician_01] Requested decision after sequence load - READY FOR ML TRAINING
📡 [SIMPLE_Technician_01] Waiting for ML-Agents server to send actions via OnActionReceived()
```

**You should see in Python Terminal:**
```
[INFO] Connected to Unity environment with package version 2.0.1
[INFO] Connected new brain: Technician1?team=0
[INFO] Connected new brain: Technician2?team=0
```

### **Step 3: Watch ML Training**
Every 30 steps, you'll see logs showing ML actions:

```
🤖 [SIMPLE_Technician_01] ML ACTION RECEIVED: move=1 (0=stop,1=fwd,2=back), rotate=1 (0=L,1=S,2=R), tool=0
```

This confirms the ML model is sending actions to agents!

### **Step 4: Monitor Training Progress**
In Python terminal, you'll see training stats:

```
[INFO] Technician1: Step: 1000. Mean Reward: 2.345
[INFO] Technician2: Step: 1000. Mean Reward: 1.987
```

Rewards should increase as the model learns to:
- Move toward targets
- Reach targets
- Complete action sequences
- Learn new skills

---

## 📊 **Output Files**

ML-Agents generates training data in several locations:

### **1. ML-Agents Results** (from Python training)
Location: `results/bsg_training/`
- `Technician1/` - Model checkpoints (.onnx files)
- `Technician2/` - Model checkpoints
- `Supervisor1/` - Model checkpoints
- `Supervisor2/` - Model checkpoints
- `run_logs/` - Training logs and tensorboard data

### **2. Unity-Generated Outputs** (from BSGMLAgent)
Location: `results/bsg_training/run_logs/`
- `basicUi_ml_output.json` - Agent progression (skills, completed steps)
- `ml_agents_training_results.json` - Comprehensive training results
- `agent_training_results.json` - Per-agent statistics
- `agent_training_results_summary.json` - Summary statistics

These files track:
- Agent skill progression (`skillLevel`, `availableSkills`)
- Step completion (`isStepCompleted`)
- Learned skills (`learnSkills`)
- Rewards earned
- Steps completed
- Time spent

---

## 🔍 **Troubleshooting**

### **Problem: Agents not moving**

**Check 1: ML-Agents Connection**
```
[INFO] Connected to Unity environment  ✅ GOOD
```
If you don't see this, the Python server isn't connected.

**Check 2: Behavior Type**
Look for in Console:
```
✅ [SIMPLE_Technician_01] Set Behavior Type to Default (0) - ML model will control movement
```
If you see "HeuristicOnly (2)", that's wrong - it should be "Default (0)".

**Check 3: OnActionReceived() Called**
Look for:
```
🤖 [SIMPLE_Technician_01] ML ACTION RECEIVED: move=1, rotate=1, tool=0
```
If you don't see this every 30 steps, the ML model isn't sending actions.

**Check 4: Sequence Loaded**
Look for:
```
✅ [SIMPLE_Technician_01] Loaded sequence with 24 steps
🚀 [SIMPLE_Technician_01] READY FOR ML TRAINING
```

### **Problem: Python server timeout**
```
The Unity environment took too long to respond.
```

**Solution**: Make sure Unity scene is running when Python server is waiting for connection.

### **Problem: No observations/actions**
**Solution**: Check that `BehaviorParameters` has:
- VectorObservationSize: 30
- DiscreteActionBranches: [3, 3, 3]

---

## ✅ **Confirmation Checklist**

- [ ] Python training server running (`mlagents-learn`)
- [ ] Unity scene playing
- [ ] Console shows "Connected to Unity environment"
- [ ] Console shows "Set Behavior Type to Default (0)"
- [ ] Console shows "READY FOR ML TRAINING"
- [ ] Console shows "ML ACTION RECEIVED" every 30 steps
- [ ] Python terminal shows "Connected new brain: Technician1"
- [ ] Python terminal shows training step updates
- [ ] Agents are moving in the scene
- [ ] Output files are being generated

---

## 📈 **Expected Behavior**

With ML-Agents training active:
1. **Initial Learning**: Agents move randomly at first (ML model is learning)
2. **Gradual Improvement**: After ~1000 steps, agents start moving toward targets
3. **Convergence**: After ~5000-10000 steps, agents consistently reach targets
4. **Skill Learning**: Agents learn to interact with tools and complete sequences

The ML model learns through trial and error, improving with each episode!

---

## 🎯 **Key Difference: ML-Driven vs Heuristic**

### **❌ OLD (Heuristic - NOT USED)**:
- Behavior Type: HeuristicOnly (2)
- Movement: Controlled by `Heuristic()` method in Unity C# code
- NO machine learning - just scripted behavior

### **✅ NEW (ML-Driven - CURRENT)**:
- Behavior Type: Default (0)
- Movement: Controlled by Python ML model via `OnActionReceived()`
- TRUE machine learning - model learns from experience
- Generates ML training outputs required by product owner

---

## 🔥 **IMPORTANT FOR PRODUCT OWNER**

This system uses **real machine learning** where:
1. Python ML model makes all movement decisions
2. Model learns from rewards/penalties
3. Model improves over time through training
4. ML-Agents generates comprehensive training outputs
5. All actions are ML-driven, not scripted

This satisfies the requirement for ML-driven sequence execution and output generation.

