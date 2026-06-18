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
/// Behavior name: <c>YBotWalker</c> — must match the YAML behaviors: key.
/// </summary>
public static class YBotLocomotionInstaller
{
    // Reuses the existing PhysicalAgentZone0 behavior slot so it trains via the project's
    // worker_training_config_fixed.yaml pipeline. Must match that YAML's behaviors: key.
    public const string BehaviorName = "PhysicalAgentZone0";
    public const string SkeletonName = "Y Bot";
    public const int DecisionPeriod = 5;

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

        YBotWalkerAgent existing = host.GetComponent<YBotWalkerAgent>();
        if (existing != null)
            return existing; // already installed

        // 1. Build the ArticulationBody rig.
        YBotLocomotionRig rig = host.GetComponent<YBotLocomotionRig>();
        if (rig == null) rig = host.AddComponent<YBotLocomotionRig>();
        if (!rig.Build(skeleton))
            return null;

        // 2. Foot contacts.
        YBotFootContact left = AttachFootContact(skeleton, "LeftFoot");
        YBotFootContact right = AttachFootContact(skeleton, "RightFoot");

        // 3. BehaviorParameters — sized from the rig BEFORE the Agent initializes.
        int actionSize = rig.TotalDof;
        int obsSize = YBotWalkerAgent.ObservationSize(actionSize);

        BehaviorParameters bp = host.GetComponent<BehaviorParameters>();
        if (bp == null) bp = host.AddComponent<BehaviorParameters>();
        bp.BehaviorName = BehaviorName;
        bp.BrainParameters.VectorObservationSize = obsSize;
        bp.BrainParameters.NumStackedVectorObservations = 1;
        bp.BrainParameters.ActionSpec = ActionSpec.MakeContinuous(actionSize);
        // MUST be Default so the agent uses the remote (trainer) policy. If left unset and it isn't
        // Default (e.g. InferenceOnly with no model), OnActionReceived never fires and the agent
        // gets zero actions even with the trainer connected — exactly the "actionsReceived=0" symptom.
        ForceBehaviorTypeDefault(bp);

        // 4. Agent FIRST. DecisionRequester caches GetComponent<Agent>() in its Awake/OnEnable; if it
        //    is added before the Agent exists it holds a NULL agent and never calls RequestDecision()
        //    → no observations are sent, no actions return, OnActionReceived never fires
        //    (communicatorOn=True but actionsReceived stays 0). The rig (step 1) and BP (step 3) are
        //    already in place, so the Agent's Initialize sees a fully configured setup.
        YBotWalkerAgent agent = host.AddComponent<YBotWalkerAgent>();
        agent.Wire(rig, left, right);

        // 5. DecisionRequester — added AFTER the Agent so it binds to it and drives decision requests.
        DecisionRequester dr = host.GetComponent<DecisionRequester>();
        if (dr == null) dr = host.AddComponent<DecisionRequester>();
        dr.DecisionPeriod = DecisionPeriod;
        dr.TakeActionsBetweenDecisions = true;

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
