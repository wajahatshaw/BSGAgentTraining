using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class SaveAndLoad
{
    private static readonly string saveFolder = "Saves/";


    public static void Save<T>(string dataPath, T data)
    {
        //dataPath = saveFolder + dataPath;
        string json = JsonUtility.ToJson(data);
        File.WriteAllText(dataPath, json);
    }

    public static T Load<T>(string dataPath)
    {
        //dataPath = saveFolder + dataPath;
        if (!File.Exists(dataPath))
        {
            FileStream fs = new FileStream(dataPath, FileMode.Create);
            fs.Close();
            return default; // Return default value for the type (null for objects, 0 for numbers, etc.)
        }

        string json = File.ReadAllText(dataPath);
        return JsonUtility.FromJson<T>(json);
    }
}
