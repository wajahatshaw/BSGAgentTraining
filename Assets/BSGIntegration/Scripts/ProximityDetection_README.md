# Proximity Detection System

This system implements proximity-based interactions between agents (supervisors and technicians) and tools/workbenches in the Unity scene, based on configuration from the `basicUi.json` file.

## Features

- **Proximity Detection**: Uses `Physics.OverlapSphere` to detect when agents enter proximity zones around tools and workbenches
- **Color Changes**: Agents change color when entering proximity zones
- **Direction Changes**: Agents are pushed away from tools with configurable avoidance force
- **Sound Effects**: Audio feedback when agents enter proximity zones
- **JSON Configuration**: All settings loaded from `basicUi.json`

## Setup Instructions

### 1. Automatic Setup
1. Add the `ProximitySystemSetup` component to any GameObject in your scene
2. The system will automatically set up everything on Start
3. Or use the context menu "Setup Proximity System" for manual setup

### 2. Manual Setup
1. Create a GameObject named "ProximityDetectionManager"
2. Add the `ProximityDetectionSystem` component
3. Create another GameObject named "ProximityConfigLoader"
4. Add the `ProximityConfigLoader` component
5. Ensure your agents and tools have appropriate colliders

## Configuration

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

## Components

### ProximityDetectionSystem
- Main system controller
- Handles proximity detection logic
- Applies effects to agents
- Provides debug visualization

### ProximityConfigLoader
- Loads configuration from JSON file
- Converts JSON data to Unity-compatible format
- Validates configuration
- Provides runtime reload capability

### ProximitySystemSetup
- Automated setup helper
- Adds required colliders to agents and tools
- Sets up layers
- Provides context menu options for testing

## Usage

### Basic Usage
1. Ensure your scene has objects named with patterns:
   - Agents: `agent_technician_A`, `agent_supervisor_B`, etc.
   - Tools: `tool_001`, `tool_002`, etc.
   - Workbenches: `workbench_001`, etc.

2. Run the scene - the system will automatically detect and apply proximity effects

### Testing
- Use the context menu on `ProximitySystemSetup` component:
  - "Test Proximity System" - Validates and enables the system
  - "Reset All Agent Colors" - Resets agent colors to original
  - "Enable/Disable Proximity System" - Controls system state

### Debug Visualization
- Enable "Show Debug Gizmos" in the `ProximityDetectionSystem` component
- Yellow wireframe spheres will show proximity zones in Scene view

## Customization

### Adding New Proximity Zones
1. Edit `basicUi.json`
2. Add new zone to `proximityZones` array
3. Specify center object, radius, effects, and affected agents
4. Reload configuration using `ProximityConfigLoader.ReloadConfiguration()`

### Modifying Effects
- **Color Change**: Modify `targetColor` (hex format) and `fadeSpeed`
- **Direction Change**: Adjust `avoidanceForce` and `rotationSpeed`
- **Sound Effects**: Set `soundClip` name and `volume`

### Performance Tuning
- Adjust `updateFrequency` for performance vs responsiveness trade-off
- Modify `detectionRadius` for global detection sensitivity
- Use layer masks to optimize collision detection

## Troubleshooting

### Common Issues
1. **No proximity effects**: Check that objects have colliders and correct naming
2. **Performance issues**: Reduce `updateFrequency` or `detectionRadius`
3. **Configuration not loading**: Verify JSON file path and format
4. **Agents not moving**: Ensure agents have Rigidbody components

### Debug Steps
1. Check console for configuration loading messages
2. Enable debug gizmos to visualize proximity zones
3. Use "Test Proximity System" context menu option
4. Verify object names match JSON configuration

## Integration with Existing Systems

The proximity detection system is designed to work alongside existing agent movement and task execution systems. It provides additional behavioral responses without interfering with core functionality.

- Agents maintain their original movement patterns
- Proximity effects are additive to existing behaviors
- System can be enabled/disabled without affecting other systems
- Color changes are reversible and don't persist
