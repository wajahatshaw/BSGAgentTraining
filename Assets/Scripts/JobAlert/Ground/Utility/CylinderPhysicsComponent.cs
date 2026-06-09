 using UnityEngine;

public class CylinderPhysicsComponent : MonoBehaviour
{
    [Header("Physical Properties")]
    [SerializeField] private float mass = 2f;
    [SerializeField] private float radius = 0.4f;
    [SerializeField] private float height = 1.2f;
    [SerializeField] private bool useGravity = true;

     [Header("Drag Settings")]
    [SerializeField] private float linearDrag = 0.15f;
    [SerializeField] private float angularDrag = 0.1f;
    [SerializeField] private float uprightStabilization = 2f;
    [Header("Rolling & Tipping")]
    [SerializeField] private float rollingResistance = 0.97f;
    [SerializeField] private float maxAngularVelocity = 18f;
    [SerializeField] private float tipSensitivity = 1.2f;

    [Header("Surface Interaction")]
    [SerializeField] private float surfaceFrictionMultiplier = 1f;
    [SerializeField] private float impactForceMultiplier = 1.1f;
     private Rigidbody rb;
    private CapsuleCollider capsule;
    private Vector3 initialUp;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        initialUp = transform.up;

        InitializePhysics();
    }
    private void InitializePhysics()
    {
        rb.mass = mass;
        rb.linearDamping = linearDrag;

        rb.angularDamping = angularDrag;
        rb.useGravity = useGravity;
        rb.maxAngularVelocity = maxAngularVelocity;

        capsule.radius = radius;
        capsule.height = height;
        capsule.direction = 1;
    }

    private void FixedUpdate()
    {
        ApplyRollingResistance();
        ApplyUprightStabilization();
    }

    private void ApplyRollingResistance()
    {
        rb.linearVelocity *= rollingResistance;
        rb.angularVelocity *= rollingResistance;
    }
    private void ApplyUprightStabilization()
    {
        Vector3 currentUp = transform.up;
        Vector3 torqueDir = Vector3.Cross(currentUp, initialUp);

        rb.AddTorque(torqueDir * uprightStabilization, ForceMode.Acceleration);
    }

    public void ApplyImpulse(Vector3 direction, float force)
    {
        rb.AddForce(direction.normalized * force, ForceMode.Impulse);
    }
     public void ApplyTorque(Vector3 torque)
    {
        rb.AddTorque(torque, ForceMode.Impulse);
    }

    private void OnCollisionEnter(Collision collision)
    {
        Rigidbody otherRb = collision.rigidbody;
        if (otherRb == null) return;

        float impactStrength = collision.relativeVelocity.magnitude;
        Vector3 impactNormal = collision.contacts[0].normal;

        rb.AddForce(
            -impactNormal * impactStrength * impactForceMultiplier,
            ForceMode.Impulse
        );

        rb.AddTorque(
            Vector3.Cross(Vector3.up, impactNormal) * tipSensitivity,
            ForceMode.Impulse
        );
    }

   private void OnCollisionStay(Collision collision)
    {
        rb.linearDamping = linearDrag * surfaceFrictionMultiplier;
    }

    private void OnCollisionExit(Collision collision)
    {
        rb.linearDamping = linearDrag;
    }

     public bool IsUpright(float tolerance = 0.9f)
    {
        return Vector3.Dot(transform.up, initialUp) > tolerance;
    }

    public float GetCurrentSpeed()
    {
        return rb.linearVelocity.magnitude;
    }

    public float GetAngularSpeed()
    {
        return rb.angularVelocity.magnitude;
    }

    public float GetStabilityFactor()
    {
        return Vector3.Dot(transform.up, initialUp);
    }
}
