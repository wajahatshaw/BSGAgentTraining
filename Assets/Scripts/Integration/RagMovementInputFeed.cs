using UnityEngine;

/// <summary>
/// Converts RAG navigation deltas into player-local joystick axes for <see cref="PlayerMovement"/>.
/// </summary>
public static class RagMovementInputFeed
{
    /// <summary>
    /// Maps world-space XZ movement delta to local horizontal/vertical axes (same as FixedJoystick).
    /// </summary>
    public static Vector2 DeltaToVirtualJoystick(Vector3 delta, Transform playerTransform)
    {
        if (playerTransform == null || delta.sqrMagnitude < 1e-10f)
            return Vector2.zero;

        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        Vector3 velocity = delta / dt;
        velocity.y = 0f;

        float speed = velocity.magnitude;
        if (speed < 0.001f)
            return Vector2.zero;

        Vector3 direction = velocity / speed;
        float horizontal = Vector3.Dot(direction, playerTransform.right);
        float vertical = Vector3.Dot(direction, playerTransform.forward);
        return Vector2.ClampMagnitude(new Vector2(horizontal, vertical), 1f);
    }
}
