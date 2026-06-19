using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// ML-Agents PPO locomotion agent for the designated Y-Bot. Drives the ArticulationBody rig
/// (<see cref="YBotLocomotionRig"/>) toward a target while staying upright. Built and wired at
/// runtime by <see cref="YBotLocomotionInstaller"/>; registers as behavior <c>PhysicalAgentZone0</c>
/// (one policy / one ONNX — see Achitecture.md).
///
/// Observation layout (size = 2*TotalDof + 16, set on BehaviorParameters by the installer):
///   per DOF: jointPosition, jointVelocity        (2 * TotalDof)
///   root up vector (local)                        3
///   root linear velocity (local)                  3
///   root angular velocity (local)                 3
///   height of hips above ground                   1
///   direction to target (local, xz-normalized)    3
///   forward alignment to target                   1
///   foot grounded flags (L, R)                    2
/// Actions: TotalDof continuous, each in [-1,1] mapped to that DOF's joint limit.
/// </summary>
[RequireComponent(typeof(YBotLocomotionRig))]
public class YBotWalkerAgent : Agent
{
    [Header("Target")]
    public Transform target;
    public float targetSpawnRadius = 6f;

    [Header("Episode")]
    [Tooltip("Zone index used to clamp the body inside the zone play area (designated player = 0).")]
    public int zoneIndex = 0;
    [Tooltip("Hips height above ground (m) below which the agent is considered fallen.")]
    public float fallHeight = 0.6f;
    [Tooltip("dot(root.up, worldUp) below which the body counts as tipped over and the episode resets. " +
             "Without this a backward-tipped body keeps its hips above fallHeight and gets stuck, never resetting.")]
    public float minUprightToSurvive = 0.4f;
    [Tooltip("Hips height above ground (m) at a healthy standing pose, used to normalize height obs.")]
    public float standHeight = 1.0f;
    public float reachTargetDistance = 1.0f;

    [Header("Curriculum")]
    [Tooltip("Week 1: stand/balance only — no walk-to-target reward; target stays at spawn.")]
    public bool stabilityOnlyTraining = true;

    [Header("Reward weights")]
    public float uprightWeight = 0.15f;
    public float heightWeight = 0.05f;
    public float footGroundedWeight = 0.02f;
    public float progressWeight = 0f;
    public float aliveBonus = 0.02f;
    public float energyPenalty = 0.0002f;
    public float fallPenalty = 1.0f;
    public float reachReward = 2.0f;

    YBotLocomotionRig _rig;
    YBotFootContact _leftFoot;
    YBotFootContact _rightFoot;
    float _prevTargetDistance;
    bool _ownsTarget;
    float[] _actionBuffer;
    int _actionsReceived;        // >0 confirms the Python trainer is sending actions
    float _nextTrainingLogTime;  // throttle for the clean current-training log

    public static int ObservationSize(int totalDof) => 2 * totalDof + 16;

    public void Wire(YBotLocomotionRig rig, YBotFootContact left, YBotFootContact right)
    {
        _rig = rig;
        _leftFoot = left;
        _rightFoot = right;
    }

    public override void Initialize()
    {
        if (_rig == null) _rig = GetComponent<YBotLocomotionRig>();

        // One-time policy/connection diagnostic — if actionsReceived stays 0 this tells us why.
        var bp = GetComponent<Unity.MLAgents.Policies.BehaviorParameters>();
        bool comms = Academy.IsInitialized && Academy.Instance.IsCommunicatorOn;
        Debug.Log($"[YBotWalker] Initialize — behavior='{(bp != null ? bp.BehaviorName : "?")}', " +
                  $"behaviorType={(bp != null ? bp.BehaviorType.ToString() : "?")} (must be Default for trainer), " +
                  $"obs={(bp != null ? bp.BrainParameters.VectorObservationSize : -1)}, " +
                  $"actions={(bp != null ? bp.BrainParameters.ActionSpec.NumContinuousActions : -1)}, " +
                  $"model={(bp != null && bp.Model != null ? bp.Model.name : "none")}, communicatorOn={comms}");
    }

    public override void OnEpisodeBegin()
    {
        if (_rig == null || !_rig.IsBuilt) return;

        _rig.ResetToSpawn();
        EnsureTarget();
        RandomizeTarget();
        _prevTargetDistance = PlanarDistanceToTarget();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        int expected = 0;
        var bp = GetComponent<BehaviorParameters>();
        if (bp != null)
            expected = bp.BrainParameters.VectorObservationSize;

        int size = _rig != null ? ObservationSize(_rig.TotalDof) : 0;

        // Non-finite physics → emit zeros so ML-Agents keeps returning actions (memory gotcha #6).
        if (_rig == null || !_rig.IsBuilt || !_rig.HasValidPhysicsState())
        {
            for (int i = 0; i < size; i++) sensor.AddObservation(0f);
            if (expected > 0 && expected != size)
                Debug.LogError($"[YBotWalker] Observation mismatch (invalid physics): BehaviorParameters expects {expected}, rig emits {size}.");
            return;
        }

        // Per-DOF joint state.
        foreach (var j in _rig.Joints)
        {
            var ab = j.body;
            int n = ab != null ? ab.dofCount : 0;
            for (int d = 0; d < j.dofCount; d++)
            {
                float pos = (ab != null && d < n) ? ab.jointPosition[d] : 0f;
                float vel = (ab != null && d < n) ? ab.jointVelocity[d] : 0f;
                sensor.AddObservation(Mathf.Clamp(pos, -10f, 10f));
                sensor.AddObservation(Mathf.Clamp(vel, -20f, 20f));
            }
        }

        Transform root = _rig.Root.transform;
        sensor.AddObservation(root.InverseTransformDirection(Vector3.up));               // 3
        sensor.AddObservation(root.InverseTransformDirection(_rig.Root.linearVelocity)); // 3
        sensor.AddObservation(root.InverseTransformDirection(_rig.Root.angularVelocity));// 3

        float groundY = SampleGroundY(root.position);
        sensor.AddObservation(Mathf.Clamp((root.position.y - groundY) / standHeight, -2f, 2f)); // 1

        Vector3 toTarget = PlanarToTarget();
        Vector3 localToTarget = root.InverseTransformDirection(toTarget.normalized);
        sensor.AddObservation(localToTarget); // 3
        sensor.AddObservation(Vector3.Dot(new Vector3(root.forward.x, 0f, root.forward.z).normalized, toTarget.normalized)); // 1

        sensor.AddObservation(_leftFoot != null && _leftFoot.IsGrounded ? 1f : 0f);  // 1
        sensor.AddObservation(_rightFoot != null && _rightFoot.IsGrounded ? 1f : 0f);// 1

        if (expected > 0 && expected != size)
            Debug.LogError($"[YBotWalker] Observation mismatch: BehaviorParameters expects {expected}, CollectObservations emits {size}. Re-enter Play so the Academy re-handshakes.");
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (_rig == null || !_rig.IsBuilt) return;

        _actionsReceived++;

        var cont = actions.ContinuousActions;
        if (_actionBuffer == null || _actionBuffer.Length != _rig.TotalDof)
            _actionBuffer = new float[_rig.TotalDof];
        for (int i = 0; i < _actionBuffer.Length; i++)
            _actionBuffer[i] = i < cont.Length ? cont[i] : 0f;
        _rig.ApplyActions(_actionBuffer);

        // Fall / non-finite handling lives in FixedUpdate so it also runs with no trainer attached.
        if (!_rig.HasValidPhysicsState())
            return;

        Transform root = _rig.Root.transform;
        float groundY = SampleGroundY(root.position);
        float height = root.position.y - groundY;
        if (height < fallHeight)
            return;

        // Upright posture: dot(local up, world up).
        float upright = Vector3.Dot(root.up, Vector3.up);
        AddReward(uprightWeight * Mathf.Clamp01(upright));

        float normHeight = Mathf.Clamp((height - fallHeight) / (standHeight - fallHeight), 0f, 1f);
        AddReward(heightWeight * normHeight);

        bool leftGrounded = _leftFoot != null && _leftFoot.IsGrounded;
        bool rightGrounded = _rightFoot != null && _rightFoot.IsGrounded;
        if (leftGrounded && rightGrounded)
            AddReward(footGroundedWeight);

        float dist = PlanarDistanceToTarget();
        if (!stabilityOnlyTraining && progressWeight > 0f)
        {
            float progress = _prevTargetDistance - dist;
            _prevTargetDistance = dist;
            AddReward(progressWeight * progress);
        }
        else
        {
            _prevTargetDistance = dist;
        }

        AddReward(aliveBonus);
        float effort = 0f;
        for (int i = 0; i < cont.Length; i++) effort += cont[i] * cont[i];
        AddReward(-energyPenalty * effort);

        if (!stabilityOnlyTraining && dist < reachTargetDistance)
        {
            AddReward(reachReward);
            EndEpisode();
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var cont = actionsOut.ContinuousActions;
        for (int i = 0; i < cont.Length; i++) cont[i] = 0f;
    }

    /// <summary>
    /// Runs every physics step regardless of whether a trainer is attached, so the body is kept
    /// inside zone 0 and reset when it falls — even during a no-trainer Play (where OnActionReceived
    /// never fires). Without this the ragdoll drifts off the zone onto a neighbouring ground.
    /// </summary>
    void FixedUpdate()
    {
        if (_rig == null || !_rig.IsBuilt) return;

        ArticulationBody root = _rig.Root;

        // Clean CURRENT-training status (replaces the legacy BSG summary spam). Shows whether the
        // trainer is actually driving the agent and how training is progressing.
        if (Time.time >= _nextTrainingLogTime)
        {
            _nextTrainingLogTime = Time.time + 10f;
            float gY = SampleGroundY(root.transform.position);
            float up = Vector3.Dot(root.transform.up, Vector3.up);
            Debug.Log($"[YBotWalker] TRAINING — completedEpisodes={CompletedEpisodes}, " +
                      $"actionsReceived={_actionsReceived} ({(_actionsReceived > 0 ? "trainer SENDING actions" : "NO actions — trainer not connected")}), " +
                      $"stepThisEpisode={StepCount}, episodeReward={GetCumulativeReward():F2}, " +
                      $"upright={up:F2}, hipHeight={(root.transform.position.y - gY):F2}");
        }

        // Non-finite → reset.
        if (!_rig.HasValidPhysicsState())
        {
            AddReward(-fallPenalty);
            EndEpisode();
            return;
        }

        // Clamp the root inside the zone play area so a fallen/flailing body can't slide off-zone.
        Vector3 pos = root.transform.position;
        Vector3 clamped = ZonePlayAreaBounds.ClampPosition(zoneIndex, pos);
        if ((new Vector2(clamped.x - pos.x, clamped.z - pos.z)).sqrMagnitude > 1e-4f)
        {
            root.TeleportRoot(new Vector3(clamped.x, pos.y, clamped.z), root.transform.rotation);
            root.linearVelocity = Vector3.zero;
            pos = root.transform.position;
        }

        // Fall → terminate + reset (returns the body to its in-zone spawn via OnEpisodeBegin).
        // Two fall conditions: hips dropped too low, OR the body tipped over. The tilt check is
        // essential — a rigid body that tips backward keeps its hips above fallHeight and would
        // otherwise stay stuck forever, so the episode never resets and nothing can be learned.
        float groundY = SampleGroundY(pos);
        float upright = Vector3.Dot(root.transform.up, Vector3.up);
        if (pos.y - groundY < fallHeight || upright < minUprightToSurvive)
        {
            AddReward(-fallPenalty);
            EndEpisode();
        }
    }

    // --- helpers ---

    Vector3 PlanarToTarget()
    {
        if (target == null || _rig == null) return Vector3.forward;
        Vector3 d = target.position - _rig.Root.transform.position;
        d.y = 0f;
        return d;
    }

    float PlanarDistanceToTarget() => PlanarToTarget().magnitude;

    void EnsureTarget()
    {
        if (target != null) return;
        var go = new GameObject("YBotWalkerTarget");
        target = go.transform;
        _ownsTarget = true;
    }

    void RandomizeTarget()
    {
        if (target == null || _rig == null) return;

        if (stabilityOnlyTraining)
        {
            Vector3 standPos = _rig.Root.transform.position;
            standPos.y = SampleGroundY(standPos);
            target.position = standPos;
            return;
        }

        Vector3 origin = _rig.Root.transform.position;
        float ang = Random.value * Mathf.PI * 2f;
        float r = Mathf.Lerp(targetSpawnRadius * 0.4f, targetSpawnRadius, Random.value);
        Vector3 pos = origin + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
        pos = ZonePlayAreaBounds.ClampPosition(zoneIndex, pos); // keep the goal inside the zone
        pos.y = SampleGroundY(pos);
        target.position = pos;
    }

    /// <summary>
    /// Ground Y under a point. For ground-tagged colliders use bounds.max.y (top surface) — raw
    /// hit.point.y can report a box's BOTTOM face and bury the feet (memory gotcha #5).
    /// </summary>
    static float SampleGroundY(Vector3 worldPos)
    {
        Vector3 origin = worldPos + Vector3.up * 5f;
        var hits = Physics.RaycastAll(origin, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore);
        float best = 0f;
        bool found = false;
        foreach (var h in hits)
        {
            if (h.collider.GetComponentInParent<ArticulationBody>() != null) continue; // skip the rig
            float topY = h.collider.bounds.max.y;
            if (!found || topY > best) { best = topY; found = true; }
        }
        return found ? best : 0f;
    }

    void OnDestroy()
    {
        if (_ownsTarget && target != null) Destroy(target.gameObject);
    }
}
