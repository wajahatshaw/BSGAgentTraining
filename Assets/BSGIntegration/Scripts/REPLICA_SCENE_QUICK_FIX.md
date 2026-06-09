# Replica Scene Quick Fix Guide

## ✅ Issues Fixed

### Problem: Empty Scene
**Symptoms**: No agents, tools, workbenches, or ground visible when playing the scene.

**Root Cause**: The script was trying to create entities before JSON data finished loading.

**Solution**: 
- Changed setup to use coroutines that wait for JSON data
- Added ground plane creation
- Added better error handling and retry logic

## 🔧 What Changed

1. **Ground Plane**: Now automatically creates a dark gray ground plane (30x30 units)
2. **JSON Loading**: Now properly waits for SceneUILoader to finish loading JSON data
3. **Error Handling**: Better logging and retry logic if JSON fails to load

## 🎮 How to Test

1. **Stop Play Mode** (if running)
2. **Press Play** again
3. **Watch Console** for these messages:
   - `🔄 REPLICA SCENE SETUP: Starting initialization...`
   - `🔄 REPLICA SCENE SETUP: Creating ground plane...`
   - `✅ Created ground plane`
   - `🔄 REPLICA SCENE SETUP: Loading JSON data...`
   - `✅ REPLICA SCENE SETUP: JSON data loaded`
   - `✅ REPLICA SCENE SETUP: Created X tools, Y agents`

4. **Check Scene View** - You should see:
   - Dark gray ground plane
   - Colored tool cubes (red, yellow, blue, green, orange)
   - Colored agent capsules (green technicians, blue supervisors)

## 🐛 If Still Empty

### Check Console for Errors

**Error: "Failed to load JSON data"**
- Solution: Verify `basicUi.json` exists in `Assets/JsonFile/` folder
- Check JSON file path in SceneUILoader component

**Error: "SceneUILoader not found"**
- Solution: Script will create one automatically, but you can manually add it
- In Inspector, add `SceneUILoader` component to `REPLICA_SceneManager`

**Error: "No tool data in scene data"**
- Solution: JSON file might be corrupted or empty
- Check that `basicUi.json` has proper structure with `initialStates` and `agentProfiles`

### Manual Steps

1. **Check JSON File**:
   - Open `Assets/JsonFile/basicUi.json`
   - Verify it has `initialStates` and `agentProfiles` sections

2. **Check SceneUILoader**:
   - Select `REPLICA_SceneManager` in Hierarchy
   - In Inspector, check `Scene UI Loader` component
   - Verify `jsonFileName` is set to `basicUi.json`

3. **Force Re-setup**:
   - Right-click `REPLICA_SceneManager` in Hierarchy
   - Select `Force Setup Scene` from context menu
   - Or press Play again

4. **Clear and Recreate**:
   - Right-click `REPLICA_SceneManager` in Hierarchy
   - Select `Clear Created Objects` from context menu
   - Press Play again

## ✅ Expected Results

When working correctly, you should see:

**Ground**: Dark gray plane (30x30 units)

**Tools (5 total)**:
- Red cube: `REPLICA_Tool_tool_001`
- Yellow cube: `REPLICA_Tool_tool_002`
- Blue cube: `REPLICA_Tool_tool_003`
- Green cube: `REPLICA_Tool_tool_004`
- Orange cube: `REPLICA_Tool_workbench_001`

**Agents (4 total)**:
- Green capsule: `REPLICA_Agent_SIMPLE_Technician_01`
- Green capsule: `REPLICA_Agent_SIMPLE_Technician_02`
- Blue capsule: `REPLICA_Agent_SIMPLE_Supervisor_01`
- Blue capsule: `REPLICA_Agent_SIMPLE_Supervisor_02`

**Status Board**: TextMesh at position (0, 8, 0) showing agent status

## 📊 Console Output Example (Success)

```
🔄 REPLICA SCENE SETUP: Starting initialization...
🔄 REPLICA SCENE SETUP: Setting up complete scene...
🔄 REPLICA SCENE SETUP: Creating ground plane...
✅ Created ground plane
🔄 REPLICA SCENE SETUP: Ensuring core systems...
✅ Core systems ensured
🔄 REPLICA SCENE SETUP: Loading JSON data...
✅ REPLICA SCENE SETUP: JSON data loaded - Scene: warehouse_scene_001
   Agents: 4, Tools: 5
🔄 REPLICA SCENE SETUP: Creating tools and workbenches...
✅ Created tool: REPLICA_Tool_tool_001 at (-4, 0.5, -4)...
✅ Created tool: REPLICA_Tool_tool_002 at (0, 0.5, -4)...
... (more tools)
✅ REPLICA SCENE SETUP: Created 5 tools/workbenches
🔄 REPLICA SCENE SETUP: Creating agents with functionality...
✅ Created agent: REPLICA_Agent_SIMPLE_Technician_01...
... (more agents)
✅ REPLICA SCENE SETUP: Created 4 agents
✅ REPLICA SCENE SETUP: Scene setup complete!
   Created: 5 tools, 4 agents
```

## 🎯 Next Steps

Once scene is populated:
1. ✅ Agents should start moving (using SimpleEntityMovement)
2. ✅ When agents approach tools, actions should trigger
3. ✅ Agent colors should change (White/Black/Gray based on actions)
4. ✅ Status board should display agent skill levels

---

**Last Updated**: After fixing empty scene issue  
**Version**: 1.1

