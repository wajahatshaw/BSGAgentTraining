# Manual ML-Agent Setup Guide

## Why Manual Setup?
The ML-Agents API has changed across different versions of Unity. Programmatic configuration can fail due to:
- Different property names in different ML-Agents versions
- Deprecated properties that no longer exist
- Missing type definitions in certain namespace versions

## ✅ What's Already Working
1. **MLAgentAttacher.cs** - Automatically adds components to runtime agents
2. **BSGMLAgent.cs** - Custom agent behavior script
3. **Behavior Name** - Automatically set to match YAML config

## 🔧 What You Need to Configure Manually

### Step 1: Run Your Scene in Unity
- The `MLAgentAttacher` will add the components to your agents
- Check the console for: `✅ ML-Agents attached to [AgentName]`

### Step 2: Configure Each Agent in Inspector

For each agent (SIMPLE_Technician_01, SIMPLE_Technician_02, SIMPLE_Supervisor_01, SIMPLE_Supervisor_02):

1. **Select the agent GameObject** in the Hierarchy
2. **Find the Behavior Parameters component**
3. **Configure these settings:**

#### Brain Parameters:
```
Vector Observation Space:
  Space Size: 15
  Num Stacked Vector Observations: 1

Action Space:
  Space Type: Discrete
  Branch 0 Size: 3  (Move: 0=stop, 1=forward, 2=backward)
  Branch 1 Size: 3  (Rotate: 0=left, 1=straight, 2=right)
```

#### Decision Requester:
```
Decision Period: 5
Take Actions Between Decisions: True
```

### Step 3: Create Agent Prefabs (Recommended)
After configuring each agent in the Inspector:

1. **Drag the configured agent from Hierarchy to Prefabs folder**
2. **Name it appropriately**: `SIMPLE_Technician_01_Prefab`, etc.
3. **Delete the configured agent from the scene**

### Step 4: Update SimpleFourEntitySystem.cs to Use Prefabs

You can modify the code to instantiate prefabs instead of creating GameObjects:

```csharp
// In CreateProminentEntity method, replace GameObject.CreatePrimitive with:
GameObject prefab = Resources.Load<GameObject>($"Prefabs/{entityName}");
GameObject entity = Instantiate(prefab);
```

OR

```csharp
// Instantiate the runtime-created agent and copy Behavior Parameters from prefab
GameObject prefab = Resources.Load<GameObject>($"Prefabs/{entityName}");
BehaviorParameters prefabBP = prefab.GetComponent<BehaviorParameters>();
entity.GetComponent<BehaviorParameters>().BehaviorName = prefabBP.BehaviorName;
// Copy other settings...
```

## 🎯 Quick Setup Checklist

- [ ] All 4 agents created in scene
- [ ] Behavior Parameters component on each agent
- [ ] Behavior Name set correctly (Technician1, Technician2, Supervisor1, Supervisor2)
- [ ] Vector Observation Size = 15
- [ ] Num Stacked = 1
- [ ] Discrete Actions with 3, 3 branches
- [ ] Decision Requester configured
- [ ] Scene saved

## 🚀 Alternative: Configuration Script
If you want to avoid manual configuration, create this script:

```csharp
using UnityEngine;
using Unity.MLAgents;

[System.Serializable]
public class AgentConfig
{
    public string agentName;
    public string behaviorName;
    public Vector3 position;
    public Color color;
}

public class MLAgentConfigLoader : MonoBehaviour
{
    public AgentConfig[] agents;
    
    void Start()
    {
        foreach (var config in agents)
        {
            GameObject agent = GameObject.Find(config.agentName);
            if (agent != null)
            {
                BehaviorParameters bp = agent.GetComponent<BehaviorParameters>();
                if (bp != null)
                {
                    bp.BehaviorName = config.behaviorName;
                    // Other settings...
                }
            }
        }
    }
}
```

## 📝 Notes
- The MLAgentAttacher will still work to add the components
- You just need to configure the properties in the Inspector
- Once configured, you can save as prefabs for reuse
- Or use the Configuration Script approach above

