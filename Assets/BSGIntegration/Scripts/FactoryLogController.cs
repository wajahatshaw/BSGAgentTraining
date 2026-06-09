using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PAP.EarnoutBSG
{
    public enum SkillTier : byte
    {
        None = 0,
        Low = 1,
        Standard = 2,
        High = 3
    }

    public enum TaskStatus : byte
    {
        None = 0,
        NotStarted = 1,
        InProgress = 2,
        DidNotComplete = 3,
        Failure = 4,
        PartialCompletion = 5,
        FullCompletion = 6,
    }
    
    [Serializable]
    public struct FactoryLogStepInfo
    {
        public int StepID;
        public int EntityID;
        public string ToolUsed;
        public DateTime Timestamp;
        public string DataOutput;
        public string KPITriggered;
        public SkillTier SkillTier;
        public TaskStatus TaskStatus;
        public float CompletionDuration;
    }

    [Serializable]
    public class FactoryLogStepList
    {
        public List<FactoryLogStepInfo> Steps = new();
    }
    
    public class FactoryLogController : MonoBehaviour
    {
        private const bool LOG_TO_UNITY_CONSOLE = true;
        private const bool LOG_TO_JSON_FILE = true;
        private const string LOG_FILE_NAME = "/TaskLog.json";

        public static void LogFactoryStep(FactoryLogStepInfo info)
        {
            if (LOG_TO_UNITY_CONSOLE) LogToUnityConsole(info);
            if (LOG_TO_JSON_FILE) LogToJsonFile(info);
        }

        private static void LogToUnityConsole(FactoryLogStepInfo info)
        {
            var output = $"Step ID: {info.StepID}\nEntity ID: {info.EntityID}\nTool Used: {info.ToolUsed}\nTimestamp: {info.Timestamp}\nData Output: {info.DataOutput}\nKPI Triggered: {info.KPITriggered}\nSkill Tier: {info.SkillTier}\nTask Status: {info.TaskStatus}\nCompletion Duration: {info.CompletionDuration}\n";
            Debug.Log(output);
        }
        
        private static void LogToJsonFile(FactoryLogStepInfo info)
        {
            FactoryLogStepList logSteps;
            
            var dataPath = Application.persistentDataPath + LOG_FILE_NAME;
            if (File.Exists(dataPath))
            {
                var currentData = File.ReadAllText(dataPath);
                
                // Does not currently handle cases for empty file or corrupt JSON
                logSteps = JsonUtility.FromJson<FactoryLogStepList>(currentData);
            }
            else
            {
                logSteps = new FactoryLogStepList();
            }

            logSteps.Steps.Add(info);

            var newData = JsonUtility.ToJson(logSteps, true);
            File.WriteAllText(dataPath, newData);
        }
    }
}