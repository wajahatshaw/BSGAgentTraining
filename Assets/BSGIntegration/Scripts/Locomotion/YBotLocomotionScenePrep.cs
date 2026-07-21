using UnityEngine;

/// <summary>
/// Hides RAG scene clutter that should not run while zone-0 AB locomotion training is active.
/// Keeps one behavior (<c>PhysicalAgentZone0</c>) — no parallel RAG physical workflow on P1.
/// </summary>
public static class YBotLocomotionScenePrep
{
    const string ToolNameLabelChild = "ToolNameLabel";

    public static void Apply()
    {
        HideBodyPartSceneEntities();
    }

    static void HideBodyPartSceneEntities()
    {
        int hidden = 0;
        foreach (ToolComponent tool in Object.FindObjectsOfType<ToolComponent>(true))
        {
            if (tool == null || !IsBodyPartTool(tool.gameObject))
                continue;

            tool.gameObject.SetActive(false);
            hidden++;
        }

        if (hidden > 0)
            Debug.Log($"[YBotLocomotionScenePrep] Hidden {hidden} RAG body_part scene object(s) for locomotion training.");
    }

    static bool IsBodyPartTool(GameObject go)
    {
        if (go == null)
            return false;

        string id = go.name ?? string.Empty;
        if (id.IndexOf("scene_019", System.StringComparison.OrdinalIgnoreCase) >= 0
            || id.IndexOf("scene_020", System.StringComparison.OrdinalIgnoreCase) >= 0
            || id.IndexOf("scene_021", System.StringComparison.OrdinalIgnoreCase) >= 0
            || id.IndexOf("torso", System.StringComparison.OrdinalIgnoreCase) >= 0
            || id.IndexOf("fovea", System.StringComparison.OrdinalIgnoreCase) >= 0
            || id.IndexOf("right_index_fingertip_pad", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        ToolComponent tc = go.GetComponent<ToolComponent>();
        if (tc != null && !string.IsNullOrEmpty(tc.toolId))
        {
            string toolId = tc.toolId;
            if (toolId.StartsWith("scene_019", System.StringComparison.OrdinalIgnoreCase)
                || toolId.StartsWith("scene_020", System.StringComparison.OrdinalIgnoreCase)
                || toolId.StartsWith("scene_021", System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        Transform label = go.transform.Find(ToolNameLabelChild);
        if (label != null)
        {
            TextMesh tm = label.GetComponent<TextMesh>();
            string text = tm != null ? tm.text : string.Empty;
            if (text.IndexOf("fovea", System.StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("right_index_fingertip_pad", System.StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("torso", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }
}
