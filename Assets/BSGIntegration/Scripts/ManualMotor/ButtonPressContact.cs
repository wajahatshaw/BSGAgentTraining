using UnityEngine;

/// <summary>
/// Lightweight, in-assembly press feedback for a station the agent physically presses — modelled on
/// <see cref="RagPhysicalAgentCollisionFx"/> (the multiplayer collision tint). On <see cref="Press"/>
/// the station renderers pulse toward a highlight colour and a non-collider visual child eases down a
/// few millimetres (button depression); <see cref="Release"/> restores. Added on demand by
/// <see cref="KleinFrameExecutor"/> so generated stations need no prior wiring, and it never touches
/// networked transforms or the solid hull collider the agent's arrival/IK depend on.
/// </summary>
[DisallowMultipleComponent]
public class ButtonPressContact : MonoBehaviour
{
    [SerializeField] float pressDepth = 0.012f;   // metres the button visual sinks at full press
    [SerializeField] float ease = 0.05f;          // seconds to blend between released and pressed
    [SerializeField] Color pressTint = new Color(0.3f, 1f, 0.45f, 1f);

    Renderer[] _renderers;
    Color[] _baseColors;
    Transform _depressVisual;     // a child mesh with no solid collider (safe to move)
    Vector3 _depressRestLocal;
    bool _cached;

    float _amount;                // 0..1 eased press amount
    float _target;

    public static ButtonPressContact EnsureOn(GameObject station)
    {
        if (station == null) return null;
        ButtonPressContact c = station.GetComponent<ButtonPressContact>();
        if (c == null) c = station.AddComponent<ButtonPressContact>();
        c.Cache();
        return c;
    }

    void Cache()
    {
        if (_cached) return;

        _renderers = GetComponentsInChildren<Renderer>(true);
        if (_renderers != null && _renderers.Length > 0)
        {
            _baseColors = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer r = _renderers[i];
                if (r != null && r.material != null)
                    _baseColors[i] = r.material.color;
            }

            // Choose a depressible visual: a renderer child that carries no non-trigger collider, so
            // the solid hull (used for the agent's arrival/IK reach) is never moved by the feedback.
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer r = _renderers[i];
                if (r == null) continue;
                Collider col = r.GetComponent<Collider>();
                if (col != null && !col.isTrigger) continue;
                _depressVisual = r.transform;
                _depressRestLocal = _depressVisual.localPosition;
                break;
            }
        }

        _cached = true;
    }

    /// <summary>Engage the press (button depresses + highlights). Idempotent; eases over time.</summary>
    public void Press()
    {
        Cache();
        _target = 1f;
    }

    /// <summary>Release the press (button pops back up + un-highlights). Idempotent.</summary>
    public void Release()
    {
        _target = 0f;
    }

    void Update()
    {
        if (!_cached) return;

        float step = ease > 0.0001f ? Time.deltaTime / ease : 1f;
        _amount = Mathf.MoveTowards(_amount, _target, step);
        ApplyTint(_amount);
        ApplyDepress(_amount);
    }

    void ApplyTint(float amount)
    {
        if (_renderers == null) return;
        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer r = _renderers[i];
            if (r == null || r.material == null) continue;
            Color baseColor = _baseColors != null && i < _baseColors.Length ? _baseColors[i] : r.material.color;
            r.material.color = Color.Lerp(baseColor, pressTint, 0.5f * amount);
        }
    }

    void ApplyDepress(float amount)
    {
        if (_depressVisual == null) return;
        Vector3 localDown = _depressVisual.parent != null
            ? _depressVisual.parent.InverseTransformDirection(Vector3.down).normalized
            : Vector3.down;
        _depressVisual.localPosition = _depressRestLocal + localDown * (pressDepth * amount);
    }
}
