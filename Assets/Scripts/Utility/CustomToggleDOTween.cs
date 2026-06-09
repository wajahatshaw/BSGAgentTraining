using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using DG.Tweening;
using System;

[RequireComponent(typeof(Button))]
public class CustomToggleDOTween : MonoBehaviour
{
    [Header("State")]
    [SerializeField] private bool _isOn;

    public bool IsOn
    {
        get => _isOn;
        set => SetState(value, true);
    }


    [Header("UI References")]
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image handleImage;
    [SerializeField] private RectTransform handleRect;

    [Header("Sprites")]
    [SerializeField] private Sprite onSprite;
    [SerializeField] private Sprite offSprite;

    [Header("Handle Positions")]
    [SerializeField] private float handleXOn = 40f;
    [SerializeField] private float handleXOff = -40f;

    [Header("Animation")]
    [SerializeField] private float tweenDuration = 0.25f;
    [SerializeField] private Ease tweenEase = Ease.OutQuad;

    [Header("Events")]
    public UnityEvent<bool> OnToggleValueChanged;

    private Button button;

    void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(Toggle);

        ApplyStateInstant();
    }

    /// <summary>
    /// Toggle state when button is pressed
    /// </summary>
    public void Toggle()
    {
        SetState(!_isOn);
    }


    /// <summary>
    /// Set toggle state manually
    /// </summary>
    public void SetState(bool value, bool animate = true)
    {
        if (_isOn == value)
            return; // no unnecessary animation

        _isOn = value;

        handleRect.DOKill();

        backgroundImage.sprite = _isOn ? onSprite : offSprite;

        float targetX = _isOn ? handleXOn : handleXOff;

        if (animate)
        {
            handleRect
                .DOAnchorPosX(targetX, tweenDuration)
                .SetEase(tweenEase);
        }
        else
        {
            handleRect.anchoredPosition =
                new Vector2(targetX, handleRect.anchoredPosition.y);
        }

        OnToggleValueChanged?.Invoke(_isOn);
    }


    /// <summary>
    /// Apply state without animation (startup)
    /// </summary>
    private void ApplyStateInstant()
    {
        backgroundImage.sprite = IsOn ? onSprite : offSprite;

        float x = IsOn ? handleXOn : handleXOff;
        handleRect.anchoredPosition =
            new Vector2(x, handleRect.anchoredPosition.y);
    }

    /// <summary>
    /// Get current toggle value
    /// </summary>
    public bool GetValue()
    {
        return IsOn;
    }
}
