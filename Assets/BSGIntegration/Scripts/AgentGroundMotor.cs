using UnityEngine;

/// <summary>
/// Single authority for agent horizontal movement with real physics collision
/// against <see cref="EnvironmentSolidCollider"/> bodies (Wall layer).
/// Uses capsule sweeps + depenetration; never teleports into environment volumes.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public class AgentGroundMotor : MonoBehaviour
{
    [Tooltip("Gap kept between capsule and environment colliders (meters).")]
    public float collisionSkin = 0.07f;

    [Tooltip("Slide iterations when a move hits environment geometry.")]
    [Range(1, 10)] public int maxSlideIterations = 6;

    [Tooltip("Inflates cast radius slightly so contacts happen before visual overlap.")]
    [Range(1f, 1.35f)] public float castRadiusMultiplier = 1.12f;

    [Tooltip("When >= 0, hard-clamps XZ after every move to this zone's play area.")]
    public int clampZoneIndex = -1;

    [Tooltip("When true, capsule sweeps ignore cognitive-station solid hulls (mental band). Used for the designated Photon physical agent.")]
    public bool skipCognitiveStationSolids;

    [Tooltip("When set, capsule sweeps + depenetration ignore every collider under this root, so the agent can stand right against (and overlap) a small interactable it is physically pressing. Cleared when the press ends.")]
    public Transform pressPassThroughRoot;

    /// <summary>0 = moved fully; 1 = move fully blocked by environment collision.</summary>
    public float LastMoveBlockedFraction { get; private set; }

    Rigidbody _rb;
    CapsuleCollider _cap;
    int _environmentMask;
    int _groundMask;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _cap = GetComponent<CapsuleCollider>();
        ScenePhysicsLayers.ApplyCharacterLayer(gameObject);

        _cap.isTrigger = false;
        _rb.useGravity = false;
        _rb.isKinematic = true;
        _rb.freezeRotation = true;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;

        ScenePhysicsLayers.EnsureInitialized();
        _environmentMask = ScenePhysicsLayers.EnvironmentMask;
        _groundMask = ScenePhysicsLayers.GroundMask | (1 << 0);
    }

    public Vector3 WorldPosition => _rb != null ? _rb.position : transform.position;

    /// <summary>Places the capsule feet on the ground raycast hit (RAG floor y≈0).</summary>
    public void SnapFeetToGround()
    {
        float footY = GetCapsuleBottomLocalY();
        Vector3 probe = transform.position + Vector3.up * 3.5f;
        Vector3 p = transform.position;
        if (Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 8f, _groundMask, QueryTriggerInteraction.Ignore))
            p.y = hit.point.y - footY;
        else
            p.y = -footY;

        if (clampZoneIndex >= 0)
            p = ZonePlayAreaBounds.ClampPosition(clampZoneIndex, p);

        _rb.MovePosition(p);
    }

    float GetCapsuleBottomLocalY()
    {
        if (_cap == null) return 0f;
        float sy = transform.lossyScale.y;
        return (_cap.center.y - _cap.height * 0.5f) * sy;
    }

    /// <summary>
    /// Moves on XZ with environment collision. Returns the position actually reached.
    /// </summary>
    public Vector3 TryMoveGround(Vector3 horizontalDelta)
    {
        horizontalDelta.y = 0f;
        if (horizontalDelta.sqrMagnitude < 1e-12f)
        {
            LastMoveBlockedFraction = 0f;
            return WorldPosition;
        }

        Vector3 startPos = WorldPosition;
        Vector3 pos = startPos;
        DepenetrateEnvironment(ref pos);

        Vector3 remainder = horizontalDelta;
        int iterations = Mathf.Clamp(maxSlideIterations, 1, 12);
        float skin = Mathf.Max(0.02f, collisionSkin);

        for (int iter = 0; iter < iterations && remainder.sqrMagnitude > 1e-10f; iter++)
        {
            Vector3 dir = remainder.normalized;
            float distLeft = remainder.magnitude;

            GetCapsuleWorld(pos, out Vector3 p1, out Vector3 p2, out float radius);
            radius *= castRadiusMultiplier;

            if (!Physics.CapsuleCast(p1, p2, radius, dir, out RaycastHit hit, distLeft,
                    _environmentMask, QueryTriggerInteraction.Ignore))
            {
                pos += dir * distLeft;
                remainder = Vector3.zero;
                break;
            }

            if (ShouldIgnoreCollider(hit.collider))
            {
                float advance = Mathf.Min(distLeft, hit.distance + 0.05f);
                pos += dir * advance;
                float skipRemainder = distLeft - advance;
                if (skipRemainder <= 1e-6f)
                {
                    remainder = Vector3.zero;
                    break;
                }

                remainder = dir * skipRemainder;
                continue;
            }

            float allowed = Mathf.Max(0f, hit.distance - skin);
            pos += dir * allowed;

            float rest = distLeft - allowed;
            if (rest < 1e-6f)
                break;

            Vector3 n = hit.normal;
            n.y = 0f;
            if (n.sqrMagnitude < 1e-10f)
                n = -dir;
            n.Normalize();

            remainder = Vector3.ProjectOnPlane(dir * rest, n);
            remainder.y = 0f;
        }

        pos.y = _rb.position.y;
        DepenetrateEnvironment(ref pos);

        if (clampZoneIndex >= 0)
            pos = ZonePlayAreaBounds.ClampPosition(clampZoneIndex, pos);

        Vector3 actualDelta = pos - startPos;
        actualDelta.y = 0f;
        float requested = horizontalDelta.magnitude;
        float moved = actualDelta.magnitude;
        LastMoveBlockedFraction = requested > 1e-6f
            ? Mathf.Clamp01(1f - moved / requested)
            : 0f;

        _rb.MovePosition(pos);
        return pos;
    }

    public bool IsOverlappingEnvironment()
    {
        Vector3 pos = WorldPosition;
        return IsOverlappingEnvironmentAt(pos, out _);
    }

    public bool IsOverlappingEnvironmentAt(Vector3 pos, out Vector3 pushOut)
    {
        pushOut = Vector3.zero;
        GetCapsuleWorld(pos, out Vector3 p1, out Vector3 p2, out float radius);
        radius *= castRadiusMultiplier;

        Collider[] ov = Physics.OverlapCapsule(p1, p2, Mathf.Max(radius - 0.03f, 0.06f),
            _environmentMask, QueryTriggerInteraction.Ignore);

        int hits = 0;
        for (int i = 0; i < ov.Length; i++)
        {
            Collider other = ov[i];
            if (other == null || other.isTrigger || ShouldIgnoreCollider(other))
                continue;

            if (Physics.ComputePenetration(_cap, pos, transform.rotation,
                    other, other.transform.position, other.transform.rotation,
                    out Vector3 dir, out float dist) && dist > 0.001f)
            {
                pushOut += Vector3.ProjectOnPlane(dir * dist, Vector3.up);
                hits++;
            }
        }

        if (hits == 0)
            return false;

        pushOut /= hits;
        return pushOut.sqrMagnitude > 1e-10f;
    }

    void DepenetrateEnvironment(ref Vector3 pos)
    {
        for (int pass = 0; pass < 10; pass++)
        {
            if (!IsOverlappingEnvironmentAt(pos, out Vector3 correction))
                break;

            pos += correction;
            pos.y = _rb.position.y;
        }
    }

    void GetCapsuleWorld(Vector3 rootPos, out Vector3 p1, out Vector3 p2, out float radius)
    {
        Vector3 delta = rootPos - transform.position;
        Transform tr = transform;
        CapsuleCollider c = _cap;

        Vector3 scale = tr.lossyScale;
        radius = c.radius * Mathf.Max(scale.x, scale.z);
        Vector3 center = tr.TransformPoint(c.center) + delta;

        float extent = Mathf.Max(0.05f, c.height * scale.y * 0.5f - radius);
        Vector3 axis = c.direction == 0 ? tr.right : (c.direction == 2 ? tr.forward : tr.up);
        axis.Normalize();

        p1 = center - axis * extent;
        p2 = center + axis * extent;
    }

    bool IsSelf(Collider c)
    {
        return c != null && c.transform.root == transform.root;
    }

    bool ShouldIgnoreCollider(Collider c)
    {
        if (c == null || IsSelf(c))
            return true;

        // Let the designated agent walk right up to (and overlap) the small interactable it presses,
        // so its oversized capsule does not hold the body out of arm's reach of the contact point.
        if (pressPassThroughRoot != null && c.transform.IsChildOf(pressPassThroughRoot))
            return true;

        if (!skipCognitiveStationSolids)
            return false;

        if (c.GetComponentInParent<CognitiveStationInteractable>() != null)
            return true;

        Transform t = c.transform;
        return t != null && t.name == "CognitiveNavObstacle";
    }
}
