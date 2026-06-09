using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Jy_Util;

public class QuestionLoader : MonoBehaviour
{
    
    private string apiUrl =
        "https://api.pap-inc.com/generate-unity-questions";

    private string accessToken;
    private string refreshToken;

    [Header("Runtime Data")]
    public List<QuestionData> loadedQuestions = new List<QuestionData>();
    public PointSummary pointSummary;


    void Start()
    {
        apiUrl = GameAsstes.Instance.simulationConfigSO.generateQuestionsApi;
        accessToken = GameAsstes.Instance.simulationConfigSO.accessToken;
        refreshToken = GameAsstes.Instance.simulationConfigSO.refreshToken;

        LoadQuestions(GameAsstes.Instance.simulationConfigSO.activityId);
    }

    public void LoadQuestions(string performedActivityId)
    {
        StartCoroutine(LoadQuestionsCoroutine(performedActivityId));
    }

    private IEnumerator LoadQuestionsCoroutine(string performedActivityId)
    {
        // Request body
        var bodyObj = new
        {
            performed_activity_id = performedActivityId
        };

        string bodyJson = JsonConvert.SerializeObject(bodyObj);

        using (UnityWebRequest request =
               new UnityWebRequest(apiUrl, "POST"))
        {
            request.uploadHandler =
                new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(bodyJson));
            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
            request.SetRequestHeader("X-Refresh-Token", $"Bearer {refreshToken}");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Question API Error: " + request.error);
                Debug.LogError(request.downloadHandler.text);
                yield break;
            }

            ParseResponse(request.downloadHandler.text);
        }
    }

    private void ParseResponse(string json)
    {
        Debug.Log("Question API Response: " + json);

        QuestionApiResponse response =
            JsonConvert.DeserializeObject<QuestionApiResponse>(json);

        if (response == null || response.results == null || response.results.Count == 0)
        {
            Debug.LogWarning("No questions found in response.");
            return;
        }

        // Clear old data
        loadedQuestions.Clear();

        // You currently get one result block → but keep it flexible
        QuestionResultBlock block = response.results[0];

        pointSummary = block.point_summary;

        if (block.questions != null)
        {
            loadedQuestions.AddRange(block.questions);
        }

        Debug.Log($"Loaded {loadedQuestions.Count} questions.");
    }
}
