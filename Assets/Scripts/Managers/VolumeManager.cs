using UnityEngine;
using UnityEngine.Rendering;

public class VolumeManager : MonoSingleton<VolumeManager>
{
    [Header("Volume")]
    [SerializeField] private Volume globalVolume;

    [Header("Profiles")]
    [SerializeField] private VolumeProfile normalProfile;
    [SerializeField] private VolumeProfile inventoryProfile;
    [SerializeField] private VolumeProfile chatProfile;

    private void Start()
    {
        
        if (globalVolume == null)
            globalVolume = GetComponent<Volume>();
            
        SetNormalMode();
    }

    /// <summary>
    /// Switch to normal gameplay volume
    /// </summary>
    public void SetNormalMode()
    {
        if (globalVolume != null && normalProfile != null)
            globalVolume.profile = normalProfile;
    }

    /// <summary>
    /// Switch to inventory volume
    /// </summary>
    public void SetInventoryMode()
    {
        if (globalVolume != null && inventoryProfile != null)
            globalVolume.profile = inventoryProfile;
    }
}
