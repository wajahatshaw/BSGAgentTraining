using NaughtyAttributes;
using UnityEngine;
using UnityEngine.UI;

public class SliderController : MonoBehaviour
{
    [Header("Slider References")]
    [SerializeField] private Slider slider;
    [SerializeField] private Image fillImage;

    [Header("Values")]
    [SerializeField] private float maxAmount = 100f;
    [SerializeField] private float currentAmount = 0f;

    [Header("Gradient")]
    [SerializeField] bool useGradient = false;
    [ShowIf("useGradient")]
    [SerializeField] private Gradient fillGradient;

    void Awake()
    {
        if (slider == null)
            slider = GetComponent<Slider>();

        slider.minValue = 0f;
        slider.maxValue = maxAmount;

        SetAmount(currentAmount);
    }

    /// <summary>
    /// Set the maximum value of the slider
    /// </summary>
    public void SetMaxAmount(float value)
    {
        maxAmount = Mathf.Max(0.01f, value);
        slider.maxValue = maxAmount;
        SetAmount(currentAmount);
    }

    /// <summary>
    /// Set current value (clamped)
    /// </summary>
    public void SetAmount(float value)
    {
        currentAmount = Mathf.Clamp(value, 0f, maxAmount);
        slider.value = currentAmount;

        UpdateGradient();
    }

    /// <summary>
    /// Add to current value
    /// </summary>
    public void AddAmount(float value)
    {
        SetAmount(currentAmount + value);
    }

    /// <summary>
    /// Reset slider to zero
    /// </summary>
    public void ResetAmount()
    {
        SetAmount(0f);
    }

    private void UpdateGradient()
    {
        if(!useGradient) return;
        if (fillImage == null || fillGradient == null)
            return;

        float normalizedValue = currentAmount / maxAmount;
        fillImage.color = fillGradient.Evaluate(normalizedValue);
    }
}