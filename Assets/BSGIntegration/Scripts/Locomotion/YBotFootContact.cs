using UnityEngine;

/// <summary>
/// Ground-contact flag for one foot. Attached to each Foot bone by the locomotion installer.
/// The walker agent reads <see cref="IsGrounded"/> as an observation and for gait rewards.
/// Uses collision enter/stay/exit against the configured ground layer mask; falls back to a
/// short downward geometric probe so it still reports contact if collision messages are missed.
/// </summary>
[RequireComponent(typeof(Collider))]
public class YBotFootContact : MonoBehaviour
{
    [Tooltip("Layers considered 'ground' for contact. Default: everything except this rig.")]
    public LayerMask groundMask = ~0;

    [Tooltip("Geometric probe distance below the foot used as a contact fallback.")]
    public float probeDistance = 0.08f;

    public bool IsGrounded { get; private set; }

    /// <summary>Planar (xz) speed of this foot in m/s. The agent penalizes this while the foot is
    /// grounded so a planted foot doesn't skate/moonwalk, giving a more natural-looking stance.</summary>
    public float HorizontalSpeed
    {
        get
        {
            if (_body == null) return 0f;
            Vector3 v = _body.linearVelocity;
            v.y = 0f;
            return v.magnitude;
        }
    }

    int _contactCount;
    Collider _col;
    ArticulationBody _body;

    void Awake()
    {
        _col = GetComponent<Collider>();
        _body = GetComponent<ArticulationBody>();
        if (_body == null) _body = GetComponentInParent<ArticulationBody>();
    }

    void OnCollisionEnter(Collision c)
    {
        if (IsGround(c)) _contactCount++;
    }

    void OnCollisionExit(Collision c)
    {
        if (IsGround(c)) _contactCount = Mathf.Max(0, _contactCount - 1);
    }

    void FixedUpdate()
    {
        bool fromCollision = _contactCount > 0;
        bool fromProbe = ProbeGround();
        IsGrounded = fromCollision || fromProbe;
    }

    bool ProbeGround()
    {
        if (_col == null) return false;
        Bounds b = _col.bounds;
        Vector3 origin = new Vector3(b.center.x, b.min.y + 0.01f, b.center.z);
        return Physics.Raycast(origin, Vector3.down, probeDistance + 0.01f, groundMask, QueryTriggerInteraction.Ignore);
    }

    /// <summary>Ignore self-collisions: only count colliders that are not part of this rig.</summary>
    bool IsGround(Collision c)
    {
        if (c == null || c.collider == null) return false;
        if (((1 << c.collider.gameObject.layer) & groundMask) == 0) return false;
        // A rig body part carries an ArticulationBody; ground does not.
        return c.collider.GetComponentInParent<ArticulationBody>() == null;
    }
}
