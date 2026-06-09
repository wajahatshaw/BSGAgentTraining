using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;
using Jy_Util;
using System.Linq;
using System;


public class UserLogLoader : MonoSingleton<UserLogLoader>
{
    
    private string baseUrl =
        "https://api.pap-inc.com/performed-activities";


    [Header("Auth")]
    private string accessToken;
    private string refreshToken;

    public List<ConversationLog> loadedLogs = new List<ConversationLog>();
    public Action<List<ConversationLog>> AC_onUserLogLoded;
    public Action<List<ConversationUser>> AC_onUserDataLoaded;
    

    void Start()
    {
        accessToken = GameAsstes.Instance.simulationConfigSO.accessToken;
        refreshToken = GameAsstes.Instance.simulationConfigSO.refreshToken;
        StartCoroutine(LoadPreviousLogs());
    }

    // public IEnumerator LoadPreviousLogs()
    // {
    //     /*string url = $"{baseUrl}/{performedActivityId}/all-conversations";

    //     using (UnityWebRequest request = UnityWebRequest.Get(url))
    //     {
    //         // Headers
    //         request.SetRequestHeader("Content-Type", "application/json");
    //         request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
    //         request.SetRequestHeader("refresh-X-Refresh-Token", $"Bearer {refreshToken}");

    //         yield return request.SendWebRequest();
    

    //         if (request.result != UnityWebRequest.Result.Success)
    //         {
    //             Debug.LogError("Failed to load logs: " + request.error);
    //             yield break;
    //         }

    //         string json = request.downloadHandler.text;
    //         Debug.Log("Logs Response: " + json);

    //         // If API returns an array directly
    //         ConversationLog[] logs =
    //             JsonHelper.FromJson<ConversationLog>(json);

    //         loadedLogs.Clear();
    //         loadedLogs.AddRange(logs);

    //         OnLogsLoaded(loadedLogs);
    //     }*/
        

    // }

    [NaughtyAttributes.Button]
IEnumerator LoadPreviousLogs()
{
    string url =
       GameAsstes.Instance.simulationConfigSO.loadConversationLogsApi
    .Replace("{activity_id}", GameAsstes.Instance.simulationConfigSO.activityId);


    using (UnityWebRequest request = UnityWebRequest.Get(url))
    {
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
        request.SetRequestHeader("X-Refresh-Token", $"Bearer {refreshToken}");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Log API Error: " + request.error);
            yield break;
        }

        string json = request.downloadHandler.text;
        Debug.Log("log json data: " + json);

        JObject root = JObject.Parse(json);

        // STEP 1: Get first activity node (dynamic key)
        JProperty activityNode = root.Properties().FirstOrDefault();
        if (activityNode == null)
        {
            Debug.LogWarning("No activity data found.");
            yield break;
        }

        JObject activityData = activityNode.Value as JObject;
        if (activityData == null)
        {
            Debug.LogWarning("Invalid activity data.");
            yield break;
        }



        // STEP 2: Parse USERS
        List<ConversationUser> users = new List<ConversationUser>();

        JArray usersArray = activityData["users"] as JArray;
        if (usersArray != null)
        {
            users = usersArray.ToObject<List<ConversationUser>>();
            
        }
        Debug.Log("Users Loaded: " + users.Count);




        // STEP 3: Parse CONVERSATIONS (may be empty)
        List<ConversationLog> loadedLogs = new List<ConversationLog>();

        JObject conversationsObj = activityData["conversations"] as JObject;
        if (conversationsObj != null)
        {
            foreach (var convo in conversationsObj.Properties())
            {
                JArray messages = convo.Value as JArray;
                if (messages == null) continue;

                foreach (var msg in messages)
                {
                    ConversationLog log = msg.ToObject<ConversationLog>();
                    loadedLogs.Add(log);
                }
            }
        }

        // STEP 4: Notify
        OnLogsLoaded(loadedLogs);
        AC_onUserLogLoded?.Invoke(loadedLogs);
        AC_onUserDataLoaded?.Invoke(users);//Notify
    }
}


    private void OnLogsLoaded(List<ConversationLog> logs)
    {
        
    }
}
