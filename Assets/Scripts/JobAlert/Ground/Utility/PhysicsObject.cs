using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PhysicsObject : MonoBehaviour
{
    public float mass = 1f;
    public float drag = 0.5f;
    public bool enableGravity = true;

    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        ApplyPhysicsSettings();
    }

    private void ApplyPhysicsSettings()
    {
        rb.mass = mass;
        rb.linearDamping = drag;
        rb.useGravity = enableGravity;
    }

    public void ApplyForce(Vector3 force)
    {
        rb.AddForce(force, ForceMode.Impulse);
    }

    public void ApplyTorque(Vector3 torque)
    {
        rb.AddTorque(torque, ForceMode.Impulse);
    }
}
