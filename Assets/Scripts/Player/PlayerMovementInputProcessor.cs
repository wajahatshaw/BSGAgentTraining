using UnityEngine;

/// <summary>
/// Merges manual (joystick / keyboard) and RAG navigation into a single move-axis output for
/// <see cref="PlayerMovement"/>. Implements <see cref="IRagPlayerMovementHost"/> so
/// <see cref="RagSequenceAgentMover"/> can drive the designated physical player without
/// touching joystick fields directly.
/// </summary>
[DisallowMultipleComponent]
public class PlayerMovementInputProcessor : MonoBehaviour, IRagPlayerMovementHost
{
    FixedJoystick _joystick;
    bool _manualJoystickDisabled;

    bool _ragAutopilotActive;
    Vector2 _ragMoveAxes;
    int _ragZoneIndex = -1;

    public bool RagAutopilotActive => _ragAutopilotActive;
    public int RagZoneIndex => _ragZoneIndex;

    /// <summary>True while RAG owns movement — manual joystick / keyboard axes are ignored.</summary>
    public bool SuppressesManualMovement => _ragAutopilotActive;

    public FixedJoystick Joystick => _manualJoystickDisabled ? null : _joystick;

    public void BindJoystick(FixedJoystick joystick)
    {
        _joystick = joystick;
    }

    public void DisableManualJoystick()
    {
        _manualJoystickDisabled = true;
        _joystick = null;
    }

    public void EnableManualJoystick(FixedJoystick joystick)
    {
        _manualJoystickDisabled = false;
        _joystick = joystick;
    }

    /// <summary>Unified move axes for PlayerMovement (local X = strafe, Y = forward).</summary>
    public Vector2 GetMoveAxes()
    {
        if (_ragAutopilotActive)
            return _ragMoveAxes;

        if (!_manualJoystickDisabled && _joystick != null)
            return new Vector2(_joystick.Horizontal, _joystick.Vertical);

        return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
    }

    public bool HasJoystickReference => !_manualJoystickDisabled && _joystick != null;

    public void SetRagAutopilot(bool active, int zoneIndex = 0)
    {
        _ragAutopilotActive = active;
        if (active)
            _ragZoneIndex = zoneIndex;
        if (!active)
            _ragMoveAxes = Vector2.zero;
    }

    public void SetRagVirtualJoystick(Vector2 axes)
    {
        _ragMoveAxes = Vector2.ClampMagnitude(axes, 1f);
    }

    public void ApplyRagMovementDelta(Vector3 delta)
    {
        ApplyRagGroundMovementDelta(delta);
    }

    public void ApplyRagGroundMovementDelta(Vector3 delta)
    {
        _ragMoveAxes = RagMovementInputFeed.DeltaToVirtualJoystick(delta, transform);
    }
}
