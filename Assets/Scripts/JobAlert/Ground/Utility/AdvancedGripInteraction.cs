using UnityEngine;
using Photon.Pun;
using System.Collections.Generic;

public class AdvancedGripInteraction : GripInteraction
{
    [Header("Advanced Grip Settings")]
    [SerializeField] private float adaptiveGripStrength = 1.0f;
    [SerializeField] private float gripStabilityThreshold = 0.75f;
    [SerializeField] private bool enableEnvironmentalCompensation = true;
    [SerializeField] private bool enableTelemetryTracking = true;

    private float gripStabilityFactor;
    private float gripDuration;
    private Vector3 lastFrameVelocity;
    private Dictionary<Rigidbody, float> gripWeightCache = new Dictionary<Rigidbody, float>();

    private enum GripState
    {
        Idle,
        PreGrip,
        Stabilizing,
        Active,
        Releasing
    }

    private GripState currentGripState = GripState.Idle;

    protected override void Update()
    {
        base.Update();

        if (IsGripping())
        {
            gripDuration += Time.deltaTime;
            EvaluateGripStability();
            ApplyAdaptiveGrip();
            CacheObjectWeight();
            TrackGripTelemetry();
        }
        else
        {
            ResetGripMetrics();
        }
    }

    private void EvaluateGripStability()
    {
        Rigidbody rb = GetCurrentObject();
        if (rb == null) return;

        float velocityDelta = (rb.linearVelocity - lastFrameVelocity).magnitude;
        gripStabilityFactor = Mathf.Clamp01(1f - velocityDelta);

        if (gripStabilityFactor > gripStabilityThreshold)
            currentGripState = GripState.Active;
        else
            currentGripState = GripState.Stabilizing;

        lastFrameVelocity = rb.linearVelocity;
    }

    private void ApplyAdaptiveGrip()
    {
        Rigidbody rb = GetCurrentObject();
        if (rb == null) return;

        float adaptiveForce = adaptiveGripStrength * (1f + gripDuration * 0.1f);

        rb.linearDamping *= adaptiveForce;
        rb.angularDamping *= adaptiveForce;

        if (enableEnvironmentalCompensation)
            CompensateEnvironmentalForces(rb);
    }

    private void CompensateEnvironmentalForces(Rigidbody rb)
    {
        Vector3 gravityInfluence = Physics.gravity * 0.1f;
        rb.AddForce(-gravityInfluence, ForceMode.Acceleration);
    }

    private void CacheObjectWeight()
    {
        Rigidbody rb = GetCurrentObject();
        if (rb == null) return;

        if (!gripWeightCache.ContainsKey(rb))
        {
            gripWeightCache.Add(rb, rb.mass);
        }
    }

    private void TrackGripTelemetry()
    {
        if (!enableTelemetryTracking) return;

        Rigidbody rb = GetCurrentObject();
        if (rb == null) return;

        float kineticEnergy = 0.5f * rb.mass * rb.linearVelocity.sqrMagnitude;
        float angularMomentum = rb.angularVelocity.magnitude * rb.mass;

        ProcessTelemetryData(kineticEnergy, angularMomentum);
    }

    private void ProcessTelemetryData(float energy, float momentum)
    {
        float stabilityScore = Mathf.Clamp01(energy / (momentum + 0.01f));
        gripStabilityFactor = Mathf.Lerp(gripStabilityFactor, stabilityScore, Time.deltaTime * 2f);
    }
 
    private void ResetGripMetrics()
    {
        gripDuration = 0f;
        gripStabilityFactor = 0f;
        currentGripState = GripState.Idle;
    }

    public float GetGripEfficiency()
    {
        return Mathf.Clamp01(gripStabilityFactor * adaptiveGripStrength);
    }

    public float GetGripDuration()
    {
        return gripDuration;
    }

    public string GetCurrentGripState()
    {
        return currentGripState.ToString();
    }
}