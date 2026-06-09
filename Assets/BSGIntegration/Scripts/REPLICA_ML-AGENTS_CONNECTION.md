# Replica Scene ML-Agents Connection - Complete Guide

## ✅ **AUTOMATIC CONNECTION CONFIRMED**

**YES, the replica scene will automatically connect to ML-Agents training server** just like the original scene!

---

## 🔗 **How It Works**

### **1. Runtime Agent Creation**
When `ReplicaSceneSetup` creates agents at runtime:
- `SIMPLE_Technician_01` → Automatically gets `MLAgentAttacher.AttachMLAgentComponents()` called
- `SIMPLE_Technician_02` → Automatically gets `MLAgentAttacher.AttachMLAgentComponents()` called
- `SIMPLE_Supervisor_01` → Automatically gets `MLAgentAttacher.AttachMLAgentComponents()` called
- `SIMPLE_Supervisor_02` → Automatically gets `MLAgentAttacher.AttachMLAgentComponents()` called

### **2. Behavior Name Mapping**
`MLAgentAttacher.GetBehaviorNameForAgent()` automatically maps:
- `SIMPLE_Technician_01` → `"Technician1"` (matches YAML config)
- `SIMPLE_Technician_02` → `"Technician2"` (matches YAML config)
- `SIMPLE_Supervisor_01` → `"Supervisor1"` (matches YAML config)
- `SIMPLE_Supervisor_02` → `"Supervisor2"` (matches YAML config)

### **3. ML-Agents Components Added Automatically**
For each agent, `MLAgentAttacher.AttachMLAgentComponents()` adds:
- ✅ `BehaviorParameters` component with correct behavior name
- ✅ `DecisionRequester` component
- ✅ `BSGMLAgent` component with agent ID and behavior name

### **4. Automatic Connection to Training Server**
When you:
1. Start ML-Agents training: `python3.9 -m mlagents.trainers.learn config/worker_training_config.yaml --run-id=bsg_training`
2. Press Play in Unity (replica scene)
3. **Agents automatically connect** and you'll see in Console/Terminal:

```
[INFO] Connected new brain: Technician1?team=0
[INFO] Connected new brain: Technician2?team=0
[INFO] Connected new brain: Supervisor1?team=0
[INFO] Connected new brain: Supervisor2?team=0
[INFO] Technician1: Step: 1. Mean Reward: 0.000
[INFO] Technician2: Step: 1. Mean Reward: 0.000
[INFO] Supervisor1: Step: 1. Mean Reward: 0.000
[INFO] Supervisor2: Step: 1. Mean Reward: 0.000
```

---

## 📋 **Connection Status Verification**

### **In Unity Console:**
You should see:
```
🔗 Attaching ML-Agents to SIMPLE_Technician_01 with behavior: Technician1
✅ ML-Agents attached to SIMPLE_Technician_01 (ID: SIMPLE_Technician_01, Behavior: Technician1)
✅ Configured Behavior Parameters for Technician1: Type=Default, VectorObs=15, Discrete Actions=[3,3]
🔗 Attaching ML-Agents to SIMPLE_Technician_02 with behavior: Technician2
✅ ML-Agents attached to SIMPLE_Technician_02 (ID: SIMPLE_Technician_02, Behavior: Technician2)
... (similar for supervisors)
```

### **In ML-Agents Training Terminal:**
You should see connection messages:
```
[INFO] Connected new brain: Technician1?team=0
[INFO] Connected new brain: Technician2?team=0
[INFO] Connected new brain: Supervisor1?team=0
[INFO] Connected new brain: Supervisor2?team=0
```

### **In Unity Inspector (during Play):**
1. Select any agent (e.g., `SIMPLE_Technician_01`)
2. Check `Behavior Parameters` component:
   - **Behavior Name**: Should show `Technician1` (not `BSGAgentBehavior`)
   - **Behavior Type**: Should be `Default` (for training)
   - **Vector Observation Size**: 15
   - **Discrete Actions**: [3, 3]

---

## 🎯 **Answer to Your Questions**

### **Q1: Will it show "connected" status for all agents?**
✅ **YES!** All 4 agents (2 technicians + 2 supervisors) will automatically connect and show connected status in the ML-Agents training terminal.

### **Q2: Does it automatically connect with runtime entities?**
✅ **YES!** The `ReplicaSceneSetup` script uses `MLAgentAttacher.AttachMLAgentComponents()` which:
- Automatically finds runtime-created agents
- Attaches ML-Agents components with correct behavior names
- Configures Behavior Parameters properly
- Ensures agents connect to training server automatically

---

## 🚀 **Step-by-Step Connection Process**

### **1. Start ML-Agents Training Server:**
```bash
cd /Users/mac/UnityProjects/BSG
source ml-agents-env/bin/activate
python3.9 -m mlagents.trainers.learn config/worker_training_config.yaml --run-id=bsg_training --force
```

### **2. In Unity:**
1. Open `JSONWorkflowSceneReplica` scene
2. Press **Play**
3. `ReplicaSceneSetup` creates agents at runtime
4. `MLAgentAttacher` automatically attaches ML-Agents components
5. Agents connect to training server automatically

### **3. Verify Connection:**
- **Unity Console**: Shows agent attachment messages
- **Training Terminal**: Shows "Connected new brain" messages
- **Unity Inspector**: Behavior Parameters show correct behavior names

---

## 🔧 **What Makes It Automatic**

### **Key Code in ReplicaSceneSetup.cs:**
```csharp
// For each agent, this is called automatically:
string behaviorName = MLAgentAttacher.GetBehaviorNameForAgent("SIMPLE_Technician_01");
MLAgentAttacher.AttachMLAgentComponents(tech01, behaviorName, "SIMPLE_Technician_01");
```

This:
1. Gets correct behavior name from agent ID
2. Attaches all ML-Agents components
3. Configures Behavior Parameters
4. Sets up for automatic connection

---

## ✅ **Summary**

**YES, it's fully automatic:**
- ✅ Runtime agents get ML-Agents components automatically
- ✅ Behavior names match YAML config (Technician1, Technician2, Supervisor1, Supervisor2)
- ✅ All 4 agents connect automatically when training server is running
- ✅ Connection status appears in training terminal
- ✅ Works exactly like the original scene

**You don't need to manually configure anything** - just:
1. Start training server
2. Press Play in Unity
3. Agents connect automatically!

---

**Last Updated**: After fixing ML-Agents behavior name mapping  
**Status**: ✅ Fully Automatic Connection

