# Troubleshooting Guide: Why New Entities Aren't Appearing

If you're not seeing new entities and agents being created, follow this step-by-step troubleshooting guide.

## 🔍 **Step 1: Check Console for Errors**

1. **Open Console** (Window > General > Console)
2. **Look for error messages** when you start the scene
3. **Look for the debug messages** from SceneGenerator

**Expected Console Output:**
```
=== SCENE GENERATION STARTED ===
SceneUILoader found successfully
Scene data loaded: scene_warehouse_001
Tools to generate: 2
Agents to generate: 1
=== GENERATING TOOLS ===
Found 2 tools to generate
Creating tool: tool_001 at index 0
Successfully created tool: tool_001 at position (7.0, 0.5, -3.0)
Creating tool: tool_002 at index 1
Successfully created tool: tool_002 at position (10.0, 0.5, -3.0)
Tool generation complete. Created 2 tools.
=== GENERATING AGENTS ===
Found 1 agents to generate
Creating agent: agent_technician_A at index 0
Successfully created agent: agent_technician_A at position (18.0, 1.0, 0.0)
Agent generation complete. Created 1 agents.
Scene 'scene_warehouse_001' generated successfully!
Generated tools: 2
Generated agents: 1
```

## 🚨 **Common Issues & Solutions:**

### **Issue 1: No Console Messages at All**
**Problem**: SceneGenerator is not running
**Solution**: 
1. Check if `generateOnStart` is checked in SceneGenerator
2. Verify all scripts are attached to the same GameObject
3. Make sure the GameObject is active in the scene

### **Issue 2: "SceneUILoader not found" Error**
**Problem**: Scripts are not on the same GameObject
**Solution**:
1. Create a new GameObject called "JSONSceneManager"
2. Attach ALL THREE scripts to it:
   - `SceneUILoader`
   - `SceneGenerator`
   - `JSONUIGenerator`

### **Issue 3: "No scene data available" Error**
**Problem**: JSON file is not loading
**Solution**:
1. Verify `basicUi.json` is in `Assets/JsonFile/` folder
2. Check JSON file syntax (use a JSON validator)
3. Look for SceneUILoader error messages

### **Issue 4: "No initial states found" Error**
**Problem**: JSON structure doesn't match expected format
**Solution**:
1. Check that your JSON has `initialStates` section
2. Verify the structure matches the C# classes

## 🧪 **Step 2: Use the Test Script**

1. **Attach `SceneGenerationTester`** to your JSONSceneManager
2. **Press G** to test normal generation
3. **Press F** to force generation (creates test objects even if JSON fails)
4. **Press C** to clear generated objects

## 🔧 **Step 3: Manual Testing**

### **Test 1: Force Generation**
```csharp
// In Console, type:
FindObjectOfType<SceneGenerator>().ForceGenerateScene()
```

### **Test 2: Check JSON Loading**
```csharp
// In Console, type:
var loader = FindObjectOfType<SceneUILoader>();
loader.GetSceneData()
```

### **Test 3: Manual Object Creation**
```csharp
// In Console, type:
var generator = FindObjectOfType<SceneGenerator>();
generator.CreateBasicScene()
```

## 📍 **Step 4: Check Object Positions**

**New objects should appear:**
- **Tools**: 10+ units to the right of your existing scene
- **Agents**: 10+ units to the right, in a circle pattern
- **Environment**: New floor and walls (cyan color)

**Look for these objects in Hierarchy:**
- `JSON_Generated_Tools`
- `JSON_Generated_Agents` 
- `JSON_Generated_Environment`

## 🎯 **Step 5: Verify JSON Structure**

Your `basicUi.json` should have this structure:
```json
{
  "scene_id": "scene_warehouse_001",
  "initialStates": {
    "tool_001": { ... },
    "tool_002": { ... }
  },
  "agentProfiles": {
    "agent_technician_A": { ... }
  }
}
```

## 🚀 **Quick Fix Steps:**

1. **Delete the current JSONSceneManager** GameObject
2. **Create a new GameObject** called "JSONSceneManager"
3. **Attach all four scripts**:
   - `SceneUILoader`
   - `SceneGenerator`
   - `JSONUIGenerator`
   - `SceneGenerationTester`
4. **Check "Generate On Start"** in SceneGenerator
5. **Uncheck "Clear Existing Scene"** in SceneGenerator
6. **Press Play**
7. **Check Console** for debug messages
8. **Press F** to force generation if needed

## 📞 **If Still Not Working:**

1. **Check Console** for specific error messages
2. **Verify JSON file** is valid
3. **Ensure all scripts** are attached
4. **Try force generation** (Press F)
5. **Look for test objects** (magenta cube, cyan capsule)

The system should create new entities that are completely separate from your existing Factory scene objects! 🎯
