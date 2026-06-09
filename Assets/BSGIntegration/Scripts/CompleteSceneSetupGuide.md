# Complete Automated Scene Generation Setup Guide

This guide explains how to set up a **fully automated Unity scene generation system** that reads your `basicUi.json` file and creates the entire warehouse scene dynamically.

## 🎯 **What This System Does**

1. **Reads your JSON**: Automatically loads `basicUi.json` 
2. **Generates the entire scene**: Creates tools, agents, environment, and UI
3. **Updates dynamically**: Scene changes based on JSON data
4. **Fully automated**: No manual scene setup required

## 📁 **Scripts Created**

1. **`SceneUILoader.cs`** - Reads JSON and manages workflow data
2. **`SceneGenerator.cs`** - Creates the physical scene objects
3. **`ToolComponent.cs`** - Manages individual tool behavior and state
4. **`AgentComponent.cs`** - Manages agent skills and behavior
5. **`UIInputController.cs`** - Handles user input and navigation

## 🚀 **Quick Setup (5 Minutes)**

### Step 1: Create Scene Manager GameObject

1. In your Unity scene, create an empty GameObject
2. Name it **"SceneManager"**
3. Attach **ALL THREE** scripts to it:
   - `SceneUILoader`
   - `SceneGenerator` 
   - `UIInputController`

### Step 2: Configure Scene Generator

In the `SceneGenerator` component:

- ✅ **Generate On Start**: Check this
- ✅ **Clear Existing Scene**: Check this
- **Scene Layout**: Adjust as needed (defaults work fine)
- **Prefab References**: Leave empty for now (uses primitives)

### Step 3: Run the Scene

1. **Play the scene**
2. **Watch the magic happen!** 

The system will automatically:
- Load your JSON
- Create the warehouse environment
- Generate tools with proper states
- Create agents with skill levels
- Set up lighting and walls
- Update all UI elements

## 🔧 **Advanced Configuration**

### Custom Prefabs (Optional)

If you want to use custom models instead of primitives:

1. **Create prefabs** for tools, agents, walls, floor
2. **Assign them** in the SceneGenerator component
3. **The system will use your custom models** instead of primitives

### Scene Layout Customization

Adjust these values in SceneGenerator:

```csharp
Scene Center: Vector3(0, 0, 0)     // Center of the scene
Tool Spacing: 3.0f                  // Distance between tools
Wall Height: 5.0f                   // Height of boundary walls
Floor Size: 20.0f                   // Size of the warehouse floor
```

## 🎮 **How to Use**

### Automatic Generation
- **Scene generates automatically** when you press Play
- **All objects positioned** based on JSON data
- **Visual feedback** shows tool states and agent skills

### Manual Control
- **Regenerate Scene**: Call `sceneGenerator.RegenerateScene()`
- **Update Tool State**: Call `sceneGenerator.UpdateToolState(toolId, newState)`
- **Get Objects**: Use `sceneGenerator.GetTool(toolId)` or `GetAgent(agentId)`

### Interactive Elements
- **Click on tools** to interact (toggle power, etc.)
- **Click on agents** to see skill information
- **Use arrow keys** to navigate workflow steps
- **Press Space** to execute current step

## 📊 **What Gets Generated**

### Tools (from `initialStates`)
- **tool_001**: Red cube (power off), positioned at (-3, 0.5, -3)
- **tool_002**: Yellow cube (locked), positioned at (0, 0.5, -3)
- **Visual indicators**: Power lights, lock symbols, status panels

### Agents (from `agentProfiles`)
- **agent_technician_A**: Capsule with skill-based coloring
- **Positioned** in a circle around the scene
- **Skill panels** showing O*NET skill levels

### Environment
- **Grid floor** (20x20 units)
- **Blue walls** around the perimeter
- **Proper lighting** with directional and ambient light
- **Organized hierarchy** for easy management

### UI Elements
- **Worker title** showing current agent
- **Current task** with step details
- **Task details** with skills and requirements
- **Step progress** indicator

## 🔄 **Automation Features**

### JSON-Driven Updates
- **Change your JSON file** → Scene updates automatically
- **Add new tools** → They appear in the scene
- **Modify agent skills** → Visual representation updates
- **Change workflow** → UI reflects new steps

### State Management
- **Tool states** (available, locked, power on/off)
- **Agent status** (idle, busy, current task)
- **Workflow progress** (current step, dependencies)
- **Visual feedback** (colors, lights, indicators)

## 🐛 **Troubleshooting**

### Scene Not Generating
- ✅ Check Console for error messages
- ✅ Ensure all three scripts are attached to SceneManager
- ✅ Verify JSON file is in `Assets/JsonFile/` or `StreamingAssets/`

### Objects Not Visible
- ✅ Check if objects are positioned outside camera view
- ✅ Verify lighting is set up correctly
- ✅ Ensure materials are assigned properly

### UI Not Updating
- ✅ Check if UI Text components are assigned
- ✅ Verify JSON data is loading (check Console)
- ✅ Ensure SceneUILoader is working

## 🚀 **Next Steps & Extensions**

### Immediate Enhancements
- **Add custom prefabs** for realistic tools and agents
- **Implement step execution** with actual animations
- **Add sound effects** for interactions
- **Create more complex workflows**

### Advanced Features
- **Real-time JSON updates** from external sources
- **Multi-scene support** with different JSON files
- **Save/load scene states** to/from JSON
- **Network synchronization** for multiplayer

### Integration Possibilities
- **Connect to external APIs** for live data
- **Integrate with ML systems** for dynamic workflow generation
- **Add VR/AR support** for immersive training
- **Connect to real IoT devices** for live tool monitoring

## 💡 **Pro Tips**

1. **Start simple**: Use the default primitive setup first
2. **Test with small JSON**: Verify everything works before scaling up
3. **Use the Console**: All generation steps are logged there
4. **Organize your JSON**: Keep it well-structured for easier debugging
5. **Backup your scene**: Save before testing major changes

## 🎉 **You're Ready!**

Your Unity scene will now automatically generate from JSON data! This system is:
- **Fully automated** - No manual scene setup needed
- **JSON-driven** - Easy to modify and extend
- **Production-ready** - Handles complex workflows
- **Extensible** - Easy to add new features

Just press Play and watch your warehouse scene come to life! 🚀
