# Simple Standalone JSON Scene Setup

This guide shows you how to create a **completely separate Unity scene** that only contains JSON-generated UI, without affecting your Factory scene.

## 🚀 **Quick Setup (3 Steps):**

### **Step 1: Create New Scene**
1. **File > New Scene** (or Ctrl+N)
2. **Choose "Basic (Built-in)"** template
3. **Save as "JSONWorkflowScene"**

### **Step 2: Add the Script**
1. **Create empty GameObject** called "JSONSceneController"
2. **Attach `StandaloneJSONScene.cs`** script to it
3. **That's it!** No other setup needed

### **Step 3: Press Play**
- **Scene automatically generates** simple UI panels
- **Reads your `basicUi.json`** file
- **Shows workflow information** from your JSON

## 🎯 **What You'll See:**

### **Simple UI Panels:**
- **Worker Panel** (top-left): Shows current agent
- **Task Panel** (top-right): Shows current task
- **Details Panel** (bottom-center): Shows task details
- **Progress Panel** (top-center): Shows step progress

### **JSON Data Displayed:**
- **Worker**: `agent_technician_A`
- **Task**: `step_001 - repair_motor on tool_001`
- **Details**: Duration, type, skills
- **Progress**: "Step 1 of 2"

## 🎮 **Controls:**

- **Right Arrow**: Next step
- **Left Arrow**: Previous step  
- **Space**: Execute current step

## 🎉 **Benefits:**

1. **Completely separate** from your Factory scene
2. **No conflicts** with existing objects
3. **Simple and clean** - just UI panels
4. **Automatically reads** your JSON file
5. **Easy to test** and modify

## 📁 **Files You Need:**

1. **`StandaloneJSONScene.cs`** - The main script
2. **`basicUi.json`** - Your JSON file (already exists)
3. **New Unity scene** - Created from scratch

## 🚀 **Try This Now:**

1. **Create the new scene**
2. **Add the script**
3. **Press Play**
4. **See your JSON data** displayed in simple UI panels!

This gives you a clean, standalone scene that only shows your JSON workflow data! 🎯
