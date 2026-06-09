using UnityEngine;

public class SpherePhysicsComponent : MonoBehaviour
{
    [Header("Physical Properties")]
    [SerializeField] private float mass = 1f;
    [SerializeField] private float radius = 0.5f;
    [SerializeField] private float linearDrag = 0.1f;
    [SerializeField] private float angularDrag = 0.05f;
    [SerializeField] private bool useGravity = true;


    [Header("Rolling Behavior")]
    [SerializeField] private float rollingResistance = 0.98f;
    [SerializeField] private float maxAngularVelocity = 25f;

    [Header("Interaction Response")]
    [SerializeField] private float impactForceMultiplier = 1.2f;
    [SerializeField] private float surfaceFrictionMultiplier = 1f;


    private Rigidbody rb;
    private SphereCollider sphereCollider;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        sphereCollider = GetComponent<SphereCollider>();

        InitializePhysics();
    }
     private void InitializePhysics()
    {
        rb.mass = mass;
        rb.linearDamping = linearDrag;
        rb.angularDamping = angularDrag;
        rb.useGravity = useGravity;
        rb.maxAngularVelocity = maxAngularVelocity;

        sphereCollider.radius = radius;
    }

    private void FixedUpdate()
    {
        ApplyRollingResistance();
    }

    private void ApplyRollingResistance()
    {
        if (rb.linearVelocity.sqrMagnitude < 0.01f)
            return;

        rb.linearVelocity *= rollingResistance;
        rb.angularVelocity *= rollingResistance;
    }

    // Update is called once per frame
    void Update()
    {
        
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

        Vector3 impactDir = collision.contacts[0].normal * -1f;
        float impactStrength = collision.relativeVelocity.magnitude;

        rb.AddForce(
            impactDir * impactStrength * impactForceMultiplier,
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

    public float GetCurrentSpeed()
    {
        return rb.linearVelocity.magnitude;
    }

    public float GetKineticEnergy()
    {
        return 0.5f * mass * rb.linearVelocity.sqrMagnitude;
    }
}

