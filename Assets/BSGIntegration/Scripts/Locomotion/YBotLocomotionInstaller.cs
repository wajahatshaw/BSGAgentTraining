using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// Runtime installer for the Y-Bot ArticulationBody locomotion stack. Called by
/// PlayerRagPhysicalBridge after the designated player is detached from RAG and forced to unit
/// scale. Builds the AB rig on the "Y Bot" skeleton, attaches foot contacts and the walker agent,
/// and configures BehaviorParameters / DecisionRequester in code (obs &amp; action sizes derived
/// from the rig so they can never drift out of sync).
///
/// Behavior name: <c>PhysicalAgentZone0</c> — sole YAML behavior / single combined ONNX policy.
/// </summary>
public static class YBotLocomotionInstaller
{
    // Reuses the existing PhysicalAgentZone0 behavior slot so it trains via the project's
    // worker_training_config_fixed.yaml pipeline. Must match that YAML's behaviors: key.
    public const string BehaviorName = "PhysicalAgentZone0";
    public const string SkeletonName = "Y Bot";
    public const int DecisionPeriod = 3;

    // --- obstacle-perception ray sensor -------------------------------------------------------
    // The static physical-type tags the agent perceives come from the FROZEN vocabulary in
    // ScenePhysicsLayers (single source of truth; kept in sync with ProjectSettings/TagManager.asset).
    // The set + count are locked in now — adding OR removing a tag changes the observation spec and
    // forces a from-scratch retrain, so every foreseeable category (incl. dormant/reserved slots) is
    // present here even before its objects appear in a scene.
    public static readonly string[] DetectableTags = ScenePhysicsLayers.ObstacleTagVocabulary;
    public const string RaySensorName = "ObstacleRays";
    public const int RaysPerDirection = 5;      // -> 2*5+1 = 11 rays
    public const float MaxRayDegrees = 90f;     // forward 180-degree fan
    public const float RayLength = 18f;         // covers a 40 m zone from most positions
    public const float RaySphereCastRadius = 0.1f;
    public const float RayStartVerticalOffset = 0.9f; // torso height so rays sweep at body level
    public const float RayEndVerticalOffset = 0.6f;
    // Ray obs count = (2*RaysPerDirection+1) * (numTags+2) = 11 * (8+2) = 110 (separate from the 58
    // VectorSensor obs; total policy input = 168). This is the FROZEN spec — see ObstacleTagVocabulary.

    /// <summary>
    /// Installs (idempotently) the locomotion stack on the designated player root. Returns the
    /// agent, or null if the Y-Bot skeleton / Hips could not be found.
    /// </summary>
    public static YBotWalkerAgent Install(GameObject playerRoot)
    {
        if (playerRoot == null)
            return null;

        Transform skeleton = FindSkeleton(playerRoot.transform);
        if (skeleton == null)
        {
            Debug.LogError($"[YBotLocomotionInstaller] '{SkeletonName}' skeleton not found under {playerRoot.name} — cannot build locomotion rig.");
            return null;
        }

        GameObject host = skeleton.gameObject;

        // Player root must not also register PhysicalAgentZone0 (default obs size = 1).
        StripConflictingMlComponentsFromPlayerRoot(playerRoot);

        // 1. Build the ArticulationBody rig.
        YBotLocomotionRig rig = host.GetComponent<YBotLocomotionRig>();
        if (rig == null) rig = host.AddComponent<YBotLocomotionRig>();
        if (!rig.Build(skeleton))
            return null;

        // 2. Foot contacts.
        YBotFootContact left = AttachFootContact(skeleton, "LeftFoot");
        YBotFootContact right = AttachFootContact(skeleton, "RightFoot");

        int actionSize = rig.TotalDof;
        int obsSize = YBotWalkerAgent.ObservationSize(actionSize);

        // 3. BehaviorParameters must register with the Academy using the final obs/action sizes.
        // AddComponent<BehaviorParameters> while active registers the default obs size (1) immediately;
        // recreate while the host is inactive so mlagents-learn sees (58,) not (1,).
        // Agent is added only after BP is configured so it never handshakes with obs=1.
        ConfigureBehaviorParameters(host, obsSize, actionSize);

        YBotWalkerAgent agent = host.GetComponent<YBotWalkerAgent>();
        if (agent == null)
            agent = host.AddComponent<YBotWalkerAgent>();
        agent.Wire(rig, left, right);

        // 4. DecisionRequester — after Agent + BP so it binds correctly.
        DecisionRequester dr = host.GetComponent<DecisionRequester>();
        if (dr == null) dr = host.AddComponent<DecisionRequester>();
        dr.DecisionPeriod = DecisionPeriod;
        dr.TakeActionsBetweenDecisions = true;
        dr.enabled = true;

        // Silence the legacy BSG "TRAINING SUMMARY / Episodes / Skill Progress" console spam — it is
        // a different (cognitive RAG) tracker and its "Episodes: 0" is unrelated to PPO locomotion.
        MLTrainingLogger.SuppressPeriodicSummary = true;

        Debug.Log($"[YBotLocomotionInstaller] {BehaviorName} (Y-Bot locomotion) installed on '{host.name}': obs={obsSize}, continuous actions={actionSize}, DecisionPeriod={DecisionPeriod}.");
        return agent;
    }

    /// <summary>
    /// Force BehaviorType = Default (remote/trainer policy). Uses the public property if writable,
    /// else reflection on the serialized field — the property setter is not public in all ML-Agents
    /// versions, which is why a freshly added component can stay on a non-training BehaviorType.
    /// </summary>
    /// <summary>
    /// Removes RAG ML components from the Photon player root so only the Y-Bot child registers
    /// PhysicalAgentZone0 with the walker observation vector.
    /// </summary>
    public static void StripConflictingMlComponentsFromPlayerRoot(GameObject playerRoot)
    {
        if (playerRoot == null) return;

        DestroyImmediateIfPresent(playerRoot.GetComponent<BSGMLAgent>());
        DestroyImmediateIfPresent(playerRoot.GetComponent<DecisionRequester>());
        DestroyImmediateIfPresent(playerRoot.GetComponent<BehaviorParameters>());
    }

    static void ConfigureBehaviorParameters(GameObject host, int obsSize, int actionSize)
    {
        YBotWalkerAgent agent = host.GetComponent<YBotWalkerAgent>();
        DecisionRequester dr = host.GetComponent<DecisionRequester>();
        if (dr != null) dr.enabled = false;
        if (agent != null) agent.enabled = false;

        bool wasActive = host.activeSelf;
        if (wasActive)
            host.SetActive(false);

        DestroyImmediateIfPresent(host.GetComponent<BehaviorParameters>());

        BehaviorParameters bp = host.AddComponent<BehaviorParameters>();
        ForceBrainParameters(bp, obsSize, ActionSpec.MakeContinuous(actionSize));
        bp.BehaviorName = BehaviorName;
        bp.BrainParameters.NumStackedVectorObservations = 1;
        ForceBehaviorTypeDefault(bp);

        // Obstacle-perception ray sensor. Added while the host is INACTIVE (before the Agent
        // registers with the Academy) so the sensor is enumerated in the initial handshake — the
        // ray obs are a SEPARATE tensor from the 58-vector, so VectorObservationSize stays 58 and
        // the policy's total input becomes 58 + 110 = 168 (11 rays x (8 tags + 2)).
        AddRaySensor(host);

        if (wasActive)
            host.SetActive(true);

        if (agent != null) agent.enabled = true;

        Debug.Log($"[YBotLocomotionInstaller] BehaviorParameters registered: obs={obsSize}, continuousActions={actionSize}, behavior={BehaviorName}.");
    }

    static void ForceBrainParameters(BehaviorParameters bp, int vectorObsSize, ActionSpec actionSpec)
    {
        bp.BrainParameters.VectorObservationSize = vectorObsSize;
        bp.BrainParameters.ActionSpec = actionSpec;

        try
        {
            var brainParamsField = typeof(BehaviorParameters).GetField("m_BrainParameters",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (brainParamsField == null)
                return;

            object brainParams = brainParamsField.GetValue(bp);
            if (brainParams == null)
                return;

            var brainParamsType = brainParams.GetType();
            var vectorObsSizeField = brainParamsType.GetField("m_VectorObservationSize",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            vectorObsSizeField?.SetValue(brainParams, vectorObsSize);

            var actionSpecField = brainParamsType.GetField("m_ActionSpec",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            actionSpecField?.SetValue(brainParams, actionSpec);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[YBotLocomotionInstaller] ForceBrainParameters reflection fallback: {ex.Message}");
        }
    }

    static void DestroyImmediateIfPresent(Component c)
    {
        if (c != null)
            Object.DestroyImmediate(c);
    }

    /// <summary>
    /// Adds (idempotently) the obstacle-perception <see cref="RayPerceptionSensorComponent3D"/> to
    /// the agent host. Detects the static physical-type tags (Wall/Station/Prop/Human) on the
    /// scene's solid layer. Must run while the host is inactive so the sensor is present before the
    /// Agent handshakes with the Academy.
    /// </summary>
    static void AddRaySensor(GameObject host)
    {
        DestroyImmediateIfPresent(host.GetComponent<RayPerceptionSensorComponent3D>());

        RayPerceptionSensorComponent3D rays = host.AddComponent<RayPerceptionSensorComponent3D>();
        rays.SensorName = RaySensorName;
        rays.DetectableTags = new System.Collections.Generic.List<string>(DetectableTags);
        rays.RaysPerDirection = RaysPerDirection;
        rays.MaxRayDegrees = MaxRayDegrees;
        rays.RayLength = RayLength;
        rays.SphereCastRadius = RaySphereCastRadius;
        rays.StartVerticalOffset = RayStartVerticalOffset;
        rays.EndVerticalOffset = RayEndVerticalOffset;

        // Cast ONLY against the solid environment layer ("Wall"), where all static scenery
        // (Wall/Station/Furniture/Prop) lives. The ground colliders are stripped during loco prep,
        // so the floor never triggers a ray.
        //
        // DELIBERATELY NOT the Character layer: the agent's own rig is on Character, and
        // RayPerceptionSensor has no self-exclusion — masking Character in would let the body occlude
        // its own forward rays and corrupt perception. Agent/Human tags therefore stay in the frozen
        // vocabulary (so they cost no retrain to use) but are perceived by the OverlapSphere proximity
        // penalty (which DOES skip the rig via its ArticulationBody check), not by the rays yet. To
        // give the rays agent/human perception later, put the loco rig on its own excluded layer and
        // keep other agents on Character — a resume-safe scene/layer change, never a spec change.
        int wallLayer = LayerMask.NameToLayer(ScenePhysicsLayers.EnvironmentLayerName);
        rays.RayLayerMask = wallLayer >= 0 ? (1 << wallLayer) : Physics.DefaultRaycastLayers;

        Debug.Log($"[YBotLocomotionInstaller] Ray sensor '{RaySensorName}' added: {2 * RaysPerDirection + 1} rays, " +
                  $"{DetectableTags.Length} tags {{{string.Join(",", DetectableTags)}}}, length={RayLength}m, " +
                  $"rayObs={(2 * RaysPerDirection + 1) * (DetectableTags.Length + 2)}, " +
                  $"layer={(wallLayer >= 0 ? ScenePhysicsLayers.EnvironmentLayerName : "Default")}.");
    }

    static void ForceBehaviorTypeDefault(BehaviorParameters bp)
    {
        try
        {
            var prop = typeof(BehaviorParameters).GetProperty("BehaviorType");
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(bp, BehaviorType.Default);
                return;
            }
        }
        catch { /* fall through to field */ }

        var field = typeof(BehaviorParameters).GetField("m_BehaviorType",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(bp, BehaviorType.Default);
    }

    static Transform FindSkeleton(Transform playerRoot)
    {
        foreach (Transform t in playerRoot.GetComponentsInChildren<Transform>(true))
            if (t.name == SkeletonName)
                return t;
        return null;
    }

    static YBotFootContact AttachFootContact(Transform skeleton, string boneSuffix)
    {
        Transform foot = YBotLocomotionRig.FindBone(skeleton, boneSuffix);
        if (foot == null)
        {
            Debug.LogWarning($"[YBotLocomotionInstaller] Foot bone '{boneSuffix}' not found — foot contact skipped.");
            return null;
        }
        YBotFootContact fc = foot.GetComponent<YBotFootContact>();
        if (fc == null) fc = foot.gameObject.AddComponent<YBotFootContact>();
        return fc;
    }
}
