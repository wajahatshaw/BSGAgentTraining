using Photon.Pun;
using UnityEngine;

public class PunSingletone : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}


public abstract class MonoPunCallbackSingleton<T> : MonoBehaviourPunCallbacks where T : MonoBehaviourPunCallbacks
{
    /************************************************************/
    #region Fields

    [Header("Singleton Settings")]
    [Tooltip("whether Object.DontDestroyOnLoad() is called on this")]
    [SerializeField] private bool dontDestroyOnLoad;
    [Tooltip("whether this GameObject unparents itself")]
    [SerializeField] private bool unparentGameObject;

    #endregion
    /************************************************************/
    #region Properties

    private T _Instance => this as T;
    public static T Instance => Singleton.Get<T>();

    #endregion
    /************************************************************/
    #region Functions

    protected void Awake() 
    {
        if (Singleton.TrySet(_Instance, dontDestroyOnLoad, unparentGameObject))
        {
            MonoSingleton_Awake();
        }
    }
    
    protected void OnDestroy()
    {
        if (Singleton.IsSingleton(_Instance))
        {
            MonoSingleton_OnDestroy();
        }
    }

    protected void OnEnable() 
    {
        if (Singleton.IsSingleton(_Instance))
        {
            MonoSingleton_OnEnable();
        }
    }
    
    protected void OnDisable() 
    {
        if (Singleton.IsSingleton(_Instance))
        {
            MonoSingleton_OnDisable();
        }
    }
    
    protected virtual void MonoSingleton_Awake() {}
    
    protected virtual void MonoSingleton_OnEnable() {}

    protected virtual void MonoSingleton_OnDisable() {}

    protected virtual void MonoSingleton_OnDestroy() {}

    #endregion
    /************************************************************/
}
