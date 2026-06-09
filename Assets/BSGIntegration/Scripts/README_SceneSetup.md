# Scene UI Setup Guide

This guide explains how to integrate the new JSON-driven UI system with your existing scene.

## Files Created

1. **SceneUILoader.cs** - Main script that reads the JSON and updates UI
2. **UIInputController.cs** - Handles user input and button interactions

## Setup Instructions

### Step 1: Add Scripts to Scene

1. Create an empty GameObject in your scene (name it "SceneUIManager")
2. Attach both `SceneUILoader.cs` and `UIInputController.cs` scripts to this GameObject

### Step 2: Connect UI References

In the SceneUILoader component, assign the following UI Text elements:

- **Worker Title Text**: The "Worker: <TITLE>" text element
- **Current Task Text**: The main task description text area
- **Task Details Text**: A new text element for detailed step information (create if needed)
- **Step Progress Text**: A text element showing "Step X of Y" (create if needed)

### Step 3: Create Additional UI Elements (Optional)

For better user experience, consider adding:

1. **Navigation Buttons**:
   - Previous Step Button (←)
   - Next Step Button (→)
   - Execute Step Button (Space)

2. **Task Details Panel**:
   - Create a new UI Panel with detailed information about the current step
   - Include skill requirements, duration, and other metadata

### Step 4: JSON File Location

The script will automatically look for `basicUi.json` in:
1. `Assets/StreamingAssets/` (preferred)
2. `Assets/JsonFile/` (fallback)

### Step 5: Test the System

1. Play the scene
2. Check the Console for JSON loading messages
3. Use arrow keys to navigate between steps
4. Press Space to "execute" the current step

## Keyboard Controls

- **Left Arrow**: Previous step
- **Right Arrow**: Next step  
- **Space**: Execute current step

## Expected Behavior

Based on your `basicUi.json`, the UI should display:

**Step 1**: 
- Worker: agent_technician_A
- Task: step_001 - repair_motor on tool_001
- Skills: Repairing (Level 4.5) [CRITICAL], Equipment Maintenance (Level 4.0) [CRITICAL]

**Step 2**:
- Worker: agent_technician_A  
- Task: step_002 - unlock_toolbox on tool_002
- Skills: Operation Monitoring (Level 3.0)

## Troubleshooting

- **JSON not loading**: Check Console for error messages
- **UI not updating**: Verify Text components are assigned in the inspector
- **Scripts not working**: Ensure both scripts are attached to the same GameObject

## Next Steps

Once basic functionality is working, consider adding:
- Visual feedback for step completion
- Tool state visualization (power on/off, locked/unlocked)
- Agent skill level indicators
- Step dependency visualization
- Progress tracking and persistence
