using UnityEngine;

/// <summary>
/// Guards LineRenderer.SetPosition — Unity asserts when the GameObject or component is inactive/disabled.
/// </summary>
public static class LineRendererSafe
{
    public static bool CanDraw(LineRenderer lr)
    {
        return lr != null
               && lr.gameObject != null
               && lr.gameObject.activeInHierarchy;
    }

    public static bool TrySetPosition(LineRenderer lr, int index, Vector3 position)
    {
        if (lr == null || lr.gameObject == null)
            return false;

        if (!lr.gameObject.activeInHierarchy)
            return false;

        if (!lr.gameObject.activeSelf)
            lr.gameObject.SetActive(true);

        if (!lr.enabled)
            lr.enabled = true;

        if (!lr.enabled || !lr.gameObject.activeInHierarchy)
            return false;

        lr.SetPosition(index, position);
        return true;
    }
}
