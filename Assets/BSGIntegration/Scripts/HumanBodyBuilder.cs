using UnityEngine;

/// <summary>
/// Builds a realistic human body for Mental Agents (MA/MB/MC) using Unity primitives.
///
/// Joint hierarchy (pivot objects carry rotation; mesh objects hang below as children):
///   root
///   ├── BodyRoot  (slight Y-bob pivot during walk)
///   │   ├── Hips
///   │   ├── LowerTorso
///   │   ├── Torso  (upper chest)
///   │   │   ├── ChestFront
///   │   │   ├── ShoulderL / ShoulderR
///   │   │   ├── LeftArmPivot  ← rotated for arm swing
///   │   │   │   ├── LeftUpperArm  (mesh, offset downward)
///   │   │   │   └── LeftElbowPivot
///   │   │   │       └── LeftForearm + HandL
///   │   │   └── RightArmPivot (mirror)
///   │   ├── Neck
///   │   └── HeadPivot
///   │       ├── Head (sphere)
///   │       ├── Hair
///   │       ├── CapBrim
///   │       ├── CapDome
///   │       ├── CapBadge
///   │       ├── EyeL / EyeR
///   ├── LeftHipPivot   ← rotated for leg swing
///   │   ├── LeftUpperLeg (mesh)
///   │   └── LeftKneePivot
///   │       ├── LeftLowerLeg (mesh)
///   │       └── FootL
///   └── RightHipPivot  (mirror)
///
/// All pivot names are exposed as constants so HumanWalkAnimation can find and rotate them.
/// </summary>
public static class HumanBodyBuilder
{
    // ── Proportions (local units; root scale applied by spawner) ──────────────

    private const float HeadR       = 0.115f;
    private const float NeckR       = 0.055f;
    private const float NeckH       = 0.10f;
    private const float TorsoW      = 0.22f;
    private const float TorsoDepth  = 0.13f;
    private const float UpperTorsoH = 0.32f;
    private const float LowerTorsoH = 0.20f;
    private const float HipR        = 0.14f;
    private const float UpperArmR   = 0.055f;
    private const float UpperArmH   = 0.26f;
    private const float ForearmR    = 0.044f;
    private const float ForearmH    = 0.24f;
    private const float HandH       = 0.10f;
    private const float HandW       = 0.06f;
    private const float UpperLegR   = 0.075f;
    private const float UpperLegH   = 0.36f;
    private const float LowerLegR   = 0.055f;
    private const float LowerLegH   = 0.34f;
    private const float FootH       = 0.06f;
    private const float FootL       = 0.18f;
    private const float FootW       = 0.08f;

    // Landmark Y positions (feet at y = 0)
    private const float FeetY      = FootH * 0.5f;
    private const float KneeY      = FootH + LowerLegH;
    private const float HipY       = KneeY + UpperLegH;
    private const float WaistY     = HipY + LowerTorsoH * 0.5f;
    private const float ShoulderY  = WaistY + LowerTorsoH * 0.5f + UpperTorsoH;
    private const float NeckBaseY  = ShoulderY;
    private const float HeadCY     = NeckBaseY + NeckH + HeadR;

    // Cap dimensions  — dome sits only on the TOP quarter of the head
    private const float CapDomeR   = HeadR * 0.95f;   // slightly smaller than head radius
    private const float CapDomeH   = HeadR * 0.55f;   // half-height: flat-bottom hemisphere
    private const float CapBrimR   = HeadR * 0.90f;   // brim radius (forward projection)
    private const float CapBrimZ   = HeadR * 0.55f;   // how far the brim juts forward

    // Colours
    private static readonly Color SkinColor   = new Color(0.96f, 0.78f, 0.62f, 1f);
    private static readonly Color HairColor   = new Color(0.18f, 0.12f, 0.07f, 1f);
    private static readonly Color LegColor    = new Color(0.18f, 0.18f, 0.22f, 1f);
    private static readonly Color ShoeColor   = new Color(0.15f, 0.10f, 0.08f, 1f);
    private static readonly Color CapColor    = new Color(0.08f, 0.08f, 0.08f, 1f);  // dark cap
    private static readonly Color BadgeColor  = new Color(0.9f,  0.7f,  0.1f,  1f);  // gold badge

    // ── Pivot bone names (used by HumanWalkAnimation) ────────────────────────
    public const string BodyRootBone    = "BodyRoot";
    public const string LeftHipPivot    = "LeftHipPivot";
    public const string RightHipPivot   = "RightHipPivot";
    public const string LeftKneePivot   = "LeftKneePivot";
    public const string RightKneePivot  = "RightKneePivot";
    public const string LeftArmPivot    = "LeftArmPivot";
    public const string RightArmPivot   = "RightArmPivot";
    public const string LeftElbowPivot  = "LeftElbowPivot";
    public const string RightElbowPivot = "RightElbowPivot";
    public const string TorsoBone       = "Torso";
    public const string HeadBone        = "Head";
    public const string RightIndexJoint1 = "RightIndexJoint1";
    public const string RightIndexJoint2 = "RightIndexJoint2";
    public const string RightIndexJoint3 = "RightIndexJoint3";
    public const string RightIndexTip    = "RightIndexTip";

    // Legacy names kept so MentalAgentController.cs thread-line lookup still works
    public const string LeftUpperLeg    = "LeftUpperLeg";
    public const string RightUpperLeg   = "RightUpperLeg";
    public const string LeftLowerLeg    = "LeftLowerLeg";
    public const string RightLowerLeg   = "RightLowerLeg";
    public const string LeftUpperArm    = "LeftUpperArm";
    public const string RightUpperArm   = "RightUpperArm";
    public const string LeftForearm     = "LeftForearm";
    public const string RightForearm    = "RightForearm";

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Hides the placeholder capsule mesh on the agent root (collision capsule stays).</summary>
    public static void EnsureBareCapsuleHidden(GameObject root)
    {
        if (root == null) return;
        Renderer rootRend = root.GetComponent<Renderer>();
        if (rootRend != null)
            rootRend.enabled = false;
    }

    public static bool HasHumanoidBody(GameObject root)
    {
        return root != null && root.transform.Find(BodyRootBone) != null;
    }

    public static void BuildBody(GameObject root, Color agentTint)
    {
        EnsureBareCapsuleHidden(root);

        CapsuleCollider rootCol = root.GetComponent<CapsuleCollider>();
        if (rootCol != null)
        {
            float s = Mathf.Max(1f, root.transform.lossyScale.y);
            rootCol.height = Mathf.Max(1.85f, 1.92f * s);
            rootCol.radius = Mathf.Max(0.32f, 0.40f * Mathf.Max(root.transform.lossyScale.x, root.transform.lossyScale.z));
            rootCol.center = new Vector3(0f, rootCol.height * 0.5f, 0f);
        }

        // Shirt follows agentTint strongly so MA / MB / MC can have clearly different colours from spawner.
        Color shirtCol    = Color.Lerp(new Color(0.12f, 0.14f, 0.18f, 1f), agentTint, 0.90f);
        Color trouserCol  = Color.Lerp(LegColor, agentTint * 0.35f, 0.18f);
        Color capCol      = Color.Lerp(CapColor, agentTint * 0.55f, 0.28f);

        // ── BodyRoot pivot (handles vertical bob) ─────────────────────────────
        GameObject bodyRoot = CreatePivot(root, BodyRootBone, new Vector3(0f, 0f, 0f));

        // ── Head group ────────────────────────────────────────────────────────
        BuildHead(bodyRoot, HeadCY, capCol, agentTint);

        // ── Neck ──────────────────────────────────────────────────────────────
        CreateMesh(bodyRoot, "Neck", PrimitiveType.Cylinder,
            new Vector3(NeckR * 2f, NeckH * 0.5f, NeckR * 2f),
            new Vector3(0f, NeckBaseY + NeckH * 0.5f, 0f),
            SkinColor);

        // ── Upper torso ───────────────────────────────────────────────────────
        GameObject torso = CreateMesh(bodyRoot, TorsoBone, PrimitiveType.Cube,
            new Vector3(TorsoW * 2f, UpperTorsoH, TorsoDepth * 2f),
            new Vector3(0f, ShoulderY - UpperTorsoH * 0.5f, 0f),
            shirtCol);
        CreateMesh(bodyRoot, "ChestFront", PrimitiveType.Sphere,
            new Vector3(TorsoW * 1.9f, UpperTorsoH * 0.95f, TorsoDepth * 1.6f),
            new Vector3(0f, ShoulderY - UpperTorsoH * 0.5f, 0f),
            shirtCol);

        // ── Lower torso ───────────────────────────────────────────────────────
        CreateMesh(bodyRoot, "LowerTorso", PrimitiveType.Cube,
            new Vector3(TorsoW * 1.7f, LowerTorsoH, TorsoDepth * 1.8f),
            new Vector3(0f, WaistY, 0f),
            shirtCol);

        // ── Hips ──────────────────────────────────────────────────────────────
        CreateMesh(bodyRoot, "Hips", PrimitiveType.Sphere,
            new Vector3(HipR * 2.4f, HipR * 1.5f, HipR * 2.0f),
            new Vector3(0f, HipY, 0f),
            trouserCol);

        // ── Arms ──────────────────────────────────────────────────────────────
        float shoulderX = TorsoW + UpperArmR * 1.1f;
        BuildArm(bodyRoot, left: true,  shoulderX, ShoulderY, shirtCol);
        BuildArm(bodyRoot, left: false, shoulderX, ShoulderY, shirtCol);

        // ── Legs ──────────────────────────────────────────────────────────────
        float hipOffX = HipR * 0.72f;
        BuildLeg(root, left: true,  hipOffX, HipY, trouserCol);
        BuildLeg(root, left: false, hipOffX, HipY, trouserCol);

        RemoveChildColliders(root);
    }

    public static void SetBodyColor(GameObject root, Color agentTint)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r.gameObject == root || r.material == null) continue;
            r.material.color = Color.Lerp(r.material.color, agentTint, 0.25f);
        }
    }

    // ── Head + Cap ────────────────────────────────────────────────────────────

    private static void BuildHead(GameObject parent, float headCY, Color capCol, Color agentTint)
    {
        // Head sphere
        CreateMesh(parent, HeadBone, PrimitiveType.Sphere,
            new Vector3(HeadR * 2f, HeadR * 2f, HeadR * 1.85f),
            new Vector3(0f, headCY, 0f),
            SkinColor);

        // Ears
        CreateMesh(parent, "EarL", PrimitiveType.Sphere,
            new Vector3(HeadR * 0.28f, HeadR * 0.38f, HeadR * 0.22f),
            new Vector3(-HeadR * 1.0f, headCY, 0f),
            SkinColor);
        CreateMesh(parent, "EarR", PrimitiveType.Sphere,
            new Vector3(HeadR * 0.28f, HeadR * 0.38f, HeadR * 0.22f),
            new Vector3( HeadR * 1.0f, headCY, 0f),
            SkinColor);

        // Eyes
        CreateMesh(parent, "EyeL", PrimitiveType.Sphere,
            Vector3.one * 0.028f,
            new Vector3(-HeadR * 0.38f, headCY + HeadR * 0.10f, HeadR * 0.88f),
            new Color(0.08f, 0.08f, 0.08f));
        CreateMesh(parent, "EyeR", PrimitiveType.Sphere,
            Vector3.one * 0.028f,
            new Vector3( HeadR * 0.38f, headCY + HeadR * 0.10f, HeadR * 0.88f),
            new Color(0.08f, 0.08f, 0.08f));

        // Eyebrows
        CreateMesh(parent, "BrowL", PrimitiveType.Cube,
            new Vector3(HeadR * 0.42f, HeadR * 0.06f, HeadR * 0.06f),
            new Vector3(-HeadR * 0.38f, headCY + HeadR * 0.30f, HeadR * 0.90f),
            HairColor);
        CreateMesh(parent, "BrowR", PrimitiveType.Cube,
            new Vector3(HeadR * 0.42f, HeadR * 0.06f, HeadR * 0.06f),
            new Vector3( HeadR * 0.38f, headCY + HeadR * 0.30f, HeadR * 0.90f),
            HairColor);

        // Nose
        CreateMesh(parent, "Nose", PrimitiveType.Sphere,
            new Vector3(HeadR * 0.18f, HeadR * 0.16f, HeadR * 0.22f),
            new Vector3(0f, headCY - HeadR * 0.10f, HeadR * 0.95f),
            new Color(0.88f, 0.68f, 0.54f));

        // Mouth
        CreateMesh(parent, "Mouth", PrimitiveType.Cube,
            new Vector3(HeadR * 0.48f, HeadR * 0.07f, HeadR * 0.06f),
            new Vector3(0f, headCY - HeadR * 0.40f, HeadR * 0.91f),
            new Color(0.72f, 0.34f, 0.28f));

        // ── Baseball cap ─────────────────────────────────────────────────────
        // The cap sits on the TOP of the head only — face stays fully visible.

        // Cap dome: small hemisphere on top of the head.
        // headCY + HeadR = top of head sphere.  Offset dome centre up by CapDomeH*0.5 so bottom aligns with head top.
        float capBaseY = headCY + HeadR * 0.85f;
        CreateMesh(parent, "CapDome", PrimitiveType.Sphere,
            new Vector3(CapDomeR * 2f, CapDomeH, CapDomeR * 2f),
            new Vector3(0f, capBaseY, 0f),
            capCol);

        // Cap brim: thin flat disc projecting forward from the front of the dome base.
        // Y matches dome base; Z shifted forward so it sticks out beyond the face.
        CreateMesh(parent, "CapBrim", PrimitiveType.Cylinder,
            new Vector3(CapBrimR * 1.5f, 0.008f, CapBrimR),
            new Vector3(0f, capBaseY - CapDomeH * 0.45f, CapBrimZ),
            capCol);

        // Cap band: very thin ring at the base of the dome
        CreateMesh(parent, "CapBand", PrimitiveType.Cylinder,
            new Vector3(CapDomeR * 2.08f, 0.012f, CapDomeR * 2.08f),
            new Vector3(0f, capBaseY - CapDomeH * 0.45f, 0f),
            new Color(capCol.r * 0.55f, capCol.g * 0.55f, capCol.b * 0.55f));

        // Badge on the front of the cap
        CreateMesh(parent, "CapBadge", PrimitiveType.Cube,
            new Vector3(HeadR * 0.22f, HeadR * 0.16f, HeadR * 0.04f),
            new Vector3(0f, capBaseY, HeadR * 0.96f),
            BadgeColor);
    }

    // ── Arm with proper pivot hierarchy ───────────────────────────────────────

    private static void BuildArm(GameObject parent, bool left, float shoulderX, float shoulderY, Color shirtCol)
    {
        float sign = left ? -1f : 1f;
        string sideSuffix = left ? "L" : "R";
        string armPivotName  = left ? LeftArmPivot   : RightArmPivot;
        string elbowPivName  = left ? LeftElbowPivot  : RightElbowPivot;
        string upperArmName  = left ? LeftUpperArm    : RightUpperArm;
        string forearmName   = left ? LeftForearm     : RightForearm;

        // Shoulder joint sphere
        CreateMesh(parent, "Shoulder" + sideSuffix, PrimitiveType.Sphere,
            Vector3.one * UpperArmR * 2.4f,
            new Vector3(sign * (shoulderX - UpperArmR * 0.5f), shoulderY - 0.04f, 0f),
            shirtCol);

        // Arm pivot sits AT the shoulder joint
        GameObject armPivot = CreatePivot(parent, armPivotName,
            new Vector3(sign * shoulderX, shoulderY - 0.04f, 0f));

        // Upper arm mesh hangs DOWN from pivot (offset = half its length)
        CreateMesh(armPivot, upperArmName, PrimitiveType.Cylinder,
            new Vector3(UpperArmR * 2f, UpperArmH * 0.5f, UpperArmR * 2f),
            new Vector3(0f, -UpperArmH * 0.5f, 0f),
            SkinColor);

        // Elbow pivot sits below the upper arm
        GameObject elbowPivot = CreatePivot(armPivot, elbowPivName,
            new Vector3(0f, -UpperArmH, 0f));

        // Forearm mesh hangs down from elbow pivot
        CreateMesh(elbowPivot, forearmName, PrimitiveType.Cylinder,
            new Vector3(ForearmR * 2f, ForearmH * 0.5f, ForearmR * 2f),
            new Vector3(0f, -ForearmH * 0.5f, 0f),
            SkinColor);

        // Hand below forearm. The right hand is slightly larger so finger actions read in the training camera.
        Vector3 handScale = left
            ? new Vector3(HandW, HandH * 0.5f, HandW * 0.7f)
            : new Vector3(HandW * 1.65f, HandH * 0.68f, HandW * 1.25f);
        CreateMesh(elbowPivot, "Hand" + sideSuffix, PrimitiveType.Cube,
            handScale,
            new Vector3(0f, -ForearmH - HandH * 0.4f, 0f),
            SkinColor);

        if (!left)
            BuildRightIndexFinger(elbowPivot);
    }

    private static void BuildRightIndexFinger(GameObject elbowPivot)
    {
        const float segmentLength = 0.105f;
        const float segmentWidth = 0.038f;

        GameObject j1 = CreatePivot(elbowPivot, RightIndexJoint1,
            new Vector3(0f, -ForearmH - HandH * 0.62f, HandW * 0.58f));
        CreateMesh(j1, "RightIndexSegment1", PrimitiveType.Cube,
            new Vector3(segmentWidth, segmentWidth, segmentLength),
            new Vector3(0f, 0f, segmentLength * 0.5f),
            SkinColor);

        GameObject j2 = CreatePivot(j1, RightIndexJoint2, new Vector3(0f, 0f, segmentLength));
        CreateMesh(j2, "RightIndexSegment2", PrimitiveType.Cube,
            new Vector3(segmentWidth * 0.9f, segmentWidth * 0.9f, segmentLength * 0.82f),
            new Vector3(0f, 0f, segmentLength * 0.42f),
            SkinColor);

        GameObject j3 = CreatePivot(j2, RightIndexJoint3, new Vector3(0f, 0f, segmentLength * 0.82f));
        CreateMesh(j3, "RightIndexSegment3", PrimitiveType.Cube,
            new Vector3(segmentWidth * 0.75f, segmentWidth * 0.75f, segmentLength * 0.7f),
            new Vector3(0f, 0f, segmentLength * 0.35f),
            SkinColor);

        CreateMesh(j3, RightIndexTip, PrimitiveType.Sphere,
            Vector3.one * segmentWidth * 1.15f,
            new Vector3(0f, 0f, segmentLength * 0.74f),
            SkinColor);
    }

    // ── Leg with proper pivot hierarchy ───────────────────────────────────────

    private static void BuildLeg(GameObject root, bool left, float hipOffX, float hipY, Color trouserCol)
    {
        float sign = left ? -1f : 1f;
        string hipPivName   = left ? LeftHipPivot   : RightHipPivot;
        string kneePivName  = left ? LeftKneePivot  : RightKneePivot;
        string upperLegName = left ? LeftUpperLeg   : RightUpperLeg;
        string lowerLegName = left ? LeftLowerLeg   : RightLowerLeg;
        string footName     = left ? "FootL"        : "FootR";

        // Hip pivot sits AT the hip joint
        GameObject hipPivot = CreatePivot(root, hipPivName,
            new Vector3(sign * hipOffX, hipY, 0f));

        // Upper leg mesh hangs DOWN from hip pivot
        CreateMesh(hipPivot, upperLegName, PrimitiveType.Cylinder,
            new Vector3(UpperLegR * 2f, UpperLegH * 0.5f, UpperLegR * 2f),
            new Vector3(0f, -UpperLegH * 0.5f, 0f),
            trouserCol);

        // Knee pivot at bottom of upper leg
        GameObject kneePivot = CreatePivot(hipPivot, kneePivName,
            new Vector3(0f, -UpperLegH, 0f));

        // Lower leg hangs from knee
        CreateMesh(kneePivot, lowerLegName, PrimitiveType.Cylinder,
            new Vector3(LowerLegR * 2f, LowerLegH * 0.5f, LowerLegR * 2f),
            new Vector3(0f, -LowerLegH * 0.5f, 0f),
            trouserCol);

        // Ankle sphere
        CreateMesh(kneePivot, "Ankle" + (left ? "L" : "R"), PrimitiveType.Sphere,
            Vector3.one * LowerLegR * 2.0f,
            new Vector3(0f, -LowerLegH, 0f),
            trouserCol);

        // Foot
        CreateMesh(kneePivot, footName, PrimitiveType.Cube,
            new Vector3(FootW, FootH, FootL),
            new Vector3(0f, -LowerLegH - FootH * 0.5f, FootL * 0.15f),
            ShoeColor);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates an invisible pivot GameObject used as a rotation anchor.</summary>
    private static GameObject CreatePivot(GameObject parent, string name, Vector3 localPos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;
        return go;
    }

    /// <summary>Creates a visible mesh part with a material.</summary>
    private static GameObject CreateMesh(
        GameObject parent, string partName,
        PrimitiveType shape, Vector3 scale, Vector3 localPos, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(shape);
        go.name = partName;
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale    = scale;

        Renderer r = go.GetComponent<Renderer>();
        if (r != null)
        {
            Shader s = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            r.material = new Material(s) { color = color };
        }

        Collider col = go.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);

        return go;
    }

    private static void RemoveChildColliders(GameObject root)
    {
        foreach (var col in root.GetComponentsInChildren<Collider>(true))
            if (col.gameObject != root) Object.Destroy(col);
    }
}
