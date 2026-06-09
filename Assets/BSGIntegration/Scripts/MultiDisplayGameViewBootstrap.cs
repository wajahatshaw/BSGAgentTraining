using UnityEngine;

/// <summary>
/// Standalone builds need <see cref="Display.Activate"/> on secondary entries; the editor also benefits
/// when the OS exposes multiple <see cref="Display.displays"/> entries. Without activation,
/// <see cref="Camera.targetDisplay"/> slots (Display 3+) can stay blank (“No cameras rendering”).
/// </summary>
public static class MultiDisplayGameViewBootstrap
{
    static bool s_hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void HookDisplays()
    {
        if (s_hooked) return;
        s_hooked = true;
        Display.onDisplaysUpdated += ActivateAllSecondaryDisplays;
        ActivateAllSecondaryDisplays();
    }

    /// <summary>Call from early scene boot (e.g. ReplicaSceneSetup.Awake) so activation runs before cameras render.</summary>
    public static void Touch() => ActivateAllSecondaryDisplays();

    static void ActivateAllSecondaryDisplays()
    {
        if (Display.displays.Length <= 1)
            return;

        for (int i = 1; i < Display.displays.Length; i++)
        {
            try
            {
                Display.displays[i].Activate();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[MultiDisplayGameViewBootstrap] Display.displays[{i}].Activate failed: {ex.Message}");
            }
        }
    }
}
