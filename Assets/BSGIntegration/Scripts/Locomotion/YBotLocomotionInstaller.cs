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
        // BehaviorType defaults to Default (0) on a freshly added component — that is what training
        // needs, so we don't set it (the setter is not reliably public in this ML-Agents version).

        // 4. DecisionRequester.
        DecisionRequester dr = host.GetComponent<DecisionRequester>();
        if (dr == null) dr = host.AddComponent<DecisionRequester>();
        dr.DecisionPeriod = DecisionPeriod;
        dr.TakeActionsBetweenDecisions = true;

        // 5. Agent (added last so OnEnable/Initialize sees a fully configured rig + BP).
        YBotWalkerAgent agent = host.AddComponent<YBotWalkerAgent>();
        agent.Wire(rig, left, right);

        Debug.Log($"[YBotLocomotionInstaller] {BehaviorName} (Y-Bot locomotion) installed on '{host.name}': obs={obsSize}, continuous actions={actionSize}, DecisionPeriod={DecisionPeriod}.");
        return agent;
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
