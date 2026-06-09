using UnityEngine;

/// <summary>
/// Hides mental agents and cognitive stations for ONNX inference scenes (Phase 5).
/// GameObjects stay active for references; renderers and labels are disabled.
/// </summary>
public static class RagInferenceVisuals
{
    public static void ApplyFullInferenceHide()
    {
        int stations = HideCognitiveStationRenderers();
        int mental = HideMentalAgentMeshes();
        int presenters = HideTemporalPresenters();
        int labels = HideCognitiveFloatingLabels();
        int lines = HideAgentDebugLines();
        RagMenuController.EnsureInScene()?.ReassertVisibleMenus();
        Debug.Log($"[RagInferenceVisuals] Hide complete: stations={stations}, mental={mental}, presenters={presenters}, labels={labels}, lines={lines}");
    }

    public static int HideCognitiveStationRenderers()
    {
        int n = 0;
        var toolsRoot = GameObject.Find("JSON_Generated_Tools");
        if (toolsRoot != null)
        {
            foreach (Transform child in toolsRoot.transform)
            {
                if (child == null) continue;
                if (!ShouldHideCognitiveStation(child.gameObject)) continue;
                n += DisableRenderersAndLabels(child.gameObject);
            }
        }

        foreach (var go in Object.FindObjectsOfType<GameObject>(true))
        {
            if (go == null || !ShouldHideCognitiveStation(go)) continue;
            n += DisableRenderersAndLabels(go);
        }

        foreach (var meta in Object.FindObjectsOfType<DeclarativeObjectMetadata>(true))
        {
            if (meta == null || meta.gameObject == null) continue;
            if (!ShouldHideCognitiveStation(meta.gameObject)) continue;
            n += DisableRenderersAndLabels(meta.gameObject);
        }

        return n;
    }

    public static int HideMentalAgentMeshes()
    {
        int n = 0;
        var agentsRoot = GameObject.Find("JSON_Generated_Agents");
        if (agentsRoot != null)
        {
            foreach (Transform child in agentsRoot.transform)
            {
                if (child == null) continue;
                string name = child.name;
                if (!name.StartsWith("Agent_M", System.StringComparison.OrdinalIgnoreCase)) continue;
                n += DisableRenderersAndLabels(child.gameObject);
                var mover = child.GetComponent<RagSequenceAgentMover>();
                if (mover != null && !RagInferenceSceneController.UseInvisibleCognitiveScriptedWalk())
                {
                    mover.enabled = false;
                    mover.inferenceSuppressScriptedCognitive = true;
                }
                var walk = child.GetComponent<HumanWalkAnimation>();
                if (walk != null) walk.enabled = false;
            }
        }

        foreach (var mover in Object.FindObjectsOfType<RagSequenceAgentMover>(true))
        {
            if (mover == null || !mover.isMentalAgent) continue;
            n += DisableRenderersAndLabels(mover.gameObject);
            if (!RagInferenceSceneController.UseInvisibleCognitiveScriptedWalk())
            {
                mover.enabled = false;
                mover.inferenceSuppressScriptedCognitive = true;
            }
            var walk = mover.GetComponent<HumanWalkAnimation>();
            if (walk != null) walk.enabled = false;
        }

        return n;
    }

    static int HideTemporalPresenters()
    {
        int n = 0;
        foreach (var p in Object.FindObjectsOfType<TemporalStationPresenter>(true))
        {
            if (p == null) continue;
            n += DisableRenderersAndLabels(p.gameObject);
            p.enabled = false;
        }
        return n;
    }

    static int HideCognitiveFloatingLabels()
    {
        int n = 0;
        foreach (var tm in Object.FindObjectsOfType<TextMesh>(true))
        {
            if (tm == null) continue;
            string t = tm.text ?? "";
            if (t.IndexOf("Temporal", System.StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Buffer", System.StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("<LEARN>", System.StringComparison.OrdinalIgnoreCase) >= 0
                || t.StartsWith("M", System.StringComparison.Ordinal) && t.Length <= 3)
            {
                tm.gameObject.SetActive(false);
                n++;
            }
        }
        return n;
    }

    static int HideAgentDebugLines()
    {
        int n = 0;
        var agentsRoot = GameObject.Find("JSON_Generated_Agents");
        if (agentsRoot != null)
        {
            foreach (Transform child in agentsRoot.transform)
            {
                if (child == null) continue;
                n += DisableLineRenderers(child.gameObject);
            }
        }

        foreach (var mover in Object.FindObjectsOfType<RagSequenceAgentMover>(true))
        {
            if (mover == null) continue;
            n += DisableLineRenderers(mover.gameObject);
        }

        return n;
    }

    static int DisableLineRenderers(GameObject go)
    {
        if (go == null || RagMenuController.ShouldProtectFromInferenceHide(go))
            return 0;

        int n = 0;
        foreach (var lr in go.GetComponentsInChildren<LineRenderer>(true))
        {
            if (lr == null) continue;
            if (lr.enabled)
            {
                lr.enabled = false;
                n++;
            }
            if (lr.gameObject.activeSelf)
            {
                lr.gameObject.SetActive(false);
                n++;
            }
        }
        return n;
    }

    static int DisableRenderersAndLabels(GameObject go)
    {
        if (RagMenuController.ShouldProtectFromInferenceHide(go))
            return 0;

        int n = 0;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r.enabled) { r.enabled = false; n++; }
        }
        foreach (var tm in go.GetComponentsInChildren<TextMesh>(true))
        {
            if (tm.gameObject.activeSelf) { tm.gameObject.SetActive(false); n++; }
        }
        foreach (var canvas in go.GetComponentsInChildren<Canvas>(true))
        {
            if (canvas.enabled) { canvas.enabled = false; n++; }
        }
        return n;
    }

    static bool ShouldHideCognitiveStation(GameObject go)
    {
        string n = go.name;
        if (n.StartsWith("cognitive_", System.StringComparison.OrdinalIgnoreCase))
            return true;
        if (n.IndexOf("Temporal", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (n.IndexOf("Buffer", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (go.GetComponent<CognitiveStationInteractable>() != null)
            return true;
        var meta = go.GetComponent<DeclarativeObjectMetadata>();
        if (meta != null)
        {
            string thread = meta.semanticThread ?? "";
            if (thread.IndexOf("cognitive", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            string id = meta.objectId ?? n;
            if (id.StartsWith("cognitive_", System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
