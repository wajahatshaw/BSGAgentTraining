# Create New Unity Scene with JSON Content Only

This guide explains how to create a **completely new Unity scene** that contains only the JSON-generated content, avoiding any conflicts with your existing Factory scene.

## 🚀 **Option 1: Create New Scene (Recommended)**

### **Step 1: Create New Scene**
1. **File > New Scene** (or Ctrl+N)
2. **Choose "Basic (Built-in)"** template
3. **Save the scene** as "JSONWorkflowScene"

### **Step 2: Add the Simple Creator**
1. **Create an empty GameObject** called "JSONSceneCreator"
2. **Attach `SimpleJSONSceneCreator.cs`** script to it
3. **Make sure "Create On Start"** is checked

### **Step 3: Press Play**
- **The scene will automatically generate** when you press Play
- **All content will be created** from scratch
- **No conflicts** with existing scenes

## 🎯 **What Gets Created:**

### **Environment:**
- **JSON_Floor**: Gray floor (30x30 units)
- **JSON_North_Wall**: Blue wall at north
- **JSON_South_Wall**: Blue wall at south  
- **JSON_East_Wall**: Blue wall at east
- **JSON_West_Wall**: Blue wall at west

### **Demo Objects:**
- **JSON_Repair_Tool_001**: Red cube at (-5, 0.5, -5)
- **JSON_Toolbox_002**: Yellow cube at (5, 0.5, -5)
- **JSON_Machine_003**: Blue cube at (0, 0.5, 5)
- **JSON_Technician_A**: Green capsule at (-8, 1, 0)
- **JSON_Technician_B**: Cyan capsule at (8, 1, 0)

### **UI Panels:**
- **JSON_Worker_Panel**: Top-left (Worker Info)
- **JSON_Task_Panel**: Top-right (Current Task)
- **JSON_Details_Panel**: Bottom-center (Task Details)
- **JSON_Progress_Panel**: Top-center (Progress)

## 🔧 **Option 2: Use in Existing Scene**

If you want to use this in your existing Factory scene:

1. **Add the `SimpleJSONSceneCreator`** to your existing scene
2. **Uncheck "Create On Start"** to prevent auto-generation
3. **Call the methods manually** when needed

## 🎮 **Controls:**

### **Automatic:**
- **Scene generates automatically** when you press Play
- **All content created** from scratch

### **Manual:**
- **Call `CreateJSONScene()`** to generate content
- **Call `RegenerateScene()`** to recreate everything
- **Call `ClearScene()`** to remove generated content

## 🎨 **Customization:**

### **Change Colors:**
```csharp
public Color backgroundColor = new Color(0.2f, 0.2f, 0.3f);
```

### **Change Object Positions:**
Modify the positions in `CreateDemoObjects()` method

### **Add More Objects:**
Add new calls to `CreateDemoTool()` or `CreateDemoAgent()`

## 🐛 **Troubleshooting:**

### **Nothing Appears:**
- ✅ Check Console for error messages
- ✅ Ensure script is attached to active GameObject
- ✅ Verify "Create On Start" is checked

### **Objects Not Visible:**
- ✅ Camera should automatically position at (0, 15, -20)
- ✅ Look for yellow highlight spheres above objects
- ✅ Check Hierarchy for "JSON_*" objects

### **UI Not Showing:**
- ✅ Look for "JSON_UI_Canvas" in Hierarchy
- ✅ Ensure Canvas is set to Screen Space Overlay
- ✅ Check Canvas sorting order (should be 100)

## 🎉 **Benefits of New Scene:**

1. **No conflicts** with existing Factory scene
2. **Clean slate** for JSON content
3. **Easy to test** and modify
4. **Can be saved separately** from main project
5. **Perfect for development** and testing

## 🚀 **Next Steps:**

1. **Create the new scene** using Option 1
2. **Test the generation** by pressing Play
3. **Modify the content** as needed
4. **Integrate with your JSON** when ready
5. **Save the scene** for future use

This approach gives you a completely clean scene dedicated to your JSON workflow system! 🎯
