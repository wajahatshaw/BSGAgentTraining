using System.Collections.Generic;
using UnityEngine;

public class CognitiveInteractionHUD : MonoBehaviour
{
    [Header("HUD")]
    public bool showHud = true;
    public Vector2 position = new Vector2(20f, 20f);
    public Vector2 size = new Vector2(520f, 240f);

    private readonly Dictionary<string, CognitiveStationEvent> lastEventByAgent = new Dictionary<string, CognitiveStationEvent>();
    private GUIStyle titleStyle;
    private GUIStyle textStyle;

    void OnEnable()
    {
        CognitiveStationInteractable.StationEvent += OnStationEvent;
    }

    void OnDisable()
    {
        CognitiveStationInteractable.StationEvent -= OnStationEvent;
    }

    void OnStationEvent(CognitiveStationEvent evt)
    {
        if (evt.phase != "enter") return;
        lastEventByAgent[evt.agentId] = evt;
    }

    void OnGUI()
    {
        if (!showHud) return;
        EnsureStyles();

        GUI.Box(new Rect(position.x, position.y, size.x, size.y), "");
        GUILayout.BeginArea(new Rect(position.x + 10, position.y + 8, size.x - 20, size.y - 16));
        GUILayout.Label("Cognitive Data Flow", titleStyle);
        GUILayout.Space(4f);

        if (lastEventByAgent.Count == 0)
        {
            GUILayout.Label("Waiting for station interactions...", textStyle);
        }
        else
        {
            foreach (var kvp in lastEventByAgent)
            {
                var evt = kvp.Value;
                GUILayout.Label($"{kvp.Key} -> {evt.stationId} [{evt.stationAction}] payload={evt.payload}", textStyle);
            }
        }

        if (FindObjectOfType<CognitiveRewardIntegrator>() is CognitiveRewardIntegrator rewards)
        {
            GUILayout.Space(6f);
            GUILayout.Label("Reward Snapshot", titleStyle);
            foreach (var kvp in rewards.RewardByAgent)
            {
                GUILayout.Label($"{kvp.Key}: {kvp.Value:F2}", textStyle);
            }
        }

        GUILayout.EndArea();
    }

    void EnsureStyles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold
        };
        textStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            wordWrap = true
        };
    }
}
