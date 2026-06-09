using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class Singleton
{
    /************************************************************/
    #region Functions

    public static T Get<T>(
        bool findObjectOfType = false, 
        bool dontDestroyOnLoad = false, 
        bool unparentGameObject = false) where T : MonoBehaviour
    {
        if (findObjectOfType && SingletonInstance<T>.instance == null) 
        {
            TrySet(Object.FindObjectOfType<T>(), dontDestroyOnLoad);
            Debug.Log($"called Get<{typeof(T)}>() before instance was set; calling FindObjectOfType<{typeof(T)}>");
        }
        return SingletonInstance<T>.instance;
    }

    public static bool TrySet<T>(
        T instance, 
        bool dontDestroyOnLoad = false, 
        bool unparentGameObject = false) where T : MonoBehaviour
    {
        // NOTE: method does not need to be called; BUT if called, FindObjectOfType() is avoided during lazy init
        if (SingletonInstance<T>.instance != null)
        {
            Debug.Log($"{instance.name} called Set<{typeof(T)}>() when singleton already exists");
            if (!IsSingleton(instance)) 
            {
                Debug.Log($"there are two different singleton instances, calling DestroyImmediate for {instance.name}");
                Object.DestroyImmediate(instance.gameObject);
            }
            return false;
        }
        else if (instance != null)
        {
            SingletonInstance<T>.instance = instance;
            if (unparentGameObject) instance.transform.SetParent(null);
            if (dontDestroyOnLoad) Object.DontDestroyOnLoad(instance.gameObject);
            return true;
        }
        else
        {
            Debug.LogError($"called Set<{typeof(T)}>() when given instance is null");
            return false;
        }
    }

    public static bool IsSingleton<T>(T instance) where T : MonoBehaviour
    {
        return ReferenceEquals(SingletonInstance<T>.instance, instance);
    }

    #endregion
    /************************************************************/
    #region Subclasses

    private static class SingletonInstance<T> where T : MonoBehaviour
    {
        public static T instance;
    }

    #endregion
    /************************************************************/
}
public abstract class MonoSingleton<T> : MonoBehaviour where T : MonoBehaviour
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