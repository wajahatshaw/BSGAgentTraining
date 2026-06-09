using UnityEngine;

/// <summary>
/// Brief visual feedback when the designated RAG physical agent hits solid environment props.
/// Local-only; safe with Photon (does not affect networked transform).
/// </summary>
public class RagPhysicalAgentCollisionFx : MonoBehaviour
{
    [SerializeField] float blockThreshold = 0.62f;
    [SerializeField] float pulseDuration = 0.14f;
    [SerializeField] Color blockTint = new Color(1f, 0.55f, 0.2f, 1f);

    AgentGroundMotor _motor;
    Renderer[] _renderers;
    Color[] _baseColors;
    float _pulseTimer;

    void Awake()
    {
        _motor = GetComponent<AgentGroundMotor>();
        CacheRenderers();
    }

    void CacheRenderers()
    {
        _renderers = GetComponentsInChildren<Renderer>(true);
        if (_renderers == null || _renderers.Length == 0)
            return;

        _baseColors = new Color[_renderers.Length];
        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer r = _renderers[i];
            if (r != null && r.material != null)
                _baseColors[i] = r.material.color;
        }
    }

    public void NotifyGroundMoveBlocked(float blockedFraction)
    {
        if (blockedFraction < blockThreshold)
            return;

        _pulseTimer = pulseDuration;
    }

    void Update()
    {
        if (_pulseTimer <= 0f)
            return;

        _pulseTimer -= Time.deltaTime;
        float t = Mathf.Clamp01(_pulseTimer / pulseDuration);
        ApplyTint(Color.Lerp(blockTint, Color.white, 1f - t));

        if (_pulseTimer <= 0f)
            RestoreBaseColors();
    }

    void ApplyTint(Color tint)
    {
        if (_renderers == null)
            return;

        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer r = _renderers[i];
            if (r == null || r.material == null)
                continue;

            Color baseColor = _baseColors != null && i < _baseColors.Length
                ? _baseColors[i]
                : r.material.color;
            r.material.color = Color.Lerp(baseColor, tint, 0.55f);
        }
    }

    void RestoreBaseColors()
    {
        if (_renderers == null || _baseColors == null)
            return;

        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer r = _renderers[i];
            if (r != null && r.material != null)
                r.material.color = _baseColors[i];
        }
    }

    public static RagPhysicalAgentCollisionFx EnsureOnAgent(GameObject agentGo)
    {
        if (agentGo == null)
            return null;

        RagPhysicalAgentCollisionFx fx = agentGo.GetComponent<RagPhysicalAgentCollisionFx>();
        if (fx == null)
            fx = agentGo.AddComponent<RagPhysicalAgentCollisionFx>();
        return fx;
    }
}
