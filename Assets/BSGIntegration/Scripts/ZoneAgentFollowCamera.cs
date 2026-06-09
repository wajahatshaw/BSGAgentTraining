using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Follow camera: third-person behind the agent, or elevated “drone” tracking (higher, shorter look-ahead, downward gaze).
/// Use a secondary <see cref="Camera.targetDisplay"/> so the main holistic camera stays on Display 1.
/// </summary>
[RequireComponent(typeof(Camera))]
public class ZoneAgentFollowCamera : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("GameObject name (e.g. SIMPLE_Technician_01 or M_M_A_Zone0).")]
    public string agentObjectName = "SIMPLE_Technician_01";

    [Tooltip("If set, takes precedence over dual-focus and name lookup.")]
    public Transform followOverride;

    [Header("M-then-P (use on P Game display only)")]
    [Tooltip("While cognition runs, follow MA/MB/MC for context; if mental and physical move together, prioritize the physical agent so Display 2 shows the action.")]
    public bool useMentalThenPhysicalFocus;

    [Header("RAG M1 / P1 (ml2 scene)")]
    [Tooltip("When set with useMentalThenPhysicalFocus: follow Agent_M1 for context, but prioritize Agent_P1 while it is moving. Ignores MA/MB/MC and MentalAgentSpawner phase.")]
    public bool useRagM1P1Focus;

    [Tooltip("Mental agent root name (SceneGenerator).")]
    public string ragMentalAgentObjectName = "Agent_M1";

    [Tooltip("Physical agent root name (SceneGenerator).")]
    public string ragPhysicalAgentObjectName = "Agent_P1";

    public int cognitiveZoneIndex;

    [Tooltip("XZ speed — physical agent must exceed this to take the view during cognitive phases.")]
    public float physicalFocusMinSpeed = 0.18f;

    [Tooltip("XZ speed — a mental agent above this counts as “moving”; otherwise mental are treated as idle.")]
    public float mentalBusyMinSpeed = 0.14f;

    [Tooltip("Framing when following MA/MB/MC (taller humanoids). Ignored unless useMentalThenPhysicalFocus.")]
    public float mentalHeightAboveFeet = 4.3f;

    public float mentalDistanceBehind = 11f;
    public float mentalLookAhead = 20f;
    public float mentalLookHeightBias = 2.1f;

    [Header("Output display")]
    [Tooltip("0 = Display 1, 1 = Display 2, … (Unity Game view dropdown). Editor names stay 'Display N'; use Corner label for P / MA / MB / MC.")]
    [Range(0, 7)]
    public int targetDisplay = 1;

    [Tooltip("Drawn in this camera only (e.g. P, MA, MB, MC). Leave empty to hide.")]
    public string cornerLabel = "";

    [Header("Framing — behind agent, view toward scene ahead")]
    public float heightAboveAgentFeet = 2.6f;
    public float distanceBehind = 6.5f;
    /// <summary>World point ahead of the agent the camera aims at (gives a clear forward view).</summary>
    public float lookAheadDistance = 14f;
    public float lookHeightBias = 1.3f;
    public float fieldOfView = 66f;
    public float positionSmoothTime = 0.08f;
    public float rotationSlerp = 14f;

    [Header("Drone-style tracking")]
    [Tooltip("Elevated follow: higher rig, slightly farther, aim point closer for a top-down track (shared look for P displays + mental cams).")]
    public bool useDroneTracking;

    [Tooltip("Added on top of framing height when drone mode is on.")]
    public float droneExtraHeight = 5.5f;

    [Tooltip("Extra lift for tall subjects (e.g. mental humanoids); applied when drone + not following the technician named in agentObjectName during dual-focus, or set manually per camera.")]
    public float droneTallSubjectBoost;

    public float droneDistanceFactor = 1.14f;

    [Tooltip("Shorter = camera aims nearer in front of the feet (more downward / drone).")]
    public float droneLookAheadFactor = 0.5f;

    public float droneLookBiasFactor = 0.88f;

    [Header("Front-view mode")]
    [Tooltip("Camera in front of the agent, looking back (face-on view). Overrides behind-agent + look-ahead.")]
    public bool useFrontView;

    [Tooltip("Distance in front of the agent to place the camera.")]
    public float frontViewDistance = 8.5f;

    [Tooltip("Height above agent feet for front view.")]
    public float frontViewHeight = 3.2f;

    [Tooltip("Look at this height on the agent (face level).")]
    public float frontViewLookAtHeight = 1.8f;

    Camera _cam;
    Vector3 _posVel;
    Transform _lastFollowTarget;
    Text _cornerLabelText;
    int _urpRetryFrames;

    void Awake()
    {
        _cam = GetComponent<Camera>();
        _cam.fieldOfView = fieldOfView;
        _cam.farClipPlane = 350f;
        _cam.nearClipPlane = 0.08f;
        _cam.depth = -1f;
        _cam.backgroundColor = new Color(0.06f, 0.06f, 0.12f, 1f);

        if (TryGetComponent<AudioListener>(out var listener))
            Destroy(listener);

        ApplyRoutingAndUrp();

        if (!string.IsNullOrEmpty(cornerLabel))
            BuildCornerLabel(cornerLabel);
    }

    void OnEnable() => ApplyRoutingAndUrp();

    void Start() => ApplyRoutingAndUrp();

    /// <summary>Re-apply <see cref="Camera.targetDisplay"/> after <see cref="Object.Instantiate"/> (clone Awake copies wrong indices).</summary>
    public void ApplyRoutingAndUrp()
    {
        if (_cam == null)
            _cam = GetComponent<Camera>();
        _cam.targetDisplay = targetDisplay;
        _cam.fieldOfView = fieldOfView;
        EnsureUrpBaseCameraForFollowDisplay();
    }

    /// <summary>After duplicating the Display 2 rig, update the overlay text to Phys (etc.).</summary>
    public void SyncCornerLabelUi()
    {
        if (_cornerLabelText != null)
        {
            _cornerLabelText.text = string.IsNullOrEmpty(cornerLabel) ? "" : cornerLabel;
            return;
        }

        if (!string.IsNullOrEmpty(cornerLabel))
            BuildCornerLabel(cornerLabel);
    }

    /// <summary>
    /// Cameras created at runtime in URP often default to <see cref="CameraRenderType.Overlay"/>.
    /// Overlays do not drive a <see cref="Camera.targetDisplay"/> on their own, so the Game view shows
    /// “No cameras rendering” for Display 3+ even though a Camera component exists.
    /// </summary>
    void EnsureUrpBaseCameraForFollowDisplay()
    {
        if (_cam == null) return;
        UniversalAdditionalCameraData urp = _cam.GetUniversalAdditionalCameraData();
        if (urp == null) return;
        urp.renderType = CameraRenderType.Base;
        while (urp.cameraStack.Count > 0)
            urp.cameraStack.RemoveAt(urp.cameraStack.Count - 1);
    }

    void LateUpdate()
    {
        Transform target = followOverride;
        if (target == null)
        {
            if (useMentalThenPhysicalFocus)
                target = ResolveMentalThenPhysicalTarget();
            else if (!string.IsNullOrEmpty(agentObjectName))
            {
                GameObject go = GameObject.Find(agentObjectName);
                if (go != null)
                    target = go.transform;
            }
        }

        if (target == null)
            return;

        if (_urpRetryFrames < 8)
        {
            _urpRetryFrames++;
            ApplyRoutingAndUrp();
        }

        if (target != _lastFollowTarget)
        {
            _lastFollowTarget = target;
            _posVel = Vector3.zero;
        }

        if (useFrontView)
        {
            // Front-view mode: camera in front of agent, looking back at them
            Vector3 flatFwd = new Vector3(target.forward.x, 0f, target.forward.z);
            if (flatFwd.sqrMagnitude < 1e-4f)
                flatFwd = Vector3.forward;
            flatFwd.Normalize();

            Vector3 feet = target.position;
            Vector3 desired = feet + flatFwd * frontViewDistance + Vector3.up * frontViewHeight;
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref _posVel, positionSmoothTime);

            Vector3 look = feet + Vector3.up * frontViewLookAtHeight;
            Quaternion want = Quaternion.LookRotation(look - transform.position, Vector3.up);
            float k = 1f - Mathf.Exp(-rotationSlerp * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, want, k);
        }
        else
        {
            // Standard behind-agent tracking
            bool framingPhysical = !useMentalThenPhysicalFocus || IsPhysicalAgentTransform(target);
            float h = framingPhysical ? heightAboveAgentFeet : mentalHeightAboveFeet;
            float d = framingPhysical ? distanceBehind : mentalDistanceBehind;
            float la = framingPhysical ? lookAheadDistance : mentalLookAhead;
            float lb = framingPhysical ? lookHeightBias : mentalLookHeightBias;

            if (useDroneTracking)
            {
                h += droneExtraHeight;
                bool tallDroneBoost = useMentalThenPhysicalFocus
                    ? !framingPhysical
                    : IsMentalAgentObjectName(agentObjectName);
                if (tallDroneBoost)
                    h += droneTallSubjectBoost;
                d *= droneDistanceFactor;
                la *= droneLookAheadFactor;
                lb *= droneLookBiasFactor;
            }

            if (useMentalThenPhysicalFocus && _cornerLabelText != null)
                _cornerLabelText.text = framingPhysical ? "P" : "M";

            Vector3 flatFwd = new Vector3(target.forward.x, 0f, target.forward.z);
            if (flatFwd.sqrMagnitude < 1e-4f)
                flatFwd = new Vector3(target.TransformDirection(Vector3.forward).x, 0f, target.TransformDirection(Vector3.forward).z);
            if (flatFwd.sqrMagnitude < 1e-4f)
                flatFwd = Vector3.forward;
            flatFwd.Normalize();

            Vector3 feet = target.position;
            Vector3 desired = feet - flatFwd * d + Vector3.up * h;
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref _posVel, positionSmoothTime);

            Vector3 look = feet + flatFwd * la + Vector3.up * lb;
            Quaternion want = Quaternion.LookRotation(look - transform.position, Vector3.up);
            float k = 1f - Mathf.Exp(-rotationSlerp * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, want, k);
        }
    }

    void BuildCornerLabel(string text)
    {
        GameObject root = new GameObject("DisplayNameOverlay");
        root.transform.SetParent(transform, false);

        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = _cam;
        canvas.planeDistance = 0.25f;
        canvas.sortingOrder = 500;

        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        root.AddComponent<GraphicRaycaster>();

        GameObject textGo = new GameObject("Label");
        textGo.transform.SetParent(root.transform, false);

        Text ui = textGo.AddComponent<Text>();
        ui.text = text;
        ui.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        ui.fontSize = 34;
        ui.fontStyle = FontStyle.Bold;
        ui.color = new Color(1f, 1f, 1f, 0.92f);
        ui.alignment = TextAnchor.UpperLeft;
        ui.horizontalOverflow = HorizontalWrapMode.Overflow;
        ui.verticalOverflow = VerticalWrapMode.Overflow;

        RectTransform rt = ui.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(18f, -14f);
        rt.sizeDelta = new Vector2(280f, 56f);
        _cornerLabelText = ui;
    }

    bool IsPhysicalAgentTransform(Transform t)
    {
        return t != null && !string.IsNullOrEmpty(agentObjectName) && t.name == agentObjectName;
    }

    static bool IsMentalAgentObjectName(string objectName)
    {
        if (string.IsNullOrEmpty(objectName)) return false;
        return objectName.StartsWith("M_M_", System.StringComparison.Ordinal);
    }

    Transform ResolveMentalThenPhysicalTarget()
    {
        if (useRagM1P1Focus)
            return ResolveRagM1P1Target();

        GameObject pGo = string.IsNullOrEmpty(agentObjectName) ? null : GameObject.Find(agentObjectName);
        string za = $"M_M_A_Zone{cognitiveZoneIndex}";
        string zb = $"M_M_B_Zone{cognitiveZoneIndex}";
        string zc = $"M_M_C_Zone{cognitiveZoneIndex}";
        GameObject ma = GameObject.Find(za);
        GameObject mb = GameObject.Find(zb);
        GameObject mc = GameObject.Find(zc);

        MentalAgentSpawner spawner = MentalAgentSpawner.ForZone(cognitiveZoneIndex);
        float pSpeed = XzSpeed(pGo != null ? pGo.GetComponent<Rigidbody>() : null);

        if (spawner != null && spawner.phase == "CognitiveProcessComplete" && pGo != null)
            return pGo.transform;

        bool cognitionActive = spawner != null
                               && spawner.phase != "Idle"
                               && spawner.phase != "CognitiveProcessComplete";

        bool awaitingPScan = spawner != null && spawner.phase == "FirstPassComplete_AwaitingPScan";
        if (awaitingPScan && pSpeed >= physicalFocusMinSpeed && pGo != null)
            return pGo.transform;

        float mMax = MaxMentalXzSpeed(ma, mb, mc);
        if (cognitionActive && pSpeed >= physicalFocusMinSpeed && mMax >= mentalBusyMinSpeed && pGo != null)
            return pGo.transform;

        if (cognitionActive && mMax >= mentalBusyMinSpeed)
            return PickBusiestMentalTransform(ma, mb, mc);

        if (cognitionActive && pSpeed >= physicalFocusMinSpeed && pGo != null)
            return pGo.transform;

        if (cognitionActive)
            return PickBusiestMentalTransform(ma, mb, mc) ?? (pGo != null ? pGo.transform : null);

        return pGo != null ? pGo.transform : PickBusiestMentalTransform(ma, mb, mc);
    }

    Transform ResolveRagM1P1Target()
    {
        GameObject m1 = string.IsNullOrEmpty(ragMentalAgentObjectName)
            ? null
            : GameObject.Find(ragMentalAgentObjectName);

        // Dynamic fallback: if the configured name is not found, scan for the first Agent_M* in the hierarchy
        if (m1 == null)
            m1 = FindFirstAgentByPrefix("Agent_M");

        GameObject p1 = string.IsNullOrEmpty(ragPhysicalAgentObjectName)
            ? null
            : GameObject.Find(ragPhysicalAgentObjectName);

        // Dynamic fallback: if the configured name is not found, scan for the first Agent_P* in the hierarchy
        if (p1 == null)
            p1 = FindFirstAgentByPrefix("Agent_P");

        float mSpeed = XzSpeed(m1 != null ? m1.GetComponent<Rigidbody>() : null);
        float pSpeed = XzSpeed(p1 != null ? p1.GetComponent<Rigidbody>() : null);
        bool pMoving = pSpeed >= physicalFocusMinSpeed;
        bool mMoving = mSpeed >= mentalBusyMinSpeed;

        bool cognitiveComplete = IsRagM1CognitiveComplete();
        if (pMoving && (mMoving || !cognitiveComplete))
            return p1 != null ? p1.transform : (m1 != null ? m1.transform : null);

        if (!cognitiveComplete)
            return m1 != null ? m1.transform : (p1 != null ? p1.transform : null);

        return p1 != null ? p1.transform : (m1 != null ? m1.transform : null);
    }

    static GameObject FindFirstAgentByPrefix(string prefix)
    {
        foreach (GameObject go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            GameObject found = SearchChildrenForPrefix(go, prefix);
            if (found != null) return found;
        }
        return null;
    }

    static GameObject SearchChildrenForPrefix(GameObject root, string prefix)
    {
        if (root.name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)) return root;
        for (int i = 0; i < root.transform.childCount; i++)
        {
            GameObject found = SearchChildrenForPrefix(root.transform.GetChild(i).gameObject, prefix);
            if (found != null) return found;
        }
        return null;
    }

    bool IsRagM1CognitiveComplete()
    {
        AgentSequenceManager mgr = AgentSequenceManager.Instance;
        if (mgr == null) return true;

        string leaderId = string.IsNullOrWhiteSpace(RagSceneJsonBridge.LastParsedLeaderAgentId)
            ? "M1"
            : RagSceneJsonBridge.LastParsedLeaderAgentId;

        AgentSequenceData cog = mgr.GetCognitiveSequence(leaderId);
        if (cog == null || cog.actionSequence == null || cog.actionSequence.Count == 0)
            return true;

        for (int i = 0; i < cog.actionSequence.Count; i++)
        {
            if (!cog.actionSequence[i].isStepCompleted)
                return false;
        }

        return true;
    }

    static float XzSpeed(Rigidbody rb)
    {
        if (rb == null) return 0f;
        Vector3 v = rb.linearVelocity;
        return new Vector3(v.x, 0f, v.z).magnitude;
    }

    static float MaxMentalXzSpeed(GameObject ma, GameObject mb, GameObject mc)
    {
        float m = 0f;
        if (ma != null) m = Mathf.Max(m, XzSpeed(ma.GetComponent<Rigidbody>()));
        if (mb != null) m = Mathf.Max(m, XzSpeed(mb.GetComponent<Rigidbody>()));
        if (mc != null) m = Mathf.Max(m, XzSpeed(mc.GetComponent<Rigidbody>()));
        return m;
    }

    static Transform PickBusiestMentalTransform(GameObject ma, GameObject mb, GameObject mc)
    {
        GameObject best = null;
        float bestV = 0f;
        TryBest(ma, ref best, ref bestV);
        TryBest(mb, ref best, ref bestV);
        TryBest(mc, ref best, ref bestV);
        if (best != null) return best.transform;
        if (ma != null) return ma.transform;
        if (mb != null) return mb.transform;
        if (mc != null) return mc.transform;
        return null;

        void TryBest(GameObject go, ref GameObject pick, ref float vmax)
        {
            if (go == null) return;
            float s = XzSpeed(go.GetComponent<Rigidbody>());
            if (s > vmax)
            {
                vmax = s;
                pick = go;
            }
        }
    }
}
