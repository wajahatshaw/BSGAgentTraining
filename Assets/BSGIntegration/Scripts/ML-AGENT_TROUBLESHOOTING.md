# ML-Agent Troubleshooting Guide

## Error: CS0246 - BehaviorParameters type not found

### Issue
```
Assets/Scripts/MLAgentAttacher.cs(53,53): error CS0246: The type or namespace name 'BehaviorParameters' could not be found
```

### Root Cause
This error occurs when Unity.MLAgents package is not properly referenced in your project.

### Solutions

#### Solution 1: Ensure ML-Agents Package is Installed
1. Open Unity Editor
2. Go to **Window** → **Package Manager**
3. Search for "ML-Agents"
4. If not installed, click "Install"

#### Solution 2: Add Assembly Reference (Manual)
If the using directives don't work, you may need to add assembly references:

1. Create or edit `Assets/asmdef` file (if you have a custom assembly)
2. Add reference to `Unity.MLAgents`

Or add to `Assets/Editor/BSG.asmdef`:
```json
{
    "name": "BSG",
    "references": [
        "GUID: Unity.MLAgents",
        "GUID: Unity.MLAgents.Policies"
    ]
}
```

#### Solution 3: Use Simpler Approach
If ML-Agents still won't compile, use a simplified version that only sets Behavior Name:

```csharp
private static void ConfigureBehaviorParameters(BehaviorParameters bp, string behaviorName)
{
    bp.BehaviorName = behaviorName;
    bp.TeamId = 0;
    
    // NOTE: ActionSpec must be configured in Unity Inspector
    Debug.Log($"✅ Set Behavior Name to: {behaviorName}");
}
```

### Solution 4: Configure in Inspector Instead
1. In Unity Editor, select each agent GameObject
2. Find the Behavior Parameters component (added by MLAgentAttacher)
3. Manually set:
   - Vector Observation Size: 15
   - Num Stacked Vector Observations: 1
   - Action Spaces: Discrete
   - Branch Sizes: 3, 3
   - Vector Action Size: 3, 3

### Verification Steps
1. Check that ML-Agents package is installed in Package Manager
2. Verify `Unity.MLAgents.dll` exists in your project
3. Check Unity console for any missing DLL errors
4. Try rebuilding the project (Assets → Reimport All)

### Alternative: Use Pre-existing Prefabs
If runtime attachment proves difficult, you can:
1. Create agent prefabs with ML-Agents components already attached
2. Instantiate those prefabs instead of creating GameObjects programmatically
3. Only set the Behavior Name and Agent ID at runtime

### Quick Fix Command
If you have ML-Agents installed but it's not recognizing types:

```bash
# In Unity project root
cd Assets
find . -name "*.asmdef" -exec echo "{}" \;
```

Check if you need to add Unity.MLAgents reference to your assembly definition files.

