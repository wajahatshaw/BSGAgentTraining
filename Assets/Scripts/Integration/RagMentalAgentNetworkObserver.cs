using UnityEngine;

/// <summary>
/// Drives walk animation on non-master clients while the mental mover simulation is disabled.
/// Position comes from the master's PhotonTransformView sync.
/// </summary>
public class RagMentalAgentNetworkObserver : MonoBehaviour
{
    const float WalkThreshold = 0.04f;

    HumanWalkAnimation _walkAnim;
    Vector3 _lastPos;

    public static RagMentalAgentNetworkObserver EnsureOn(GameObject mentalAgentRoot)
    {
        if (mentalAgentRoot == null)
            return null;

        RagMentalAgentNetworkObserver observer = mentalAgentRoot.GetComponent<RagMentalAgentNetworkObserver>();
        if (observer == null)
            observer = mentalAgentRoot.AddComponent<RagMentalAgentNetworkObserver>();
        observer.enabled = true;
        return observer;
    }

    void Awake()
    {
        _walkAnim = GetComponent<HumanWalkAnimation>();
        _lastPos = transform.position;
    }

    void OnEnable()
    {
        _lastPos = transform.position;
    }

    void LateUpdate()
    {
        if (_walkAnim == null)
            return;

        Vector3 delta = transform.position - _lastPos;
        delta.y = 0f;
        _lastPos = transform.position;

        if (delta.sqrMagnitude > WalkThreshold * WalkThreshold)
            _walkAnim.StartWalking();
        else
            _walkAnim.StopWalking();
    }
}
