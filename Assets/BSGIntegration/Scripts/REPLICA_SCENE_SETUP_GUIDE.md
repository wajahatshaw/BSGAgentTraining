# Replica Scene Setup Guide

This guide explains how to set up the **JSONWorkflowSceneReplica** - a complete replica of JSONWorkflowScene with all action logic, rewards/penalties, and visual feedback systems.

## 🎯 Overview

The replica scene contains:
- ✅ Same action logic (Positive/Negative/Neutral actions)
- ✅ Same reward/penalty system
- ✅ Same visual feedback (color changes, labels)
- ✅ Same skill-based learning system
- ✅ **Completely independent** - doesn't affect the original scene

## 📋 Phase-by-Phase Setup

### Phase 1: Create New Scene

1. **File > New Scene** (or Ctrl+N / Cmd+N)
2. Choose **"Basic (Built-in)"** template
3. **Save the scene** as `JSONWorkflowSceneReplica` in `Assets/Scenes/`

### Phase 2: Add Core Components

1. **Create empty GameObject** named `REPLICA_SceneManager`
2. **Attach these scripts** to `REPLICA_SceneManager`:
   - `ReplicaSceneSetup` (NEW - handles scene setup)
   - `SceneUILoader` (loads JSON data)
   - `SkillBasedActionSystem` (determines action types)
   
3. **Configure SceneUILoader**:
   - Set `jsonFileName` to `basicUi.json`

4. **Configure SkillBasedActionSystem**:
   - Set `skillPenaltyAmount` to `10.0` (0-100 scale)

### Phase 3: Auto-Setup (Automatic)

The `ReplicaSceneSetup` script will automatically:
- ✅ Create all tools/workbenches from JSON
- ✅ Create all agents with full functionality
- ✅ Setup AgentProximity components for action detection
- ✅ Setup SimpleEntityMovement for agent movement
- ✅ Setup BSGMLAgent for ML-Agents integration
- ✅ Setup SimpleStatusBoard for status display

**Just press Play** - everything is created automatically!

### Phase 4: Manual Setup (If Needed)

If auto-setup doesn't work, manually create:

#### Status Board
1. Create empty GameObject named `REPLICA_SimpleStatusBoard`
2. Attach `SimpleStatusBoard` component
3. Configure position (default: Vector3(0, 8, 0))

#### Proximity Detection (Optional)
1. Create empty GameObject named `REPLICA_ProximityDetectionSystem`
2. Attach `ProximityDetectionSystem` component
3. **Important**: Call `DisableDirectionEffects()` to avoid movement disruption //-----------------------------------------

## 🔧 How It Works

### Action Flow

1. **Agent Movement**: Agents move randomly using `SimpleEntityMovement`
2. **Proximity Detection**: When agent approaches tool/workbench, `AgentProximity` detects it
3. **Action Determination**: `SkillBasedActionSystem.DetermineAction()` checks:
   - Agent skill level vs tool required level
   - Returns: Positive (learn), Negative (penalty), or Neutral (proficient)
4. **Visual Feedback**: 
   - **Positive**: White color, skill increases
   - **Negative**: Black color, skill decreases (penalty)
   - **Neutral**: Gray color, no change
5. **Status Display**: `SimpleStatusBoard` shows agent status

### Color System

- **Default (No Action)**: Green (Spring Green for Technicians, Royal Blue for Supervisors)
- **Positive Action**: White (agent learns new skill)
- **Negative Action**: Black (insufficient skill, penalty applied)
- **Neutral Action**: Gray (already proficient)

### Reward/Penalty System

- **Positive Actions**: Increase skill level by reward amount (from JSON)
- **Negative Actions**: Decrease skill level by penalty amount (default: 10.0)
- **Neutral Actions**: No change to skill level
- **ML-Agents**: Get rewards/penalties via `BSGMLAgent.AddTaskReward()`

## 🎮 Testing

### Test Scene Isolation

1. **Open JSONWorkflowScene** - verify it works normally
2. **Open JSONWorkflowSceneReplica** - verify it works independently
3. **Both scenes should work** without affecting each other

### Test Actions

1. **Run the scene**
2. **Watch agents move** around the scene
3. **Wait for agents to approach tools** (yellow highlight spheres)
4. **Observe color changes**:
   - White = Positive (learning)
   - Black = Negative (penalty)
   - Gray = Neutral (proficient)
5. **Check status board** (top of scene) for agent status

### Context Menu Tests

Right-click on `REPLICA_SceneManager` in Inspector:
- **"Force Setup Scene"** - Re-setup the scene
- **"Clear Created Objects"** - Clear all created objects

## 🔍 Troubleshooting

### Agents Not Moving
- Check `SimpleEntityMovement` component is attached
- Check `Rigidbody` component exists (should be auto-added)
- Check `Rigidbody.useGravity` is false
- Check `Rigidbody.isKinematic` is false

### Actions Not Triggering
- Check `AgentProximity` component is attached to agents
- Check `agentID` is set correctly (e.g., "SIMPLE_Technician_01")
- Check `detectionRadius` is set (default: 3f)
- Check tools are on correct layer (Default)

### Colors Not Changing
- Check `AgentProximity.ApplyActionToAgent()` is being called
- Check `SkillBasedActionSystem` is loaded and ready
- Check agent has `Renderer` component

### Status Board Not Showing
- Check `SimpleStatusBoard` component exists
- Check `SkillBasedActionSystem` is found and ready
- Check status board position (default: Vector3(0, 8, 0))

## 📊 Key Differences from Original Scene

| Feature | JSONWorkflowScene | JSONWorkflowSceneReplica |
|---------|------------------|-------------------------|
| Scene Name | JSONWorkflowScene | JSONWorkflowSceneReplica |
| GameObject Prefix | JSON_ | REPLICA_ |
| Setup Script | Multiple scripts | Single `ReplicaSceneSetup` |
| Dependencies | Shared components | Same components, isolated |

## ✅ Verification Checklist

- [ ] Scene created and saved
- [ ] `REPLICA_SceneManager` GameObject created
- [ ] `ReplicaSceneSetup` component attached
- [ ] `SceneUILoader` component attached
- [ ] `SkillBasedActionSystem` component attached
- [ ] Press Play - scene generates automatically
- [ ] Agents are created and moving
- [ ] Tools/workbenches are created
- [ ] Actions trigger when agents approach tools
- [ ] Colors change correctly (White/Black/Gray)
- [ ] Status board displays agent status
- [ ] Original scene still works independently

## 🎉 Success Criteria

The replica scene is working correctly when:
1. ✅ All agents spawn and move around
2. ✅ Agents approach tools/workbenches
3. ✅ Actions trigger (Positive/Negative/Neutral)
4. ✅ Colors change correctly based on action type
5. ✅ Status board shows agent status
6. ✅ Original scene is unaffected

## 📝 Notes

- **Scene Isolation**: Unity scenes are completely separate - objects in one scene don't affect the other
- **Shared Scripts**: Both scenes use the same scripts (SceneUILoader, SkillBasedActionSystem, etc.) - this is fine as scripts are shared
- **Naming Convention**: Using "REPLICA_" prefix for clarity, but not required for isolation
- **JSON File**: Both scenes use the same JSON file (`basicUi.json`) - this is fine as it's read-only

## 🚀 Next Steps

Once the replica scene is working:
1. Test both scenes independently
2. Verify actions work correctly in both
3. Test ML-Agents training in replica scene
4. Customize replica scene as needed

---

**Created by**: Replica Scene Setup System  
**Version**: 1.0  
**Last Updated**: 2025-01-XX

