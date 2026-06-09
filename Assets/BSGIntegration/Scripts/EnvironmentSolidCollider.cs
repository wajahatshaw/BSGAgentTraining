using UnityEngine;

/// <summary>
/// Marks a scene object as a solid environment body for agent locomotion.
/// Ensures a non-trigger collider on the environment layer so kinematic agents
/// cannot MovePosition through it.
/// </summary>
[DisallowMultipleComponent]
public class EnvironmentSolidCollider : MonoBehaviour
{
    [Tooltip("Extra meters beyond the agent capsule radius when placing the stand point outside the hull.")]
    public float approachPadding = 0.18f;

    Collider _solid;

    void Awake()
    {
        ConfigureSolidCollider();
    }

    public void ConfigureSolidCollider()
    {
        _solid = GetComponent<Collider>();
        if (_solid == null)
            _solid = gameObject.AddComponent<BoxCollider>();

        _solid.isTrigger = false;
        ScenePhysicsLayers.ApplyEnvironmentLayer(gameObject);
    }

    /// <summary>
    /// Ensures every non-trigger collider under <paramref name="root"/> is on the environment layer.
    /// Adds a bounds-fitted box when none exist (dynamic tools / props).
    /// </summary>
    public static void EnsureOnObject(GameObject root, bool addBoxIfMissing = true)
    {
        if (root == null) return;
        ScenePhysicsLayers.EnsureInitialized();

        bool anySolid = false;
        Collider[] cols = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            Collider c = cols[i];
            if (c == null || c.isTrigger) continue;
            c.isTrigger = false;
            ScenePhysicsLayers.ApplyEnvironmentLayer(c.gameObject);
            anySolid = true;
        }

        if (!anySolid && addBoxIfMissing)
        {
            BoxCollider box = root.GetComponent<BoxCollider>();
            if (box == null)
                box = root.AddComponent<BoxCollider>();

            if (TryFitBoxToRenderers(root, box))
            {
                box.isTrigger = false;
                ScenePhysicsLayers.ApplyEnvironmentLayer(root);
            }
        }

        if (root.GetComponent<EnvironmentSolidCollider>() == null)
            root.AddComponent<EnvironmentSolidCollider>();
    }

    static bool TryFitBoxToRenderers(GameObject root, BoxCollider box)
    {
        Renderer[] rends = root.GetComponentsInChildren<Renderer>(true);
        if (rends == null || rends.Length == 0)
        {
            box.center = Vector3.zero;
            box.size = Vector3.one;
            return true;
        }

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
            b.Encapsulate(rends[i].bounds);

        Transform t = root.transform;
        Vector3 localCenter = t.InverseTransformPoint(b.center);
        Vector3 localExtents = t.InverseTransformVector(b.extents);
        box.center = localCenter;
        box.size = new Vector3(
            Mathf.Max(0.35f, Mathf.Abs(localExtents.x) * 2f),
            Mathf.Max(0.35f, Mathf.Abs(localExtents.y) * 2f),
            Mathf.Max(0.35f, Mathf.Abs(localExtents.z) * 2f));
        return true;
    }

    public static bool TryGetStationSolidCollider(Transform stationRoot, out Collider col)
    {
        col = null;
        if (stationRoot == null) return false;

        Transform nav = stationRoot.Find("CognitiveNavObstacle");
        if (nav == null)
            nav = stationRoot.Find("EnvironmentNavObstacle");
        if (nav != null)
            col = nav.GetComponent<Collider>();

        if (col == null)
        {
            EnvironmentSolidCollider tag = stationRoot.GetComponentInChildren<EnvironmentSolidCollider>(true);
            if (tag != null)
                col = tag.GetComponent<Collider>();
        }

        if (col == null)
            col = stationRoot.GetComponentInChildren<Collider>(false);

        return col != null && !col.isTrigger;
    }

    /// <summary>XZ distance from <paramref name="worldPos"/> to the nearest point on the station solid hull.</summary>
    public static float GetHullDistance(Transform stationRoot, Vector3 worldPos)
    {
        if (!TryGetStationSolidCollider(stationRoot, out Collider col))
            return Vector3.Distance(worldPos, stationRoot != null ? stationRoot.position : worldPos);

        Vector3 closest = col.ClosestPoint(worldPos);
        Vector3 a = worldPos;
        a.y = closest.y;
        return Vector3.Distance(a, closest);
    }

    /// <summary>
    /// Stand position just outside the hull facing <paramref name="fromWorld"/> (tight, realistic interaction range).
    /// </summary>
    public static Vector3 GetApproachPosition(Transform stationRoot, Vector3 fromWorld, float agentRadius, float outwardPad)
    {
        if (stationRoot == null)
            return fromWorld;

        if (!TryGetStationSolidCollider(stationRoot, out Collider col))
            return stationRoot.position;

        Vector3 closest = col.ClosestPoint(fromWorld);
        Vector3 outDir = fromWorld - closest;
        outDir.y = 0f;

        float standOff = Mathf.Max(0.16f, agentRadius + outwardPad);

        if (outDir.sqrMagnitude < 0.04f)
        {
            Vector3 face = stationRoot.forward;
            face.y = 0f;
            if (face.sqrMagnitude < 0.02f)
                face = Vector3.forward;
            outDir = face.normalized;
        }
        else
        {
            outDir.Normalize();
        }

        Vector3 stand = closest + outDir * standOff;
        stand.y = stationRoot.position.y;
        return stand;
    }
}
