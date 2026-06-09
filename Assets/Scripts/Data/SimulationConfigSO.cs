using UnityEngine;
using NaughtyAttributes;

[CreateAssetMenu(
    fileName = "SimulationConfig",
    menuName = "GAME/Simulation Config",
    order = 1)]
public class SimulationConfigSO : ScriptableObject
{
    // ============================
    // AUTH / USER DATA
    // ============================

    [Foldout("Auth / User")]
    public string userId;

    [Foldout("Auth / User")]
    [TextArea(3,8)]
    public string accessToken;

    [Foldout("Auth / User")]
    public string refreshToken;

    [Foldout("Auth / User")]
    public string sessionId;

    [Foldout("Auth / User")]
    public string activityId;

    [Foldout("Auth / User")]
    public string photonRoomName;


    // ============================
    // API ENDPOINTS
    // ============================

    [Foldout("API Endpoints")]
    [Header("Conversation Logs")]
    public string loadConversationLogsApi =
        "https://api.pap-inc.com/performed-activities/{activity_id}/all-conversations";

    [Foldout("API Endpoints")]
    [Header("Store Logs")]
    public string storeLogsApi =
        "https://api.pap-inc.com/logs/store";

    [Foldout("API Endpoints")]
    [Header("Questions")]
    public string generateQuestionsApi =
        "https://api.pap-inc.com/generate-unity-questions";

    [Foldout("API Endpoints")]
    [Header("Skills")]
    public string updateSkillApi =
        "https://api.pap-inc.com/phase-d2/skill-level-by-name";

    [Foldout("API Endpoints")]
    [Header("Simulation Session")]
    public string getSimulationSessionApi =
        "https://api.pap-inc.com/simulation-session/{session_id}";


    // ============================
    // REQUEST SETTINGS
    // ============================

    [Foldout("Request Settings")]
    public int requestTimeoutSeconds = 15;

    [Foldout("Request Settings")]
    public bool enableDebugLogs = true;


    // ============================
    // COMMON HEADERS (REFERENCE)
    // ============================

    [Foldout("Common Headers (Reference Only)")]
    [ReadOnly]
    public string contentTypeHeader = "application/json";

    [Foldout("Common Headers (Reference Only)")]
    [ReadOnly]
    public string authorizationHeaderFormat = "Bearer {access_token}";

    [Foldout("Common Headers (Reference Only)")]
    [ReadOnly]
    public string refreshHeaderFormat = "Bearer {refresh_token}";
}
