using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class EnhancedMaterialColor
{
    public float r;
    public float g; 
    public float b;
    public float a;
    
    public Color ToColor()
    {
        return new Color(r, g, b, a);
    }
}

[System.Serializable]
public class EnhancedMovementPattern
{
    public string type;
    public float speed;
    public EnhancedPositionData[] waypoints;
    public float pauseDuration;
    public float rotationSpeed;
    
    public MovementPattern ToMovementPattern()
    {
        MovementPattern pattern = new MovementPattern();
        pattern.type = type;
        pattern.speed = speed;
        pattern.pauseDuration = pauseDuration;
        pattern.rotationSpeed = rotationSpeed;
        
        pattern.waypoints = new List<Vector3>();
        if (waypoints != null)
        {
            foreach (var waypoint in waypoints)
            {
                pattern.waypoints.Add(new Vector3(waypoint.x, waypoint.y, waypoint.z));
            }
        }
        
        return pattern;
    }
}

[System.Serializable]
public class EnhancedPositionData
{
    public float x;
    public float y;
    public float z;
}

[System.Serializable]
public class EnhancedToolState
{
    public string objectId;
    public string name;
    public string type;
    public bool isAvailable;
    public bool isActive;
    public string currentUser;
    public EnhancedPositionData position;
    public string color;
    public EnhancedMaterialColor materialColor;
    public ToolProperties properties;
}

[System.Serializable]
public class EnhancedAgentProfile
{
    public string agentId;
    public string name;
    public string role;
    public EnhancedPositionData position;
    public string color;
    public EnhancedMaterialColor materialColor;
    public int experience_years;
    public string[] certifications;
    public EnhancedMovementPattern movementPattern;
    public Dictionary<string, float> onetSkillLevels;
}

[System.Serializable]
public class EnhancedSceneData
{
    public string scene_id;
    public PlanData plan;
    public Dictionary<string, EnhancedToolState> initialStates;
    public Dependency[] dependencies;
    public WorkflowStep[] sequence;
    public Dictionary<string, EnhancedAgentProfile> agentProfiles;
    public SceneLayoutData sceneLayout;
    public MetadataInfo metadata;
}

[System.Serializable]
public class SceneLayoutData
{
    public EnvironmentData environment;
    public Dictionary<string, ZoneData> zones;
}

[System.Serializable]
public class EnvironmentData
{
    public int floor_size;
    public int wall_height;
    public string lighting;
    public string atmosphere;
}

[System.Serializable]
public class ZoneData
{
    public EnhancedPositionData center;
    public float radius;
    public string[] tools;
}

[System.Serializable]
public class MetadataInfo
{
    public string version;
    public string created_date;
    public string last_modified;
    public string author;
    public string scene_complexity;
    public string estimated_completion_time;
}

