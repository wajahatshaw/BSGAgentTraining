# Updated Setup Guide: New Entities & Separate UI

This guide explains how to set up the **completely separate scene generation system** that creates new entities and UI panels without affecting your existing scene objects.

## 🎯 **What This System Does Now:**

1. **Creates NEW entities** - Doesn't modify your existing orange cube, teal capsules, etc.
2. **Generates separate UI panels** - Creates new UI specifically for JSON data
3. **Positions everything separately** - New objects are offset from your existing scene
4. **Completely independent** - Your current scene remains untouched

## 📁 **Scripts to Use:**

1. **`SceneUILoader.cs`** - Reads JSON and manages workflow data
2. **`SceneGenerator.cs`** - Creates NEW physical scene objects (offset from existing)
3. **`JSONUIGenerator.cs`** - Creates NEW UI panels for JSON data
4. **`ToolComponent.cs`** - Manages new tool behavior
5. **`AgentComponent.cs`** - Manages new agent behavior

## 🚀 **Quick Setup (5 Minutes):**

### Step 1: Create Scene Manager GameObject

1. In your Unity scene, create an empty GameObject
2. Name it **"JSONSceneManager"**
3. Attach **ALL THREE** scripts to it:
   - `SceneUILoader`
   - `SceneGenerator` 
   - `JSONUIGenerator`

### Step 2: Configure Scene Generator

In the `SceneGenerator` component:

- ✅ **Generate On Start**: Check this
- ❌ **Clear Existing Scene**: **UNCHECK this** (important!)
- **Scene Layout**: Defaults work fine
- **Prefab References**: Leave empty (uses primitives)

### Step 3: Configure JSON UI Generator

In the `JSONUIGenerator` component:

- ✅ **Generate UI On Start**: Check this
- **Target Canvas**: Leave empty (will create new canvas)
- **UI Layout**: Adjust positions if needed

### Step 4: Run the Scene

1. **Play the scene**
2. **Watch the magic happen!** 

## 🆕 **What Gets Created (Separate from Existing):**

### **New Physical Objects (Offset 10 units to the right):**
- **JSON_Generated_Tools** folder containing:
  - `Tool_tool_001`: Red cube (power off) at position (7, 0.5, -3)
  - `Tool_tool_002`: Yellow cube (locked) at position (10, 0.5, -3)
- **JSON_Generated_Agents** folder containing:
  - `Agent_agent_technician_A`: Capsule with skill-based coloring
- **JSON_Generated_Environment** folder containing:
  - New floor, walls (cyan color to distinguish from existing blue)

### **New UI Panels (Separate Canvas):**
- **JSON_Worker_Panel** (top-left): Shows agent information
- **JSON_Task_Panel** (top-right): Shows current task
- **JSON_Details_Panel** (center-bottom): Shows detailed step info
- **JSON_Progress_Panel** (center-top): Shows workflow progress

## 🔍 **Scene Layout:**

```
Your Existing Scene (Left Side):
├── Orange Cube
├── Teal Capsules  
├── Red Hemisphere
├── Blue Cuboid
└── Existing UI panels

NEW JSON Scene (Right Side - 10 units offset):
├── tool_001 (red cube, power off)
├── tool_002 (yellow cube, locked)
├── agent_technician_A (capsule)
├── New floor and walls
└── New UI panels
```

## 🎮 **How to Use:**

### **Automatic Generation:**
- **Scene generates automatically** when you press Play
- **All NEW objects positioned** to the right of your existing scene
- **NEW UI panels appear** with JSON workflow data

### **Navigation:**
- **Arrow keys** to navigate between workflow steps
- **Space** to execute current step
- **Click on new tools** to interact
- **Click on new agents** to see skills

### **Expected Results:**
- **Worker Panel**: "agent_technician_A"
- **Task Panel**: "step_001 - repair_motor on tool_001"
- **Details Panel**: Skills, duration, requirements
- **Progress Panel**: "Step 1 of 2"

## 🔧 **Customization Options:**

### **Change Object Positions:**
In `SceneGenerator.cs`, modify these values:
```csharp
// Offset from existing scene (currently 10f to the right)
col * toolSpacing - toolSpacing + 10f

// Agent positioning offset
Mathf.Cos(angle * Mathf.Deg2Rad) * 8f + 10f
```

### **Change UI Panel Positions:**
In `JSONUIGenerator.cs`, modify:
```csharp
public Vector2 workerPanelPosition = new Vector2(-400, 200);
public Vector2 taskPanelPosition = new Vector2(400, 200);
public Vector2 detailsPanelPosition = new Vector2(0, -200);
public Vector2 progressPanelPosition = new Vector2(0, 100);
```

### **Change Colors:**
- **Tools**: Modify colors in `ToolComponent.cs`
- **Agents**: Modify colors in `AgentComponent.cs`
- **Walls**: Change from cyan to any color in `SceneGenerator.cs`

## 🐛 **Troubleshooting:**

### **New Objects Not Visible:**
- ✅ Check Console for generation messages
- ✅ Look to the right of your existing scene (10 units offset)
- ✅ Ensure all three scripts are attached to JSONSceneManager

### **New UI Not Appearing:**
- ✅ Check if new Canvas was created (look for "JSON_Generated_Canvas")
- ✅ Verify JSON file is loading (check Console)
- ✅ Ensure JSONUIGenerator script is attached

### **Conflicts with Existing Objects:**
- ✅ New objects use unique names (JSON_Generated_*)
- ✅ New objects are positioned separately
- ✅ New UI uses separate Canvas

## 🎉 **Benefits of This Approach:**

1. **No interference** with your existing scene
2. **Easy to remove** - just delete the JSONSceneManager
3. **Completely separate** - your current work is preserved
4. **Easy to test** - JSON changes don't affect existing objects
5. **Production ready** - can be easily integrated or removed

## 🚀 **Next Steps:**

1. **Test the system** with your current JSON
2. **Customize positions** if needed
3. **Add custom prefabs** for more realistic tools/agents
4. **Extend the workflow** with more complex JSON data
5. **Integrate with your existing systems** when ready

Your existing scene will remain completely untouched while the new JSON-driven system creates a parallel world to the right! 🎯
