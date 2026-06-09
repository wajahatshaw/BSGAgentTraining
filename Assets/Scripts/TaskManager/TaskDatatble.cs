using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "GAME/DataTable")]
public class TaskDatatble : ScriptableObject
{
    public List<TaskData> Table = new List<TaskData>();
}

[System.Serializable]
public struct TaskData
{
    public int taskNo;
    public string text;
    public AudioClip audioClip;
}



