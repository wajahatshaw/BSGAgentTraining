# ML-Agent Setup Guide for Runtime Agents

## Overview
This guide explains how to connect ML-Agents to your runtime-created agents (Technicians and Supervisors).

## ✅ What's Been Implemented

### 1. **MLAgentAttacher.cs**
- Static utility that automatically attaches ML-Agents components to runtime agents
- Configures: Behavior Parameters, Decision Requester, and BSGMLAgent script
- Maps agent IDs to behavior names from YAML config

### 2. **BSGMLAgent.cs**
- Custom ML-Agent script for training
- Handles movement, observations, and rewards
- Integrates with your existing system

### 3. **SimpleFourEntitySystem.cs** (Updated)
- Now automatically attaches ML-Agents when creating entities
- Loads skill and desire levels from JSON structure
- Maps each agent to its corresponding behavior

## 🎯 How It Works

### Agent → Behavior Mapping:
- **SIMPLE_Technician_01** → `Technician1` (YAML config)
- **SIMPLE_Technician_02** → `Technician2` (YAML config)
- **SIMPLE_Supervisor_01** → `Supervisor1` (YAML config)
- **SIMPLE_Supervisor_02** → `Supervisor2` (YAML config)

### Skill Levels (from basicUi.json):
- **SIMPLE_Technician_01**: skillLevel=40, desireLevel=100
- **SIMPLE_Technician_02**: skillLevel=40, desireLevel=80
- **SIMPLE_Supervisor_01**: skillLevel=10, desireLevel=90
- **SIMPLE_Supervisor_02**: skillLevel=30, desireLevel=100

## 🚀 Running ML Training

### Step 1: Start Training Server
```bash
cd /Users/mac/UnityProjects/BSG
source ml-agents-env/bin/activate
python3.9 -m mlagents.trainers.learn config/worker_training_config.yaml --run-id=bsg_training_new --force --no-graphics
```

### Step 2: In Unity Editor
1. Build your scene as a server build
2. Or press Play while the training server is running
3. ML-Agents components will be attached automatically at runtime

## 📊 Observations Space (15 values)
1. Agent skill level (normalized 0-1)
2. Agent desire level (normalized 0-1)
3-5. Has specific skills (repair, inspect, safety) - binary
6-10. Distance to 5 tools (normalized by 20 units)
11-13. Agent position (X, Z normalized)
14. Agent rotation (Y normalized to 360°)
15. Distance from spawn (normalized to 20 units)

## 🎮 Action Space
- **Branch 0**: Move (0=stop, 1=forward, 2=backward)
- **Branch 1**: Rotate (0=left, 1=straight, 2=right)

## 🔧 Customization

### To Change Movement Speed:
Edit `BSGMLAgent.cs`:
```csharp
public float moveSpeed = 2f;  // Adjust this value
```

### To Adjust Skill Levels:
Edit in `SimpleFourEntitySystem.cs` lines 585-604

### To Change Observations:
Edit `BSGMLAgent.cs` → `CollectObservations()` method

## 🐛 Troubleshooting

### Issue: Agents moving too fast
- ✅ Fixed: `time_scale: 1.0` in YAML
- ✅ Fixed: Lower moveSpeed in BSGMLAgent

### Issue: YAML parsing errors
- ✅ Fixed: Removed typos (`buffer_size:plies:`, `0. Britannica`)

### Issue: Agents not connecting
1. Check console for ML-Agents connection logs
2. Verify agent names match: `SIMPLE_Technician_01`, etc.
3. Verify Behavior Parameters are attached in Inspector

## 📝 Next Steps

1. **Add Reward System**: Implement tool interaction rewards
2. **Add Task Logic**: Connect to JSON task sequences
3. **Add Skill Validation**: Check agent skills before tasks
4. **Monitor Training**: Use TensorBoard to view progress

