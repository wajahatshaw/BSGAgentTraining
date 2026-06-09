using UnityEngine;
using UnityEngine.EventSystems;

public class FixedTouchField : MonoBehaviour , IPointerDownHandler, IPointerUpHandler
{
    public float touchSensitivity = 2f;
    [HideInInspector]
    public Vector2 touchDist;
    [HideInInspector]
    public Vector2 pointerOld;

    private int _pointerId;
    [HideInInspector]
    public bool pressed;

    void Start()
    {
        SettingsManager.AC_TouchSensetivityChanged += SensetivityChanged;
    }

    void OnDisable()
    {
        SettingsManager.AC_TouchSensetivityChanged -= SensetivityChanged;
    }

    // Update is called once per frame
    void Update()
    {
        if (pressed)
        {
            // TOUCH INPUT
            if (_pointerId >= 0 && _pointerId < Input.touchCount)
            {
                Touch touch = Input.touches[_pointerId];
                touchDist = touch.deltaPosition * touchSensitivity;
            }
            // MOUSE INPUT
            else
            {
                
                Vector2 mousePos = Input.mousePosition;
                touchDist = (mousePos - pointerOld) * touchSensitivity;
                pointerOld = mousePos;
            }
        }
        else
        {
            touchDist = Vector2.zero;
        }

        
    }


    public void OnPointerDown(PointerEventData eventData)
    {
        pressed = true;
        _pointerId = eventData.pointerId;
        pointerOld = eventData.position;
    }


    public void OnPointerUp(PointerEventData eventData)
    {
        pressed = false;
    }

    void SensetivityChanged(float value)
    {
        touchSensitivity = value;
    }
}
