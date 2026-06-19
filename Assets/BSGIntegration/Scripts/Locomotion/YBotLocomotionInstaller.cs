using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
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
