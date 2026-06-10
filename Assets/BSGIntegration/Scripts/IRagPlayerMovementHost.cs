using UnityEngine;

/// <summary>
/// Implemented by <see cref="PlayerMovementInputProcessor"/> on the Photon player so
/// <see cref="RagSequenceAgentMover"/> can drive movement without a circular asmdef reference.
/// </summary>
public interface IRagPlayerMovementHost
{
    void SetRagAutopilot(bool active, int zoneIndex = 0);
    void SetRagVirtualJoystick(Vector2 axes);
    void ApplyRagMovementDelta(Vector3 delta);
    void ApplyRagGroundMovementDelta(Vector3 delta);
}
