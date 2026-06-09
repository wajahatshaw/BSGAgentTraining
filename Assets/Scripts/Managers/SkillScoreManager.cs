using UnityEngine;
using TMPro;
using Jy_Util;
using System.Collections;
using Newtonsoft.Json;
using UnityEngine.Networking;


public class SkillScoreManager : MonoSingleton<SkillScoreManager>
{
    [Header("Score")]
    [SerializeField] private float totalScore = 0f;
    [SerializeField] private float maxScore = 100f;

    [Header("UI")]
    [SerializeField] private SliderController  scoreSlider;
    [SerializeField] TMP_Text skillText; 
    

    void Start()
    {
        UpdateUI();
    }

    /// <summary>
    /// Adds score (can be positive or negative)
    /// </summary>
    public void AddScore(float amount)
    {
        totalScore += amount;
        UpdateUI();
    }

    /// <summary>
    /// Removes score. If more is removed than available,
    /// score is allowed to go negative.
    /// </summary>
    public void RemoveScore(float amount)
    {

        totalScore -= amount;

        if (totalScore < 0f)
            totalScore = 0f;

        UpdateUI();
    }

    /// <summary>
    /// Directly set score (optional utility)
    /// </summary>
    public void SetScore(float value)
    {
        totalScore = value;
        UpdateUI();
    }

    /// <summary>
    /// Returns current total score
    /// </summary>
    public float GetTotalScore()
    {
        return totalScore;
    }

    private void UpdateUI()
    {
        if (scoreSlider == null)
            return;

        // Slider should not show negative visually
        float displayScore = Mathf.Clamp(totalScore, 0f, maxScore);

        scoreSlider.SetMaxAmount(maxScore);
        scoreSlider.SetAmount(displayScore);

        skillText.text = "Skill: "+totalScore+"/"+maxScore;
    }


     private const string SkillApiUrl =
        "https://api.pap-inc.com/phase-d2/skill-level-by-name";

    [Header("Auth")]
    [SerializeField] private string accessToken;
    [SerializeField] private string refreshToken;

    public void UpdateSkillLevel(
        string objectId,
        string skillName,
        int newLevel)
    {
        SkillLevelRequest requestData = new SkillLevelRequest
        {
            object_id = objectId,
            skill_name = skillName,
            new_level = newLevel.ToString()
        };

        StartCoroutine(SendSkillRequest(requestData));
    }

    private IEnumerator SendSkillRequest(SkillLevelRequest requestData)
    {
        string jsonBody = JsonConvert.SerializeObject(requestData);

        UnityWebRequest request =
            new UnityWebRequest(SkillApiUrl, "POST");

        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();

        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
        request.SetRequestHeader(
            "X-Refresh-Token",
            $"Bearer {refreshToken}"
        );

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Skill API Error: " + request.error);
            Debug.LogError(request.downloadHandler.text);
            yield break;
        }

        string responseJson = request.downloadHandler.text;
        Debug.Log("Skill API Response: " + responseJson);

        SkillLevelResponse response =
            JsonConvert.DeserializeObject<SkillLevelResponse>(responseJson);

        // OPTIONAL: Use response data
        Debug.Log($"Skill '{response.skill_name}' updated to {response.new_level}");
    }
}
