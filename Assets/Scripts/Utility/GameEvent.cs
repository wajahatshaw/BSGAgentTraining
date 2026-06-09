using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName ="GAME/GameEvent")]
public class GameEvent : ScriptableObject
{
   [SerializeField] HashSet<GameEventListener> listeners=new HashSet<GameEventListener>();

    public void Raise(Component sender,object data)
    {
        foreach(var listener in listeners)
        {
            listener.OnEventRaised(sender,data);
        }
    }

    public void RegisterListener(GameEventListener listener)
    {
        listeners.Add(listener);
    }

    public void UnregisterListener(GameEventListener listener)
    {
        listeners.Remove(listener);
    }
    
}
