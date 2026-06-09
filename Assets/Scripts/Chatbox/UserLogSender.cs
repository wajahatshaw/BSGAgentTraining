using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using System.Text;
using Jy_Util;

public class UserLogSender : MonoSingleton<UserLogSender>
{
    [Space]
    [SerializeField] bool send = true;
    [Header("API")]
    [SerializeField] private string logsStoreUrl =
        "https://api.pap-inc.com/logs/store";

    

    [Header("Recipient")]
    [SerializeField] private string recipientId;

    /// <summary>
    /// Call this to send a log
    /// </summary>
    public void SendLog(string message, string msgType)
    {
        if(send)
        StartCoroutine(SendLogCoroutine(message, msgType));
    }

    private IEnumerator SendLogCoroutine(string message, string msgType)
    {
        LogRequestBody body = new LogRequestBody
        {
            performed_activity_id = GameAsstes.Instance.simulationConfigSO.activityId,
            message = message,
            recipient_id = recipientId,
            msg_type = msgType // "msg" or "task"
        };

        string json = JsonUtility.ToJson(body);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest request = new UnityWebRequest(GameAsstes.Instance.simulationConfigSO.storeLogsApi, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {GameAsstes.Instance.simulationConfigSO.accessToken}");
            request.SetRequestHeader("X-Refresh-Token", $"Bearer {GameAsstes.Instance.simulationConfigSO.refreshToken}");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Log send failed: " + request.error);
                Debug.LogError("Response: " + request.downloadHandler.text);
                yield break;
            }

            Debug.Log("Log sent successfully: " + json);
        }
    }
}
