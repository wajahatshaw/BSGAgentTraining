using UnityEngine;

/// <summary>
/// ML-Agents Python can raise <see cref="Time.timeScale"/> via engine_settings. That also inflates
/// <see cref="Time.fixedDeltaTime"/>, so clamping timeScale alone still leaves physics stepping too fast.
/// This runs at a very late execution order and normalizes both every frame.
/// </summary>
[DefaultExecutionOrder(32000)]
[DisallowMultipleComponent]
public class MlAgentsRealtimeTimeScaleEnforcer : MonoBehaviour
{
    const string HolderName = "_MlAgentsRealtimeTimeScaleEnforcer";
    const float DefaultFixedDelta = 0.02f;

    [Tooltip("When true, Time.timeScale is forced down to maxTimeScale whenever it is higher.")]
    public bool clampingActive = true;

    [Tooltip("Upper bound for Time.timeScale while clamping is active (1 = real-time).")]
    [Min(0.01f)]
    public float maxTimeScale = 1f;

    static MlAgentsRealtimeTimeScaleEnforcer _instance;
    static float _baselineFixedDelta = DefaultFixedDelta;

    public static void EnsureExists(float maxTimeScaleValue = 1f, bool clampingEnabled = true)
    {
        if (_instance != null)
        {
            _instance.maxTimeScale = maxTimeScaleValue;
            _instance.clampingActive = clampingEnabled;
            return;
        }

        GameObject go = GameObject.Find(HolderName);
        if (go == null)
        {
            go = new GameObject(HolderName);
            DontDestroyOnLoad(go);
        }

        _instance = go.GetComponent<MlAgentsRealtimeTimeScaleEnforcer>();
        if (_instance == null)
            _instance = go.AddComponent<MlAgentsRealtimeTimeScaleEnforcer>();

        _instance.maxTimeScale = maxTimeScaleValue;
        _instance.clampingActive = clampingEnabled;
    }

    public static void Configure(float maxTimeScaleValue, bool clampingEnabled)
    {
        EnsureExists(maxTimeScaleValue, clampingEnabled);
    }

    void Awake()
    {
        if (_baselineFixedDelta <= 0f)
            _baselineFixedDelta = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : DefaultFixedDelta;
    }

    void Update() => ApplyClamp();

    void FixedUpdate() => ApplyClamp();

    void LateUpdate() => ApplyClamp();

    void ApplyClamp()
    {
        if (!clampingActive || maxTimeScale <= 0f)
            return;

        if (Time.timeScale > maxTimeScale)
            Time.timeScale = maxTimeScale;

        float expectedFixed = _baselineFixedDelta * Time.timeScale;
        if (Mathf.Abs(Time.fixedDeltaTime - expectedFixed) > 0.0005f)
            Time.fixedDeltaTime = expectedFixed;
    }

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }
}
