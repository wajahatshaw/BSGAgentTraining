using UnityEngine;

public class PhysicsInteractor : MonoBehaviour
{
    [SerializeField] private float interactionForce = 5f;

    public void Interact(PhysicsObject physicsObject, Vector3 direction)
    {
        if (physicsObject == null) return;

        Vector3 force = direction.normalized * interactionForce;
        physicsObject.ApplyForce(force);
    }
}
