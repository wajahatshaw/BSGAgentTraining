# Proximity Detection System - Complete Setup Guide

This guide explains how to set up and use the complete proximity detection system that integrates with your existing Unity scene and JSON configuration.

## 🎯 What the System Does

The proximity detection system automatically detects when agents (supervisors, technicians, inspectors) enter proximity zones around tools and workbenches, and applies various effects:

- **Color Changes**: Agents change color when entering proximity zones
- **Direction Changes**: Agents are pushed away from tools with configurable avoidance force
- **Sound Effects**: Audio feedback when agents enter proximity zones
- **JSON Configuration**: All settings loaded from `basicUi.json`

## 📁 Files Created/Modified

### New Files:
- `ProximityDetectionSystem.cs` - Main proximity detection logic
- `ProximityConfigLoader.cs` - Loads configuration from JSON
- `ProximitySystemSetup.cs` - Automated setup helper
- `ProximityIntegration.cs` - Integrates with existing systems
- `ProximityDetection_README.md` - Detailed documentation

### Modified Files:
- `basicUi.json` - Added proximity detection configuration
- `StandaloneJSONScene.cs` - Added proximity integration

## 🚀 Quick Setup (Automatic)

### Option 1: Automatic Integration (Recommended)
1. **Run your existing scene** - The system will automatically integrate
2. **That's it!** - Proximity detection will be active

The `StandaloneJSONScene` component now automatically creates and configures the proximity system when it creates entities from JSON.

### Option 2: Manual Setup
1. Create an empty GameObject in your scene
2. Add the `ProximityIntegration` component
3. Use the context menu "Integrate Proximity System"
4. Use "Test Integration" to verify everything works

## 🔧 Configuration

The system reads configuration from `Assets/JsonFile/basicUi.json`. The proximity detection section includes:

```json
"proximityDetection": {
  "enabled": true,
  "detectionRadius": 3.0,
  "updateFrequency": 0.1,
  "proximityZones": [
    {
      "zoneId": "tool_001_proximity",
      "centerObject": "tool_001",
      "radius": 2.5,
      "effects": {
        "colorChange": {
          "enabled": true,
          "targetColor": "#FF6B6B",
          "fadeSpeed": 2.0
        },
        "directionChange": {
          "enabled": true,
          "avoidanceForce": 1.5,
          "rotationSpeed": 45.0
        },
        "soundEffect": {
          "enabled": true,
          "soundClip": "proximity_alert",
          "volume": 0.7
        }
      },
      "affectedAgents": ["agent_technician_A", "agent_supervisor_B", "agent_inspector_C"]
    }
  ]
}
```

## 🎮 How to Use

### Running the System
1. **Play your Unity scene**
2. **Watch agents move around** - They will automatically avoid tools when they get too close
3. **Observe color changes** - Agents will change color when entering proximity zones
4. **Listen for sounds** - Audio feedback will play (if sound clips are available)

### Testing the System
- Use the context menu on `ProximityIntegration` component:
  - **"Test Integration"** - Validates and enables the system
  - **"Reset Integration"** - Disables system and resets colors
  - **"Integrate Proximity System"** - Manual setup

### Debug Visualization
- Enable "Show Debug Gizmos" in the `ProximityDetectionSystem` component
- Yellow wireframe spheres will show proximity zones in Scene view

## 🔍 Proximity Zones Configured

The system creates proximity zones for:

1. **tool_001** (Industrial Motor Unit) - Red proximity effect
2. **tool_002** (Secure Toolbox Alpha) - Orange proximity effect  
3. **tool_003** (Hydraulic Lift Station) - Light blue proximity effect
4. **tool_004** (Safety Inspection Station) - Light green proximity effect
5. **workbench_001** (Main Assembly Workbench) - Orange proximity effect

Each zone has different:
- **Radius**: 2.0 to 3.0 units
- **Avoidance Force**: 1.0 to 2.0
- **Rotation Speed**: 25 to 60 degrees/second
- **Color Changes**: Unique colors for each zone
- **Affected Agents**: Specific agents for each zone

## 🎛️ Customization

### Adding New Proximity Zones
1. Edit `basicUi.json`
2. Add new zone to `proximityZones` array
3. Specify center object, radius, effects, and affected agents
4. Reload configuration

### Modifying Existing Zones
- **Radius**: Change detection distance
- **Color**: Modify `targetColor` (hex format)
- **Force**: Adjust `avoidanceForce` for push strength
- **Speed**: Change `rotationSpeed` for rotation rate
- **Agents**: Modify `affectedAgents` array

### Performance Tuning
- **Update Frequency**: Lower values = more responsive, higher values = better performance
- **Detection Radius**: Global setting for all zones
- **Layer Masks**: Use Unity layers to optimize collision detection

## 🐛 Troubleshooting

### Common Issues

1. **No proximity effects visible**
   - Check that objects have colliders (system adds them automatically)
   - Verify object names match JSON configuration
   - Ensure proximity system is enabled

2. **Performance issues**
   - Reduce `updateFrequency` in JSON (try 0.2 instead of 0.1)
   - Decrease `detectionRadius` globally
   - Check for too many objects in scene

3. **Configuration not loading**
   - Verify JSON file path: `Assets/JsonFile/basicUi.json`
   - Check JSON format is valid
   - Look for console error messages

4. **Agents not moving**
   - Ensure agents have Rigidbody components (added automatically)
   - Check that avoidance force is > 0
   - Verify agents are in `affectedAgents` list

### Debug Steps
1. **Check console** for configuration loading messages
2. **Enable debug gizmos** to visualize proximity zones
3. **Use "Test Integration"** context menu option
4. **Verify object names** match JSON configuration
5. **Check colliders** are present on agents and tools

## 🔄 Integration with Existing Systems

The proximity detection system is designed to work alongside your existing systems:

- **StandaloneJSONScene**: Automatically integrates when creating entities
- **SceneGenerator**: Works with objects created by scene generation
- **Agent Movement**: Adds avoidance behavior without interfering with existing movement
- **Task Execution**: Provides additional behavioral responses

### System Compatibility
- ✅ Works with existing agent movement systems
- ✅ Compatible with task execution workflows  
- ✅ Integrates with JSON scene generation
- ✅ Non-destructive to existing scene objects
- ✅ Can be enabled/disabled without affecting other systems

## 📊 Performance Considerations

- **Update Frequency**: Default 0.1 seconds provides good balance
- **Collision Detection**: Uses Unity's Physics.OverlapSphere (efficient)
- **Memory Usage**: Minimal overhead, stores original colors for reset
- **CPU Usage**: Scales with number of agents and proximity zones

## 🎯 Next Steps

1. **Test the system** with your existing scene
2. **Customize proximity zones** in the JSON file
3. **Adjust parameters** for your specific use case
4. **Add sound effects** by placing audio clips in Resources folder
5. **Extend the system** with additional effects or behaviors

The system is now ready to use and will automatically enhance your Unity scene with proximity-based interactions between agents and tools!
