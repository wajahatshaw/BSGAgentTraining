using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-episode field of PLACEHOLDER obstacles for Y-Bot locomotion training. Gives the obstacle ray
/// sensor + avoidance reward something concrete to learn against BEFORE the real populated office
/// scene exists: a handful of boxes/cylinders on the "Wall" layer, tagged with the frozen obstacle
/// vocabulary (Furniture / Prop / Wall), randomized in position/size/type each episode and biased to
/// sit between the agent and its target so the policy must actually route around them.
///
/// This is SCENE-ONLY, not spec: it changes what colliders exist, never the observation/action
/// shape. So it is fully resume-safe and can be tuned or switched off (see
/// <see cref="YBotWalkerAgent.spawnTrainingPlaceholders"/>) with no retrain. Turn it off once
/// training runs in the real scene that already contains real furniture/stations/walls.
///
/// A pool of primitives is created once and reused (repositioned) each episode to avoid GC churn.
/// </summary>
public class YBotTrainingObstacleField : MonoBehaviour
{
    // The zone ground in the locomotion training scene is flat at y = 0 (ground colliders are
    // stripped during loco prep; SampleGroundY falls back to 0). Placeholders rest their base here.
    const float GroundY = 0f;

    static readonly Dictionary<int, YBotTrainingObstacleField> _byZone =
        new Dictionary<int, YBotTrainingObstacleField>();

    readonly List<GameObject> _pool = new List<GameObject>();

    /// <summary>Gets (or lazily creates) the placeholder field for a zone.</summary>
    public static YBotTrainingObstacleField GetOrCreate(int zoneIndex)
    {
        if (_byZone.TryGetValue(zoneIndex, out var existing) && existing != null)
            return existing;
        var go = new GameObject($"YBotTrainingObstacleField_Zone{zoneIndex}");
        var field = go.AddComponent<YBotTrainingObstacleField>();
        _byZone[zoneIndex] = field;
        return field;
    }

    /// <summary>Hides every placeholder (used when placeholders are disabled or in stability mode).</summary>
    public void Clear()
    {
        foreach (var o in _pool)
            if (o != null) o.SetActive(false);
    }

    /// <summary>
    /// Repositions/re-tags <paramref name="count"/> placeholders for a fresh episode, biased onto the
    /// agent→target corridor, keeping clear of the agent spawn and the goal.
    /// </summary>
    public void Randomize(Vector3 agentOrigin, Transform target, int zoneIndex, int count,
                          float reachTargetDistance)
    {
        if (count < 0) count = 0;
        EnsurePool(count);
        foreach (var o in _pool)
            if (o != null) o.SetActive(false);

        Vector3 targetPos = target != null ? target.position : agentOrigin;
        Vector3 seg = targetPos - agentOrigin; seg.y = 0f;
        float segLen = seg.magnitude;
        Vector3 segDir = segLen > 0.01f ? seg / segLen : Vector3.forward;
        Vector3 lateralDir = Vector3.Cross(segDir, Vector3.up);

        const float clearAgent = 1.3f;               // never spawn on top of the agent
        float clearTarget = reachTargetDistance + 1.0f; // keep the goal approach roughly clear

        for (int i = 0; i < count && i < _pool.Count; i++)
        {
            GameObject o = _pool[i];
            if (o == null) continue;

            ApplyRandomShapeAndTag(o, i);
            float halfExtent = Mathf.Max(o.transform.localScale.x, o.transform.localScale.z) * 0.5f;

            bool placed = false;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                // Bias along the corridor (t in 0.2..0.8) with lateral jitter, so obstacles land in
                // the path rather than behind the agent or the goal.
                float t = segLen > 0.01f ? Random.Range(0.2f, 0.8f) : 0.5f;
                Vector3 onLine = agentOrigin + segDir * (t * segLen);
                Vector3 pos = onLine + lateralDir * Random.Range(-2.2f, 2.2f);
                // Also allow some fully-random placements so the agent doesn't overfit to the corridor.
                if (Random.value < 0.3f)
                    pos = agentOrigin + new Vector3(Random.Range(-5f, 5f), 0f, Random.Range(-5f, 5f));

                pos = ZonePlayAreaBounds.ClampPosition(zoneIndex, pos);
                pos.y = GroundY + o.transform.localScale.y * 0.5f;

                Vector2 flat = new Vector2(pos.x, pos.z);
                if (Vector2.Distance(flat, new Vector2(agentOrigin.x, agentOrigin.z)) < clearAgent + halfExtent)
                    continue;
                if (Vector2.Distance(flat, new Vector2(targetPos.x, targetPos.z)) < clearTarget + halfExtent)
                    continue;

                o.transform.position = pos;
                o.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                o.SetActive(true);
                placed = true;
                break;
            }
            if (!placed) o.SetActive(false); // couldn't find a clear spot this episode — skip it
        }
    }

    void EnsurePool(int count)
    {
        while (_pool.Count < count)
        {
            int idx = _pool.Count;
            // Alternate cube/cylinder at build time (mesh can't change later); size/tag vary per episode.
            var prim = GameObject.CreatePrimitive(idx % 2 == 0 ? PrimitiveType.Cube : PrimitiveType.Cylinder);
            prim.name = $"LocoPlaceholderObstacle_{idx}";
            prim.transform.SetParent(transform, false);
            ScenePhysicsLayers.ApplyEnvironmentLayer(prim); // Wall layer → perceived by the ray sensor
            var rend = prim.GetComponent<Renderer>();
            if (rend != null) rend.material.color = new Color(0.55f, 0.35f, 0.25f); // muted brown
            prim.SetActive(false);
            _pool.Add(prim);
        }
    }

    /// <summary>
    /// Randomize a placeholder's size + obstacle tag. Cubes become Furniture (desk-scale), Prop
    /// (small clutter) or Wall (thin long partition); cylinders are always Prop. Tags come from the
    /// frozen vocabulary in <see cref="ScenePhysicsLayers"/>.
    /// </summary>
    void ApplyRandomShapeAndTag(GameObject o, int idx)
    {
        bool isCube = idx % 2 == 0;
        Vector3 scale;
        string tag;
        if (!isCube)
        {
            // Cylinder prop. Unity cylinders are 2 m tall at scale 1, so y*0.5 gives the half-height.
            float radius = Random.Range(0.3f, 0.5f);
            scale = new Vector3(radius, Random.Range(0.5f, 0.9f), radius);
            tag = ScenePhysicsLayers.TagProp;
        }
        else
        {
            float roll = Random.value;
            if (roll < 0.4f) // desk / workstation-scale furniture
            {
                scale = new Vector3(Random.Range(1.0f, 1.5f), Random.Range(0.7f, 0.9f), Random.Range(0.6f, 0.8f));
                tag = ScenePhysicsLayers.TagFurniture;
            }
            else if (roll < 0.75f) // small clutter prop
            {
                float s = Random.Range(0.3f, 0.8f);
                scale = new Vector3(s, Random.Range(0.4f, 0.9f), s);
                tag = ScenePhysicsLayers.TagProp;
            }
            else // thin long wall / partition
            {
                scale = new Vector3(Random.Range(0.15f, 0.3f), Random.Range(1.2f, 2.0f), Random.Range(2.0f, 4.0f));
                tag = ScenePhysicsLayers.TagWall;
            }
        }
        o.transform.localScale = scale;
        ScenePhysicsLayers.SafeSetTag(o, tag);
    }

    void OnDestroy()
    {
        var stale = new List<int>();
        foreach (var kv in _byZone)
            if (kv.Value == this) stale.Add(kv.Key);
        foreach (var k in stale) _byZone.Remove(k);
    }
}
