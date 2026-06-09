using System;
using UnityEngine;

public class CountdownTimer : MonoSingleton<CountdownTimer>
{
    [Header("Initial Settings")]
    [SerializeField] private float startTimeSeconds = 120f;

    [Header("Low Time Settings")]
    [SerializeField] private float lowTimeThreshold = 30f;
    [SerializeField] private float lowTimeTickInterval = 5f;

    private float currentTime;
    private bool isRunning;

    private bool lowTimeTriggered;
    private float lowTimeTickTimer;

    // ================================
    // STATIC EVENTS
    // ================================

    // Fired once when time <= threshold
    public static Action OnLowTimeReached;

    // Fired every fixed interval when in low time
    public static Action<float> OnLowTimeTick;

    // Fired when timer ends
    public static Action OnTimerFinished;

    // ================================
    // PUBLIC METHODS
    // ================================

    /// <summary>
    /// Starts the timer with optional override time.
    /// </summary>
    [NaughtyAttributes.Button]
    public void InitiateTimer(float? overrideTimeSeconds = null)
    {
        currentTime = overrideTimeSeconds ?? startTimeSeconds;

        isRunning = true;
        lowTimeTriggered = false;
        lowTimeTickTimer = 0f;
        Debug.Log("Timmer initiated");

        // UI update hook
        UpdateTimerUI(currentTime);
    }

    /// <summary>
    /// Add time to current timer.
    /// </summary>
    public void AddTime(float seconds)
    {
        currentTime += seconds;

        if(!isRunning)
        {
            InitiateTimer(currentTime);
        }
        // Prevent negative overflow if needed
        if (currentTime < 0f)
            currentTime = 0f;

        // UI update hook
        UpdateTimerUI(currentTime);
    }

    /// <summary>
    /// Stop the timer manually.
    /// </summary>
    public void StopTimer()
    {
        isRunning = false;
    }

    // ================================
    // UNITY UPDATE
    // ================================

    private void Update()
    {
        if (!isRunning)
            return;

        if (currentTime <= 0f)
        {
            currentTime = 0f;
            isRunning = false; 

            UpdateTimerUI(currentTime);

            OnTimerFinished?.Invoke();
            return;
        }

        currentTime -= Time.deltaTime;

        UpdateTimerUI(currentTime);

        HandleLowTimeEvents();
    }

    // ================================
    // LOW TIME LOGIC
    // ================================

    private void HandleLowTimeEvents()
    {
        if (currentTime > lowTimeThreshold)
            return;

        if (!lowTimeTriggered)
        {
            lowTimeTriggered = true;
            OnLowTimeReached?.Invoke();
        }

        lowTimeTickTimer += Time.deltaTime;

        if (lowTimeTickTimer >= lowTimeTickInterval)
        {
            lowTimeTickTimer = 0f;
            OnLowTimeTick?.Invoke(currentTime);
        }
    }

    // ================================
    // UI UPDATE METHOD (HOOK POINT)
    // ================================

    /// <summary>
    /// This method is called every frame when time changes.
    /// Implement your UI update logic here.
    /// </summary>
    private void UpdateTimerUI(float timeInSeconds)
    {
        // Convert to MM:SS
        int minutes = Mathf.FloorToInt(timeInSeconds / 60f);
        int seconds = Mathf.FloorToInt(timeInSeconds % 60f);

        string formattedTime = $"{minutes:00}:{seconds:00}";

        // 🔵UPDATE HERE
        UIManager.Instance.UpdateTimerUIdata(formattedTime);
        // timerText.text = formattedTime;

        // For now debug:
        //Debug.Log(formattedTime);
    }

    // ================================
    // HELPER (OPTIONAL PUBLIC ACCESS)
    // ================================

    public float GetRemainingTime()
    {
        return currentTime;
    }

    public bool IsRunning()
    {
        return isRunning;
    }
}
