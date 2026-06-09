using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

public class UIButtonEvents : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    
    public UnityEvent OnButtonPressed;
    public UnityEvent OnButtonRelease;
  
    private bool isPressed = false;


    public void OnPointerDown(PointerEventData eventData)
    {
      isPressed = true;
      OnButtonPressed?.Invoke();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
      isPressed = false;
      OnButtonRelease?.Invoke();
    }

    void OnDisable()
    {
      if(isPressed)
      {
         isPressed = false;
         OnButtonRelease?.Invoke();
      }  
    }
}
